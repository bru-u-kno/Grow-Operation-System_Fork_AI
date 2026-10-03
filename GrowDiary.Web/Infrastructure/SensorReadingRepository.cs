using System.Globalization;
using GrowDiary.Web.Models;
using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Infrastructure;

public sealed class SensorReadingRepository
{
    private readonly AppPaths _paths;

    public SensorReadingRepository(AppPaths paths)
    {
        _paths = paths;
    }

    public void AddReading(TentSensorReading reading)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO TentSensorReadings (TentId, MetricKey, Value, Unit, CapturedAtUtc)
            VALUES ($tentId, $metricKey, $value, $unit, $capturedAtUtc);
            """;
        cmd.Parameters.AddWithValue("$tentId", reading.TentId);
        cmd.Parameters.AddWithValue("$metricKey", reading.MetricKey);
        cmd.Parameters.AddWithValue("$value", reading.Value);
        cmd.Parameters.AddWithValue("$unit", (object?)reading.Unit ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$capturedAtUtc", reading.CapturedAtUtc.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<TentSensorReading> GetReadings(
        int tentId, string metricKey, DateTime fromUtc, DateTime toUtc)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, TentId, MetricKey, Value, Unit, CapturedAtUtc
            FROM TentSensorReadings
            WHERE TentId = $tentId
              AND MetricKey = $metricKey
              AND CapturedAtUtc >= $from
              AND CapturedAtUtc <= $to
            ORDER BY CapturedAtUtc ASC;
            """;
        cmd.Parameters.AddWithValue("$tentId", tentId);
        cmd.Parameters.AddWithValue("$metricKey", metricKey);
        cmd.Parameters.AddWithValue("$from", fromUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$to", toUtc.ToString("O"));
        return ReadReadings(cmd);
    }

