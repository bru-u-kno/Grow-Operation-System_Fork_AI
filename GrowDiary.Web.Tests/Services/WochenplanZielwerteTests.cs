using GrowDiary.Web.Models;
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

    // ------------------------------------------------ Fork AI (forkai.129)

    [Fact]
    public void DasVpdBandGehtAnDenEntfeuchter()
    {
        // SKX Blüte W5: 1,2–1,4 kPa → EIN unter 1,2, entfeuchten bis 1,4.
        var werte = Werte(new FeedChartColumn { VpdMin = 1.2, VpdMax = 1.4 });

        Assert.Equal(1.2, werte[WochenplanSyncService.Rollen.VpdUnten]);
        Assert.Equal(1.4, werte[WochenplanSyncService.Rollen.VpdOben]);
    }

    [Fact]
    public void OhneVpdImPlanBleibenDieEntfeuchterSchwellenUnberuehrt()
    {
        var werte = Werte(new FeedChartColumn { RhMax = 55 });

        Assert.False(werte.ContainsKey(WochenplanSyncService.Rollen.VpdUnten));
        Assert.False(werte.ContainsKey(WochenplanSyncService.Rollen.VpdOben));
    }

    [Fact]
    public void DerBlattOffsetKommtNichtAusDerWoche()
    {
        // Er hängt am Zelt, nicht an der Plan-Spalte — sonst würde er jede Woche
        // auf einen Wert gesetzt, den niemand dort gepflegt hat.
        var werte = Werte(new FeedChartColumn { VpdMin = 1.2, VpdMax = 1.4, AirTempC = 24 });

        Assert.False(werte.ContainsKey(WochenplanSyncService.Rollen.BlattOffset));
    }

    // ------------------------------------------------ Fork AI (forkai.130)

    private static Dictionary<string, double> Werte(FeedChartColumn spalte, GrowPlanInhalt inhalt, double abweichung)
        => WochenplanSyncService.Werte(spalte, inhalt, abweichung).ToDictionary(x => x.Rolle, x => x.Wert);

    [Fact]
    public void DieErlaubteAbweichungErsetztDieFestenDreiKelvin()
    {
        var w = Werte(new FeedChartColumn { Id = "w5", AirTempC = 24, AirTempNightC = 20 }, new GrowPlanInhalt(), 2);
        Assert.Equal(22, w[WochenplanSyncService.Rollen.LuftUnten]);
        Assert.Equal(26, w[WochenplanSyncService.Rollen.LuftOben]);
        Assert.Equal(18, w[WochenplanSyncService.Rollen.LuftNachtUnten]);
        Assert.Equal(22, w[WochenplanSyncService.Rollen.LuftNachtOben]);
    }

    [Fact]
    public void NachtsWieTagsGibtNachtsDasTagband()
    {
        var w = Werte(new FeedChartColumn { Id = "w5", AirTempC = 24, AirTempNightC = 20, RhMax = 55 },
            new GrowPlanInhalt { NachtWieTag = true }, 3);
        Assert.Equal(21, w[WochenplanSyncService.Rollen.LuftNachtUnten]);
        Assert.Equal(27, w[WochenplanSyncService.Rollen.LuftNachtOben]);
        Assert.Equal(55, w[WochenplanSyncService.Rollen.FeuchteNachtOben]);
    }

    [Fact]
    public void FeuchteNachtsNieUnbemerktLockerer()
    {
        // Ohne eigene Nachtfeuchte gilt nachts der Tageswert — nicht „keine Grenze".
        var ohne = Werte(new FeedChartColumn { Id = "w5", AirTempC = 24, RhMax = 55 }, new GrowPlanInhalt(), 3);
        Assert.Equal(55, ohne[WochenplanSyncService.Rollen.FeuchteNachtOben]);

        var mit = Werte(new FeedChartColumn { Id = "w5", AirTempC = 24, RhMax = 55, RhMaxNight = 60 }, new GrowPlanInhalt(), 3);
        Assert.Equal(60, mit[WochenplanSyncService.Rollen.FeuchteNachtOben]);
    }

    [Fact]
    public void OhnePlanInhaltBleibtAllesWieVorher()
    {
        // Ältere Wege ohne Grow-Plan: keine Nachtfeuchte-Grenze, ± 3 K, Nacht = Tag − 4 K.
        var werte = Werte(new FeedChartColumn { AirTempC = 25, RhMax = 60 });
        Assert.False(werte.ContainsKey(WochenplanSyncService.Rollen.FeuchteNachtOben));
        Assert.Equal(18, werte[WochenplanSyncService.Rollen.LuftNachtUnten]);
    }
}
