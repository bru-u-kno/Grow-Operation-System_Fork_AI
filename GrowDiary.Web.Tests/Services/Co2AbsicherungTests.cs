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

    private static readonly Co2Absicherung.Rahmen Rahmen =
        new(Fuehler, new HashSet<string>(StringComparer.Ordinal) { Dosierung });

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
            "{{ states('sensor.big_co2_light_sensor_co2') | float(-1) > 0 and states('sensor.co2_impuls_bedarf') | int(0) > 0 }}",
            vorschrift);
    }

    [Fact]
    public void Dosierung_OhneZugeordnetenFuehler_WirdDieSperreNichtErfunden()
    {
        var ohne = new Co2Absicherung.Rahmen(null, Rahmen.Dosierungen);

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
    public void Waechter_DieForkVorlageGiltSchonAlsAbgesichert()
    {
        var vorlage = Vorlage("waechter");
        Assert.DoesNotContain(Art.WaechterUebersiehtNeustart, Arten(vorlage));
    }

    // ------------------------------------------------------------ Not-Aus

    [Fact]
    public void NotAus_WiederEinschaltenNurWennSieVorherAnWar()
    {
        var neu = Co2Absicherung.Absichern(Echt("licht-aus-sicherung"), Rahmen);
        var schritte = (JsonArray)neu["actions"]!;

        Assert.Equal(
            "{{ is_state('automation.co2_dosierung_rdwc_port_5', 'on') }}",
            schritte[0]!["variables"]![Co2Absicherung.WarAnVariable]!.ToString());
        Assert.Equal("automation.turn_off", schritte[1]!["action"]!.ToString());
        Assert.Equal(Co2Absicherung.WiederEinAlias, schritte[2]!["alias"]!.ToString());
        Assert.Equal("automation.turn_on", schritte[2]!["then"]![0]!["action"]!.ToString());
        Assert.Equal("Off", schritte[3]!["data"]!["option"]!.ToString());
        Assert.Equal(4, schritte.Count);
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
        var fremd = new Co2Absicherung.Rahmen(Fuehler, new HashSet<string> { "automation.etwas_anderes" });
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
