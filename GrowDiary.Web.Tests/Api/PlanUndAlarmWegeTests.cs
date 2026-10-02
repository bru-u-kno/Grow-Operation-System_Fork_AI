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

    private static object Formular(int? tentId, string? programm) => new
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

    /// <summary>
    /// Ein leeres Programm bei einem Grow mit Plan ist ein Wechsel „auf nichts" —
    /// und wird abgelehnt wie jeder andere. Bis zum 02.10.2026 nahm der Weg es an:
    /// am Grow stand kein Programm, der Plan lieferte weiter Programm A
    /// (docs/pruefung-2026-10-01.md, „Noch offen, klein").
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EinLeeresProgrammWirdBeiPlanAbgelehnt(string? leer)
    {
        var (id, tentId) = GrowMitPlan();
        var client = _app.IngressClient();
        // Selbsttest: der Grow hat wirklich einen Plan — sonst prüft der Fall nichts.
        Assert.NotNull(GrowPlanRegister.Programm(id));

        var antwort = await client.PutAsJsonAsync($"/api/grows/{id}", Formular(tentId, leer));
        Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
        var text = await antwort.Content.ReadAsStringAsync();
        Assert.Contains("ohne Programm geht es nicht", text);
        Assert.Contains("feedProgramId", text, StringComparison.OrdinalIgnoreCase);

        // Grow und Plan stehen weiter auf demselben Programm.
        var grow = await client.GetFromJsonAsync<JsonElement>($"/api/grows/{id}");
        Assert.Equal("skx-canna-aqua", grow.GetProperty("feedProgramId").GetString());
        Assert.Equal("skx-canna-aqua", GrowPlanRegister.Programm(id)!.Id);
    }

    /// <summary>
    /// Dasselbe Programm wie der Plan ist kein Wechsel — auch in anderer
    /// Schreibweise. Bis zum 02.10.2026 verglich die Sperre gegen das Feld am Grow
    /// statt gegen den Plan (E2E: formularfelder-kommen-an konnte nicht aufräumen).
    /// </summary>
    [Fact]
    public async Task DasProgrammDesPlansDarfWiederEingetragenWerden()
    {
        var (id, tentId) = GrowMitPlan();
        var client = _app.IngressClient();

        var gleich = await client.PutAsJsonAsync($"/api/grows/{id}", Formular(tentId, "SKX-Canna-Aqua"));
        Assert.Equal(HttpStatusCode.OK, gleich.StatusCode);

        // Ein anderes Programm bleibt gesperrt.
        var anderes = await client.PutAsJsonAsync($"/api/grows/{id}", Formular(tentId, "athena"));
        Assert.Equal(HttpStatusCode.BadRequest, anderes.StatusCode);
    }

    /// <summary>Ohne Plan bleibt ein leeres Programm erlaubt — die Sperre gilt nur dem Plan.</summary>
    [Fact]
    public async Task OhnePlanDarfDasProgrammLeerSein()
    {
        int id;
        int? tentId;
        using (var bereich = _app.Services.CreateScope())
        {
            var grows = bereich.ServiceProvider.GetRequiredService<GrowRepository>();
            var vorlage = grows.GetActiveGrows().First();
            tentId = vorlage.TentId;
            id = grows.CreateGrow(new GrowRun
            {
                Name = "Ohne Plan " + Guid.NewGuid().ToString("N")[..6],
                TentId = vorlage.TentId,
                HydroStyle = HydroStyle.RDWC,
                FeedProgramId = "skx-canna-aqua",
                Status = GrowStatus.Completed,
                StartDate = DateTime.Today.AddDays(-60),
            });
        }
        Assert.Null(GrowPlanRegister.Programm(id));

        var antwort = await _app.IngressClient().PutAsJsonAsync($"/api/grows/{id}", Formular(tentId, null));
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
    }

    // ---- Name der Anzucht nach dem Startmaterial (02.10.2026) ----

    private static async Task<string?> AnzuchtName(HttpClient client, int id, string stand = "arbeit")
    {
        var plan = await client.GetFromJsonAsync<JsonElement>($"/api/grows/{id}/plan?stand={stand}");
        return plan.GetProperty("chart").GetProperty("columns").EnumerateArray()
            .Single(c => c.GetProperty("id").GetString() == "root").GetProperty("label").GetString();
    }

    /// <summary>
    /// Ändert das Formular das Startmaterial, heißt die Anzucht im Plan sofort
    /// passend — nicht erst nach dem nächsten Start der App. Der Abschluss im
    /// selben Speichern friert den angeglichenen Stand ein.
    /// </summary>
    [Fact]
    public async Task EinGeaendertesStartmaterialBenenntDieAnzuchtImPlanUm()
    {
        var (id, tentId) = GrowMitPlan();
        var client = _app.IngressClient();
        Assert.Equal("Anzucht", await AnzuchtName(client, id)); // Samen ist die Vorgabe

        var antwort = await client.PutAsJsonAsync($"/api/grows/{id}", new
        {
            name = "Plan-Weg",
            tentId,
            startDate = DateTime.Today.AddDays(-60).ToString("yyyy-MM-dd"),
            status = "Completed",
            hydroStyle = "RDWC",
            feedProgramId = "skx-canna-aqua",
            startMaterial = "Clone",
        });
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);

        Assert.Equal("Bewurzelung", await AnzuchtName(client, id));
        Assert.Equal("Bewurzelung", await AnzuchtName(client, id, "ende"));
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
