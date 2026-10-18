using System.Text.Json.Nodes;
using GrowMcp.Tools;
using GrowOsAccess;

namespace GrowMcp.Tests;

/// <summary>
/// Fork AI (Paket D, 18.10.2026): <c>fork_funktionen</c> und <c>fork_ausfuehren</c>.
/// </summary>
/// <remarks>
/// Das Verzeichnis der Attrappe ist ein Ausschnitt in der Form, die Grow OS liefert (<c>GET /api/ki-zugriff/funktionen</c>);
/// die Einträge stammen aus dem Verzeichnis der Demo-App vom 18.10.2026.
/// </remarks>
public sealed class ForkAusfuehrenTests
{
    private static JsonObject Eintrag(string methode, string pfad, string zugang, string[]? stufen = null, string? nie = null,
        bool sicherung = false, bool schalt = false, bool datei = false, bool hochladen = false, string beschreibung = "Beschreibung.",
        (string Name, string Typ)[]? routen = null, string? rumpfTyp = null, (string Name, string Typ)[]? felder = null)
    {
        var bereich = pfad.Split('/', StringSplitOptions.RemoveEmptyEntries)[1];
        var e = new JsonObject
        {
            ["methode"] = methode, ["pfad"] = pfad, ["bereich"] = bereich,
            ["art"] = methode switch { "GET" => "lesen", "POST" => "anlegen", "DELETE" => "löschen", _ => "ändern" },
            ["zugang"] = zugang, ["stufen"] = new JsonArray((stufen ?? []).Select(s => (JsonNode)JsonValue.Create(s)!).ToArray()),
            ["verwaltungsweg"] = pfad.StartsWith("/api/settings", StringComparison.Ordinal), ["schaltbefehl"] = schalt,
            ["sicherungVorher"] = sicherung, ["beschreibung"] = beschreibung,
            ["routenparameter"] = new JsonArray((routen ?? []).Select(r => (JsonNode)new JsonObject { ["name"] = r.Name, ["typ"] = r.Typ, ["pflicht"] = true }).ToArray()),
            ["abfrage"] = new JsonArray(), ["dateiHochladen"] = hochladen, ["liefertDatei"] = datei,
        };
        if (nie is not null) e["nieGrund"] = nie;
        if (rumpfTyp is not null)
        {
            e["rumpf"] = new JsonObject
            {
                ["typ"] = rumpfTyp,
                ["felder"] = new JsonArray((felder ?? []).Select(f => (JsonNode)new JsonObject { ["name"] = f.Name, ["typ"] = f.Typ }).ToArray()),
            };
        }
        return e;
    }

    private static readonly (string, string)[] Id = [("id", "Ganzzahl")];

