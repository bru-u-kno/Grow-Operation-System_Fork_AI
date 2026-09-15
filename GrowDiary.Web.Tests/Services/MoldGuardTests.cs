using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Der Schimmeldeckel auf der Feuchte-Empfehlung.
/// </summary>
/// <remarks>
/// Die VPD-Rückrechnung kennt nur Physik. Ohne Deckel empfahl sie in der Blüte
/// bei warmer Luft über 60 % Luftfeuchte — ab da wird Grauschimmel in dichten
/// Blüten wahrscheinlich, und man merkt ihn erst beim Trimmen.
/// </remarks>
public sealed class MoldGuardTests
{
    private const double LeafOffset = -2.0;

    [Fact]
    public void TheCeilingFallsAsFlowersDensify()
    {
        // Junge Pflanzen ohne Blueten vertragen viel Feuchte; dichte Blueten
        // halten Wasser fest. Die Grenze muss also fallen, nie steigen.
        Assert.True(MoldGuard.MaxHumidityPercent(GrowStage.Seedling) > MoldGuard.MaxHumidityPercent(GrowStage.Veg));
        Assert.True(MoldGuard.MaxHumidityPercent(GrowStage.Veg) > MoldGuard.MaxHumidityPercent(GrowStage.Flower));
        Assert.True(MoldGuard.MaxHumidityPercent(GrowStage.Flower) > MoldGuard.MaxHumidityPercent(GrowStage.Finish));
        Assert.Equal(60, MoldGuard.MaxHumidityPercent(GrowStage.Flower));
    }

    [Fact]
    public void InWarmFlowerAir_TheRecommendationCollapsesInsteadOfAdvisingMold()
    {
        // 32 °C, Bluete-VPD 1,0–1,2: rein physikalisch waeren ~64–68 % RLF
        // noetig. Mit Deckel 60 bleibt kein Band uebrig — und genau das ist die
        // richtige Antwort: Temperatur senken, nicht Feuchte hochziehen.
        var ohneDeckel = ClimateBandCalculator.HumidityBand(32, 1.0, 1.2, LeafOffset);
        var mitDeckel = ClimateBandCalculator.HumidityBand(32, 1.0, 1.2, LeafOffset, MoldGuard.MaxHumidityPercent(GrowStage.Flower));

        Assert.NotNull(ohneDeckel.Min);
        Assert.True(ohneDeckel.Min > 60);
        Assert.Null(mitDeckel.Min);
    }

    [Fact]
    public void InCoolFlowerAir_TheBandSurvivesUntouched()
    {
        // 22 °C, Bluete-VPD: das Band liegt von selbst unter 60 % — der Deckel
        // darf dann nichts veraendern.
        var ohne = ClimateBandCalculator.HumidityBand(22, 1.0, 1.2, LeafOffset);
        var mit = ClimateBandCalculator.HumidityBand(22, 1.0, 1.2, LeafOffset, 60);

        Assert.Equal(ohne, mit);
        Assert.True(mit.Max <= 60);
    }

    [Fact]
    public void TheCapOnlyTrimsTheTop()
    {
        // 28 °C: das physikalische Band (57–63 %) reicht ueber 60 hinaus, beginnt aber
        // darunter — dann wird nur oben beschnitten, nicht verworfen.
        var mit = ClimateBandCalculator.HumidityBand(28, 1.0, 1.2, LeafOffset, 60);

        Assert.NotNull(mit.Min);
        Assert.Equal(60, mit.Max);
    }

    [Fact]
    public void SeedlingsKeepTheirHighHumidity()
    {
        // Der Fall aus dem echten Zelt: 28 °C, Saemlings-VPD 0,4–0,5 — dort ist
        // hohe Feuchte richtig, der 80er-Deckel laesst das Band durch.
        var band = ClimateBandCalculator.HumidityBand(28.1, 0.4, 0.5, LeafOffset, MoldGuard.MaxHumidityPercent(GrowStage.Seedling));

        Assert.NotNull(band.Min);
        Assert.InRange(band.Max!.Value, 70, 80);
    }
}
