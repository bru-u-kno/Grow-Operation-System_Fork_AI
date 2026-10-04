using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using GrowMcp.Tools;
using GrowOsAccess;

namespace GrowMcp.Tests;

/// <summary>
/// Eintragen und Schalten: mit dem MCP-Schlüssel nie, mit dem Fork-Schlüssel immer über Grow OS.
/// </summary>
/// <remarks>
/// <para>Fork AI (A-004, 03.10.2026). Gezählt wird über die Grundmenge — jedes
/// Werkzeug mit <see cref="BrauchtForkSchluesselAttribute"/> —, nicht über eine
/// Liste. Ein zwölftes Schreib-Werkzeug, das jemand ohne Wächter baut, fällt
/// damit von selbst auf.</para>
/// </remarks>
public sealed class SchreibWerkzeugeTests
{
    private static MethodInfo[] MitSchluessel()
        => Werkzeugkasten.Werkzeugmethoden().Where(m => m.GetCustomAttribute<BrauchtForkSchluesselAttribute>() is not null).ToArray();

    private static MethodInfo[] Schreibend()
        => MitSchluessel().Where(m => m.GetCustomAttribute<BrauchtForkSchluesselAttribute>()!.Stufe is not null).ToArray();

    public static IEnumerable<object[]> WerkzeugeMitSchluessel() => MitSchluessel().Select(m => new object[] { Werkzeugkasten.Name(m) });

    public static IEnumerable<object[]> SchreibendeWerkzeuge() => Schreibend().Select(m => new object[] { Werkzeugkasten.Name(m) });

    /// <summary>Ein Wert für die Messungs-Werkzeuge — ohne jeden Wert tragen sie mit Absicht nichts ein.</summary>
    private static readonly Dictionary<string, object?> EinWert = new() { ["ph"] = 5.8 };

    [Fact]
    public void Die_Zaehlung_sieht_ihre_Grundmenge()
    {
        Assert.True(MitSchluessel().Length >= 15, $"Nur {MitSchluessel().Length} Werkzeuge mit Fork-Schlüssel gefunden.");
        Assert.True(Schreibend().Length >= 11, $"Nur {Schreibend().Length} schreibende Werkzeuge gefunden.");
    }

    [Fact]
    public void Jedes_Werkzeug_ausserhalb_der_Lesewerkzeuge_traegt_das_Merkmal()
    {
        // Sonst entkäme ein Schreib-Werkzeug der Zählung „mcp-token schreibt nie".
        var ohne = Werkzeugkasten.Werkzeugmethoden()
            .Where(m => m.DeclaringType != typeof(GrowTools) && m.GetCustomAttribute<BrauchtForkSchluesselAttribute>() is null)
            .Select(Werkzeugkasten.Name)
            .ToList();
        Assert.Empty(ohne);

        // Und andersherum: die Lesewerkzeuge gehen mit dem MCP-Schlüssel.
        Assert.DoesNotContain(typeof(GrowTools).GetMethods(), m => m.GetCustomAttribute<BrauchtForkSchluesselAttribute>() is not null);
    }

    [Theory]
    [MemberData(nameof(WerkzeugeMitSchluessel))]
    public async Task Mit_dem_MCP_Schluessel_kommt_der_Hinweis_und_Grow_OS_wird_nicht_gefragt(string werkzeug)
    {
        var fork = ForkAttrappe.MitGrow();
        var text = await Werkzeugkasten.TextAsync(werkzeug, Werkzeugkasten.Leser(fork, schluessel: null));

        Assert.Contains(ForkFehler.SchluesselNoetig, text);
        // Nicht einmal angeklopft: kein Weg zu Grow OS, auch kein lesender.
        Assert.Empty(fork.Anfragen);
    }

    [Theory]
    [MemberData(nameof(SchreibendeWerkzeuge))]
    public async Task Mit_dem_Fork_Schluessel_schreibt_jedes_Werkzeug_und_traegt_ihn_bei_jeder_Anfrage(string werkzeug)
    {
        var fork = ForkAttrappe.MitGrow();
        await Werkzeugkasten.TextAsync(werkzeug, Werkzeugkasten.Leser(fork, Werkzeugkasten.ForkSchluessel), EinWert);

        var anfragen = fork.VonWerkzeugen.ToList();
        Assert.Contains(anfragen, a => a.Methode != "GET");
        Assert.All(anfragen, a => Assert.Equal($"Bearer {Werkzeugkasten.ForkSchluessel}", a.Authorization));
    }

