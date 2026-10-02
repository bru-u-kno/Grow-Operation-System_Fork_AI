using System.Text.Json;
using GrowDiary.Web.Models;
using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Infrastructure;

/// <summary>Die eigenen Sollwert-Profile des Nutzers.</summary>
public sealed class SetpointProfileRepository : RepositoryBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public SetpointProfileRepository(AppPaths paths) : base(paths)
    {
    }

    public List<SetpointProfile> GetAll()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM SetpointProfiles ORDER BY Name;";

        var profiles = new List<SetpointProfile>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) profiles.Add(Map(reader));
        return profiles;
    }

    public SetpointProfile? Get(int id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM SetpointProfiles WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public int Insert(SetpointProfile profile)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SetpointProfiles (Name, BaseProfileId, OverridesJson, CreatedAtUtc, UpdatedAtUtc)
            VALUES ($name, $base, $overrides, $now, $now);
            SELECT last_insert_rowid();
        """;
        Bind(command, profile);
        command.Parameters.AddWithValue("$now", ToStorageUtc(DateTime.UtcNow));
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public void Update(SetpointProfile profile)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SetpointProfiles
               SET Name = $name, BaseProfileId = $base, OverridesJson = $overrides, UpdatedAtUtc = $now
             WHERE Id = $id;
        """;
        Bind(command, profile);
        command.Parameters.AddWithValue("$now", ToStorageUtc(DateTime.UtcNow));
        command.Parameters.AddWithValue("$id", profile.Id);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Löscht das Profil und löst jeden Verweis darauf.
    /// </summary>
    /// <remarks>
    /// Ohne das Lösen zeigte ein Grow auf ein Profil, das es nicht mehr gibt.
    /// Die Auflösung fiele dann still auf den Anbaustil zurück — richtig, aber
    /// unerklärlich. Lieber gleich sauber: der Verweis verschwindet mit.
    /// </remarks>
    public void Delete(int id)
    {
        var reference = SetpointProfile.Reference(id);
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        foreach (var table in new[] { "Grows", "GrowSystems" })
        {
            using var clear = connection.CreateCommand();
            clear.Transaction = transaction;
            clear.CommandText = $"UPDATE {table} SET SetpointProfileId = NULL WHERE SetpointProfileId = $ref;";
            clear.Parameters.AddWithValue("$ref", reference);
            clear.ExecuteNonQuery();
        }

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM SetpointProfiles WHERE Id = $id;";
            delete.Parameters.AddWithValue("$id", id);
            delete.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static void Bind(SqliteCommand command, SetpointProfile profile)
    {
        command.Parameters.AddWithValue("$name", profile.Name);
        command.Parameters.AddWithValue("$base", profile.BaseProfileId);
        command.Parameters.AddWithValue("$overrides", JsonSerializer.Serialize(profile.Overrides, Json));
    }

    private static SetpointProfile Map(SqliteDataReader reader)
    {
        var raw = NullString(reader["OverridesJson"]);
        Dictionary<string, Dictionary<string, double>> overrides;
        try
        {
            overrides = string.IsNullOrWhiteSpace(raw)
                ? new()
                : JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, double>>>(raw, Json) ?? new();
        }
        catch (JsonException)
        {
            // Ein kaputter Eintrag darf das Profil nicht unbenutzbar machen —
            // dann gilt eben ueberall die Basis.
            overrides = new();
        }

        return new SetpointProfile
        {
            Id = Convert.ToInt32(reader["Id"]),
            Name = reader["Name"].ToString() ?? string.Empty,
            BaseProfileId = reader["BaseProfileId"].ToString() ?? "rdwc-default",
            // Kanonische Phasennamen auch beim Lesen (Fork AI, 02.10.2026): vor diesem
            // Tag konnte „flower" gespeichert werden — Apply sucht „Flower" und fand es nie.
            Overrides = SetpointProfile.PhasenNormalisiert(overrides),
            CreatedAtUtc = ParseStoredUtcDateTime(NullString(reader["CreatedAtUtc"])) ?? DateTime.UtcNow,
            UpdatedAtUtc = ParseStoredUtcDateTime(NullString(reader["UpdatedAtUtc"])) ?? DateTime.UtcNow,
        };
    }
}