    private static string Verzeichnis()
    {
        var liste = new JsonArray(
            Eintrag("GET", "/api/grows", "frei", beschreibung: "Die Grows."),
            Eintrag("GET", "/api/grows/archive", "frei", beschreibung: "Abgeschlossene Grows."),
            Eintrag("GET", "/api/grows/{id}", "frei", routen: Id),
            Eintrag("GET", "/api/steuerung/entfeuchter", "frei", beschreibung: "Die Entfeuchter-Seite."),
            Eintrag("PUT", "/api/steuerung/entfeuchter", "stufe", ["GeraeteSchalten", "Verwaltung"], schalt: true,
                beschreibung: "Entfeuchter speichern.", rumpfTyp: "EntfeuchterEinstellungen",
                felder: [("hystereseProzent", "Zahl"), ("mindestlaufzeitMin", "Ganzzahl"), ("automatikAktiv", "ja/nein")]),
            Eintrag("GET", "/api/kosten/nachfuellungen/{id}", "frei", routen: Id),
            Eintrag("DELETE", "/api/kosten/nachfuellungen/{id}", "stufe", ["Verwaltung"], sicherung: true, routen: Id),
            Eintrag("GET", "/api/kosten", "frei", beschreibung: "Die Kostenseite."),
            Eintrag("GET", "/api/settings/tents/{id}", "stufe", ["Verwaltung"], routen: Id),
            Eintrag("PUT", "/api/settings/tents/{id}", "stufe", ["Verwaltung"], routen: Id, rumpfTyp: "UpsertTentRequest",
                felder: [("name", "Text"), ("notes", "Text"), ("raumId", "Text"), ("sensors", "Liste von Objekt {metricType: Text}")]),
            Eintrag("GET", "/api/steuerung/co2", "frei"),
            Eintrag("PUT", "/api/steuerung/co2", "stufe", ["GeraeteSchalten", "Verwaltung"], schalt: true, rumpfTyp: "Co2Einstellungen",
                felder: [("zielWarmPpm", "Ganzzahl"), ("mindestlaufzeitMin", "Ganzzahl"), ("kostenArtikelId", "Ganzzahl?")]),
            Eintrag("PUT", "/api/steuerung/ohne-lesen", "stufe", ["Verwaltung"], rumpfTyp: "X", felder: [("a", "Zahl"), ("b", "Zahl")]),
            Eintrag("GET", "/api/tents/{tentId}/dashboard", "frei", routen: [("tentId", "Ganzzahl")]),
            Eintrag("PUT", "/api/tents/{tentId}/dashboard", "stufe", ["GrowPlanen"], routen: [("tentId", "Ganzzahl")], rumpfTyp: "DashboardRequest",
                felder: [("isCustom", "ja/nein"), ("sections", "Liste von Objekt {id: Text}")]),
            Eintrag("GET", "/api/grows/{growId}/journal", "frei", routen: [("growId", "Ganzzahl")]),
            Eintrag("POST", "/api/grows/{growId}/journal", "stufe", ["Dokumentieren"], routen: [("growId", "Ganzzahl")], rumpfTyp: "JournalRequest", felder: [("title", "Text")]),
            Eintrag("DELETE", "/api/kosten/anschaffungen/{id}", "stufe", ["Verwaltung"], sicherung: true, routen: Id),
            Eintrag("POST", "/api/kosten/anschaffungen", "stufe", ["Dokumentieren"], rumpfTyp: "AnschaffungRequest",
                felder: [("name", "Text"), ("stueck", "Ganzzahl"), ("einzelpreisEur", "Zahl")]),
            Eintrag("POST", "/api/settings/ki-zugriff/schluessel", "nie", nie: "Die Schlüsselverwaltung — sonst könnte sich ein Schlüssel selbst Stufen geben."),
            Eintrag("GET", "/api/system/backup/{fileName}", "nie", nie: "In der Sicherungsdatei kann das Home-Assistant-Token im Klartext stehen.", datei: true, routen: [("fileName", "Text")]),
            Eintrag("GET", "/api/live/tents/{tentId}/camera", "frei", datei: true, routen: [("tentId", "Ganzzahl")]),
            Eintrag("POST", "/api/measurements/{measurementId}/photos", "stufe", ["Dokumentieren"], hochladen: true, routen: [("measurementId", "Ganzzahl")]));
        return new JsonObject { ["anzahl"] = liste.Count, ["hinweis"] = "…", ["funktionen"] = liste }.ToJsonString();
    }

    private static ForkAttrappe Fork()
        => new ForkAttrappe().Antwort("GET", "api/ki-zugriff/funktionen", 200, Verzeichnis());

    private static Task<string> Ausfuehren(ForkAttrappe fork, string methode, string pfad, string? query = null, string? body = null, bool ergaenzen = true)
        => Werkzeugkasten.TextAsync("fork_ausfuehren", Werkzeugkasten.Leser(fork, Werkzeugkasten.ForkSchluessel),
            new Dictionary<string, object?> { ["methode"] = methode, ["pfad"] = pfad, ["query"] = query, ["body"] = body, ["ergaenzen"] = ergaenzen });

    /// <summary>Was die Werkzeuge ohne das Verzeichnis gefragt haben.</summary>
    private static List<Angekommen> Aufrufe(ForkAttrappe fork) => fork.VonWerkzeugen.Where(a => a.Weg != "api/ki-zugriff/funktionen").ToList();

    // ------------------------------------------------------------------ Pfad

    /// <summary>Jeder Ausbruchsversuch scheitert, bevor Grow OS gefragt wird — auch das Verzeichnis nicht.</summary>
    [Theory]
    [InlineData("api/../system/backup/x")]
    [InlineData("api/grows/../../etc/passwd")]
    [InlineData("http://anderer-host/api/grows")]
    [InlineData("//anderer-host/api/grows")]
    [InlineData("https:/anderer-host/api/grows")]
    [InlineData("api/%2e%2e/settings")]
    [InlineData("api\\..\\settings")]
    [InlineData("api/grows?archived=true")]
    [InlineData("api/grows#x")]
    [InlineData("api//grows")]
    [InlineData("api/grows/@anderer-host")]
    [InlineData("system/backup")]
    [InlineData("uploads/1/bild.jpg")]
    [InlineData("api")]
    [InlineData("api/./grows")]
    [InlineData(" ")]
    public async Task Ein_Pfad_ausserhalb_von_api_wird_abgelehnt_ohne_Anfrage(string pfad)
    {
        var fork = Fork();
        var text = await Ausfuehren(fork, "GET", pfad);

        Assert.StartsWith("Nicht ausgeführt", text);
        Assert.Empty(fork.VonWerkzeugen);
    }

