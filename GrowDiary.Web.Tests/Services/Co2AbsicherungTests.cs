using System.Text.Json.Nodes;
using GrowDiary.Web.Services;
using Art = GrowDiary.Web.Services.Co2Absicherung.Art;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Die Absicherung vorhandener CO₂-Automationen — geprüft an den echten,
/// von Hand gebauten Automationen der Anlage, aus der die Vorlagen stammen
/// (Stand 30.09.2026, nur die Beschreibung der Dosierung ist gekürzt).
/// </summary>
/// <remarks>
/// Ein ausgedachter Testbestand hätte genau die Formen, die der Prüfer kennt.
/// Die echten Automationen haben die Formen, die es gibt: zwei Auslöser im
/// Wächter (Steckdose UND Modus), ein Dosierfenster im Abbruch, eine
/// Push-Meldung, eine Rückfall-Impulsdauer aus einem Helfer.
/// </remarks>
public sealed class Co2AbsicherungTests
{
    private const string Fuehler = "sensor.big_co2_light_sensor_co2";
    private const string Dosierung = "automation.co2_dosierung_rdwc_port_5";

    private static readonly HashSet<string> Ports = new(StringComparer.Ordinal)
    {
        "select.rdwc_venti_aktiver_modus_2", "binary_sensor.big_port_5_zustand",
    };

    private static readonly Co2Absicherung.Rahmen Rahmen =
        new(Fuehler, new HashSet<string>(StringComparer.Ordinal) { Dosierung }, Ports);

    private static JsonObject Echt(string name)
    {
        var pfad = Path.Combine(AppContext.BaseDirectory, "Services", "Daten", "co2-handgebaut", name + ".json");
        return (JsonObject)JsonNode.Parse(File.ReadAllText(pfad))!;
    }

    private static IReadOnlyList<Art> Arten(JsonObject config, Co2Absicherung.Rahmen? rahmen = null)
        => Co2Absicherung.Pruefen(config, rahmen ?? Rahmen).Select(b => b.Art).ToList();

    // ------------------------------------------------------------ Selbsttest

    [Fact]
    public void Selbsttest_DieEchtenAutomationenZeigenAlleVierSchwachstellen()
    {
        // Ohne diesen Nachweis wäre jede „keine Befunde nach dem Absichern“-
        // Prüfung unten auch dann grün, wenn die Prüfung gar nichts sieht.
        Assert.True(Co2Absicherung.IstDosierung(Echt("dosierung")), "Die echte Dosierung wird nicht als Dosierung erkannt.");
        Assert.Equal([Art.SchleifeHaeltBeiAusfall, Art.OeffnetOhneMesswert], Arten(Echt("dosierung")));
        Assert.Equal([Art.WaechterUebersiehtNeustart], Arten(Echt("waechter")));
        Assert.Equal([Art.NotAusWirdZurueckgenommen], Arten(Echt("licht-aus-sicherung")));
    }

    [Theory]
    [InlineData("dosierung")]
    [InlineData("waechter")]
    [InlineData("licht-aus-sicherung")]
    public void NachDemAbsichern_KeinBefundMehr_UndEinZweitesMalAendertNichts(string name)
    {
        var einmal = Co2Absicherung.Absichern(Echt(name), Rahmen);
        Assert.Empty(Arten(einmal));

        var zweimal = Co2Absicherung.Absichern(einmal, Rahmen);
        Assert.True(JsonNode.DeepEquals(einmal, zweimal), "Ein zweites Absichern hat noch einmal etwas geändert.");
    }

    [Fact]
    public void Absichern_VeraendertDasOriginalNicht()
    {
        var original = Echt("dosierung");
        var vorher = original.ToJsonString();
        Co2Absicherung.Absichern(original, Rahmen);
        Assert.Equal(vorher, original.ToJsonString());
    }

    // ------------------------------------------------------------ Dosierung

    private static JsonObject Schleife(JsonObject config) => (JsonObject)config["actions"]![0]!["repeat"]!;

