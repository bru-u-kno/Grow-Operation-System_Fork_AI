using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GrowMcp.Tools;
using ModelContextProtocol.Protocol;

namespace GrowMcp.Tests;

/// <summary>
/// Fork AI (Paket C1, 18.10.2026): kamera_bild, steuerung, kosten, verbrauch_buchen, nachfuellung_eintragen,
/// anschaffung_eintragen, pflanze_aendern, probelauf_abbrechen.
/// </summary>
public sealed class PaketC1WerkzeugeTests
{
    private static Task<string> Aufruf(ForkAttrappe fork, string werkzeug, Dictionary<string, object?>? werte = null, string? schluessel = Werkzeugkasten.ForkSchluessel)
        => Werkzeugkasten.TextAsync(werkzeug, Werkzeugkasten.Leser(fork, schluessel), werte);

    private static List<Angekommen> Schreibend(ForkAttrappe fork) => fork.VonWerkzeugen.Where(a => a.Methode != "GET").ToList();

    private static JsonObject Rumpf(Angekommen a) => JsonNode.Parse(a.Rumpf!)!.AsObject();

    // ------------------------------------------------------------------ kamera_bild

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F'];

    [Fact]
    public async Task kamera_bild_liefert_das_Bild_des_Zelts_vom_Grow()
    {
        var fork = ForkAttrappe.MitGrow().Datei(@"api/live/tents/1/camera", "image/jpeg", Jpeg);
        var bloecke = (IEnumerable<ContentBlock>)(await Werkzeugkasten.AufrufenAsync(Werkzeugkasten.Werkzeug("kamera_bild"),
            Werkzeugkasten.Leser(fork, null), new Dictionary<string, object?> { ["growId"] = 1, ["kamera"] = "camera.zelt oben" }))!;

        var liste = bloecke.ToList();
        Assert.Equal("image/jpeg", Assert.IsType<ImageContentBlock>(liste[0]).MimeType);
        Assert.Contains("Livebild Zelt 1 (Grow 1), Kamera camera.zelt oben", Assert.IsType<TextContentBlock>(liste[1]).Text);
        Assert.Contains(fork.VonWerkzeugen, a => a.Pfad == "/api/live/tents/1/camera?entity=camera.zelt%20oben");
    }

    [Fact]
    public async Task kamera_bild_gibt_den_Grund_von_Grow_OS_weiter()
    {
        var fork = ForkAttrappe.MitGrow().Antwort("GET", @"api/live/tents/1/camera", 404,
            """{"ok":false,"status":"camera_missing","meldung":"Für dieses Zelt ist keine Kamera-Entity hinterlegt."}""");
        var bloecke = (IEnumerable<ContentBlock>)(await Werkzeugkasten.AufrufenAsync(Werkzeugkasten.Werkzeug("kamera_bild"),
            Werkzeugkasten.Leser(fork, null), new Dictionary<string, object?> { ["growId"] = 1 }))!;

        Assert.Equal("Für dieses Zelt ist keine Kamera-Entity hinterlegt.", Assert.IsType<TextContentBlock>(Assert.Single(bloecke)).Text);
    }

    [Fact]
    public void Ein_altes_Bild_wird_als_solches_benannt()
    {
        var jetzt = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
        var kopf = new Dictionary<string, string> { ["X-Camera-Live"] = "false", ["X-Camera-Captured-At"] = "2026-10-18T11:20:00.0000000+00:00" };

        var text = GrowTools.KameraNotiz(1, 1, null, kopf, jetzt);

        Assert.Contains("KEIN frisches Bild", text);
        Assert.Contains("vor 40 min", text);
        Assert.Contains("frisch aufgenommen", GrowTools.KameraNotiz(1, 1, null, new Dictionary<string, string>(kopf) { ["X-Camera-Live"] = "true" }, jetzt));
    }

    // ------------------------------------------------------------------ steuerung, kosten

    private const string Uebersicht = """
        {"haErreichbar":true,"module":[
          {"kennung":"entfeuchter","titel":"Entfeuchter","status":"aus","kurz":"Luftfeuchte · ein ab 58,0 % · aus unter 55,0 %","wert":"54,0 %","unterzeile":"bereit · VPD 1,20"},
          {"kennung":"zuluft","titel":"Zuluft","status":"warn","kurz":"Ansaugen ab 1,0 g/m³","wert":"–","unterzeile":"aus","zustand":{"wort":"wartet","satz":"Außenluft zu feucht","ton":"neutral"}}],
         "nichtEingerichtet":[{"kennung":"chiller","titel":"Wasserkühler"}]}
        """;

