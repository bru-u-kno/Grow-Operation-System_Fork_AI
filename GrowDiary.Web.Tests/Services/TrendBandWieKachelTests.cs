using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Der Trendwaechter misst „noch im erlaubten Bereich" am selben Band wie Kachel
/// und Diagnose.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (Fork AI, 02.10.2026).</b> Der Waechter hatte eigene
/// Baender: pH fest 5,8–6,2, Wassertemperatur [Nacht, Tag] des Profils. Bei
/// 20,5 °C, vier Tage langsam steigend, ging eine Push-Nachricht „ausserhalb"
/// raus — Kachel und Diagnose sagten fuer dieselbe Messung „im Bereich".</para>
/// </remarks>
public sealed class TrendBandWieKachelTests
{
    private static readonly DateTime Jetzt = new(2026, 7, 25, 20, 0, 0, DateTimeKind.Unspecified);

    private static readonly HydroTargetValues Bluete = new(
        PhMin: 5.8, PhMax: 6.2,
        EcMin: 1.0, EcMax: 1.2,
        OrpMin: 400, OrpMax: 450,
        WaterTempDayC: 20, WaterTempNightC: 18,
        VpdMin: 1.0, VpdMax: 1.2,
        PpfdMin: 800, PpfdMax: 1000,
        Co2Min: 1200, Co2Max: 1400);

    private static List<Measurement> Wasser(params double[] aeltesteZuerst) =>
        aeltesteZuerst.Select((wert, i) => new Measurement
        {
            TakenAt = Jetzt.Date.AddDays(-(aeltesteZuerst.Length - 1 - i)).AddHours(9),
            ReservoirWaterTempC = wert,
        }).ToList();

    private static List<Measurement> Ph(params double[] aeltesteZuerst) =>
        aeltesteZuerst.Select((wert, i) => new Measurement
        {
            TakenAt = Jetzt.Date.AddDays(-(aeltesteZuerst.Length - 1 - i)).AddHours(9),
            ReservoirPh = wert,
        }).ToList();

    /// <summary>Das Szenario aus der Durchsicht: 20,5 °C, langsam steigend.</summary>
    [Fact]
    public void Wasser20Komma5_LangsamSteigend_IstNochImBereich_WieAufDerKachel()
    {
        var messungen = Wasser(18.0, 18.8, 19.6, 20.5);

        var drift = Assert.Single(TrendWatchService.Evaluate(messungen, Bluete, Jetzt),
            b => b.Code == "trend.watertemp.drift");

        var (kachelMin, kachelMax) = Zielband.FuerMetrik("reservoir-temp", Bluete);
        Assert.True(20.5 >= kachelMin && 20.5 <= kachelMax,
            $"Vorbedingung: die Kachel nennt {kachelMin}–{kachelMax} °C, 20,5 muss darin liegen.");

        Assert.True(drift.Severity == TrendSeverity.Info,
            "Der Waechter meldet 20,5 °C als Warnung „ausserhalb\" und schickt eine Push-Nachricht, "
            + $"waehrend die Kachel fuer dieselbe Messung {kachelMin}–{kachelMax} °C nennt. "
            + "Text: " + drift.Detail);
        Assert.Contains("noch im erlaubten Bereich", drift.Detail);
    }

    /// <summary>Und die Gegenrichtung: ueber dem Arbeitsbereich bleibt es eine Warnung.</summary>
    [Fact]
    public void WasserUeberDemArbeitsbereich_BleibtEineWarnung()
    {
        var messungen = Wasser(20.0, 21.0, 22.0, 23.0);

        var drift = Assert.Single(TrendWatchService.Evaluate(messungen, Bluete, Jetzt),
            b => b.Code == "trend.watertemp.drift");

        Assert.Equal(TrendSeverity.Warning, drift.Severity);
    }

    /// <summary>
    /// Die Nachtabsenkung zieht die Untergrenze mit — wie auf der Kachel.
    /// </summary>
    [Fact]
    public void FallendBisZumRampenboden_IstKeineWarnung()
    {
        var messungen = Wasser(19.0, 18.0, 17.0, 16.2);

        var mitRampe = Assert.Single(TrendWatchService.Evaluate(messungen, Bluete, Jetzt, rampenBodenC: 16),
            b => b.Code == "trend.watertemp.drift");
        Assert.True(mitRampe.Severity == TrendSeverity.Info,
            "Die App faehrt selbst auf 16 °C herunter; der Waechter meldet ihre eigene Regelung "
            + "als Abweichung. " + mitRampe.Detail);

        // Ohne Rampe ist 16,2 unter dem Arbeitsbereich 17 — dann zu Recht eine Warnung.
        var ohneRampe = Assert.Single(TrendWatchService.Evaluate(messungen, Bluete, Jetzt),
            b => b.Code == "trend.watertemp.drift");
        Assert.Equal(TrendSeverity.Warning, ohneRampe.Severity);
    }

    /// <summary>
    /// Ein Profil mit tieferem pH: Handlungsbereich wie auf der Kachel, nicht 5,8 fest.
    /// </summary>
    [Fact]
    public void PhProfilMitTieferemBand_FolgtDemHandlungsbereich()
    {
        var tief = Bluete with { PhMin = 5.6, PhMax = 6.0 };
        var messungen = Ph(6.10, 5.95, 5.82, 5.70);

        var drift = Assert.Single(TrendWatchService.Evaluate(messungen, tief, Jetzt),
            b => b.Code == "trend.ph.drift");

        var (min, max) = Zielband.FuerMetrik("reservoir-ph", tief);
        Assert.True(drift.Severity == TrendSeverity.Info,
            $"Die Kachel nennt den Handlungsbereich {min}–{max}; 5,70 liegt darin. Der Waechter "
            + "warnt trotzdem — er misst an einer eigenen Zahl. " + drift.Detail);
    }

    /// <summary>
    /// Zaehlung: fuer jede Messgroesse, die der Waechter beobachtet, ist sein Band das der Kachel.
    /// </summary>
    [Theory]
    [InlineData("reservoir-ph")]
    [InlineData("reservoir-ec")]
    [InlineData("orp")]
    [InlineData("reservoir-temp")]
    public void JedesBand_IstDasDerKachel(string schluessel)
    {
        var profil = Bluete with { PhMin = 5.6, PhMax = 6.0, WaterTempNightC = 16 };

        foreach (double? rampe in new double?[] { null, 15 })
        {
            Assert.Equal(
                Zielband.FuerMetrik(schluessel, profil, rampe),
                TrendWatchService.BandFuer(schluessel, profil, rampe));
        }
    }

    /// <summary>Ohne Profil rechnet der Waechter wie die Diagnose: Komfortzone, SOP-Bereich.</summary>
    [Fact]
    public void OhneProfil_GeltenKomfortzoneUndSopBereich()
    {
        (double? Min, double? Max) komfort = (DeviationAnalyzerService.PhComfortMin, DeviationAnalyzerService.PhComfortMax);
        (double? Min, double? Max) sop = (Wasserband.ArbeitsbereichMinC, Wasserband.ArbeitsbereichMaxC);
        (double? Min, double? Max) keins = (null, null);
        Assert.Equal(komfort, TrendWatchService.BandFuer("reservoir-ph", null));
        Assert.Equal(sop, TrendWatchService.BandFuer("reservoir-temp", null));
        Assert.Equal(keins, TrendWatchService.BandFuer("reservoir-ec", null));
    }
}
