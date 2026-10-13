using System.Globalization;
using GrowDiary.Web.Models;
using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Infrastructure;

/// <summary>
/// Der Wasserwechsel-Vorgang (A-006): anlegen und löschen als Ganzes.
/// </summary>
/// <remarks>
/// <para><b>Eine Transaktion.</b> Wechsel, Messungen, Buchungen und
/// Tagebuchzeile entstehen zusammen oder gar nicht. Bis zum 05.10.2026 lief das
/// im Messformular als zwei Aufrufe — die Messung war schon gespeichert, wenn
/// die Buchung scheiterte („Die Messung ist gespeichert, die Zugaben konnten
/// nicht gebucht werden"). Ein halber Vorgang sieht aus wie ein ganzer.</para>
///
/// <para><b>Dieselben INSERTs wie überall.</b> Die Sätze legen die Methoden der
/// zuständigen Repositories an (Überladungen mit Verbindung und Transaktion),
/// nicht eine zweite Spaltenliste hier. Sonst fehlt nach dem nächsten neuen
/// Messfeld genau hier eine Spalte.</para>
///
/// <para><b>Eigene Tabelle</b> wie die übrigen Fork-Tabellen
/// (<see cref="KostenRepository"/>): angelegt über <see cref="EigenesSchema"/>,
/// nicht im Kern-Schema — das hält den Abgleich mit dem Original frei.</para>
/// </remarks>
public sealed class WasserwechselVorgangRepository : RepositoryBase
{
    /// <summary>So heißt der Artikel für Leitungswasser — derselbe Name wie in Nutzer-Anlage (Artikel 10).</summary>
    public const string LeitungswasserArtikel = "Leitungswasser";

    /// <summary>So heißt der Artikel für Osmosewasser, der beim ersten Vorgang mit Osmose angelegt wird.</summary>
    public const string OsmosewasserArtikel = "Osmosewasser";

    private readonly KostenRepository _kosten;

    public WasserwechselVorgangRepository(AppPaths paths, KostenRepository kosten) : base(paths)
    {
        _kosten = kosten;
    }

    private SqliteConnection Open()
    {
        // Die Buchungen brauchen die Spalte VorgangId — die zieht das
        // Kosten-Repository nach, nicht wir.
        _kosten.SchemaSicherstellen();
        var connection = OpenConnection();
        EigenesSchema.Sicherstellen(nameof(WasserwechselVorgangRepository), Paths.DatabasePath, () => SchemaAnlegen(connection));
        return connection;
    }

