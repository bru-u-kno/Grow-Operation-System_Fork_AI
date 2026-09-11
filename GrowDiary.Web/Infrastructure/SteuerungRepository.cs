using System.Globalization;
using System.Text.Json;
using GrowDiary.Web.Models;
using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Infrastructure;

/// <summary>
/// Fork AI (forkai.20): Einstellungen und Tageswerte der Steuerungen.
/// </summary>
/// <remarks>
/// <para>Die Einstellungen liegen als <b>ein JSON-Dokument je Modul</b> in
/// <c>ForkSteuerungEinstellungen</c> — bewusst nicht als Spaltenliste. Jedes
/// weitere Modul (Entfeuchter, Chiller …) bringt eigene Felder mit, und ein
/// Schema, das bei jedem neuen Feld ein <c>ALTER TABLE</c> braucht, ist genau
/// die Sorte Wartung, die die Kosten-Tabellen schon dreimal gekostet hat.</para>
///
/// <para>Die Tageswerte der CO₂-Begasung bekommen dagegen echte Spalten:
/// darüber wird gerechnet und sortiert.</para>
/// </remarks>
public sealed class SteuerungRepository : RepositoryBase
{
    private static readonly object SchemaLock = new();
    private static bool _schemaEnsured;
    private static readonly JsonSerializerOptions JsonOptionen = new(JsonSerializerDefaults.Web);

    public SteuerungRepository(AppPaths paths) : base(paths)
    {
    }

    private SqliteConnection Open()
    {
        var connection = OpenConnection();
        EnsureSchema(connection);
        return connection;
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        if (_schemaEnsured) return;
        lock (SchemaLock)
        {
            if (_schemaEnsured) return;
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS ForkSteuerungEinstellungen (
                    Modul TEXT PRIMARY KEY,
                    Json TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS ForkCo2Tage (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Datum TEXT NOT NULL UNIQUE,
                    GrowId INTEGER NULL,
                    Impulse INTEGER NOT NULL DEFAULT 0,
                    VentilSekunden REAL NOT NULL DEFAULT 0,
                    Gramm REAL NOT NULL DEFAULT 0,
                    ZielErreichtUm TEXT NULL,
                    FlascheStartKg REAL NOT NULL DEFAULT 0,
                    FlascheEndeKg REAL NULL,
                    GrammVorher REAL NOT NULL DEFAULT 0,
                    Flaschenwechsel INTEGER NOT NULL DEFAULT 0,
                    Abgeschlossen INTEGER NOT NULL DEFAULT 0,
                    JournalEntryId INTEGER NULL,
                    VerbrauchId INTEGER NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    AbgeschlossenUtc TEXT NULL
                );
                """;
            command.ExecuteNonQuery();

            // Fork AI (forkai.20): nachgereichte Spalten. ALTER TABLE ADD COLUMN
            // ist der einzige Weg, ohne die Tabelle neu zu bauen; auf einer
            // frischen Datenbank sind sie schon da, deshalb der geschluckte
            // Fehler statt einer Abfrage auf PRAGMA table_info.
            foreach (var spalte in new[]
                     {
                         "ALTER TABLE ForkCo2Tage ADD COLUMN GrammVorher REAL NOT NULL DEFAULT 0;",
                         "ALTER TABLE ForkCo2Tage ADD COLUMN Flaschenwechsel INTEGER NOT NULL DEFAULT 0;",
                     })
            {
                try
                {
                    using var nachtrag = connection.CreateCommand();
                    nachtrag.CommandText = spalte;
                    nachtrag.ExecuteNonQuery();
                }
                catch (SqliteException)
                {
                    // Spalte existiert bereits.
                }
            }
            _schemaEnsured = true;
        }
    }

    // ----------------------------------------------------------- Einstellungen

    public T? GetEinstellungen<T>(string modul) where T : class
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Json FROM ForkSteuerungEinstellungen WHERE Modul = $modul;";
        command.Parameters.AddWithValue("$modul", modul);
        var json = command.ExecuteScalar() as string;
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptionen);
        }
        catch (JsonException)
        {
            // Ein kaputtes Dokument darf die Seite nicht mitreißen — dann
            // gelten die Vorgaben, und der nächste Speichervorgang heilt es.
            return null;
        }
    }

    public void SetEinstellungen<T>(string modul, T einstellungen) where T : class
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ForkSteuerungEinstellungen (Modul, Json, UpdatedAtUtc)
            VALUES ($modul, $json, $updated)
            ON CONFLICT(Modul) DO UPDATE SET Json = excluded.Json, UpdatedAtUtc = excluded.UpdatedAtUtc;
            """;
        command.Parameters.AddWithValue("$modul", modul);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(einstellungen, JsonOptionen));
        command.Parameters.AddWithValue("$updated", ToStorageUtc(DateTime.UtcNow));
        command.ExecuteNonQuery();
    }

    // ---------------------------------------------------------------- CO₂-Tage

    public Co2Tag? GetCo2Tag(string datum)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM ForkCo2Tage WHERE Datum = $datum;";
        command.Parameters.AddWithValue("$datum", datum);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapTag(reader) : null;
    }

