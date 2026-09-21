namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI: taktet die Übergabe der Wochenwerte an Home Assistant.
/// </summary>
/// <remarks>
/// <para><b>Zwei Anlässe, kein Dauerlauf.</b> Geschrieben wird beim Wechsel der
/// Plan-Woche und einmal täglich um 06:00 Ortszeit. Ein laufender Abgleich würde
/// sich mit den Automatisierungen in HA um dieselben Helfer streiten — dasselbe
/// Muster, das beim Water Chiller schon einmal zur Doppelsteuerung führte. 06:00
/// liegt vor Licht-an, Änderungen greifen also zum Tagesbeginn.</para>
///
/// <para><b>Der Takt selbst ist eng, die Arbeit selten.</b> Alle fünf Minuten wird
/// nur geprüft, ob einer der beiden Anlässe vorliegt; der Wochenwechsel soll nicht
/// bis zum nächsten Morgen warten.</para>
/// </remarks>
public sealed class WochenplanSyncWorker : BackgroundService
{
    private static readonly TimeSpan Takt = TimeSpan.FromMinutes(5);
    private const int TagesstundeLokal = 6;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WochenplanSyncWorker> _logger;
    private DateTime _letzterTageslauf = DateTime.MinValue;

    public WochenplanSyncWorker(IServiceProvider serviceProvider, ILogger<WochenplanSyncWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Erst hochfahren lassen: Wissensdateien, HA-Verbindung und Repositories
        // sind in den ersten Sekunden noch nicht verlässlich da.
        try { await Task.Delay(TimeSpan.FromSeconds(100), stoppingToken); }
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
                _logger.LogWarning(ex, "Wochenplan-Übergabe: Durchlauf fehlgeschlagen.");
            }

            try { await Task.Delay(Takt, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task EinmalAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var dienst = scope.ServiceProvider.GetRequiredService<WochenplanSyncService>();

        var jetzt = DateTime.Now;
        var tageslaufFaellig = jetzt.Hour >= TagesstundeLokal && _letzterTageslauf.Date < jetzt.Date;
        var wochenwechsel = dienst.Wochenwechsel();

        if (!tageslaufFaellig && !wochenwechsel) return;

        var geschrieben = await dienst.UebergebenAsync(ct);

        // Fork AI (forkai.129): Temperatur max. des Entfeuchters folgt, wo so
        // eingestellt, der Plan-Luft — zum selben Takt wie die übrigen Sollwerte.
        geschrieben += await scope.ServiceProvider.GetRequiredService<EntfeuchterSteuerungService>().PlanNachziehenAsync(ct);
        if (tageslaufFaellig) _letzterTageslauf = jetzt;

        if (geschrieben > 0)
        {
            _logger.LogInformation(
                "Wochenplan: {Anzahl} Sollwerte an Home Assistant übergeben ({Anlass}).",
                geschrieben, wochenwechsel ? "Wochenwechsel" : "Tageslauf");
        }
    }
}