    [Theory]
    [InlineData("api/grows", "api/grows")]
    [InlineData("/api/grows/7", "api/grows/7")]
    [InlineData("API/Grows", "API/Grows")]
    public void Ein_erlaubter_Pfad_bleibt_wie_er_ist(string pfad, string erwartet)
        => Assert.Equal(erwartet, Funktionsverzeichnis.PfadPruefen(pfad, out _));

    [Fact]
    public async Task Ein_Weg_der_nicht_im_Verzeichnis_steht_wird_nicht_gerufen()
    {
        var fork = Fork();
        var text = await Ausfuehren(fork, "DELETE", "api/grows/7");

        Assert.Contains("steht nicht im Funktionsverzeichnis", text);
        Assert.Contains("Auf diesem Weg gibt es: GET", text);
        Assert.Empty(Aufrufe(fork));
    }

    // ------------------------------------------------------------------ Gesperrt

    [Fact]
    public async Task Ein_nie_Weg_wird_abgelehnt_bevor_er_gerufen_wird()
    {
        var fork = Fork();
        var text = await Ausfuehren(fork, "POST", "api/settings/ki-zugriff/schluessel", body: """{"name":"zweiter"}""");

        Assert.Contains("nie erreichbar", text);
        Assert.Contains("Schlüsselverwaltung", text);
        Assert.Empty(Aufrufe(fork));
    }

    [Fact]
    public async Task Sicherung_herunterladen_und_Upload_und_Bilder_gehen_nicht()
    {
        var fork = Fork();
        Assert.Contains("nie erreichbar", await Ausfuehren(fork, "GET", "api/system/backup/sicherung.zip"));
        Assert.Contains("hochgeladene Datei", await Ausfuehren(fork, "POST", "api/measurements/3/photos", body: "{}"));
        Assert.Contains("kamera_bild", await Ausfuehren(fork, "GET", "api/live/tents/1/camera"));
        Assert.Empty(Aufrufe(fork));
    }

    [Fact]
    public async Task Eine_Datei_in_der_Antwort_wird_nicht_weitergegeben()
    {
        // Steht nicht als Datei im Verzeichnis, liefert aber eine: der Inhaltstyp entscheidet, der Rumpf wird nie gelesen.
        var fork = Fork().Datei("api/grows/archive", "application/zip", [0x50, 0x4b, 3, 4]);
        var text = await Ausfuehren(fork, "GET", "api/grows/archive");

        Assert.Contains("mit einer Datei (application/zip)", text);
        Assert.DoesNotContain("PK", text);
    }

    // ------------------------------------------------------------------ Lesen

    [Fact]
    public async Task Lesen_reicht_die_Antwort_durch_und_kodiert_die_Abfrage()
    {
        var fork = Fork().Antwort("GET", "api/grows", 200, """[{"id":1,"name":"Grow 1"}]""");
        var text = await Ausfuehren(fork, "get", "api/grows", query: "archived=false&suche=weiße widow");

        Assert.Contains("\"name\":\"Grow 1\"", text);
        var aufruf = Assert.Single(Aufrufe(fork));
        Assert.Equal("/api/grows?archived=false&suche=wei%C3%9Fe%20widow", aufruf.Pfad);
        Assert.Equal($"Bearer {Werkzeugkasten.ForkSchluessel}", aufruf.Authorization);
    }

    [Fact]
    public async Task Ein_fester_Abschnitt_gewinnt_vor_dem_Platzhalter_und_Ganzzahl_nimmt_nur_Ziffern()
    {
        var fork = Fork().Antwort("GET", "api/grows/archive", 200, "[]");
        await Ausfuehren(fork, "GET", "api/grows/archive");
        Assert.Equal("api/grows/archive", Assert.Single(Aufrufe(fork)).Weg);

        var verzeichnis = Funktionsverzeichnis.Lesen(Verzeichnis())!;
        Assert.Equal("/api/grows/archive", Funktionsverzeichnis.Zuordnen(verzeichnis, "GET", "api/grows/archive")!.Pfad);
        Assert.Equal("/api/grows/{id}", Funktionsverzeichnis.Zuordnen(verzeichnis, "GET", "api/grows/12")!.Pfad);
        Assert.Null(Funktionsverzeichnis.Zuordnen(verzeichnis, "GET", "api/grows/zwölf/x"));
    }

