using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Produces("application/json")]
[KiStufe(KiStufe.Verwaltung)]
public sealed class NotificationsApiController : ControllerBase
{
    private readonly NotificationSettingsRepository _settingsRepo;
    private readonly GrowRepository _repository;
    private readonly HomeAssistantService _homeAssistant;
    private readonly HardwareRepository _hardware;
    private readonly NotificationService _notifications;

    public NotificationsApiController(
        NotificationSettingsRepository settingsRepo,
        GrowRepository repository,
        HomeAssistantService homeAssistant,
        HardwareRepository hardware,
        NotificationService notifications)
    {
        _settingsRepo = settingsRepo;
        _repository = repository;
        _homeAssistant = homeAssistant;
        _hardware = hardware;
        _notifications = notifications;
    }

    [HttpGet("settings")]
    [ProducesResponseType(typeof(NotificationSettingsDto), StatusCodes.Status200OK)]
    public ActionResult<NotificationSettingsDto> GetSettings()
        => Ok(ToDto(_settingsRepo.GetNotificationSettings()));

    [HttpPut("settings")]
    [ProducesResponseType(typeof(NotificationSettingsDto), StatusCodes.Status200OK)]
    public ActionResult<NotificationSettingsDto> SaveSettings([FromBody] NotificationSettingsDto request)
    {
        var settings = new NotificationSettings
        {
            NotifyService = string.IsNullOrWhiteSpace(request.NotifyService) ? null : request.NotifyService.Trim(),
            QuietHoursStartHour = NormalizeHour(request.QuietHoursStartHour),
            QuietHoursEndHour = NormalizeHour(request.QuietHoursEndHour),
            Thresholds = request.Thresholds,
            Calibration = request.Calibration,
            Maintenance = request.Maintenance,
            SensorOffline = request.SensorOffline,
            Risks = request.Risks,
            SystemWatch = request.SystemWatch,
            DailyDigest = request.DailyDigest,
            DigestHour = request.DigestHour is >= 0 and <= 23 ? request.DigestHour : 6,
            DigestMinute = request.DigestMinute is >= 0 and <= 59 ? request.DigestMinute : 0,
            DigestDetailed = request.DigestDetailed,
        };

        _settingsRepo.SaveNotificationSettings(settings);
        return Ok(ToDto(settings));
    }

