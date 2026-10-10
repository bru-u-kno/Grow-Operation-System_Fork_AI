using System.Globalization;
using GrowDiary.Web.Models;
using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Infrastructure;

/// <summary>
/// Der Nachfüll-Vorgang (A-006, Etappe 3): anlegen und löschen als Ganzes.
/// </summary>
/// <remarks>
/// <para>Dasselbe Muster wie <see cref="WasserwechselVorgangRepository"/>, und
/// die gemeinsamen Schritte (Messungen, Buchungen, Wasser-Artikel, Tagebuch,
/// Abräumen) stehen in <see cref="VorgangBausteine"/> — nicht ein zweites Mal hier.</para>
///
/// <para><b>Eine Transaktion.</b> Eintrag, Messungen, Buchungen und
/// Tagebuchzeile entstehen zusammen oder gar nicht.</para>
///
/// <para><b>Eigene Tabelle</b> wie die übrigen Fork-Tabellen, angelegt über
/// <see cref="EigenesSchema"/> — das hält den Abgleich mit dem Original frei.</para>
/// </remarks>
public sealed class AddbackVorgangRepository : RepositoryBase
{
    private readonly KostenRepository _kosten;

    public AddbackVorgangRepository(AppPaths paths, KostenRepository kosten) : base(paths)
    {
        _kosten = kosten;
    }

    private SqliteConnection Open()
    {
        // Die Buchungen brauchen die Spalte AddbackVorgangId — die zieht das
        // Kosten-Repository nach, nicht wir.
        _kosten.SchemaSicherstellen();
        var connection = OpenConnection();
        EigenesSchema.Sicherstellen(nameof(AddbackVorgangRepository), Paths.DatabasePath, () => SchemaAnlegen(connection));
        return connection;
    }