    [Fact]
    public void Dosierung_AbbruchGreiftAuchBeiNichtVerfuegbar()
    {
        var neu = Co2Absicherung.Absichern(Echt("dosierung"), Rahmen);
        var oder = (JsonArray)Schleife(neu)["until"]![0]!["conditions"]!;

        foreach (var entity in new[] { "binary_sensor.co2_bedarf", "binary_sensor.klein_abluft_zustand", "binary_sensor.co2_klima_ok" })
        {
            Assert.Contains(oder, b => b?["condition"]?.ToString() == "not"
                && b["conditions"]![0]!["entity_id"]!.ToString() == entity
                && b["conditions"]![0]!["state"]!.ToString() == "on");
        }

        Assert.DoesNotContain("\"state\":\"off\"", Schleife(neu)["until"]!.ToJsonString());
    }

    [Fact]
    public void Dosierung_BehaeltAllesAndere()
    {
        var alt = Echt("dosierung");
        var neu = Co2Absicherung.Absichern(alt, Rahmen);

        Assert.True(JsonNode.DeepEquals(alt["triggers"], neu["triggers"]));
        Assert.True(JsonNode.DeepEquals(alt["conditions"], neu["conditions"]), "Reconnect-Schutz oder Dosierfenster geändert.");
        Assert.Equal(alt["description"]!.ToString(), neu["description"]!.ToString());

        // Jeder alte Schritt steht noch da, in derselben Reihenfolge — nur die Sperre kommt dazu.
        var alteSchritte = ((JsonArray)Schleife(alt)["sequence"]!).Select(s => s!.ToJsonString()).ToList();
        var neueSchritte = ((JsonArray)Schleife(neu)["sequence"]!)
            .Where(s => s?["alias"]?.ToString() != Co2Absicherung.SperreAlias)
            .Select(s => s!.ToJsonString()).ToList();
        Assert.Equal(alteSchritte, neueSchritte);

        // Das Dosierfenster und die Höchstzahl im Abbruch bleiben.
        var bis = Schleife(neu)["until"]!.ToJsonString();
        Assert.Contains("Dosierfenster zu Ende", bis);
        Assert.Contains("co2_max_impulse_je_zyklus", bis);
    }

    [Fact]
    public void Dosierung_SperreStehtVorDemOeffnen_UndPrueftFuehlerUndImpulsBedarf()
    {
        var ablauf = ((JsonArray)Schleife(Co2Absicherung.Absichern(Echt("dosierung"), Rahmen))["sequence"]!).ToList();

        var sperre = ablauf.FindIndex(s => s?["alias"]?.ToString() == Co2Absicherung.SperreAlias);
        var oeffnen = ablauf.FindIndex(s => s?["data"]?["option"]?.ToString() == "On");
        Assert.True(oeffnen >= 0, "Kein Öffnen gefunden — der Test sieht die Schleife nicht.");
        Assert.Equal(oeffnen - 1, sperre);

        var vorschrift = ablauf[sperre]!["value_template"]!.ToString();
        Assert.Equal(
            "{{ states('sensor.big_co2_light_sensor_co2') | float(-1) > 0 }}",
            vorschrift);
    }

    [Fact]
    public void Dosierung_InDerEchtenAbbruchForm_KommtDerAbbruchInsOder()
    {
        // Die echte Dosierung hat until: [ { or: [...] } ] — genau dieser Fall.
        var neu = Co2Absicherung.Absichern(Echt("dosierung"), Rahmen);
        var bis = Schleife(neu)["until"]!.AsArray();

        Assert.Single(bis);
        var oder = bis[0]!["conditions"]!.AsArray();
        var abbruch = Assert.Single(oder, b => b?["alias"]?.GetValue<string>() == Co2Absicherung.AbbruchAlias);
        Assert.Equal("{{ states('sensor.big_co2_light_sensor_co2') | float(-1) <= 0 }}", abbruch!["value_template"]!.ToString());
    }

