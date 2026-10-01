using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>One tent's pulse: how many sensors are mapped, and when data last arrived.</summary>
public sealed record WatchdogTentPulse(string Name, int MappedSensorCount, DateTime? NewestReadingUtc);

/// <summary>What the watchdog looked at.</summary>
public sealed record WatchdogInput(
    bool HomeAssistantConfigured,
    DateTime? LastSnapshotRunUtc,
    DateTime? LastHomeAssistantSuccessUtc,
    string? LastHomeAssistantError,
    IReadOnlyList<WatchdogTentPulse> Tents,
    /// <summary>Wann der Prozess startete — vorher hat er nichts versäumt.</summary>
    DateTime? StartedAtUtc = null);

/// <summary>
/// What it concluded. <see cref="ChangeKey"/> is the identity of the state for
/// notification dedup: for tent outages it carries WHICH tents are dark, so a second
/// tent going dark is a new state and gets its own push instead of hiding in the old one.
/// </summary>
public sealed record WatchdogVerdict(string Code, string Headline, string Detail, bool IsProblem, string ChangeKey);

/// <summary>The verdict plus the per-tent pulses it was based on — for the status endpoint.</summary>
public sealed record WatchdogReport(WatchdogVerdict Verdict, IReadOnlyList<WatchdogTentPulse> Tents);

/// <summary>
/// The dead-man's switch: a normal alert says "this value is wrong", the watchdog says
/// "I cannot see anything at all right now". Without it, silence is ambiguous — it could
/// mean everything is fine, or that monitoring itself has stopped.
///
/// The pulse is per tent. A global "newest reading anywhere" hid the case where one tent
/// goes dark while another keeps reporting — exactly the outage a multi-tent owner never
/// notices, because the app still shows fresh numbers somewhere.
/// </summary>
public sealed class WatchdogService
{
    public const string Ok = "ok";
    public const string Idle = "idle";
    public const string WorkerStalled = "worker_stalled";
    public const string HaUnreachable = "ha_unreachable";
    public const string NoData = "no_data";
    public const string TentDark = "tent_dark";
    public const string Starting = "starting";

    // The snapshot worker loops every 5 minutes; three missed rounds is a real problem,
    // not a hiccup.
    public const int StalledMinutes = 16;
    public const int NoDataMinutes = 21;

    private readonly GrowRepository _repository;
    private readonly SensorReadingRepository _readings;
    private readonly SystemHeartbeat _heartbeat;
    private readonly NotificationService _notifications;
    private readonly AnlagenRisikoService _risiken;
    private readonly ILogger<WatchdogService> _logger;

    public WatchdogService(
        GrowRepository repository,
        SensorReadingRepository readings,
        SystemHeartbeat heartbeat,
        NotificationService notifications,
        AnlagenRisikoService risiken,
        ILogger<WatchdogService> logger)
    {
        _repository = repository;
        _readings = readings;
        _heartbeat = heartbeat;
        _notifications = notifications;
        _risiken = risiken;
        _logger = logger;
    }

