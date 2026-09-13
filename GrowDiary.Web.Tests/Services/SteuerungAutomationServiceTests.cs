using System.Text.Json.Nodes;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (forkai.72): Was aus einer Automations-Vorlage wird, bevor sie nach
/// Home Assistant geht.
/// </summary>
/// <remarks>
/// Das Schreiben selbst braucht ein laufendes Home Assistant. Prüfbar ist der
/// Teil davor — und der entscheidet, ob am Ende eine Automation entsteht, die
/// ein Gasventil richtig bedient.
/// </remarks>
public class SteuerungAutomationServiceTests
{
    private static readonly Dictionary<string, string> MitAllem = new(StringComparer.Ordinal)
    {
        ["co2_sensor"] = "sensor.co2",
        ["canopy"] = "sensor.blatt",
        ["rh"] = "sensor.feuchte",
        ["licht"] = "binary_sensor.licht",
        ["port_zustand"] = "binary_sensor.port",
        ["port_schalter"] = "select.port",
        ["abluft_stufe"] = "number.abluft",
    };

    private static Dictionary<string, string> OhneAbluft()
    {
        var d = new Dictionary<string, string>(MitAllem, StringComparer.Ordinal);
        d.Remove("abluft_stufe");
        return d;
    }

    private static JsonObject Vorlage(string json) => (JsonObject)JsonNode.Parse(json)!;

    [Fact]
    public void PlatzhalterWerdenErsetzt()
    {
        var fertig = SteuerungAutomationService.Fuellen(
            Vorlage("""{"alias":"X","triggers":[{"entity_id":"[[licht]]"}]}"""), MitAllem);

        Assert.NotNull(fertig);
        Assert.DoesNotContain("[[", fertig!.ToJsonString());
        Assert.Contains("binary_sensor.licht", fertig.ToJsonString());
    }

    [Fact]
    public void OhneGebrauchteRolleEntstehtNichts()
    {
        // Null statt einer Automation mit stehendem Platzhalter: die wuerde HA
        // annehmen und dann auf eine Entitaet zeigen, die es nicht gibt.
        var fertig = SteuerungAutomationService.Fuellen(
            Vorlage("""{"alias":"X","triggers":[{"entity_id":"[[port_schalter]]"}]}"""),
            new Dictionary<string, string>(StringComparer.Ordinal));

        Assert.Null(fertig);
    }

    [Fact]
    public void EinBlockMitFreierRolleFaelltWeg()
    {
        var fertig = SteuerungAutomationService.Fuellen(Vorlage("""
            {"alias":"X","conditions":[
              {"condition":"state","entity_id":"input_boolean.co2_abluft_drosseln","state":"off"},
              {"condition":"template","value_template":"[[abluft_stufe]]","wenn":"abluft_stufe"}]}
            """), OhneAbluft());

        Assert.NotNull(fertig);
        var bedingungen = fertig!["conditions"]!.AsArray();
        Assert.Single(bedingungen);

        // Nur der Block mit der freien Rolle faellt weg. Der Schalter
        // co2_abluft_drosseln bleibt - er ist ein eigener Helfer, kein Geraet,
        // und ohne Luefter steht er einfach auf aus. Eine Pruefung auf das Wort
        // 'abluft' waere deshalb zu scharf gewesen.
        Assert.DoesNotContain("[[", fertig.ToJsonString());
        Assert.DoesNotContain("number.abluft", fertig.ToJsonString());
        Assert.Contains("input_boolean.co2_abluft_drosseln", fertig.ToJsonString());
    }

    [Fact]
    public void DerselbeBlockBleibtWennDieRolleBelegtIst()
    {
        var fertig = SteuerungAutomationService.Fuellen(Vorlage("""
            {"alias":"X","conditions":[
              {"condition":"state","entity_id":"input_boolean.co2_abluft_drosseln","state":"off"},
              {"condition":"template","value_template":"[[abluft_stufe]]","wenn":"abluft_stufe"}]}
            """), MitAllem);

        Assert.NotNull(fertig);
        Assert.Equal(2, fertig!["conditions"]!.AsArray().Count);
        Assert.Contains("number.abluft", fertig.ToJsonString());
    }

    [Fact]
    public void DerSchluesselWennStehtNichtInDerFertigenAutomation()
    {
        // Home Assistant kennt ihn nicht und wuerde die Automation ablehnen.
        var fertig = SteuerungAutomationService.Fuellen(Vorlage("""
            {"alias":"X","conditions":[{"condition":"state","entity_id":"[[abluft_stufe]]","wenn":"abluft_stufe"}]}
            """), MitAllem);

        Assert.NotNull(fertig);
        Assert.DoesNotContain("\"wenn\"", fertig!.ToJsonString());
    }

    [Fact]
    public void JedeMitgelieferteVorlageLaesstSichFuellen()
    {
        // Ueber alle Module, nicht nur co2: eine neue Steuerung soll dieselbe
        // Pruefung mitbekommen, ohne dass jemand daran denken muss.
        var wurzel = Path.Combine(AppContext.BaseDirectory, "Vorlagen");
        Assert.True(Directory.Exists(wurzel), $"Vorlagen fehlen: {wurzel}");

        var dateien = Directory.GetFiles(wurzel, "*.json", SearchOption.AllDirectories);
        Assert.NotEmpty(dateien);

        foreach (var datei in dateien)
        {
            var modul = Path.GetFileName(Path.GetDirectoryName(datei))!;
            var zuordnung = SteuerungGeraeteRollen.FuerModul(modul)
                .ToDictionary(r => r.Schluessel, r => $"sensor.probe_{r.Schluessel}", StringComparer.Ordinal);

            var vorlage = (JsonObject)JsonNode.Parse(File.ReadAllText(datei))!;
            var fertig = SteuerungAutomationService.Fuellen(vorlage, zuordnung);

            Assert.NotNull(fertig);
            Assert.DoesNotContain("[[", fertig!.ToJsonString());
            Assert.False(string.IsNullOrWhiteSpace(fertig["alias"]?.GetValue<string>()), datei);
        }
    }

    [Fact]
    public void JedeVorlageTraegtDieHerkunftsmarke()
    {
        // Ohne sie haelt der Fork seine eigene Automation spaeter fuer
        // handgebaut und fasst sie nie wieder an.
        var wurzel = Path.Combine(AppContext.BaseDirectory, "Vorlagen");
        foreach (var datei in Directory.GetFiles(wurzel, "*.json", SearchOption.AllDirectories))
        {
            var beschreibung = ((JsonObject)JsonNode.Parse(File.ReadAllText(datei))!)["description"]
                ?.GetValue<string>() ?? string.Empty;

            Assert.Contains(SteuerungAutomationService.HerkunftsMarke, beschreibung, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DieDosierungLaeuftAuchOhneAbluftRegler()
    {
        // Wer keinen Luefter hat, soll trotzdem dosieren. Ohne das Aussieben
        // waere die ganze Automation an einem Geraet gescheitert, das sie zum
        // Dosieren gar nicht braucht.
        var datei = Path.Combine(AppContext.BaseDirectory, "Vorlagen", "co2", "dosierung.json");
        var fertig = SteuerungAutomationService.Fuellen(
            (JsonObject)JsonNode.Parse(File.ReadAllText(datei))!, OhneAbluft());

        Assert.NotNull(fertig);
        Assert.DoesNotContain("[[", fertig!.ToJsonString());
    }
}
