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
    public void JedeRolleNenntIhreErlaubtenDomains()
    {
        // Ohne Domain ließe sich nichts zuordnen: die Oberfläche filtert darauf, und die Prüfung beim Speichern auch.
        foreach (var rolle in SteuerungGeraeteRollen.Alle)
        {
            Assert.NotEmpty(rolle.Domains);
            Assert.All(rolle.Domains, d => Assert.Matches("^[a-z_]+$", d));
        }
    }

    [Fact]
    public void EsGibtKeineWerksvorgabe()
        => Assert.All(SteuerungGeraeteRollen.Alle, rolle => Assert.Equal(string.Empty, rolle.Vorgabe));

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
