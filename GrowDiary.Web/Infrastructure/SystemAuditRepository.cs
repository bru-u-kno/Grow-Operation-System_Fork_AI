using System.Globalization;
using GrowDiary.Web.Models;
using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Infrastructure;

public sealed class SystemAuditRepository
{
    private readonly AppPaths _paths;

    public SystemAuditRepository(AppPaths paths)
    {
        _paths = paths;
    }

    public void Add(SystemAuditEvent entry)
    {
        entry.CreatedAtUtc = DateTime.UtcNow;
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO SystemAuditEvents (EventType, Action, Summary, Severity, Source, RemoteAddress, RelatedGrowId, RelatedFileName, Success, CreatedAtUtc,
                                           KiSchluesselId, Methode, Pfad, HttpStatus, Fehlercode, HaDienst)
            VALUES ($eventType, $action, $summary, $severity, $source, $remoteAddress, $relatedGrowId, $relatedFileName, $success, $createdAtUtc,
                    $kiSchluesselId, $methode, $pfad, $httpStatus, $fehlercode, $haDienst);";
        command.Parameters.AddWithValue("$eventType", entry.EventType);
        command.Parameters.AddWithValue("$action", entry.Action);
        command.Parameters.AddWithValue("$summary", entry.Summary);
        command.Parameters.AddWithValue("$severity", string.IsNullOrWhiteSpace(entry.Severity) ? "info" : entry.Severity);
        command.Parameters.AddWithValue("$source", string.IsNullOrWhiteSpace(entry.Source) ? "backend" : entry.Source);
        command.Parameters.AddWithValue("$remoteAddress", (object?)entry.RemoteAddress ?? DBNull.Value);
        command.Parameters.AddWithValue("$relatedGrowId", (object?)entry.RelatedGrowId ?? DBNull.Value);
        command.Parameters.AddWithValue("$relatedFileName", (object?)entry.RelatedFileName ?? DBNull.Value);
        command.Parameters.AddWithValue("$success", entry.Success ? 1 : 0);
        command.Parameters.AddWithValue("$createdAtUtc", entry.CreatedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$kiSchluesselId", (object?)entry.KiSchluesselId ?? DBNull.Value);
        command.Parameters.AddWithValue("$methode", (object?)entry.Methode ?? DBNull.Value);
        command.Parameters.AddWithValue("$pfad", (object?)entry.Pfad ?? DBNull.Value);
        command.Parameters.AddWithValue("$httpStatus", (object?)entry.HttpStatus ?? DBNull.Value);
        command.Parameters.AddWithValue("$fehlercode", (object?)entry.Fehlercode ?? DBNull.Value);
        command.Parameters.AddWithValue("$haDienst", (object?)entry.HaDienst ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Fork AI (A-003, 03.10.2026): Die jüngsten Einträge einer Quelle, neueste zuerst —
    /// wahlweise nur bestimmte Arten (<see cref="SystemAuditEvent.Action"/>) und
    /// nur die eines KI-Schlüssels.
    /// </summary>
    /// <remarks>
    /// Gefiltert wird in SQL, nicht danach: sonst schnitte die Grenze erst die
    /// jüngsten <paramref name="limit"/> Einträge ab und filterte dann — ein
    /// Schlüssel, der lange nichts getan hat, stünde mit leerer Liste da. Ein
    /// Eintrag ohne Schlüssel-Id (ungültiger Schlüssel, alle aus forkai.163)
    /// erscheint nur ohne Filter.
    /// </remarks>
    public List<SystemAuditEvent> GetRecentForSource(string source, int limit, IReadOnlyCollection<string>? actions = null, int? kiSchluesselId = null)
    {
        var safeLimit = Math.Clamp(limit, 1, 500);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var bedingungen = new List<string> { "Source = $source" };
        command.Parameters.AddWithValue("$source", source);

        if (actions is { Count: > 0 })
        {
            var namen = new List<string>();
            foreach (var action in actions)
            {
                var name = "$action" + namen.Count.ToString(CultureInfo.InvariantCulture);
                namen.Add(name);
                command.Parameters.AddWithValue(name, action);
            }
            bedingungen.Add("Action IN (" + string.Join(", ", namen) + ")");
        }

        if (kiSchluesselId is { } id)
        {
            bedingungen.Add("KiSchluesselId = $kiSchluesselId");
            command.Parameters.AddWithValue("$kiSchluesselId", id);
        }

        command.CommandText = "SELECT * FROM SystemAuditEvents WHERE " + string.Join(" AND ", bedingungen)
            + " ORDER BY CreatedAtUtc DESC, Id DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", safeLimit);

        var items = new List<SystemAuditEvent>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(Map(reader));
        }

        return items;
    }

    public List<SystemAuditEvent> GetRecent(int limit = 100, string? eventType = null)
    {
        var safeLimit = Math.Clamp(limit, 1, 500);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        if (string.IsNullOrWhiteSpace(eventType))
        {
            command.CommandText = "SELECT * FROM SystemAuditEvents ORDER BY CreatedAtUtc DESC, Id DESC LIMIT $limit;";
        }
        else
        {
            command.CommandText = "SELECT * FROM SystemAuditEvents WHERE EventType = $eventType ORDER BY CreatedAtUtc DESC, Id DESC LIMIT $limit;";
            command.Parameters.AddWithValue("$eventType", eventType.Trim());
        }

        command.Parameters.AddWithValue("$limit", safeLimit);
        var items = new List<SystemAuditEvent>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(Map(reader));
        }

        return items;
    }

