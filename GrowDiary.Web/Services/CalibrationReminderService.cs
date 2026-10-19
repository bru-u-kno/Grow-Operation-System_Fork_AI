using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Pushes a reminder when sensor calibrations become due. Runs once a day from the snapshot
/// worker and reuses the central <see cref="NotificationService"/> (so it respects the chosen
/// device, quiet hours and the calibration toggle).
/// </summary>
public sealed class CalibrationReminderService
{
    private readonly HardwareRepository _hardware;
    private readonly NotificationService _notifications;

    public CalibrationReminderService(HardwareRepository hardware, NotificationService notifications)
    {
        _hardware = hardware;
        _notifications = notifications;
    }

    /// <summary>Pure message builder (testable): one line summarising the due calibrations, or null when none.</summary>
    public static string? BuildDueMessage(IReadOnlyList<CalibrationEvent> dueEvents)
    {
        var planned = dueEvents.Where(e => e.Status == CalibrationEventStatus.Planned).ToList();
        if (planned.Count == 0)
        {
            return null;
        }

        if (planned.Count == 1)
        {
            return $"Kalibrierung fällig: {Describe(planned[0])}.";
        }

        var titles = string.Join(", ", planned.Take(4).Select(Describe));
        var overflow = planned.Count > 4 ? " …" : string.Empty;
        return $"{planned.Count} Kalibrierungen fällig: {titles}{overflow}.";
    }

    public async Task CheckAndNotifyAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var message = BuildDueMessage(_hardware.GetDueCalibrationEvents(nowUtc));
        if (message is null)
        {
            return;
        }

        await _notifications.SendAsync(NotificationCategory.Calibration, "🌱 Cultivate Control · Kalibrierung", message, cancellationToken);
    }

    private static string Describe(CalibrationEvent calibrationEvent)
        => string.IsNullOrWhiteSpace(calibrationEvent.Title) ? "Sensor" : calibrationEvent.Title;
}
