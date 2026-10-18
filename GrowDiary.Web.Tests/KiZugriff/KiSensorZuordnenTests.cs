using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using GrowDiary.Web.Tests.Api;

namespace GrowDiary.Web.Tests.KiZugriff;

/// <summary>
/// Fork AI (forkai.247): Eine Messgröße über einen Schlüssel zuordnen braucht die Stufe Verwaltung — wie das ganze Zelt.
/// </summary>
/// <remarks>
/// Gilt über die Einstufung des Controllers (<c>[KiStufe(KiStufe.Verwaltung)]</c> an <c>SettingsApiController</c>) und den
/// Verwaltungsweg <c>/api/settings</c>. Der Katalog <c>GET /api/messgroessen</c> liegt bewusst außerhalb: <c>grenzwert_setzen</c>
/// (Grow planen) braucht die Namen auch.
/// </remarks>
[Collection(IntegrationsSammlung.Name)]
public sealed class KiSensorZuordnenTests : IClassFixture<KiZugriffApp>
{
    private readonly KiZugriffApp _ki;

    public KiSensorZuordnenTests(KiZugriffApp ki) => _ki = ki;

    private async Task<int> ZeltAsync()
    {
        var antwort = await _ki.Oberflaeche().PostAsJsonAsync("/api/settings/tents", new { name = "KI Sensor" });
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        return JsonNode.Parse(await antwort.Content.ReadAsStringAsync())!["id"]!.GetValue<int>();
    }

    [Fact]
    public async Task Ohne_Verwaltung_403_mit_Verwaltung_zugeordnet()
    {
        await _ki.SchalterAsync(true);
        var zelt = await ZeltAsync();
        var (_, ohne) = await _ki.SchluesselAsync("Sensor ohne Verwaltung", "Dokumentieren", "GrowPlanen", "GeraeteSchalten");
        var (_, mit) = await _ki.SchluesselAsync("Sensor mit Verwaltung", "Verwaltung");

        var abgelehnt = await _ki.App.AddonClient("172.30.33.60", ohne)
            .PutAsJsonAsync($"/api/settings/tents/{zelt}/sensors/OutsideTemperature", new { haEntityId = "sensor.controller_temperatur" });
        var text = await abgelehnt.Content.ReadAsStringAsync();
        Assert.True(abgelehnt.StatusCode == HttpStatusCode.Forbidden && text.Contains("\"code\":\"ki_stufe_fehlt\"", StringComparison.Ordinal),
            $"erwartet 403 ki_stufe_fehlt, kam {(int)abgelehnt.StatusCode} {text}");

        var zelt1 = JsonNode.Parse(await _ki.Oberflaeche().GetStringAsync($"/api/settings/tents/{zelt}"))!;
        Assert.Empty(zelt1["sensors"]!.AsArray());

        var erlaubt = await _ki.App.AddonClient("172.30.33.61", mit)
            .PutAsJsonAsync($"/api/settings/tents/{zelt}/sensors/OutsideTemperature", new { haEntityId = "sensor.controller_temperatur" });
        Assert.Equal(HttpStatusCode.OK, erlaubt.StatusCode);
        var zelt2 = JsonNode.Parse(await _ki.Oberflaeche().GetStringAsync($"/api/settings/tents/{zelt}"))!;
        Assert.Equal("sensor.controller_temperatur", Assert.Single(zelt2["sensors"]!.AsArray())!["haEntityId"]!.GetValue<string>());
    }

    [Fact]
    public async Task Den_Katalog_liest_auch_ein_Schluessel_ohne_Verwaltung()
    {
        await _ki.SchalterAsync(true);
        var (_, planen) = await _ki.SchluesselAsync("Katalog Grow planen", "GrowPlanen");

        var antwort = await _ki.App.AddonClient("172.30.33.62", planen).GetAsync("/api/messgroessen");
        Assert.Equal(HttpStatusCode.OK, antwort.StatusCode);
    }
}