    [Fact]
    public void steuerung_liest_die_Uebersicht_und_den_laufenden_Probelauf_vor()
    {
        var text = GrowTools.SteuerungBeschreiben(Uebersicht,
            """[{"id":7,"modulTitel":"Entfeuchter","status":"Laeuft","restSekunden":61},{"id":6,"status":"Fertig"}]""",
            "entfeuchter", """{"live":{"feuchteProzent":54,"portAn":false,"bremsen":[]},"einstellungen":{"hystereseProzent":3,"meldung":{"aktiv":true}},"abweichungen":[]}""");

        Assert.Contains("Entfeuchter [entfeuchter]: aus · Luftfeuchte · ein ab 58,0 % · aus unter 55,0 % · jetzt 54,0 % · bereit · VPD 1,20", text);
        Assert.Contains("Zuluft [zuluft]: WARNUNG (wartet: Außenluft zu feucht)", text);
        Assert.Contains("Nicht eingerichtet: Wasserkühler.", text);
        Assert.Contains("Probelauf läuft: Id 7 am Modul Entfeuchter, noch 2 min", text);
        Assert.Contains("Live: feuchteProzent 54; portAn nein; bremsen keine", text);
        Assert.Contains("Einstellungen: hystereseProzent 3; meldung (aktiv ja)", text);
    }

    [Fact]
    public async Task steuerung_nimmt_nur_bekannte_Module()
    {
        var fork = ForkAttrappe.MitGrow().Antwort("GET", "api/steuerung", 200, Uebersicht)
            .Antwort("GET", "api/steuerung/chiller", 200, """{"live":{"wasserC":19.5}}""");
        Assert.Contains("ist kein Modul", await Aufruf(fork, "steuerung", new() { ["modul"] = "toaster" }, null));
        Assert.Contains("wasserC 19,5", await Aufruf(fork, "steuerung", new() { ["modul"] = "Kühler" }, null));
        Assert.Contains(fork.VonWerkzeugen, a => a.Weg == "api/steuerung/chiller");
    }

    [Fact]
    public void kosten_dampft_die_Kostenseite_ein()
    {
        var text = GrowTools.KostenBeschreiben(ForkAttrappe.KostenSeite);

        Assert.Contains("Grow 1 „Test\", Tag 74, Blüte, 4 Pflanzen.", text);
        Assert.Contains("Gesamt 165,27 € = Strom 145,77 € + Artikel 19,50 € + Anschaffungen 0,00 €", text);
        Assert.Contains("Strom: 455,5 kWh seit Start, 6,2 kWh/Tag = 2,00 €/Tag bei 32 ct/kWh.", text);
        Assert.Contains("CO₂-Flasche: 10 kg seit 02.09.2026, Füllstand 50 % (gemessen), reicht bis 19.11.2026 (64 Tage, gemessen), 0,61 €/Tag", text);
        Assert.Contains("2 pH-Minus (ml)", text);
        Assert.Contains("4 Purolyt alt (ml) — inaktiv", text);
    }

    // ------------------------------------------------------------------ verbrauch_buchen

    [Fact]
    public async Task verbrauch_bucht_per_Name_in_der_Einheit_des_Artikels()
    {
        var fork = ForkAttrappe.MitGrow();
        var text = await Aufruf(fork, "verbrauch_buchen", new() { ["growId"] = 1, ["artikel"] = "ph minus", ["menge"] = 20.0 });

        Assert.Equal("Gebucht: 20 ml pH-Minus auf Grow 1. Was es kostet, rechnet Grow OS (Werkzeug kosten).", text);
        var post = Rumpf(Assert.Single(Schreibend(fork)));
        Assert.Equal(1, post["growId"]!.GetValue<int>());
        Assert.Equal(2, post["zeilen"]![0]!["artikelId"]!.GetValue<int>());
        Assert.Equal(20, post["zeilen"]![0]!["menge"]!.GetValue<double>());
    }