    [Theory]
    [InlineData("a b=1")]
    [InlineData("x&=3")]
    public async Task Eine_kaputte_Abfrage_wird_abgelehnt(string query)
    {
        var fork = Fork();
        Assert.StartsWith("Nicht ausgeführt", await Ausfuehren(fork, "GET", "api/grows", query: query));
        Assert.Empty(fork.VonWerkzeugen);
    }

    // ------------------------------------------------------------------ Ändern mit Vorher/Nachher

    /// <summary>Ein Entfeuchter, dessen PUT wie in Grow OS das Ganze ersetzt; die Live-Werte bewegen sich bei jedem Lesen.</summary>
    private static ForkAttrappe EntfeuchterFork(bool vergisstMindestlaufzeit = false)
    {
        var stand = new JsonObject { ["hystereseProzent"] = 2, ["mindestlaufzeitMin"] = 10, ["automatikAktiv"] = true };
        var lesungen = 0;
        return Fork()
            .Antwort("GET", "api/steuerung/entfeuchter", _ =>
            {
                lesungen++;
                return (200, new JsonObject
                {
                    ["einstellungen"] = stand.DeepClone(),
                    ["live"] = new JsonObject { ["rlf"] = 50 + lesungen, ["zeit"] = $"12:0{lesungen}" },
                }.ToJsonString());
            })
            .Antwort("PUT", "api/steuerung/entfeuchter", a =>
            {
                var neu = JsonNode.Parse(a.Rumpf!)!.AsObject();
                stand = new JsonObject
                {
                    ["hystereseProzent"] = neu["hystereseProzent"]?.DeepClone(),
                    ["mindestlaufzeitMin"] = vergisstMindestlaufzeit ? 0 : neu["mindestlaufzeitMin"]?.DeepClone(),
                    ["automatikAktiv"] = neu["automatikAktiv"]?.DeepClone(),
                };
                return (200, stand.ToJsonString());
            });
    }

    [Fact]
    public async Task Eine_Aenderung_meldet_genau_was_sich_geaendert_hat()
    {
        var fork = EntfeuchterFork();
        var text = await Ausfuehren(fork, "PUT", "api/steuerung/entfeuchter", body: """{"hystereseProzent":3}""");

        Assert.Contains("Ausgeführt: PUT /api/steuerung/entfeuchter → 200.", text);
        Assert.Contains("Zählt als Schaltbefehl.", text);
        Assert.Contains("Geändert (1): einstellungen.hystereseProzent: 2 → 3.", text);
        Assert.Contains("2 Werte unverändert.", text);
        // Live-Werte bewegen sich bei jedem Lesen — sie sind kein Ergebnis der Änderung.
        Assert.Contains("2 Werte bewegten sich schon vorher", text);
        Assert.DoesNotContain("live.rlf", text);

        // Gelesen, gelesen, geschrieben, gelesen — und der Rumpf trägt die ergänzten Felder.
        Assert.Equal(["GET", "GET", "PUT", "GET"], Aufrufe(fork).Select(a => a.Methode).ToArray());
        var put = JsonNode.Parse(Aufrufe(fork)[2].Rumpf!)!.AsObject();
        Assert.Equal(3, put["hystereseProzent"]!.GetValue<int>());
        Assert.Equal(10, put["mindestlaufzeitMin"]!.GetValue<int>());
        Assert.True(put["automatikAktiv"]!.GetValue<bool>());
        Assert.Contains("Aus dem gelesenen Stand ergänzt (2): mindestlaufzeitMin, automatikAktiv.", text);
    }

    [Fact]
    public async Task Eine_ungewollte_Nebenwirkung_wird_ausdruecklich_genannt()
    {
        var fork = EntfeuchterFork(vergisstMindestlaufzeit: true);
        var text = await Ausfuehren(fork, "PUT", "api/steuerung/entfeuchter", body: """{"hystereseProzent":3}""");

        Assert.Contains("Außerdem verändert, ohne dass es im Rumpf stand (1) — bitte prüfen: einstellungen.mindestlaufzeitMin: 10 → 0.", text);
    }

    [Fact]
    public async Task Ohne_ergaenzen_geht_nur_der_Rumpf_hinaus()
    {
        var fork = EntfeuchterFork();
        await Ausfuehren(fork, "PUT", "api/steuerung/entfeuchter", body: """{"hystereseProzent":3}""", ergaenzen: false);

        var put = JsonNode.Parse(Aufrufe(fork).Single(a => a.Methode == "PUT").Rumpf!)!.AsObject();
        Assert.Equal(["hystereseProzent"], put.Select(p => p.Key).ToArray());
    }

