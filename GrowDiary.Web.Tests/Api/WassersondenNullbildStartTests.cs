using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Die Startbereinigung der Nullbilder läuft über den echten Container der App.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (Prüfer, 03.10.2026).</b> Der Aufruf in <c>Program.cs</c>
/// holte <see cref="SensorReadingRepository"/> (scoped) aus dem
/// Wurzel-Container. In Development — Tor-Backend, lokaler Start, diese
/// Testumgebung — wirft das; die Nullbilder blieben stehen, und kein Test sah es,
/// weil die Regel nur mit einer selbst gebauten Ablage geprüft war.</para>
/// </remarks>
[Collection(IntegrationsSammlung.Name)]
public sealed class WassersondenNullbildStartTests
{
    private readonly IntegrationsApp _app;

    public WassersondenNullbildStartTests(IntegrationsApp app) => _app = app;

    [Fact]
    public void DerStartaufrufEntferntEinNullbildUeberDenEchtenContainer()
    {
        // Ein eigenes Zelt, damit kein anderer Fall der Sammlung die Werte sieht.
        int zelt;
        using (var bereich = _app.Services.CreateScope())
        {
            var grows = bereich.ServiceProvider.GetRequiredService<GrowRepository>();
            zelt = grows.GetActiveGrows().First().TentId!.Value;
            var rohwerte = bereich.ServiceProvider.GetRequiredService<SensorReadingRepository>();
            var zeit = DateTime.UtcNow.AddMinutes(-7);
            foreach (var groesse in new[] { "reservoir-ph", "reservoir-ec", "reservoir-temp" })
                rohwerte.AddReading(new TentSensorReading { TentId = zelt, MetricKey = groesse, Value = 0, CapturedAtUtc = zeit });
        }

        // Der Wurzel-Container, genau wie Program.cs ihn übergibt.
        var ergebnis = WassersondenNullbild.BeimStart(_app.Services, NullLogger.Instance, DateOnly.FromDateTime(DateTime.Now));

        Assert.True(ergebnis.Entfernt >= 3, $"Erwartet mindestens 3 entfernte Rohwerte, gefunden {ergebnis.Entfernt}.");
        using var pruefung = _app.Services.CreateScope();
        var rest = pruefung.ServiceProvider.GetRequiredService<SensorReadingRepository>()
            .GetReadingsWithValue(["reservoir-ph", "reservoir-ec"], 0)
            .Where(r => r.TentId == zelt);
        Assert.Empty(rest);
    }
}