    [Theory]
    [MemberData(nameof(SchreibendeWerkzeuge))]
    public async Task Eine_Absage_von_Grow_OS_kommt_als_deutscher_Satz_zurueck(string werkzeug)
    {
        const string meldung = "Dafür fehlt die Freigabe für Stufe „Grow planen“. Der Betreiber kann sie in Grow OS für diesen Schlüssel anhaken.";
        var fork = ForkAttrappe.MitGrow()
            .Antwort("POST", ".*", 403, $$"""{"code":"ki_stufe_fehlt","message":"{{meldung}}","status":403}""")
            .Antwort("PUT", ".*", 403, $$"""{"code":"ki_stufe_fehlt","message":"{{meldung}}","status":403}""")
            .Antwort("PATCH", ".*", 403, $$"""{"code":"ki_stufe_fehlt","message":"{{meldung}}","status":403}""");

        var text = await Werkzeugkasten.TextAsync(werkzeug, Werkzeugkasten.Leser(fork, Werkzeugkasten.ForkSchluessel), EinWert);

        Assert.StartsWith("Nicht ausgeführt", text);
        Assert.Contains(meldung, text);
    }

    [Theory]
    [InlineData(401, """{"code":"ki_schluessel_ungueltig","message":"Der Schlüssel ist ungültig."}""", "Der Schlüssel ist ungültig.")]
    [InlineData(403, """{"code":"ki_zugriff_aus","message":"Der Zugriff für KI-Assistenten ist in Grow OS ausgeschaltet."}""", "ausgeschaltet")]
    [InlineData(403, """{"code":"admin_access_required","message":"Admin access required."}""", "erst ab Grow OS Fork AI")]
    [InlineData(422, """{"code":"ki_hoechstwert","message":"KI-Höchstwert: 12 ml angefragt, über den Assistenten sind je Befehl höchstens 10 ml erlaubt."}""", "höchstens 10 ml")]
    [InlineData(429, """{"code":"ki_hoechstwert","message":"Höchstwert erreicht: über KI-Assistenten sind höchstens 20 Schalt- und Dosierbefehle je Stunde erlaubt."}""", "später noch einmal")]
    [InlineData(404, """{"code":"endpoint_not_found","message":"Es gibt keinen Endpunkt."}""", "aktualisieren")]
    [InlineData(500, "<html>kaputt</html>", "Grow OS antwortete mit 500")]
    public async Task Jede_Art_Absage_wird_ein_Satz_ohne_Ausnahme(int status, string rumpf, string erwartet)
    {
        var fork = ForkAttrappe.MitGrow().Antwort("POST", @"api/dosing/pumps/\d+/dose", status, rumpf);

        var text = await Werkzeugkasten.TextAsync("pumpe_dosieren", Werkzeugkasten.Leser(fork, Werkzeugkasten.ForkSchluessel),
            new Dictionary<string, object?> { ["pumpeId"] = 3, ["ml"] = 12.0 });

        Assert.StartsWith("Nicht ausgeführt (Dosieren)", text);
        Assert.Contains(erwartet, text);
    }

    [Fact]
    public async Task Angenommen_ist_nicht_dosiert()
    {
        // Grow OS antwortet 200, wenn seine eigene Sperre die Dosis verweigert —
        // mit dosed=false. „Schalten meldete Erfolg und veränderte nichts" (CLAUDE.md).
        var fork = ForkAttrappe.MitGrow().Antwort("POST", @"api/dosing/pumps/\d+/dose", 200,
            """{"dosed":false,"ml":0,"seconds":0,"reason":"pH liegt schon im Zielbereich."}""");

        var text = await Werkzeugkasten.TextAsync("pumpe_dosieren", Werkzeugkasten.Leser(fork, Werkzeugkasten.ForkSchluessel));

        Assert.StartsWith("NICHT dosiert", text);
        Assert.Contains("pH liegt schon im Zielbereich.", text);
    }

    [Fact]
    public async Task Ein_Home_Assistant_Dienst_mit_erfolg_false_ist_ein_Misserfolg()
    {
        var fork = ForkAttrappe.MitGrow().Antwort("POST", "api/ki-ha/dienst", 200,
            """{"erfolg":false,"meldung":"Home Assistant hat nicht geantwortet — bitte den Zustand nachsehen."}""");

        var text = await Werkzeugkasten.TextAsync("ha_dienst", Werkzeugkasten.Leser(fork, Werkzeugkasten.ForkSchluessel),
            new Dictionary<string, object?> { ["domain"] = "light", ["dienst"] = "turn_on", ["entityId"] = "light.zelt" });

        Assert.StartsWith("NICHT ausgeführt", text);
        Assert.Contains("Zustand nachsehen", text);
    }

