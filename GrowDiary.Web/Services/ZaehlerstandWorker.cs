using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI (forkai.6): Hält den kWh-Zähler aus Home Assistant fest — einmal am
/// Tag, dazu sofort, wenn ein anderer Grow der laufende wird oder seine Phase
/// wechselt. Aus diesen Ständen rechnet die Kosten-Seite Strom je Grow und je
/// Phase.
/// </summary>
/// <remarks>
/// <para><b>Warum kein Hook am Grow-Speichern.</b> Ein Phasenwechsel entsteht
/// nicht nur durch Speichern: der <see cref="GrowStageResolver"/> rechnet ihn
/// aus dem Kalender (Sämling → Veg nach 14 Tagen, Übergang → Blüte nach 10).
/// Ein Takt, der den Stand von heute mit dem letzten gespeicherten vergleicht,
/// sieht beides — den Klick und den Kalender.</para>
///
/// <para><b>Ohne Quelle passiert nichts.</b> Erst wenn in der Kosten-Seite ein
/// Zähler gewählt ist, wird gelesen; das Original bleibt davon unberührt.</para>
/// </remarks>
public sealed class ZaehlerstandWorker : BackgroundService
{
    private static readonly TimeSpan Takt = TimeSpan.FromMinutes(10);

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ZaehlerstandWorker> _logger;

    public ZaehlerstandWorker(IServiceProvider serviceProvider, ILogger<ZaehlerstandWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(70), stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EinmalAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Zählerstand-Durchlauf fehlgeschlagen.");
            }

            try { await Task.Delay(Takt, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task EinmalAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var dienst = scope.ServiceProvider.GetRequiredService<KostenSeiteService>();
        var repo = scope.ServiceProvider.GetRequiredService<KostenRepository>();

        if (string.IsNullOrWhiteSpace(dienst.StromQuelle.ZaehlerEntityId)) return;

        var letzter = repo.GetLetzterZaehlerstand();
        var (growId, phase) = dienst.LaufenderGrow(DateTime.Today);

        var anlass = Entscheiden(letzter, growId, phase, DateTime.UtcNow);
        if (anlass is null) return;

        var stand = await dienst.ZaehlerstandFesthaltenAsync(anlass.Value, ct);
        if (stand is not null)
        {
            _logger.LogInformation("Zählerstand festgehalten: {Kwh} kWh ({Anlass}, Grow {GrowId}, Phase {Phase}).",
                stand.Kwh, stand.Anlass, stand.GrowId, stand.Phase);
        }
    }

    /// <summary>Ob und warum jetzt ein Stand fällig ist — rein, damit es prüfbar ist.</summary>
    public static ZaehlerAnlass? Entscheiden(Zaehlerstand? letzter, int? growId, string? phase, DateTime jetztUtc)
    {
        if (letzter is null) return ZaehlerAnlass.Manuell;
        if (letzter.GrowId != growId) return ZaehlerAnlass.GrowStart;
        if (growId is not null && !string.Equals(letzter.Phase, phase, StringComparison.Ordinal)) return ZaehlerAnlass.Phase;

        var letzterTag = letzter.ZeitpunktUtc.ToLocalTime().Date;
        var heute = jetztUtc.ToLocalTime().Date;
        return heute > letzterTag ? ZaehlerAnlass.Tag : null;
    }
}
