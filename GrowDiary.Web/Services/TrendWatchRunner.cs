using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services.Knowledge;

namespace GrowDiary.Web.Services;

/// <summary>
/// Runs the holiday guard over every active grow and pushes what it finds.
///
/// Edge-triggered on purpose: a drift that lasts a week is one message, not ten thousand.
/// The state lives in AppSettings rather than memory so a restart doesn't re-announce
/// everything that was already reported.
/// </summary>
public sealed class TrendWatchRunner
{
    private const string StateKeyPrefix = "trendwatch:seen:";

    private readonly GrowRepository _repository;
    private readonly TargetValueService _targets;
    // Fork AI (forkai.107): fuer die Wochenspalte des Feedcharts — ohne die
    // Wissensbasis kann Zielband.FuerGrow sie nicht auflegen.
    private readonly KnowledgeBaseLoader _wissen;
    private readonly NotificationService _notifications;
    private readonly AppSettingsRepository _settings;
    private readonly ILogger<TrendWatchRunner> _logger;

    public TrendWatchRunner(
        GrowRepository repository,
        TargetValueService targets,
        KnowledgeBaseLoader wissen,
        NotificationService notifications,
        AppSettingsRepository settings,
        ILogger<TrendWatchRunner> logger)
    {
        _repository = repository;
        _targets = targets;
        _wissen = wissen;
        _notifications = notifications;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>The current findings for one grow, without notifying — used by the API.</summary>
    public IReadOnlyList<TrendFinding> Inspect(int growId, DateTime now)
    {
        var grow = _repository.GetGrow(growId);
        if (grow is null)
        {
            return [];
        }

        var measurements = _repository.GetMeasurementsForGrow(growId);
        /* Die Phase von HEUTE, nicht die Aufschrift der letzten Messung.
           Bis zum 02.09.2026 stand hier `...FirstOrDefault()?.Stage`. Was auf
           einer Messzeile steht, beschreibt DIESE Messung; nach einem Flip, an
           dem niemand von Hand gemessen hat, urteilte der Waechter wochenlang
           gegen die Veg-Baender — und schickt dabei Nachrichten aufs Telefon. */
        var stage = GrowStageResolver.Resolve(grow, now.Date);
        // Die Profil-Kette Grow -> System -> Anbaustil, nicht die Abkuerzung.
        //
        // `GetTargets(HydroStyle, stage)` landet immer beim Standardprofil und
        // uebergeht damit das eigene Profil des Nutzers. Genau dieser Fehler
        // stand in der Diagnose und hat dort EC 0,6-0,8 gemeldet, waehrend die
        // Live-Kachel fuer denselben Grow 0,9-1,1 sagte.
        return TrendWatchService.Evaluate(
            measurements, ZieleFuer(grow, stage), now, _repository.GetChangeoutsForGrow(growId));
    }

    /// <summary>Die Sollwerte über die volle Profil-Kette.</summary>
    /// <remarks>
    /// Fork AI (forkai.107): ueber <see cref="Zielband.FuerGrow"/> statt direkt
    /// ueber das Phasenprofil. Vorher urteilte der Waechter gegen das Band der
    /// PHASE, waehrend Live-Kachel, Messprotokoll und Mischplan laengst die
    /// Wochenspalte des Feedcharts lasen — bei EC 1,4 in Bluetewoche 4 gegen
    /// ein Blueteband von 1,0-1,2 sind das Push-Nachrichten fuer einen Wert,
    /// der genau im Plan liegt. Eigene Grenzwerte bleiben aussen vor (null),
    /// wie bei der Alarmauswertung: der Waechter beurteilt den Plan.
    /// </remarks>
    private HydroTargetValues? ZieleFuer(GrowRun grow, GrowStage stage)
        => Zielband.FuerGrow(
            _targets,
            _wissen,
            grow,
            stage,
            grow.SystemId is { } systemId ? _repository.GetSystem(systemId)?.SetpointProfileId : null,
            null);

    public async Task RunAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        foreach (var grow in _repository.GetActiveGrows())
        {
            try
            {
                await RunForGrowAsync(grow, now, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Trend-Wächter fehlgeschlagen: Grow {GrowId}.", grow.Id);
            }
        }
    }

    private async Task RunForGrowAsync(GrowRun grow, DateTime now, CancellationToken cancellationToken)
    {
        var findings = Inspect(grow.Id, now);
        var key = StateKeyPrefix + grow.Id;
        var previous = (_settings.GetValue(key) ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

        // Info-level findings are for the screen; only something worth acting on is worth
        // interrupting someone's holiday for.
        var pushWorthy = findings.Where(finding => finding.Severity >= TrendSeverity.Warning).ToList();

        /* Gemerkt wird erst, wenn es RAUS ist.
           Bis zum 01.09.2026 wanderte jeder Befund in die Merkstelle, ohne dass
           jemand das Ergebnis von SendAsync angesehen hat. Der Dienst gibt aber
           false zurueck, wenn Ruhezeit ist, die Kategorie aus steht oder Home
           Assistant den Aufruf nicht annimmt. Kippt der EC um 23:10 ueber das
           Band und steht die Ruhezeit auf 22-07, galt der Befund danach als
           gemeldet — und weil er sich nicht mehr aendert, kam der Push NIE.
           Der Waechter, den es fuer die Abwesenheit gibt, schwieg dauerhaft.

           Dieselbe Reparatur wie im PumpWatchNotifier und im WatchdogService. */
        var gemeldet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var finding in pushWorthy)
        {
            // Was schon in der Merkstelle steht, bleibt darin: es wurde bereits
            // zugestellt, und ein zweiter Push waere Laerm.
            if (previous.Contains(finding.Code))
            {
                gemeldet.Add(finding.Code);
                continue;
            }

            var raus = await _notifications.SendAsync(
                NotificationCategory.Risk,
                $"{grow.Name}: {finding.Headline}",
                finding.Detail,
                cancellationToken);

            if (raus) gemeldet.Add(finding.Code);
        }

        if (!gemeldet.SetEquals(previous))
        {
            _settings.SetValue(key, string.Join(',', gemeldet));
        }
    }
}
