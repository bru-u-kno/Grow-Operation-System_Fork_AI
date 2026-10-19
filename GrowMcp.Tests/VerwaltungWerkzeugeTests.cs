using System.Text.Json;
using System.Text.Json.Nodes;
using GrowMcp.Tools;

namespace GrowMcp.Tests;

/// <summary>
/// Fork AI (A-027, Schritt 5): <c>raum</c>, <c>raum_setzen</c>, <c>zelt_aufstellraum_setzen</c>.
/// </summary>
/// <remarks>
/// Die Zelt-Attrappe verhält sich wie der Fork: ein PUT ersetzt die genannten Felder, ein weggelassenes
/// <c>sensors</c>/<c>cameras</c> lässt die gespeicherten stehen. Die Variante <c>VergisstSensoren</c> spielt den Fehler nach,
/// vor dem die Prüfsumme schützen soll (ein Server, der ohne Angabe die Sensoren löscht).
/// </remarks>
public sealed class VerwaltungWerkzeugeTests
{
    private const string Zelt = """
        {"id":1,"name":"Blütezelt","kind":"Grow Tent","tentType":"Hydro","status":"Active","notes":"Notiz","displayOrder":1,
         "accentColor":"#69b578","widthCm":120,"depthCm":120,"tentHeightCm":200,"lightType":"LED","lightWatt":480,
         "lightController":null,"lightControllerEntityId":null,"exhaustFanCount":1,"exhaustM3h":300,"circulationFanCount":2,
         "hvacController":null,"hvacControllerEntityId":null,"co2Available":true,"hasCo2Enrichment":false,"raumId":null,
         "cameraEntityId":"camera.zelt","activeGrowCount":1,"archivedGrowCount":0,"activeSetupCount":1,"archivedSetupCount":0,
         "sensors":[{"id":31,"tentId":1,"metricType":"Temperature","haEntityId":"sensor.zelt_t","displayLabel":null,"isActive":true}],
         "cameras":["camera.zelt"],"leafTempOffsetC":-2,"leafOffsetSyncService":null,"leafOffsetSyncPort":1}
        """;

    private const string RaumLeer = """{"id":"haupt","name":"Keller","vorgabe":"Aufstellraum","zelte":[]}""";

    /// <summary>Ein Fork mit einem Raum „Keller“ und einem Zelt, das wie das echte gespeichert wird.</summary>
    private static ForkAttrappe Fork(bool vergisstSensoren = false, string zelt = Zelt)
    {
        var stand = JsonNode.Parse(zelt)!.AsObject();
        return new ForkAttrappe()
            .Antwort("GET", @"api/steuerung/raum", 200, RaumLeer)
            .Antwort("PUT", @"api/steuerung/raum", a => (200, $$"""{"id":"haupt","name":{{JsonNode.Parse(a.Rumpf!)!["name"]?.ToJsonString() ?? "\"Aufstellraum\""}},"vorgabe":"Aufstellraum","zelte":[]}"""))
            .Antwort("GET", @"api/settings/tents", 200, "[" + zelt + "]")
            .Antwort("GET", @"api/settings/tents/\d+", 404, """{"code":"tent_not_found","message":"Zelt existiert nicht."}""")
            .Antwort("GET", @"api/settings/tents/1", _ => (200, stand.ToJsonString()))
            .Antwort("PUT", @"api/settings/tents/1", a =>
            {
                var body = JsonNode.Parse(a.Rumpf!)!.AsObject();
                foreach (var (name, wert) in body) stand[name] = wert?.DeepClone();
                if (vergisstSensoren && !body.ContainsKey("sensors")) stand["sensors"] = new JsonArray();
                return (200, stand.ToJsonString());
            });
    }

    private static Task<string> Aufruf(ForkAttrappe fork, string werkzeug, Dictionary<string, object?>? werte = null, string? schluessel = Werkzeugkasten.ForkSchluessel)
        => Werkzeugkasten.TextAsync(werkzeug, Werkzeugkasten.Leser(fork, schluessel), werte);

    private static JsonObject Rumpf(Angekommen a) => JsonNode.Parse(a.Rumpf!)!.AsObject();

