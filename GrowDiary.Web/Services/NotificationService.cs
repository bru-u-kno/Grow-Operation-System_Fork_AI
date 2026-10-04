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

    /// <summary>Die Seite, auf der die Meldung steht, je Meldungsart — wenn der Absender keine genauere kennt.</summary>
    /// <remarks>
    /// <para>Ziel ist die Stelle, an der man die Sache <b>sieht</b>. forkai.168
    /// schickte Grenzwert und Risiko auf „Aufgaben" — dort steht aber keine
    /// Grenzwert-Überschreitung und kein Trend-Befund. Bru bekam fast nur
    /// Grenzwert-Meldungen und landete damit immer auf einer Seite, die mit der
    /// Meldung nichts zu tun hatte (04.10.2026).</para>
    /// <para>Absender, die das Zelt kennen, geben es per <c>seite</c> mit
    /// (<see cref="LiveSeite"/>); diese Tabelle ist der Rückfall.</para>
    /// </remarks>
    public static string SeiteFuer(NotificationCategory category) => category switch
    {
        // Sensoren & Wartung: Kalibrier- und Wartungstermine.
        NotificationCategory.Calibration => "sensoren",
        NotificationCategory.Maintenance => "sensoren",
        // Live: die Kacheln zeigen den Wert ausserhalb des Bands, „Beobachtungen"
        // die Trend-Befunde, ein ausgefallener Sensor steht als „–" in seiner
        // Kachel und fehlt bei „N Sensoren live". Sensoren & Wartung zeigt nur
        // den von Hand gesetzten Status „Offline".
        NotificationCategory.SensorOffline => "live",
        NotificationCategory.Threshold => "live",
        NotificationCategory.Risk => "live",
        // Aufgaben: Pumpen-Lage, offene Risiken, Termine.
        NotificationCategory.System => "aufgaben",
        _ => "live",
    };

    /// <summary>
    /// Live mit diesem Zelt — <c>live/&lt;id&gt;</c>, ohne Zelt <c>live</c>.
    /// </summary>
    /// <remarks>
    /// Nie leer für die Startseite: HA gibt dann <c>route.path = ""</c> weiter,
    /// und das schickt es auch bei jeder Größenänderung. Die App könnte einen
    /// Tipp auf „Live" so nicht von einem gedrehten Handy unterscheiden und
    /// bliebe auf der Seite, die gerade offen ist.
    /// </remarks>
    public static string LiveSeite(int? zeltId = null) => zeltId is { } id ? $"live/{id}" : "live";

    /// <summary>
    /// Der HA-interne Pfad zur Grow-OS-Seite, oder null wenn Grow OS nicht als
    /// Add-on laeuft (dann gibt es nichts, auf das man zeigen koennte).
    /// </summary>
    private async Task<string?> ZielPfadAsync(string seite, CancellationToken ct)
    {
        if (_supervisor is null) return null;
        return SupervisorInfoService.PanelPath(await _supervisor.GetAddonSlugAsync(ct), seite);
    }

    /// <summary>Der Link auf Live, für Tagesbericht und Testmeldung.</summary>
    public Task<string?> StartPfadAsync(CancellationToken ct) => ZielPfadAsync(LiveSeite(), ct);

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
    /// <param name="seite">
    /// Die Seite der App, die der Tipp auf die Meldung öffnet (z. B.
    /// <c>live/3</c>). Leer = die Seite der Meldungsart (<see cref="SeiteFuer"/>).
    /// </param>
    public async Task<bool> SendAsync(NotificationCategory category, string title, string message, CancellationToken cancellationToken = default, bool trotzRuhezeit = false, string? seite = null)
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
        var ziel = await ZielPfadAsync(string.IsNullOrWhiteSpace(seite) ? SeiteFuer(category) : seite, cancellationToken);
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
