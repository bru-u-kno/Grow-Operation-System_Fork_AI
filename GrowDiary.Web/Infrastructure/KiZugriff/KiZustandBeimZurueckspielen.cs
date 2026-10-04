using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Infrastructure.KiZugriff;

/// <summary>
/// Fork AI (A-003, Prüfer 03.10.2026): Die Schlüssel reisen nicht mit einer Sicherung zurück.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass.</b> Eine Sicherung ist die ganze Datenbank, also auch
/// <c>ForkKiSchluessel</c> und die Einstellungen <c>ki-zugriff.*</c>. Wer eine
/// ältere Sicherung zurückspielte, holte damit jeden Schlüssel zurück, der
/// inzwischen gelöscht, gesperrt oder heruntergestuft war — auch einen, der
/// gerade deshalb gelöscht wurde, weil er in falsche Hände geraten war.</para>
///
/// <para><b>Die Regel.</b> Der Zugriff für KI-Assistenten ist Sicherheitszustand,
/// kein Datenbestand: nach dem Zurückspielen gilt, was <b>vorher</b> galt. Er wird
/// vor dem Austausch der Datei gelesen und danach in die zurückgespielte
/// Datenbank geschrieben, als Ganzes ersetzt.</para>
///
/// <para><b>Keine feste Spaltenliste</b> (A-005, 03.10.2026). Bis dahin stand
/// hier eine; mit der Spalte <c>Rueckfrage</c> hätte jedes Zurückspielen die
/// Rückfrage aller Schlüssel auf 0 gesetzt. Jetzt wird jede Spalte kopiert, die
/// die Tabelle hat — eine neue Spalte reist von selbst mit. Gehalten von
/// <c>KiZustandBeimZurueckspielenTests</c>.</para>
/// </remarks>
public static class KiZustandBeimZurueckspielen
{
    private const string EinstellungsMuster = "ki-zugriff.%";

    /// <param name="Spalten">Die Spaltennamen der Tabelle, in der Reihenfolge der Werte in <paramref name="Schluessel"/>.</param>
    public sealed record Zustand(
        IReadOnlyList<string> Spalten,
        IReadOnlyList<object?[]> Schluessel,
        IReadOnlyList<(string Key, string? Value)> Einstellungen);

    /// <summary>Den Zugriffs-Zustand der Datenbank an diesem Pfad lesen; fehlt die Tabelle, ist er leer.</summary>
    /// <remarks>
    /// Vor dem Lesen wird das Schema dieser Datei auf den Stand gebracht — sonst
    /// fehlte einer Datenbank, deren Schlüssel seit dem Start niemand angefasst hat,
    /// die Spalte <c>Rueckfrage</c> mit ihrer übernommenen Einstellung.
    /// </remarks>
    public static Zustand Lesen(string datenbankPfad)
    {
        var spalten = new List<string>();
        var schluessel = new List<object?[]>();
        var einstellungen = new List<(string, string?)>();
        if (!File.Exists(datenbankPfad)) return new Zustand(spalten, schluessel, einstellungen);

        using var verbindung = Oeffnen(datenbankPfad);
        if (TabelleDa(verbindung, "ForkKiSchluessel"))
        {
            KiSchluesselRepository.SchemaSicherstellen(verbindung);

            using var befehl = verbindung.CreateCommand();
            befehl.CommandText = "SELECT * FROM ForkKiSchluessel ORDER BY Id;";
            using var leser = befehl.ExecuteReader();
            for (var i = 0; i < leser.FieldCount; i++) spalten.Add(leser.GetName(i));
            while (leser.Read())
            {
                var zeile = new object?[leser.FieldCount];
                for (var i = 0; i < zeile.Length; i++) zeile[i] = leser.IsDBNull(i) ? null : leser.GetValue(i);
                schluessel.Add(zeile);
            }
        }

        if (TabelleDa(verbindung, "AppSettings"))
        {
            using var befehl = verbindung.CreateCommand();
            befehl.CommandText = "SELECT Key, Value FROM AppSettings WHERE Key LIKE $muster;";
            befehl.Parameters.AddWithValue("$muster", EinstellungsMuster);
            using var leser = befehl.ExecuteReader();
            while (leser.Read()) einstellungen.Add((leser.GetString(0), leser.IsDBNull(1) ? null : leser.GetString(1)));
        }

        return new Zustand(spalten, schluessel, einstellungen);
    }