    /// <summary>Pure verdict logic, so every branch can be tested without a database.</summary>
    public static WatchdogVerdict Evaluate(WatchdogInput input, DateTime nowUtc)
    {
        if (!input.HomeAssistantConfigured)
        {
            return new WatchdogVerdict(Idle, "Nicht überwacht",
                "Home Assistant ist noch nicht verbunden — es gibt nichts zu überwachen.", false, Idle);
        }

        var watched = input.Tents.Where(tent => tent.MappedSensorCount > 0).ToList();
        if (watched.Count == 0)
        {
            return new WatchdogVerdict(Idle, "Nicht überwacht",
                "Noch kein Sensor zugeordnet — ordne auf der Home-Assistant-Seite Entitäten zu.", false, Idle);
        }

        var snapshotAge = Age(input.LastSnapshotRunUtc, nowUtc);
        if (snapshotAge is null || snapshotAge > TimeSpan.FromMinutes(StalledMinutes))
        {
            // Frisch gestartet und noch keine Runde gedreht ist kein Stillstand,
            // sondern der Anfang. Ohne diese Ausnahme schlug der Watchdog nach
            // jedem Neustart und jedem Update erst einmal Alarm.
            if (input.LastSnapshotRunUtc is null
                && Age(input.StartedAtUtc, nowUtc) is { } seitStart
                && seitStart <= TimeSpan.FromMinutes(StalledMinutes))
            {
                return new WatchdogVerdict(Starting, "Startet gerade",
                    "Grow OS ist eben hochgefahren und hat noch keine Runde gedreht.", false, Starting);
            }

            return new WatchdogVerdict(WorkerStalled, "Überwachung steht",
                "Grow OS hat seit über 15 Minuten keine Runde mehr gedreht. Ein Neustart des Add-ons behebt das meist.", true, WorkerStalled);
        }

        var haAge = Age(input.LastHomeAssistantSuccessUtc, nowUtc);
        if (haAge is null || haAge > TimeSpan.FromMinutes(StalledMinutes))
        {
            var reason = string.IsNullOrWhiteSpace(input.LastHomeAssistantError) ? string.Empty : $" ({input.LastHomeAssistantError})";
            return new WatchdogVerdict(HaUnreachable, "Home Assistant antwortet nicht",
                $"Seit über 15 Minuten kommen keine Werte aus Home Assistant{reason}. Solange sind Grenzwert-Alarme blind.", true, HaUnreachable);
        }

        var dark = watched.Where(tent => IsDark(tent, nowUtc)).ToList();

        if (dark.Count == watched.Count)
        {
            return new WatchdogVerdict(NoData, "Keine neuen Messwerte",
                "Die Verbindung steht, aber deine Sensoren liefern seit über 20 Minuten nichts Neues. Prüfe die Geräte in Home Assistant.", true, NoData);
        }

        if (dark.Count > 0)
        {
            // Some tents report, some are dark — the case a global check could not see.
            var parts = dark.Select(tent => DarkLabel(tent, nowUtc)).ToList();
            var detail = dark.Count == 1
                ? $"{parts[0]} — die übrigen Zelte melden normal. Prüfe die Geräte in Home Assistant."
                : $"{string.Join(" · ", parts)} — die übrigen Zelte melden normal. Prüfe die Geräte in Home Assistant.";
            var changeKey = $"{TentDark}:{string.Join(",", dark.Select(tent => tent.Name).OrderBy(name => name, StringComparer.Ordinal))}";
            return new WatchdogVerdict(TentDark,
                dark.Count == 1 ? $"Zelt „{dark[0].Name}\" ist dunkel" : $"{dark.Count} Zelte sind dunkel",
                detail, true, changeKey);
        }

        var freshest = watched
            .Select(tent => Age(tent.NewestReadingUtc, nowUtc)!.Value)
            .Min();
        var minutes = (int)Math.Round(freshest.TotalMinutes);
        return new WatchdogVerdict(Ok, "Alles wach",
            minutes <= 1 ? "Letzte Sensordaten gerade eben." : $"Letzte Sensordaten vor {minutes} Minuten.", false, Ok);
    }

    private static bool IsDark(WatchdogTentPulse tent, DateTime nowUtc)
        => Age(tent.NewestReadingUtc, nowUtc) is not { } age || age > TimeSpan.FromMinutes(NoDataMinutes);

    private static string DarkLabel(WatchdogTentPulse tent, DateTime nowUtc)
        => Age(tent.NewestReadingUtc, nowUtc) is { } age
            ? $"Zelt „{tent.Name}\" liefert seit {(int)Math.Round(age.TotalMinutes)} Minuten nichts Neues"
            : $"Zelt „{tent.Name}\" hat noch nie Werte geliefert";

    private static TimeSpan? Age(DateTime? timestampUtc, DateTime nowUtc)
        => timestampUtc is { } value ? nowUtc - value : null;