    private static SystemAuditEvent Map(SqliteDataReader reader)
        => new()
        {
            Id = Convert.ToInt32((long)reader["Id"]),
            EventType = reader["EventType"]?.ToString() ?? string.Empty,
            Action = reader["Action"]?.ToString() ?? string.Empty,
            Summary = reader["Summary"]?.ToString() ?? string.Empty,
            Severity = reader["Severity"]?.ToString() ?? "info",
            Source = reader["Source"]?.ToString() ?? "backend",
            RemoteAddress = reader["RemoteAddress"] is DBNull ? null : reader["RemoteAddress"]?.ToString(),
            RelatedGrowId = reader["RelatedGrowId"] is DBNull ? null : Convert.ToInt32((long)reader["RelatedGrowId"]),
            RelatedFileName = reader["RelatedFileName"] is DBNull ? null : reader["RelatedFileName"]?.ToString(),
            Success = Convert.ToInt32((long)reader["Success"]) == 1,
            CreatedAtUtc = ParseUtcOrDefault(reader["CreatedAtUtc"]),
            KiSchluesselId = Spalte(reader, "KiSchluesselId") is long schluessel ? Convert.ToInt32(schluessel) : null,
            Methode = Spalte(reader, "Methode") as string,
            Pfad = Spalte(reader, "Pfad") as string,
            HttpStatus = Spalte(reader, "HttpStatus") is long status ? Convert.ToInt32(status) : null,
            Fehlercode = Spalte(reader, "Fehlercode") as string,
            HaDienst = Spalte(reader, "HaDienst") as string,
        };

    /// <summary>
    /// Fork AI (A-003, 03.10.2026): Eine Spalte, die erst nachgezogen wird — oder null.
    /// </summary>
    /// <remarks>
    /// Der DatabaseInitializer zieht sie beim Start und nach dem Zurückspielen
    /// nach. Wer dazwischen liest (eine eben eingespielte ältere Datei), soll
    /// das Protokoll trotzdem sehen und nicht an einer fehlenden Spalte scheitern.
    /// </remarks>
    private static object? Spalte(SqliteDataReader reader, string name)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), name, StringComparison.OrdinalIgnoreCase))
            {
                return reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
        }
        return null;
    }

    private static DateTime ParseUtcOrDefault(object raw)
    {
        var text = raw is DBNull ? null : raw?.ToString();
        if (!string.IsNullOrWhiteSpace(text)
            && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return DateTime.UtcNow;
    }

    private SqliteConnection OpenConnection()
    {
        var builder = new SqliteConnectionStringBuilder { DataSource = _paths.DatabasePath };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return connection;
    }
}
