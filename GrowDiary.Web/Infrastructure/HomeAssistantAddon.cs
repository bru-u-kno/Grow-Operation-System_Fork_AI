using GrowDiary.Web.Models;

namespace GrowDiary.Web.Infrastructure;

/// <summary>
/// Detects and describes the Home Assistant add-on runtime.
///
/// When Cultivate Control runs as a Home Assistant add-on, the Supervisor injects a
/// <c>SUPERVISOR_TOKEN</c> and proxies the Home Assistant core API at
/// <c>http://supervisor/core</c>. That means the HA connection needs no manual
/// setup at all — no URL, no long-lived access token. This helper turns that
/// environment into effective <see cref="HomeAssistantSettings"/> so every
/// runtime consumer (live dashboard, snapshot worker, camera proxy, entity
/// picker) works out of the box inside the add-on.
/// </summary>
public static class HomeAssistantAddon
{
    public const string SupervisorTokenEnvironmentVariable = "SUPERVISOR_TOKEN";

    /// <summary>Home Assistant core API, reachable from any add-on via the Supervisor proxy.</summary>
    public const string SupervisorCoreUrl = "http://supervisor/core";

    /// <summary>The Supervisor-provided access token, or null when not running as an add-on.</summary>
    public static string? SupervisorToken
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(SupervisorTokenEnvironmentVariable);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    /// <summary>True when Cultivate Control is running inside a Home Assistant add-on.</summary>
    public static bool IsRunningAsAddon => SupervisorToken is not null;

    /// <summary>
    /// Returns the effective Home Assistant connection. Inside the add-on the
    /// Supervisor-provided URL + token always win (zero-config); otherwise the
    /// user's stored settings apply unchanged.
    /// </summary>
    public static HomeAssistantSettings ResolveEffective(HomeAssistantSettings stored)
    {
        // Testdatenmodus (GROW_OS_DEMO) schlägt alles: der Entwicklungsrechner
        // hat kein Home Assistant, soll sich aber wie ein verbundener verhalten.
        // Ohne das hielten Watchdog, Live und Dosierung ihn für unkonfiguriert
        // und blockierten, bevor die erfundenen Werte gefragt wären.
        if (Services.DemoData.IsEnabled)
        {
            return Services.DemoData.Settings();
        }

        var token = SupervisorToken;
        if (token is null)
        {
            return stored;
        }

        return new HomeAssistantSettings
        {
            BaseUrl = SupervisorCoreUrl,
            AccessToken = token,
            Enabled = true,
        };
    }
}