    /// <summary>Collects the current state without notifying — used by the status endpoint.</summary>
    public WatchdogReport Inspect(DateTime nowUtc)
    {
        var settings = _repository.GetEffectiveHomeAssistantSettings();
        var pulses = _repository.GetTents()
            .Select(tent => new WatchdogTentPulse(
                tent.Name,
                tent.Sensors.Count(sensor => sensor.IsActive && !string.IsNullOrWhiteSpace(sensor.HaEntityId)),
                _readings.GetNewestReadingUtc(tent.Id)))
            .ToList();

        var (snapshotRun, haSuccess, haError) = _heartbeat.Read();
        var verdict = Evaluate(
            new WatchdogInput(settings.IsConfigured, snapshotRun, haSuccess, haError, pulses, _heartbeat.StartedAtUtc),
            nowUtc);
        return new WatchdogReport(verdict, pulses);
    }

    /// <summary>
    /// Checks and pushes once per state change: one message when something breaks, one when
    /// it recovers. Never repeats the same complaint — but a DIFFERENT set of dark tents is
    /// a new state, not a repetition.
    /// </summary>
    public async Task<WatchdogVerdict> CheckAndNotifyAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var verdict = Inspect(nowUtc).Verdict;
        var schritt = Entscheiden(verdict.IsProblem, _heartbeat.NotifiedCode, verdict.ChangeKey);

        if (schritt.Warnen)
        {
            var sent = await _notifications.SendAsync(
                NotificationCategory.System, "🌱 Grow OS · Systemwarnung", $"{verdict.Headline}: {verdict.Detail}", cancellationToken);
            if (sent)
            {
                _heartbeat.NotifiedCode = verdict.ChangeKey;
                _logger.LogWarning("Watchdog: {Code} — {Detail}", verdict.Code, verdict.Detail);
            }

            // Auch in die App. Vorher blieb von einem Ausfall der Verbindung nur
            // eine Push-Nachricht — und der Notfall-Ablauf, der genau an diesem
            // Ereignistyp haengt, konnte nie vorgeschlagen werden.
            _risiken.Melden(
                RiskEventType.HomeAssistantUnavailable,
                RiskEventSeverity.Critical,
                null,
                verdict.Headline,
                verdict.Detail,
                verdict.Code);
        }

        if (schritt.RisikoSchliessen)
        {
            _risiken.Entwarnen(RiskEventType.HomeAssistantUnavailable, null);
        }

        if (schritt.EntwarnungSenden)
        {
            await _notifications.SendAsync(
                NotificationCategory.System, "🌱 Grow OS · Entwarnung", "Die Überwachung läuft wieder — Sensordaten kommen an.", cancellationToken);
            _heartbeat.NotifiedCode = null;
            _logger.LogInformation("Watchdog: wieder normal.");
        }

        return verdict;
    }

    /// <summary>Was ein Takt tut — rein, damit jeder Zweig ohne Datenbank prüfbar ist.</summary>
    /// <remarks>
    /// Fork AI (Prüfung 01.10.2026): Das Risiko „Home Assistant nicht erreichbar"
    /// wurde vorher nur geschlossen, wenn vorher ein Push RAUSGEGANGEN war
    /// (<c>NotifiedCode</c> gesetzt). Ohne eingerichtete Benachrichtigung, mit
    /// abgeschalteter Kategorie, in der Ruhezeit oder nach einem Neustart
    /// (<c>NotifiedCode</c> lebt nur im Speicher) blieb ein kritisches Risiko
    /// für immer offen. Das Risiko folgt jetzt dem Zustand, nur der Push hängt
    /// an der Merkmarke.
    /// </remarks>
    public static WatchdogSchritt Entscheiden(bool istProblem, string? gemeldet, string aenderungsSchluessel)
        => istProblem
            ? new WatchdogSchritt(Warnen: gemeldet != aenderungsSchluessel, RisikoSchliessen: false, EntwarnungSenden: false)
            : new WatchdogSchritt(Warnen: false, RisikoSchliessen: true, EntwarnungSenden: gemeldet is not null);
}

/// <summary>Die Handlungen eines Watchdog-Takts.</summary>
public sealed record WatchdogSchritt(bool Warnen, bool RisikoSchliessen, bool EntwarnungSenden);