    [Theory]
    [InlineData(503, "ha_nicht_eingerichtet", "Home Assistant ist in Grow OS nicht eingerichtet.")]
    [InlineData(502, "ha_nicht_erreichbar", "Home Assistant ist gerade nicht erreichbar.")]
    public async Task Home_Assistant_nicht_da_wird_ein_Satz(int status, string code, string meldung)
    {
        var fork = ForkAttrappe.MitGrow().Antwort("*", "api/ki-ha/.*", status, $$"""{"code":"{{code}}","message":"{{meldung}}"}""");
        var leser = Werkzeugkasten.Leser(fork, Werkzeugkasten.ForkSchluessel);

        Assert.Contains(meldung, await Werkzeugkasten.TextAsync("ha_zustaende", leser));
        Assert.Contains(meldung, await Werkzeugkasten.TextAsync("ha_dienst", leser,
            new Dictionary<string, object?> { ["domain"] = "switch", ["dienst"] = "turn_off", ["entityId"] = "switch.luefter" }));
    }

    [Theory]
    [InlineData("Light", "turn_on", "light.zelt", null, "Kleinbuchstaben")]
    [InlineData("light", "turn on", "light.zelt", null, "Kleinbuchstaben")]
    [InlineData("light", "turn_on", "switch.zelt", null, "Domain light")]
    [InlineData("light", "turn_on", "light.zelt,light.flur", null, "genau eine")]
    [InlineData("light", "turn_on", "light.zelt", """{"area_id":"haus"}""", "area_id")]
    [InlineData("light", "turn_on", "light.zelt", """{"target":{"entity_id":"light.flur"}}""", "target")]
    [InlineData("light", "turn_on", "light.zelt", "[1,2]", "JSON-Objekt")]
    public async Task Was_Grow_OS_mit_400_ablehnen_wuerde_geht_gar_nicht_erst_hin(string domain, string dienst, string entityId, string? daten, string erwartet)
    {
        var fork = ForkAttrappe.MitGrow();
        var text = await Werkzeugkasten.TextAsync("ha_dienst", Werkzeugkasten.Leser(fork, Werkzeugkasten.ForkSchluessel),
            new Dictionary<string, object?> { ["domain"] = domain, ["dienst"] = dienst, ["entityId"] = entityId, ["daten"] = daten });

        Assert.StartsWith("Nichts ausgeführt", text);
        Assert.Contains(erwartet, text);
        Assert.Empty(fork.Anfragen);
    }

