using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using GrowMcp.Tools;

namespace GrowMcp.Tests;

/// <summary>
/// Fork AI (forkai.247): <c>sensoren</c> und <c>sensor_zuordnen</c>.
/// </summary>
/// <remarks>
/// Die Attrappe verhält sich wie Grow OS: <c>PUT …/sensors/{metricType}</c> ändert die eine Zeile an Ort und Stelle (Id bleibt) oder
/// legt sie an. Die Variante <c>ersetztAlles</c> spielt den Fehler nach, vor dem der Vorher/Nachher-Vergleich schützen soll — einen
/// Server, der dabei alle Zeilen neu anlegt (neue Ids), wie es der Zelt-Endpunkt mit <c>sensors</c> tut.
/// </remarks>
public sealed class SensorWerkzeugeTests
{
    private const string HaStaende = """
        [{"entityId":"sensor.controller_temperatur","name":"Controller Temperatur","zustand":"20.9","einheit":"°C","bereich":null,"geaendertAmUtc":null},
         {"entityId":"sensor.zelt_t","name":"Zelt Temperatur","zustand":"24.4","einheit":"°C","bereich":null,"geaendertAmUtc":null},
         {"entityId":"sensor.zelt_rlf","name":"Zelt RLF","zustand":"50","einheit":"%","bereich":null,"geaendertAmUtc":null},
         {"entityId":"sensor.rdwc_ph","name":"pH","zustand":"5.82","einheit":null,"bereich":null,"geaendertAmUtc":null},
         {"entityId":"switch.controller","name":"Controller","zustand":"on","einheit":null,"bereich":null,"geaendertAmUtc":null}]
        """;

    /// <summary>Ein Zelt mit Lufttemp, Luftfeuchte (aktiv), pH (gelöst, Zeile steht) und einem Licht-Sensor, der zur Regel passt.</summary>
    private static ForkAttrappe Fork(bool ersetztAlles = false, int zelte = 1, bool doppelt = false)
    {
        var sensoren = new List<JsonObject>
        {
            Zeile(31, "AirTemperature", "sensor.zelt_t", true),
            Zeile(32, "Humidity", "sensor.zelt_rlf", true),
            Zeile(33, "ReservoirPh", "", false),
            Zeile(34, "PumpCirculation", "sensor.pumpe_leistung", true),
        };
        // Altbestand: eine zweite Luftfeuchte-Zeile mit höherer Id — die liest Grow OS.
        if (doppelt) sensoren.Add(Zeile(35, "Humidity", "sensor.zelt_rlf_2", true));
        var naechsteId = 40;

        string Zelt() => new JsonObject { ["id"] = 1, ["name"] = "Zelt-RDWC", ["sensors"] = new JsonArray(sensoren.Select(z => (JsonNode)z.DeepClone()).ToArray()) }.ToJsonString();

        return new ForkAttrappe()
            .Antwort("GET", "api/messgroessen", 200, ForkAttrappe.Katalog)
            .Antwort("GET", "api/settings/tents", 200, zelte == 1 ? """[{"id":1,"name":"Zelt-RDWC"}]""" : """[{"id":1,"name":"Zelt-RDWC"},{"id":2,"name":"Mutter"}]""")
            .Antwort("GET", @"api/settings/tents/\d+", 404, """{"code":"tent_not_found","message":"Zelt existiert nicht."}""")
            .Antwort("GET", "api/settings/tents/1", _ => (200, Zelt()))
            .Antwort("GET", "api/ki-ha/zustaende", a =>
            {
                // Wie Grow OS: suche filtert auf Teilstück der Id, domain auf die Domain.
                var abfrage = System.Web.HttpUtility.ParseQueryString(new Uri("http://x/" + a.Pfad.TrimStart('/')).Query);
                var liste = JsonNode.Parse(HaStaende)!.AsArray().Where(e =>
                    (abfrage["suche"] is not { } suche || e!["entityId"]!.GetValue<string>().Contains(suche, StringComparison.OrdinalIgnoreCase))
                    && (abfrage["domain"] is not { } domain || e!["entityId"]!.GetValue<string>().StartsWith(domain + ".", StringComparison.Ordinal)));
                return (200, new JsonArray(liste.Select(e => e!.DeepClone()).ToArray()).ToJsonString());
            })
            .Antwort("PUT", @"api/settings/tents/1/sensors/\w+", a =>
            {
                var messgroesse = a.Weg.Split('/')[^1];
                var entitaet = JsonNode.Parse(a.Rumpf!)!["haEntityId"]?.GetValue<string>();
                if (ersetztAlles)
                {
                    for (var i = 0; i < sensoren.Count; i++) sensoren[i]["id"] = naechsteId++;
                }
                // Wie Grow OS: die wirksame Zeile (letzte aktive) ändern, weitere derselben Messgröße stilllegen.
                var eigene = sensoren.Where(z => z["metricType"]!.GetValue<string>() == messgroesse).OrderBy(z => z["id"]!.GetValue<int>()).ToList();
                var zeile = eigene.LastOrDefault(z => z["isActive"]!.GetValue<bool>()) ?? eigene.FirstOrDefault();
                foreach (var andere in eigene.Where(z => z != zeile)) { andere["haEntityId"] = ""; andere["isActive"] = false; }
                if (zeile is null && entitaet is not null) sensoren.Add(Zeile(naechsteId++, messgroesse, entitaet, true));
                else if (zeile is not null)
                {
                    zeile["haEntityId"] = entitaet ?? "";
                    zeile["isActive"] = entitaet is not null;
                }
                return (200, Zelt());
            });
    }

