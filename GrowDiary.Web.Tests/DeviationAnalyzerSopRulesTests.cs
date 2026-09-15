using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge;
using GrowDiary.Web.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests;

/// <summary>
/// Rules taken straight from the SOPs, asserted with the SOP's own numbers.
///
/// These were missing or wrong until the documents were checked against the code rather
/// than only against the knowledge-base files: the pH drift rate did not exist at all, and
/// the dissolved-oxygen threshold sat below the level at which SOP-N1 already calls for
/// action.
/// </summary>
public sealed class DeviationAnalyzerSopRulesTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly DeviationAnalyzerService _service;

    public DeviationAnalyzerSopRulesTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "SopRules_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        CopyDefaults(Path.Combine(FindProjectRoot(), "GrowDiary.Web", "wwwroot", "knowledge-defaults"), _tempRoot);

        var loader = new KnowledgeBaseLoader(new AppPaths(_tempRoot), NullLogger<KnowledgeBaseLoader>.Instance);
        loader.Initialize();
        _service = new DeviationAnalyzerService(new TargetValueService(loader));
    }

    /// <summary>Ein Grow in der Blüte — die Phase steht am GROW, nicht an der Messung.</summary>
    /// <remarks>
    /// Seit dem 02.09.2026 liest die Diagnose die Phase aus
    /// <see cref="GrowStageResolver"/>. Wer sie hier auf die Messung schriebe,
    /// prüfte eine andere Phase als er meint.
    /// </remarks>
    private static GrowRun Grow(bool imFinish = false)
    {
        var grow = new GrowRun
        {
            Id = 1,
            Name = "Testlauf",
            MediumType = MediumType.Hydro,
            HydroStyle = HydroStyle.RDWC,
            SeedType = SeedType.Feminized,
            StartDate = DateTime.Today.AddDays(-70),
            FlipDate = DateTime.Today.AddDays(-40),
            FinishStartedAt = imFinish ? DateTime.Today.AddDays(-3) : null,
        };

        var gerechnet = GrowStageResolver.Resolve(grow, DateTime.Today);
        var gewollt = imFinish ? GrowStage.Finish : GrowStage.Flower;
        Assert.True(gerechnet == gewollt, $"Der Aufbau liefert {gerechnet}, gewollt war {gewollt}.");

        return grow;
    }

    private static Measurement At(DateTime takenAt, double? ph = null, double? doMgL = null, double? ec = null) => new()
    {
        Id = (int)(takenAt.Ticks % 100000),
        Stage = GrowStage.Flower,
        TakenAt = takenAt,
        ReservoirPh = ph,
        DissolvedOxygenMgL = doMgL,
        ReservoirEc = ec,
    };

    // --- SOP-RDWC-CAN-N1 §2.1: drift is defined by speed, not by position ---

    [Fact]
    public void PhJumpingHalfAPointOvernight_IsCritical_EvenThoughItNeverLeftTheBand()
    {
        // 5,8 -> 6,3 in 14 hours. Both readings sit inside the comfort band, so no absolute
        // check would say a word — but the SOP calls this acute and lists immediate steps.
        var now = DateTime.Now;
        var measurements = new List<Measurement>
        {
            At(now, ph: 6.30),
            At(now.AddHours(-14), ph: 5.80),
        };

        var drift = Assert.Single(_service.Analyze(Grow(), measurements), d => d.StableKey == "hydro.ph-drift");

        Assert.Equal(DeviationSeverity.Critical, drift.Severity);
        // Trennzeichen haengt an der Host-Kultur — nicht mitpruefen.
        Assert.Contains($"{0.50:0.00}", drift.Message);
        Assert.Contains("Wurzeln", drift.RecommendationHint ?? string.Empty);
    }

    [Fact]
    public void ADriftOfTwoTenths_IsOnlyAGentleNote()
    {
        var now = DateTime.Now;
        var measurements = new List<Measurement>
        {
            At(now, ph: 6.05),
            At(now.AddHours(-20), ph: 5.80),
        };

        var drift = Assert.Single(_service.Analyze(Grow(), measurements), d => d.StableKey == "hydro.ph-drift");

        Assert.Equal(DeviationSeverity.Info, drift.Severity);
        Assert.Contains("0,1-0,2", drift.RecommendationHint ?? string.Empty);
    }

    [Fact]
    public void TheNormalDailySwing_IsNotReported()
    {
        // 0,1 a day is the plant feeding — SOP-N1 calls this not merely harmless but useful.
        var now = DateTime.Now;
        var measurements = new List<Measurement>
        {
            At(now, ph: 6.00),
            At(now.AddHours(-24), ph: 5.90),
        };

        Assert.DoesNotContain(_service.Analyze(Grow(), measurements), d => d.StableKey == "hydro.ph-drift");
    }

    [Fact]
    public void TheSameChangeSpreadOverAWeek_IsNotAnAcuteDrift()
    {
        // The rule is explicitly about 12–24 h. Slow movement is the trend guard's job.
        var now = DateTime.Now;
        var measurements = new List<Measurement>
        {
            At(now, ph: 6.30),
            At(now.AddDays(-7), ph: 5.80),
        };

        Assert.DoesNotContain(_service.Analyze(Grow(), measurements), d => d.StableKey == "hydro.ph-drift");
    }

    // --- SOP-RDWC-CAN-N1 §2.2 and SOP-RDWC-CAN-S1 §2.2: dissolved oxygen ---

    [Theory]
    [InlineData(6.2)]
    [InlineData(6.4)]
    public void OxygenBelowSixPointFive_IsReported_AsMicrobialActivity(double value)
    {
        // Used to be silent: the old threshold was 6,0, but SOP-N1 already calls for action
        // at 6,5 — the band where root rot starts and nothing looks wrong yet.
        var deviation = Assert.Single(
            _service.Analyze(Grow(), new List<Measurement> { At(DateTime.Now, doMgL: value) }),
            d => d.StableKey == "hydro.do");

        Assert.Equal(DeviationSeverity.Warning, deviation.Severity);
        Assert.Contains("mikrobiologische", deviation.RecommendationHint ?? string.Empty);
    }

    [Fact]
    public void OxygenBelowSix_CountsAsConfirmedRootRot()
    {
        var deviation = Assert.Single(
            _service.Analyze(Grow(), new List<Measurement> { At(DateTime.Now, doMgL: 5.5) }),
            d => d.StableKey == "hydro.do");

        Assert.Equal(DeviationSeverity.Critical, deviation.Severity);
        Assert.Contains("Wurzelfaeule", deviation.RecommendationHint ?? string.Empty);
    }

    [Fact]
    public void HealthyOxygen_IsSilent()
    {
        Assert.DoesNotContain(
            _service.Analyze(Grow(), new List<Measurement> { At(DateTime.Now, doMgL: 7.8) }),
            d => d.StableKey == "hydro.do");
    }

    // --- VPD: the setpoints carried a band per stage that nothing ever read ---

    [Fact]
    public void VpdBelowItsBand_IsReported_WithTheRdwcReasoning()
    {
        // Flower targets 1,0–1,2 kPa. 24 °C at 75 % RH with a 2 °C leaf offset lands well
        // under that — which for RDWC means the plant is being held back, not protected.
        var measurement = At(DateTime.Now);
        measurement.AirTemperatureC = 24.0;
        measurement.HumidityPercent = 75.0;

        var deviation = Assert.Single(
            _service.Analyze(Grow(), new List<Measurement> { measurement }),
            d => d.StableKey == "hydro.vpd");

        Assert.Contains("unter", deviation.Message);
        Assert.Contains("Luftstrom", deviation.RecommendationHint ?? string.Empty);
        Assert.Contains("90-120", deviation.RecommendationHint ?? string.Empty);
    }

    [Fact]
    public void VpdInsideItsBand_IsSilent()
    {
        // 26 °C at 55 % RH with a 2 °C leaf offset gives 1,14 kPa — inside Flower's 1,0–1,2.
        var measurement = At(DateTime.Now);
        measurement.AirTemperatureC = 26.0;
        measurement.HumidityPercent = 55.0;

        var deviations = _service.Analyze(Grow(), new List<Measurement> { measurement });
        var vpd = deviations.FirstOrDefault(d => d.StableKey == "hydro.vpd");

        Assert.True(vpd is null,
            $"Erwartet keine VPD-Abweichung, kam aber: {vpd?.Message}");
    }

    [Fact]
    public void VpdIsJudgedAsLeafVpd_NotAirVpd()
    {
        // The two are genuinely different numbers, and every RDWC recommendation is written
        // for the leaf one: 26 °C at 55 % RH is 1,14 kPa at the leaf — comfortably inside
        // Flower's band — but 1,51 kPa measured against air alone, which would be flagged.
        var measurement = At(DateTime.Now);
        measurement.AirTemperatureC = 26.0;
        measurement.HumidityPercent = 55.0;

        var asLeaf = _service.Analyze(Grow(), new List<Measurement> { measurement }, leafTempOffsetC: -2.0);
        var asAir = _service.Analyze(Grow(), new List<Measurement> { measurement }, leafTempOffsetC: 0.0);

        Assert.DoesNotContain(asLeaf, d => d.StableKey == "hydro.vpd");
        Assert.Contains(asAir, d => d.StableKey == "hydro.vpd");
    }

    [Fact]
    public void WithoutTemperatureOrHumidity_NoVpdIsInvented()
    {
        var deviations = _service.Analyze(Grow(), new List<Measurement> { At(DateTime.Now, ph: 6.0) });

        Assert.DoesNotContain(deviations, d => d.StableKey == "hydro.vpd");
    }

    // --- Growplan: the flush ends at EC 0,4 ---

    [Fact]
    public void FlushingCorrectlyInFinish_IsNotReportedAsOutOfRange()
    {
        // The setpoint used to say 1,1–1,6 for Finish, so a grower following the plan down
        // to 0,4 was told the value was wrong.
        var grow = Grow(imFinish: true);
        var measurement = At(DateTime.Now, ec: 0.4);

        var deviations = _service.Analyze(grow, new List<Measurement> { measurement });

        Assert.DoesNotContain(deviations, d => d.StableKey == "hydro.ec");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* temp dir */ }
    }

    private static void CopyDefaults(string source, string tempRoot)
    {
        var destination = Path.Combine(tempRoot, "wwwroot", "knowledge-defaults");
        foreach (var file in Directory.EnumerateFiles(source, "*.json", SearchOption.AllDirectories))
        {
            var target = file.Replace(source, destination);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static string FindProjectRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory, "GrowDiary.Web")))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new InvalidOperationException("Projektwurzel nicht gefunden.");
    }
}