    /// <summary>
    /// Der jüngste Wert dieser Messgröße in diesem Zelt — null, wenn es keinen gibt.
    /// </summary>
    /// <remarks>
    /// Für die Dosierung: dort zählt der aktuelle Wert und sein Alter, nicht der
    /// Verlauf. Über <see cref="GetReadings"/> mit einem Zeitfenster zu gehen
    /// hiesse raten, wie weit man zurückschauen muss, und bei einem stehenden
    /// Sensor käme leer zurück statt „der letzte Wert ist drei Tage alt" — genau
    /// der Unterschied, an dem die Automatik abbrechen muss.
    /// </remarks>
    public TentSensorReading? GetNewestReading(int tentId, string metricKey)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, TentId, MetricKey, Value, Unit, CapturedAtUtc
            FROM TentSensorReadings
            WHERE TentId = $tentId AND MetricKey = $metricKey
            ORDER BY CapturedAtUtc DESC
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$tentId", tentId);
        cmd.Parameters.AddWithValue("$metricKey", metricKey);
        return ReadReadings(cmd).FirstOrDefault();
    }

    public IReadOnlyList<TentSensorReading> GetReadingsForDay(
        int tentId, string metricKey, DateOnly date)
    {
        // Der Aufrufer meint einen LOKALEN Kalendertag, die Spalte ist UTC.
        // Wer das Datum direkt als UTC-Grenze nimmt, buendelt in DE die Werte
        // von 02:00 bis 02:00 — Mitternacht bis zwei Uhr rutschte in den
        // Vortags-Eintrag (dieselbe Familie wie der alte UTC-als-Ortszeit-Fall).
        var from = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
        var to = from.AddDays(1).AddTicks(-1);
        return GetReadings(tentId, metricKey, from, to);
    }

    /// <summary>When the tent last delivered any sensor value at all — the watchdog's pulse.</summary>
    public DateTime? GetNewestReadingUtc(int tentId)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT MAX(CapturedAtUtc) FROM TentSensorReadings WHERE TentId = $tentId;";
        cmd.Parameters.AddWithValue("$tentId", tentId);
        var result = cmd.ExecuteScalar();
        return result is null || result is DBNull
            ? null
            : DateTime.TryParse(result.ToString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed.ToUniversalTime()
                : null;
    }

    /// <summary>Alle Rohwerte dieser Messgrößen, die genau diesen Wert tragen (für die Nullbild-Bereinigung).</summary>
    public IReadOnlyList<TentSensorReading> GetReadingsWithValue(IReadOnlyList<string> metricKeys, double value)
    {
        if (metricKeys.Count == 0) return [];

        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        var platzhalter = metricKeys.Select((_, i) => $"$k{i}").ToList();
        cmd.CommandText =
            "SELECT Id, TentId, MetricKey, Value, Unit, CapturedAtUtc FROM TentSensorReadings " +
            $"WHERE Value = $value AND MetricKey IN ({string.Join(", ", platzhalter)}) " +
            "ORDER BY CapturedAtUtc ASC;";
        cmd.Parameters.AddWithValue("$value", value);
        for (var i = 0; i < metricKeys.Count; i++) cmd.Parameters.AddWithValue(platzhalter[i], metricKeys[i]);
        return ReadReadings(cmd);
    }

    /// <summary>
    /// Löscht diese Rohwerte und schreibt diese Tageswerte — alles oder nichts.
    /// </summary>
    /// <remarks>
    /// Für die Nullbild-Bereinigung (<c>WassersondenNullbild</c>): bricht sie
    /// zwischen Löschen und Schreiben ab, wären die Rohwerte weg und der alte
    /// Tageswert mit seinem Minimum 0 bliebe für immer stehen. In einer
    /// Transaktion bleibt dann beides, wie es war, und der nächste Start
    /// versucht es neu.
    /// </remarks>
    public void Bereinigen(IEnumerable<int> loeschen, IEnumerable<TentSensorDailyStat> tageswerte)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = "DELETE FROM TentSensorReadings WHERE Id = $id;";
            var id = cmd.Parameters.Add("$id", SqliteType.Integer);
            foreach (var eintrag in loeschen)
            {
                id.Value = eintrag;
                cmd.ExecuteNonQuery();
            }
        }

        foreach (var stat in tageswerte)
        {
            using var cmd = TageswertBefehl(connection, stat);
            cmd.Transaction = transaction;
            cmd.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>Der älteste noch vorhandene Rohwert dieser Messgröße — null, wenn es keinen gibt.</summary>
    public DateTime? GetOldestReadingUtc(int tentId, string metricKey)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, TentId, MetricKey, Value, Unit, CapturedAtUtc
            FROM TentSensorReadings
            WHERE TentId = $tentId AND MetricKey = $metricKey
            ORDER BY CapturedAtUtc ASC
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$tentId", tentId);
        cmd.Parameters.AddWithValue("$metricKey", metricKey);
        return ReadReadings(cmd).FirstOrDefault()?.CapturedAtUtc;
    }

    public void DeleteOlderThan(DateTime cutoffUtc)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM TentSensorReadings WHERE CapturedAtUtc < $cutoff;";
        cmd.Parameters.AddWithValue("$cutoff", cutoffUtc.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void UpsertDailyStat(TentSensorDailyStat stat)
    {
        using var connection = OpenConnection();
        using var cmd = TageswertBefehl(connection, stat);
        cmd.ExecuteNonQuery();
    }

    private static SqliteCommand TageswertBefehl(SqliteConnection connection, TentSensorDailyStat stat)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO TentSensorDailyStats
                (TentId, MetricKey, Date, Min, Max, Median, P5, P95, Avg, Count, Unit)
            VALUES
                ($tentId, $metricKey, $date, $min, $max, $median, $p5, $p95, $avg, $count, $unit)
            ON CONFLICT(TentId, MetricKey, Date) DO UPDATE SET
                Min    = excluded.Min,
                Max    = excluded.Max,
                Median = excluded.Median,
                P5     = excluded.P5,
                P95    = excluded.P95,
                Avg    = excluded.Avg,
                Count  = excluded.Count,
                Unit   = excluded.Unit;
            """;
        cmd.Parameters.AddWithValue("$tentId",    stat.TentId);
        cmd.Parameters.AddWithValue("$metricKey", stat.MetricKey);
        cmd.Parameters.AddWithValue("$date",      stat.Date.ToString("O"));
        cmd.Parameters.AddWithValue("$min",       stat.Min);
        cmd.Parameters.AddWithValue("$max",       stat.Max);
        cmd.Parameters.AddWithValue("$median",    stat.Median);
        cmd.Parameters.AddWithValue("$p5",        stat.P5);
        cmd.Parameters.AddWithValue("$p95",       stat.P95);
        cmd.Parameters.AddWithValue("$avg",       stat.Avg);
        cmd.Parameters.AddWithValue("$count",     stat.Count);
        cmd.Parameters.AddWithValue("$unit",      (object?)stat.Unit ?? DBNull.Value);
        return cmd;
    }

    public IReadOnlyList<TentSensorDailyStat> GetDailyStats(
        int tentId, string metricKey, DateOnly from, DateOnly to)
    {
        using var connection = OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, TentId, MetricKey, Date, Min, Max, Median, P5, P95, Avg, Count, Unit
            FROM TentSensorDailyStats
            WHERE TentId = $tentId
              AND MetricKey = $metricKey
              AND Date >= $from
              AND Date <= $to
            ORDER BY Date ASC;
            """;
        cmd.Parameters.AddWithValue("$tentId",    tentId);
        cmd.Parameters.AddWithValue("$metricKey", metricKey);
        cmd.Parameters.AddWithValue("$from",      from.ToString("O"));
        cmd.Parameters.AddWithValue("$to",        to.ToString("O"));

        using var reader = cmd.ExecuteReader();
        var list = new List<TentSensorDailyStat>();
        while (reader.Read())
        {
            list.Add(new TentSensorDailyStat
            {
                Id        = Convert.ToInt32(reader["Id"]),
                TentId    = Convert.ToInt32(reader["TentId"]),
                MetricKey = reader["MetricKey"].ToString()!,
                Date      = ParseDateOnlyOrDefault(reader["Date"]),
                Min       = Convert.ToDouble(reader["Min"]),
                Max       = Convert.ToDouble(reader["Max"]),
                Median    = Convert.ToDouble(reader["Median"]),
                P5        = Convert.ToDouble(reader["P5"]),
                P95       = Convert.ToDouble(reader["P95"]),
                Avg       = Convert.ToDouble(reader["Avg"]),
                Count     = Convert.ToInt32(reader["Count"]),
                Unit      = reader["Unit"] as string
            });
        }
        return list;
    }

    // ── Hilfsmethode: Rohdaten lesen ─────────────────────────────────────────

    private static IReadOnlyList<TentSensorReading> ReadReadings(SqliteCommand cmd)
    {
        using var reader = cmd.ExecuteReader();
        var list = new List<TentSensorReading>();
        while (reader.Read())
        {
            list.Add(new TentSensorReading
            {
                Id           = Convert.ToInt32(reader["Id"]),
                TentId       = Convert.ToInt32(reader["TentId"]),
                MetricKey    = reader["MetricKey"].ToString()!,
                Value        = Convert.ToDouble(reader["Value"]),
                Unit         = reader["Unit"] as string,
                CapturedAtUtc = ParseUtcOrDefault(reader["CapturedAtUtc"])
            });
        }
        return list;
    }

    private static DateTime ParseUtcOrDefault(object raw)
    {
        var text = raw is DBNull ? null : raw?.ToString();
        if (!string.IsNullOrWhiteSpace(text) &&
            DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return DateTime.UtcNow;
    }

    private static DateOnly ParseDateOnlyOrDefault(object raw)
    {
        var text = raw is DBNull ? null : raw?.ToString();
        return DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? parsed
            : DateOnly.FromDateTime(DateTime.UtcNow);
    }

    private SqliteConnection OpenConnection()
    {
        var builder = new SqliteConnectionStringBuilder { DataSource = _paths.DatabasePath };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }
}
