using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.GrowPlan;
using GrowDiary.Web.Services.Knowledge.Schema;

namespace GrowDiary.Web.Tests.Services.GrowPlan;

/// <summary>
/// Fork AI (forkai.130): Nachtwerte als Planwerte und der Schalter „Nachts gelten
/// die Tageswerte" — Standard für den Grow, je Woche abweichend (Bru, 21.09.2026).
/// </summary>
public sealed class NachtwerteTests
{
    private static FeedChartColumn Woche(string id = "flower-w5", double? luft = 24, double? rh = 55)
        => new() { Id = id, AirTempC = luft, RhMax = rh };

    [Fact]
    public void LuftNachtWirdEinmalMitTagMinusVierVorbefuellt()
    {
        var inhalt = new GrowPlanInhalt();
        var spalte = Woche();

        Assert.True(GrowPlanBauer.NachtLuftFuellen(inhalt, spalte));
        Assert.Equal(20, spalte.AirTempNightC);
        Assert.Equal(GrowPlanHerkunft.Standard, inhalt.HerkunftVon(spalte.Id, "airTempNightC"));

        // Ein zweiter Lauf fasst den (vielleicht inzwischen geänderten) Wert nicht mehr an.
        spalte.AirTempNightC = 18;
        Assert.False(GrowPlanBauer.NachtLuftFuellen(inhalt, spalte));
        Assert.Equal(18, spalte.AirTempNightC);
    }

    [Fact]
    public void OhneTagwertWirdNichtsErfunden()
    {
        var spalte = Woche(luft: null);
        Assert.False(GrowPlanBauer.NachtLuftFuellen(new GrowPlanInhalt(), spalte));
        Assert.Null(spalte.AirTempNightC);
    }

    [Fact]
    public void FeuchteNachtIstOptionalUndNieFehlend()
    {
        // Leer heißt „wie tags" — eine Meldung „fehlt — bitte eintragen" wäre falsch.
        var feld = Wochenwertfelder.Alle.Single(f => f.Name == "rhMaxNight");
        Assert.True(feld.Optional);
        Assert.False(Wochenwertfelder.Alle.Single(f => f.Name == "airTempNightC").Optional);
    }

    [Fact]
    public void StandardAusGeltenDieNachtfelder()
    {
        var inhalt = new GrowPlanInhalt { NachtWieTag = false };
        var spalte = Woche();
        spalte.AirTempNightC = 20;

        var (luft, rh) = inhalt.NachtWerte(spalte);
        Assert.Equal(20, luft);
        Assert.Equal(55, rh); // ohne eigene Nachtfeuchte: wie tags
    }

    [Fact]
    public void StandardAnGeltenNachtsDieTageswerte()
    {
        var inhalt = new GrowPlanInhalt { NachtWieTag = true };
        var spalte = Woche();
        spalte.AirTempNightC = 20;
        spalte.RhMaxNight = 60;

        Assert.Equal((24d, 55d), inhalt.NachtWerte(spalte) is var (l, r) ? (l!.Value, r!.Value) : default);
    }

    [Fact]
    public void EineWocheKannVomStandardAbweichen()
    {
        var inhalt = new GrowPlanInhalt { NachtWieTag = true };
        inhalt.NachtWieTagJeWoche["flower-w7"] = false;
        var w5 = Woche("flower-w5");
        var w7 = Woche("flower-w7", luft: 22, rh: 40);
        w7.AirTempNightC = 18;
        w7.RhMaxNight = 45;

        Assert.True(inhalt.NachtWieTagFuer("flower-w5"));
        Assert.False(inhalt.NachtWieTagFuer("FLOWER-W7"));
        Assert.Equal((24d, 55d), inhalt.NachtWerte(w5) is var (l5, r5) ? (l5!.Value, r5!.Value) : default);
        Assert.Equal((18d, 45d), inhalt.NachtWerte(w7) is var (l7, r7) ? (l7!.Value, r7!.Value) : default);
    }

    [Fact]
    public void OhneNachtfeldRechnetDieLuftErsatzweiseAbSolangeNichtNachgetragen()
        => Assert.Equal(24 - WochenplanSyncService.Nachtabsenkung, new GrowPlanInhalt().NachtWerte(Woche()).LuftC);
}
