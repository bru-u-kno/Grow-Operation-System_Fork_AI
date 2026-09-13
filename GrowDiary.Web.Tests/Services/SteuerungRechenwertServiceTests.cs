using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (forkai.69): Was im letzten Schritt des Einrichtungsdialogs steht.
/// </summary>
/// <remarks>
/// Den Dialog selbst kann ohne laufendes Home Assistant niemand fahren. Prüfbar
/// ist, was dabei am ehesten falsch wird: die Art des Schritts und die Felder.
/// Ein Rechenwert, der als <c>sensor</c> statt <c>binary_sensor</c> entsteht,
/// liefert „True" als Text — die Automation prüft auf <c>on</c> und dosiert nie.
/// </remarks>
public class SteuerungRechenwertServiceTests
{
    private static Bauteil Suche(string entityId)
        => SteuerungBauteile.Alle.Single(b => b.EntityId == entityId);

    [Fact]
    public void EinJaNeinWertGehtInDenBinarySensorSchritt()
    {
        Assert.Equal("binary_sensor", SteuerungRechenwertService.Schritt(BauteilArt.RechenSchalter));
        Assert.Equal("sensor", SteuerungRechenwertService.Schritt(BauteilArt.RechenSensor));
    }

    [Fact]
    public void AndereArtenHabenHierNichtsZuSuchen()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SteuerungRechenwertService.Schritt(BauteilArt.Zahl));
        Assert.Throws<ArgumentOutOfRangeException>(() => SteuerungRechenwertService.Schritt(BauteilArt.Automation));
    }

    [Fact]
    public void DerSchrittPasstZurDomaeneDerEntitaet()
    {
        // Sonst entsteht der Helfer in der falschen Domaene und traegt nie die
        // Id, die der Katalog erwartet.
        foreach (var b in SteuerungBauteile.Alle
                     .Where(x => x.Art is BauteilArt.RechenSensor or BauteilArt.RechenSchalter))
        {
            Assert.Equal(b.Domaene, SteuerungRechenwertService.Schritt(b.Art));
        }
    }

    [Fact]
    public void NameUndVorschriftGehenImmerMit()
    {
        var b = Suche("sensor.co2_ziel_effektiv");
        var felder = SteuerungRechenwertService.Felder(b, "{{ 42 }}");

        Assert.Equal(b.Name, felder["name"]);
        Assert.Equal("{{ 42 }}", felder["state"]);
    }

    [Fact]
    public void EinMesswertBringtEinheitUndZustandsklasseMit()
    {
        var felder = SteuerungRechenwertService.Felder(Suche("sensor.co2_impuls_bedarf"), "{{ 5 }}");

        Assert.Equal("s", felder["unit_of_measurement"]);
        Assert.Equal("measurement", felder["state_class"]);
    }

    [Fact]
    public void EinJaNeinWertBringtWederEinheitNochZustandsklasseMit()
    {
        // binary_sensor kennt beides nicht - der Dialog lehnt die Felder ab.
        var felder = SteuerungRechenwertService.Felder(Suche("binary_sensor.co2_bedarf"), "{{ true }}");

        Assert.False(felder.ContainsKey("unit_of_measurement"));
        Assert.False(felder.ContainsKey("state_class"));
        Assert.Equal(2, felder.Count);
    }

    [Fact]
    public void AusDemNamenFaelltDieErwarteteEntityId()
    {
        foreach (var b in SteuerungBauteile.Alle
                     .Where(x => x.Art is BauteilArt.RechenSensor or BauteilArt.RechenSchalter))
        {
            var name = Assert.IsType<string>(SteuerungRechenwertService.Felder(b, "{{ 1 }}")["name"]);
            var abgeleitet = new string(name.ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) ? c : '_')
                .ToArray());

            Assert.Equal(b.Objektkennung, abgeleitet);
        }
    }

    [Fact]
    public void DasZielStehtVorDenenDieEsLesen()
    {
        // Bedarf und Impulslaenge lesen sensor.co2_ziel_effektiv. Entstuende es
        // zuletzt, staenden die anderen kurz auf 'nicht verfuegbar'.
        var rechenwerte = SteuerungBauteile.FuerModul("co2")
            .Where(b => b.Art is BauteilArt.RechenSensor or BauteilArt.RechenSchalter)
            .Select(b => b.EntityId)
            .ToList();

        var ziel = rechenwerte.IndexOf("sensor.co2_ziel_effektiv");
        Assert.True(ziel >= 0);
        Assert.True(ziel < rechenwerte.IndexOf("binary_sensor.co2_bedarf"));
        Assert.True(ziel < rechenwerte.IndexOf("sensor.co2_impuls_bedarf"));
    }
}
