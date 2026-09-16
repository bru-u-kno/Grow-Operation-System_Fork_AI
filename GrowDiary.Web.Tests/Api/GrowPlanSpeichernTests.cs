using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services.GrowPlan;
using Microsoft.Extensions.DependencyInjection;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Fork AI (Grow-Plan, Schritt 3): eine Planwoche über den echten Weg speichern.
/// </summary>
/// <remarks>
/// Eigener, abgeschlossener Grow: er taucht in keiner Liste laufender Grows auf
/// und stört die übrigen Fälle der Sammlung nicht.
/// </remarks>
[Collection(IntegrationsSammlung.Name)]
public sealed class GrowPlanSpeichernTests
{
    private readonly IntegrationsApp _app;

    public GrowPlanSpeichernTests(IntegrationsApp app) => _app = app;

    private int GrowMitPlan()
    {
        using var bereich = _app.Services.CreateScope();
        var grows = bereich.ServiceProvider.GetRequiredService<GrowRepository>();
        var vorlage = grows.GetActiveGrows().First();
        var id = grows.CreateGrow(new GrowRun
        {
            Name = "Plan-Test " + Guid.NewGuid().ToString("N")[..6],
            TentId = vorlage.TentId,
            HydroStyle = HydroStyle.RDWC,
            FeedProgramId = "skx-canna-aqua",
            Status = GrowStatus.Completed,
            StartDate = DateTime.Today.AddDays(-60),
        });
        var grow = grows.GetGrow(id)!;
        _app.Services.GetRequiredService<GrowPlanService>().Anlegen(grow);
        return id;
    }

    [Fact]
    public async Task WerteUndDosierungWerdenGespeichertUndStehenImBuch()
    {
        var id = GrowMitPlan();
        var client = _app.IngressClient();

        var antwort = await client.PostAsJsonAsync($"/api/grows/{id}/plan", new
        {
            spalteId = "flower-w5",
            werte = new[] { new { feld = "ecTarget", wert = (double?)1.4 } },
            dosierung = new[]
            {
                new { komponente = "Aqua Flores A", mlProLiter = 2.5 },
                new { komponente = "Aqua Flores B", mlProLiter = 2.5 },
                new { komponente = "Pro-Silicate", mlProLiter = 0.6 },
            },
            auchInsProgramm = false,
            grund = "Test",
        });
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
        var inhalt = await antwort.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(inhalt.GetProperty("aenderungen").GetInt32() >= 3);

        var buch = await client.GetFromJsonAsync<JsonElement>($"/api/grows/{id}/plan/buch");
        var arten = buch.EnumerateArray().Select(e => e.GetProperty("art").GetString()).ToList();
        Assert.Contains("wert", arten);
        Assert.Contains("dosierung", arten);
    }

    [Theory]
    [InlineData("""{"spalteId":"gibt-es-nicht","werte":[]}""")]
    [InlineData("""{"spalteId":"flower-w5","werte":[{"feld":"quatsch","wert":1}]}""")]
    [InlineData("""{"spalteId":"flower-w5","werte":[{"feld":"ecTarget","wert":99}]}""")]
    [InlineData("""{"spalteId":"flower-w5","werte":[{"feld":"phMin","wert":6.5}]}""")]
    [InlineData("""{"spalteId":"flower-w5","werte":[],"dosierung":[{"komponente":"A","mlProLiter":1},{"komponente":"a","mlProLiter":2}]}""")]
    [InlineData("""{"spalteId":"flower-w5","werte":[],"dosierung":[{"komponente":" ","mlProLiter":1}]}""")]
    [InlineData("""{"spalteId":"flower-w5","werte":[],"dosierung":[{"komponente":"A","mlProLiter":-1}]}""")]
    public async Task UngueltigesWirdAbgelehntUndNichtsGespeichert(string json)
    {
        var id = GrowMitPlan();
        var client = _app.IngressClient();

        var antwort = await client.PostAsync($"/api/grows/{id}/plan",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        var buch = await client.GetFromJsonAsync<JsonElement>($"/api/grows/{id}/plan/buch");
        Assert.Equal(1, buch.GetArrayLength()); // nur „angelegt"
    }
}
