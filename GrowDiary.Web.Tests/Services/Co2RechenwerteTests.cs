using System.Text.Json.Nodes;
using GrowDiary.Web.Services;
using Stand = GrowDiary.Web.Services.Co2Rechenwerte.Stand;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Die Formeln der CO₂-Rechenwerte erkennen — geprüft an den Formeln, die am
/// 30.09.2026 in Home Assistant standen — und die Antwort auf den
/// Einstellungsdialog so bauen, dass nichts verloren geht.
/// </summary>
public sealed class Co2RechenwerteTests
{
    private const string Bedarf = "binary_sensor.co2_bedarf";
    private const string Impuls = "sensor.co2_impuls_bedarf";

    private static readonly Dictionary<string, string> Rollen = new() { ["co2_sensor"] = "sensor.big_co2_light_sensor_co2" };

    private static JsonObject Echt()
        => (JsonObject)JsonNode.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Services", "Daten", "co2-handgebaut", "rechenwerte.json")))!;

    private static string EchteFormel(string entity) => Echt()[entity]!["optionen"]!["state"]!.ToString();

    [Fact]
    public void Selbsttest_BeideRechenwerteHabenEineAeltereFassung_UndEineAktuelle()
    {
        Assert.Equal([Bedarf, Impuls], Co2Rechenwerte.Betroffene);
        foreach (var e in Co2Rechenwerte.Betroffene)
        {
            Assert.NotNull(Co2Rechenwerte.Aktuelle(e, Rollen));
            Assert.Contains(Co2Rechenwerte.Aeltere, f => f.EntityId == e);
        }
    }

    [Theory]
    [InlineData(Bedarf, "bleibt bis zu 5 Minuten „an“, bei 0 ppm unbegrenzt", "sofort „aus“")]
    [InlineData(Impuls, "rechnet mit 0 ppm und liefert die Höchstdauer", "0 s")]
    public void DieEchtenFormeln_SindDieBekannteAlteFassung(string entity, string heute, string danach)
    {
        var (stand, alt) = Co2Rechenwerte.Beurteilen(entity, EchteFormel(entity), Rollen);
        Assert.Equal(Stand.Veraltet, stand);
        Assert.Equal(heute, alt!.Heute);
        Assert.Equal(danach, alt.Danach);
    }

    [Theory]
    [InlineData(Bedarf)]
    [InlineData(Impuls)]
    public void DieAktuelleFormel_GiltAlsAktuell(string entity)
        => Assert.Equal(Stand.Aktuell, Co2Rechenwerte.Beurteilen(entity, Co2Rechenwerte.Aktuelle(entity, Rollen)!, Rollen).Stand);

    [Fact]
    public void Umbrueche_ImEditor_AendernDieErkennungNicht()
    {
        var umgebrochen = EchteFormel(Bedarf).Replace("%}{%", "%}\n  {%");
        Assert.Equal(Stand.Veraltet, Co2Rechenwerte.Beurteilen(Bedarf, umgebrochen, Rollen).Stand);
    }

    [Fact]
    public void EineVonHandAngepassteFormel_IstAngepasst_UndNichtVeraltet()
    {
        // Etwa eine andere Hysterese als Rückfallwert — wer das ändert, hat einen Grund.
        var angepasst = EchteFormel(Bedarf).Replace("float(100)", "float(80)");
        Assert.Equal(Stand.Angepasst, Co2Rechenwerte.Beurteilen(Bedarf, angepasst, Rollen).Stand);
    }

    [Fact]
    public void EinAndererFuehler_IstNichtDieAlteFassungDiesesFuehlers()
    {
        var andere = new Dictionary<string, string> { ["co2_sensor"] = "sensor.anderer_fuehler" };
        Assert.Equal(Stand.AndererFuehler, Co2Rechenwerte.Beurteilen(Bedarf, EchteFormel(Bedarf), andere).Stand);
        Assert.Equal("sensor.big_co2_light_sensor_co2", Co2Rechenwerte.FuehlerInFormel(Bedarf, EchteFormel(Bedarf)));

        // Auch die aktuelle Fassung mit anderem Fühler, und beim Impuls-Bedarf.
        Assert.Equal(Stand.AndererFuehler,
            Co2Rechenwerte.Beurteilen(Impuls, Co2Rechenwerte.Aktuelle(Impuls, Rollen)!, andere).Stand);
    }

    [Fact]
    public void EineAngepassteFormel_NenntKeinenFuehler()
    {
        var angepasst = EchteFormel(Bedarf).Replace("float(100)", "float(80)");
        Assert.Null(Co2Rechenwerte.FuehlerInFormel(Bedarf, angepasst));
        Assert.Equal(Stand.Angepasst, Co2Rechenwerte.Beurteilen(Bedarf, angepasst,
            new Dictionary<string, string> { ["co2_sensor"] = "sensor.anderer_fuehler" }).Stand);
    }

    [Fact]
    public void OhneZugeordnetenFuehler_WirdNichtsErfunden()
        => Assert.Equal(Stand.OhneFuehler, Co2Rechenwerte.Beurteilen(Bedarf, EchteFormel(Bedarf), new Dictionary<string, string>()).Stand);

    // ------------------------------------------------------------ Dialog

    /// <summary>
    /// Ein Formular für einen Template-Sensor, wie Home Assistant 2026.9 es
    /// vermutlich schickt: Felder und Abschnitt aus template/config_flow.py,
    /// die Vorbelegung aus data_entry_flow.py. Die Serialisierung selbst
    /// (probatio.to_field_list, Abschnitt als „expandable“) lag beim Bau nicht
    /// im Quelltext vor — deshalb vergleicht der Dienst nach dem Schreiben
    /// alle übrigen Werte und schreibt bei einer Abweichung zurück.
    /// </summary>
    private static JsonArray Formular() => (JsonArray)JsonNode.Parse("""
        [
          {"name":"state","required":true,"selector":{"template":{}},"description":{"suggested_value":"{{ 1 }}"}},
          {"name":"unit_of_measurement","required":false,"selector":{"select":{}},"description":{"suggested_value":"s"}},
          {"name":"device_class","required":false,"selector":{"select":{}}},
          {"name":"state_class","required":false,"selector":{"select":{}},"description":{"suggested_value":"measurement"}},
          {"name":"device_id","required":false,"selector":{"device":{}}},
          {"name":"additional_options","type":"expandable","required":false,"expanded":false,
           "schema":[{"name":"availability","required":false,"selector":{"template":{}},"description":{"suggested_value":"{{ has_value('sensor.x') }}"}}]}
        ]
        """)!;

    [Fact]
    public void Antwort_GibtJedenVorbelegtenWertZurueck_UndTauschtNurDieFormel()
    {
        var antwort = Co2Rechenwerte.Antwort(Formular(), "{{ 2 }}");

        Assert.Equal("{{ 2 }}", antwort["state"]!.ToString());
        Assert.Equal("s", antwort["unit_of_measurement"]!.ToString());
        Assert.Equal("measurement", antwort["state_class"]!.ToString());
        Assert.Equal("{{ has_value('sensor.x') }}", antwort["additional_options"]!["availability"]!.ToString());

        // Was nicht belegt war, bleibt weg — sonst setzte Fork AI Werte, die niemand gewählt hat.
        Assert.Null(antwort["device_class"]);
        Assert.Null(antwort["device_id"]);
        Assert.Equal(4, antwort.Count);
    }

    [Fact]
    public void FormelAusDialog_LiestDieVorbelegteFormel()
        => Assert.Equal("{{ 1 }}", Co2Rechenwerte.FormelAusDialog(Formular()));
}