    [Fact]
    public async Task Kaputtes_JSON_und_ein_Rumpf_bei_GET_werden_abgelehnt()
    {
        var fork = Fork();
        Assert.Contains("kein gültiges JSON", await Ausfuehren(fork, "PUT", "api/steuerung/entfeuchter", body: "{hysterese: 3"));
        Assert.Contains("nimmt keinen Rumpf", await Ausfuehren(fork, "GET", "api/grows", body: "{}"));
        Assert.Contains("keine Methode", await Ausfuehren(fork, "TRACE", "api/grows"));
        Assert.Empty(fork.VonWerkzeugen);
    }

    // ------------------------------------------------------------------ Prüferbefunde vom 18.10.2026 (B1–B3, S1)

    [Fact]
    public async Task B1_Eine_fehlende_Liste_bricht_ab_statt_zu_raten()
    {
        // Prüfer 18.10.2026: Grow OS behandelt eine fehlende Liste je Weg verschieden — die Sensoren eines Zelts bleiben,
        // die Kacheln des Dashboards werden leer. Also weder mitschicken (neue Sensor-Ids) noch still weglassen.
        var fork = Fork()
            .Antwort("GET", "api/settings/tents/1", 200, """{"id":1,"name":"Zelt","notes":"n","sensors":[{"id":31,"metricType":"Temperature"}]}""")
            .Antwort("PUT", "api/settings/tents/1", 200, """{"id":1,"name":"Neu","notes":"n"}""");
        var text = await Ausfuehren(fork, "PUT", "api/settings/tents/1", body: """{"name":"Neu"}""");

        Assert.StartsWith("Nicht ausgeführt: Im Rumpf fehlt die Liste sensors.", text);
        Assert.DoesNotContain(Aufrufe(fork), a => a.Methode == "PUT");

        // Wer weiß, dass Grow OS die Liste ohne Angabe stehen lässt, schickt ohne Ergänzen.
        await Ausfuehren(fork, "PUT", "api/settings/tents/1", body: """{"name":"Neu","notes":"n","raumId":null}""", ergaenzen: false);
        var put = JsonNode.Parse(Assert.Single(Aufrufe(fork), a => a.Methode == "PUT").Rumpf!)!.AsObject();
        Assert.False(put.ContainsKey("sensors"));
    }

    [Fact]
    public async Task B2_Unter_der_Haelfte_passender_Felder_wird_nicht_ergaenzt()
    {
        var fork = Fork()
            .Antwort("GET", "api/steuerung/co2", 200, """{"ganzAnders":{"x":1},"live":{"ppm":700}}""")
            .Antwort("PUT", "api/steuerung/co2", 200, "{}");
        var text = await Ausfuehren(fork, "PUT", "api/steuerung/co2", body: """{"zielWarmPpm":1000}""");

        Assert.Contains("nicht sicher zuordnen (nur 0 von 3 Feldern gefunden)", text);
        Assert.DoesNotContain(Aufrufe(fork), a => a.Methode == "PUT");
    }

    [Fact]
    public async Task Ein_Pflichtfeld_das_im_Stand_fehlt_wird_weggelassen_nicht_leer_geschickt()
    {
        var fork = Fork()
            .Antwort("GET", "api/steuerung/co2", 200, """{"einstellungen":{"zielWarmPpm":900,"kostenArtikelId":2}}""")
            .Antwort("PUT", "api/steuerung/co2", 200, "{}");
        var text = await Ausfuehren(fork, "PUT", "api/steuerung/co2", body: """{"zielWarmPpm":1000}""");

        var put = JsonNode.Parse(Aufrufe(fork).Single(a => a.Methode == "PUT").Rumpf!)!.AsObject();
        Assert.False(put.ContainsKey("mindestlaufzeitMin"));
        Assert.Contains("nicht leer erlaubt, deshalb weggelassen (Grow OS nimmt seine Vorgabe): mindestlaufzeitMin.", text);
    }

    [Fact]
    public async Task Ein_bewusst_geleertes_Feld_ist_gewollt_auch_wenn_Grow_OS_es_weglaesst()
    {
        var stand = JsonNode.Parse("""{"einstellungen":{"zielWarmPpm":900,"mindestlaufzeitMin":5,"kostenArtikelId":2}}""")!.AsObject();
        var fork = Fork()
            .Antwort("GET", "api/steuerung/co2", _ => (200, stand.ToJsonString()))
            .Antwort("PUT", "api/steuerung/co2", _ =>
            {
                stand["einstellungen"]!.AsObject().Remove("kostenArtikelId");
                return (200, "{}");
            });
        var text = await Ausfuehren(fork, "PUT", "api/steuerung/co2", body: """{"kostenArtikelId":null}""");

        Assert.Contains("Geändert (1): einstellungen.kostenArtikelId: 2 → (fehlte).", text);
        Assert.DoesNotContain("Außerdem verändert", text);
    }

