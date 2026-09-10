namespace GrowDiary.Web.Models;

/// <summary>
/// A user-defined threshold on a tent's live sensor metric. When the current value
/// crosses below <see cref="MinValue"/> or above <see cref="MaxValue"/>, Grow OS sends a
/// push notification through a Home Assistant <c>notify</c> service. Evaluation is
/// edge-triggered (one alert per breach) with a cooldown to avoid flapping spam.
/// </summary>
public sealed class TentAlertRule
{
    public int Id { get; set; }
    public int TentId { get; set; }

    /// <summary>Canonical live metric key, e.g. <c>reservoir-ph</c> (see TentSensorMetricKeyMap).</summary>
    public string MetricKey { get; set; } = string.Empty;

    /// <summary>Alert when the value drops below this. Null = no lower bound.</summary>
    public double? MinValue { get; set; }

    /// <summary>Alert when the value rises above this. Null = no upper bound.</summary>
    public double? MaxValue { get; set; }

    /// <summary>The Home Assistant notify service to call, e.g. <c>notify.mobile_app_pixel</c>.</summary>
    public string NotifyService { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    /// <summary>Minimum minutes between repeat notifications for the same rule.</summary>
    public int CooldownMinutes { get; set; } = 30;

    /// <summary>
    /// Woher die Grenzen kommen: die eingetragenen Zahlen (<c>Fest</c>) oder das
    /// Zielband der laufenden Woche (<c>Plan</c>). Standard ist <c>Fest</c> —
    /// bestehende Regeln aendern ihr Verhalten dadurch nicht.
    /// </summary>
    public Services.Grenzwertquelle Quelle { get; set; } = Services.Grenzwertquelle.Fest;

    /// <summary>
    /// Wie weit der Wert bei <c>Plan</c> ueber das Zielband hinausdarf, bevor
    /// gemeldet wird. Null = der Standard der Messgroesse.
    /// </summary>
    public double? Toleranz { get; set; }

    /// <summary>Last evaluated state: <c>InRange</c>, <c>Below</c> or <c>Above</c> (null = never evaluated).</summary>
    public string? LastState { get; set; }

    public DateTime? LastNotifiedUtc { get; set; }
}
