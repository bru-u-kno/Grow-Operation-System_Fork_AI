using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge.Schema;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI: prueft die Zuordnung Wochenspalte → Rollen. Reine Rechnung, keine
/// Datenbank und kein Home Assistant — genau deshalb steht sie hier und nicht
/// im Schreibweg.
/// </summary>
public class WochenplanZielwerteTests
{
    private static Dictionary<string, double> Werte(FeedChartColumn spalte)
        => WochenplanSyncService.Werte(spalte).ToDictionary(x => x.Rolle, x => x.Wert);

    [Fact]
    public void LufttemperaturWirdZumBandUmDenPlanwert()
    {
        var werte = Werte(new FeedChartColumn { AirTempC = 25 });

        Assert.Equal(25 - WochenplanSyncService.LufttemperaturSpanne, werte[WochenplanSyncService.Rollen.LuftUnten]);
        Assert.Equal(25 + WochenplanSyncService.LufttemperaturSpanne, werte[WochenplanSyncService.Rollen.LuftOben]);
    }

    [Fact]
    public void OhneLuftwertKeineLuftgrenzen()
    {
        var werte = Werte(new FeedChartColumn { RhMax = 60 });

        Assert.False(werte.ContainsKey(WochenplanSyncService.Rollen.LuftUnten));
        Assert.False(werte.ContainsKey(WochenplanSyncService.Rollen.LuftOben));
        Assert.Equal(60, werte[WochenplanSyncService.Rollen.FeuchteOben]);
    }

    [Fact]
    public void BeimCo2GiltDieUntergrenzeDerSpanne()
    {
        var werte = Werte(new FeedChartColumn { Co2Min = 1200, Co2Max = 1400 });

        Assert.Equal(1200, werte[WochenplanSyncService.Rollen.Co2Ziel]);
    }
}
