using System.Text.Json.Nodes;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Sicherheitsprüfung 01.10.2026: Rollen-Werte werden roh in JSON- und
/// Jinja-Text der erzeugten HA-Automationen eingesetzt. Geprüft wurde vorher
/// nur „genau ein Punkt" — ein Wert mit Anführungszeichen hängte der
/// Chiller-Automation eine beliebige Aktion an, die HA mit dem
/// Supervisor-Token anlegte.
/// </summary>
public class EntityIdEinschleusenTests
{
    private static readonly string Ordner = Path.Combine(AppContext.BaseDirectory, "Vorlagen", "chiller");

    // Der Wert aus dem Prüfbericht: ein Punkt, Rest JSON-Ausbruch.
    private const string Angriff =
        "climate.x\"}},{\"action\":\"homeassistant\\u002erestart\",\"target\":{\"entity_id\":\"x";

    private static Dictionary<string, string> Rollen(string sollwert) => new(StringComparer.Ordinal)
    {
        ["wasser_temp"] = "sensor.wasser",
        ["licht_zustand"] = "binary_sensor.licht",
        ["kuehler_sollwert"] = sollwert,
    };

    private static JsonObject Sollwert()
        => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(Ordner, "sollwert.json")))!;

    [Fact]
    public void EinGueltigerWertWirdEingesetzt()
    {
        var fertig = SteuerungAutomationService.Fuellen(Sollwert(), Rollen("climate.kuehler"));
        Assert.NotNull(fertig);
        Assert.Contains("climate.kuehler", fertig!.ToJsonString());
    }

    [Fact]
    public void EinAusbruchsversuchErgibtKeineAutomation()
    {
        var fertig = SteuerungAutomationService.Fuellen(Sollwert(), Rollen(Angriff));
        Assert.Null(fertig);
    }

    [Theory]
    [InlineData("sensor.zelt_co2", true)]
    [InlineData("binary_sensor.big_port_5_zustand", true)]
    [InlineData("climate.x\"}}", false)]
    [InlineData("sensor.a') }}{{ states('x", false)]
    [InlineData("sensor.a\\u002eb", false)]
    [InlineData("Sensor.Zelt", false)]
    [InlineData("sensor.", false)]
    [InlineData("sensor", false)]
    [InlineData("a.b.c", false)]
    public void NurEchteEntityIdsGeltenBeimSpeichern(string wert, bool gueltig)
        => Assert.Equal(gueltig, SteuerungGeraeteService.Domain(wert) is not null);
}