    private static JsonObject Zeile(int id, string messgroesse, string entitaet, bool aktiv)
        => new() { ["id"] = id, ["tentId"] = 1, ["metricType"] = messgroesse, ["haEntityId"] = entitaet, ["displayLabel"] = null, ["isActive"] = aktiv };

    private static Task<string> Aufruf(ForkAttrappe fork, string werkzeug, Dictionary<string, object?> werte)
        => Werkzeugkasten.TextAsync(werkzeug, Werkzeugkasten.Leser(fork, Werkzeugkasten.ForkSchluessel), werte);

    private static Dictionary<string, object?> Zuordnen(string messgroesse, string entitaet) => new() { ["zeltId"] = 1, ["messgroesse"] = messgroesse, ["entitaet"] = entitaet };

    // ------------------------------------------------------------------ sensoren

    [Fact]
    public async Task sensoren_nennt_Etikett_Entitaet_Wert_Inaktive_und_Freie()
    {
        var text = await Aufruf(Fork(), "sensoren", new() { ["zeltId"] = 1 });

        Assert.Contains("Zelt 1 „Zelt-RDWC\" — zugeordnet (3):", text);
        Assert.Contains("Lufttemp: sensor.zelt_t — 24,4 °C, aktiv [Sensor-Id 31]", text);
        Assert.Contains("Luftfeuchte: sensor.zelt_rlf — 50 %, aktiv [Sensor-Id 32]", text);
        Assert.Contains("Inaktiv: pH [Sensor-Id 33].", text);
        Assert.Contains("Frei: Licht, pH, DO, Wasserstand (L), Wasserstand (cm), Außen Temp., Außen RLF, Außen VPD.", text);
        // Eine Zuordnung, die der Fork heute ablehnen würde, wird genannt — nicht still übergangen.
        Assert.Contains("Umwälzpumpe: sensor.pumpe_leistung — in Home Assistant nicht gefunden, aktiv (passt nicht zur Regel: erwartet switch oder input_boolean)", text);
    }