    [Fact]
    public void Dosierung_DieRueckfallImpulsdauerBleibtWirksam()
    {
        // Die Handfassung dosiert mit co2_impulsdauer weiter, wenn der
        // Rechenwert ausfällt. Die Sperre prüft nur den Fühler — sonst wäre
        // diese Entscheidung still abgeschaltet.
        var ablauf = Schleife(Co2Absicherung.Absichern(Echt("dosierung"), Rahmen))["sequence"]!.AsArray();
        var sperre = ablauf.Single(s => s?["alias"]?.GetValue<string>() == Co2Absicherung.SperreAlias)!;
        Assert.DoesNotContain("co2_impuls_bedarf", sperre["value_template"]!.ToString());
    }

    [Theory]
    [InlineData("for")]
    [InlineData("klima")]
    [InlineData("liste")]
    public void Dosierung_OffWirdNurUmgedrehtWoEsGleichwertigIst(string fall)
    {
        var config = Echt("dosierung");
        var oder = (JsonArray)Schleife(config)["until"]![0]!["conditions"]!;
        var bedingung = (JsonObject)oder[0]!;
        switch (fall)
        {
            case "for": bedingung["for"] = "00:02:00"; break;                         // „seit 2 min aus“
            case "klima": bedingung["entity_id"] = "climate.entfeuchter"; break;     // kennt kein „on“
            case "liste": bedingung["entity_id"] = new JsonArray("binary_sensor.a", "binary_sensor.b"); break;
        }
        var vorher = bedingung.ToJsonString();

        var neu = Co2Absicherung.Absichern(config, Rahmen);
        Assert.Equal(vorher, Schleife(neu)["until"]![0]!["conditions"]![0]!.ToJsonString());

        // Die beiden übrigen werden weiterhin umgedreht.
        Assert.Equal(2, Schleife(neu)["until"]![0]!["conditions"]!.AsArray()
            .Count(b => b?["condition"]?.ToString() == "not" && b["conditions"]?[0]?["state"]?.ToString() == "on"));
    }

    [Fact]
    public void Dosierung_OhneZugeordnetenFuehler_WirdDieSperreNichtErfunden()
    {
        var ohne = new Co2Absicherung.Rahmen(null, Rahmen.Dosierungen, Ports);

        var befund = Co2Absicherung.Pruefen(Echt("dosierung"), ohne).Single(b => b.Art == Art.OeffnetOhneMesswert);
        Assert.False(befund.Behebbar);
        Assert.Contains("CO₂-Sensor", befund.Hinweis);

        // Der Abbruch wird trotzdem behoben — er braucht keinen Fühler.
        var neu = Co2Absicherung.Absichern(Echt("dosierung"), ohne);
        Assert.Equal([Art.OeffnetOhneMesswert], Arten(neu, ohne));
        Assert.DoesNotContain(Co2Absicherung.SperreAlias, neu.ToJsonString());
    }

    [Fact]
    public void Dosierung_MitMehrerenAbbruchBedingungen_WirdDasBisherigeEingefasst()
    {
        // until: [a, b] heisst „a UND b“. Den Abbruch einfach anzuhängen, hiesse
        // „a UND b UND Fühler fehlt“ — er griffe fast nie.
        var config = Echt("dosierung");
        var bis = (JsonArray)Schleife(config)["until"]!;
        var oder = (JsonArray)bis[0]!["conditions"]!;
        var a = oder[0]!.DeepClone();
        var b = oder[1]!.DeepClone();
        bis.Clear();
        bis.Add(a);
        bis.Add(b);

        var neu = Schleife(Co2Absicherung.Absichern(config, Rahmen))["until"]!.AsArray();
        Assert.Single(neu);
        Assert.Equal("or", neu[0]!["condition"]!.ToString());
        Assert.Equal("and", neu[0]!["conditions"]![0]!["condition"]!.ToString());
        Assert.Equal(Co2Absicherung.AbbruchAlias, neu[0]!["conditions"]![1]!["alias"]!.ToString());
    }

    // ------------------------------------------------------------ Wächter

