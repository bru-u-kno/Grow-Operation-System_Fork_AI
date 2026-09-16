using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// F-017: die Glocke auf der Werte-Karte misst gegen dieselben Grenzen wie die
/// Alarmauswertung — sonst meldet die Karte nachts VPD, das Telefon aber nicht.
/// </summary>
public sealed class ZielwerteGlockeTests
{
    private static TentAlertRule Luft() => new()
    {
        MetricKey = "temperature", MinValue = 22, MaxValue = 28,
        NightMinValue = 18, NightMaxValue = 24, Enabled = true, Quelle = Grenzwertquelle.Fest,
    };

    private static TentAlertRule Vpd() => new()
    {
        MetricKey = "vpd", MinValue = 1.0, MaxValue = 1.6, Enabled = true, Quelle = Grenzwertquelle.Fest,
    };

    [Fact]
    public void NachtsSchweigtVpd()
        => Assert.Equal((null, null), ZielwerteApiController.AlarmgrenzenJetzt(Vpd(), "vpd", LightsNow.Off));

    [Fact]
    public void TagsgiltVpd()
        => Assert.Equal((1.0, 1.6), ZielwerteApiController.AlarmgrenzenJetzt(Vpd(), "vpd", LightsNow.On));

    [Fact]
    public void NachtsGiltDasNachtbandDerLuft()
        => Assert.Equal((18, 24), ZielwerteApiController.AlarmgrenzenJetzt(Luft(), "temperature", LightsNow.Off));

    [Fact]
    public void TagsGiltDasTagbandDerLuft()
        => Assert.Equal((22, 28), ZielwerteApiController.AlarmgrenzenJetzt(Luft(), "temperature", LightsNow.On));

    [Fact]
    public void OhneRegelKeineGrenzen()
        => Assert.Equal((null, null), ZielwerteApiController.AlarmgrenzenJetzt(null, "temperature", LightsNow.On));
}