    [Fact]
    public async Task verbrauch_raet_nicht_bei_mehreren_Treffern_und_nimmt_Inaktive_nur_ohne_Aktive()
    {
        var fork = ForkAttrappe.MitGrow().Antwort("GET", "api/kosten", 200,
            ForkAttrappe.KostenSeite.Replace("\"name\":\"pH-Minus\"", "\"name\":\"Purolyt Plus\""));
        var text = await Aufruf(fork, "verbrauch_buchen", new() { ["growId"] = 1, ["artikel"] = "purol", ["menge"] = 300.0 });

        Assert.StartsWith("Nichts gebucht: „purol\" passt auf mehrere Artikel", text);
        Assert.Contains("2 Purolyt Plus (ml)", text);
        Assert.DoesNotContain("Purolyt alt", text);
        Assert.Empty(Schreibend(fork));

        // Der genaue Name gewinnt vor den ähnlichen.
        Assert.StartsWith("Gebucht: 300 ml Purolyt auf Grow 1.", await Aufruf(fork, "verbrauch_buchen", new() { ["growId"] = 1, ["artikel"] = "Purolyt", ["menge"] = 300.0 }));
    }

    [Fact]
    public async Task verbrauch_lehnt_den_CO2_Artikel_der_Steuerung_ab()
    {
        var fork = ForkAttrappe.MitGrow();
        var text = await Aufruf(fork, "verbrauch_buchen", new() { ["growId"] = 1, ["artikel"] = "co2", ["menge"] = 0.2 });

        Assert.Contains("die CO₂-Steuerung selbst bucht", text);
        Assert.Empty(Schreibend(fork));
    }

    [Theory]
    [InlineData(0.0, null, "größer als 0")]
    [InlineData(-5.0, null, "größer als 0")]
    [InlineData(5.0, "2099-01-01T10:00", "in der Zukunft")]
    [InlineData(5.0, "gestern", "kann ich nicht lesen")]
    public async Task verbrauch_prueft_Menge_und_Zeitpunkt_vorher(double menge, string? zeitpunkt, string erwartet)
    {
        var fork = ForkAttrappe.MitGrow();
        var text = await Aufruf(fork, "verbrauch_buchen", new() { ["growId"] = 1, ["artikel"] = "ph", ["menge"] = menge, ["zeitpunkt"] = zeitpunkt });

        Assert.Contains(erwartet, text);
        Assert.Empty(Schreibend(fork));
    }

    // ------------------------------------------------------------------ nachfuellung_eintragen

    [Theory]
    [InlineData(true, "Die vorige Füllung vom 02.09.2026 ist als leer geschlossen.")]
    [InlineData(false, "Die vorige Füllung vom 02.09.2026 bleibt offen (vorherigeLeer=false).")]
    public async Task nachfuellung_schickt_vorherigeLeer_ausdruecklich_und_sagt_was_mit_der_vorigen_ist(bool leer, string erwartet)
    {
        var fork = ForkAttrappe.MitGrow();
        var text = await Aufruf(fork, "nachfuellung_eintragen", new() { ["artikel"] = "CO2", ["menge"] = 10.0, ["kostenEur"] = 38.0, ["vorherigeLeer"] = leer, ["growId"] = 1 });

        Assert.Contains("Neue Füllung „CO₂-Flasche\" 10 kg für 38,00 €", text);
        Assert.Contains(erwartet, text);
        var post = Rumpf(Assert.Single(Schreibend(fork)));
        Assert.Equal(leer, post["vorherigeLeer"]!.GetValue<bool>());
        Assert.DoesNotContain("ACHTUNG", text);
    }

    [Fact]
    public async Task Eine_rueckdatierte_Fuellung_schliesst_die_spaetere_nicht()
    {
        // Grow OS schließt nur Füllungen VOR der neuen — die offene ist vom 02.09.2026.
        var fork = ForkAttrappe.MitGrow();
        var text = await Aufruf(fork, "nachfuellung_eintragen",
            new() { ["artikel"] = "CO2", ["menge"] = 10.0, ["vorherigeLeer"] = true, ["zeitpunkt"] = "2026-09-01T10:00" });

        Assert.Contains("Die vorige Füllung vom 02.09.2026 bleibt offen — die neue liegt zeitlich vor ihr.", text);
    }

    [Fact]
    public async Task Ein_hoher_Betrag_wird_genannt_ein_negativer_abgelehnt()
    {
        var fork = ForkAttrappe.MitGrow();
        Assert.Contains("ACHTUNG: ungewöhnlich hoher Betrag — 3800,00 €",
            await Aufruf(fork, "nachfuellung_eintragen", new() { ["artikel"] = "CO2", ["menge"] = 10.0, ["kostenEur"] = 3800.0, ["vorherigeLeer"] = true }));
        Assert.Contains("darf nicht negativ", await Aufruf(fork, "nachfuellung_eintragen", new() { ["artikel"] = "CO2", ["menge"] = 10.0, ["kostenEur"] = -1.0, ["vorherigeLeer"] = true }));
        Assert.Contains("ACHTUNG: ungewöhnlich hoher Betrag — 1200,00 €",
            await Aufruf(fork.Antwort("POST", "api/kosten/anschaffungen", 201, """{"id":4,"gesamtEur":1200}"""), "anschaffung_eintragen",
                new() { ["name"] = "Lampe", ["einzelpreisEur"] = 600.0, ["stueck"] = 2 }));
        Assert.Contains("nicht negativ", await Aufruf(fork, "anschaffung_eintragen", new() { ["name"] = "X", ["einzelpreisEur"] = -3.0 }));
        Assert.Equal(2, Schreibend(fork).Count);
    }