    public List<Co2Tag> GetCo2Tage(int anzahl)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM ForkCo2Tage ORDER BY Datum DESC LIMIT $n;";
        command.Parameters.AddWithValue("$n", anzahl);
        using var reader = command.ExecuteReader();
        var list = new List<Co2Tag>();
        while (reader.Read()) list.Add(MapTag(reader));
        return list;
    }

    /// <summary>Der jüngste offene Tag — der, den ein Abschluss noch einholen muss.</summary>
    public Co2Tag? GetOffenerCo2Tag()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM ForkCo2Tage WHERE Abgeschlossen = 0 ORDER BY Datum DESC LIMIT 1;";
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapTag(reader) : null;
    }

    public int CreateCo2Tag(Co2Tag tag)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ForkCo2Tage (Datum, GrowId, Impulse, VentilSekunden, Gramm, ZielErreichtUm, FlascheStartKg, FlascheEndeKg, GrammVorher, Flaschenwechsel, Abgeschlossen, JournalEntryId, VerbrauchId, CreatedAtUtc, AbgeschlossenUtc)
            VALUES ($datum, $growId, $impulse, $ventil, $gramm, $ziel, $start, $ende, $grammVorher, $wechsel, $abg, $journal, $verbrauch, $created, $abgUtc);
            SELECT last_insert_rowid();
            """;
        Bind(command, tag);
        command.Parameters.AddWithValue("$created", ToStorageUtc(DateTime.UtcNow));
        return Convert.ToInt32((long)command.ExecuteScalar()!);
    }

    public void UpdateCo2Tag(Co2Tag tag)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ForkCo2Tage SET
                GrowId = $growId, Impulse = $impulse, VentilSekunden = $ventil, Gramm = $gramm,
                ZielErreichtUm = $ziel, FlascheStartKg = $start, FlascheEndeKg = $ende,
                GrammVorher = $grammVorher, Flaschenwechsel = $wechsel,
                Abgeschlossen = $abg, JournalEntryId = $journal, VerbrauchId = $verbrauch, AbgeschlossenUtc = $abgUtc
            WHERE Id = $id;
            """;
        Bind(command, tag);
        command.Parameters.AddWithValue("$id", tag.Id);
        command.ExecuteNonQuery();
    }

    private static void Bind(SqliteCommand command, Co2Tag t)
    {
        command.Parameters.AddWithValue("$datum", t.Datum);
        command.Parameters.AddWithValue("$growId", (object?)t.GrowId ?? DBNull.Value);
        command.Parameters.AddWithValue("$impulse", t.Impulse);
        command.Parameters.AddWithValue("$ventil", t.VentilSekunden);
        command.Parameters.AddWithValue("$gramm", t.Gramm);
        command.Parameters.AddWithValue("$ziel", (object?)t.ZielErreichtUm ?? DBNull.Value);
        command.Parameters.AddWithValue("$start", t.FlascheStartKg);
        command.Parameters.AddWithValue("$ende", (object?)t.FlascheEndeKg ?? DBNull.Value);
        command.Parameters.AddWithValue("$grammVorher", t.GrammVorher);
        command.Parameters.AddWithValue("$wechsel", t.Flaschenwechsel ? 1 : 0);
        command.Parameters.AddWithValue("$abg", t.Abgeschlossen ? 1 : 0);
        command.Parameters.AddWithValue("$journal", (object?)t.JournalEntryId ?? DBNull.Value);
        command.Parameters.AddWithValue("$verbrauch", (object?)t.VerbrauchId ?? DBNull.Value);
        command.Parameters.AddWithValue("$abgUtc", t.AbgeschlossenUtc is { } a ? ToStorageUtc(a) : DBNull.Value);
    }

    private static Co2Tag MapTag(SqliteDataReader reader) => new()
    {
        Id = Convert.ToInt32(reader["Id"], CultureInfo.InvariantCulture),
        Datum = reader["Datum"].ToString() ?? string.Empty,
        GrowId = reader["GrowId"] is DBNull ? null : Convert.ToInt32(reader["GrowId"], CultureInfo.InvariantCulture),
        Impulse = Convert.ToInt32(reader["Impulse"], CultureInfo.InvariantCulture),
        VentilSekunden = Convert.ToDouble(reader["VentilSekunden"], CultureInfo.InvariantCulture),
        Gramm = Convert.ToDouble(reader["Gramm"], CultureInfo.InvariantCulture),
        ZielErreichtUm = NullString(reader["ZielErreichtUm"]),
        FlascheStartKg = Convert.ToDouble(reader["FlascheStartKg"], CultureInfo.InvariantCulture),
        FlascheEndeKg = NullableDouble(reader["FlascheEndeKg"]),
        GrammVorher = Convert.ToDouble(reader["GrammVorher"], CultureInfo.InvariantCulture),
        Flaschenwechsel = Convert.ToInt32(reader["Flaschenwechsel"], CultureInfo.InvariantCulture) == 1,
        Abgeschlossen = Convert.ToInt32(reader["Abgeschlossen"], CultureInfo.InvariantCulture) == 1,
        JournalEntryId = reader["JournalEntryId"] is DBNull ? null : Convert.ToInt32(reader["JournalEntryId"], CultureInfo.InvariantCulture),
        VerbrauchId = reader["VerbrauchId"] is DBNull ? null : Convert.ToInt32(reader["VerbrauchId"], CultureInfo.InvariantCulture),
        CreatedAtUtc = ParseStoredUtcDateTime(reader["CreatedAtUtc"].ToString()) ?? DateTime.UtcNow,
        AbgeschlossenUtc = ParseStoredUtcDateTime(NullString(reader["AbgeschlossenUtc"])),
    };
}
