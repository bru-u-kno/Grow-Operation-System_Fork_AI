using System.Net;
using System.Net.Http.Json;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using Microsoft.Extensions.DependencyInjection;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Fork AI (02.10.2026): EC ist nach oben begrenzt — in Messung, Wasserwechsel,
/// Addback und festen Alarm-Grenzen.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass.</b> Seit die Oberfläche Zahlen deutsch liest
/// (<c>zahlenfeld.ts</c>), ist „1.250" tausendzweihundertfünfzig — vorher 1,25.
/// Der Prüfer hat gegen die laufende App nachgestellt: EC 1250 kam in allen vier
/// Wegen mit 201/200 durch, denn geprüft war nur „nicht negativ" bzw. bei den
/// Alarmen nur „nicht vertauscht". Die Grenze ist die vorhandene Tabelle
/// <c>MeasurementSanityService.PhysikalischeGrenzen["ec"]</c> (0–10).</para>
/// </remarks>
[Collection(IntegrationsSammlung.Name)]
public sealed class EcObergrenzeTests
{
    private readonly IntegrationsApp _app;

    public EcObergrenzeTests(IntegrationsApp app) => _app = app;

    private int EigenerGrow()
    {
        using var bereich = _app.Services.CreateScope();
        var grows = bereich.ServiceProvider.GetRequiredService<GrowRepository>();
        var vorlage = grows.GetActiveGrows().First();
        return grows.CreateGrow(new GrowRun
        {
            Name = "EC-Grenze " + Guid.NewGuid().ToString("N")[..6],
            TentId = vorlage.TentId,
            HydroStyle = HydroStyle.RDWC,
            Status = GrowStatus.Completed,
            StartDate = DateTime.Today.AddDays(-60),
        });
    }

    [Theory]
    [InlineData("reservoirEc")]
    [InlineData("irrigationEc")]
    [InlineData("drainEc")]
    [InlineData("addbackEc")]
    public async Task EineMessungMitEc1250WirdAbgelehnt(string feld)
    {
        var id = EigenerGrow();
        var client = _app.IngressClient();

        var zuHoch = await client.PostAsJsonAsync($"/api/grows/{id}/measurements", new Dictionary<string, object> { [feld] = 1250.0 });
        Assert.Equal(HttpStatusCode.BadRequest, zuHoch.StatusCode);
        Assert.Contains("physikalisch", await zuHoch.Content.ReadAsStringAsync());
        Assert.Contains($"\"{feld}\"", await zuHoch.Content.ReadAsStringAsync());

        // Gegenprobe: ein echter Wert geht durch — die Sperre ist keine Mauer.
        var echt = await client.PostAsJsonAsync($"/api/grows/{id}/measurements", new Dictionary<string, object> { [feld] = 1.25 });
        Assert.True(echt.IsSuccessStatusCode, await echt.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task WasserwechselUndAddbackMitEc1250WerdenAbgelehnt()
    {
        var id = EigenerGrow();
        var client = _app.IngressClient();

        // Abgelehnt WEGEN der EC — der Feldfehler muss an genau diesem Feld stehen,
        // sonst könnte die 400 von etwas anderem kommen.
        var wechsel = await client.PostAsJsonAsync($"/api/grows/{id}/changeouts", new { ecBefore = 1250.0, ecAfter = 1.4, percentChanged = 50.0 });
        Assert.Equal(HttpStatusCode.BadRequest, wechsel.StatusCode);
        Assert.Contains("\"ecBefore\"", await wechsel.Content.ReadAsStringAsync());

        var addback = await client.PostAsJsonAsync($"/api/grows/{id}/addback/logs", new { ecBefore = 1.2, ecTarget = 1400.0, ecStock = 1.8 });
        Assert.Equal(HttpStatusCode.BadRequest, addback.StatusCode);
        Assert.Contains("\"ecTarget\"", await addback.Content.ReadAsStringAsync());

        var wechselEcht = await client.PostAsJsonAsync($"/api/grows/{id}/changeouts", new { ecBefore = 1.25, ecAfter = 1.4, percentChanged = 50.0 });
        Assert.True(wechselEcht.IsSuccessStatusCode, await wechselEcht.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task EineFesteEcAlarmgrenzeVon1200WirdAbgelehnt()
    {
        var client = _app.IngressClient();
        using var bereich = _app.Services.CreateScope();
        var zeltId = bereich.ServiceProvider.GetRequiredService<GrowRepository>().GetActiveGrows().First().TentId!.Value;
        var regeln = bereich.ServiceProvider.GetRequiredService<AlertRuleRepository>();
        var vorher = regeln.GetForTent(zeltId).ToList();

        try
        {
            var satz = vorher.Where(r => r.MetricKey != "reservoir-ec").Select(Dto).ToList();
            satz.Add(new { metricKey = "reservoir-ec", minValue = (double?)1200, maxValue = (double?)2400, notifyService = "", enabled = true, cooldownMinutes = 30, quelle = "Fest" });
            var antwort = await client.PutAsJsonAsync($"/api/alerts/tents/{zeltId}", new { rules = satz });
            Assert.Equal(HttpStatusCode.BadRequest, antwort.StatusCode);
            Assert.Contains("physikalisch", await antwort.Content.ReadAsStringAsync());
            Assert.Equal(vorher.Count, regeln.GetForTent(zeltId).Count);

            satz[^1] = new { metricKey = "reservoir-ec", minValue = (double?)1.2, maxValue = (double?)2.4, notifyService = "", enabled = true, cooldownMinutes = 30, quelle = "Fest" };
            var echt = await client.PutAsJsonAsync($"/api/alerts/tents/{zeltId}", new { rules = satz });
            Assert.Equal(HttpStatusCode.OK, echt.StatusCode);
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