    [Fact]
    public async Task anschaffung_nennt_den_Gesamtbetrag()
    {
        var fork = ForkAttrappe.MitGrow();
        var text = await Aufruf(fork, "anschaffung_eintragen", new() { ["name"] = " Luftbefeuchter ", ["einzelpreisEur"] = 49.0, ["growId"] = 1 });

        Assert.Equal("Anschaffung „Luftbefeuchter\" eingetragen: 1 × 49,00 € = 49,00 € (Grow 1).", text);
        Assert.Equal("Luftbefeuchter", Rumpf(Assert.Single(Schreibend(fork)))["name"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------ pflanze_aendern

    /// <summary>Eine Pflanze, deren PUT wie Grow OS das Ganze ersetzt — wahlweise mit einer Nebenwirkung.</summary>
    private static ForkAttrappe PflanzenFork(string pflanze = ForkAttrappe.Pflanze, bool verliertNotiz = false)
    {
        var stand = JsonNode.Parse(pflanze)!.AsObject();
        return ForkAttrappe.MitGrow()
            .Antwort("GET", @"api/plants/2", _ => (200, stand.ToJsonString()))
            .Antwort("PUT", @"api/plants/2", a =>
            {
                var rumpf = Rumpf(a);
                foreach (var (n, w) in rumpf)
                {
                    if (w is null) stand.Remove(n);
                    else stand[n] = w.DeepClone();
                }
                if (verliertNotiz && rumpf["plantStatus"]?.GetValue<string>() == "Culled") stand.Remove("notes");
                if (rumpf["strainId"] is JsonValue s) stand["strainName"] = s.GetValue<int>() == 2 ? "Gorilla Glue" : "White Widow";
                return (200, stand.ToJsonString());
            });
    }

    [Fact]
    public async Task pflanze_aendern_aendert_nur_den_Status_und_belegt_den_Rest()
    {
        var fork = PflanzenFork();
        var text = await Aufruf(fork, "pflanze_aendern", new() { ["pflanzeId"] = 2, ["status"] = "raus" });

        Assert.StartsWith("Pflanze 2 „Pflanze 2\" (Topf 2): Status Aktiv → Ausgemustert. Alle anderen", text);
        Assert.Contains("Angaben unverändert (Prüfsumme", text);
        var put = Rumpf(Assert.Single(Schreibend(fork)));
        Assert.Equal(SchreibWerkzeuge.PflanzenFelder.Order(), put.Select(p => p.Key).Order());
        Assert.Equal("Culled", put["plantStatus"]!.GetValue<string>());
        Assert.Equal("alt", put["notes"]!.GetValue<string>());
        Assert.Equal(2, put["siteIndex"]!.GetValue<int>());
        Assert.Null(put["endedAt"]);
    }

    [Fact]
    public async Task Beim_Topfwechsel_zieht_ein_automatischer_Name_mit_ein_eigener_nicht()
    {
        var fork = PflanzenFork();
        Assert.Contains("Topf 2 → 5; Name „Pflanze 2\" → „Pflanze 5\"", await Aufruf(fork, "pflanze_aendern", new() { ["pflanzeId"] = 2, ["topf"] = "5" }));

        var eigen = PflanzenFork(ForkAttrappe.Pflanze.Replace("\"label\":\"Pflanze 2\"", "\"label\":\"Zitronenpheno\""));
        var text = await Aufruf(eigen, "pflanze_aendern", new() { ["pflanzeId"] = 2, ["topf"] = "kein" });
        Assert.Contains("Topf 2 → leer.", text);
        Assert.Equal("Zitronenpheno", Rumpf(Assert.Single(Schreibend(eigen)))["label"]!.GetValue<string>());
    }

    [Fact]
    public async Task Eine_Nebenwirkung_wird_zurueckgeschrieben()
    {
        var fork = PflanzenFork(verliertNotiz: true);
        var text = await Aufruf(fork, "pflanze_aendern", new() { ["pflanzeId"] = 2, ["status"] = "raus" });

        Assert.StartsWith("ACHTUNG: Beim Speichern von Pflanze 2", text);
        Assert.Contains("(notes)", text);
        var puts = Schreibend(fork);
        Assert.Equal(2, puts.Count);
        Assert.Equal("Active", Rumpf(puts[1])["plantStatus"]!.GetValue<string>());
        Assert.Equal("alt", Rumpf(puts[1])["notes"]!.GetValue<string>());
    }

    [Fact]
    public async Task Eine_Sorte_per_Name_und_unbekannte_Angaben_werden_abgelehnt()
    {
        var fork = PflanzenFork();
        Assert.Contains("Sorte „White Widow\" → „Gorilla Glue\"", await Aufruf(fork, "pflanze_aendern", new() { ["pflanzeId"] = 2, ["sorte"] = "gorilla" }));
        Assert.Contains("keine Sorte passt", await Aufruf(fork, "pflanze_aendern", new() { ["pflanzeId"] = 2, ["sorte"] = "Haze" }));
        Assert.Contains("ist kein Status", await Aufruf(fork, "pflanze_aendern", new() { ["pflanzeId"] = 2, ["status"] = "kaputt" }));
        Assert.Contains("ist kein Topf", await Aufruf(fork, "pflanze_aendern", new() { ["pflanzeId"] = 2, ["topf"] = "0" }));
        Assert.Contains("keine Angabe genannt", await Aufruf(fork, "pflanze_aendern", new() { ["pflanzeId"] = 2 }));
        Assert.Single(Schreibend(fork));
    }

    /// <summary>Eine Wahrheit: die Namensregel steht in der Oberfläche; diese Kopie muss ihr gleichen.</summary>
    [Fact]
    public void Die_Namensregel_ist_die_der_Oberflaeche()
    {
        var datei = Path.Combine(Wurzel(), "GrowDiary.React", "src", "features", "grow-detail", "pflanzen-namen.ts");
        var quelle = File.ReadAllText(datei);
        var muster = Regex.Match(quelle, @"function istAutomatischerName[\s\S]*?return /(?<muster>[^\n]+)/i\.test\(label\) \|\| label\.trim\(\) === '(?<ohne>[^']+)'");
        Assert.True(muster.Success, "istAutomatischerName in pflanzen-namen.ts nicht gefunden — umbenannt?");
        Assert.Equal(SchreibWerkzeuge.AutomatischerNameMuster, muster.Groups["muster"].Value);
        Assert.Equal(SchreibWerkzeuge.PflanzenName(null), muster.Groups["ohne"].Value);
        Assert.Contains("`Pflanze ${topf}`", quelle);
    }

    private static string Wurzel()
    {
        var ordner = AppContext.BaseDirectory;
        while (ordner is not null && !Directory.Exists(Path.Combine(ordner, "GrowDiary.React"))) ordner = Path.GetDirectoryName(ordner);
        return ordner ?? throw new InvalidOperationException("Projektwurzel nicht gefunden.");
    }

    // ------------------------------------------------------------------ probelauf_abbrechen

    [Fact]
    public async Task probelauf_abbrechen_nimmt_den_einen_laufenden()
    {
        var fork = ForkAttrappe.MitGrow();
        var text = await Aufruf(fork, "probelauf_abbrechen");

        Assert.Equal("api/steuerung/probelauf/7/abbrechen", Assert.Single(Schreibend(fork)).Weg);
        Assert.StartsWith("Probelauf 7 (Entfeuchter) abgebrochen, Status jetzt: Nachlauf.", text);
    }

    [Theory]
    [InlineData("[]", "Es läuft kein Probelauf")]
    [InlineData("""[{"id":7,"modulTitel":"Entfeuchter","status":"Laeuft"},{"id":8,"modulTitel":"CO₂","status":"Laeuft"}]""", "7 (Entfeuchter); 8 (CO₂)")]
    [InlineData("""[{"id":6,"status":"Fertig"}]""", "Es läuft kein Probelauf")]
    public async Task probelauf_abbrechen_raet_nicht(string liste, string erwartet)
    {
        var fork = ForkAttrappe.MitGrow().Antwort("GET", "api/steuerung/probelauf", 200, liste);
        Assert.Contains(erwartet, await Aufruf(fork, "probelauf_abbrechen"));
        Assert.Empty(Schreibend(fork));
    }
}
