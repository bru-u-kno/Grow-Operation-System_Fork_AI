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

    /// <summary>
    /// Ohne eigene Nachttemperatur rechnet der Plan die Absenkung selbst.
    /// </summary>
    /// <remarks>
    /// Woche 4 des SKX-Plans nennt 25 °C. Nacht heisst damit 21 °C, Band
    /// 18–24 °C — dasselbe, was auf der Kachel steht.
    /// </remarks>
    [Fact]
    public void OhneNachtwertGiltDerTagwertMinusDerAbsenkung()
    {
        var werte = Werte(new FeedChartColumn { AirTempC = 25 });

        Assert.Equal(18, werte[WochenplanSyncService.Rollen.LuftNachtUnten]);
        Assert.Equal(24, werte[WochenplanSyncService.Rollen.LuftNachtOben]);
    }

    /// <summary>Nennt der Plan eine Nachttemperatur, gilt sie statt der Rechnung.</summary>
    [Fact]
    public void EigeneNachttemperaturSchlaegtDieAbsenkung()
    {
        var werte = Werte(new FeedChartColumn { AirTempC = 25, AirTempNightC = 19 });

        Assert.Equal(19 - WochenplanSyncService.LufttemperaturSpanne, werte[WochenplanSyncService.Rollen.LuftNachtUnten]);
        Assert.Equal(19 + WochenplanSyncService.LufttemperaturSpanne, werte[WochenplanSyncService.Rollen.LuftNachtOben]);
    }

    /// <summary>
    /// Die Feuchte wird nachts nicht gelockert.
    /// </summary>
    /// <remarks>
    /// Bewusst als Test und nicht nur als Kommentar: ein spaeter ergaenztes
    /// Nacht-Band fuer die Feuchte waere eine leisere Anzeige, kein besserer
    /// Grow — Kondensat entsteht im Dunkeln, nicht am Tag.
    /// </remarks>
    [Fact]
    public void DieFeuchteBekommtKeinNachtband()
    {
        var werte = Werte(new FeedChartColumn { AirTempC = 25, RhMax = 60 });

        Assert.Equal(60, werte[WochenplanSyncService.Rollen.FeuchteOben]);
        Assert.DoesNotContain(werte.Keys, k => k.Contains("feuchte") && k.Contains("nacht"));
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
