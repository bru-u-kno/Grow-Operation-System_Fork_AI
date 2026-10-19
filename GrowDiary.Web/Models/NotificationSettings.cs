namespace GrowDiary.Web.Models;

/// <summary>
/// One central place for all push notifications Cultivate Control sends through a Home Assistant
/// notify service: which device to notify, quiet hours, and which categories are on.
/// Stored as key/value rows in AppSettings.
/// </summary>
public sealed class NotificationSettings
{
    /// <summary>The Home Assistant notify service, e.g. <c>notify.mobile_app_pixel</c>.</summary>
    public string? NotifyService { get; set; }

    /// <summary>Quiet-hours window (local hour 0–23). During the window nothing is pushed. Null = off.</summary>
    public int? QuietHoursStartHour { get; set; }
    public int? QuietHoursEndHour { get; set; }

    public bool Thresholds { get; set; } = true;
    public bool Calibration { get; set; } = true;
    public bool Maintenance { get; set; } = true;
    public bool SensorOffline { get; set; } = true;
    public bool Risks { get; set; } = true;

    /// <summary>Warn when monitoring itself stops (no data, HA unreachable, worker stalled).</summary>
    public bool SystemWatch { get; set; } = true;

    /// <summary>A once-a-day summary push ("system is up, here are the values"). Opt-in.</summary>
    public bool DailyDigest { get; set; }
    public int DigestHour { get; set; } = 6;
    public int DigestMinute { get; set; }
    /// <summary>False = short "all OK / N issues" summary; true = full values per tent.</summary>
    public bool DigestDetailed { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(NotifyService);

    public bool IsCategoryEnabled(NotificationCategory category) => category switch
    {
        NotificationCategory.Threshold => Thresholds,
        NotificationCategory.Calibration => Calibration,
        NotificationCategory.Maintenance => Maintenance,
        NotificationCategory.SensorOffline => SensorOffline,
        NotificationCategory.Risk => Risks,
        NotificationCategory.System => SystemWatch,
        _ => false,
    };

    /// <summary>True when <paramref name="localHour"/> falls inside the quiet-hours window.</summary>
    public bool IsQuietHour(int localHour)
    {
        if (QuietHoursStartHour is not { } start || QuietHoursEndHour is not { } end || start == end)
        {
            return false;
        }

        return start < end
            ? localHour >= start && localHour < end
            : localHour >= start || localHour < end; // wraps past midnight, e.g. 22 → 7
    }
}

public enum NotificationCategory
{
    Threshold,
    Calibration,
    Maintenance,
    SensorOffline,
    Risk,
    System,
}
