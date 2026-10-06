using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Fork AI (A-009): Die Endpunkte des Zusatz-Entfeuchters, durch die ganze Kette (Routen, Model-Binding, Dienst).
/// </summary>
/// <remarks>
/// <para>Die App läuft hier ohne Home Assistant: nichts wird nach draußen geschrieben, und die Werte
/// stehen auf den Vorgaben. Geprüft wird, was die Seite sieht — dass ein Speichern <b>nur</b> das ändert,
/// was im Body steht, und dass die gemeinsame Höchsttemperatur die des Entfeuchters ist.</para>
/// <para>Die App wird von allen Fällen der Sammlung geteilt; jeder Fall stellt vorher her, was er braucht,
/// und räumt hinterher auf (Namen, Höchsttemperatur).</para>
/// </remarks>
[Collection(IntegrationsSammlung.Name)]
public sealed class EntfeuchterZusatzApiTests
{
    private readonly IntegrationsApp _app;

    public EntfeuchterZusatzApiTests(IntegrationsApp app) => _app = app;

    private const string Seite = "/api/steuerung/entfeuchter-zusatz";

    private static StringContent Roh(string json) => new(json, Encoding.UTF8, "application/json");

    private async Task<JsonObject> Lesen(string pfad)
    {
        var antwort = await _app.IngressClient().GetAsync(pfad);
        var text = await antwort.Content.ReadAsStringAsync();
        Assert.True(antwort.IsSuccessStatusCode, $"{pfad}: {(int)antwort.StatusCode} {text}");
        return (JsonObject)JsonNode.Parse(text)!;
    }

    private async Task<(HttpStatusCode Status, JsonObject Antwort)> Schreiben(string pfad, string json)
    {
        var antwort = await _app.IngressClient().PutAsync(pfad, Roh(json));
        return (antwort.StatusCode, (JsonObject)JsonNode.Parse(await antwort.Content.ReadAsStringAsync())!);
    }

    /// <summary>Alle Felder der Einstellungen als flache Liste Pfad → Wert.</summary>
    private static Dictionary<string, string> Flach(JsonNode? knoten, string pfad = "")
    {
        var ergebnis = new Dictionary<string, string>(StringComparer.Ordinal);
        if (knoten is JsonObject o)
        {
            foreach (var (k, v) in o) foreach (var (p, w) in Flach(v, pfad.Length == 0 ? k : pfad + "." + k)) ergebnis[p] = w;
        }
        else
        {
            ergebnis[pfad] = knoten?.ToJsonString() ?? "null";
        }
        return ergebnis;
    }

    private static List<string> Unterschiede(JsonNode? vorher, JsonNode? nachher)
    {
        var a = Flach(vorher);
        var b = Flach(nachher);
        return a.Keys.Union(b.Keys).Where(k => a.GetValueOrDefault(k) != b.GetValueOrDefault(k)).Order().ToList();
    }

