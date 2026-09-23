using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge.Schema;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (F-041): die Kachel trennt Ziel (Plan) und Grenze (Alarmregel).
/// </summary>
/// <remarks>
/// Anlass 23.09.2026: Luft zeigte „Tag 21–27 / Nacht 17–23" — das waren die
/// Meldegrenzen, nicht das Planziel (24/20 °C). 26,8 °C stand als „im Ziel" da.
/// </remarks>
public sealed class KachelZieleTests
{
    private static FeedChartColumn Woche5() => new()
    {
        Id = "flower-w5",
        Label = "Blütewoche 5",
        AirTempC = 25,
        AirTempNightC = 21,
        RhMax = 55,
    };

    [Fact]
    public void LuftZielIstDerEinzelwertDesPlans_OhneErfundeneToleranz()
    {
        var karte = new MetricCard { Key = "temperature", NumericValue = 26.8 };

        KachelZiele.ZieleSetzen([karte], Woche5(), null, LightsNow.On);

        Assert.Equal(25, karte.TargetMin);
        Assert.Equal(25, karte.TargetMax);
        Assert.Equal(21, karte.TargetNightMin);
        Assert.Equal(21, karte.TargetNightMax);
        Assert.Equal("day", karte.TargetPhase);
        Assert.False(karte.TargetDerived);
    }

    [Fact]
    public void NachtsGiltDasNachtziel()
    {
        var karte = new MetricCard { Key = "temperature" };

        KachelZiele.ZieleSetzen([karte], Woche5(), null, LightsNow.Off);

        Assert.Equal(21, karte.TargetMin);
        Assert.Equal(21, karte.TargetMax);
        Assert.Equal("night", karte.TargetPhase);
    }

    [Fact]
    public void FeuchteIstHoechstensUndNachtsWieTags()
    {
        var karte = new MetricCard { Key = "humidity" };

        KachelZiele.ZieleSetzen([karte], Woche5(), null, LightsNow.On);

        Assert.Null(karte.TargetMin);
        Assert.Equal(55, karte.TargetMax);
        Assert.Null(karte.TargetNightMin);
        Assert.Equal(55, karte.TargetNightMax);
    }

    [Fact]
    public void FesteRegelLandetAlsGrenze_NichtAlsZiel()
    {
        var karte = new MetricCard { Key = "temperature", TargetMin = 25, TargetMax = 25 };
        var regel = new TentAlertRule
        {
            MetricKey = "temperature", Enabled = true, Quelle = Grenzwertquelle.Fest,
            MinValue = 22, MaxValue = 28, NightMinValue = 18, NightMaxValue = 24,
        };

        KachelZiele.GrenzenSetzen([karte], [regel], null, null, LightsNow.On);

        Assert.Equal(25, karte.TargetMin);
        Assert.Equal(25, karte.TargetMax);
        Assert.Equal(22, karte.AlarmMin);
        Assert.Equal(28, karte.AlarmMax);
        Assert.Equal(18, karte.AlarmNightMin);
        Assert.Equal(24, karte.AlarmNightMax);
    }

    [Fact]
    public void AbgeschalteteRegelGibtKeineGrenze()
    {
        var karte = new MetricCard { Key = "temperature" };
        var regel = new TentAlertRule { MetricKey = "temperature", Enabled = false, MinValue = 22, MaxValue = 28 };

        KachelZiele.GrenzenSetzen([karte], [regel], null, null, LightsNow.On);

        Assert.Null(karte.AlarmMin);
        Assert.Null(karte.AlarmMax);
    }

    [Fact]
    public void OhnePlanspalteBleibtDieKachelUnberuehrt()
    {
        var karte = new MetricCard { Key = "temperature", TargetMin = 20, TargetMax = 26 };

        KachelZiele.ZieleSetzen([karte], null, null, LightsNow.On);

        Assert.Equal(20, karte.TargetMin);
        Assert.Equal(26, karte.TargetMax);
    }
}