    // ------------------------------------------------------------------ raum

    [Fact]
    public async Task raum_nennt_Namen_Vorgabe_und_die_Zelte_der_Anlage()
    {
        var text = await Aufruf(Fork(), "raum");

        Assert.Contains("Raum „Keller\" (Id haupt, Vorgabe: Aufstellraum)", text);
        Assert.Contains("Zelte darin: keine.", text);
        Assert.Contains("Zelt 1 „Blütezelt\" (ohne Raum)", text);
    }

    [Fact]
    public async Task raum_ohne_Verwaltung_zeigt_den_Raum_und_sagt_dass_die_Zeltliste_fehlt()
    {
        var fork = Fork().Antwort("GET", @"api/settings/tents", 403, """{"code":"ki_stufe_fehlt","message":"Stufe fehlt"}""");
        var text = await Aufruf(fork, "raum");

        Assert.Contains("Raum „Keller\"", text);
        Assert.Contains("Stufe „Verwaltung\"", text);
    }

    // ------------------------------------------------------------------ raum_setzen

    [Fact]
    public async Task raum_setzen_schickt_nur_den_Namen()
    {
        var fork = Fork();
        var text = await Aufruf(fork, "raum_setzen", new() { ["name"] = "  Werkstatt " });

        var put = Assert.Single(fork.VonWerkzeugen, a => a.Methode == "PUT");
        Assert.Equal("api/steuerung/raum", put.Weg);
        Assert.Equal(["name"], Rumpf(put).Select(p => p.Key).ToArray());
        Assert.Equal("Werkstatt", Rumpf(put)["name"]!.GetValue<string>());
        Assert.Contains("von „Keller\" auf „Werkstatt\" gesetzt", text);
    }

    [Fact]
    public async Task raum_setzen_ohne_Namen_setzt_auf_die_Vorgabe_zurueck()
    {
        var fork = Fork();
        var text = await Aufruf(fork, "raum_setzen", new() { ["name"] = "  " });

        Assert.Null(Rumpf(Assert.Single(fork.VonWerkzeugen, a => a.Methode == "PUT"))["name"]);
        Assert.Contains("auf die Vorgabe „Aufstellraum\" zurückgesetzt", text);
    }

    [Fact]
    public async Task raum_setzen_gibt_die_Absage_des_Forks_als_Satz_weiter()
    {
        var fork = Fork().Antwort("PUT", @"api/steuerung/raum", 403, """{"code":"ki_stufe_fehlt","message":"Für diese Aktion fehlt die Stufe Verwaltung."}""");
        var text = await Aufruf(fork, "raum_setzen", new() { ["name"] = "Keller" });

        Assert.StartsWith("Nicht ausgeführt", text);
        Assert.Contains("Verwaltung", text);
    }

    // ------------------------------------------------------------------ zelt_aufstellraum_setzen

    [Fact]
    public async Task Das_Zelt_wird_zugeordnet_und_nur_raumId_aendert_sich()
    {
        var fork = Fork();
        var text = await Aufruf(fork, "zelt_aufstellraum_setzen", new() { ["zeltId"] = 1, ["raum"] = "keller" });

        var put = Assert.Single(fork.VonWerkzeugen, a => a.Methode == "PUT" && a.Weg == "api/settings/tents/1");
        var rumpf = Rumpf(put);
        Assert.Equal("haupt", rumpf["raumId"]!.GetValue<string>());

        // Sensoren und Kameras gehen nicht mit: Cultivate Control ersetzt sonst die Sensor-Zeilen (neue Ids).
        foreach (var weg in new[] { "sensors", "cameras", "cameraEntityId", "id", "activeGrowCount" }) Assert.False(rumpf.ContainsKey(weg), weg);

        // Alles andere geht unverändert zurück.
        var vorher = JsonNode.Parse(Zelt)!.AsObject();
        foreach (var (name, wert) in rumpf.Where(p => p.Key != "raumId"))
        {
            Assert.True(JsonNode.DeepEquals(vorher[name], wert), $"{name} wurde verändert.");
        }

        Assert.Contains("Zelt 1 „Blütezelt\" ist jetzt im Raum „Keller\"", text);
        Assert.Contains("Prüfsumme", text);
        Assert.DoesNotContain("ACHTUNG", text);
    }