    [Fact]
    public async Task Seite_LiefertEinstellungenUndLivebild_UndZaehltDieGeraete()
    {
        var seite = await Lesen(Seite);

        var e = seite["einstellungen"]!.AsObject();
        // Mengenwächter: die Seite trägt wirklich alle Felder des Vertrags.
        foreach (var feld in new[]
                 {
                     "hilfe", "automatikAktiv", "tagbetriebErlauben", "nachtDurchlaufen", "vpdHystereseKpa", "zuschaltVerzoegerungMin",
                     "folgeAbstandK", "wiederEinAbstandK", "mindestlaufzeitMin", "mindestpauseMin", "meldung", "ablauf",
                     "tempMaxTagModus", "tempMaxTagAbstandK", "tempMaxTagFestC", "tempMaxNachtModus", "tempMaxNachtAbstandK", "tempMaxNachtFestC",
                 })
        {
            Assert.True(e.ContainsKey(feld), $"einstellungen.{feld} fehlt");
        }
        foreach (var feld in new[] { "aktiv", "grenzeW", "dauerMin", "wiederholungH" })
        {
            Assert.True(e["meldung"]!.AsObject().ContainsKey(feld), $"einstellungen.meldung.{feld} fehlt");
        }

        var live = seite["live"]!.AsObject();
        foreach (var feld in new[]
                 {
                     "haErreichbar", "fuehrungName", "zusatzName", "planWoche", "planLuftTagC", "planLuftNachtC", "tempMaxTagC", "tempMaxNachtC",
                     "folgeAusTagC", "folgeAusNachtC", "wiederEinTagC", "wiederEinNachtC", "tempC", "feuchteProzent", "vpd", "tagPhase",
                     "schaltgroesse", "vpdZiel", "vpdEinSchwelle", "vpdAusSchwelle", "feuchteEinProzent", "feuchteAusProzent",
                     "zusatzAn", "zusatzOnline", "leistungW", "energieHeuteKwh", "fuehrungAn", "ziehtNichts", "planUnvollstaendig", "automatikAn",
                 })
        {
            Assert.True(live.ContainsKey(feld), $"live.{feld} fehlt");
        }
        Assert.Equal("normal", e["hilfe"]!.GetValue<string>());
        Assert.Equal("keine", live["schaltgroesse"]!.GetValue<string>());

        // Vier eigene Rollen plus die vier mitbenutzten aus dem Entfeuchter — der Bestand ordnet alle zu.
        Assert.Equal(8, seite["geraeteGesamt"]!.GetValue<int>());
        Assert.Equal(8, seite["geraeteZugeordnet"]!.GetValue<int>());
        Assert.Contains("ausHomeAssistantUebernommen", seite.Select(p => p.Key));
        // Unbekanntes steht als null da, nicht als fehlendes Feld.
        Assert.True(seite.ContainsKey("haAngenommen") && seite["haAngenommen"] is null);
    }

    [Fact]
    public async Task Speichern_NurDasGenannteAendertSich_AuchBeimZweitenMal()
    {
        var vorher = await Lesen(Seite);
        var neuerWert = vorher["einstellungen"]!["mindestlaufzeitMin"]!.GetValue<int>() == 33 ? 34 : 33;

        var (status, antwort) = await Schreiben(Seite, $$"""{"mindestlaufzeitMin":{{neuerWert}}}""");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(neuerWert, antwort["einstellungen"]!["mindestlaufzeitMin"]!.GetValue<int>());
        Assert.False(antwort["ausHomeAssistantUebernommen"]!.GetValue<bool>());
        // Die App läuft hier ohne Home Assistant: der Helfer ließ sich nicht schreiben — das sagt die Antwort
        // aus dem echten Schreibergebnis, nicht „true" aus Gewohnheit.
        Assert.False(antwort["haAngenommen"]!.GetValue<bool>());
        Assert.NotNull(antwort["hinweise"]);

        var nachher = await Lesen(Seite);
        Assert.Equal(["mindestlaufzeitMin"], Unterschiede(vorher["einstellungen"], nachher["einstellungen"]));

        // Zweites Mal derselbe Body: nichts ändert sich.
        var (status2, _) = await Schreiben(Seite, $$"""{"mindestlaufzeitMin":{{neuerWert}}}""");
        Assert.Equal(HttpStatusCode.OK, status2);
        var danach = await Lesen(Seite);
        Assert.Empty(Unterschiede(nachher["einstellungen"], danach["einstellungen"]));
    }

    [Fact]
    public async Task Speichern_EinReinesForkFeld_SchreibtNichtsNachHomeAssistant_HaAngenommenIstNull()
    {
        var (status, antwort) = await Schreiben(Seite, """{"ablauf":"schlauch"}""");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("schlauch", antwort["einstellungen"]!["ablauf"]!.GetValue<string>());
        Assert.True(antwort.ContainsKey("haAngenommen") && antwort["haAngenommen"] is null);
    }

