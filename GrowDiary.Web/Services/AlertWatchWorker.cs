using GrowDiary.Web.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace GrowDiary.Web.Services;

/// <summary>
/// A lightweight, dedicated loop that checks every tent's threshold rules once a minute,
/// so a per-minute repeat interval actually works. Unlike the snapshot worker (which runs
/// every 5 minutes and also stores readings), this only fetches live values and evaluates
/// alerts — it does not persist readings, keeping the minute cadence cheap.
/// </summary>
public sealed class AlertWatchWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AlertWatchWorker> _logger;

    public AlertWatchWorker(IServiceProvider serviceProvider, ILogger<AlertWatchWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small offset from the snapshot worker's start so they don't hammer HA in lockstep.
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Zuerst, und mit eigenem Fang: der Abgleich arbeitet nur in der lokalen
                // Datenbank und darf weder auf Home-Assistant-Aufrufe warten noch von
                // einem Fehler im Wächter ausgebremst werden, der weiter unten den
                // ganzen Durchlauf abbricht.
                RunRisikoAbgleich();
                await EvaluateAllTentsAsync(stoppingToken);
                await RunWatchdogAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Alarm-Wächter-Durchlauf fehlgeschlagen.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    /// <summary>
    /// Die Abweichungs-Karten reiten ebenfalls mit: gleicht ab, sobald es eine neue
    /// Messung gibt. Läuft vor den übrigen Schritten und fängt eigene Fehler, damit
    /// weder ein Fehler hier die Alarme aufhält noch ein Fehler dort den Abgleich.
    /// </summary>
    private void RunRisikoAbgleich()
    {
        try
        {
            _serviceProvider.GetRequiredService<RisikoAbgleichRunner>().Einmal();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Risiko-Abgleich im Wächter fehlgeschlagen.");
        }
    }

    /// <summary>The dead-man's switch rides along on the same minute tick.</summary>
    private async Task RunWatchdogAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var watchdog = scope.ServiceProvider.GetRequiredService<WatchdogService>();
        await watchdog.CheckAndNotifyAsync(DateTime.UtcNow, cancellationToken);

        // The trend guard looks at days, so a minute tick is far more often than needed —
        // but it is edge-triggered, so re-checking costs a query and sends nothing.
        var trends = scope.ServiceProvider.GetRequiredService<TrendWatchRunner>();
        await trends.RunAsync(DateTime.Now, cancellationToken);
    }

    private async Task EvaluateAllTentsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<GrowRepository>();
        var haService = scope.ServiceProvider.GetRequiredService<HomeAssistantService>();
        var alertEval = scope.ServiceProvider.GetRequiredService<AlertEvaluationService>();

        var settings = repository.GetEffectiveHomeAssistantSettings();
        if (!settings.IsConfigured)
        {
            return;
        }

        var pumpen = scope.ServiceProvider.GetRequiredService<PumpWatchNotifier>();

        foreach (var tent in repository.GetTents())
        {
            try
            {
                var states = await haService.GetStatesAsync(settings, tent, cancellationToken);
                await alertEval.EvaluateAsync(tent, states, cancellationToken);

                // Reitet auf denselben Zustaenden mit: sie sind gerade geholt,
                // und eine stehende Pumpe darf keine Minute laenger warten als
                // ein ueberschrittener Grenzwert.
                await pumpen.PruefenUndMeldenAsync(tent, states, DateTime.UtcNow, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Alarm-Auswertung im Wächter fehlgeschlagen: Zelt {TentId}.", tent.Id);
            }
        }
    }
}
