using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Single gateway for every push Grow OS sends. Callers say what category a message is;
/// this checks the central settings (a notify service is configured, the category is on,
/// and it is not quiet hours) and then pushes through Home Assistant.
/// </summary>
public sealed class NotificationService
{
    private readonly NotificationSettingsRepository _settingsRepo;
    private readonly GrowRepository _growRepository;
    private readonly HomeAssistantService _homeAssistant;
    private readonly ILogger<NotificationService> _logger;

    private readonly SupervisorInfoService? _supervisor;

    public NotificationService(
        NotificationSettingsRepository settingsRepo,
        GrowRepository growRepository,
        HomeAssistantService homeAssistant,
        ILogger<NotificationService> logger,
        SupervisorInfoService? supervisor = null)
    {
        _settingsRepo = settingsRepo;
        _growRepository = growRepository;
        _homeAssistant = homeAssistant;
        _logger = logger;
        _supervisor = supervisor;
    }

    /// <summary>Die Seite, auf der man die Sache erledigt, je Meldungsart.</summary>
    /// <remarks>
    /// Eine Warnung, die nur meldet, ist eine halbe Warnung — bisher landete
    /// jeder Tipp auf der Startseite von Home Assistant, und der Weg zur
    /// eigentlichen Stelle blieb Handarbeit.
    /// </remarks>
    private static string SeiteFuer(NotificationCategory category) => category switch
    {
        NotificationCategory.Calibration => "sensoren",
        NotificationCategory.Maintenance => "sensoren",
        NotificationCategory.SensorOffline => "sensoren",
        // Grenzwert, Risiko und Systemmeldung fuehren dorthin, wo das offene
        // Zeug steht — Aufgaben zeigt Risiken, Termine und Pumpen zusammen.
        _ => "aufgaben",
    };

    /// <summary>
    /// Der HA-interne Pfad zur Grow-OS-Seite, oder null wenn Grow OS nicht als
    /// Add-on laeuft (dann gibt es nichts, auf das man zeigen koennte).
    /// </summary>
    private async Task<string?> ZielPfadAsync(NotificationCategory category, CancellationToken ct)
    {
        if (_supervisor is null) return null;
        return SupervisorInfoService.PanelPath(await _supervisor.GetAddonSlugAsync(ct), SeiteFuer(category));
    }

    /// <summary>Der Link auf die Startseite (Live), für Tagesbericht und Testmeldung.</summary>
    public async Task<string?> StartPfadAsync(CancellationToken ct)
        => _supervisor is null ? null : SupervisorInfoService.PanelPath(await _supervisor.GetAddonSlugAsync(ct));

    public NotificationSettings GetSettings() => _settingsRepo.GetNotificationSettings();

    /// <summary>
    /// Sends a push if the category is enabled and it is not quiet hours. Returns false
    /// (silently) when notifications are unconfigured, the category is off, or it is quiet.
    /// </summary>
    /// <param name="trotzRuhezeit">
    /// Für Meldungen, die <b>nachts passieren</b> und morgens wertlos sind.
    /// </param>
    /// <remarks>
    /// <para><b>Der Anlass (01.09.2026).</b> Der Lichteinbruch-Wächter lief
    /// durch den Ruhezeit-Filter. Ein Blütezelt fährt 12/12 mit Licht aus um
    /// 20:00; die übliche Ruhezeit 22–07 überdeckt <b>neun der zwölf</b>
    /// Dunkelstunden. Der Alarm war also genau dann stumm, wofür es ihn
    /// gibt.</para>
    ///
    /// <para><b>Sparsam benutzen.</b> Die Ruhezeit ist dazu da, dass niemand um
    /// drei Uhr wegen eines EC-Trends geweckt wird. Sie zu übergehen ist nur
    /// richtig, wenn die Meldung <i>in</i> der Ruhezeit entsteht und bis zum
    /// Morgen wertlos wäre. Die Kategorie muss weiter eingeschaltet sein: wer
    /// eine Art Meldung ganz abstellt, meint das auch.</para>
    /// </remarks>
    public async Task<bool> SendAsync(NotificationCategory category, string title, string message, CancellationToken cancellationToken = default, bool trotzRuhezeit = false)
    {
        var settings = _settingsRepo.GetNotificationSettings();
        if (!settings.IsConfigured || !settings.IsCategoryEnabled(category))
        {
            return false;
        }

        if (!trotzRuhezeit && settings.IsQuietHour(DateTime.Now.Hour))
        {
            return false;
        }

        var haSettings = _growRepository.GetEffectiveHomeAssistantSettings();
        var ziel = await ZielPfadAsync(category, cancellationToken);
        var sent = await _homeAssistant.SendNotificationAsync(haSettings, settings.NotifyService!, title, message, cancellationToken, ziel);
        if (sent)
        {
            _logger.LogInformation("Benachrichtigung gesendet ({Category}): {Title}", category, title);
        }

        return sent;
    }

    /// <summary>
    /// Sends the daily digest. Unlike <see cref="SendAsync"/> this ignores quiet hours —
    /// the user picks the digest time deliberately, so it must arrive even at, say, 5:30.
    /// </summary>
    public async Task<bool> SendDigestAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        var settings = _settingsRepo.GetNotificationSettings();
        if (!settings.IsConfigured)
        {
            return false;
        }

        var haSettings = _growRepository.GetEffectiveHomeAssistantSettings();
        // Der Tagesbericht ist ein Rundumblick — er fuehrt auf die Live-Seite.
        var ziel = await StartPfadAsync(cancellationToken);
        return await _homeAssistant.SendNotificationAsync(haSettings, settings.NotifyService!, title, message, cancellationToken, ziel);
    }
}
