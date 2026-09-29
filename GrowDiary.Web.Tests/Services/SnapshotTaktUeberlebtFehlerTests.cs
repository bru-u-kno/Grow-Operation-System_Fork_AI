using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Ein Fehler in einem Schritt des Snapshot-Takts beendet nicht das Add-on.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (29.09.2026).</b> Die Schleife des
/// <see cref="HomeAssistantSnapshotWorker"/> lief ohne <c>try/catch</c>. Nach
/// der Vorgabe von .NET beendet eine Ausnahme in einem Hintergrunddienst den
/// ganzen Prozess — eine gesperrte SQLite-Datei während einer Sicherung
/// reichte. Mit dem Add-on fielen Alarme, Wächter und Dosier-Riegel.</para>
/// <para>Hier scheitert JEDER Schritt: der Dienstanbieter ist leer, jedes
/// <c>GetRequiredService</c> wirft. Der Takt muss trotzdem durchlaufen und alle
/// fälligen Schritte versuchen.</para>
/// </remarks>
public sealed class SnapshotTaktUeberlebtFehlerTests
{
    /// <summary>Zählt, was als Fehler geloggt wurde.</summary>
    private sealed class Mitschrift : ILogger<HomeAssistantSnapshotWorker>
    {
        public List<string> Fehler { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error) Fehler.Add(formatter(state, exception));
        }
    }

    [Fact]
    public async Task JederSchrittScheitert_DerTaktLaeuftTrotzdemDurch()
    {
        var mitschrift = new Mitschrift();
        var leer = new ServiceCollection().BuildServiceProvider();
        var worker = new HomeAssistantSnapshotWorker(leer, mitschrift, new AppPaths(Path.GetTempPath()));

        // 09:00: Erfassen, Tagesstatistik, Kalibrier-Erinnerung und Tagesbericht sind fällig.
        var ausnahme = await Record.ExceptionAsync(
            () => worker.DurchlaufAsync(new DateTime(2026, 9, 29, 9, 0, 0), CancellationToken.None));

        Assert.Null(ausnahme);
        // Selbsttest: die Schritte liefen wirklich — und scheiterten jeder für sich.
        Assert.Equal(4, mitschrift.Fehler.Count);
        Assert.Contains(mitschrift.Fehler, f => f.Contains("Messwerte erfassen"));
        Assert.Contains(mitschrift.Fehler, f => f.Contains("Tagesstatistik"));
    }

    [Fact]
    public async Task GescheiterteTagesstatistik_WirdImNaechstenTaktWiederholt()
    {
        var mitschrift = new Mitschrift();
        var leer = new ServiceCollection().BuildServiceProvider();
        var worker = new HomeAssistantSnapshotWorker(leer, mitschrift, new AppPaths(Path.GetTempPath()));
        var neun = new DateTime(2026, 9, 29, 9, 0, 0);

        await worker.DurchlaufAsync(neun, CancellationToken.None);
        await worker.DurchlaufAsync(neun.AddMinutes(5), CancellationToken.None);

        Assert.Equal(2, mitschrift.Fehler.Count(f => f.Contains("Tagesstatistik")));
        // Die Erinnerung dagegen nur einmal — sonst käme sie alle fünf Minuten.
        Assert.Equal(1, mitschrift.Fehler.Count(f => f.Contains("Kalibrier-Erinnerung")));
    }
}