    /// <summary>Den gelesenen Zustand in die Datenbank an diesem Pfad schreiben — ersetzt, was dort steht.</summary>
    public static void Schreiben(string datenbankPfad, Zustand zustand)
    {
        using var verbindung = Oeffnen(datenbankPfad);
        using var transaktion = verbindung.BeginTransaction();

        // Auch die Spalten nachrüsten: eine Sicherung aus forkai.163 hat keine Rueckfrage.
        KiSchluesselRepository.SchemaSicherstellen(verbindung, transaktion);
        Ausfuehren(verbindung, transaktion, "DELETE FROM ForkKiSchluessel;");

        if (zustand.Schluessel.Count > 0)
        {
            // Die Namen stammen aus dem Schema der eigenen Datei, nicht aus einer Eingabe;
            // in Anführungszeichen trotzdem, damit kein Name als Schlüsselwort gelesen wird.
            var namen = string.Join(", ", zustand.Spalten.Select(s => "\"" + s.Replace("\"", "\"\"") + "\""));
            var platzhalter = string.Join(", ", zustand.Spalten.Select((_, i) => "$p" + i));
            foreach (var zeile in zustand.Schluessel)
            {
                using var befehl = verbindung.CreateCommand();
                befehl.Transaction = transaktion;
                befehl.CommandText = $"INSERT INTO ForkKiSchluessel ({namen}) VALUES ({platzhalter});";
                for (var i = 0; i < zustand.Spalten.Count; i++) befehl.Parameters.AddWithValue("$p" + i, zeile[i] ?? DBNull.Value);
                befehl.ExecuteNonQuery();
            }
        }

        if (TabelleDa(verbindung, "AppSettings", transaktion))
        {
            using (var loeschen = verbindung.CreateCommand())
            {
                loeschen.Transaction = transaktion;
                loeschen.CommandText = "DELETE FROM AppSettings WHERE Key LIKE $muster;";
                loeschen.Parameters.AddWithValue("$muster", EinstellungsMuster);
                loeschen.ExecuteNonQuery();
            }

            foreach (var (key, value) in zustand.Einstellungen)
            {
                using var befehl = verbindung.CreateCommand();
                befehl.Transaction = transaktion;
                befehl.CommandText = "INSERT INTO AppSettings (Key, Value) VALUES ($key, $value);";
                befehl.Parameters.AddWithValue("$key", key);
                befehl.Parameters.AddWithValue("$value", (object?)value ?? DBNull.Value);
                befehl.ExecuteNonQuery();
            }
        }

        transaktion.Commit();
    }

    private static SqliteConnection Oeffnen(string pfad)
    {
        // Ohne Pool: zwischen Lesen und Schreiben wird die Datei ausgetauscht.
        var verbindung = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = pfad, Pooling = false }.ToString());
        verbindung.Open();
        return verbindung;
    }

    private static bool TabelleDa(SqliteConnection verbindung, string name, SqliteTransaction? transaktion = null)
    {
        using var befehl = verbindung.CreateCommand();
        befehl.Transaction = transaktion;
        befehl.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        befehl.Parameters.AddWithValue("$name", name);
        return Convert.ToInt64(befehl.ExecuteScalar()) > 0;
    }

    private static void Ausfuehren(SqliteConnection verbindung, SqliteTransaction transaktion, string sql)
    {
        using var befehl = verbindung.CreateCommand();
        befehl.Transaction = transaktion;
        befehl.CommandText = sql;
        befehl.ExecuteNonQuery();
    }
}