    [Fact]
    public void Waechter_SiehtBeimStartUndImTaktNach_MitDerEigenenWartezeit()
    {
        var alt = Echt("waechter");
        var neu = Co2Absicherung.Absichern(alt, Rahmen);

        var ausloeser = (JsonArray)neu["triggers"]!;
        Assert.Contains(ausloeser, t => t?["trigger"]?.ToString() == "homeassistant" && t["event"]?.ToString() == "start");
        Assert.Contains(ausloeser, t => t?["trigger"]?.ToString() == "time_pattern" && t["minutes"]?.ToString() == "/1");

        var zweig = (JsonObject)neu["actions"]![0]!["choose"]![0]!;
        var text = zweig["conditions"]!.ToJsonString();

        // Die bisherigen Auslöser gelten weiter.
        Assert.Contains("\"port_lang\"", text);
        Assert.Contains("\"modus_lang\"", text);

        // Beide beobachteten Entitäten, mit ihrem eigenen Zustand und ihrer eigenen Wartezeit.
        Assert.Contains("\"entity_id\":\"binary_sensor.big_port_5_zustand\",\"state\":\"on\",\"for\":\"00:01:30\"", text);
        Assert.Contains("\"entity_id\":\"select.rdwc_venti_aktiver_modus_2\",\"state\":\"On\",\"for\":\"00:01:30\"", text);

        // Die Push-Meldung und der Zähler-Zweig bleiben.
        Assert.True(JsonNode.DeepEquals(alt["actions"]![0]!["choose"]![0]!["sequence"], zweig["sequence"]));
        Assert.True(JsonNode.DeepEquals(alt["actions"]![0]!["choose"]![1], neu["actions"]![0]!["choose"]![1]));
    }

    [Fact]
    public void Waechter_BeimNeustartNurWennDerPortWirklichOffenSteht()
    {
        var neu = Co2Absicherung.Absichern(Echt("waechter"), Rahmen);
        var oder = neu["actions"]![0]!["choose"]![0]!["conditions"]![0]!["conditions"]!.AsArray();

        var start = oder.Single(b => b!.ToJsonString().Contains(Co2Absicherung.NeustartKennung))!;
        var zustaende = start["conditions"]![1]!["conditions"]!.AsArray();
        Assert.Equal(2, zustaende.Count);
        Assert.All(zustaende, z =>
        {
            Assert.Equal("state", z!["condition"]!.ToString());
            Assert.Null(z["for"]); // beim Start: gleich, ohne Wartezeit
        });
    }

    [Fact]
    public void Waechter_OhneChoose_WirdErkanntUndGegatet()
    {
        // Die ganze Automation ist der Wächter: Port lange an → zu.
        var config = (JsonObject)JsonNode.Parse("""
            {"alias":"Wächter schlicht","triggers":[{"trigger":"state","entity_id":"binary_sensor.big_port_5_zustand","to":"on","for":"00:01:30"}],
             "actions":[{"action":"select.select_option","data":{"option":"Off"},"target":{"entity_id":"select.rdwc_venti_aktiver_modus_2"}}],"mode":"single"}
            """)!;
        Assert.Equal([Art.WaechterUebersiehtNeustart], Arten(config));

        var neu = Co2Absicherung.Absichern(config, Rahmen);
        Assert.Empty(Arten(neu));

        // Der bisherige Auslöser schließt weiter ohne Bedingung, die neuen nur bei offenem Port.
        var tor = neu["conditions"]![0]!["conditions"]!.AsArray();
        Assert.Equal("not", tor[0]!["condition"]!.ToString());
        Assert.Contains("\"for\":\"00:01:30\"", tor[2]!.ToJsonString());
        Assert.True(JsonNode.DeepEquals(config["actions"], neu["actions"]));
    }

    [Fact]
    public void Waechter_ImAltenFormat_WirdErkanntUndBleibtImAltenFormat()
    {
        var alt = Echt("waechter");
        var text = alt.ToJsonString()
            .Replace("\"triggers\":", "\"trigger\":")
            .Replace("\"actions\":", "\"action\":")
            .Replace("\"trigger\":\"state\"", "\"platform\":\"state\"");
        var config = (JsonObject)JsonNode.Parse(text)!;
        Assert.Equal([Art.WaechterUebersiehtNeustart], Arten(config));

        var neu = Co2Absicherung.Absichern(config, Rahmen);
        Assert.Empty(Arten(neu));
        Assert.Null(neu["triggers"]);
        Assert.Contains(neu["trigger"]!.AsArray(), t => t?["platform"]?.ToString() == "homeassistant");
    }

