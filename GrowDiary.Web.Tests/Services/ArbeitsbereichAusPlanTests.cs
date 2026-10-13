using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (A-021, forkai.212): „Arbeitsbereich aus dem Plan" — EIN = Plan-Obergrenze der Luftfeuchte minus Vorlauf,
/// AUS = EIN minus Abstand. Brus Zelt (12.10.2026): Obergrenze 54 %, Vorlauf 4 %, Abstand 4 % ergibt 50/46 — genau seine
/// festen Werte, deshalb ändert sich bei ihm nichts, wenn er umstellt.
/// </summary>
public sealed class ArbeitsbereichAusPlanTests
{
    private static EntfeuchterEinstellungen Plan(double vorlauf = 4, double abstand = 4) => new()
    {
        ArbeitsbereichQuelle = ArbeitsbereichQuellen.Plan,
        VorlaufProzent = vorlauf,
        HystereseProzent = abstand,
    };

    [Fact]
    public void HalbeZehntelRundenAufWieInDerSeite()
    {
        Assert.Equal((50.3d, 46.3d), EntfeuchterSteuerungService.ArbeitsbereichAusPlan(Plan(), 54.25));
    }

    [Fact]
    public void BrusZeltErgibtDieHeutigenWerte()
    {
        var b = EntfeuchterSteuerungService.ArbeitsbereichAusPlan(Plan(), 54);
        Assert.Equal((50d, 46d), b);
    }

    [Fact]
    public void DerAbstandVerschiebtNurDasAusUndDerVorlaufNurDasEin()
    {
        Assert.Equal((50d, 44d), EntfeuchterSteuerungService.ArbeitsbereichAusPlan(Plan(abstand: 6), 54));
        Assert.Equal((52d, 48d), EntfeuchterSteuerungService.ArbeitsbereichAusPlan(Plan(vorlauf: 2), 54));
    }

    [Fact]
    public void EinWandertMitDemWochenwechsel()
    {
        // Eine andere Woche, eine andere Plan-Obergrenze: dieselben Einstellungen, neue Schwellen.
        Assert.Equal((46d, 42d), EntfeuchterSteuerungService.ArbeitsbereichAusPlan(Plan(), 50));
        Assert.Equal((56d, 52d), EntfeuchterSteuerungService.ArbeitsbereichAusPlan(Plan(), 60));
    }

    [Fact]
    public void FestOderOhnePlanWertGiltNichts_dieGespeichertenWerteBleiben()
    {
        Assert.Null(EntfeuchterSteuerungService.ArbeitsbereichAusPlan(new EntfeuchterEinstellungen(), 54));
        Assert.Null(EntfeuchterSteuerungService.ArbeitsbereichAusPlan(Plan(), null));
        Assert.Null(EntfeuchterSteuerungService.ArbeitsbereichAusPlan(Plan(), double.NaN));
    }

    [Fact]
    public void EinBleibtInDerSpanneDesBackendsUndAusUeberDerUntergrenze()
    {
        // Obergrenze 40, Vorlauf 10: 30 wäre zu klein (AUS 26 < 30) — EIN steigt auf 31 + Abstand.
        var (ein, aus) = EntfeuchterSteuerungService.ArbeitsbereichAusPlan(Plan(vorlauf: 10), 40)!.Value;
        Assert.Equal(35, ein);
        Assert.True(aus >= 30 && aus < ein);
        // Nach oben nie über 90 %.
        Assert.Equal(90, EntfeuchterSteuerungService.ArbeitsbereichAusPlan(Plan(vorlauf: 0), 95)!.Value.Ein);
    }

    [Fact]
    public void RundetAufEineNachkommastelle()
    {
        var (ein, aus) = EntfeuchterSteuerungService.ArbeitsbereichAusPlan(Plan(), 54.34)!.Value;
        Assert.Equal(50.3, ein);
        Assert.Equal(46.3, aus);
    }

    [Fact]
    public void JedeBerechneteSchwellePasstZurPruefungDesBackends()
    {
        // Über alle Obergrenzen, Vorläufe und Abstände: was der Rechner liefert, lehnt die Prüfung nicht ab.
        var n = 0;
        for (var rh = 35d; rh <= 95; rh += 2.5)
        {
            foreach (var vorlauf in new[] { 0d, 1, 4, 10, 20 })
            {
                foreach (var abstand in new[] { 1d, 2, 4, 6, 10 })
                {
                    var e = Plan(vorlauf, abstand);
                    var (ein, aus) = EntfeuchterSteuerungService.ArbeitsbereichAusPlan(e, rh)!.Value;
                    e.FeuchteEinTag = e.FeuchteEinNacht = ein;
                    e.FeuchteAusTag = e.FeuchteAusNacht = aus;
                    var fehler = EntfeuchterSteuerungService.Pruefen(e);
                    Assert.True(fehler.Count == 0, $"rh {rh}, Vorlauf {vorlauf}, Abstand {abstand}: {string.Join("; ", fehler.Values)}");
                    n++;
                }
            }
        }
        Assert.True(n >= 300, "Mengenwächter: die Schleife muss wirklich laufen.");
    }

    [Theory]
    [InlineData("wochenweise")]
    [InlineData("")]
    public void EineUnbekannteQuelleLehntDiePruefungAb(string quelle)
    {
        var e = new EntfeuchterEinstellungen { ArbeitsbereichQuelle = quelle };
        Assert.Contains(nameof(e.ArbeitsbereichQuelle), EntfeuchterSteuerungService.Pruefen(e).Keys);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(20.1)]
    public void EinVorlaufAusserhalbDerSpanneWirdAbgelehnt(double vorlauf)
    {
        var e = new EntfeuchterEinstellungen { VorlaufProzent = vorlauf };
        Assert.Contains(nameof(e.VorlaufProzent), EntfeuchterSteuerungService.Pruefen(e).Keys);
    }

    [Fact]
    public void DieVoreinstellungIstFestMitVorlaufVier_wasBeiBruNichtsAendert()
    {
        var e = new EntfeuchterEinstellungen();
        Assert.Equal(ArbeitsbereichQuellen.Fest, e.ArbeitsbereichQuelle);
        Assert.Equal(4, e.VorlaufProzent);
        Assert.Empty(EntfeuchterSteuerungService.Pruefen(e));
    }
}