    [Fact]
    public async Task Die_Zuordnung_laesst_sich_mit_keiner_loesen()
    {
        var gesetzt = Zelt.Replace("\"raumId\":null", "\"raumId\":\"haupt\"");
        var fork = Fork(zelt: gesetzt);
        var text = await Aufruf(fork, "zelt_aufstellraum_setzen", new() { ["zeltId"] = 1, ["raum"] = "keiner" });

        Assert.Null(Rumpf(Assert.Single(fork.VonWerkzeugen, a => a.Methode == "PUT" && a.Weg.StartsWith("api/settings/tents", StringComparison.Ordinal)))["raumId"]);
        Assert.Contains("keinem Raum zugeordnet", text);
    }

    [Fact]
    public async Task Verliert_der_Fork_beim_Speichern_Sensoren_wird_es_gemeldet_und_der_Stand_zurueckgeschrieben()
    {
        // Bissnachweis: ohne die Gegenprobe merkte niemand, dass die Sensor-Zuordnung weg ist.
        var fork = Fork(vergisstSensoren: true);
        var text = await Aufruf(fork, "zelt_aufstellraum_setzen", new() { ["zeltId"] = 1, ["raum"] = "Keller" });

        Assert.StartsWith("ACHTUNG", text);
        Assert.Contains("sensors", text);
        Assert.Equal(2, fork.VonWerkzeugen.Count(a => a.Methode == "PUT" && a.Weg == "api/settings/tents/1"));
        Assert.Null(Rumpf(fork.VonWerkzeugen.Last(a => a.Methode == "PUT"))["raumId"]);
    }

    [Fact]
    public async Task Ein_unbekanntes_Zelt_aendert_nichts_und_nennt_die_moeglichen()
    {
        var fork = Fork();
        var text = await Aufruf(fork, "zelt_aufstellraum_setzen", new() { ["zeltId"] = 7, ["raum"] = "Keller" });

        Assert.DoesNotContain(fork.VonWerkzeugen, a => a.Methode == "PUT");
        Assert.Equal("Nichts geändert: Es gibt kein Zelt mit der Id 7. Möglich: 1.", text);
    }

    [Fact]
    public async Task Ein_unbekannter_Raum_aendert_nichts()
    {
        var fork = Fork();
        var text = await Aufruf(fork, "zelt_aufstellraum_setzen", new() { ["zeltId"] = 1, ["raum"] = "Garage" });

        Assert.DoesNotContain(fork.VonWerkzeugen, a => a.Methode == "PUT");
        Assert.StartsWith("Nichts geändert: „Garage\" ist nicht der Raum. Der Raum heißt „Keller\" (Id haupt)", text);
    }

    [Fact]
    public async Task Steht_das_Zelt_schon_im_Raum_wird_nichts_geschrieben()
    {
        var fork = Fork(zelt: Zelt.Replace("\"raumId\":null", "\"raumId\":\"haupt\""));
        var text = await Aufruf(fork, "zelt_aufstellraum_setzen", new() { ["zeltId"] = 1, ["raum"] = "Keller" });

        Assert.DoesNotContain(fork.VonWerkzeugen, a => a.Methode == "PUT");
        Assert.Contains("schon im Raum „Keller\"", text);
    }

    [Fact]
    public async Task Die_Absage_wegen_fehlender_Stufe_kommt_als_Satz_und_das_Zelt_bleibt()
    {
        var fork = Fork().Antwort("PUT", @"api/settings/tents/1", 403, """{"code":"ki_stufe_fehlt","message":"Für diese Aktion fehlt die Stufe Verwaltung."}""");
        var text = await Aufruf(fork, "zelt_aufstellraum_setzen", new() { ["zeltId"] = 1, ["raum"] = "Keller" });

        Assert.StartsWith("Nicht ausgeführt", text);
        Assert.Contains("Verwaltung", text);
    }

    // ------------------------------------------------------------------ Grenzen