    [Fact]
    public async Task Ein_Stand_der_eine_Liste_ist_wird_als_solcher_benannt()
    {
        var fork = Fork().Antwort("GET", "api/steuerung/co2", 200, "[1,2]");
        Assert.Contains("ist eine Liste, kein Objekt", await Ausfuehren(fork, "PUT", "api/steuerung/co2", body: """{"zielWarmPpm":1000}"""));
    }

    [Fact]
    public async Task S1_In_verschachtelten_Listen_ist_nur_neu_oder_weg_was_sein_id_Blatt_verliert()
    {
        // Wie das Dashboard: Abschnitte mit Kacheln. Zwei Kacheln fallen weg, der Abschnitt bleibt.
        var stand = JsonNode.Parse("""{"isCustom":true,"sections":[{"id":"klima","tiles":[{"id":"a","w":1},{"id":"b","w":2},{"id":"c","w":3}]}]}""")!.AsObject();
        var fork = Fork()
            .Antwort("GET", "api/tents/1/dashboard", _ => (200, stand.ToJsonString()))
            .Antwort("PUT", "api/tents/1/dashboard", _ =>
            {
                stand = JsonNode.Parse("""{"isCustom":false,"sections":[{"id":"klima","tiles":[{"id":"a","w":1}]}]}""")!.AsObject();
                return (200, "{}");
            });
        var text = await Ausfuehren(fork, "PUT", "api/tents/1/dashboard", body: """{"isCustom":true,"sections":[]}""");

        Assert.Contains("Entfernt (2): sections[id=klima].tiles[id=b]; sections[id=klima].tiles[id=c].", text);
        Assert.DoesNotContain("Entfernt (1): sections[id=klima]", text);
        // isCustom wurde mit true geschickt und steht jetzt auf false — das ist keine gewollte Änderung.
        Assert.DoesNotContain("Geändert (", text);
        Assert.Contains("Außerdem verändert, ohne dass es im Rumpf stand (1) — bitte prüfen: isCustom: ja → nein.", text);
    }

    [Fact]
    public async Task B2_Ergaenzt_wird_aus_dem_Teil_mit_den_meisten_Feldern_nicht_aus_live()
    {
        var fork = Fork()
            .Antwort("GET", "api/steuerung/co2", 200, """{"einstellungen":{"zielWarmPpm":900,"mindestlaufzeitMin":25},"live":{"mindestlaufzeitMin":10,"ppm":700}}""")
            .Antwort("PUT", "api/steuerung/co2", 200, "{}");
        var text = await Ausfuehren(fork, "PUT", "api/steuerung/co2", body: """{"zielWarmPpm":1000}""");

        var put = JsonNode.Parse(Aufrufe(fork).Single(a => a.Methode == "PUT").Rumpf!)!.AsObject();
        Assert.Equal(25, put["mindestlaufzeitMin"]!.GetValue<int>());
        // kostenArtikelId fehlt im gelesenen Stand — Grow OS lässt leere Felder weg; es geht ausdrücklich leer hinaus.
        Assert.True(put.ContainsKey("kostenArtikelId"));
        Assert.Null(put["kostenArtikelId"]);
        Assert.Contains("Im gelesenen Stand leer, deshalb leer mitgeschickt: kostenArtikelId.", text);
    }

    [Fact]
    public async Task B3_Ohne_gelesenen_Stand_geht_kein_halber_PUT_hinaus()
    {
        var fork = Fork().Antwort("GET", "api/steuerung/entfeuchter", 503, """{"code":"x","message":"weg"}""");
        var text = await Ausfuehren(fork, "PUT", "api/steuerung/entfeuchter", body: """{"hystereseProzent":3}""");
        Assert.Contains("Den Stand vor der Änderung konnte ich nicht lesen", text);

        var ohne = await Ausfuehren(fork, "PUT", "api/steuerung/ohne-lesen", body: """{"a":1}""");
        Assert.Contains("keinen Lese-Weg", ohne);
        Assert.DoesNotContain(Aufrufe(fork), a => a.Methode == "PUT");

        // Mit allen Feldern oder ergaenzen=false geht es.
        await Ausfuehren(fork, "PUT", "api/steuerung/ohne-lesen", body: """{"a":1,"b":2}""");
        await Ausfuehren(fork, "PUT", "api/steuerung/ohne-lesen", body: """{"a":1}""", ergaenzen: false);
        Assert.Equal(2, Aufrufe(fork).Count(a => a.Methode == "PUT"));
    }

