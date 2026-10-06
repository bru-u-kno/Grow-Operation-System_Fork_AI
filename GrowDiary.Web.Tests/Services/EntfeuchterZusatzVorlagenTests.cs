using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (A-009): Die mitgelieferten Vorlagen des Zusatz-Entfeuchters — geprüft an der gefüllten
/// Automation, so wie sie nach Home Assistant ginge.
/// </summary>
/// <remarks>
/// <para><b>Was hier gilt:</b> ENTSCHEIDUNGEN A-009, Punkt 2 und 7. Der Kern ist ein Satz: <i>beide
/// Geräte dürfen nie gleichzeitig ausgehen.</i> Er steht in der Vorlage als Bedingung („Führung läuft")
/// an jedem Abschalten außer der Übertemperatur — und genau das prüft dieser Fall, statt auf die
/// Beschreibung der Vorlage zu vertrauen (eine Erwähnung ist keine Verwendung).</para>
/// <para>Alle Prüfungen gehen über die <i>gefüllte</i> Vorlage mit allen Rollen — auch den mitbenutzten
/// des Entfeuchters. Ohne sie bliebe ein Platzhalter stehen, <c>Fuellen</c> gäbe null zurück, und eine
/// Zählung, die so etwas überspringt, hätte nichts geprüft.</para>
/// </remarks>
public sealed class EntfeuchterZusatzVorlagenTests
{
    private const string Modul = EntfeuchterZusatzSteuerungService.Modul;