    [Theory]
    [InlineData("raum")]
    [InlineData("raum_setzen")]
    [InlineData("zelt_aufstellraum_setzen")]
    public async Task Mit_dem_MCP_Schluessel_geht_nichts_an_Grow_OS(string werkzeug)
    {
        var fork = Fork();
        var text = await Aufruf(fork, werkzeug, schluessel: null);

        Assert.Empty(fork.VonWerkzeugen);
        Assert.Contains("Schlüssel aus Cultivate Control", text);
    }

    [Theory]
    [InlineData("raum_setzen")]
    [InlineData("zelt_aufstellraum_setzen")]
    public void Die_Schreibwerkzeuge_verlangen_die_Stufe_Verwaltung(string werkzeug)
    {
        var attribut = Werkzeugkasten.Werkzeug(werkzeug)
            .GetCustomAttributes(typeof(BrauchtForkSchluesselAttribute), false)
            .Cast<BrauchtForkSchluesselAttribute>().Single();
        Assert.Equal(Stufen.Verwaltung, attribut.Stufe);
    }

    [Fact]
    public async Task Kein_Werkzeug_erreicht_Schluesselverwaltung_oder_KI_Schalter()
    {
        // Die Verwaltungswerkzeuge sind eng; ein freies „Einstellung ändern“ gibt es nicht. Hier: Über ALLE Werkzeuge
        // schreibt keines auf die Wege der Schlüsselverwaltung oder des KI-Hauptschalters.
        var gesperrt = new[] { "api/settings/ki", "api/settings/ki-zugriff", "api/ki-zugriff" };
        foreach (var methode in Werkzeugkasten.Werkzeugmethoden())
        {
            var fork = ForkAttrappe.MitGrow();
            await Werkzeugkasten.AufrufenAsync(methode, Werkzeugkasten.Leser(fork, Werkzeugkasten.ForkSchluessel));
            Assert.DoesNotContain(fork.VonWerkzeugen, a => a.Methode != "GET" && gesperrt.Any(g => a.Weg.StartsWith(g, StringComparison.Ordinal)));
        }
    }

    [Fact]
    public async Task Ein_leeres_Raumargument_loest_die_Zuordnung_nicht()
    {
        var fork = Fork(zelt: Zelt.Replace("\"raumId\":null", "\"raumId\":\"haupt\""));
        var text = await Aufruf(fork, "zelt_aufstellraum_setzen", new() { ["zeltId"] = 1, ["raum"] = "" });

        Assert.DoesNotContain(fork.VonWerkzeugen, a => a.Methode == "PUT");
        Assert.StartsWith("Nichts geändert", text);
    }

    [Fact]
    public async Task Eine_Aenderung_der_Zaehlfelder_zwischen_den_Abfragen_ist_kein_Fehlalarm()
    {
        var fork = Fork().Antwort("GET", @"api/settings/tents/1", a =>
            (200, Zelt.Replace("\"activeGrowCount\":1", a.Methode == "GET" && fork2Aufrufe++ > 0 ? "\"activeGrowCount\":0" : "\"activeGrowCount\":1")));
        var text = await Aufruf(fork, "zelt_aufstellraum_setzen", new() { ["zeltId"] = 1, ["raum"] = "Keller" });

        Assert.DoesNotContain("ACHTUNG", text);
    }

    private int fork2Aufrufe;

    [Fact]
    public void Die_Pruefsumme_ignoriert_raumId_und_sieht_jede_andere_Aenderung()
    {
        var a = JsonNode.Parse(Zelt)!.AsObject();
        var b = (JsonObject)a.DeepClone();
        b["raumId"] = "haupt";
        Assert.Equal(SchreibWerkzeuge.Pruefsumme(a), SchreibWerkzeuge.Pruefsumme(b));
        Assert.Empty(SchreibWerkzeuge.AbweichendeFelder(a, b));

        b["lightWatt"] = 481;
        Assert.NotEqual(SchreibWerkzeuge.Pruefsumme(a), SchreibWerkzeuge.Pruefsumme(b));
        Assert.Equal(["lightWatt"], SchreibWerkzeuge.AbweichendeFelder(a, b));
    }
}
