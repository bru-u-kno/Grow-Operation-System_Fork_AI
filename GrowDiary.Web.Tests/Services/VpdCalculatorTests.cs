using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

public sealed class VpdCalculatorTests
{
    [Fact]
    public void WithoutOffset_MatchesPlainAirVpd()
    {
        // 25 °C / 60 % RH -> saturation 3.169 kPa, deficit 40 % of it ≈ 1.27 kPa
        var vpd = VpdCalculator.Calculate(25, 60);

        Assert.NotNull(vpd);
        Assert.Equal(1.27, vpd!.Value, 2);
    }

    [Fact]
    public void LeafOffset_LowersVpd()
    {
        // A cooler leaf holds less moisture, so the deficit against it is smaller.
        var air = VpdCalculator.Calculate(25, 60, 0)!.Value;
        var leaf = VpdCalculator.Calculate(25, 60, -2)!.Value;

        // 25 °C air / 60 % RH with the leaf at 23 °C: 2.809 kPa (leaf) − 1.901 kPa (air) ≈ 0.91
        Assert.True(leaf < air, $"leaf VPD {leaf} should be below air VPD {air}");
        Assert.Equal(0.91, leaf, 2);
    }

    [Fact]
    public void LargerOffset_LowersVpdFurther()
    {
        var small = VpdCalculator.Calculate(26, 55, -1)!.Value;
        var large = VpdCalculator.Calculate(26, 55, -3)!.Value;

        Assert.True(large < small);
    }

    [Fact]
    public void SaturatedAirWithCoolLeaf_ClampsAtZero()
    {
        // 100 % RH and a cooler leaf would give a negative deficit — report 0, not below.
        var vpd = VpdCalculator.Calculate(24, 100, -3);

        Assert.Equal(0, vpd);
    }

    [Theory]
    [InlineData(null, 60.0)]
    [InlineData(25.0, null)]
    [InlineData(25.0, -1.0)]
    [InlineData(25.0, 101.0)]
    public void ImplausibleInput_YieldsNull(double? temperature, double? humidity)
        => Assert.Null(VpdCalculator.Calculate(temperature, humidity));

    [Fact]
    public void SaturationRisesWithTemperature()
        => Assert.True(VpdCalculator.SaturationKpa(30) > VpdCalculator.SaturationKpa(20));
}