    private static JsonObject Laden(string vorlage)
        => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(SteuerungAutomationService.VorlagenWurzel, Modul, vorlage + ".json")))!;

    private static Dictionary<string, string> Rollen(params string[] ohne)
        => SteuerungGeraeteRollen.FuerModulMitMitbenutzten(Modul)
            .Where(r => !ohne.Contains(r.Schluessel))
            .ToDictionary(r => r.Schluessel, r => $"{r.Domains[0]}.t_{r.Schluessel}", StringComparer.Ordinal);

    private static JsonObject Gefuellt(string vorlage, params string[] ohne)
        => SteuerungAutomationService.Fuellen(Laden(vorlage), Rollen(ohne))
           ?? throw new InvalidOperationException($"{vorlage} liess sich nicht füllen.");

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

    /// <summary>Die Zweige („choose") der Regelung — Alias, Bedingungen, Aktionen.</summary>
    private static List<JsonObject> Zweige(JsonObject regelung)
        => ((JsonArray)((JsonObject)regelung["actions"]![0]!)["choose"]!).Cast<JsonObject>().ToList();

    private static bool Schaltet(JsonObject zweig, string aktion, string ziel)
        => ((JsonArray)zweig["sequence"]!).Cast<JsonObject>()
            .Any(a => a["action"]?.ToString() == aktion && a["target"]?["entity_id"]?.ToString() == ziel);

    private static bool Fordert(JsonObject zweig, string entitaet, string zustand)
        => Objekte(zweig["conditions"]).Any(c => c["condition"]?.ToString() == "state"
            && c["entity_id"]?.ToString() == entitaet
            && (c["state"] is JsonArray z ? z.Select(x => x!.ToString()).Contains(zustand) : c["state"]?.ToString() == zustand));

    private static string Wirksam(JsonObject automation)
    {
        var kopie = (JsonObject)automation.DeepClone();
        foreach (var o in Objekte(kopie).ToList())
        {
            o.Remove("description");
            o.Remove("alias");
        }
        return kopie.ToJsonString();
    }

    // ------------------------------------------------------------ Regelung

    [Fact]
    public void Regelung_BeideGeraeteGehenNieGleichzeitigAus()
    {
        var rollen = Rollen();
        var zweige = Zweige(Gefuellt("regelung"));
        var z = rollen["zusatz_schalter"];
        var fuehrung = rollen["fuehrung_zustand"];

        var aus = zweige.Where(zw => Schaltet(zw, "homeassistant.turn_off", z)).ToList();
        var an = zweige.Where(zw => Schaltet(zw, "homeassistant.turn_on", z)).ToList();
        // Mengenwächter: die Zählung sieht die Zweige wirklich.
        Assert.True(aus.Count >= 6, $"Nur {aus.Count} Abschalt-Zweige gefunden — die Zählung sieht die Regelung nicht.");
        Assert.True(an.Count >= 3, $"Nur {an.Count} Einschalt-Zweige gefunden.");

        // Genau EIN Zweig schaltet ohne Führung ab: die Übertemperatur.
        var ohneFuehrung = aus.Where(zw => !Fordert(zw, fuehrung, "On")).ToList();
        var uebertemperatur = Assert.Single(ohneFuehrung);
        Assert.StartsWith("AUS: ueber der Temperaturgrenze", uebertemperatur["alias"]!.ToString());
        // … und er schaltet nach der Grenze selbst, nicht nach der früheren Folge-Grenze.
        Assert.Contains("sensor.trotec_temp_max_aktiv", uebertemperatur["conditions"]!.ToJsonString());
        Assert.DoesNotContain("folge_aus", uebertemperatur["conditions"]!.ToJsonString());

        // Jeder andere Abschalt-Zweig verlangt „Führung läuft" — für beide Schreibweisen (select: On, Schalter: on).
        foreach (var zw in aus.Except(ohneFuehrung))
        {
            Assert.True(Fordert(zw, fuehrung, "On") && Fordert(zw, fuehrung, "on"), $"{zw["alias"]}: schaltet ab, ohne dass die Führung laufen muss");
        }
    }

    [Fact]
    public void Regelung_FolgeAus_GehtFruehererAlsDieGrenze_UndNurMitLaufenderFuehrung()
    {
        var rollen = Rollen();
        var folge = Zweige(Gefuellt("regelung"))
            .Single(zw => zw["alias"]!.ToString().StartsWith("AUS Folge", StringComparison.Ordinal));

        Assert.True(Fordert(folge, rollen["fuehrung_zustand"], "On"));
        Assert.True(Fordert(folge, rollen["zusatz_schalter"], "on"));
        Assert.Contains($"\"entity_id\":\"{rollen["zelt_temp"]}\"", folge["conditions"]!.ToJsonString());
        Assert.Contains("\"above\":\"sensor.trotec_zelt_folge_aus_temperatur\"", folge["conditions"]!.ToJsonString());
    }

    [Fact]
    public void Regelung_VpdUndFeuchteSchaltenErstNachDerMindestlaufzeit()
    {
        var rollen = Rollen();
        var zweige = Zweige(Gefuellt("regelung"));
        var nachLaufzeit = zweige.Where(zw => Schaltet(zw, "homeassistant.turn_off", rollen["zusatz_schalter"])
                                              && !zw["alias"]!.ToString().StartsWith("AUS: ueber", StringComparison.Ordinal)
                                              && !zw["alias"]!.ToString().StartsWith("AUS Folge", StringComparison.Ordinal)).ToList();

        Assert.True(nachLaufzeit.Count >= 4);
        foreach (var zw in nachLaufzeit)
        {
            var laufzeit = Objekte(zw["conditions"]).Where(c => c["condition"]?.ToString() == "state"
                && c["entity_id"]?.ToString() == rollen["zusatz_schalter"] && c["state"]?.ToString() == "on" && c["for"] is not null).ToList();
            var bedingung = Assert.Single(laufzeit);
            Assert.Contains("trotec_zelt_mindestlaufzeit", bedingung["for"]!.ToJsonString());
        }
    }

    [Fact]
    public void Regelung_Einschalten_VerlangtMindestpause_Kuehle_UndDieZuschaltRegel()
    {
        var rollen = Rollen();
        var einschalten = Zweige(Gefuellt("regelung")).Where(zw => Schaltet(zw, "homeassistant.turn_on", rollen["zusatz_schalter"])).ToList();
        Assert.True(einschalten.Count >= 3);

        foreach (var zw in einschalten)
        {
            var text = zw["conditions"]!.ToJsonString();
            // Mindestpause: aus seit N Minuten.
            var pause = Assert.Single(Objekte(zw["conditions"]), c => c["condition"]?.ToString() == "state"
                && c["entity_id"]?.ToString() == rollen["zusatz_schalter"] && c["state"]?.ToString() == "off");
            Assert.Contains("trotec_zelt_mindestpause", pause["for"]!.ToJsonString());
            // Erst wieder an, wenn es kühl genug ist.
            Assert.Contains("\"below\":\"sensor.trotec_zelt_wieder_ein_temperatur\"", text);
            // Führung ist aus (dann sofort) ODER läuft seit der Zuschalt-Verzögerung.
            var oder = Assert.Single(Objekte(zw["conditions"]), c => c["condition"]?.ToString() == "or"
                && c.ToJsonString().Contains("trotec_zelt_zuschalt_verzogerung", StringComparison.Ordinal));
            Assert.Contains("\"Off\"", oder.ToJsonString());
            Assert.Contains("\"On\"", oder.ToJsonString());
        }
    }

    [Fact]
    public void Regelung_TagUndNacht_FolgenDemLicht_UndDemPlan()
    {
        var rollen = Rollen();
        var text = Wirksam(Gefuellt("regelung"));

        // Tag: VPD-Band des Plans. Nacht: Plan-Feuchte, oder durchlaufen.
        Assert.Contains("sensor.trotec_zelt_vpd_ein_schwelle", text);
        Assert.Contains("sensor.trotec_zelt_vpd_aus_schwelle", text);
        Assert.Contains("sensor.trotec_feuchte_ein_aktiv", text);
        Assert.Contains("sensor.trotec_feuchte_aus_aktiv", text);
        Assert.Contains("input_boolean.trotec_zelt_nacht_durchlaufen", text);
        Assert.Contains("input_boolean.trotec_zelt_tagbetrieb_erlauben", text);
        Assert.Contains($"\"entity_id\":\"{rollen["licht_zustand"]}\"", text);
        Assert.Contains($"\"entity_id\":\"{rollen["zelt_vpd"]}\"", text);
        Assert.Contains($"\"entity_id\":\"{rollen["zelt_rh"]}\"", text);

        // Der Takt und der Lichtwechsel lösen die Prüfung aus.
        var ausloeser = Laden("regelung")["triggers"]!.AsArray();
        Assert.Contains(ausloeser, t => t!["trigger"]!.ToString() == "time_pattern");
        Assert.Contains(ausloeser, t => t!["trigger"]!.ToString() == "state" && t["entity_id"]!.ToString() == "[[licht_zustand]]");
    }

    [Fact]
    public void Regelung_NachtDurchlaufen_UebergehtDieFeuchteNurDann()
    {
        var rollen = Rollen();
        var zweige = Zweige(Gefuellt("regelung"));

        var ausNacht = zweige.Single(zw => zw["alias"]!.ToString().StartsWith("AUS Nacht", StringComparison.Ordinal));
        // Aus wegen Feuchte nur OHNE „durchlaufen".
        Assert.True(Fordert(ausNacht, "input_boolean.trotec_zelt_nacht_durchlaufen", "off"));

        var einNacht = zweige.Single(zw => zw["alias"]!.ToString().StartsWith("EIN Nacht", StringComparison.Ordinal));
        var oder = Assert.Single(Objekte(einNacht["conditions"]), c => c["condition"]?.ToString() == "or"
            && c.ToJsonString().Contains("nacht_durchlaufen", StringComparison.Ordinal));
        Assert.Contains($"\"entity_id\":\"{rollen["zelt_rh"]}\"", oder.ToJsonString());
    }

    [Fact]
    public void Regelung_OhneVpdFuehler_FaelltAufDiePlanFeuchteZurueck_MitFuehlerNurWennDerPlanKeinVpdNennt()
    {
        var mit = Zweige(Gefuellt("regelung"));
        var ohne = Zweige(Gefuellt("regelung", "zelt_vpd"));

        // Ohne VPD-Fühler entfallen die VPD-Zweige, die Feuchte-Zweige bleiben …
        Assert.DoesNotContain("t_zelt_vpd", string.Join("", ohne.Select(z => z.ToJsonString())));
        Assert.True(mit.Count > ohne.Count);
        var feuchteMit = mit.Single(z => z["alias"]!.ToString().StartsWith("EIN Tag: Feuchte", StringComparison.Ordinal));
        var feuchteOhne = ohne.Single(z => z["alias"]!.ToString().StartsWith("EIN Tag: Feuchte", StringComparison.Ordinal));

        // … und dort gilt sie IMMER (ohne Fühler gibt es keine VPD-Regelung); mit Fühler nur, wenn der Plan kein VPD-Ziel nennt.
        var oderMit = Assert.Single(Objekte(feuchteMit["conditions"]), c => c["condition"]?.ToString() == "or" && c["alias"]?.ToString()?.StartsWith("Plan nennt kein VPD-Ziel", StringComparison.Ordinal) == true);
        var oderOhne = Assert.Single(Objekte(feuchteOhne["conditions"]), c => c["condition"]?.ToString() == "or" && c["alias"]?.ToString()?.StartsWith("Plan nennt kein VPD-Ziel", StringComparison.Ordinal) == true);
        Assert.Single(oderMit["conditions"]!.AsArray());
        Assert.Equal(2, oderOhne["conditions"]!.AsArray().Count);
    }

    [Fact]
    public void Regelung_SchaltetAnJederDomaeneDesSchalters()
    {
        // Kein select.select_option an einen switch: die Vorlage benutzt homeassistant.turn_on/off.
        foreach (var domaene in new[] { "switch", "input_boolean" })
        {
            var rollen = Rollen();
            rollen["zusatz_schalter"] = $"{domaene}.shelly";
            var fertig = SteuerungAutomationService.Fuellen(Laden("regelung"), rollen)!;
            var befehle = Objekte(fertig).Where(o => o["target"]?["entity_id"]?.ToString() == $"{domaene}.shelly").ToList();
            Assert.True(befehle.Count >= 9, $"{domaene}: nur {befehle.Count} Befehle gefunden");
            Assert.All(befehle, b => Assert.StartsWith("homeassistant.turn_", b["action"]!.ToString()));
        }
    }

    // ------------------------------------------------------------- Meldung

    [Fact]
    public void Meldung_NurBeiAnUndSchwacherLeistung_NieBeiAus_UndWiederholtSichSolangeEsBesteht()
    {
        var rollen = Rollen();
        var meldung = Gefuellt("meldung");
        var z = rollen["zusatz_schalter"];
        var leistung = rollen["zusatz_leistung"];

        // Auslöser: Leistung unter der Grenze seit N Minuten.
        var ausloeser = meldung["triggers"]!.AsArray().Cast<JsonObject>().ToList();
        Assert.Contains(ausloeser, t => t["trigger"]!.ToString() == "numeric_state" && t["entity_id"]!.ToString() == leistung
            && t["below"]!.ToString() == "input_number.trotec_zelt_melde_grenze_w"
            && t["for"]!.ToJsonString().Contains("trotec_zelt_melde_dauer_min"));

        // Bedingungen: Meldung eingeschaltet, Shelly an, Leistung unter der Grenze.
        var bedingungen = meldung["conditions"]!.AsArray().Cast<JsonObject>().ToList();
        Assert.Contains(bedingungen, c => c["entity_id"]!.ToString() == "input_boolean.trotec_zelt_melden" && c["state"]!.ToString() == "on");
        Assert.Contains(bedingungen, c => c["entity_id"]!.ToString() == z && c["state"]!.ToString() == "on");
        Assert.Contains(bedingungen, c => c["entity_id"]!.ToString() == leistung && c["below"]!.ToString() == "input_number.trotec_zelt_melde_grenze_w");

        // Die Wiederholung läuft NUR solange dieselben Bedingungen gelten …
        var wiederholung = Objekte(meldung["actions"]).Single(o => o["repeat"] is not null)["repeat"]!.AsObject();
        Assert.Equal(bedingungen.Count, wiederholung["while"]!.AsArray().Count);
        Assert.Contains(z, wiederholung["while"]!.ToJsonString());
        // … im Abstand der eingestellten Stunden, und prüft vor jeder Wiederholung noch einmal.
        var schritte = wiederholung["sequence"]!.AsArray().Cast<JsonObject>().ToList();
        Assert.Contains("trotec_zelt_melde_wiederholung_h", schritte[0]["timeout"]!.ToJsonString());
        Assert.True(schritte[0]["continue_on_timeout"]!.GetValue<bool>());
        Assert.Equal("template", schritte[1]["condition"]!.ToString());
        Assert.Contains(z, schritte[1]["value_template"]!.ToString());

        // Ohne Messwert wird nichts erraten: ein fehlender Wert beendet die Meldung.
        Assert.Contains("float(999999)", schritte[0]["wait_template"]!.ToString());
    }

    [Fact]
    public void Meldung_GehtInDieMeldungslisteUndAufsHandy_UndEinFehlenderPushStopptNichts()
    {
        var aktionen = Objekte(Gefuellt("meldung")["actions"]).Where(o => o["action"] is not null).ToList();

        var liste = aktionen.Where(a => a["action"]!.ToString() == "persistent_notification.create").ToList();
        var push = aktionen.Where(a => a["action"]!.ToString() == "notify.notify").ToList();
        Assert.Equal(2, liste.Count); // sofort und bei jeder Wiederholung
        Assert.Equal(2, push.Count);
        Assert.All(push, p => Assert.True(p["continue_on_error"]!.GetValue<bool>()));
        // Dieselbe Nachricht-ID: die Wiederholung ersetzt den Eintrag, sie stapelt keine.
        Assert.Single(liste.Select(l => l["data"]!["notification_id"]!.ToString()).Distinct());
        Assert.Contains("zieht nichts", liste[0]["data"]!["title"]!.ToString());
    }

    // ------------------------------------------------------ Katalog und Vorlagen

    [Fact]
    public void JedeEntitaetDerVorlagen_DieDemZusatzGehoert_StehtImKatalog()
    {
        var katalog = SteuerungBauteile.FuerModul(Modul).Select(b => b.EntityId).ToHashSet();
        var gefunden = new HashSet<string>();
        foreach (var vorlage in new[] { "regelung", "meldung" })
        {
            var text = Wirksam(Gefuellt(vorlage));
            foreach (Match m in Regex.Matches(text, @"(?:input_number|input_boolean|sensor)\.trotec_zelt_[a-z0-9_]+"))
            {
                gefunden.Add(m.Value);
            }
        }

        Assert.True(gefunden.Count >= 12, $"Nur {gefunden.Count} Zusatz-Entitäten in den Vorlagen — die Suche greift nicht.");
        Assert.Empty(gefunden.Where(id => !katalog.Contains(id)));
    }

    [Fact]
    public void RechenwerteLesenDenPlanUndDenEntfeuchter_RichtigRum()
    {
        string Vorschrift(string id) => SteuerungBauteile.FuerModul(Modul).Single(b => b.EntityId == id).Vorlage!;

        // EIN unter Ziel MINUS Hysterese, AUS über Ziel PLUS Hysterese.
        Assert.Contains("vpd_ziel_unten') | float - states('input_number.trotec_zelt_vpd_hysterese", Vorschrift("sensor.trotec_zelt_vpd_ein_schwelle"));
        Assert.Contains("vpd_ziel_abschaltung') | float + states('input_number.trotec_zelt_vpd_hysterese", Vorschrift("sensor.trotec_zelt_vpd_aus_schwelle"));
        // Folge-AUS = aktive Höchsttemperatur − Folge-Abstand; Wieder-EIN = Folge-AUS − Wieder-ein-Abstand.
        Assert.Contains("trotec_temp_max_aktiv') | float - states('input_number.trotec_zelt_folge_abstand", Vorschrift("sensor.trotec_zelt_folge_aus_temperatur"));
        Assert.Contains("trotec_zelt_folge_aus_temperatur') | float - states('input_number.trotec_zelt_wieder_ein_abstand", Vorschrift("sensor.trotec_zelt_wieder_ein_temperatur"));
    }

    [Fact]
    public void Katalog_ZeigtDieHandgebauteRegelungAlsVorhanden_UndLegtNieEineZweiteDaneben()
    {
        var regelung = SteuerungBauteile.FuerModul(Modul).Single(b => b.VorlagenDatei == "regelung");
        Assert.Equal("automation.rdwc_trotec_zelt_shelly_plan_regelung", regelung.EntityId);

        var handgebaut = new HomeAssistantEntity { EntityId = regelung.EntityId, Domain = "automation", State = "on", KonfigKennung = "1791297367181" };

        // Eine handgebaute Automation unter der Katalog-Kennung → keine zweite daneben.
        Assert.Equal(regelung.EntityId, SteuerungAutomationService.HandgebauteDaneben(Modul, "regelung", [handgebaut]));
        Assert.Equal([regelung.EntityId], SteuerungBauteile.AutomationFinden(regelung, [handgebaut]));

        // Eine vom Fork angelegte (andere Entity-ID, Konfigurations-Kennung des Forks) ist keine fremde.
        var vomFork = new HomeAssistantEntity { EntityId = "automation.zusatz_entfeuchter_regelung", Domain = "automation", State = "on", KonfigKennung = regelung.KonfigKennung };
        Assert.Null(SteuerungAutomationService.HandgebauteDaneben(Modul, "regelung", [vomFork]));
        Assert.Equal(["automation.zusatz_entfeuchter_regelung"], SteuerungBauteile.AutomationFinden(regelung, [vomFork]));
        Assert.Equal("fork_ai_entfeuchter-zusatz_regelung", regelung.KonfigKennung);
    }

    [Fact]
    public void MeldungHaengtAmLeistungsmesser_UndFehlendeRollenMachenAusDerVorlageNichts()
    {
        var meldung = SteuerungBauteile.FuerModul(Modul).Single(b => b.VorlagenDatei == "meldung");
        Assert.False(meldung.Pflicht);
        Assert.Equal(["zusatz_leistung"], meldung.HaengtAn);

        // Ohne Leistungsmesser bleibt ein Platzhalter stehen → die Vorlage wird nicht angelegt (nicht halb).
        Assert.Null(SteuerungAutomationService.Fuellen(Laden("meldung"), Rollen("zusatz_leistung")));
        // Die Regelung braucht ihn nicht.
        Assert.NotNull(SteuerungAutomationService.Fuellen(Laden("regelung"), Rollen("zusatz_leistung", "zusatz_energie")));
    }

    [Fact]
    public void OhneLeistungsmesser_EntfaelltDieMeldungMitAllemWasDazuGehoert_DerRestBleibt()
    {
        var alle = SteuerungBauteile.Anwendbar(Modul, SteuerungGeraeteRollen.FuerModul(Modul).Select(r => r.Schluessel).ToList())
            .Select(b => b.EntityId).ToHashSet();
        var ohne = SteuerungBauteile.Anwendbar(Modul, ["zusatz_schalter", "fuehrung_zustand"]).Select(b => b.EntityId).ToHashSet();

        var weg = alle.Except(ohne).Order().ToList();
        Assert.Equal(new[]
        {
            "automation.zusatz_entfeuchter_zieht_nichts",
            "input_boolean.trotec_zelt_melden",
            "input_number.trotec_zelt_melde_dauer_min",
            "input_number.trotec_zelt_melde_grenze_w",
            "input_number.trotec_zelt_melde_wiederholung_h",
        }, weg);
        // Mengenwächter: die Regelung und ihre Helfer bleiben (6 Zahlen, 2 Schalter, 4 Rechenwerte, 1 Automation).
        Assert.Equal(13, ohne.Count);
        Assert.Contains("automation.rdwc_trotec_zelt_shelly_plan_regelung", ohne);
    }
}
