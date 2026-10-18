using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests;

/// <summary>
/// Fork AI (forkai.247): Jede Messgröße hat Etikett und Domains — und der Demobestand hält sich an die eigene Regel.
/// </summary>
/// <remarks>
/// Dass Etikett und Domains mit der Oberfläche übereinstimmen, prüft <c>src/sensor-messgroessen-gleich.test.ts</c>
/// (eine Wahrheit, zwei Sprachen). Hier die Seite des Backends; den Demobestand gegen die Regel hält
/// <c>DemobestandStimmigTests.Die_Zuordnungen_passen_zur_Domainregel</c>.
/// </remarks>
public sealed class SensorMessgroessenTests
{
    [Fact]
    public void Jede_Messgroesse_hat_einen_Eintrag_mit_Etikett_und_Domain()
    {
        var alle = Enum.GetValues<SensorMetricType>();
        Assert.True(alle.Length >= 20, $"Nur {alle.Length} Messgrößen — die Zählung sähe nichts.");

        var ohne = alle.Where(m => !SensorMessgroessen.Alle.ContainsKey(m)).ToList();
        Assert.True(ohne.Count == 0, "Ohne Eintrag in SensorMessgroessen (nicht über den Fork-MCP zuordenbar): " + string.Join(", ", ohne));
        Assert.All(SensorMessgroessen.Alle, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Value.Etikett), p.Key.ToString());
            Assert.NotEmpty(p.Value.Domains);
        });
    }

    [Fact]
    public void Kein_Etikett_kommt_doppelt_vor()
    {
        // Der Fork-MCP findet eine Messgröße über ihr Etikett; zwei gleiche wären nicht zu unterscheiden.
        var doppelt = SensorMessgroessen.Alle.GroupBy(p => p.Value.Etikett, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key);
        Assert.Empty(doppelt);
    }

    [Theory]
    [InlineData("OutsideTemperature", SensorMetricType.OutsideTemperature)]
    [InlineData("outsidetemperature", SensorMetricType.OutsideTemperature)]
    [InlineData(" ReservoirPh ", SensorMetricType.ReservoirPh)]
    [InlineData("co2", SensorMetricType.Co2)]
    [InlineData("3", null)]
    [InlineData("-1", null)]
    [InlineData("Humidity,Vpd", null)]
    [InlineData("Humidity, Vpd", null)]
    [InlineData("Aussenluft", null)]
    [InlineData("", null)]
    public void Lesen_nimmt_nur_Namen_keine_Zahlen(string text, SensorMetricType? erwartet)
        => Assert.Equal(erwartet, SensorMessgroessen.Lesen(text));
}
