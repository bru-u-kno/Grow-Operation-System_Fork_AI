using System.Text.Json;
using System.Text.Json.Nodes;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (A-025): Der Kühlbedarf des Wasserkühlers merkt sich seinen Zustand in einem Helfer, wie der Zuluft-Bedarf
/// (forkai.234). Ohne Merker fiele er im Haltebereich der Hysterese nach einem "unavailable" (Fühleraussetzer,
/// Neuladen) auf "aus".
/// </summary>
public sealed class ChillerMerkerTests
{
    private static readonly string Bedarf = ChillerSteuerungService.Entitaeten.Kuehlbedarf;

    private static SteuerungRechenwertService.Nachzug Nachzug
        => SteuerungRechenwertService.Nachzuege.Single(n => n.EntityId == Bedarf);

    // Das Template, wie der Fork es vor dieser Änderung angelegt hat (Fühler neutral benannt).
    private const string KuehlbedarfAlt =
        "{% set t = states('sensor.wasser_temp') | float %}{% set z = states('sensor.chiller_zieltemperatur_aktiv') | float %}"
        + "{% set h = states('input_number.chiller_hysterese') | float(0.3) %}"
        + "{% set prev = (this.state == 'on') if this is defined else false %}"
        + "{% if t >= z + h %}on{% elif t <= z - h %}off{% else %}{{ 'on' if prev else 'off' }}{% endif %}";

    private static JsonElement Formular(string state)
    {
        var form = new
        {
            type = "form",
            flow_id = "abc",
            step_id = "binary_sensor",
            data_schema = new object[]
            {
                new { selector = new { template = new { } }, name = "state", description = new { suggested_value = state }, required = false },
                new { selector = new { device_class = new { domain = "binary_sensor" } }, name = "device_class", description = new { suggested_value = "heat" }, required = false },
            },
        };
        return JsonSerializer.SerializeToElement(form);
    }

    [Fact]
    public void DerNachzugTauschtNurDasTextstueckUndEinZweiterLaufFindetNichtsMehr()
    {
        var felder = SteuerungRechenwertService.NachzugFelder(Formular(KuehlbedarfAlt), Nachzug, out var stand);
        Assert.Equal("angepasst", stand);
        var neu = (string)felder!["state"]!;

        Assert.Contains("states('input_boolean.chiller_kuhlbedarf_merker')", neu);
        Assert.Contains("if m in ['on','off'] else ((this.state == 'on')", neu);
        Assert.Contains("sensor.wasser_temp", neu); // der Fühler des Nutzers bleibt
        Assert.Equal("heat", ((JsonElement)felder["device_class"]!).GetString());

        Assert.Null(SteuerungRechenwertService.NachzugFelder(Formular(neu), Nachzug, out var wieder));
        Assert.Equal("aktuell", wieder);
    }

    [Fact]
    public void EinHandgebautesTemplateMitAnderemWortlautBleibtUnberuehrt()
    {
        // So sieht es bei einem Nutzer aus, der das Template selbst geschrieben hat: kein Textstück zum Tauschen.
        var handgebaut = "{% if t >= z + h %}on{% elif t <= z - h %}off{% else %}{{ this.state if this is defined else 'off' }}{% endif %}";
        Assert.Null(SteuerungRechenwertService.NachzugFelder(Formular(handgebaut), Nachzug, out var stand));
        Assert.Equal("abweichend", stand);
    }

    [Fact]
    public void DerKatalogRechnetMitDemMerkerUndKenntHelferUndAutomation()
    {
        var bedarf = SteuerungBauteile.FuerModul("chiller").Single(b => b.EntityId == Bedarf);
        // Gleichlauf: der Katalog enthält genau das Textstück, das der Nachzug einsetzt, und nicht mehr das alte.
        Assert.Contains(Nachzug.Neu, bedarf.Vorlage);
        Assert.DoesNotContain(Nachzug.Alt, bedarf.Vorlage);
        Assert.Contains("t >= z + h", bedarf.Vorlage);
        Assert.Contains("t <= z - h %", bedarf.Vorlage);

        Assert.Equal(ChillerSteuerungService.Entitaeten.KuehlbedarfMerker, Nachzug.Merker);
        Assert.Equal(ChillerSteuerungService.Entitaeten.MerkerAbgleich, Nachzug.Abgleich);

        var helfer = SteuerungBauteile.FuerModul("chiller").Single(b => b.EntityId == Nachzug.Merker);
        Assert.Equal(BauteilArt.Schalter, helfer.Art);
        // Der Name muss die Kennung ergeben, sonst entsteht der Helfer bei jedem Lauf neu.
        Assert.Equal("chiller_kuhlbedarf_merker", helfer.Name.ToLowerInvariant().Replace(' ', '_'));

        var automation = SteuerungBauteile.FuerModul("chiller").Single(b => b.EntityId == Nachzug.Abgleich);
        Assert.Equal(BauteilArt.Automation, automation.Art);
        Assert.Equal("merker", automation.VorlagenDatei);
        Assert.Equal("chiller_merker_abgleich", automation.Name.ToLowerInvariant().Replace(' ', '_'));

        // Der Titel des Nachzugs ist der des Katalog-Eintrags (so findet er ihn in Home Assistant).
        Assert.Equal(bedarf.Name, Nachzug.Name);
    }

