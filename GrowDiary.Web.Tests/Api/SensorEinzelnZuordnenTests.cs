using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Fork AI (forkai.247): <c>PUT /api/settings/tents/{id}/sensors/{metricType}</c> ändert genau eine Zuordnung — an Ort und Stelle.
/// </summary>
/// <remarks>
/// <para>Der Anlass: Der Zelt-Endpunkt (<c>PUT /api/settings/tents/{id}</c> mit <c>sensors</c>) legt alle Sensorzeilen neu an.
/// Ein Assistent, der nur „Außen Temp." zuordnen soll, hätte damit jede Id im Zelt verändert, und an den Ids hängen Geräte und
/// Meldungen. Geprüft wird deshalb die Id JEDER anderen Zeile vor und nach dem Aufruf — und als Gegenprobe, dass genau dieser
/// Vergleich beim alten Weg anschlägt (sonst bewiese er nichts).</para>
/// <para>Jeder Fall legt ein eigenes Zelt an und archiviert es am Ende; die App wird von der Sammlung geteilt.</para>
/// </remarks>
[Collection(IntegrationsSammlung.Name)]
public sealed class SensorEinzelnZuordnenTests
{
    private readonly IntegrationsApp _app;

    public SensorEinzelnZuordnenTests(IntegrationsApp app) => _app = app;

    private static StringContent Roh(string json) => new(json, Encoding.UTF8, "application/json");

