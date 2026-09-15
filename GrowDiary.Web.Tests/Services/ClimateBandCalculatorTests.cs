using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

public sealed class ClimateBandCalculatorTests
{
    private const double LeafOffset = -2.0;

    [Fact]
    public void HumidityBand_LandsInsideTheVpdTarget()
    {
        // Die Probe aufs Exempel: was das Band als Grenze nennt, muss vorwärts
        // gerechnet genau das VPD-Ziel treffen.
        var (min, max) = ClimateBandCalculator.HumidityBand(25.6, 0.9, 1.3, LeafOffset);

        Assert.NotNull(min);
        Assert.NotNull(max);
        Assert.Equal(1.3, VpdCalculator.Calculate(25.6, min!.Value, LeafOffset)!.Value, 2);
        Assert.Equal(0.9, VpdCalculator.Calculate(25.6, max!.Value, LeafOffset)!.Value, 2);
    }

    [Fact]
    public void HumidityBand_MoreVpdMeansLessHumidity()
    {
        // Die Grenzen kreuzen sich: das obere VPD ergibt die untere Feuchte.
        var (min, max) = ClimateBandCalculator.HumidityBand(25.0, 0.8, 1.2, LeafOffset);

        Assert.True(min < max);
    }

    [Fact]
    public void TemperatureBand_LandsInsideTheVpdTarget()
    {
        var (min, max) = ClimateBandCalculator.TemperatureBand(46, 0.9, 1.3, LeafOffset);

        Assert.NotNull(min);
        Assert.NotNull(max);
        Assert.Equal(0.9, VpdCalculator.Calculate(min!.Value, 46, LeafOffset)!.Value, 2);
        Assert.Equal(1.3, VpdCalculator.Calculate(max!.Value, 46, LeafOffset)!.Value, 2);
    }

    [Fact]
    public void TemperatureBand_RisesWithHumidity()
    {
        // Feuchtere Luft braucht mehr Wärme für dasselbe VPD.
        var trocken = ClimateBandCalculator.TemperatureBand(45, 0.9, 1.3, LeafOffset);
        var feucht = ClimateBandCalculator.TemperatureBand(65, 0.9, 1.3, LeafOffset);

        Assert.True(feucht.Min > trocken.Min);
    }

    [Fact]
    public void TheUsersOwnReading_IsJudgedTheSameWayByBothPaths()
    {
        // 25,6 °C bei 46 % ergibt VPD 1,41 — über dem Ziel 0,9–1,3. Also muss
        // auch die Temperatur über ihrem zurückgerechneten Band liegen, sonst
        // widersprächen sich zwei Kacheln nebeneinander.
        var vpd = VpdCalculator.Calculate(25.6, 46, LeafOffset)!.Value;
        Assert.True(vpd > 1.3);

        var (_, max) = ClimateBandCalculator.TemperatureBand(46, 0.9, 1.3, LeafOffset);
        Assert.NotNull(max);
        Assert.True(25.6 > max!.Value);
    }

    [Fact]
    public void ImplausibleHumidity_HasNoBand()
    {
        Assert.Null(ClimateBandCalculator.TemperatureBand(-5, 0.9, 1.3, LeafOffset).Min);
        Assert.Null(ClimateBandCalculator.TemperatureBand(120, 0.9, 1.3, LeafOffset).Min);
    }

    [Fact]
    public void VeryHighHumidity_YieldsNoTemperatureBandInsteadOfANonsenseOne()
    {
        // Bei 95 % ist das VPD-Ziel im ganzen Growraum-Bereich nicht erreichbar.
        // Dann lieber gar kein Band als eines an den Anschlag geklemmt.
        var (min, max) = ClimateBandCalculator.TemperatureBand(95, 0.9, 1.3, LeafOffset);

        Assert.Null(min);
        Assert.Null(max);
    }

    [Fact]
    public void SwappedTargets_AreReadInTheRightOrder()
    {
        var normal = ClimateBandCalculator.HumidityBand(25, 0.9, 1.3, LeafOffset);
        var vertauscht = ClimateBandCalculator.HumidityBand(25, 1.3, 0.9, LeafOffset);

        Assert.Equal(normal, vertauscht);
    }

    [Fact]
    public void WithoutLeafOffset_ItIsPlainAirVpd()
    {
        var (min, max) = ClimateBandCalculator.HumidityBand(24, 0.8, 1.0, leafOffsetC: 0);

        Assert.NotNull(min);
        Assert.Equal(1.0, VpdCalculator.Calculate(24, min!.Value, 0)!.Value, 2);
        Assert.Equal(0.8, VpdCalculator.Calculate(24, max!.Value, 0)!.Value, 2);
    }
}
