using System.Text.Json;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (forkai.225): Der Abschalt-Abstand der Zuluft und sein Nachziehen in vorhandene
/// Rechenwerte.
/// </summary>
/// <remarks>
/// <para>Die Formularprobe ist der echte Optionsdialog des Rechenwerts „Zuluft Bedarf" aus
/// einer Anlage, in der er von Hand gebaut und danach angepasst wurde (geglättete
/// Differenz). Gerade deshalb: das Nachziehen darf nur ein Textstück tauschen und
/// nichts von dem anfassen, was der Nutzer verändert hat.</para>
/// </remarks>
public sealed class ZuluftNachzugTests
{
    private const string BedarfAlt =
        "{% set d = states('sensor.zuluft_differenz_geglattet') | float %}{% set s = states('input_number.zuluft_mindest_differenz') | float %}"
        + "{% if d >= s %}on{% elif d < s - 0.5 or t < tm - 1 %}off{% else %}{{ 'on' if prev else 'off' }}{% endif %}";

    private static JsonElement Formular(string state)
    {
        object Feld(string name, object selector, object? wert = null) => wert is null
            ? new { selector, name, required = false }
            : new { selector, name, description = new { suggested_value = wert }, required = false };

        var form = new
        {
            type = "form",
            flow_id = "abc",
            step_id = "binary_sensor",
            data_schema = new object[]
            {
                Feld("state", new { template = new { } }, state),
                Feld("device_class", new { device_class = new { domain = "binary_sensor" } }, "running"),
                Feld("device_id", new { device = new { } }),
                new
                {
                    type = "expandable",
                    name = "additional_options",
                    expanded = false,
                    schema = new object[]
                    {
                        Feld("availability", new { template = new { } }, "{{ has_value('sensor.a') }}"),
                        Feld("delay_on", new { duration = new { } }),
                    },
                },
            },
        };
        return JsonSerializer.SerializeToElement(form);
    }

    private static SteuerungRechenwertService.Nachzug Bedarf
        => SteuerungRechenwertService.Nachzuege.Single(n => n.EntityId == "binary_sensor.zuluft_bedarf");

    [Fact]
    public void DasAlteTextstueckWirdGetauschtUndAllesAndereBleibt()
    {
        var felder = SteuerungRechenwertService.NachzugFelder(Formular(BedarfAlt), Bedarf, out var stand);

        Assert.Equal("angepasst", stand);
        Assert.NotNull(felder);
        var state = (string)felder!["state"]!;
        // Das Neue steht drin, das Alte nicht mehr ...
        Assert.Contains("input_number.zuluft_abschalt_abstand", state);
        Assert.DoesNotContain("s - 0.5", state);
        // ... und die Abweichung des Nutzers (geglättete Differenz) ist unangetastet.
        Assert.Contains("sensor.zuluft_differenz_geglattet", state);
        Assert.Contains("t < tm - 1", state);
        // Geräteklasse und Verfügbarkeit gehen mit zurück, sonst verlöre der Eintrag sie.
        Assert.Equal("running", ((JsonElement)felder["device_class"]!).GetString());
        var zusatz = (Dictionary<string, object?>)felder["additional_options"]!;
        Assert.Equal("{{ has_value('sensor.a') }}", ((JsonElement)zusatz["availability"]!).GetString());
        // Felder ohne eingestellten Wert werden nicht erfunden.
        Assert.False(felder.ContainsKey("device_id"));
        Assert.False(zusatz.ContainsKey("delay_on"));
    }

    [Fact]
    public void EinSchonNeuerRechenwertWirdNichtNochEinmalGetauscht()
    {
        var neu = BedarfAlt.Replace(Bedarf.Alt, Bedarf.Neu);
        Assert.Null(SteuerungRechenwertService.NachzugFelder(Formular(neu), Bedarf, out var stand));
        Assert.Equal("aktuell", stand);
    }

    [Fact]
    public void EineVorlageOhneDasAlteTextstueckBleibtUnberuehrt()
    {
        // Der Nutzer hat die Hysterese selbst anders geschrieben (0.8 statt 0.5).
        var eigen = BedarfAlt.Replace("s - 0.5", "s - 0.8");
        Assert.Null(SteuerungRechenwertService.NachzugFelder(Formular(eigen), Bedarf, out var stand));
        Assert.Equal("abweichend", stand);
    }

    [Fact]
    public void EinFormularOhneErwartetenAufbauWirdNichtGeraten()
    {
        var leer = JsonDocument.Parse("{\"type\":\"abort\"}").RootElement;
        Assert.Null(SteuerungRechenwertService.NachzugFelder(leer, Bedarf, out var stand));
        Assert.Equal("abweichend", stand);
    }

    [Fact]
    public void DerKatalogUndDasNachziehenSagenDasselbe()
    {
        // Läuft beides auseinander, bekäme eine neue Anlage eine andere Regel als eine
        // nachgezogene. Das Neue steht im Katalog, das Alte nicht mehr.
        var bauteil = SteuerungBauteile.FuerModul("zuluft").Single(b => b.EntityId == Bedarf.EntityId);
        Assert.Contains(Bedarf.Neu, bauteil.Vorlage);
        Assert.DoesNotContain(Bedarf.Alt, bauteil.Vorlage);
    }

    [Fact]
    public void DerHelferStehtImKatalogMitDenGrenzenDesPruefers()
    {
        var helfer = SteuerungBauteile.FuerModul("zuluft")
            .Single(b => b.EntityId == ZuluftSteuerungService.Entitaeten.AbschaltAbstand);
        Assert.Equal(BauteilArt.Zahl, helfer.Art);
        Assert.Equal(0, helfer.Min);
        Assert.Equal(ZuluftSteuerungService.AbschaltAbstandMax, helfer.Max);
        // Der Name muss die Kennung ergeben, sonst entsteht der Helfer bei jedem Lauf neu.
        Assert.Equal("zuluft_abschalt_abstand", helfer.Name.ToLowerInvariant().Replace(' ', '_'));
    }
}
