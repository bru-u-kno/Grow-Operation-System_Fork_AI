namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI (A-010, 07.10.2026): Der Takt des Probelaufs.
/// </summary>
/// <remarks>
/// <para>Beim Start zuerst: was noch im Eingriff steht, wird zurückgestellt (das Add-on wurde mitten
/// in einem Lauf beendet). Danach alle fünf Sekunden ein Schritt für jeden offenen Lauf — Messen, Grenzen
/// prüfen, Ende erkennen, Zurückstellen wiederholen, Nachlauf aufzeichnen.</para>
/// <para>Ist kein Lauf offen, kostet der Takt eine Abfrage an die Datenbank.</para>
/// </remarks>
public sealed class ProbelaufWorker : BackgroundService
{
    public static readonly TimeSpan Takt = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ProbelaufWorker> _log;

    public ProbelaufWorker(IServiceScopeFactory scopes, ILogger<ProbelaufWorker> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ProbelaufService>().OffeneBeiStartZurueckstellenAsync(stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            // Nicht fatal: ein nicht zurückgestellter Lauf bleibt offen und wird im Takt weiter versucht.
            _log.LogError(ex, "Probelauf: offene Läufe beim Start nicht zurückgestellt.");
        }

        using var timer = new PeriodicTimer(Takt);
        while (await SafeWaitAsync(timer, stoppingToken))
        {
            try
            {
                using var scope = _scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ProbelaufService>().TickAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _log.LogError(ex, "Probelauf: Takt fehlgeschlagen.");
            }
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
