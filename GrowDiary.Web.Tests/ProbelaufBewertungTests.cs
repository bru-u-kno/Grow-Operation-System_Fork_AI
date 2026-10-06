using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests;

/// <summary>
/// Fork AI (A-010, 07.10.2026): Die reine Rechnung des Probelaufs — Grenzprüfung und Kennzahlen.
/// </summary>
public sealed class ProbelaufBewertungTests
{
    private static readonly DateTime T0 = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private static ProbelaufMesswerte Wert(int minute, double? feuchte = 55, double? temp = 25, double? vpd = 1.2)
        => new(T0.AddMinutes(minute), feuchte, temp, vpd);

    private static readonly ProbelaufGrenzen Grenzen = new(FeuchteMax: 60, TempMax: 27.5, VpdMin: 0.9, VpdMax: 1.4);

    // ------------------------------------------------------------- Grenzprüfung

    [Fact]
    public void Pruefen_AllesImBand_KeineVerletzung()
        => Assert.Null(ProbelaufBewertung.Pruefen(Grenzen, Wert(0), TimeSpan.Zero));

    [Fact]
    public void Pruefen_FeuchteUeberMax_MeldetFeuchte()
    {
        var v = ProbelaufBewertung.Pruefen(Grenzen, Wert(0, feuchte: 60.1), TimeSpan.Zero);

        Assert.NotNull(v);
        Assert.Equal("Feuchte", v!.Groesse);
    }

    [Fact]
    public void Pruefen_GenauAufDerGrenze_IstNochErlaubt()
        => Assert.Null(ProbelaufBewertung.Pruefen(Grenzen, Wert(0, feuchte: 60, temp: 27.5, vpd: 0.9), TimeSpan.Zero));

    [Fact]
    public void Pruefen_TempUeberMax_MeldetTemperatur()
        => Assert.Equal("Temperatur", ProbelaufBewertung.Pruefen(Grenzen, Wert(0, temp: 27.6), TimeSpan.Zero)!.Groesse);

    [Theory]
    [InlineData(0.89)]
    [InlineData(1.41)]
    public void Pruefen_VpdAusserhalbDesBandes_MeldetVpd(double vpd)
        => Assert.Equal("VPD", ProbelaufBewertung.Pruefen(Grenzen, Wert(0, vpd: vpd), TimeSpan.Zero)!.Groesse);

    [Fact]
    public void Pruefen_OhneGrenze_WirdNichtGeprueft()
    {
        var offen = new ProbelaufGrenzen(null, null, null, null);

        Assert.Null(ProbelaufBewertung.Pruefen(offen, Wert(0, feuchte: 99, temp: 40, vpd: 3), TimeSpan.Zero));
    }

    [Fact]
    public void Pruefen_FuehlerLosKurz_Toleriert_UeberEineMinute_Abbruch()
    {
        var ohne = Wert(0, feuchte: null);

        Assert.Null(ProbelaufBewertung.Pruefen(Grenzen, ohne, TimeSpan.FromSeconds(59)));
        var v = ProbelaufBewertung.Pruefen(Grenzen, ohne, TimeSpan.FromSeconds(61));
        Assert.Equal("Feuchte", v!.Groesse);
        Assert.Contains("Fühler", v.Grund);
    }

    [Fact]
    public void Pruefen_FuehlerLosOhneGrenzeFuerDenWert_IstEgal()
        => Assert.Null(ProbelaufBewertung.Pruefen(new ProbelaufGrenzen(null, 27.5, null, null), Wert(0, feuchte: null), TimeSpan.FromMinutes(10)));

    // ------------------------------------------------------------- Kennzahlen

    private static List<ProbelaufMesswerte> Reihe(int vonMinute, int bisMinute, Func<int, double> feuchte)
        => Enumerable.Range(vonMinute, bisMinute - vonMinute + 1)
            .Select(m => Wert(m, feuchte: feuchte(m))).ToList();

    [Fact]
    public void Kennzahlen_AenderungProMinute_UndSpitze()
    {
        var vorher = Reihe(-10, -1, _ => 50);
        var waehrend = Reihe(0, 20, m => 50 + 0.3 * m);           // 50 → 56
        var nachher = Reihe(21, 30, m => 56 - (m - 20) * 0.6);    // fällt zurück

        var feuchte = ProbelaufBewertung.Kennzahlen(vorher, waehrend, nachher).Single(k => k.Groesse == "Feuchte");

        Assert.Equal(50, feuchte.Start, 3);
        Assert.Equal(56, feuchte.Spitze, 3);
        Assert.Equal(56, feuchte.Ende, 3);
        Assert.Equal(0.3, feuchte.AenderungProMinute, 3);
    }

    [Fact]
    public void Kennzahlen_Erholung_MinutenBisZurueckAufDenVorlauf()
    {
        var vorher = Reihe(-10, -1, _ => 50);
        var waehrend = Reihe(0, 20, m => 50 + 0.3 * m);
        var nachher = Reihe(21, 40, m => Math.Max(50, 56 - (m - 20) * 1.0)); // 1 %/Min. → nach 6 Min. wieder 50

        var feuchte = ProbelaufBewertung.Kennzahlen(vorher, waehrend, nachher).Single(k => k.Groesse == "Feuchte");

        // Innerhalb von 5 % der Spanne (0,3) um 50: ab 50,3 → 56 − 5,7 → 5,7 Min. nach Ende, aufgerundet auf Messpunkt 26 (6 Min.)
        Assert.NotNull(feuchte.ErholungMinuten);
        Assert.InRange(feuchte.ErholungMinuten!.Value, 5, 7);
    }

    [Fact]
    public void Kennzahlen_Erholung_NullWennNichtErreicht()
    {
        var vorher = Reihe(-10, -1, _ => 50);
        var waehrend = Reihe(0, 20, m => 50 + 0.3 * m);
        var nachher = Reihe(21, 30, _ => 55.5);

        var feuchte = ProbelaufBewertung.Kennzahlen(vorher, waehrend, nachher).Single(k => k.Groesse == "Feuchte");

        Assert.Null(feuchte.ErholungMinuten);
    }

    [Fact]
    public void Kennzahlen_LeereWaehrendReihe_LiefertNichts()
        => Assert.Empty(ProbelaufBewertung.Kennzahlen(Reihe(-10, -1, _ => 50), [], []));

    [Fact]
    public void Kennzahlen_ReiheOhneWerteEinerGroesse_LaesstSieAus()
    {
        var waehrend = Enumerable.Range(0, 5).Select(m => Wert(m, feuchte: 55, temp: null, vpd: null)).ToList();

        var k = ProbelaufBewertung.Kennzahlen([], waehrend, []);

        Assert.Single(k);
        Assert.Equal("Feuchte", k[0].Groesse);
    }
}
