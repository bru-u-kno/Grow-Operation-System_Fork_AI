using System.Text.Json;

namespace GrowMcp.Services;

/// <summary>
/// Was der Betreiber in den Add-on-Einstellungen eingetragen hat.
/// </summary>
/// <remarks>
/// Es ist genau eine Angabe, und die braucht fast niemand: normalerweise findet
/// der Server Cultivate Control von selbst. Home Assistant legt die Optionen als
/// <c>/data/options.json</c> ab; beim Entwickeln gibt es die Datei nicht, dann
/// greift die Umgebungsvariable.
/// </remarks>
public sealed class McpEinstellungen
{
    /// <summary>Eine von Hand eingetragene Grow-OS-Adresse, sonst leer.</summary>
    public string GrowOsAdresse { get; init; } = string.Empty;

    public static McpEinstellungen Laden(ILogger logger)
    {
        const string pfad = "/data/options.json";
        if (File.Exists(pfad))
        {
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(pfad));
                return new McpEinstellungen
                {
                    GrowOsAdresse = json.RootElement.TryGetProperty("grow_os_adresse", out var wert)
                        ? wert.GetString() ?? string.Empty
                        : string.Empty,
                };
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Die Add-on-Einstellungen ließen sich nicht lesen — es wird gesucht.");
            }
        }

        return new McpEinstellungen
        {
            GrowOsAdresse = Environment.GetEnvironmentVariable("GROW_MCP_GROW_OS") ?? string.Empty,
        };
    }
}
