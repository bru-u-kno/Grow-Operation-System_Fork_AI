using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Ein Wasserwechsel zwischen zwei Messungen ist keine Abweichung.
/// </summary>
/// <remarks>
/// Der Anlass (11.10.2026): Vollwechsel um 11:07, Messung davor EC 1,30 / pH 5,80,
/// Messung danach EC 0,61 / pH 6,12. Die Diagnose meldete „Reservoir-EC ist um
/// −0,69 mS/cm gefallen" und „pH ist in 6 h um 0,32 Punkte gestiegen" — der Wechsel
/// war als Vorgang erfasst, die Analyse kannte ihn nicht. Das Risiko stand bis zur
/// übernächsten Messung.
///
/// Jeder Fall hat sein Gegenstück ohne Wechsel: ohne die Kontrolle bewiese ein
/// grüner Test nur, dass die Meldung nie kommt.
/// </remarks>
public sealed class DeviationAnalyzerWasserwechselTests : IDisposable
{
    private readonly string _contentRoot;
    private readonly Tent _tent;
    private readonly AlertRuleRepository _rules;

    public DeviationAnalyzerWasserwechselTests()
    {
        _contentRoot = Path.Combine(Path.GetTempPath(), $"grow-wwtrend-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_contentRoot);
        var paths = new AppPaths(_contentRoot);
        _tent = TestDatabase.InitializeWithDefaultTent(paths);
        _rules = new AlertRuleRepository(paths);
    }

    public void Dispose()
    {
        try { Directory.Delete(_contentRoot, recursive: true); } catch { }
    }

    private DeviationAnalyzerService Analyzer() => new(TestKnowledgeBase.TargetValues(), _rules);

    private GrowRun Grow() => new()
    {
        Id = 1,
        Name = "Test",
        TentId = _tent.Id,
        MediumType = MediumType.Hydro,
        IrrigationType = IrrigationType.ActiveHydro,
        HydroStyle = HydroStyle.RDWC,
    };

    private static readonly DateTime Vorher = DateTime.Now.AddHours(-7);
    private static readonly DateTime Nachher = DateTime.Now.AddHours(-1);

    private static Measurement Messung(int id, DateTime wann, double ph, double ec, bool wechsel = false) => new()
    {
        Id = id,
        GrowId = 1,
        Stage = GrowStage.Veg,
        TakenAt = wann,
        ReservoirPh = ph,
        ReservoirEc = ec,
        ReservoirWaterTempC = 20,
        AirTemperatureC = 24,
        HumidityPercent = 60,
        SolutionChange = wechsel,
    };

    private static IReadOnlyList<Measurement> Paar(bool wechselAnNachher = false) => new[]
    {
        Messung(1, Vorher, ph: 5.80, ec: 1.30),
        Messung(2, Nachher, ph: 6.12, ec: 0.61, wechsel: wechselAnNachher),
    };

    private static ChangeoutEntry Wechsel(DateTime utc) => new()
    {
        GrowId = 1,
        Kind = ChangeoutKind.Full,
        PerformedAtUtc = utc,
    };

    private static bool EcSprung(IEnumerable<GrowDeviation> d)
        => d.Any(x => x.StableKey == "hydro.ec" && x.Message.Contains("gefallen"));

    private static bool PhDrift(IEnumerable<GrowDeviation> d)
        => d.Any(x => x.StableKey == "hydro.ph-drift");

    [Fact]
    public void OhneWechsel_MeldetDieAnalyseSprungUndDrift()
    {
        // Die Kontrolle: ohne sie wäre jeder Test unten auch ohne den Fix grün.
        var abweichungen = Analyzer().Analyze(Grow(), Paar());

        Assert.True(EcSprung(abweichungen), "EC-Sprung ohne Wechsel muss gemeldet werden");
        Assert.True(PhDrift(abweichungen), "pH-Drift ohne Wechsel muss gemeldet werden");
    }

    [Fact]
    public void WechselAusDemFormularZwischenDenMessungen_UnterdrueckenSprungUndDrift()
    {
        var wechsel = new[] { Wechsel(DateTime.UtcNow.AddHours(-4)) };

        var abweichungen = Analyzer().Analyze(Grow(), Paar(), wechsel: wechsel);

        Assert.False(EcSprung(abweichungen));
        Assert.False(PhDrift(abweichungen));
    }

    [Fact]
    public void WechselAlsHaekchenAnDerMessungNachher_UnterdrueckenSprungUndDrift()
    {
        var abweichungen = Analyzer().Analyze(Grow(), Paar(wechselAnNachher: true));

        Assert.False(EcSprung(abweichungen));
        Assert.False(PhDrift(abweichungen));
    }

    [Fact]
    public void WechselVorDerAelterenMessung_TrenntDasPaarNicht()
    {
        var wechsel = new[] { Wechsel(DateTime.UtcNow.AddHours(-9)) };

        var abweichungen = Analyzer().Analyze(Grow(), Paar(), wechsel: wechsel);

        Assert.True(EcSprung(abweichungen));
        Assert.True(PhDrift(abweichungen));
    }

    [Fact]
    public void WechselNachDerJuengerenMessung_TrenntDasPaarNicht()
    {
        var wechsel = new[] { Wechsel(DateTime.UtcNow.AddMinutes(-10)) };

        var abweichungen = Analyzer().Analyze(Grow(), Paar(), wechsel: wechsel);

        Assert.True(EcSprung(abweichungen));
    }

    [Fact]
    public void NachDemWechsel_VergleichtDieNaechsteMessungMitDerErstenDanach()
    {
        // Wechsel zwischen 1 und 2; Messung 3 kommt sechs Stunden später und
        // springt wieder um 0,4 — das ist eine echte Abweichung und bleibt eine.
        var messungen = new[]
        {
            Messung(1, DateTime.Now.AddHours(-14), 5.80, 1.30),
            Messung(2, DateTime.Now.AddHours(-8), 5.90, 0.61),
            Messung(3, DateTime.Now.AddHours(-2), 5.90, 1.05),
        };
        var wechsel = new[] { Wechsel(DateTime.UtcNow.AddHours(-11)) };

        var abweichungen = Analyzer().Analyze(Grow(), messungen, wechsel: wechsel);

        Assert.Contains(abweichungen, x => x.StableKey == "hydro.ec" && x.Message.Contains("gestiegen"));
    }

    [Fact]
    public void LiegtDazwischen_FensterIstOffenUntenUndGeschlossenOben()
    {
        var von = new DateTime(2026, 10, 11, 8, 0, 0, DateTimeKind.Local);
        var bis = von.AddHours(6);
        ChangeoutEntry[] Mit(DateTime w) => new[] { new ChangeoutEntry { PerformedAtUtc = w.ToUniversalTime() } };

        Assert.False(Wasserwechsel.LiegtDazwischen(von, bis, null, Mit(von)));
        Assert.True(Wasserwechsel.LiegtDazwischen(von, bis, null, Mit(von.AddMinutes(1))));
        Assert.True(Wasserwechsel.LiegtDazwischen(von, bis, null, Mit(bis)));
        Assert.False(Wasserwechsel.LiegtDazwischen(von, bis, null, Mit(bis.AddMinutes(1))));
        Assert.False(Wasserwechsel.LiegtDazwischen(von, bis, null, null));
    }
}
