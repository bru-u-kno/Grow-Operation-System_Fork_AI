using System.Net;
using System.Net.Http.Json;
using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using Microsoft.Extensions.DependencyInjection;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Fork AI (F-004): Wochenwerte über die Oberfläche ändern — über den echten Weg.
/// </summary>
/// <remarks>
/// Der Grow des Bestands bekommt für die Dauer des Falls das SKX-Programm und
/// danach sein altes zurück; eigene Werte werden am Ende wieder gelöscht. Die
/// Sammlung teilt sich eine Datenbank, und kein anderer Fall soll hier
/// hineinlaufen.
/// </remarks>
[Collection(IntegrationsSammlung.Name)]
public sealed class WochenwerteBearbeitenTests
{
    private const string Programm = "skx-canna-aqua";
    private const string Woche = "flower-w4";

    private readonly IntegrationsApp _app;

    public WochenwerteBearbeitenTests(IntegrationsApp app) => _app = app;

    private async Task MitSkx(Func<HttpClient, int, Task> fall)
    {
        var client = _app.IngressClient();
        using var bereich = _app.Services.CreateScope();
        var grows = bereich.ServiceProvider.GetRequiredService<GrowRepository>();
        var grow = grows.GetActiveGrows().First();
        var vorher = grow.FeedProgramId;
        grow.FeedProgramId = Programm;
        grows.UpdateGrow(grow);
        try
        {
            await fall(client, grow.Id);
        }
        finally
        {
            bereich.ServiceProvider.GetRequiredService<WochenwertRepository>().Speichern(
                Programm,
                Wochenwertfelder.Alle.Select(f => (Woche, f.Name, (double?)null)));
            bereich.ServiceProvider.GetRequiredService<GrowDiary.Web.Services.Knowledge.WochenwertUeberlagerung>().Auffrischen();
            grow.FeedProgramId = vorher;
            grows.UpdateGrow(grow);
        }
    }

    private static WochenwertFeldDto Feld(WochenwerteDto werte, string feld)
        => werte.Spalten.Single(s => s.Id == Woche).Felder.Single(f => f.Feld == feld);

    private static Task<HttpResponseMessage> Speichern(HttpClient client, int growId, params WochenwertAenderung[] aenderungen)
        => client.PostAsJsonAsync($"/api/wochenplan/werte/{growId}", new WochenwerteSpeichernRequest { Aenderungen = [.. aenderungen] });

    [Fact]
    public Task GespeicherterWertKommtZurueckUndWirktImWochenplan() => MitSkx(async (client, growId) =>
    {
        var vorher = (await client.GetFromJsonAsync<WochenwerteDto>($"/api/wochenplan/werte/{growId}"))!;
        var plan = Feld(vorher, "waterTempNightC").Plan;
        Assert.NotNull(plan);

        var antwort = await Speichern(client, growId,
            new WochenwertAenderung { SpalteId = Woche, Feld = "waterTempNightC", Wert = plan + 1.5 });
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);

        var gespeichert = (await antwort.Content.ReadFromJsonAsync<WochenwerteGespeichertDto>())!;
        var feld = Feld(gespeichert.Werte, "waterTempNightC");
        Assert.Equal(plan + 1.5, feld.Wert);
        Assert.Equal(plan, feld.Plan);
        Assert.True(feld.Geaendert);

        // Und der Leser, den alle anderen benutzen, sieht es auch.
        var wochenplan = await client.GetFromJsonAsync<List<WochenplanDto>>("/api/wochenplan");
        var zeile = wochenplan!.Single(p => p.GrowId == growId).Wochen.Single(w => w.Id == Woche);
        Assert.Contains(((plan + 1.5)!.Value).ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("de-DE")), zeile.Wasser);
    });

    [Fact]
    public Task DerPlanwertSelbstIstKeineAbweichung() => MitSkx(async (client, growId) =>
    {
        var vorher = (await client.GetFromJsonAsync<WochenwerteDto>($"/api/wochenplan/werte/{growId}"))!;
        var plan = Feld(vorher, "ecTarget").Plan;

        var antwort = await Speichern(client, growId,
            new WochenwertAenderung { SpalteId = Woche, Feld = "ecTarget", Wert = plan });
        var gespeichert = (await antwort.Content.ReadFromJsonAsync<WochenwerteGespeichertDto>())!;

        Assert.False(Feld(gespeichert.Werte, "ecTarget").Geaendert);
    });

    [Fact]
    public Task VonUeberBisWirdAbgelehntUndNichtsGespeichert() => MitSkx(async (client, growId) =>
    {
        var antwort = await Speichern(client, growId,
            new WochenwertAenderung { SpalteId = Woche, Feld = "rhMax", Wert = 55 },
            new WochenwertAenderung { SpalteId = Woche, Feld = "vpdMin", Wert = 2.5 },
            new WochenwertAenderung { SpalteId = Woche, Feld = "vpdMax", Wert = 1.0 });

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Contains("VPD von", await antwort.Content.ReadAsStringAsync());

        // Auch die gültige RH-Änderung derselben Anfrage darf nicht gelandet sein.
        var danach = (await client.GetFromJsonAsync<WochenwerteDto>($"/api/wochenplan/werte/{growId}"))!;
        Assert.False(Feld(danach, "rhMax").Geaendert);
    });

    [Theory]
    [InlineData("ecTarget", 14.0)]
    [InlineData("waterTempDayC", -3.0)]
    [InlineData("co2Max", 5000.0)]
    public Task WerteAusserhalbDesBereichsWerdenAbgelehnt(string feld, double wert) => MitSkx(async (client, growId) =>
    {
        var antwort = await Speichern(client, growId, new WochenwertAenderung { SpalteId = Woche, Feld = feld, Wert = wert });
        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
    });

    [Fact]
    public Task UnbekanntesFeldUndUnbekannteWocheWerdenAbgelehnt() => MitSkx(async (client, growId) =>
    {
        var feld = await Speichern(client, growId, new WochenwertAenderung { SpalteId = Woche, Feld = "items", Wert = 1 });
        var woche = await Speichern(client, growId, new WochenwertAenderung { SpalteId = "flower-w99", Feld = "ecTarget", Wert = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, feld.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, woche.StatusCode);
    });

    [Fact]
    public async Task EinGrowOhneWochenplanBekommt404()
    {
        var client = _app.IngressClient();
        var antwort = await client.GetAsync("/api/wochenplan/werte/999999");
        Assert.Equal(HttpStatusCode.NotFound, antwort.StatusCode);
    }
}