    [Fact]
    public void Waechter_EineAutomationDieNichtDenPortSchliesst_IstKeiner()
    {
        // „Licht 10 min an → Abluft aus“: Wartezeit-Auslöser und Ausschalten,
        // aber nicht der Dosier-Port.
        var config = (JsonObject)JsonNode.Parse("""
            {"triggers":[{"trigger":"state","entity_id":"light.zelt","to":"on","for":"00:10:00"}],
             "actions":[{"action":"switch.turn_off","target":{"entity_id":"switch.abluft"}}]}
            """)!;
        Assert.Empty(Arten(config));
    }

    [Fact]
    public void Waechter_DieForkVorlageGiltSchonAlsAbgesichert()
    {
        var vorlage = Vorlage("waechter");
        Assert.DoesNotContain(Art.WaechterUebersiehtNeustart, Arten(vorlage));

        // Selbsttest: ohne Start- und Takt-Auslöser wird sie als Wächter erkannt
        // — sonst wäre „kein Befund“ oben auch ohne Hinsehen wahr.
        var ohne = Vorlage("waechter");
        var ausloeser = ohne["triggers"]!.AsArray();
        foreach (var t in ausloeser.Where(t => t?["trigger"]?.ToString() is "homeassistant" or "time_pattern").ToList())
        {
            ausloeser.Remove(t);
        }
        Assert.Equal([Art.WaechterUebersiehtNeustart], Arten(ohne));
    }

    // ------------------------------------------------------------ Not-Aus

    [Fact]
    public void NotAus_WiederEinschaltenNurWennSieVorherAnWar()
    {
        var neu = Co2Absicherung.Absichern(Echt("licht-aus-sicherung"), Rahmen);
        var schritte = (JsonArray)neu["actions"]!;

        Assert.Equal(
            "{{ is_state('automation.co2_dosierung_rdwc_port_5', 'on') }}",
            schritte[0]!["variables"]![Co2Absicherung.WarAnPraefix + "co2_dosierung_rdwc_port_5"]!.ToString());
        Assert.Equal("automation.turn_off", schritte[1]!["action"]!.ToString());
        Assert.Equal(Co2Absicherung.WiederEinAlias, schritte[2]!["alias"]!.ToString());
        Assert.Equal("automation.turn_on", schritte[2]!["then"]![0]!["action"]!.ToString());
        Assert.Equal("Off", schritte[3]!["data"]!["option"]!.ToString());
        Assert.Equal(4, schritte.Count);
    }

