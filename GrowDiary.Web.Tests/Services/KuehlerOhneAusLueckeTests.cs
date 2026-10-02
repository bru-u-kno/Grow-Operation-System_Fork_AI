using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Tests.TestFakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (02.10.2026): Ein Kühler, den der Wächter nicht abschalten kann, steht
/// als Lücke im Bestand — nicht erst im HA-Logbuch, wenn der Fühler schon tot ist.
/// </summary>
public sealed class KuehlerOhneAusLueckeTests
{
    private const string Satz = "kann den Kühler nicht abschalten";

    private static readonly HomeAssistantSettings Einstellungen = new()
    {
        Enabled = true, BaseUrl = "http://ha.local:8123", AccessToken = "test",
    };

    private static Dictionary<string, string?> Zuordnung(string? steckdose, string? sollwert) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            [ChillerSteuerungService.Rollen.WasserFuehler] = "sensor.wasser",
            [ChillerSteuerungService.Rollen.Steckdose] = steckdose,
            [ChillerSteuerungService.Rollen.KuehlerSollwert] = sollwert,
        };

    private static async Task<SteuerungBestandService.Bestandsaufnahme> Bestand(Dictionary<string, string?> zuordnung)
    {
        var handler = new RecordingHttpHandler((anfrage, _) =>
            anfrage.RequestUri!.AbsolutePath == "/api/states"
                ? RecordingHttpHandler.Json("""[{"entity_id":"sensor.wasser","state":"19.5","attributes":{}}]""")
                : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        var ha = new HomeAssistantService(new StubHttpClientFactory(handler), NullLogger<HomeAssistantService>.Instance);
        var belegt = zuordnung.Where(p => !string.IsNullOrWhiteSpace(p.Value)).Select(p => p.Key).ToList();

        return await new SteuerungBestandService(ha, NullLogger<SteuerungBestandService>.Instance)
            .AufnehmenAsync(ChillerSteuerungService.Modul, belegt, Einstellungen, default, zuordnung);
    }

    [Fact]
    public async Task NurSollwertEingang_OhneSteckdose_IstEineLuecke()
    {
        var bestand = await Bestand(Zuordnung(null, "number.kuehler_soll"));

        Assert.True(bestand.AusgefalleneFunktionen.Any(f => f.Contains(Satz, StringComparison.Ordinal)),
            "Ein number-Kuehler ohne Steckdose laesst sich vom Waechter nicht abschalten; der Bestand "
            + "sagt nichts davon. Gefunden: " + string.Join(" | ", bestand.AusgefalleneFunktionen));
    }

    [Theory]
    [InlineData("switch.kuehler", "number.kuehler_soll")] // Steckdose als Not-Aus
    [InlineData(null, "climate.kuehler")]                 // climate kennt turn_off
    [InlineData("switch.kuehler", null)]                  // reine Steckdose
    public async Task MitAusMoeglichkeit_KeineLuecke(string? steckdose, string? sollwert)
    {
        var bestand = await Bestand(Zuordnung(steckdose, sollwert));

        Assert.DoesNotContain(bestand.AusgefalleneFunktionen, f => f.Contains(Satz, StringComparison.Ordinal));
    }

    [Fact]
    public void AndereModule_BleibenUnberuehrt()
        => Assert.Empty(SteuerungBestandService.Luecken("co2", Zuordnung(null, "number.kuehler_soll")));
}
