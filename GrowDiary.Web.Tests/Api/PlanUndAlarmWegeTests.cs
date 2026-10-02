using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services.GrowPlan;
using Microsoft.Extensions.DependencyInjection;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Fork AI (02.10.2026): Befunde aus der Durchsicht der Plan- und Alarm-Logik —
/// über die echten Endpunkte.
/// </summary>
/// <remarks>
/// Eigene, abgeschlossene Grows wie in <see cref="GrowPlanSpeichernTests"/>:
/// sie tauchen in keiner Liste laufender Grows auf und stören die übrigen
/// Fälle der Sammlung nicht.
/// </remarks>
[Collection(IntegrationsSammlung.Name)]
public sealed class PlanUndAlarmWegeTests
{
    private readonly IntegrationsApp _app;

    public PlanUndAlarmWegeTests(IntegrationsApp app) => _app = app;

    private (int Id, int? TentId) GrowMitPlan(bool einfrieren = false)
    {
        using var bereich = _app.Services.CreateScope();
        var grows = bereich.ServiceProvider.GetRequiredService<GrowRepository>();
        var vorlage = grows.GetActiveGrows().First();
        var id = grows.CreateGrow(new GrowRun
        {
            Name = "Plan-Weg " + Guid.NewGuid().ToString("N")[..6],
            TentId = vorlage.TentId,
            HydroStyle = HydroStyle.RDWC,
            FeedProgramId = "skx-canna-aqua",
            Status = GrowStatus.Completed,
            StartDate = DateTime.Today.AddDays(-60),
        });
        var grow = grows.GetGrow(id)!;
        var plaene = _app.Services.GetRequiredService<GrowPlanService>();
        plaene.Anlegen(grow);
        if (einfrieren) Assert.Equal(GrowPlanArten.Eingefroren, plaene.Abgleichen(grow));
        return (id, vorlage.TentId);
    }

    private async Task<int> BuchLaenge(HttpClient client, int id)
        => (await client.GetFromJsonAsync<JsonElement>($"/api/grows/{id}/plan/buch")).GetArrayLength();

    private async Task<double?> EcZiel(HttpClient client, int id, string woche)
    {
        var plan = await client.GetFromJsonAsync<JsonElement>($"/api/grows/{id}/plan");
        var spalte = plan.GetProperty("chart").GetProperty("columns").EnumerateArray()
            .Single(c => c.GetProperty("id").GetString() == woche);
        return spalte.TryGetProperty("ecTarget", out var wert) && wert.ValueKind == JsonValueKind.Number ? wert.GetDouble() : null;
    }

    // ---- Befund 2: EC-Ziel außerhalb des Bands ----

    [Fact]
    public async Task PlanSpeichernLehntEinEcZielAusserhalbDesBandsAb()
    {
        var (id, _) = GrowMitPlan();
        var client = _app.IngressClient();

        var antwort = await client.PostAsJsonAsync($"/api/grows/{id}/plan", new
        {
            spalteId = "flower-w5",
            werte = new[]
            {
                new { feld = "ecTarget", wert = (double?)1.2 },
                new { feld = "ecMin", wert = (double?)1.8 },
                new { feld = "ecMax", wert = (double?)2.2 },
            },
        });

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Contains("EC-Band", await antwort.Content.ReadAsStringAsync());
        Assert.Equal(1, await BuchLaenge(client, id)); // nur „angelegt"

        // Nur das Ziel: das Band wandert mit, gespeichert wird.
        var nurZiel = await client.PostAsJsonAsync($"/api/grows/{id}/plan", new
        {
            spalteId = "flower-w5",
            werte = new[] { new { feld = "ecTarget", wert = (double?)1.2 } },
        });
        Assert.Equal(HttpStatusCode.OK, nurZiel.StatusCode);
    }

    [Fact]
    public async Task WochenwerteLehnenEinEcZielAusserhalbDesBandsAb()
    {
        var (id, _) = GrowMitPlan();
        var client = _app.IngressClient();

        var antwort = await client.PostAsJsonAsync($"/api/wochenplan/werte/{id}", new WochenwerteSpeichernRequest
        {
            Aenderungen =
            [
                new WochenwertAenderung { SpalteId = "flower-w5", Feld = "ecMin", Wert = 1.8 },
                new WochenwertAenderung { SpalteId = "flower-w5", Feld = "ecMax", Wert = 2.2 },
            ],
        });

        // Ziel 1,5 (SKX W5) unter dem neuen Band 1,8–2,2.
        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Equal(1, await BuchLaenge(client, id));
    }

    // ---- Befund 3: abgeschlossene Grows ----

