using System.Net.Http.Json;
using System.Text.Json;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using Microsoft.Extensions.DependencyInjection;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Fork AI (Grow-Plan, Schritt 2): was das Bearbeiten-Blatt einer Werte-Karte
/// von <c>/api/zielwerte</c> braucht — über den echten Weg.
/// </summary>
[Collection(IntegrationsSammlung.Name)]
public sealed class ZielwerteBlattTests
{
    private readonly IntegrationsApp _app;

    public ZielwerteBlattTests(IntegrationsApp app) => _app = app;

    private static JsonElement Wert(JsonElement antwort, string key)
        => antwort.GetProperty("werte").EnumerateArray().Single(w => w.GetProperty("key").GetString() == key);

    [Fact]
    public async Task KarteTraegtRegelGrenzenUndDieToleranzDerRegel()
    {
        var client = _app.IngressClient();
        var antwort = await client.GetFromJsonAsync<JsonElement>("/api/zielwerte");
        var werte = antwort.GetProperty("werte");
        Assert.True(werte.GetArrayLength() > 0, "Der Demobestand liefert keine Karten — der Test prüft sonst nichts.");
        var zeltId = antwort.GetProperty("zeltId").GetInt32();

        using var bereich = _app.Services.CreateScope();
        var regeln = bereich.ServiceProvider.GetRequiredService<AlertRuleRepository>();
        var vorher = regeln.GetForTent(zeltId).ToList();
        var key = werte.EnumerateArray()
            .Select(w => w.GetProperty("key").GetString()!)
            .First(k => GrowDiary.Web.Services.Planzielgrenzen.KenntPlanziel(k));

        try
        {
            var satz = vorher.Where(r => r.MetricKey != key).Select(Dto).ToList();
            satz.Add(new { metricKey = key, notifyService = "", enabled = true, cooldownMinutes = 45, quelle = "Plan", toleranz = 7.5 });
            var put = await client.PutAsJsonAsync($"/api/alerts/tents/{zeltId}", new { rules = satz });
            put.EnsureSuccessStatusCode();

            antwort = await client.GetFromJsonAsync<JsonElement>("/api/zielwerte");
            var wert = Wert(antwort, key);

            var regel = wert.GetProperty("regel");
            Assert.Equal("Plan", regel.GetProperty("quelle").GetString());
            Assert.Equal(7.5, regel.GetProperty("toleranz").GetDouble());
            Assert.Equal(45, regel.GetProperty("karenzMinuten").GetInt32());
            Assert.True(regel.GetProperty("planMoeglich").GetBoolean());

            // F-015: die Herkunftskette nennt die Toleranz der Regel, nicht die Werkseinstellung.
            var kette = wert.GetProperty("kette").EnumerateArray()
                .Select(s => s.TryGetProperty("hinweis", out var h) ? h.GetString() : null)
                .ToList();
            Assert.Contains(kette, h => h != null && h.Contains("±7,5"));
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