    [Fact]
    public void DerMerkerRechnetImHaltebereichUndNichtUmgekehrt()
    {
        Assert.Contains("{% set prev = (m == 'on') if m in ['on','off'] else ((this.state == 'on') if this is defined else false) %}", Nachzug.Neu);
        Assert.DoesNotContain("(m == 'off')", Nachzug.Neu);
    }

    [Fact]
    public void DieMerkerAutomationReagiertNieAufNichtVerfuegbarUndSchaltetPassend()
    {
        var datei = Path.Combine(SteuerungAutomationService.VorlagenWurzel, "chiller", "merker.json");
        var json = JsonNode.Parse(File.ReadAllText(datei))!.AsObject();

        var ausloeser = json["triggers"]!.AsArray().Select(t => t!.AsObject()).Where(t => (string?)t["trigger"] == "state").ToList();
        Assert.True(ausloeser.Count >= 1);
        foreach (var t in ausloeser)
        {
            Assert.Equal(Bedarf, (string?)t["entity_id"]);
            Assert.Equal(new[] { "off", "on" }, t["to"]!.AsArray().Select(z => (string?)z).OrderBy(z => z).ToArray());
        }

        var bedingung = (string)json["conditions"]![0]!["value_template"]!;
        Assert.Contains($"states('{Bedarf}') in ['on', 'off']", bedingung);
        Assert.Contains($"states('{ChillerSteuerungService.Entitaeten.KuehlbedarfMerker}') in ['on', 'off']", bedingung);
        Assert.Contains($"states('{Bedarf}') != states('{ChillerSteuerungService.Entitaeten.KuehlbedarfMerker}')", bedingung);

        var zweige = json["actions"]![0]!["choose"]!.AsArray();
        Assert.Equal(2, zweige.Count);
        foreach (var zweig in zweige)
        {
            Assert.Equal(Bedarf, (string)zweig!["conditions"]![0]!["entity_id"]!);
            var bedarf = (string)zweig["conditions"]![0]!["state"]!;
            var dienst = (string)zweig["sequence"]![0]!["action"]!;
            Assert.Equal($"input_boolean.turn_{bedarf}", dienst); // on -> turn_on, off -> turn_off, nie vertauscht
            Assert.Equal(ChillerSteuerungService.Entitaeten.KuehlbedarfMerker, (string)zweig["sequence"]![0]!["target"]!["entity_id"]!);
        }

        // Nichts am Kühler: weder Steckdose noch Sollwert noch Zeitstempel.
        var wirksam = json["actions"]!.ToJsonString();
        Assert.DoesNotContain("switch.", wirksam);
        Assert.DoesNotContain("[[", wirksam);
        Assert.DoesNotContain("input_datetime", wirksam);
        Assert.DoesNotContain("Zuluft", json.ToJsonString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DieRegelungSchaltetNurBeiAusdruecklichemZustandUndNieBeiNichtVerfuegbar()
    {
        // Verdichter-Schutz: Beide Zweige verlangen den Bedarf ausdrücklich ("on" bzw. "off") — kein "nicht an", das
        // "nicht verfügbar" einschlösse. Der Merker allein hielte den Kühler sonst nicht.
        var datei = Path.Combine(SteuerungAutomationService.VorlagenWurzel, "chiller", "regelung.json");
        var json = JsonNode.Parse(File.ReadAllText(datei))!.AsObject();
        var zweige = json["actions"]![0]!["choose"]!.AsArray();
        Assert.True(zweige.Count >= 2);
        var einschalten = zweige.Single(o => (string?)o!["alias"] == "Einschalten")!["conditions"]!.ToJsonString().Replace(" ", "");
        var ausschalten = zweige.Single(o => (string?)o!["alias"] == "Ausschalten")!["conditions"]!.ToJsonString().Replace(" ", "");
        Assert.Contains($"\"entity_id\":\"{Bedarf}\",\"state\":\"on\"", einschalten);
        Assert.Contains($"\"entity_id\":\"{Bedarf}\",\"state\":\"off\"", ausschalten);
        Assert.DoesNotContain("\"condition\":\"not\"", ausschalten);
        // Und die Mindestlaufzeit bleibt als Schutz im Ausschalten-Zweig.
        Assert.Contains("laufzeit_frei", ausschalten);
    }
}