    [Fact]
    public async Task Anzahl_und_Stunden_bleiben_in_den_Grenzen_von_Grow_OS()
    {
        // Grow OS gibt ausserhalb 1–500 bzw. 1–168 ein 400, statt zu kappen.
        var fork = ForkAttrappe.MitGrow();
        var leser = Werkzeugkasten.Leser(fork, Werkzeugkasten.ForkSchluessel);

        await Werkzeugkasten.TextAsync("ha_zustaende", leser, new Dictionary<string, object?> { ["anzahl"] = 5000 });
        await Werkzeugkasten.TextAsync("ha_zustaende", leser, new Dictionary<string, object?> { ["anzahl"] = 0 });
        await Werkzeugkasten.TextAsync("ha_verlauf", leser, new Dictionary<string, object?> { ["entityId"] = "sensor.a", ["stunden"] = 1000 });

        var pfade = fork.VonWerkzeugen.Select(a => a.Pfad).ToList();
        Assert.Contains(pfade, p => p.Contains("anzahl=500", StringComparison.Ordinal));
        Assert.Contains(pfade, p => p.EndsWith("anzahl=1", StringComparison.Ordinal));
        Assert.Contains(pfade, p => p.Contains("stunden=168", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(SchreibendeWerkzeuge))]
    public void Jedes_schreibende_Werkzeug_nennt_seine_Stufe(string werkzeug)
    {
        var methode = Werkzeugkasten.Werkzeug(werkzeug);
        var stufe = methode.GetCustomAttribute<BrauchtForkSchluesselAttribute>()!.Stufe!;
        var beschreibung = methode.GetCustomAttribute<DescriptionAttribute>()!.Description;

        Assert.Contains($"Braucht Stufe „{Stufen.Text(stufe)}\"", beschreibung);
    }

    [Fact]
    public void Die_Beschreibung_von_ha_dienst_nennt_Verwaltung_und_was_nie_geht()
    {
        var beschreibung = Werkzeugkasten.Werkzeug("ha_dienst").GetCustomAttribute<DescriptionAttribute>()!.Description;

        Assert.Contains($"Stufe „{Stufen.VerwaltungText}\"", beschreibung);
        Assert.Contains("Automationen", beschreibung);
        Assert.Contains("Helfer", beschreibung);
        Assert.Contains("nie", beschreibung);
        // Seit dem Prüferbefund vom 04.10.2026 gilt in Grow OS eine Positivliste
        // (KiHaEinstufung): die Beschreibung nennt sie, und dass alles andere nie geht.
        foreach (var domain in new[] { "light", "switch", "fan", "climate", "humidifier", "cover", "valve", "number",
                     "select", "button", "water_heater", "vacuum", "media_player" })
        {
            Assert.Contains(domain, beschreibung);
        }
        Assert.Contains("Jede andere Domain", beschreibung);
        Assert.Contains("ohne play_media", beschreibung);
    }

    [Fact]
    public async Task Ohne_Phase_gilt_die_des_Grows_und_nicht_still_Veg()
    {
        // Der Grow steht in der Blüte (ForkAttrappe.MitGrow). Grow OS selbst setzt
        // ohne Angabe Veg — eine Blüte-Messung würde gegen die Veg-Sollwerte beurteilt.
        var fork = ForkAttrappe.MitGrow();
        await Werkzeugkasten.TextAsync("messung_eintragen", Werkzeugkasten.Leser(fork, Werkzeugkasten.ForkSchluessel),
            new Dictionary<string, object?> { ["growId"] = 1, ["ph"] = 5.8 });

        Assert.Contains(fork.VonWerkzeugen, a => a is { Methode: "GET", Weg: "api/grows/1" });
        var post = Assert.Single(fork.VonWerkzeugen, a => a.Methode == "POST");
        using var rumpf = JsonDocument.Parse(post.Rumpf!);
        Assert.Equal("Flower", rumpf.RootElement.GetProperty("stage").GetString());
    }

    [Fact]
    public async Task Eine_genannte_Phase_wird_uebersetzt_und_der_Grow_nicht_gefragt()
    {
        var fork = ForkAttrappe.MitGrow();
        await Werkzeugkasten.TextAsync("messung_eintragen", Werkzeugkasten.Leser(fork, Werkzeugkasten.ForkSchluessel),
            new Dictionary<string, object?> { ["growId"] = 1, ["ph"] = 5.8, ["phase"] = "Blüte" });

        Assert.DoesNotContain(fork.VonWerkzeugen, a => a.Weg == "api/grows/1");
        using var rumpf = JsonDocument.Parse(Assert.Single(fork.VonWerkzeugen, a => a.Methode == "POST").Rumpf!);
        Assert.Equal("Flower", rumpf.RootElement.GetProperty("stage").GetString());
    }

    [Fact]
    public async Task Aendern_loescht_nichts_was_nicht_genannt_wurde()
    {
        // Grow OS ersetzt beim PUT die ganze Messung. Wer nur den pH schickt,
        // verlöre ORP, EC und Notiz.
        var fork = ForkAttrappe.MitGrow();
        await Werkzeugkasten.TextAsync("messung_aendern", Werkzeugkasten.Leser(fork, Werkzeugkasten.ForkSchluessel),
            new Dictionary<string, object?> { ["messungId"] = 7, ["ph"] = 6.1 });

        var put = Assert.Single(fork.VonWerkzeugen, a => a.Methode == "PUT");
        Assert.Equal("api/measurements/7", put.Weg);
        using var rumpf = JsonDocument.Parse(put.Rumpf!);
        var wurzel = rumpf.RootElement;
        Assert.Equal(6.1, wurzel.GetProperty("reservoirPh").GetDouble());
        Assert.Equal(450, wurzel.GetProperty("orpMv").GetDouble());
        Assert.Equal(1.2, wurzel.GetProperty("reservoirEc").GetDouble());
        Assert.Equal("alt", wurzel.GetProperty("notes").GetString());
        Assert.Equal("Flower", wurzel.GetProperty("stage").GetString());
        // Ohne neuen Zeitpunkt keiner — sonst schöbe Grow OS die Messung.
        Assert.False(wurzel.TryGetProperty("takenAtLocal", out _));
        Assert.False(wurzel.TryGetProperty("id", out _));
    }

    [Fact]
    public async Task Ohne_Werte_wird_nichts_eingetragen()
    {
        var fork = ForkAttrappe.MitGrow();
        var text = await Werkzeugkasten.TextAsync("messung_eintragen", Werkzeugkasten.Leser(fork, Werkzeugkasten.ForkSchluessel),
            new Dictionary<string, object?> { ["growId"] = 1 });

        Assert.StartsWith("Nichts eingetragen", text);
        Assert.Empty(fork.Anfragen);
    }

    // ------------------------------------------------------------ zugriff_pruefen

    private static string Beschreiben(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return SchreibWerkzeuge.ZugriffBeschreiben(doc.RootElement);
    }

    [Fact]
    public void Rueckfrage_ab_Stufe_heisst_diese_und_alle_danach()
    {
        var text = Beschreiben("""
            {"schluesselName":"Claude","stufen":["Dokumentieren","GeraeteSchalten"],"rueckfrageAbStufe":"GrowPlanen",
             "hoechstwerte":{"maxDosisMlJeBefehl":10,"maxSchaltbefehleJeStunde":20}}
            """);

        Assert.Contains("„Claude\"", text);
        Assert.Contains("Freigegeben: Dokumentieren, Geräte schalten.", text);
        Assert.Contains("Nicht freigegeben: Grow planen, Verwaltung", text);
        Assert.Contains("Vorher den Betreiber fragen bei: Grow planen, Geräte schalten, Verwaltung.", text);
        Assert.Contains("höchstens 10 ml je Dosierbefehl", text);
        Assert.Contains("höchstens 20 Schalt- und Dosierbefehle je Stunde", text);
    }

    [Fact]
    public void Die_neue_Liste_rueckfrageBei_wird_gelesen_und_geht_vor()
    {
        var text = Beschreiben("""
            {"stufen":["Dokumentieren","GeraeteSchalten"],"rueckfrageBei":["GeraeteSchalten"],"rueckfrageAbStufe":"Dokumentieren"}
            """);

        Assert.Contains("Vorher den Betreiber fragen bei: Geräte schalten.", text);
        Assert.DoesNotContain("fragen bei: Dokumentieren", text);
    }

    [Theory]
    [InlineData("""{"stufen":["Dokumentieren"],"rueckfrageAbStufe":null}""")]
    [InlineData("""{"stufen":["Dokumentieren"],"rueckfrageBei":[]}""")]
    public void Nie_fragen_heisst_nie_fragen(string json)
        => Assert.Contains("Rückfrage: nie", Beschreiben(json));

    /// <summary>
    /// Fork AI (A-005, 03.10.2026): genau die Form, die Grow OS jetzt schickt —
    /// <c>rueckfrageBei</c> je Schlüssel, kein <c>rueckfrageAbStufe</c> mehr.
    /// </summary>
    [Fact]
    public void Die_Antwort_von_heute_ohne_rueckfrageAbStufe()
    {
        var text = Beschreiben("""
            {"schluesselName":"Claude","stufen":["Dokumentieren","GrowPlanen","GeraeteSchalten"],"rueckfrageBei":["GrowPlanen"],
             "hoechstwerte":{"maxDosisMlJeBefehl":10,"maxSchaltbefehleJeStunde":20}}
            """);

        Assert.Contains("Freigegeben: Dokumentieren, Grow planen, Geräte schalten.", text);
        Assert.Contains("Nicht freigegeben: Verwaltung", text);
        Assert.Contains("Vorher den Betreiber fragen bei: Grow planen.", text);
        Assert.DoesNotContain("Grow OS sagt dazu nichts", text);
    }

    [Fact]
    public async Task zugriff_pruefen_fragt_Grow_OS_mit_dem_Schluessel()
    {
        var fork = new ForkAttrappe().Antwort("GET", "api/ki-zugriff/ich", 200,
            """{"schluesselName":"Claude","stufen":["Dokumentieren"],"rueckfrageBei":[],"hoechstwerte":{"maxDosisMlJeBefehl":10,"maxSchaltbefehleJeStunde":20}}""");

        var text = await Werkzeugkasten.TextAsync("zugriff_pruefen", Werkzeugkasten.Leser(fork, Werkzeugkasten.ForkSchluessel));

        Assert.Contains("Freigegeben: Dokumentieren.", text);
        Assert.Contains("Rückfrage: nie", text);
        Assert.Equal($"Bearer {Werkzeugkasten.ForkSchluessel}", Assert.Single(fork.VonWerkzeugen).Authorization);
    }
}
