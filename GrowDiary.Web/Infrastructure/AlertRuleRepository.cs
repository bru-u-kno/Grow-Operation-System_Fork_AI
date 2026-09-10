using GrowDiary.Web.Models;
using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Infrastructure;

public sealed class AlertRuleRepository : RepositoryBase
{
    public AlertRuleRepository(AppPaths paths) : base(paths)
    {
    }

    public List<TentAlertRule> GetForTent(int tentId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM TentAlertRules WHERE TentId = $tentId ORDER BY MetricKey;";
        command.Parameters.AddWithValue("$tentId", tentId);

        var rules = new List<TentAlertRule>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rules.Add(Map(reader));
        }

        return rules;
    }

    public List<TentAlertRule> GetEnabledForTent(int tentId)
        => GetForTent(tentId).Where(rule => rule.Enabled).ToList();

    /// <summary>Replaces all rules for a tent with the supplied set (state is reset).</summary>
    public void ReplaceForTent(int tentId, IReadOnlyList<TentAlertRule> rules)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM TentAlertRules WHERE TentId = $tentId;";
            delete.Parameters.AddWithValue("$tentId", tentId);
            delete.ExecuteNonQuery();
        }

        var nowUtc = ToStorageUtc(DateTime.UtcNow);
        foreach (var rule in rules)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO TentAlertRules
                    (TentId, MetricKey, MinValue, MaxValue, NotifyService, Enabled, CooldownMinutes, Quelle, Toleranz, LastState, LastNotifiedUtc, CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    ($tentId, $metricKey, $minValue, $maxValue, $notifyService, $enabled, $cooldown, $quelle, $toleranz, NULL, NULL, $now, $now);
            """;
            insert.Parameters.AddWithValue("$tentId", tentId);
            insert.Parameters.AddWithValue("$metricKey", rule.MetricKey);
            AddNullable(insert, "$minValue", rule.MinValue);
            AddNullable(insert, "$maxValue", rule.MaxValue);
            insert.Parameters.AddWithValue("$notifyService", rule.NotifyService);
            insert.Parameters.AddWithValue("$enabled", rule.Enabled ? 1 : 0);
            insert.Parameters.AddWithValue("$cooldown", rule.CooldownMinutes);
            insert.Parameters.AddWithValue("$quelle", rule.Quelle.ToString());
            AddNullable(insert, "$toleranz", rule.Toleranz);
            insert.Parameters.AddWithValue("$now", nowUtc);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void UpdateState(int id, string lastState, DateTime? lastNotifiedUtc)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE TentAlertRules
               SET LastState = $lastState,
                   LastNotifiedUtc = $lastNotified,
                   UpdatedAtUtc = $now
             WHERE Id = $id;
        """;
        command.Parameters.AddWithValue("$lastState", lastState);
        command.Parameters.AddWithValue("$lastNotified", lastNotifiedUtc.HasValue ? ToStorageUtc(lastNotifiedUtc.Value) : (object)DBNull.Value);
        command.Parameters.AddWithValue("$now", ToStorageUtc(DateTime.UtcNow));
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static TentAlertRule Map(SqliteDataReader reader) => new()
    {
        Id = Convert.ToInt32(reader["Id"]),
        TentId = Convert.ToInt32(reader["TentId"]),
        MetricKey = reader["MetricKey"].ToString() ?? string.Empty,
        MinValue = NullableDouble(reader["MinValue"]),
        MaxValue = NullableDouble(reader["MaxValue"]),
        NotifyService = reader["NotifyService"].ToString() ?? string.Empty,
        Enabled = Convert.ToInt32(reader["Enabled"]) == 1,
        CooldownMinutes = Convert.ToInt32(reader["CooldownMinutes"]),
        // HasColumn, weil die Spalten erst am 10.09.2026 dazukamen: eine noch
        // nicht migrierte Datei darf hier nicht mit einer Ausnahme aussteigen.
        Quelle = HasColumn(reader, "Quelle")
                 && Enum.TryParse<Services.Grenzwertquelle>(NullString(reader["Quelle"]), out var quelle)
            ? quelle
            : Services.Grenzwertquelle.Fest,
        Toleranz = HasColumn(reader, "Toleranz") ? NullableDouble(reader["Toleranz"]) : null,
        LastState = NullString(reader["LastState"]),
        LastNotifiedUtc = ParseStoredUtcDateTime(NullString(reader["LastNotifiedUtc"])),
    };
}