    /// <summary>Lists the Home Assistant notify services the user can push to.</summary>
    [HttpGet("notify-services")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<string>>> NotifyServices(CancellationToken cancellationToken)
    {
        var settings = _repository.GetEffectiveHomeAssistantSettings();
        return Ok(await _homeAssistant.GetNotifyServicesAsync(settings, cancellationToken));
    }

    /// <summary>Sends a test push (to the provided service, or the saved one) so the user can confirm it works.</summary>
    [HttpPost("test")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Test([FromBody] NotificationTestRequest request, CancellationToken cancellationToken)
    {
        var service = string.IsNullOrWhiteSpace(request.NotifyService)
            ? _settingsRepo.GetNotificationSettings().NotifyService
            : request.NotifyService.Trim();

        if (string.IsNullOrWhiteSpace(service))
        {
            return Ok(new { ok = false, message = "Kein Push-Dienst gewählt." });
        }

        var settings = _repository.GetEffectiveHomeAssistantSettings();
        // Mit Ziel, damit der Test auch den Tipp auf die Meldung prüft — der
        // führte bis forkai.167 auf „404: Not Found", und kein Test sah es.
        var sent = await _homeAssistant.SendNotificationAsync(
            settings, service, "🌱 Grow OS", "Test-Benachrichtigung — deine Push-Nachrichten sind richtig eingerichtet.", cancellationToken,
            await _notifications.StartPfadAsync(cancellationToken));

        return Ok(new { ok = sent });
    }

    /// <summary>
    /// Runs the real calibration-reminder path now and reports the outcome, so the user can
    /// verify sensor reminders actually reach the phone — and understand why one didn't
    /// (no phone saved, category off, quiet hours, or nothing due).
    /// </summary>
    [HttpPost("test-calibration")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> TestCalibration(CancellationToken cancellationToken)
    {
        var settings = _settingsRepo.GetNotificationSettings();
        var configured = settings.IsConfigured;
        var categoryEnabled = settings.IsCategoryEnabled(NotificationCategory.Calibration);
        var quietNow = settings.IsQuietHour(DateTime.Now.Hour);
        var due = _hardware.GetDueCalibrationEvents(DateTime.UtcNow)
            .Where(e => e.Status == CalibrationEventStatus.Planned)
            .ToList();

        bool sent = false;
        string message;
        if (!configured)
        {
            message = "Kein Push-Handy gespeichert. Trag oben deinen Push-Dienst ein und speichere — sonst kommt keine Erinnerung an.";
        }
        else if (!categoryEnabled)
        {
            message = "„Kalibrierung fällig“ ist ausgeschaltet. Schalte es unten ein, damit Erinnerungen rausgehen.";
        }
        else if (quietNow)
        {
            message = "Gerade ist Ruhezeit — jetzt würde keine Erinnerung gesendet. Deine Einrichtung ist aber in Ordnung.";
        }
        else
        {
            var body = CalibrationReminderService.BuildDueMessage(due)
                ?? "Test: So sieht eine Kalibrierungs-Erinnerung aus. Aktuell ist nichts fällig.";
            sent = await _notifications.SendAsync(NotificationCategory.Calibration, "🌱 Grow OS · Kalibrierung", body, cancellationToken);
            message = sent
                ? (due.Count > 0
                    ? $"{due.Count} Kalibrierung(en) fällig — Erinnerung ans Handy gesendet."
                    : "Test-Erinnerung ans Handy gesendet — Push funktioniert.")
                : "Einrichtung sieht ok aus, aber Home Assistant hat den Push nicht angenommen. Stimmt der Dienstname?";
        }

        return Ok(new
        {
            ok = sent,
            configured,
            categoryEnabled,
            quietNow,
            dueCount = due.Count,
            message,
        });
    }

    /// <summary>Current watchdog verdict — is the monitoring itself alive? Includes the per-tent pulse.</summary>
    [HttpGet("watchdog")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Watchdog([FromServices] WatchdogService watchdog)
    {
        var nowUtc = DateTime.UtcNow;
        var report = watchdog.Inspect(nowUtc);
        var verdict = report.Verdict;
        return Ok(new
        {
            code = verdict.Code,
            headline = verdict.Headline,
            detail = verdict.Detail,
            isProblem = verdict.IsProblem,
            // Nur überwachte Zelte: ohne gemappte Sensoren gibt es keinen Puls zu zeigen.
            tents = report.Tents
                .Where(tent => tent.MappedSensorCount > 0)
                .Select(tent =>
                {
                    var minutes = tent.NewestReadingUtc is { } newest
                        ? (int?)Math.Max(0, (int)Math.Round((nowUtc - newest).TotalMinutes))
                        : null;
                    return new
                    {
                        name = tent.Name,
                        minutesSinceData = minutes,
                        stale = minutes is null || minutes > WatchdogService.NoDataMinutes,
                    };
                }),
        });
    }

    /// <summary>Sends the current watchdog state to the phone, so the path can be proven.</summary>
    [HttpPost("watchdog/test")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> WatchdogTest([FromServices] WatchdogService watchdog, CancellationToken cancellationToken)
    {
        var verdict = watchdog.Inspect(DateTime.UtcNow).Verdict;
        var settings = _settingsRepo.GetNotificationSettings();
        if (!settings.IsConfigured)
        {
            return Ok(new { ok = false, code = verdict.Code, message = "Kein Push-Handy gespeichert. Trag oben deinen Push-Dienst ein und speichere." });
        }

        if (!settings.IsCategoryEnabled(NotificationCategory.System))
        {
            return Ok(new { ok = false, code = verdict.Code, message = "Systemüberwachung ist ausgeschaltet — schalte sie unten ein." });
        }

        var sent = await _notifications.SendAsync(
            NotificationCategory.System, "🌱 Grow OS · Systemtest", $"{verdict.Headline}: {verdict.Detail}", cancellationToken,
            // Derselbe Weg wie die echte Watchdog-Meldung (WatchdogService) — sonst belegt der Test einen anderen.
            seite: NotificationService.LiveSeite());
        return Ok(new
        {
            ok = sent,
            code = verdict.Code,
            message = sent
                ? $"Gesendet — auf dem Handy steht: „{verdict.Headline}“."
                : "Home Assistant hat die Nachricht nicht angenommen. Stimmt der Dienstname?",
        });
    }

    private static int? NormalizeHour(int? hour) => hour is >= 0 and <= 23 ? hour : null;

    private static NotificationSettingsDto ToDto(NotificationSettings settings) => new(
        settings.NotifyService,
        settings.QuietHoursStartHour,
        settings.QuietHoursEndHour,
        settings.Thresholds,
        settings.Calibration,
        settings.Maintenance,
        settings.SensorOffline,
        settings.Risks,
        settings.SystemWatch,
        settings.DailyDigest,
        settings.DigestHour,
        settings.DigestMinute,
        settings.DigestDetailed);
}
