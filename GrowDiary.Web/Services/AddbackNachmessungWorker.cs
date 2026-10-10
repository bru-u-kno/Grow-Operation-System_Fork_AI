namespace GrowDiary.Web.Services;

/// <summary>
/// Der Takt der automatischen Nachmessung nach einem Nachfüllen: alle 30 Sekunden prüfen, ob eine
/// fällig ist. Ist keine offen, kostet der Takt eine kleine Abfrage an die Datenbank.
/// </summary>
/// <remarks>
/// Nach einem Neustart sind alle während des Ausfalls fällig gewordenen Aufträge sofort dran —
/// <see cref="AddbackNachmessungService"/> liest die Werte zur Fälligkeit aus dem Sensorverlauf,
/// nicht zur Ausführung.
/// </remarks>
public sealed class AddbackNachmessungWorker : BackgroundService
{
    public static readonly TimeSpan Takt = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AddbackNachmessungWorker> _log;

    public AddbackNachmessungWorker(IServiceScopeFactory scopes, ILogger<AddbackNachmessungWorker> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Takt);
        do
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var geschlossen = scope.ServiceProvider.GetRequiredService<AddbackNachmessungService>().FaelligeAbarbeiten(DateTime.UtcNow);
                if (geschlossen > 0) _log.LogInformation("Nachfüllen: {Anzahl} Nachmessung(en) abgeschlossen.", geschlossen);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // Nicht fatal: der Auftrag bleibt offen und kommt im nächsten Takt wieder dran.
                _log.LogError(ex, "Nachfüllen: automatische Nachmessung fehlgeschlagen.");
            }
        }
        while (await WarteAsync(timer, stoppingToken));
    }

    private static async Task<bool> WarteAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