    [Fact]
    public void NotAus_ZweiDosierungen_JedeMitEigenerMerkvariable()
    {
        const string zwei = "automation.co2_dosierung_zelt_2";
        var rahmen = new Co2Absicherung.Rahmen(Fuehler, new HashSet<string> { Dosierung, zwei }, Ports);
        var config = (JsonObject)JsonNode.Parse($$$"""
            {"triggers":[{"trigger":"state","entity_id":"binary_sensor.licht","to":"off"}],
             "actions":[
               {"action":"automation.turn_off","data":{"stop_actions":true},"target":{"entity_id":["{{{Dosierung}}}","{{{zwei}}}"]}},
               {"action":"automation.turn_on","target":{"entity_id":["{{{Dosierung}}}","{{{zwei}}}","automation.etwas_anderes"]}}]}
            """)!;
        Assert.Equal([Art.NotAusWirdZurueckgenommen], Arten(config, rahmen));

        var neu = Co2Absicherung.Absichern(config, rahmen);
        Assert.Empty(Arten(neu, rahmen));
        var schritte = neu["actions"]!.AsArray();

        var merken = schritte[0]!["variables"]!.AsObject();
        Assert.Equal($"{{{{ is_state('{Dosierung}', 'on') }}}}", merken[Co2Absicherung.WarAnPraefix + "co2_dosierung_rdwc_port_5"]!.ToString());
        Assert.Equal($"{{{{ is_state('{zwei}', 'on') }}}}", merken[Co2Absicherung.WarAnPraefix + "co2_dosierung_zelt_2"]!.ToString());

        // Jede Dosierung schaltet nur nach ihrem eigenen Merker wieder ein.
        Assert.Equal("{{ fork_ai_war_an_co2_dosierung_rdwc_port_5 }}", schritte[2]!["if"]![0]!["value_template"]!.ToString());
        Assert.Equal(Dosierung, schritte[2]!["then"]![0]!["target"]!["entity_id"]!.ToString());
        Assert.Equal("{{ fork_ai_war_an_co2_dosierung_zelt_2 }}", schritte[3]!["if"]![0]!["value_template"]!.ToString());
        Assert.Equal(zwei, schritte[3]!["then"]![0]!["target"]!["entity_id"]!.ToString());

        // Was keine Dosierung ist, wird weiter ohne Bedingung eingeschaltet.
        Assert.Equal("automation.etwas_anderes", schritte[4]!["target"]!["entity_id"]!.ToString());
        Assert.Equal(5, schritte.Count);
    }

    [Fact]
    public void NotAus_EinBlossesEinschaltenIstGewolltUndBleibt()
    {
        // Etwa eine Automation, die morgens die Dosierung scharf schaltet.
        var config = Echt("licht-aus-sicherung");
        ((JsonArray)config["actions"]!).RemoveAt(0);
        Assert.DoesNotContain(Art.NotAusWirdZurueckgenommen, Arten(config));
    }

    [Fact]
    public void NotAus_AndereAutomationenAlsDieDosierungZaehlenNicht()
    {
        var fremd = new Co2Absicherung.Rahmen(Fuehler, new HashSet<string> { "automation.etwas_anderes" }, Ports);
        Assert.DoesNotContain(Art.NotAusWirdZurueckgenommen, Arten(Echt("licht-aus-sicherung"), fremd));
    }

    // ------------------------------------------------------------ Vorlagen

    private static readonly IReadOnlyDictionary<string, string> Rollen = new Dictionary<string, string>
    {
        ["co2_sensor"] = Fuehler,
        ["licht"] = "binary_sensor.klein_abluft_zustand",
        ["port_zustand"] = "binary_sensor.big_port_5_zustand",
        ["port_schalter"] = "select.rdwc_venti_aktiver_modus_2",
        ["abluft_stufe"] = "number.rdwc_venti_einschaltleistung",
        ["canopy"] = "sensor.big_probe_sensor_sonden_temperatur",
        ["rh"] = "sensor.big_probe_sensor_sonden_luftfeuchtigkeit",
    };

    private static JsonObject Vorlage(string name)
    {
        var pfad = Path.Combine(AppContext.BaseDirectory, "Vorlagen", "co2", name + ".json");
        var vorlage = (JsonObject)JsonNode.Parse(File.ReadAllText(pfad))!;
        return SteuerungAutomationService.Fuellen(vorlage, Rollen)
            ?? throw new InvalidOperationException($"Vorlage {name} ließ sich nicht füllen.");
    }

    [Theory]
    [InlineData("dosierung")]
    [InlineData("waechter")]
    [InlineData("abluft")]
    public void DieForkVorlagen_SindSchonAbgesichert(string name)
    {
        // Sonst meldete die Seite eine frisch angelegte Automation als unsicher,
        // und „Automationen anlegen“ nähme die Absicherung beim nächsten Mal
        // wieder zurück.
        Assert.Empty(Arten(Vorlage(name)));
    }

    [Fact]
    public void Selbsttest_DieVorlageDerDosierungIstEineDosierung()
    {
        Assert.True(Co2Absicherung.IstDosierung(Vorlage("dosierung")));
        Assert.False(Co2Absicherung.IstDosierung(Vorlage("waechter")));
    }
}
