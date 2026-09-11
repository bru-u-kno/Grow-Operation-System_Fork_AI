namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI (forkai.20): Hält den Tagesdatensatz der CO₂-Begasung nach und
/// schreibt das Planziel nach Home Assistant, sobald es sich ändert.
/// </summary>
/// <remarks>
/// <para><b>Zwei Takte.</b> Alle zwei Minuten der Tageslauf (Tag anlegen,
/// „Ziel erreicht" merken, nach Licht-aus abschließen). Einmal in der Stunde
/// die Sollwerte nach HA — nicht, weil sie sich ständig ändern, sondern weil
/// bei Ziel-Quelle <c>plan</c> mit der Blütewoche ein neues Wochenziel kommt,
/// das niemand von Hand einträgt.</para>
///
/// <para><b>Ohne Einstellungen passiert nichts.</b> Solange der Nutzer die
/// Seite nie gespeichert hat, schreibt der Worker keine Sollwerte — sonst
/// überschriebe der Fork beim ersten Start die Werte, die in HA schon stimmen.</para>
/// </remarks>
public sealed class Co2SyncWorker : BackgroundService
{
    private static readonly TimeSpan Takt = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Sollwerttakt = TimeSpan.FromHours(1);

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<Co2SyncWorker> _logger;
    private DateTime _letzterSollwertlauf = DateTime.MinValue;

    public Co2SyncWorker(IServiceProvider serviceProvider, ILogger<Co2SyncWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(80), stoppingToken); }
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
                _logger.LogWarning(ex, "CO₂-Steuerung: Durchlauf fehlgeschlagen.");
            }

            try { await Task.Delay(Takt, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task EinmalAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var dienst = scope.ServiceProvider.GetRequiredService<Co2SteuerungService>();
        var repo = scope.ServiceProvider.GetRequiredService<Infrastructure.SteuerungRepository>();

        await dienst.TaktAsync(ct);

        var gespeichert = repo.GetEinstellungen<Models.Co2Einstellungen>(Co2SteuerungService.Modul);
        if (gespeichert is null) return;

        if (DateTime.UtcNow - _letzterSollwertlauf >= Sollwerttakt)
        {
            _letzterSollwertlauf = DateTime.UtcNow;
            var ok = await dienst.NachHomeAssistantSchreibenAsync(gespeichert, ct);
            _logger.LogInformation("CO₂-Sollwerte nach Home Assistant geschrieben: {Ergebnis}.", ok ? "vollständig" : "unvollständig");
        }
    }
}