    [Fact]
    public async Task S1_Ein_neuer_Eintrag_vorne_in_der_Liste_ist_neu_und_kein_Umbenennen()
    {
        var stand = new JsonArray(new JsonObject { ["id"] = 2, ["title"] = "B" }, new JsonObject { ["id"] = 1, ["title"] = "A" });
        var fork = Fork()
            .Antwort("GET", "api/grows/1/journal", _ => (200, stand.ToJsonString()))
            .Antwort("POST", "api/grows/1/journal", _ =>
            {
                stand.Insert(0, new JsonObject { ["id"] = 3, ["title"] = "C" });
                return (201, """{"id":3,"title":"C"}""");
            });
        var text = await Ausfuehren(fork, "POST", "api/grows/1/journal", body: """{"title":"C"}""");

        Assert.Contains("Neu (1): [id=3].", text);
        Assert.DoesNotContain("Geändert", text);
        Assert.DoesNotContain("Verändert (", text);
        Assert.DoesNotContain("Außerdem verändert", text);
        Assert.Contains("4 Werte unverändert.", text);
    }

    // ------------------------------------------------------------------ Löschen

    [Fact]
    public async Task Ein_Loeschen_nennt_das_Geloeschte_und_die_Sicherung()
    {
        var fork = Fork()
            .Antwort("GET", @"api/kosten/nachfuellungen/41", 200, """{"id":41,"artikelName":"pH-Minus 1 L","kostenEur":12.9,"zeitpunkt":"2026-10-17T10:00:00"}""")
            .Antwort("DELETE", @"api/kosten/nachfuellungen/41", 204, string.Empty);
        var text = await Ausfuehren(fork, "DELETE", "api/kosten/nachfuellungen/41");

        Assert.Contains("Ausgeführt: DELETE /api/kosten/nachfuellungen/41 → 204. Grow OS hat vorher eine Sicherung angelegt.", text);
        Assert.Contains("Gelöscht: id: 41, artikelName: „pH-Minus 1 L\"", text);
        Assert.Contains("12.9", text);
        Assert.Equal(["GET", "DELETE"], Aufrufe(fork).Select(a => a.Methode).ToArray());
    }

    [Fact]
    public async Task Ein_Loeschen_ohne_eigenen_Leseweg_findet_den_Eintrag_in_der_Liste_darueber()
    {
        // Wie in Grow OS: GET api/kosten führt die Anschaffungen als Liste; daneben eine Nachfüllung mit derselben Id.
        var fork = Fork()
            .Antwort("GET", "api/kosten", 200, """{"nachfuellungen":[{"id":5,"artikelName":"Falsch"}],"anschaffungen":[{"id":4,"name":"Andere"},{"id":5,"name":"Testkauf","gesamtEur":4.5}]}""")
            .Antwort("DELETE", "api/kosten/anschaffungen/5", 204, string.Empty);
        var text = await Ausfuehren(fork, "DELETE", "api/kosten/anschaffungen/5");

        Assert.Contains("Gelöscht: id: 5, name: „Testkauf\"", text);
        Assert.DoesNotContain("Falsch", text);
        Assert.Equal(["GET", "DELETE"], Aufrufe(fork).Select(a => a.Methode).ToArray());
    }

    [Fact]
    public async Task Ein_Feld_das_es_nicht_gibt_wird_abgelehnt_statt_still_uebergangen()
    {
        var fork = Fork();
        var text = await Ausfuehren(fork, "POST", "api/kosten/anschaffungen", body: """{"name":"Test","preisEur":4.5}""");

        Assert.StartsWith("Nicht ausgeführt: preisEur ist kein Feld von AnschaffungRequest.", text);
        Assert.Contains("einzelpreisEur (Zahl)", text);
        Assert.Empty(Aufrufe(fork));
    }

    // ------------------------------------------------------------------ Absagen von Grow OS

