using System.Net;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Services;
using GrowDiary.Web.Tests.TestFakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (02.10.2026): Die Sicherungen der Automationen liegen unter dem
/// Datenpfad — im Add-on <c>/data</c>, das ein Update überlebt.
/// </summary>
/// <remarks>
/// Vorher schrieben <see cref="SteuerungAutomationService"/> und
/// <see cref="SteuerungRechenwertAbsicherung"/> nach
/// <c>AppContext.BaseDirectory/App_Data</c>, im Container <c>/app</c>. Nach
/// jedem Update war das „Zurück" weg, und die Meldung verwies trotzdem dorthin.
/// </remarks>
public sealed class SicherungsOrdnerTests : IDisposable
{
    private readonly string _wurzel = Path.Combine(Path.GetTempPath(), "Sicherung_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_wurzel, recursive: true); } catch { /* Windows haelt manchmal fest */ }
    }

    [Fact]
    public async Task SichernAsync_SchreibtUnterDenDatenpfad()
    {
        var pfade = new AppPaths(_wurzel);
        var handler = new RecordingHttpHandler((_, _) => RecordingHttpHandler.Json("""{"id":"fork_ai_co2_waechter","alias":"x"}"""));
        var ha = new HomeAssistantService(new StubHttpClientFactory(handler), NullLogger<HomeAssistantService>.Instance);
        var dienst = new SteuerungAutomationService(ha, NullLogger<SteuerungAutomationService>.Instance, pfade);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://ha.local:8123/") };

        var gesichert = await dienst.SichernAsync(client, "fork_ai_co2_waechter", CancellationToken.None);

        Assert.True(gesichert);
        var ordner = SteuerungSicherungsOrdner.Fuer(pfade);
        Assert.StartsWith(pfade.DataRootPath, ordner, StringComparison.Ordinal);
        Assert.Single(Directory.EnumerateFiles(ordner, "fork_ai_co2_waechter-*.json"));
    }

    [Fact]
    public void AlteSicherungen_WerdenUebernommen_OhneVorhandeneZuUeberschreiben()
    {
        var alt = Path.Combine(_wurzel, "alt");
        var neu = Path.Combine(_wurzel, "neu");
        Directory.CreateDirectory(alt);
        Directory.CreateDirectory(neu);
        File.WriteAllText(Path.Combine(alt, "a.json"), "alt-a");
        File.WriteAllText(Path.Combine(alt, "b.json"), "alt-b");
        File.WriteAllText(Path.Combine(neu, "b.json"), "neu-b");

        var anzahl = SteuerungSicherungsOrdner.AlteUebernehmen(alt, neu, NullLogger.Instance);

        Assert.Equal(1, anzahl);
        Assert.Equal("alt-a", File.ReadAllText(Path.Combine(neu, "a.json")));
        Assert.Equal("neu-b", File.ReadAllText(Path.Combine(neu, "b.json")));
        Assert.Empty(Directory.EnumerateFiles(alt));
    }

    [Fact]
    public void OhneAltenOrdner_PassiertNichts()
        => Assert.Equal(0, SteuerungSicherungsOrdner.AlteUebernehmen(
            Path.Combine(_wurzel, "gibt-es-nicht"), Path.Combine(_wurzel, "neu"), NullLogger.Instance));

    [Fact]
    public void GleicherOrdner_WirdNichtGeleert()
    {
        var ordner = Path.Combine(_wurzel, "gleich");
        Directory.CreateDirectory(ordner);
        File.WriteAllText(Path.Combine(ordner, "a.json"), "a");

        SteuerungSicherungsOrdner.AlteUebernehmen(ordner, ordner + Path.DirectorySeparatorChar, NullLogger.Instance);

        Assert.True(File.Exists(Path.Combine(ordner, "a.json")));
    }
}
