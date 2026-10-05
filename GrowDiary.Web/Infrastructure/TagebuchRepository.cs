using GrowDiary.Web.Models;
using GrowDiary.Web.Services.Tagebuch;
using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Infrastructure;

/// <summary>
/// Die gemerkten Sprünge im Sensorverlauf (A-006) — Tabelle <c>ForkTagebuchAuffaelligkeiten</c>.
/// </summary>
/// <remarks>
/// Eigenes Schema wie <see cref="KostenRepository"/>: die Tabelle entsteht beim
/// ersten Zugriff, je Datenbankdatei (<see cref="EigenesSchema"/>). Eine Sicherung
/// aus einer Fassung ohne sie wird damit beim nächsten Öffnen nachgerüstet.
/// Eindeutig ist ein Sprung über Zelt, Messgröße und Beginn: dieselben Rohwerte
/// liefern denselben Beginn, ein zweites Erkennen legt also nichts doppelt an.
/// </remarks>
public sealed class TagebuchRepository : RepositoryBase
{
    public TagebuchRepository(AppPaths paths) : base(paths)
    {
    }

    private SqliteConnection Open()
    {
        var connection = OpenConnection();
        EigenesSchema.Sicherstellen(nameof(TagebuchRepository), Paths.DatabasePath, () => SchemaAnlegen(connection));
        return connection;
    }

    private static void SchemaAnlegen(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ForkTagebuchAuffaelligkeiten (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TentId INTEGER NOT NULL,
                MetricKey TEXT NOT NULL,
                BeginnUtc TEXT NOT NULL,
                EndeUtc TEXT NOT NULL,
                Vorher REAL NOT NULL,
                Nachher REAL NOT NULL,
                ErkanntAmUtc TEXT NOT NULL,
                VerworfenAmUtc TEXT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS UX_ForkTagebuchAuffaelligkeiten_Sprung
                ON ForkTagebuchAuffaelligkeiten(TentId, MetricKey, BeginnUtc);
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>Sprünge festhalten; schon bekannte bleiben, wie sie sind (auch „War nichts").</summary>
    /// <returns>Wie viele neu dazukamen.</returns>
    public int Merken(int tentId, IEnumerable<Sprung> spruenge)
    {
        using var connection = Open();
        using var transaktion = connection.BeginTransaction();
        var neu = 0;
        foreach (var sprung in spruenge)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaktion;
            command.CommandText = """
                INSERT OR IGNORE INTO ForkTagebuchAuffaelligkeiten
                    (TentId, MetricKey, BeginnUtc, EndeUtc, Vorher, Nachher, ErkanntAmUtc)
                VALUES ($tentId, $metricKey, $beginn, $ende, $vorher, $nachher, $erkannt);
                """;
            command.Parameters.AddWithValue("$tentId", tentId);
            command.Parameters.AddWithValue("$metricKey", sprung.MetricKey);
            command.Parameters.AddWithValue("$beginn", ToStorageUtc(sprung.LetzteRuheUtc));
            command.Parameters.AddWithValue("$ende", ToStorageUtc(sprung.NeueRuheUtc));
            command.Parameters.AddWithValue("$vorher", sprung.Vorher);
            command.Parameters.AddWithValue("$nachher", sprung.Nachher);
            command.Parameters.AddWithValue("$erkannt", ToStorageUtc(DateTime.UtcNow));
            neu += command.ExecuteNonQuery();
        }

        transaktion.Commit();
        return neu;
    }

    /// <summary>Alle gemerkten Sprünge eines Zelts in diesem Zeitraum (Beginn), ältester zuerst.</summary>
    public List<TagebuchAuffaelligkeit> Lesen(int tentId, DateTime vonUtc, DateTime bisUtc)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT * FROM ForkTagebuchAuffaelligkeiten
            WHERE TentId = $tentId AND BeginnUtc >= $von AND BeginnUtc < $bis
            ORDER BY BeginnUtc, Id;
            """;
        command.Parameters.AddWithValue("$tentId", tentId);
        command.Parameters.AddWithValue("$von", ToStorageUtc(vonUtc));
        command.Parameters.AddWithValue("$bis", ToStorageUtc(bisUtc));
        using var reader = command.ExecuteReader();
        var liste = new List<TagebuchAuffaelligkeit>();
        while (reader.Read()) liste.Add(Lesen(reader));
        return liste;
    }

    public TagebuchAuffaelligkeit? Get(int id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM ForkTagebuchAuffaelligkeiten WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Lesen(reader) : null;
    }

    /// <summary>„War nichts" setzen (<paramref name="wann"/>) oder zurücknehmen (<c>null</c>).</summary>
    public bool Verwerfen(int id, DateTime? wann)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ForkTagebuchAuffaelligkeiten SET VerworfenAmUtc = $wann WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$wann", wann is { } w ? ToStorageUtc(w) : DBNull.Value);
        return command.ExecuteNonQuery() > 0;
    }

    private static TagebuchAuffaelligkeit Lesen(SqliteDataReader reader) => new()
    {
        Id = Convert.ToInt32(reader["Id"]),
        TentId = Convert.ToInt32(reader["TentId"]),
        MetricKey = reader["MetricKey"].ToString() ?? string.Empty,
        BeginnUtc = ParseStoredUtcDateTime(reader["BeginnUtc"].ToString()) ?? DateTime.MinValue,
        EndeUtc = ParseStoredUtcDateTime(reader["EndeUtc"].ToString()) ?? DateTime.MinValue,
        Vorher = Convert.ToDouble(reader["Vorher"]),
        Nachher = Convert.ToDouble(reader["Nachher"]),
        ErkanntAmUtc = ParseStoredUtcDateTime(reader["ErkanntAmUtc"].ToString()) ?? DateTime.MinValue,
        VerworfenAmUtc = ParseStoredUtcDateTime(NullString(reader["VerworfenAmUtc"])),
    };
}