    private async Task<(HttpStatusCode Status, JsonNode? Antwort)> Senden(HttpMethod methode, string pfad, string? json = null)
    {
        var anfrage = new HttpRequestMessage(methode, pfad) { Content = json is null ? null : Roh(json) };
        var antwort = await _app.IngressClient().SendAsync(anfrage);
        var text = await antwort.Content.ReadAsStringAsync();
        return (antwort.StatusCode, string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text));
    }

    private async Task<JsonObject> Zelt(int id)
    {
        var (status, zelt) = await Senden(HttpMethod.Get, $"/api/settings/tents/{id}");
        Assert.Equal(HttpStatusCode.OK, status);
        return zelt!.AsObject();
    }

    /// <summary>Messgröße → (Id, Entität, aktiv) aller Sensorzeilen eines Zelts.</summary>
    private static Dictionary<string, (int Id, string Entitaet, bool Aktiv)> Zeilen(JsonObject zelt)
        => zelt["sensors"]!.AsArray().ToDictionary(
            s => s!["metricType"]!.GetValue<string>(),
            s => (s!["id"]!.GetValue<int>(), s["haEntityId"]?.GetValue<string>() ?? "", s["isActive"]!.GetValue<bool>()));

    /// <summary>Ein Zelt mit drei zugeordneten Messgrößen — darunter pH, für den der Abgleich ein Gerät anlegt.</summary>
    private async Task<int> ZeltMitDreiSensoren(string name)
    {
        var (status, angelegt) = await Senden(HttpMethod.Post, "/api/settings/tents", $$"""
            {"name":"{{name}}","sensors":[
              {"metricType":"AirTemperature","haEntityId":"sensor.einzeln_luft","isActive":true},
              {"metricType":"Humidity","haEntityId":"sensor.einzeln_feuchte","isActive":true},
              {"metricType":"ReservoirPh","haEntityId":"sensor.einzeln_ph","isActive":true}]}
            """);
        Assert.Equal(HttpStatusCode.Created, status);
        return angelegt!["id"]!.GetValue<int>();
    }

    private async Task Aufraeumen(int id) => await Senden(HttpMethod.Post, $"/api/settings/tents/{id}/archive");

    private async Task<JsonObject> PhGeraet(int zeltId)
    {
        var (status, liste) = await Senden(HttpMethod.Get, $"/api/hardware-items?tentId={zeltId}");
        Assert.Equal(HttpStatusCode.OK, status);
        return Assert.Single(liste!.AsArray(), g => g!["metricType"]?.GetValue<string>() == "ReservoirPh")!.AsObject();
    }

    [Fact]
    public async Task Zuordnen_Umhaengen_und_Loesen_lassen_jede_andere_Id_stehen()
    {
        var id = await ZeltMitDreiSensoren("Einzeln zuordnen");
        try
        {
            var vorher = Zeilen(await Zelt(id));
            Assert.Equal(3, vorher.Count);
            var geraetVorher = await PhGeraet(id);
            Assert.Equal(vorher["ReservoirPh"].Id, geraetVorher["tentSensorId"]!.GetValue<int>());

            // 1. Neue Messgröße: eine Zeile kommt dazu, die drei anderen behalten ihre Id.
            var (s1, z1) = await Senden(HttpMethod.Put, $"/api/settings/tents/{id}/sensors/OutsideTemperature",
                """{"haEntityId":"sensor.controller_temperatur"}""");
            Assert.Equal(HttpStatusCode.OK, s1);
            var nach1 = Zeilen(z1!.AsObject());
            Assert.Equal(4, nach1.Count);
            foreach (var (messgroesse, zeile) in vorher) Assert.Equal(zeile, nach1[messgroesse]);
            Assert.Equal(("sensor.controller_temperatur", true), (nach1["OutsideTemperature"].Entitaet, nach1["OutsideTemperature"].Aktiv));

            // 2. Umhängen: dieselbe Id, neue Entität — und das Gerät folgt der Entität, sein Verweis bleibt.
            var (s2, z2) = await Senden(HttpMethod.Put, $"/api/settings/tents/{id}/sensors/reservoirph", """{"haEntityId":"sensor.einzeln_ph_neu"}""");
            Assert.Equal(HttpStatusCode.OK, s2);
            var nach2 = Zeilen(z2!.AsObject());
            Assert.Equal((nach1["ReservoirPh"].Id, "sensor.einzeln_ph_neu", true), nach2["ReservoirPh"]);
            foreach (var messgroesse in nach1.Keys.Where(m => m != "ReservoirPh")) Assert.Equal(nach1[messgroesse], nach2[messgroesse]);
            var geraetNachher = await PhGeraet(id);
            Assert.Equal(geraetVorher["id"]!.GetValue<int>(), geraetNachher["id"]!.GetValue<int>());
            Assert.Equal(nach2["ReservoirPh"].Id, geraetNachher["tentSensorId"]!.GetValue<int>());
            Assert.Equal("sensor.einzeln_ph_neu", geraetNachher["haEntityId"]!.GetValue<string>());

            // 3. Lösen: die Zeile bleibt mit ihrer Id, inaktiv und ohne Entität — wie beim Leeren auf der Einrichtungsseite.
            var (s3, z3) = await Senden(HttpMethod.Put, $"/api/settings/tents/{id}/sensors/Humidity", """{"haEntityId":"  "}""");
            Assert.Equal(HttpStatusCode.OK, s3);
            var nach3 = Zeilen(z3!.AsObject());
            Assert.Equal((nach2["Humidity"].Id, "", false), nach3["Humidity"]);
            foreach (var messgroesse in nach2.Keys.Where(m => m != "Humidity")) Assert.Equal(nach2[messgroesse], nach3[messgroesse]);

            // Und der gespeicherte Stand, nicht nur die Antwort.
            Assert.Equal(nach3, Zeilen(await Zelt(id)));

            // 4. Lösen einer Messgröße ohne Zeile ändert nichts.
            var (s4, z4) = await Senden(HttpMethod.Put, $"/api/settings/tents/{id}/sensors/Co2", """{"haEntityId":null}""");
            Assert.Equal(HttpStatusCode.OK, s4);
            Assert.Equal(nach3, Zeilen(z4!.AsObject()));
        }
        finally
        {
            await Aufraeumen(id);
        }
    }

    /// <summary>
    /// Gegenprobe: Über den alten Weg (das ganze Zelt mit <c>sensors</c>) ändern sich die Ids — derselbe Vergleich wie oben schlägt
    /// also an, wenn der neue Endpunkt intern doch alles ersetzt.
    /// </summary>
    [Fact]
    public async Task Gegenprobe_der_Zelt_Endpunkt_vergibt_neue_Ids()
    {
        var id = await ZeltMitDreiSensoren("Einzeln Gegenprobe");
        try
        {
            var vorher = Zeilen(await Zelt(id));
            var (status, nachher) = await Senden(HttpMethod.Put, $"/api/settings/tents/{id}", $$"""
                {"name":"Einzeln Gegenprobe","sensors":[
                  {"metricType":"AirTemperature","haEntityId":"sensor.einzeln_luft","isActive":true},
                  {"metricType":"Humidity","haEntityId":"sensor.einzeln_feuchte","isActive":true},
                  {"metricType":"ReservoirPh","haEntityId":"sensor.einzeln_ph","isActive":true},
                  {"metricType":"OutsideTemperature","haEntityId":"sensor.controller_temperatur","isActive":true}]}
                """);
            Assert.Equal(HttpStatusCode.OK, status);
            var nach = Zeilen(nachher!.AsObject());
            Assert.All(vorher, p => Assert.NotEqual(p.Value.Id, nach[p.Key].Id));
        }
        finally
        {
            await Aufraeumen(id);
        }
    }

    [Theory]
    [InlineData("Aussenluft", """{"haEntityId":"sensor.x"}""", "sensor_messgroesse_unbekannt", "Die Messgröße „Aussenluft“ gibt es nicht")]
    [InlineData("3", """{"haEntityId":"sensor.x"}""", "sensor_messgroesse_unbekannt", "gibt es nicht")]
    // Enum.TryParse verknüpfte das zu 1 | 2 = Co2 (Prüfer forkai.247).
    [InlineData("Humidity,Vpd", """{"haEntityId":"sensor.x"}""", "sensor_messgroesse_unbekannt", "gibt es nicht")]
    [InlineData("OutsideTemperature", """{"haEntityId":"switch.controller"}""", "sensor_domain_passt_nicht", "„Außen Temp.“ erwartet eine Entität der Art sensor; switch.controller ist switch.")]
    [InlineData("PumpCirculation", """{"haEntityId":"sensor.pumpe_leistung"}""", "sensor_domain_passt_nicht", "„Umwälzpumpe“ erwartet eine Entität der Art switch oder input_boolean; sensor.pumpe_leistung ist sensor.")]
    [InlineData("OutsideTemperature", """{"haEntityId":"sensor.a,sensor.b"}""", "sensor_entitaet_ungueltig", "nicht genau eine Entität")]
    [InlineData("OutsideTemperature", """{"haEntityId":"../states"}""", "sensor_entitaet_ungueltig", "nicht genau eine Entität")]
    public async Task Was_nicht_passt_gibt_400_mit_deutscher_Meldung_und_aendert_nichts(string messgroesse, string rumpf, string code, string meldung)
    {
        var id = await ZeltMitDreiSensoren("Einzeln Absage");
        try
        {
            var vorher = Zeilen(await Zelt(id));
            var (status, antwort) = await Senden(HttpMethod.Put, $"/api/settings/tents/{id}/sensors/{messgroesse}", rumpf);

            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Equal(code, antwort!["code"]!.GetValue<string>());
            Assert.Contains(meldung, antwort["message"]!.GetValue<string>());
            Assert.Equal(vorher, Zeilen(await Zelt(id)));
        }
        finally
        {
            await Aufraeumen(id);
        }
    }

    [Fact]
    public async Task Ein_unbekanntes_Zelt_gibt_404()
    {
        var (status, antwort) = await Senden(HttpMethod.Put, "/api/settings/tents/987654/sensors/OutsideTemperature", """{"haEntityId":"sensor.x"}""");
        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal("tent_not_found", antwort!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task Der_Katalog_nennt_jede_Messgroesse_mit_Etikett_Schluessel_und_Domains()
    {
        var (status, liste) = await Senden(HttpMethod.Get, "/api/messgroessen");
        Assert.Equal(HttpStatusCode.OK, status);
        var eintraege = liste!.AsArray();
        Assert.Equal(Enum.GetValues<GrowDiary.Web.Models.SensorMetricType>().Length, eintraege.Count);

        var aussen = Assert.Single(eintraege, e => e!["metricType"]!.GetValue<string>() == "OutsideTemperature")!;
        Assert.Equal("outside-temperature", aussen["metrikSchluessel"]!.GetValue<string>());
        Assert.Equal("Außen Temp.", aussen["etikett"]!.GetValue<string>());
        Assert.Equal(["sensor"], aussen["domains"]!.AsArray().Select(d => d!.GetValue<string>()));

        // Wo die Meldegrenzen anders heißen, steht das dabei.
        var luft = Assert.Single(eintraege, e => e!["metricType"]!.GetValue<string>() == "AirTemperature")!;
        Assert.Equal("Lufttemp.", luft["meldeEtikett"]!.GetValue<string>());
    }
}
