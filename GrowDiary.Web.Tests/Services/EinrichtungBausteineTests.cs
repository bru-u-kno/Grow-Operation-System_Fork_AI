using System.Text.Json.Nodes;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (A-016, Etappe 1): Die Bausteine, die bei einer Neuinstallation bisher fehlten —
/// Plan-Zielwerte, Entfeuchter-Rechenwerte, Entfeuchter-Regelung, CO₂-Licht-aus-Sicherung.
/// Geprüft an der gefüllten Vorlage, so wie sie nach Home Assistant ginge.
/// </summary>
public sealed class EinrichtungBausteineTests
{
    private static string Wurzel => SteuerungAutomationService.VorlagenWurzel;

    private static JsonObject Laden(string modul, string vorlage)
        => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(Wurzel, modul, vorlage + ".json")))!;

    private static Dictionary<string, string> Rollen(string modul, bool nurPflicht = false, string? domaene = null)
        => SteuerungGeraeteRollen.FuerModul(modul)
            .Where(r => !nurPflicht || r.Pflicht)
            .ToDictionary(r => r.Schluessel,
                r => $"{(r.Schluessel == "port_schalter" && domaene is not null ? domaene : r.Domains[0])}.t_{r.Schluessel}",
                StringComparer.Ordinal);

    private static IEnumerable<JsonObject> Objekte(JsonNode? knoten)
    {
        switch (knoten)
        {
            case JsonObject o:
                yield return o;
                foreach (var (_, kind) in o) foreach (var k in Objekte(kind)) yield return k;
                break;
            case JsonArray a:
                foreach (var kind in a) foreach (var k in Objekte(kind)) yield return k;
                break;
        }
    }

    /// <summary>Der Text, den Home Assistant auswertet — ohne Beschreibung und Aliase (die erwähnen nur).</summary>
    private static string Wirksam(JsonObject automation)
    {
        var kopie = (JsonObject)automation.DeepClone();
        foreach (var o in Objekte(kopie).ToList()) { o.Remove("description"); o.Remove("alias"); }
        return kopie.ToJsonString();
    }

    private static JsonObject Gefuellt(string modul, string vorlage, Dictionary<string, string> rollen)
        => SteuerungAutomationService.Fuellen(Laden(modul, vorlage), rollen)
           ?? throw new InvalidOperationException($"{modul}/{vorlage} liess sich nicht füllen.");

    // ---------------------------------------------------------- Plan-Zielwerte

    [Theory]
    [InlineData("input_number.vpd_ziel_unten", 0.4, 2.0, 0.05)]
    [InlineData("input_number.vpd_ziel_abschaltung", 0.4, 2.0, 0.05)]
    [InlineData("input_number.vpd_blatt_offset", -5.0, 0.0, 0.1)]
    public void DiePlanZielwerteSindImKatalogMitDenGrenzenDerAnlage(string entity, double min, double max, double schritt)
    {
        var b = Assert.Single(SteuerungBauteile.FuerModul("entfeuchter"), x => x.EntityId == entity);
        Assert.Equal(BauteilArt.Zahl, b.Art);
        Assert.Equal(min, b.Min);
        Assert.Equal(max, b.Max);
        Assert.Equal(schritt, b.Schritt);
        Assert.Null(b.HaengtAn); // ohne Zusatzgerät nötig: der Plan schreibt sie immer
    }

    [Fact]
    public void DerHelferDienstLegtDiePlanZielwerteAn()
    {
        // Die Zeile im Katalog allein ist keine Anlage: der Dienst muss sie auch als offen sehen.
        var offen = SteuerungBauteile.Anwendbar("entfeuchter", Rollen("entfeuchter").Keys.ToList())
            .Where(b => SteuerungHelferService.Kann(b.Art)).Select(b => b.EntityId).ToList();
        Assert.Contains("input_number.vpd_ziel_unten", offen);
        Assert.Contains("input_number.vpd_ziel_abschaltung", offen);
        Assert.Contains("input_number.vpd_blatt_offset", offen);
    }

    // -------------------------------------------------- Entfeuchter-Rechenwerte

    [Theory]
    [InlineData("sensor.trotec_feuchte_ein_aktiv")]
    [InlineData("sensor.trotec_feuchte_aus_aktiv")]
    [InlineData("sensor.trotec_temp_max_aktiv")]
    [InlineData("binary_sensor.trotec_feuchte_uber_ein")]
    public void JederEntfeuchterRechenwertWirdMitPflichtRollenAngelegt(string entity)
    {
        var b = Assert.Single(SteuerungBauteile.FuerModul("entfeuchter"), x => x.EntityId == entity);
        var rollen = Rollen("entfeuchter", nurPflicht: true);
        Assert.Contains(b, SteuerungBauteile.Anwendbar("entfeuchter", rollen.Keys.ToList()));

        var fertig = SteuerungBauteile.VorlageFuellen(b.Vorlage!, rollen);
        Assert.NotNull(fertig);
        Assert.DoesNotContain("[[", fertig);
    }

    [Fact]
    public void DieSchwellenRechnenMitDenZugeordnetenFuehlernUndDemLicht()
    {
        var rollen = Rollen("entfeuchter");
        string Text(string e) => SteuerungBauteile.VorlageFuellen(
            SteuerungBauteile.FuerModul("entfeuchter").Single(b => b.EntityId == e).Vorlage!, rollen)!;

        var ein = Text("sensor.trotec_feuchte_ein_aktiv");
        Assert.Contains("states('binary_sensor.t_licht_zustand')", ein);
        Assert.Contains("states('sensor.t_zelt_temp')", ein);
        Assert.Contains("input_number.vpd_ziel_unten", ein);
        Assert.Contains("input_number.trotec_feuchte_ein_tag", ein);

        var aus = Text("sensor.trotec_feuchte_aus_aktiv");
        Assert.Contains("input_number.vpd_ziel_abschaltung", aus);
        Assert.Contains("input_number.trotec_hysterese", aus);

        Assert.Contains("states('sensor.t_zelt_rh')", Text("binary_sensor.trotec_feuchte_uber_ein"));
    }

    [Fact]
    public void OhneCo2HelferGiltKeinFeuchteDeckel()
    {
        // Brus Fassung fiel ohne die CO₂-Helfer auf 65 − 3 = 62 % zurück — für jeden, der keine
        // CO₂-Begasung hat, wäre das eine fremde, unsichtbare Obergrenze gewesen.
        foreach (var e in new[] { "sensor.trotec_feuchte_ein_aktiv", "sensor.trotec_feuchte_aus_aktiv" })
        {
            var v = SteuerungBauteile.FuerModul("entfeuchter").Single(b => b.EntityId == e).Vorlage!;
            Assert.Contains("input_number.co2_rh_obergrenze') | float(100)", v);
            Assert.DoesNotContain("float(65)", v);
        }
    }

    // ----------------------------------------------------- Entfeuchter-Regelung

    [Fact]
    public void DieEntfeuchterRegelungWirdAngelegtUndGehoertZumKatalog()
    {
        var b = Assert.Single(SteuerungBauteile.FuerModul("entfeuchter"), x => x.VorlagenDatei == "regelung");
        Assert.Equal(BauteilArt.Automation, b.Art);
        Assert.Equal(ProbelaufRolle.Pausieren, b.Probelauf);
        Assert.Equal(1, SteuerungAutomationService.VorlagenFassung("entfeuchter", "regelung"));
    }

    [Fact]
    public void MitAllenRollenKommenDieZuluftZweigeMit()
    {
        var f = Gefuellt("entfeuchter", "regelung", Rollen("entfeuchter"));
        var text = Wirksam(f);
        Assert.Contains("rh_hoch_lang", text);
        Assert.Contains("binary_sensor.t_zuluft_bedarf", text);
        Assert.Contains("sensor.t_zuluft_stufe_ist", text);
        // Marker sind ausgesiebt, nicht stehengeblieben (Schlüssel, nicht Wörter im Text).
        Assert.DoesNotContain(Objekte(f), o => o.ContainsKey("wenn") || o.ContainsKey("wennNicht"));
    }

    [Fact]
    public void OhneZuluftRollenBleibtEineSchlankeGueltigeRegelung()
    {
        var f = Gefuellt("entfeuchter", "regelung", Rollen("entfeuchter", nurPflicht: true));
        var text = Wirksam(f);
        Assert.DoesNotContain("rh_hoch_lang", text);
        Assert.DoesNotContain("zuluft", text);
        Assert.DoesNotContain("trotec_wartezeit_aussenluft", text);

        // Kein leerer or/and-Block: Home Assistant lehnt die ganze Automation ab.
        foreach (var o in Objekte(f).Where(o => o["conditions"] is JsonArray && o["condition"] is not null))
        {
            Assert.True(((JsonArray)o["conditions"]!).Count > 0, "leere conditions: " + o.ToJsonString());
        }
        // Jeder Zweig kann noch schalten.
        var zweige = ((JsonArray)((JsonObject)f["actions"]![0]!)["choose"]!).Cast<JsonObject>().ToList();
        Assert.Equal(6, zweige.Count);
        Assert.All(zweige, z => Assert.NotEmpty((JsonArray)z["sequence"]!));
        // Jeder Auslöser, auf den ein Zweig wartet, existiert.
        var ids = ((JsonArray)f["triggers"]!).Cast<JsonObject>().Select(t => t["id"]?.ToString()).ToHashSet();
        foreach (var o in Objekte(zweige.Cast<JsonNode>().ToArray().Aggregate(new JsonArray(), (a, z) => { a.Add(z.DeepClone()); return a; }))
                     .Where(o => o["condition"]?.ToString() == "trigger"))
        {
            foreach (var id in (JsonArray)o["id"]!) Assert.Contains(id!.ToString(), ids);
        }
    }

    [Theory]
    [InlineData("select", "select.select_option")]
    [InlineData("switch", "switch.turn_")]
    [InlineData("input_boolean", "input_boolean.turn_")]
    public void DerSchaltbefehlPasstZurDomaeneDesEntfeuchters(string domaene, string praefix)
    {
        var f = Gefuellt("entfeuchter", "regelung", Rollen("entfeuchter", domaene: domaene));
        var ziel = $"{domaene}.t_port_schalter";
        var befehle = Objekte(f).Where(o => o["target"]?["entity_id"]?.ToString() == ziel)
            .Select(o => o["action"]!.ToString()).ToList();
        Assert.NotEmpty(befehle);
        Assert.All(befehle, b => Assert.StartsWith(praefix, b));
    }

    // ---------------------------------------------------- Licht-aus-Sicherung

    [Fact]
    public void DieLichtAusSicherungSchliesstDasVentilSobaldDasLichtAusgeht()
    {
        var b = Assert.Single(SteuerungBauteile.FuerModul("co2"), x => x.VorlagenDatei == "licht_aus_sicherung");
        Assert.True(b.Pflicht); // eine Sicherung ist keine Kür
        Assert.Null(b.HaengtAn);

        var f = Gefuellt("co2", "licht_aus_sicherung", Rollen("co2"));
        var trigger = (JsonObject)((JsonArray)f["triggers"]!)[0]!;
        Assert.Equal("binary_sensor.t_licht", trigger["entity_id"]!.ToString());
        Assert.Equal("off", trigger["to"]!.ToString());
        var aktion = (JsonObject)((JsonArray)f["actions"]!)[0]!;
        Assert.Equal("select.select_option", aktion["action"]!.ToString());
        Assert.Equal("Off", aktion["data"]!["option"]!.ToString());
    }

    [Fact]
    public void DieLichtAusSicherungSchliesstAuchEinVentilAnEinerSteckdose()
    {
        var f = Gefuellt("co2", "licht_aus_sicherung", Rollen("co2", domaene: "switch"));
        var aktion = (JsonObject)((JsonArray)f["actions"]!)[0]!;
        Assert.Equal("switch.turn_off", aktion["action"]!.ToString());
        Assert.Equal("switch.t_port_schalter", aktion["target"]!["entity_id"]!.ToString());
    }

    // ----------------------------------------------------------- neue Rollen

    [Fact]
    public void DieZuluftRollenDesEntfeuchtersSindOptional()
    {
        foreach (var schluessel in new[] { "zuluft_bedarf", "zuluft_stufe_ist" })
        {
            var r = SteuerungGeraeteRollen.Finden("entfeuchter", schluessel);
            Assert.NotNull(r);
            Assert.False(r!.Pflicht);
        }
    }
}
