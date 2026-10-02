using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests;

public sealed class DeviationAnalyzerServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly DeviationAnalyzerService _svc;

    public DeviationAnalyzerServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "DevAnalyzerTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        var projectRoot = FindProjectRoot();
        var defaultsSource = Path.Combine(projectRoot, "GrowDiary.Web", "wwwroot", "knowledge-defaults");
        CopyDefaults(defaultsSource, _tempRoot);

        var paths = new AppPaths(_tempRoot);
        var loader = new KnowledgeBaseLoader(paths, NullLogger<KnowledgeBaseLoader>.Instance);
        loader.Initialize();

        _svc = new DeviationAnalyzerService(new TargetValueService(loader));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private static string FindProjectRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.GetFiles(dir, "*.sln").Length > 0 ||
                Directory.Exists(Path.Combine(dir, "GrowDiary.Web")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Project root not found");
    }

    private static void CopyDefaults(string source, string tempRoot)
    {
        var dest = Path.Combine(tempRoot, "wwwroot", "knowledge-defaults");
        foreach (var file in Directory.EnumerateFiles(source, "*.json", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            var target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    /// <summary>
    /// Ein Grow, dessen <b>gerechnete</b> Phase die gewünschte ist.
    /// </summary>
    /// <remarks>
    /// <para>Bis zum 02.09.2026 stand die Phase auf der <i>Messung</i>, und die
    /// Diagnose las sie von dort. Das lief mit dem Rest der App auseinander:
    /// die Kopfzeile fragte immer schon <see cref="GrowStageResolver"/>, die
    /// Kachel auch — die Diagnose nicht. Eine einzige Messzeile mit falscher
    /// Aufschrift reichte, und dieselbe Zahl war auf der Kachel „im Ziel" und
    /// in der Diagnose „ausserhalb".</para>
    ///
    /// <para>Seitdem kommt die Phase aus dem Grow, und diese Tests schreiben sie
    /// deshalb dorthin, wo der Nutzer sie auch setzt: Startdatum, Flip,
    /// „Finish beginnt".</para>
    /// </remarks>
    private static GrowRun CreateHydroGrow(GrowStage stage = GrowStage.Veg)
    {
        var grow = new GrowRun
        {
            Name = "Test",
            MediumType = MediumType.Hydro,
            IrrigationType = IrrigationType.ActiveHydro,
            HydroStyle = HydroStyle.RDWC,
            SeedType = SeedType.Feminized,
            StartDate = DateTime.Today.AddDays(-60),
            // „Vegi beginnt" — seit dem Phasenanker schaltet nichts mehr nach Tagen.
            VegStartedAt = DateTime.Today.AddDays(-50),
        };

        switch (stage)
        {
            case GrowStage.Veg:
                break;
            case GrowStage.Flower:
                grow.FlipDate = DateTime.Today.AddDays(-30);
                break;
            case GrowStage.Finish:
                grow.FlipDate = DateTime.Today.AddDays(-45);
                grow.FinishStartedAt = DateTime.Today.AddDays(-3);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(stage), stage, "Fuer diese Phase gibt es hier keinen Aufbau.");
        }

        // Gegenprobe: der Aufbau muss wirklich die gewuenschte Phase ergeben.
        // Ohne sie prueft der Test darunter eine andere Phase als benannt.
        var gerechnet = GrowStageResolver.Resolve(grow, DateTime.Today);
        Assert.True(gerechnet == stage,
            $"Der Aufbau liefert {gerechnet}, gewollt war {stage}.");

        return grow;
    }

    private static Measurement CreateMeasurement(GrowStage stage) => new()
    {
        Id = 1,
        Stage = stage,
        TakenAt = DateTime.Now,
        // Veg OK-Bereich: pH 6.0–6.1, EC 0.6–0.8
        ReservoirPh = 6.05,
        ReservoirEc = 0.7,
        ReservoirWaterTempC = 20.0,
        DissolvedOxygenMgL = 8.0
    };

    [Fact]
    public void EineMessungOhneAuffaelligkeiten_GibtLeersteListe()
    {
        var grow = CreateHydroGrow();
        var measurements = new List<Measurement> { CreateMeasurement(GrowStage.Veg) };

        var result = _svc.Analyze(grow, measurements);

        Assert.Empty(result);
    }

    [Fact]
    public void KeineMEssungen_GibtLeersteListe()
    {
        var grow = CreateHydroGrow();

        var result = _svc.Analyze(grow, new List<Measurement>());

        Assert.Empty(result);
    }

    [Fact]
    public void Ph_ZuHoch_Warning()
    {
        var grow = CreateHydroGrow();
        var m = CreateMeasurement(GrowStage.Veg);
        // Handlungsbereich ist 5.8–6.2 (Growplan), Critical erst ab 6.5 → 6.3 ist Warning.
        m.ReservoirPh = 6.3;

        var result = _svc.Analyze(grow, new List<Measurement> { m });

        var dev = Assert.Single(result, d => d.Metric == DeviationMetric.Ph);
        Assert.Equal(DeviationSeverity.Warning, dev.Severity);
        Assert.Equal("hydro.ph", dev.StableKey);
        Assert.Equal("pH", dev.Unit);
        Assert.Equal(6.3, dev.ActualValue);
        Assert.Equal(5.8, dev.TargetMin);
        Assert.Equal(6.2, dev.TargetMax);
        // Das Anmischziel bleibt sichtbar, damit die Empfehlung brauchbar bleibt. Das
        // Dezimaltrennzeichen haengt an der Kultur des Hosts (Komma hier, Punkt auf dem
        // CI-Runner) und darf deshalb nicht mitgeprueft werden.
        Assert.Contains($"{6.0:0.0}-{6.1:0.0}", dev.RecommendationHint ?? string.Empty);
        Assert.False(string.IsNullOrWhiteSpace(dev.Message));
        Assert.Contains(m.Id, dev.SourceMeasurementIds);
        Assert.Equal(DeviationSource.Manual, dev.Source);
    }

    [Fact]
    public void Ph_ZuHoch_DreiMessungen_Critical()
    {
        var grow = CreateHydroGrow();
        var measurements = Enumerable.Range(0, 3).Select(i =>
        {
            var m = CreateMeasurement(GrowStage.Veg);
            m.Id = i + 1;
            m.ReservoirPh = 6.6;
            m.TakenAt = DateTime.Now.AddHours(-i);
            return m;
        }).ToList();

        var result = _svc.Analyze(grow, measurements);

        var dev = Assert.Single(result, d => d.Metric == DeviationMetric.Ph);
        Assert.Equal(DeviationSeverity.Critical, dev.Severity);
        Assert.Equal(3, dev.ConsecutiveCount);
        Assert.Equal(measurements.Select(m => m.Id).ToList(), dev.SourceMeasurementIds);
        Assert.Equal(measurements.Min(m => m.TakenAt).ToUniversalTime(), dev.FirstDetectedAtUtc);
        Assert.Equal(measurements.Max(m => m.TakenAt).ToUniversalTime(), dev.LastDetectedAtUtc);
    }

    [Theory]
    [InlineData(5.4)]
    [InlineData(6.6)]
    public void Ph_DeutlichAusserhalb_Critical(double ph)
    {
        var grow = CreateHydroGrow();
        var m = CreateMeasurement(GrowStage.Veg);
        m.ReservoirPh = ph;

        var result = _svc.Analyze(grow, new List<Measurement> { m });

        var dev = Assert.Single(result, d => d.Metric == DeviationMetric.Ph);
        Assert.Equal(DeviationSeverity.Critical, dev.Severity);
    }

    [Fact]
    public void Ph_ImBereich_KeineDeviation()
    {
        var grow = CreateHydroGrow();
        var m = CreateMeasurement(GrowStage.Veg);
        m.ReservoirPh = 6.0; // Veg-Bereich 6.0–6.1

        var result = _svc.Analyze(grow, new List<Measurement> { m });

        Assert.DoesNotContain(result, d => d.Metric == DeviationMetric.Ph);
    }

    [Theory]
    [InlineData(5.85)]
    [InlineData(6.1)]
    [InlineData(6.2)]
    public void Ph_DriftetInnerhalbDerKomfortzone_MahntNicht(double ph)
    {
        // Regression: der Growplan sagt ausdruecklich, den pH zwischen 5.8 und 6.2 in Ruhe
        // zu lassen (ab der 4. Bluetewoche bewusst). Frueher gab es hier eine Warnung samt
        // "pH-Down pruefen" — also genau den Rat, den die Quelle verbietet.
        var grow = CreateHydroGrow();
        var m = CreateMeasurement(GrowStage.Veg);
        m.ReservoirPh = ph;

        var result = _svc.Analyze(grow, new List<Measurement> { m });

        Assert.DoesNotContain(result, d => d.Metric == DeviationMetric.Ph);
    }

    [Fact]
    public void Ph_InFinish_TieferAnmischzielBleibtErlaubt()
    {
        // Finish mischt bewusst auf 5.6–5.8 an — das darf keine Abweichung ausloesen.
        var grow = CreateHydroGrow(GrowStage.Finish);
        var m = CreateMeasurement(GrowStage.Finish);
        m.ReservoirPh = 5.65;
        m.ReservoirEc = 1.3;

        var result = _svc.Analyze(grow, new List<Measurement> { m });

        Assert.DoesNotContain(result, d => d.Metric == DeviationMetric.Ph);
    }

    [Fact]
    public void Ppfd_OhneCo2_UeberDerObergrenze_Warnt()
    {
        // Growplan: die hohen PPFD-Ziele setzen CO2 voraus; ohne CO2 sind 800–900 Schluss.
        var grow = CreateHydroGrow();
        var m = CreateMeasurement(GrowStage.Veg);
        m.PpfdMol = 1000;
        m.Co2Ppm = 450;

        var result = _svc.Analyze(grow, new List<Measurement> { m });

        var dev = Assert.Single(result, d => d.Metric == DeviationMetric.Ppfd);
        Assert.Equal(DeviationSeverity.Warning, dev.Severity);
        Assert.Equal("hydro.ppfd-no-co2", dev.StableKey);
        Assert.Contains("50er", dev.RecommendationHint ?? string.Empty);
    }

    [Fact]
    public void Ppfd_MitCo2_BleibtErlaubt()
    {
        var grow = CreateHydroGrow();
        var m = CreateMeasurement(GrowStage.Veg);
        m.PpfdMol = 1000;
        m.Co2Ppm = 1200;

        var result = _svc.Analyze(grow, new List<Measurement> { m });

        Assert.DoesNotContain(result, d => d.StableKey == "hydro.ppfd-no-co2");
    }

    [Fact]
    public void EC_Gefallen_Warning()
    {
        var grow = CreateHydroGrow();
        var m1 = CreateMeasurement(GrowStage.Veg);
        m1.ReservoirEc = 0.6;
        m1.TakenAt = DateTime.Now;

        var m2 = CreateMeasurement(GrowStage.Veg);
        m2.ReservoirEc = 0.9;
        m2.TakenAt = DateTime.Now.AddHours(-1);

        var result = _svc.Analyze(grow, new List<Measurement> { m1, m2 });

        var dev = Assert.Single(result, d => d.Metric == DeviationMetric.Ec);
        Assert.Contains("gefallen", dev.Recommendation);
    }

    [Fact]
    public void EC_Gestiegen_Warning()
    {
        var grow = CreateHydroGrow();
        var m1 = CreateMeasurement(GrowStage.Veg);
        m1.ReservoirEc = 1.2;
        m1.TakenAt = DateTime.Now;

        var m2 = CreateMeasurement(GrowStage.Veg);
        m2.ReservoirEc = 0.9;
        m2.TakenAt = DateTime.Now.AddHours(-1);

        var result = _svc.Analyze(grow, new List<Measurement> { m1, m2 });

        var dev = Assert.Single(result, d => d.Metric == DeviationMetric.Ec);
        Assert.Contains("gestiegen", dev.Recommendation);
    }

    [Fact]
    public void EC_UeberZiel_Warning()
    {
        var grow = CreateHydroGrow();
        var m = CreateMeasurement(GrowStage.Veg);
        m.ReservoirEc = 1.0;

        var result = _svc.Analyze(grow, new List<Measurement> { m });

        var dev = Assert.Single(result, d => d.Metric == DeviationMetric.Ec);
        Assert.Equal(DeviationSeverity.Warning, dev.Severity);
        Assert.Equal("mS/cm", dev.Unit);
    }

    [Fact]
    public void WasserTemp_Kritisch_Critical()
    {
        var grow = CreateHydroGrow();
        var m = CreateMeasurement(GrowStage.Veg);
        m.ReservoirWaterTempC = 25.0; // über 24°C Critical-Schwelle

        var result = _svc.Analyze(grow, new List<Measurement> { m });

        var dev = Assert.Single(result, d => d.Metric == DeviationMetric.WaterTemp);
        Assert.Equal(DeviationSeverity.Critical, dev.Severity);
    }

    [Fact]
    public void DO_Niedrig_Warning()
    {
        var grow = CreateHydroGrow();
        var m = CreateMeasurement(GrowStage.Veg);
        m.DissolvedOxygenMgL = 5.5; // unter 6.0 Warning-Schwelle

        var result = _svc.Analyze(grow, new List<Measurement> { m });

        Assert.Contains(result, d => d.Metric == DeviationMetric.DissolvedOxygen);
    }

    [Fact]
    public void DO_SehrNiedrig_Critical()
    {
        var grow = CreateHydroGrow();
        var m = CreateMeasurement(GrowStage.Veg);
        m.DissolvedOxygenMgL = 3.8; // unter 4.0 Critical-Schwelle

        var result = _svc.Analyze(grow, new List<Measurement> { m });

        var dev = Assert.Single(result, d => d.Metric == DeviationMetric.DissolvedOxygen);
        Assert.Equal(DeviationSeverity.Critical, dev.Severity);
    }

    [Theory]
    [InlineData(280, DeviationSeverity.Warning)]
    [InlineData(700, DeviationSeverity.Critical)]
    public void ORP_AusserhalbBereich_ErzeugtDeviation(double orp, DeviationSeverity expectedSeverity)
    {
        var grow = CreateHydroGrow();
        var m = CreateMeasurement(GrowStage.Veg);
        m.OrpMv = orp;

        var result = _svc.Analyze(grow, new List<Measurement> { m });

        var dev = Assert.Single(result, d => d.Metric == DeviationMetric.Orp);
        Assert.Equal(expectedSeverity, dev.Severity);
        Assert.Equal("mV", dev.Unit);
    }

    [Fact]
    public void Source_WirdAlsMixedBerechnet()
    {
        var grow = CreateHydroGrow();
        var latest = CreateMeasurement(GrowStage.Veg);
        latest.Id = 10;
        latest.Source = ValueOrigin.HomeAssistant;
        latest.ReservoirPh = 6.4;
        // ORTSZEIT: TakenAt meint die Uhr des Nutzers. Mit DateTime.UtcNow laege
        // die Messung in einer Zeitzone westlich von Greenwich in der ZUKUNFT,
        // und die Analyse schliesst solche Zeilen aus — hier (UTC+2) und im Tor
        // (UTC) faellt das nicht auf.
        latest.TakenAt = DateTime.Now;
        var previous = CreateMeasurement(GrowStage.Veg);
        previous.Id = 9;
        previous.Source = ValueOrigin.Manual;
        previous.ReservoirPh = 6.3;
        previous.TakenAt = latest.TakenAt.AddHours(-1);

        var result = _svc.Analyze(grow, new List<Measurement> { latest, previous });

        var dev = Assert.Single(result, d => d.Metric == DeviationMetric.Ph);
        Assert.Equal(DeviationSource.Mixed, dev.Source);
        Assert.Equal(new[] { latest.Id, previous.Id }, dev.SourceMeasurementIds);
    }

    [Fact]
    public void Source_WirdAlsHomeAssistantBerechnet()
    {
        var grow = CreateHydroGrow();
        var m = CreateMeasurement(GrowStage.Veg);
        m.Source = ValueOrigin.HomeAssistant;
        m.ReservoirPh = 6.3;

        var result = _svc.Analyze(grow, new List<Measurement> { m });

        var dev = Assert.Single(result, d => d.Metric == DeviationMetric.Ph);
        Assert.Equal(DeviationSource.HomeAssistant, dev.Source);
    }

    [Fact]
    public void AlleWerteOK_KeinDeviations()
    {
        var grow = CreateHydroGrow();
        var m = CreateMeasurement(GrowStage.Veg);
        // Alle Werte sicher im Veg-Bereich: pH 6.05, EC 0.7, Temp 20, DO 8.0

        var result = _svc.Analyze(grow, new List<Measurement> { m });

        Assert.Empty(result);
    }
}