    private static void SchemaAnlegen(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        // Wie beim Wasserwechsel: Verweise mit ON DELETE SET NULL, am Grow CASCADE.
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ForkAddbackVorgaenge (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                GrowId INTEGER NOT NULL REFERENCES Grows(Id) ON DELETE CASCADE,
                AddbackLogId INTEGER NULL REFERENCES AddbackLogs(Id) ON DELETE SET NULL,
                MessungVorherId INTEGER NULL REFERENCES Measurements(Id) ON DELETE SET NULL,
                MessungNachherId INTEGER NULL REFERENCES Measurements(Id) ON DELETE SET NULL,
                JournalId INTEGER NULL REFERENCES JournalEntries(Id) ON DELETE SET NULL,
                OsmoseProzent REAL NULL,
                VorherHerkunft TEXT NULL,
                VorherSensorZeitUtc TEXT NULL,
                ErstelltAmUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_ForkAddbackVorgaenge_Grow ON ForkAddbackVorgaenge(GrowId);
            CREATE INDEX IF NOT EXISTS IX_ForkAddbackVorgaenge_Log ON ForkAddbackVorgaenge(AddbackLogId);
            CREATE INDEX IF NOT EXISTS IX_ForkVerbraeuche_AddbackVorgang ON ForkVerbraeuche(AddbackVorgangId);
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>Legt den ganzen Vorgang an — alles oder nichts.</summary>
    public AddbackVorgang Anlegen(AddbackVorgangEntwurf entwurf)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        var eintrag = AddbackRepository.CreateAddbackLog(entwurf.Eintrag, connection, transaction);
        var vorherId = VorgangBausteine.MessungAnlegen(entwurf.Vorher, connection, transaction);
        var nachherId = VorgangBausteine.MessungAnlegen(entwurf.Nachher, connection, transaction);

        var vorgang = new AddbackVorgang
        {
            GrowId = entwurf.GrowId,
            AddbackLogId = eintrag.Id,
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
                INSERT INTO ForkAddbackVorgaenge
                    (GrowId, AddbackLogId, MessungVorherId, MessungNachherId, JournalId, OsmoseProzent, VorherHerkunft, VorherSensorZeitUtc, ErstelltAmUtc)
                VALUES ($growId, $logId, $vorherId, $nachherId, NULL, $osmose, $herkunft, $sensorZeit, $erstellt);
                SELECT last_insert_rowid();
                """;
            insert.Parameters.AddWithValue("$growId", vorgang.GrowId);
            insert.Parameters.AddWithValue("$logId", (object?)vorgang.AddbackLogId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$vorherId", (object?)vorgang.MessungVorherId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$nachherId", (object?)vorgang.MessungNachherId ?? DBNull.Value);
            AddNullable(insert, "$osmose", vorgang.OsmoseProzent);
            insert.Parameters.AddWithValue("$herkunft", (object?)vorgang.VorherHerkunft ?? DBNull.Value);
            insert.Parameters.AddWithValue("$sensorZeit", vorgang.VorherSensorZeitUtc is { } z ? ToStorageUtc(z) : DBNull.Value);
            insert.Parameters.AddWithValue("$erstellt", ToStorageUtc(vorgang.ErstelltAmUtc));
            vorgang.Id = Convert.ToInt32((long)insert.ExecuteScalar()!, CultureInfo.InvariantCulture);
        }

        VorgangBausteine.Buchen(entwurf.Buchungen, entwurf.GrowId, nachherId, eintrag.PerformedAtUtc, "addback",
            v => v.AddbackVorgangId = vorgang.Id, connection, transaction);

        if (VorgangBausteine.TagebuchAnlegen(entwurf.Tagebuch, nachherId, connection, transaction) is { } journalId)
        {
            vorgang.JournalId = journalId;
            VorgangBausteine.Ausfuehren(connection, transaction, "UPDATE ForkAddbackVorgaenge SET JournalId = $journalId WHERE Id = $id;",
                ("$journalId", journalId), ("$id", vorgang.Id));
        }

        transaction.Commit();
        return vorgang;
    }

    /// <summary>
    /// Löscht den Vorgang samt Eintrag, Messungen, Buchungen und Tagebuchzeile.
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

            VorgangBausteine.Ausfuehren(connection, transaction, "DELETE FROM ForkVerbraeuche WHERE AddbackVorgangId = $id;", ("$id", vorgang.Id));
            fotos = VorgangBausteine.TagebuchUndMessungenLoeschen(growId, vorgang.JournalId,
                [vorgang.MessungVorherId, vorgang.MessungNachherId], connection, transaction);

            if (vorgang.AddbackLogId is { } logId)
            {
                VorgangBausteine.Ausfuehren(connection, transaction, "DELETE FROM AddbackLogs WHERE Id = $id AND GrowId = $growId;", ("$id", logId), ("$growId", growId));
            }

            VorgangBausteine.Ausfuehren(connection, transaction, "DELETE FROM ForkAddbackVorgaenge WHERE Id = $id;", ("$id", vorgang.Id));
            transaction.Commit();
        }

        foreach (var pfad in fotos)
        {
            if (TryResolveUploadPath(pfad, out var datei) && File.Exists(datei)) File.Delete(datei);
        }

        return true;
    }

    /// <summary>Alle Vorgänge eines Grows, neueste zuerst.</summary>
    public List<AddbackVorgang> FuerGrow(int growId)
    {
        using var connection = Open();
        return Lesen(connection, null, "WHERE GrowId = $growId ORDER BY ErstelltAmUtc DESC, Id DESC",
            c => c.Parameters.AddWithValue("$growId", growId));
    }

    /// <summary>Ein Vorgang dieses Grows, oder <c>null</c>.</summary>
    public AddbackVorgang? Get(int growId, int vorgangId)
    {
        using var connection = Open();
        return Lesen(connection, null, "WHERE Id = $id AND GrowId = $growId",
            c => { c.Parameters.AddWithValue("$id", vorgangId); c.Parameters.AddWithValue("$growId", growId); })
            .FirstOrDefault();
    }

    /// <summary>Der Vorgang, zu dem ein Addback-Eintrag gehört — oder <c>null</c> bei Altdaten.</summary>
    public AddbackVorgang? ZumEintrag(int growId, int addbackLogId)
    {
        using var connection = Open();
        return Lesen(connection, null, "WHERE AddbackLogId = $logId AND GrowId = $growId",
            c => { c.Parameters.AddWithValue("$logId", addbackLogId); c.Parameters.AddWithValue("$growId", growId); })
            .FirstOrDefault();
    }

    /// <summary>Die Buchungen eines Vorgangs.</summary>
    public List<Verbrauch> Buchungen(int vorgangId)
        => _kosten.GetVerbraeuche().Where(v => v.AddbackVorgangId == vorgangId).OrderBy(v => v.Id).ToList();

    private static List<AddbackVorgang> Lesen(SqliteConnection connection, SqliteTransaction? transaction, string bedingung, Action<SqliteCommand> parameter)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT * FROM ForkAddbackVorgaenge {bedingung};";
        parameter(command);
        using var reader = command.ExecuteReader();
        var liste = new List<AddbackVorgang>();
        while (reader.Read())
        {
            liste.Add(new AddbackVorgang
            {
                Id = Convert.ToInt32(reader["Id"], CultureInfo.InvariantCulture),
                GrowId = Convert.ToInt32(reader["GrowId"], CultureInfo.InvariantCulture),
                AddbackLogId = VorgangBausteine.NullInt(reader["AddbackLogId"]),
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