    [Fact]
    public async Task Speichern_MeldungTeilfeld_LaesstDieAnderenMeldungsfelderStehen()
    {
        var vorher = (await Lesen(Seite))["einstellungen"]!["meldung"]!.AsObject();
        var grenze = vorher["grenzeW"]!.GetValue<int>() == 90 ? 91 : 90;

        var (status, antwort) = await Schreiben(Seite, $$$"""{"meldung":{"grenzeW":{{{grenze}}}}}""");

        Assert.Equal(HttpStatusCode.OK, status);
        var nachher = antwort["einstellungen"]!["meldung"]!.AsObject();
        Assert.Equal(["grenzeW"], Unterschiede(vorher, nachher));
    }

    [Fact]
    public async Task Speichern_Hilfsstaerke_SetztDieVoreinstellung_UndEinzelwertMachtEigene()
    {
        var (_, kraeftig) = await Schreiben(Seite, """{"hilfe":"kraeftig"}""");
        var e = kraeftig["einstellungen"]!;
        Assert.Equal("kraeftig", e["hilfe"]!.GetValue<string>());
        Assert.Equal(0.5, e["folgeAbstandK"]!.GetValue<double>());
        Assert.Equal(0.5, e["wiederEinAbstandK"]!.GetValue<double>());
        Assert.Equal(0.1, e["vpdHystereseKpa"]!.GetValue<double>());
        Assert.Equal(5, e["zuschaltVerzoegerungMin"]!.GetValue<int>());
        Assert.Equal(10, e["mindestpauseMin"]!.GetValue<int>());

        var (_, eigene) = await Schreiben(Seite, """{"mindestpauseMin":11}""");
        Assert.Equal("eigene", eigene["einstellungen"]!["hilfe"]!.GetValue<string>());

        // Aufräumen: zurück auf „normal".
        var (_, normal) = await Schreiben(Seite, """{"hilfe":"normal"}""");
        Assert.Equal("normal", normal["einstellungen"]!["hilfe"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("""{"folgeAbstandK":9}""", "folgeAbstandK")]
    [InlineData("""{"mindestpauseMin":0}""", "mindestpauseMin")]
    [InlineData("""{"mindestlaufzeitMin":0}""", "mindestlaufzeitMin")]
    [InlineData("""{"mindestlaufzeitMin":121}""", "mindestlaufzeitMin")]
    [InlineData("""{"vpdHystereseKpa":0.01}""", "vpdHystereseKpa")]
    [InlineData("""{"meldung":{"grenzeW":500}}""", "grenzeW")]
    [InlineData("""{"hilfe":"turbo"}""", "hilfe")]
    [InlineData("""{"ablauf":"eimer"}""", "ablauf")]
    [InlineData("""{"tempMaxTagFestC":50}""", "tempMaxTagFestC")]
    [InlineData("""{"tempMaxNachtModus":"irgendwie"}""", "tempMaxNachtModus")]
    [InlineData("""{"tempMaxTagAbstandK":20}""", "tempMaxTagAbstandK")]
    public async Task UngueltigeEingabe_Gibt400MitFeldfehler_UndAendertNichts(string body, string feld)
    {
        var vorher = await Lesen(Seite);
        var entfeuchterVorher = await Lesen("/api/steuerung/entfeuchter");

        var (status, antwort) = await Schreiben(Seite, body);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        var fehler = antwort["fieldErrors"]!.AsObject().Select(p => p.Key).ToList();
        Assert.Contains(fehler, k => k.EndsWith(feld, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(Unterschiede(vorher["einstellungen"], (await Lesen(Seite))["einstellungen"]));
        Assert.Empty(Unterschiede(entfeuchterVorher["einstellungen"], (await Lesen("/api/steuerung/entfeuchter"))["einstellungen"]));
    }

    [Fact]
    public async Task Hoechsttemperatur_IstDieDesEntfeuchters_UndAndereEntfeuchterFelderBleiben()
    {
        var entfeuchterAlt = (JsonObject)(await Lesen("/api/steuerung/entfeuchter"))["einstellungen"]!;
        var originalJson = entfeuchterAlt.ToJsonString();
        try
        {
            // Der Entfeuchter bekommt Werte, die vom Werkswert abweichen — sonst fiele ein Zurücksetzen
            // des Rests auf den Werkswert nicht auf.
            var eingestellt = (JsonObject)entfeuchterAlt.DeepClone();
            eingestellt["hystereseProzent"] = 7.5;
            eingestellt["mindestlaufzeitMin"] = 33;
            eingestellt["einschaltverzoegerungMin"] = 9;
            eingestellt["wartezeitAussenluftMin"] = 44;
            eingestellt["feuchteEinTag"] = 66.0;
            eingestellt["feuchteAusNacht"] = 51.0;
            eingestellt["tagbetriebErlauben"] = false;
            eingestellt["tempMaxNachtFestC"] = 23.5;
            var (vorbereitung, _) = await Schreiben("/api/steuerung/entfeuchter", eingestellt.ToJsonString());
            Assert.Equal(HttpStatusCode.OK, vorbereitung);

            var entfeuchterVorher = await Lesen("/api/steuerung/entfeuchter");
            var zusatzVorher = await Lesen(Seite);
            var alt = entfeuchterVorher["einstellungen"]!["tempMaxTagFestC"]!.GetValue<double>();
            // Beide Seiten zeigen dieselbe Grenze, bevor jemand etwas tut.
            Assert.Equal(alt, zusatzVorher["einstellungen"]!["tempMaxTagFestC"]!.GetValue<double>());
            Assert.Equal(23.5, zusatzVorher["einstellungen"]!["tempMaxNachtFestC"]!.GetValue<double>());
            var neu = alt == 27.5 ? 28.0 : 27.5;

            var (status, antwort) = await Schreiben(Seite, $$"""{"tempMaxTagFestC":{{neu.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}""");

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal(neu, antwort["einstellungen"]!["tempMaxTagFestC"]!.GetValue<double>());
            var entfeuchterNachher = await Lesen("/api/steuerung/entfeuchter");
            // Beim Entfeuchter ist GENAU diese Grenze anders — nichts sonst.
            Assert.Equal(["tempMaxTagFestC"], Unterschiede(entfeuchterVorher["einstellungen"], entfeuchterNachher["einstellungen"]));
            // Und beim Zusatz nur die Grenze.
            Assert.Equal(["tempMaxTagFestC"], Unterschiede(zusatzVorher["einstellungen"], antwort["einstellungen"]));
        }
        finally
        {
            await Schreiben("/api/steuerung/entfeuchter", originalJson);
        }
    }

    [Fact]
    public async Task Namen_FehlendLaesstStehen_NullOderLeerSetztAufDieVorgabe()
    {
        var pfad = "/api/steuerung/entfeuchter-namen";
        try
        {
            var vorgabe = await Lesen(pfad);
            Assert.False(string.IsNullOrWhiteSpace(vorgabe["fuehrung"]!["anzeigename"]!.GetValue<string>()));
            Assert.False(string.IsNullOrWhiteSpace(vorgabe["zusatz"]!["vorgabe"]!.GetValue<string>()));

            var (status, nach1) = await Schreiben(pfad, """{"fuehrung":"Hauptgerät","zusatz":"Zeltgerät"}""");
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal("Hauptgerät", nach1["fuehrung"]!["anzeigename"]!.GetValue<string>());
            Assert.Equal("Zeltgerät", nach1["zusatz"]!["anzeigename"]!.GetValue<string>());
            Assert.Equal(vorgabe["zusatz"]!["vorgabe"]!.GetValue<string>(), nach1["zusatz"]!["vorgabe"]!.GetValue<string>());

            // Der Name steht in der Seite — nicht „Port 7", nicht der Vorgabetext.
            Assert.Equal("Zeltgerät", (await Lesen(Seite))["live"]!["zusatzName"]!.GetValue<string>());

            // Nur einen nennen: der andere bleibt.
            var (_, nach2) = await Schreiben(pfad, """{"fuehrung":"Haupt 2"}""");
            Assert.Equal("Haupt 2", nach2["fuehrung"]!["anzeigename"]!.GetValue<string>());
            Assert.Equal("Zeltgerät", nach2["zusatz"]!["anzeigename"]!.GetValue<string>());

            // Leerer Body ändert nichts.
            var (_, nach3) = await Schreiben(pfad, "{}");
            Assert.Equal("Haupt 2", nach3["fuehrung"]!["anzeigename"]!.GetValue<string>());
            Assert.Equal("Zeltgerät", nach3["zusatz"]!["anzeigename"]!.GetValue<string>());

            // null und leer: Vorgabe — und nur der genannte.
            var (_, nach4) = await Schreiben(pfad, """{"fuehrung":null}""");
            Assert.Equal(vorgabe["fuehrung"]!["vorgabe"]!.GetValue<string>(), nach4["fuehrung"]!["anzeigename"]!.GetValue<string>());
            Assert.Equal("Zeltgerät", nach4["zusatz"]!["anzeigename"]!.GetValue<string>());
            var (_, nach5) = await Schreiben(pfad, """{"zusatz":""}""");
            Assert.Equal(vorgabe["zusatz"]!["vorgabe"]!.GetValue<string>(), nach5["zusatz"]!["anzeigename"]!.GetValue<string>());
        }
        finally
        {
            await Schreiben(pfad, """{"fuehrung":null,"zusatz":null}""");
        }
    }

    [Fact]
    public async Task Namen_ZuLangOderMitSteuerzeichen_Gibt400()
    {
        var pfad = "/api/steuerung/entfeuchter-namen";
        var (status, antwort) = await Schreiben(pfad, $$"""{"zusatz":"{{new string('x', 61)}}"}""");
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains(antwort["fieldErrors"]!.AsObject().Select(p => p.Key), k => k.Equals("zusatz", StringComparison.OrdinalIgnoreCase));

        var (status2, _) = await Schreiben(pfad, """{"fuehrung":"a\nb"}""");
        Assert.Equal(HttpStatusCode.BadRequest, status2);
    }

    [Fact]
    public async Task Uebersicht_FuehrtDenZusatzEntfeuchterMitDetailseite()
    {
        var uebersicht = await Lesen("/api/steuerung");
        var module = uebersicht["module"]!.AsArray();
        Assert.True(module.Count >= 6, "Die Übersicht ist leer — der Test sieht die Module nicht.");

        var zusatz = Assert.Single(module, m => m!["kennung"]!.GetValue<string>() == "entfeuchter-zusatz")!;
        Assert.True(zusatz["hatDetail"]!.GetValue<bool>());
        Assert.False(string.IsNullOrWhiteSpace(zusatz["titel"]!.GetValue<string>()));
        Assert.Contains(zusatz["status"]!.GetValue<string>(), new[] { "an", "aus", "warn" });
        // Kein „Port 7" fest im Text.
        Assert.DoesNotContain("Port 7", zusatz.ToJsonString());
    }

    [Fact]
    public async Task GeraeteSeite_ZeigtDieVierEigenenRollenDesZusatzes()
    {
        var geraete = await Lesen("/api/steuerung/geraete");
        var modul = Assert.Single(geraete["module"]!.AsArray(), m => m!["modul"]!.GetValue<string>() == "entfeuchter-zusatz")!;
        var rollen = modul["zeilen"]!.AsArray().Select(z => z!["rolle"]!.GetValue<string>()).Order().ToList();

        Assert.Equal(["fuehrung_zustand", "zusatz_energie", "zusatz_leistung", "zusatz_schalter"], rollen);
        // Zelt-Fühler und Licht stehen dort NICHT noch einmal.
        Assert.DoesNotContain("zelt_temp", rollen);
        Assert.DoesNotContain("licht_zustand", rollen);
    }
}