    private static void SchemaAnlegen(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        // Die Verweise mit ON DELETE SET NULL: löscht jemand die Messung an
        // ihrer eigenen Stelle, bleibt der Vorgang mit dem Rest lesbar.
        // Am Grow CASCADE — ein gelöschter Grow nimmt seine Wechsel und
        // Messungen ohnehin mit, die Klammer gehört dazu.
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ForkWasserwechselVorgaenge (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                GrowId INTEGER NOT NULL REFERENCES Grows(Id) ON DELETE CASCADE,
                ChangeoutId INTEGER NULL REFERENCES ChangeoutEntries(Id) ON DELETE SET NULL,
                MessungVorherId INTEGER NULL REFERENCES Measurements(Id) ON DELETE SET NULL,
                MessungNachherId INTEGER NULL REFERENCES Measurements(Id) ON DELETE SET NULL,
                JournalId INTEGER NULL REFERENCES JournalEntries(Id) ON DELETE SET NULL,
                OsmoseProzent REAL NULL,
                VorherHerkunft TEXT NULL,
                VorherSensorZeitUtc TEXT NULL,
                ErstelltAmUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_ForkWasserwechselVorgaenge_Grow ON ForkWasserwechselVorgaenge(GrowId);
            CREATE INDEX IF NOT EXISTS IX_ForkWasserwechselVorgaenge_Changeout ON ForkWasserwechselVorgaenge(ChangeoutId);
            CREATE INDEX IF NOT EXISTS IX_ForkVerbraeuche_Vorgang ON ForkVerbraeuche(VorgangId);
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>Legt den ganzen Vorgang an — alles oder nichts.</summary>
    /// <returns>Der Vorgang mit den Kennungen der angelegten Sätze.</returns>
    public WasserwechselVorgang Anlegen(WasserwechselVorgangEntwurf entwurf)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        var wechsel = AddbackRepository.CreateChangeout(entwurf.Wechsel, connection, transaction);

        var vorherId = VorgangBausteine.MessungAnlegen(entwurf.Vorher, connection, transaction);
        var nachherId = VorgangBausteine.MessungAnlegen(entwurf.Nachher, connection, transaction);

        var vorgang = new WasserwechselVorgang
        {
            GrowId = entwurf.GrowId,
            ChangeoutId = wechsel.Id,
            MessungVorherId = vorherId,
            MessungNachherId = nachherId,
            OsmoseProzent = entwurf.OsmoseProzent,
            VorherHerkunft = entwurf.VorherHerkunft,
            VorherSensorZeitUtc = entwurf.VorherSensorZeitUtc,
            ErstelltAmUtc = DateTime.UtcNow,
        };

        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO ForkWasserwechselVorgaenge
                    (GrowId, ChangeoutId, MessungVorherId, MessungNachherId, JournalId, OsmoseProzent, VorherHerkunft, VorherSensorZeitUtc, ErstelltAmUtc)
                VALUES ($growId, $changeoutId, $vorherId, $nachherId, NULL, $osmose, $herkunft, $sensorZeit, $erstellt);
                SELECT last_insert_rowid();
                """;
            insert.Parameters.AddWithValue("$growId", vorgang.GrowId);
            insert.Parameters.AddWithValue("$changeoutId", (object?)vorgang.ChangeoutId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$vorherId", (object?)vorgang.MessungVorherId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$nachherId", (object?)vorgang.MessungNachherId ?? DBNull.Value);
            AddNullable(insert, "$osmose", vorgang.OsmoseProzent);
            insert.Parameters.AddWithValue("$herkunft", (object?)vorgang.VorherHerkunft ?? DBNull.Value);
            insert.Parameters.AddWithValue("$sensorZeit", vorgang.VorherSensorZeitUtc is { } z ? ToStorageUtc(z) : DBNull.Value);
            insert.Parameters.AddWithValue("$erstellt", ToStorageUtc(vorgang.ErstelltAmUtc));
            vorgang.Id = Convert.ToInt32((long)insert.ExecuteScalar()!, CultureInfo.InvariantCulture);
        }

        VorgangBausteine.Buchen(entwurf.Buchungen, entwurf.GrowId, nachherId, wechsel.PerformedAtUtc, "wasserwechsel",
            v => v.VorgangId = vorgang.Id, connection, transaction);

        if (VorgangBausteine.TagebuchAnlegen(entwurf.Tagebuch, nachherId, connection, transaction) is { } journalId)
        {
            vorgang.JournalId = journalId;
            VorgangBausteine.Ausfuehren(connection, transaction, "UPDATE ForkWasserwechselVorgaenge SET JournalId = $journalId WHERE Id = $id;",
                ("$journalId", journalId), ("$id", vorgang.Id));
        }

        transaction.Commit();
        return vorgang;
    }

    /// <summary>
    /// Löscht den Vorgang samt Wechsel, Messungen, Buchungen und Tagebuchzeile.
    /// </summary>
    /// <returns><c>false</c>, wenn es den Vorgang in diesem Grow nicht gibt.</returns>
    public bool Loeschen(int growId, int vorgangId)
    {
        List<string> fotos;
        using (var connection = Open())
        using (var transaction = connection.BeginTransaction())
        {
            var vorgang = Lesen(connection, transaction, "WHERE Id = $id AND GrowId = $growId",
                c => { c.Parameters.AddWithValue("$id", vorgangId); c.Parameters.AddWithValue("$growId", growId); })
                .FirstOrDefault();
            if (vorgang is null) return false;

            VorgangBausteine.Ausfuehren(connection, transaction, "DELETE FROM ForkVerbraeuche WHERE VorgangId = $id;", ("$id", vorgang.Id));

            fotos = VorgangBausteine.TagebuchUndMessungenLoeschen(growId, vorgang.JournalId,
                [vorgang.MessungVorherId, vorgang.MessungNachherId], connection, transaction);

            if (vorgang.ChangeoutId is { } changeoutId)
            {
                VorgangBausteine.Ausfuehren(connection, transaction, "DELETE FROM ChangeoutEntries WHERE Id = $id AND GrowId = $growId;", ("$id", changeoutId), ("$growId", growId));
            }

            VorgangBausteine.Ausfuehren(connection, transaction, "DELETE FROM ForkWasserwechselVorgaenge WHERE Id = $id;", ("$id", vorgang.Id));
            transaction.Commit();
        }

        foreach (var pfad in fotos)
        {
            if (TryResolveUploadPath(pfad, out var datei) && File.Exists(datei)) File.Delete(datei);
        }

        return true;
    }

    /// <summary>Alle Vorgänge eines Grows, neueste zuerst.</summary>
    public List<WasserwechselVorgang> FuerGrow(int growId)
    {
        using var connection = Open();
        return Lesen(connection, null, "WHERE GrowId = $growId ORDER BY ErstelltAmUtc DESC, Id DESC",
            c => c.Parameters.AddWithValue("$growId", growId));
    }

    /// <summary>Ein Vorgang dieses Grows, oder <c>null</c>.</summary>
    public WasserwechselVorgang? Get(int growId, int vorgangId)
    {
        using var connection = Open();
        return Lesen(connection, null, "WHERE Id = $id AND GrowId = $growId",
            c => { c.Parameters.AddWithValue("$id", vorgangId); c.Parameters.AddWithValue("$growId", growId); })
            .FirstOrDefault();
    }

    /// <summary>Der Vorgang, zu dem ein Wechsel gehört — oder <c>null</c> bei Altdaten.</summary>
    public WasserwechselVorgang? ZumWechsel(int growId, int changeoutId)
    {
        using var connection = Open();
        return Lesen(connection, null, "WHERE ChangeoutId = $changeoutId AND GrowId = $growId",
            c => { c.Parameters.AddWithValue("$changeoutId", changeoutId); c.Parameters.AddWithValue("$growId", growId); })
            .FirstOrDefault();
    }

    /// <summary>Die Buchungen eines Vorgangs.</summary>
    public List<Verbrauch> Buchungen(int vorgangId)
        => _kosten.GetVerbraeuche().Where(v => v.VorgangId == vorgangId).OrderBy(v => v.Id).ToList();

    private static List<WasserwechselVorgang> Lesen(SqliteConnection connection, SqliteTransaction? transaction, string bedingung, Action<SqliteCommand> parameter)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT * FROM ForkWasserwechselVorgaenge {bedingung};";
        parameter(command);
        using var reader = command.ExecuteReader();
        var liste = new List<WasserwechselVorgang>();
        while (reader.Read())
        {
            liste.Add(new WasserwechselVorgang
            {
                Id = Convert.ToInt32(reader["Id"], CultureInfo.InvariantCulture),
                GrowId = Convert.ToInt32(reader["GrowId"], CultureInfo.InvariantCulture),
                ChangeoutId = VorgangBausteine.NullInt(reader["ChangeoutId"]),
                MessungVorherId = VorgangBausteine.NullInt(reader["MessungVorherId"]),
                MessungNachherId = VorgangBausteine.NullInt(reader["MessungNachherId"]),
                JournalId = VorgangBausteine.NullInt(reader["JournalId"]),
                OsmoseProzent = NullableDouble(reader["OsmoseProzent"]),
                VorherHerkunft = NullString(reader["VorherHerkunft"]),
                VorherSensorZeitUtc = ParseStoredUtcDateTime(NullString(reader["VorherSensorZeitUtc"])),
                ErstelltAmUtc = ParseStoredUtcDateTime(reader["ErstelltAmUtc"]?.ToString()) ?? DateTime.UtcNow,
            });
        }

        return liste;
    }
}
