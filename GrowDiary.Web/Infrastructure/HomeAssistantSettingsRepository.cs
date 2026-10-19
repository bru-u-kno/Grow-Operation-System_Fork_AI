using GrowDiary.Web.Models;
using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Infrastructure;

public sealed class HomeAssistantSettingsRepository : RepositoryBase
{
    public HomeAssistantSettingsRepository(AppPaths paths) : base(paths)
    {
    }

    public HomeAssistantSettings GetHomeAssistantSettings()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Key, Value FROM AppSettings WHERE Key IN ('ha:baseUrl','ha:accessToken','ha:enabled');";
        using var reader = command.ExecuteReader();

        var settings = new HomeAssistantSettings();
        while (reader.Read())
        {
            var key = reader["Key"]?.ToString();
            var value = reader["Value"]?.ToString();
            switch (key)
            {
                case "ha:baseUrl":
                    settings.BaseUrl = value;
                    break;
                case "ha:accessToken":
                    settings.AccessToken = value;
                    break;
                case "ha:enabled":
                    settings.Enabled = string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                    break;
            }
        }
        return settings;
    }

    /// <summary>
    /// The connection Cultivate Control should actually talk to at runtime. Inside a Home
    /// Assistant add-on the Supervisor-provided URL + token override the stored
    /// settings, so live reads work without any manual configuration.
    /// </summary>
    public HomeAssistantSettings GetEffectiveHomeAssistantSettings()
        => HomeAssistantAddon.ResolveEffective(GetHomeAssistantSettings());

    public void SaveHomeAssistantSettings(HomeAssistantSettings settings)
    {
        using var connection = OpenConnection();
        UpsertSetting(connection, "ha:baseUrl", settings.BaseUrl);
        UpsertSetting(connection, "ha:accessToken", settings.AccessToken);
        UpsertSetting(connection, "ha:enabled", settings.Enabled ? "1" : "0");
    }

    private static void UpsertSetting(SqliteConnection connection, string key, string? value)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AppSettings (Key, Value)
            VALUES ($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
        """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", (object?)value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }
}