    [Fact]
    public async Task EinEingefrorenerPlanNimmtUeberDieWochenwerteNichtsAn()
    {
        var (id, _) = GrowMitPlan(einfrieren: true);
        var client = _app.IngressClient();
        var vorher = await BuchLaenge(client, id);

        var antwort = await client.PostAsJsonAsync($"/api/wochenplan/werte/{id}", new WochenwerteSpeichernRequest
        {
            Aenderungen = [new WochenwertAenderung { SpalteId = "flower-w5", Feld = "ecTarget", Wert = 1.3 }],
        });

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Equal(1.5, await EcZiel(client, id, "flower-w5"));
        Assert.Equal(vorher, await BuchLaenge(client, id));
    }

    [Fact]
    public async Task EinEingefrorenerPlanNimmtKeineStartkorrekturAn()
    {
        var (id, _) = GrowMitPlan(einfrieren: true);
        var client = _app.IngressClient();
        var vorher = await BuchLaenge(client, id);

        var antwort = await client.PostAsJsonAsync($"/api/grows/{id}/plan/startstand", new
        {
            felder = new[] { new { spalteId = "flower-w5", feld = "ecTarget" } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        Assert.Equal(vorher, await BuchLaenge(client, id));
    }

    // ---- Befund 8: Programmwechsel über PUT /api/grows/{id} ----

    private static object Formular(int? tentId, string programm) => new
    {
        name = "Plan-Weg",
        tentId,
        startDate = DateTime.Today.AddDays(-60).ToString("yyyy-MM-dd"),
        status = "Completed",
        hydroStyle = "RDWC",
        feedProgramId = programm,
    };

    [Fact]
    public async Task EinProgrammwechselUeberDasFormularWirdBeiPlanAbgelehnt()
    {
        var (id, tentId) = GrowMitPlan();
        var client = _app.IngressClient();

        var wechsel = await client.PutAsJsonAsync($"/api/grows/{id}", Formular(tentId, "athena"));
        Assert.Equal(HttpStatusCode.BadRequest, wechsel.StatusCode);
        Assert.Contains("plan/programm", await wechsel.Content.ReadAsStringAsync());

        var grow = await client.GetFromJsonAsync<JsonElement>($"/api/grows/{id}");
        Assert.Equal("skx-canna-aqua", grow.GetProperty("feedProgramId").GetString());

        // Dasselbe Programm (so schickt es die Oberfläche nach dem Planweg): angenommen.
        var gleich = await client.PutAsJsonAsync($"/api/grows/{id}", Formular(tentId, "skx-canna-aqua"));
        Assert.Equal(HttpStatusCode.OK, gleich.StatusCode);
    }

    // ---- Befund 6: Toleranz einer Plan-Regel ----

    [Theory]
    [InlineData(1e6, HttpStatusCode.BadRequest)]
    [InlineData(14.0, HttpStatusCode.BadRequest)]   // pH 0–14: ab hier kann die Regel nie melden
    [InlineData(0.5, HttpStatusCode.OK)]
    public async Task DieToleranzEinerPlanRegelHatEineObergrenze(double toleranz, HttpStatusCode erwartet)
    {
        var client = _app.IngressClient();
        using var bereich = _app.Services.CreateScope();
        var zeltId = bereich.ServiceProvider.GetRequiredService<GrowRepository>().GetActiveGrows().First().TentId!.Value;
        var regeln = bereich.ServiceProvider.GetRequiredService<AlertRuleRepository>();
        var vorher = regeln.GetForTent(zeltId).ToList();

        try
        {
            var satz = vorher.Where(r => r.MetricKey != "reservoir-ph").Select(Dto).ToList();
            satz.Add(new { metricKey = "reservoir-ph", notifyService = "", enabled = true, cooldownMinutes = 30, quelle = "Plan", toleranz = (double?)toleranz });
            var antwort = await client.PutAsJsonAsync($"/api/alerts/tents/{zeltId}", new { rules = satz });

            Assert.Equal(erwartet, antwort.StatusCode);
            if (erwartet == HttpStatusCode.BadRequest)
            {
                Assert.Contains("nie melden", await antwort.Content.ReadAsStringAsync());
                Assert.Equal(vorher.Count, regeln.GetForTent(zeltId).Count);
            }
        }
        finally
        {
            await client.PutAsJsonAsync($"/api/alerts/tents/{zeltId}", new { rules = vorher.Select(Dto).ToList() });
        }
    }

    private static object Dto(TentAlertRule r) => new
    {
        metricKey = r.MetricKey,
        minValue = r.MinValue,
        maxValue = r.MaxValue,
        notifyService = r.NotifyService,
        enabled = r.Enabled,
        cooldownMinutes = r.CooldownMinutes,
        quelle = r.Quelle.ToString(),
        toleranz = r.Toleranz,
        nightMinValue = r.NightMinValue,
        nightMaxValue = r.NightMaxValue,
    };
}
