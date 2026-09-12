using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (forkai.21): Die Geräte-Zuordnung der Steuerungen — ohne Datenbank
/// prüfbar: die Rollen-Registry selbst und die Prüfung einer Entity-ID.
/// </summary>
/// <remarks>
/// <para><b>Warum die Registry geprüft wird.</b> Jede Rolle bringt ihre Vorgabe
/// mit — genau die Entität, die vorher als Konstante im Dienst stand. Ein
/// Tippfehler dort ist still: die Regelung liest dann eine Entität, die es nicht
/// gibt, und zeigt „–" statt einer Warnung. Der Test hält wenigstens Form und
/// Domain der Vorgabe fest.</para>
/// </remarks>
public sealed class SteuerungGeraeteTests
{
    [Fact]
    public void JedeRolleHatEineVorgabeDieZuIhrenDomainsPasst()
    {
        foreach (var rolle in SteuerungGeraeteRollen.Alle)
        {
            var domain = SteuerungGeraeteService.Domain(rolle.Vorgabe);
            Assert.True(domain is not null, $"Vorgabe von {rolle.Modul}/{rolle.Schluessel} ist keine Entity-ID: {rolle.Vorgabe}");
            Assert.True(rolle.Domains.Contains(domain!, StringComparer.OrdinalIgnoreCase),
                $"Vorgabe von {rolle.Modul}/{rolle.Schluessel} ist {domain}, erlaubt sind {string.Join("/", rolle.Domains)}.");
        }
    }

    [Fact]
    public void RollenschluesselSindJeModulEindeutig()
    {
        var doppelt = SteuerungGeraeteRollen.Alle
            .GroupBy(rolle => $"{rolle.Modul}/{rolle.Schluessel}", StringComparer.OrdinalIgnoreCase)
            .Where(gruppe => gruppe.Count() > 1)
            .Select(gruppe => gruppe.Key)
            .ToList();

        Assert.True(doppelt.Count == 0, $"Doppelte Rollen: {string.Join(", ", doppelt)}");
    }

    /// <summary>
    /// Die CO₂-Begasung hatte diese Entitäten bis forkai.20 als Konstanten im
    /// Dienst. Wandern sie beim Umzug in die Registry verloren, läuft die
    /// Regelung nach dem Update plötzlich auf anderen Geräten — deshalb stehen
    /// sie hier ausgeschrieben.
    /// </summary>
    [Fact]
    public void Co2BehaeltSeineBisherigenGeraeteAlsVorgabe()
    {
        var vorgaben = SteuerungGeraeteRollen.FuerModul("co2")
            .ToDictionary(rolle => rolle.Schluessel, rolle => rolle.Vorgabe, StringComparer.Ordinal);

        Assert.Equal("sensor.big_co2_light_sensor_co2", vorgaben["co2_sensor"]);
        Assert.Equal("sensor.big_probe_sensor_sonden_temperatur", vorgaben["canopy"]);
        Assert.Equal("sensor.big_probe_sensor_sonden_luftfeuchtigkeit", vorgaben["rh"]);
        Assert.Equal("sensor.big_probe_sensor_sonden_vpd", vorgaben["vpd"]);
        Assert.Equal("binary_sensor.big_port_5_zustand", vorgaben["port_zustand"]);
        Assert.Equal("number.rdwc_venti_einschaltleistung", vorgaben["abluft_stufe"]);
        Assert.Equal("binary_sensor.klein_abluft_zustand", vorgaben["licht"]);
    }

    [Theory]
    [InlineData("sensor.big_co2_light_sensor_co2", "sensor")]
    [InlineData("  number.rdwc_venti_einschaltleistung  ", "number")]
    [InlineData("sensor.", null)]
    [InlineData(".co2", null)]
    [InlineData("sensor", null)]
    [InlineData("sensor.a.b", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void DomainErkenntNurEchteEntityIds(string? eingabe, string? erwartet)
        => Assert.Equal(erwartet, SteuerungGeraeteService.Domain(eingabe));
}
