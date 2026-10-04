using System.Globalization;
using GrowDiary.Web.Models;
using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Infrastructure;

public sealed class JournalRepository
{
    private readonly AppPaths _paths;

    public JournalRepository(AppPaths paths)
    {
        _paths = paths;
    }

    public List<JournalEntry> GetForGrow(int growId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM JournalEntries WHERE GrowId = $growId ORDER BY OccurredAtUtc DESC, Id DESC;";
        command.Parameters.AddWithValue("$growId", growId);
        var items = new List<JournalEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(Map(reader));
        }
        return items;
    }

    public JournalEntry? Get(int id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM JournalEntries WHERE Id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    /// <summary>Einen Journaleintrag entfernen.</summary>
    /// <remarks>
    /// Ein Journal ist ein Tagebuch, kein Gesetzblatt. Wer den falschen Grow
    /// erwischt oder sich vertippt, muss den Eintrag loswerden koennen.
    /// <c>SopStepInstances.JournalEntryId</c> haengt mit
    /// <c>ON DELETE SET NULL</c> daran — der Ablaufschritt bleibt, er verliert
    /// nur seinen Verweis.
    /// </remarks>
    public void Delete(int id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM JournalEntries WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public int Create(JournalEntry entry)
    {
        entry.CreatedAtUtc = DateTime.UtcNow;
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO JournalEntries (GrowId, MeasurementId, Title, Body, EntryType, Source, OccurredAtUtc, CreatedAtUtc, UpdatedAtUtc)
            VALUES ($growId, $measurementId, $title, $body, $entryType, $source, $occurredAtUtc, $createdAtUtc, $updatedAtUtc);
            SELECT last_insert_rowid();
        """;
        command.Parameters.AddWithValue("$growId", entry.GrowId);
        command.Parameters.AddWithValue("$measurementId", (object?)entry.MeasurementId ?? DBNull.Value);
        command.Parameters.AddWithValue("$title", (object?)entry.Title ?? DBNull.Value);
        command.Parameters.AddWithValue("$body", (object?)entry.Body ?? DBNull.Value);
        command.Parameters.AddWithValue("$entryType", entry.EntryType.ToString());
        command.Parameters.AddWithValue("$source", entry.Source.ToString());
        command.Parameters.AddWithValue("$occurredAtUtc", entry.OccurredAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$createdAtUtc", entry.CreatedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updatedAtUtc", entry.UpdatedAtUtc is { } geaendert
            ? geaendert.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
            : DBNull.Value);
        return Convert.ToInt32((long)command.ExecuteScalar()!);
    }

    /// <summary>Titel, Text, Art und Zeitpunkt eines Eintrags ändern.</summary>
    /// <remarks>
    /// Grow, Messungsbezug, Herkunft und Anlagezeitpunkt bleiben — korrigiert
    /// wird der Inhalt, nicht die Geschichte des Eintrags. Fotos hängen an der
    /// Messung, nicht am Eintrag, und bleiben deshalb ebenfalls unberührt.
    /// Setzt <see cref="JournalEntry.UpdatedAtUtc"/> auf jetzt.
    /// </remarks>
    /// <returns><c>false</c>, wenn es den Eintrag nicht gibt.</returns>
    public bool Update(JournalEntry entry)
    {
        entry.UpdatedAtUtc = DateTime.UtcNow;
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE JournalEntries
               SET Title = $title, Body = $body, EntryType = $entryType,
                   OccurredAtUtc = $occurredAtUtc, UpdatedAtUtc = $updatedAtUtc
             WHERE Id = $id;
        """;
        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$title", (object?)entry.Title ?? DBNull.Value);
        command.Parameters.AddWithValue("$body", (object?)entry.Body ?? DBNull.Value);
        command.Parameters.AddWithValue("$entryType", entry.EntryType.ToString());
        command.Parameters.AddWithValue("$occurredAtUtc", entry.OccurredAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updatedAtUtc", entry.UpdatedAtUtc.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        return command.ExecuteNonQuery() == 1;
    }

    private static JournalEntry Map(SqliteDataReader reader)
        => new()
        {
            Id = Convert.ToInt32((long)reader["Id"]),
            GrowId = Convert.ToInt32((long)reader["GrowId"]),
            MeasurementId = reader["MeasurementId"] is DBNull ? null : Convert.ToInt32((long)reader["MeasurementId"]),
            Title = reader["Title"] is DBNull ? null : reader["Title"]?.ToString(),
            Body = reader["Body"] is DBNull ? null : reader["Body"]?.ToString(),
            EntryType = Enum.TryParse<JournalEntryType>(reader["EntryType"]?.ToString(), out var type) ? type : JournalEntryType.Note,
            Source = Enum.TryParse<ValueOrigin>(reader["Source"]?.ToString(), out var source) ? source : ValueOrigin.Manual,
            OccurredAtUtc = ParseUtcOrDefault(reader["OccurredAtUtc"]),
            CreatedAtUtc = ParseUtcOrDefault(reader["CreatedAtUtc"]),
            UpdatedAtUtc = reader["UpdatedAtUtc"] is DBNull or null ? null : ParseUtcOrDefault(reader["UpdatedAtUtc"])
        };

    private static DateTime ParseUtcOrDefault(object raw)
    {
        var text = raw is DBNull ? null : raw?.ToString();
        if (!string.IsNullOrWhiteSpace(text) &&
            DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var parsed))
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
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }
}