    [Theory]
    [InlineData(403, """{"code":"ki_stufe_fehlt","message":"Dafür fehlt die Freigabe für Stufe „Verwaltung“."}""", "Dafür fehlt die Freigabe für Stufe „Verwaltung“.")]
    [InlineData(429, """{"code":"ki_hoechstwert","message":"Höchstwert erreicht: über KI-Assistenten sind höchstens 20 Schalt- und Dosierbefehle je Stunde erlaubt."}""", "Bitte später noch einmal versuchen.")]
    [InlineData(503, """{"code":"ki_sicherung_fehlgeschlagen","message":"Vor dieser Aktion legt Grow OS eine Sicherung an, und das ist gerade nicht gelungen. Es wurde nichts ausgeführt."}""", "Es wurde nichts ausgeführt.")]
    [InlineData(400, """{"code":"validation_failed","message":"Die Angaben passen nicht.","fieldErrors":{"HystereseProzent":["Höchstens 10 %."]}}""", "HystereseProzent: Höchstens 10 %.")]
    public async Task Eine_Absage_von_Grow_OS_kommt_als_Satz(int status, string rumpf, string erwartet)
    {
        var fork = EntfeuchterFork().Antwort("PUT", "api/steuerung/entfeuchter", status, rumpf);
        var text = await Ausfuehren(fork, "PUT", "api/steuerung/entfeuchter", body: """{"hystereseProzent":30}""");

        Assert.StartsWith("Nicht ausgeführt (PUT /api/steuerung/entfeuchter)", text);
        Assert.Contains(erwartet, text);
    }

    [Fact]
    public async Task Ohne_Verzeichnis_wird_nichts_ausgefuehrt()
    {
        var fork = new ForkAttrappe().Antwort("GET", "api/ki-zugriff/funktionen", 404, """{"code":"ki_aus","message":"aus"}""");
        var text = await Ausfuehren(fork, "GET", "api/grows");

        Assert.Contains("ohne Funktionsverzeichnis führe ich nichts aus", text);
        Assert.Contains("KI-Funktionen", text);
        Assert.Empty(Aufrufe(fork));
    }

    // ------------------------------------------------------------------ fork_funktionen

    private static Task<string> Funktionen(ForkAttrappe fork, string? suche = null, string? bereich = null, string? art = null, string? zugang = null)
        => Werkzeugkasten.TextAsync("fork_funktionen", Werkzeugkasten.Leser(fork, null),
            new Dictionary<string, object?> { ["suche"] = suche, ["bereich"] = bereich, ["art"] = art, ["zugang"] = zugang });

    [Fact]
    public async Task fork_funktionen_findet_mit_Suchwort_und_nennt_Stufen_und_Felder()
    {
        // Mit dem MCP-Schlüssel: das Verzeichnis zu lesen braucht keinen Fork-Schlüssel.
        var text = await Funktionen(Fork(), suche: "Entfeuchter");

        Assert.Contains("2 von 23 Funktionen passen zu „Entfeuchter\".", text);
        Assert.Contains("PUT /api/steuerung/entfeuchter — ändern · braucht Geräte schalten + Verwaltung · zählt als Schaltbefehl", text);
        Assert.Contains("Rumpf (body) EntfeuchterEinstellungen: hystereseProzent (Zahl)", text);
    }

    [Fact]
    public async Task fork_funktionen_filtert_nach_Bereich_Art_und_Zugang()
    {
        Assert.Contains("2 von 23 Funktionen passen im Bereich kosten (Art löschen).", await Funktionen(Fork(), bereich: "kosten", art: "löschen"));
        var nie = await Funktionen(Fork(), zugang: "nie");
        Assert.Contains("2 von 23", nie);
        Assert.Contains("NIE über einen Schlüssel — Die Schlüsselverwaltung", nie);
        Assert.Contains("0 von 23", await Funktionen(Fork(), suche: "gibtesnicht"));
    }

    [Fact]
    public void Zu_viele_Treffer_werden_nach_Bereichen_zusammengefasst()
    {
        var viele = Enumerable.Range(1, GrowTools.ZeilenBis + 1)
            .Select(i => new Funktionsverzeichnis.Funktion("GET", $"/api/x{i % 3}/w{i}", $"x{i % 3}", "lesen", "frei", [], false, null, false, false, "B", [], [], null, [], false, false))
            .ToList();

        var text = GrowTools.FunktionenBeschreiben(viele, null, null, null, null);

        Assert.Contains("Zu viele für eine Liste. Bereiche der Treffer: x0 (27), x1 (27), x2 (27).", text);
        Assert.DoesNotContain("/api/x1/w1 ", text);
    }

    [Fact]
    public async Task fork_funktionen_sagt_wenn_die_KI_aus_ist()
    {
        var fork = new ForkAttrappe().Antwort("GET", "api/ki-zugriff/funktionen", 404, """{"code":"ki_aus","message":"aus"}""");
        Assert.Contains("KI-Funktionen eingeschaltet", await Funktionen(fork));
    }
}
