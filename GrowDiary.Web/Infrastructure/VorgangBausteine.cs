using System.Globalization;
using GrowDiary.Web.Models;
using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Infrastructure;

/// <summary>
/// Was jeder Vorgang (A-006) gleich macht — Wasserwechsel und Nachfüllen.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass.</b> Etappe 1 baute den Wasserwechsel als Vorgang;
/// Etappe 3 (Nachfüllen) braucht dieselben Schritte: Messungen anlegen, Wasser
/// und Zugaben buchen (den Wasser-Artikel beim ersten Mal anlegen),
/// Tagebuchzeile schreiben — und beim Löschen dieselben Teile wieder abräumen.
/// Zweimal abgetippt liefe das auseinander, sobald einer der beiden einen
/// Fehler repariert bekommt. Deshalb steht es hier, einmal.</para>
///
/// <para>Alles auf der Verbindung und Transaktion des Aufrufers: der Vorgang
/// entsteht ganz oder gar nicht.</para>
/// </remarks>
internal static class VorgangBausteine
{
    /// <summary>Legt die Messung an, falls es eine gibt, und gibt ihre Id zurück.</summary>
    public static int? MessungAnlegen(Measurement? messung, SqliteConnection connection, SqliteTransaction transaction)
    {
        if (messung is null) return null;
        messung.Id = MeasurementRepository.CreateMeasurement(messung, connection, transaction);
        return messung.Id;
    }

    /// <summary>
    /// Bucht Wasser und Zugaben — jede Buchung mit dem Verweis, den
    /// <paramref name="verknuepfen"/> setzt (Wasserwechsel- oder Nachfüll-Vorgang).
    /// </summary>
    /// <remarks>Vorher <see cref="KostenRepository.SchemaSicherstellen"/>, sonst fehlen womöglich die Vorgang-Spalten.</remarks>
    public static void Buchen(
        IEnumerable<VorgangBuchungEntwurf> buchungen,
        int growId,
        int? messungId,
        DateTime zeitpunktUtc,
        string quelle,
        Action<Verbrauch> verknuepfen,
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        foreach (var buchung in buchungen)
        {
            var artikelId = buchung.ArtikelId
                ?? WasserArtikelFindenOderAnlegen(buchung.WasserArtikelName!, connection, transaction);
            var verbrauch = new Verbrauch
            {
                ArtikelId = artikelId,
                GrowId = growId,
                MessungId = messungId,
                ZeitpunktUtc = zeitpunktUtc,
                Menge = buchung.Menge,
                Quelle = quelle,
            };
            verknuepfen(verbrauch);
            KostenRepository.CreateVerbrauch(verbrauch, connection, transaction);
        }
    }

    /// <summary>Schreibt die Tagebuchzeile (an der Messung „nachher") und gibt ihre Id zurück.</summary>
    public static int? TagebuchAnlegen(JournalEntry? tagebuch, int? messungNachherId, SqliteConnection connection, SqliteTransaction transaction)
    {
        if (tagebuch is null) return null;
        tagebuch.MeasurementId = messungNachherId;
        tagebuch.Id = JournalRepository.Create(tagebuch, connection, transaction);
        return tagebuch.Id;
    }

    /// <summary>
    /// Der Artikel für das Wasser — gefunden über den Namen, beim ersten Mal angelegt.
    /// </summary>
    /// <remarks>
    /// Bru hat „Leitungswasser" schon als Artikel (Einheit L). „Osmosewasser"
    /// gibt es noch nicht; Bru hat entschieden, dass es beim ersten Vorgang
    /// angelegt wird. Ohne Preis: die Kosten je Liter trägt er im Artikel nach.
    /// Verglichen wird ohne Groß/Klein und ohne Leerzeichen am Rand — wie
    /// <see cref="Stammdaten.Angleichen"/>.
    /// </remarks>
    public static int WasserArtikelFindenOderAnlegen(string name, SqliteConnection connection, SqliteTransaction transaction)
    {
        using (var suche = connection.CreateCommand())
        {
            suche.Transaction = transaction;
            suche.CommandText = "SELECT Id, Name FROM ForkVerbrauchsartikel ORDER BY Aktiv DESC, Id;";
            using var reader = suche.ExecuteReader();
            while (reader.Read())
            {
                var vorhanden = reader["Name"]?.ToString() ?? string.Empty;
                if (string.Equals(Stammdaten.Normalisieren(vorhanden), Stammdaten.Normalisieren(name), StringComparison.OrdinalIgnoreCase))
                {
                    return Convert.ToInt32(reader["Id"], CultureInfo.InvariantCulture);
                }
            }
        }

        return KostenRepository.CreateArtikel(new Verbrauchsartikel
        {
            Name = name,
            Einheit = "L",
            Aktiv = true,
            // Wasser geht in einem Lauf auf — es zählt, was gebucht wird.
            AufGrowBuchen = true,
            Notiz = "Angelegt beim ersten Wasserwechsel bzw. Nachfüllen. Preis je Liter hier nachtragen, dann rechnet die Kostenseite das Wasser mit.",
        }, connection, transaction);
    }

    /// <summary>
    /// Löscht Tagebuchzeile und Messungen eines Vorgangs — nur in seinem Grow.
    /// </summary>
    /// <returns>Die Foto-Dateien der Messungen; der Aufrufer räumt sie nach dem Commit ab.</returns>
    public static List<string> TagebuchUndMessungenLoeschen(
        int growId, int? journalId, IEnumerable<int?> messungen, SqliteConnection connection, SqliteTransaction transaction)
    {
        if (journalId is { } jid)
        {
            Ausfuehren(connection, transaction, "DELETE FROM JournalEntries WHERE Id = $id AND GrowId = $growId;", ("$id", jid), ("$growId", growId));
        }

        var fotos = new List<string>();
        foreach (var messungId in messungen.OfType<int>())
        {
            // Fotos hängen per CASCADE an der Messung — die Zeilen gehen mit,
            // die Dateien räumen wir nach dem Commit ab (wie DeleteMeasurement).
            using (var fotoSuche = connection.CreateCommand())
            {
                fotoSuche.Transaction = transaction;
                fotoSuche.CommandText = "SELECT RelativePath FROM Photos WHERE MeasurementId = $id;";
                fotoSuche.Parameters.AddWithValue("$id", messungId);
                using var reader = fotoSuche.ExecuteReader();
                while (reader.Read())
                {
                    if (reader["RelativePath"]?.ToString() is { Length: > 0 } pfad) fotos.Add(pfad);
                }
            }

            Ausfuehren(connection, transaction, "DELETE FROM Measurements WHERE Id = $id AND GrowId = $growId;", ("$id", messungId), ("$growId", growId));
        }

        return fotos;
    }

    public static int? NullInt(object? wert)
        => wert is DBNull or null ? null : Convert.ToInt32(wert, CultureInfo.InvariantCulture);

    public static void Ausfuehren(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object Wert)[] parameter)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, wert) in parameter) command.Parameters.AddWithValue(name, wert);
        command.ExecuteNonQuery();
    }
}