    [Fact]
    public async Task sensoren_ohne_zeltId_nimmt_das_einzige_Zelt_und_nennt_sonst_die_Liste()
    {
        Assert.Contains("Zelt 1 „Zelt-RDWC\"", await Aufruf(Fork(), "sensoren", new()));

        var fork = Fork(zelte: 2);
        var text = await Aufruf(fork, "sensoren", new());
        Assert.Equal("Es gibt mehrere Zelte — bitte zeltId angeben: Zelt 1 „Zelt-RDWC\"; Zelt 2 „Mutter\".", text);
        Assert.DoesNotContain(fork.VonWerkzeugen, a => a.Weg.StartsWith("api/settings/tents/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task sensoren_sagt_einmal_wenn_Home_Assistant_nicht_antwortet()
    {
        var fork = Fork().Antwort("GET", "api/ki-ha/zustaende", 503, """{"code":"ha_nicht_eingerichtet","message":"Home Assistant ist in Grow OS nicht eingerichtet."}""");
        var text = await Aufruf(fork, "sensoren", new() { ["zeltId"] = 1 });

        Assert.Contains("Lufttemp: sensor.zelt_t, aktiv", text);
        Assert.Contains("Werte aus Home Assistant: nicht lesbar — Nicht ausgeführt (Home Assistant lesen): Home Assistant ist in Grow OS nicht eingerichtet.", text);
        Assert.Single(fork.VonWerkzeugen, a => a.Weg == "api/ki-ha/zustaende");
    }

    // ------------------------------------------------------------------ sensor_zuordnen

    [Fact]
    public async Task Eine_neue_Messgroesse_aendert_nur_ihre_Zeile_und_belegt_das()
    {
        var fork = Fork();
        var text = await Aufruf(fork, "sensor_zuordnen", Zuordnen("Außen Temp.", "sensor.controller_temperatur"));

        var put = Assert.Single(fork.VonWerkzeugen, a => a.Methode == "PUT");
        Assert.Equal("api/settings/tents/1/sensors/OutsideTemperature", put.Weg);
        Assert.Equal("""{"haEntityId":"sensor.controller_temperatur"}""", put.Rumpf);
        Assert.Equal(
            "Außen Temp. an Zelt 1 „Zelt-RDWC\": vorher keine Zuordnung, jetzt sensor.controller_temperatur (gerade 20,9 °C; neue Sensor-Id 40). "
            + "Die übrigen 4 Zuordnungen sind unverändert (Ids gleich, vorher und nachher verglichen). Grow OS liest den Wert ab dem nächsten Messtakt.",
            text);
    }

    [Fact]
    public async Task Umhaengen_behaelt_die_Sensor_Id()
    {
        var text = await Aufruf(Fork(), "sensor_zuordnen", Zuordnen("Lufttemp", "sensor.controller_temperatur"));
        Assert.Contains("vorher sensor.zelt_t, jetzt sensor.controller_temperatur (gerade 20,9 °C; Sensor-Id 31 wie vorher)", text);
        Assert.Contains("Die übrigen 3 Zuordnungen sind unverändert", text);
    }

    [Fact]
    public async Task Verteilt_der_Fork_neue_Ids_wird_das_gemeldet()
    {
        // Bissnachweis für den Vergleich: so sähe es aus, wenn Grow OS doch alle Zeilen ersetzte.
        var text = await Aufruf(Fork(ersetztAlles: true), "sensor_zuordnen", Zuordnen("Außen Temp.", "sensor.controller_temperatur"));
        Assert.Contains("ACHTUNG: Bei anderen Zuordnungen hat sich etwas verändert: Sensor-Id 31 (AirTemperature) fehlt", text);
        Assert.DoesNotContain("unverändert", text);
    }

    [Theory]
    [InlineData("Außen Temp.")]
    [InlineData("aussen temp")]
    [InlineData("Aussentemperatur")]
    [InlineData("OutsideTemperature")]
    [InlineData("outside-temperature")]
    public async Task Die_Messgroesse_wird_tolerant_gefunden(string gesagt)
    {
        var fork = Fork();
        await Aufruf(fork, "sensor_zuordnen", Zuordnen(gesagt, "sensor.controller_temperatur"));
        Assert.Equal("api/settings/tents/1/sensors/OutsideTemperature", Assert.Single(fork.VonWerkzeugen, a => a.Methode == "PUT").Weg);
    }

    [Theory]
    [InlineData("Aussenluft", "sensor.controller_temperatur", "Nichts geändert: Messgröße „Aussenluft\" gibt es nicht. Möglich: Lufttemp, Luftfeuchte, Licht, pH, DO,")]
    [InlineData("Wasserstand", "sensor.controller_temperatur", "Nichts geändert: „Wasserstand\" ist nicht eindeutig: Wasserstand (L), Wasserstand (cm).")]
    [InlineData("Dosierung", "sensor.controller_temperatur", "gibt es nicht")]
    [InlineData("Außen Temp.", "switch.controller", "Nichts geändert: „Außen Temp.\" erwartet eine Entität der Art sensor; switch.controller ist switch.")]
    [InlineData("Außen Temp.", "sensor.a,sensor.b", "nicht genau eine Entity-Id")]
    [InlineData("Außen Temp.", "sensor.controler_temperatur", "Nichts geändert: In Home Assistant gibt es keine Entität sensor.controler_temperatur. Ähnlich: sensor.controller_temperatur.")]
    [InlineData("Lufttemp", "sensor.zelt_t", "Nichts geändert: Lufttemp an Zelt 1 „Zelt-RDWC\" zeigt schon auf sensor.zelt_t.")]
    [InlineData("pH", "", "Nichts geändert: pH an Zelt 1 „Zelt-RDWC\" ist schon frei.")]
    public async Task Was_nicht_geht_aendert_nichts_und_sagt_warum(string messgroesse, string entitaet, string erwartet)
    {
        var fork = Fork();
        var text = await Aufruf(fork, "sensor_zuordnen", Zuordnen(messgroesse, entitaet));

        Assert.Contains(erwartet, text);
        Assert.DoesNotContain(fork.VonWerkzeugen, a => a.Methode == "PUT");
    }

    [Fact]
    public async Task Ein_unbekanntes_Zelt_aendert_nichts()
    {
        var fork = Fork();
        var text = await Aufruf(fork, "sensor_zuordnen", new() { ["zeltId"] = 7, ["messgroesse"] = "Außen Temp.", ["entitaet"] = "sensor.controller_temperatur" });
        Assert.Equal("Nichts geändert: Es gibt kein Zelt mit der Id 7. Möglich: 1.", text);
        Assert.DoesNotContain(fork.VonWerkzeugen, a => a.Methode == "PUT");
    }

    [Fact]
    public async Task Ohne_Home_Assistant_wird_nicht_blind_zugeordnet()
    {
        var fork = Fork().Antwort("GET", "api/ki-ha/zustaende", 502, """{"code":"ha_nicht_erreichbar","message":"Home Assistant ist gerade nicht erreichbar."}""");
        var text = await Aufruf(fork, "sensor_zuordnen", Zuordnen("Außen Temp.", "sensor.controller_temperatur"));

        Assert.StartsWith("Nichts geändert: Ob es sensor.controller_temperatur in Home Assistant gibt, ließ sich nicht prüfen.", text);
        Assert.DoesNotContain(fork.VonWerkzeugen, a => a.Methode == "PUT");
    }

    [Theory]
    [InlineData("")]
    [InlineData("keine")]
    [InlineData(" Lösen ")]
    public async Task Loesen_schickt_keine_Entitaet_und_die_Zeile_bleibt(string entitaet)
    {
        var fork = Fork();
        var text = await Aufruf(fork, "sensor_zuordnen", Zuordnen("Luftfeuchte", entitaet));

        var put = Assert.Single(fork.VonWerkzeugen, a => a.Methode == "PUT");
        Assert.Equal("""{"haEntityId":null}""", put.Rumpf);
        Assert.DoesNotContain(fork.VonWerkzeugen, a => a.Weg == "api/ki-ha/zustaende");
        Assert.Equal(
            "Luftfeuchte an Zelt 1 „Zelt-RDWC\": Zuordnung gelöst (vorher sensor.zelt_rlf). Die Zeile bleibt mit Sensor-Id 32 inaktiv stehen, so behalten Geräte und Meldungen ihren Verweis. "
            + "Die übrigen 3 Zuordnungen sind unverändert (Ids gleich, vorher und nachher verglichen).",
            text);
    }

    [Fact]
    public async Task Bei_doppelten_Zeilen_nennt_Loesen_alle_und_Setzen_die_die_gelesen_wird()
    {
        var loesen = await Aufruf(Fork(doppelt: true), "sensor_zuordnen", Zuordnen("Luftfeuchte", "keine"));
        Assert.Contains("Zuordnung gelöst (vorher sensor.zelt_rlf_2). Die Zeilen mit den Sensor-Ids 32, 35 bleiben inaktiv stehen", loesen);
        Assert.DoesNotContain("ACHTUNG", loesen);

        var setzen = await Aufruf(Fork(doppelt: true), "sensor_zuordnen", Zuordnen("Luftfeuchte", "sensor.zelt_t"));
        Assert.Contains("vorher sensor.zelt_rlf_2, jetzt sensor.zelt_t (gerade 24,4 °C; Sensor-Id 35 wie vorher)", setzen);
        Assert.DoesNotContain("ACHTUNG", setzen);
    }

    [Fact]
    public async Task Bleibt_nach_dem_Loesen_eine_Zeile_aktiv_wird_das_gemeldet()
    {
        // Gegenprobe zum Fall darüber: ein Grow OS, das die zweite Zeile nicht stilllegt (Prüfer forkai.247).
        var fork = Fork(doppelt: true).Antwort("PUT", @"api/settings/tents/1/sensors/\w+", 200, "{}");
        var text = await Aufruf(fork, "sensor_zuordnen", Zuordnen("Luftfeuchte", "keine"));
        Assert.StartsWith("ACHTUNG: Grow OS hat gespeichert, aber Luftfeuchte ist danach noch zugeordnet.", text);
    }

    /// <summary>
    /// Ein Werkzeug mit <c>NurLesen</c> fällt aus den Schreib-Prüfungen heraus. Dafür muss es wirklich nur lesen — über alle
    /// solchen Werkzeuge, nicht nur über <c>sensoren</c> (Prüfer forkai.247).
    /// </summary>
    [Fact]
    public async Task Jedes_NurLesen_Werkzeug_fragt_Grow_OS_nur_mit_GET()
    {
        var nurLesen = Werkzeugkasten.Werkzeugmethoden().Where(m => m.GetCustomAttribute<BrauchtForkSchluesselAttribute>()?.NurLesen == true).ToList();
        Assert.True(nurLesen.Count >= 1, "Kein NurLesen-Werkzeug gefunden — die Prüfung sähe nichts.");
        foreach (var methode in nurLesen)
        {
            var fork = ForkAttrappe.MitGrow();
            await Werkzeugkasten.AufrufenAsync(methode, Werkzeugkasten.Leser(fork, Werkzeugkasten.ForkSchluessel));
            Assert.NotEmpty(fork.VonWerkzeugen);
            Assert.All(fork.VonWerkzeugen, a => Assert.True(a.Methode == "GET", $"{Werkzeugkasten.Name(methode)} schickt {a.Methode} {a.Weg}."));
        }
    }

    [Fact]
    public async Task Die_Absage_von_Grow_OS_kommt_als_Satz()
    {
        var fork = Fork().Antwort("PUT", @"api/settings/tents/1/sensors/\w+", 400,
            """{"code":"sensor_domain_passt_nicht","message":"„Außen Temp.“ erwartet eine Entität der Art sensor; switch.x ist switch."}""");
        var text = await Aufruf(fork, "sensor_zuordnen", Zuordnen("Außen Temp.", "sensor.controller_temperatur"));
        Assert.StartsWith("Nicht ausgeführt (Sensor zuordnen): „Außen Temp.“ erwartet", text);
    }

    [Fact]
    public async Task Der_Rumpf_passt_zum_Vertrag_in_Grow_OS()
    {
        var fork = Fork();
        await Aufruf(fork, "sensor_zuordnen", Zuordnen("Außen Temp.", "sensor.controller_temperatur"));
        var typ = GrowOsBau.Typ("GrowDiary.Web.Api.Contracts.SetTentSensorRequest");
        var gelesen = JsonSerializer.Deserialize(Assert.Single(fork.VonWerkzeugen, a => a.Methode == "PUT").Rumpf!, typ,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow });
        Assert.Equal("sensor.controller_temperatur", typ.GetProperty("HaEntityId")!.GetValue(gelesen));
    }

    [Fact]
    public void Der_Weg_nennt_die_Messgroesse_mit_dem_Namen_den_Grow_OS_liest()
    {
        // Der Katalog der Attrappe nennt nur Namen, die das Enum in Grow OS wirklich hat.
        var enumWerte = Enum.GetNames(GrowOsBau.Typ("GrowDiary.Web.Models.SensorMetricType"));
        var katalog = JsonNode.Parse(ForkAttrappe.Katalog)!.AsArray().Select(e => e!["metricType"]!.GetValue<string>()).ToList();
        Assert.True(katalog.Count >= 10);
        Assert.All(katalog, m => Assert.Contains(m, enumWerte));
    }

    // ------------------------------------------------------------------ Stufen

    [Theory]
    [InlineData("sensoren", Stufen.Verwaltung, true)]
    [InlineData("sensor_zuordnen", Stufen.Verwaltung, false)]
    [InlineData("journal_loeschen", Stufen.Dokumentieren, false)]
    [InlineData("messung_loeschen", Stufen.Dokumentieren, false)]
    [InlineData("grenzwert_setzen", Stufen.GrowPlanen, false)]
    [InlineData("lichtplan_setzen", Stufen.GrowPlanen, false)]
    public void Die_neuen_Werkzeuge_tragen_ihre_Stufe(string werkzeug, string stufe, bool nurLesen)
    {
        var attribut = Werkzeugkasten.Werkzeug(werkzeug).GetCustomAttribute<BrauchtForkSchluesselAttribute>()!;
        Assert.Equal(stufe, attribut.Stufe);
        Assert.Equal(nurLesen, attribut.NurLesen);
    }

    [Fact]
    public async Task sensoren_schreibt_nie()
    {
        var fork = Fork();
        await Aufruf(fork, "sensoren", new() { ["zeltId"] = 1 });
        Assert.All(fork.VonWerkzeugen, a => Assert.Equal("GET", a.Methode));
    }

    [Theory]
    [InlineData("sensor.controller_temperatur", "sensor.controler_temperatur", 1)]
    [InlineData("abc", "abc", 0)]
    [InlineData("", "abc", 3)]
    public void Der_Abstand_zaehlt_Tippfehler(string a, string b, int erwartet)
        => Assert.Equal(erwartet, SchreibWerkzeuge.Abstand(a, b));
}
