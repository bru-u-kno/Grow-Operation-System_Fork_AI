using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Der Abgleich im Minutentakt: gleicht ab, sobald es eine neue Messung gibt —
/// ohne dass jemand die Seite öffnet — und sonst nicht (16.10.2026).
/// </summary>
public sealed class RisikoAbgleichRunnerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly GrowRepository _repository;
    private readonly RisikoAbgleichRunner _runner;

    public RisikoAbgleichRunnerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RisikoAbgleichRunnerTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        CopyDefaults(Path.Combine(FindProjectRoot(), "GrowDiary.Web", "wwwroot", "knowledge-defaults"), _tempRoot);

        var paths = new AppPaths(_tempRoot);
        TestDatabase.InitializeWithDefaultTent(paths, tentType: TentType.Production);
        _repository = new GrowRepository(paths);

        var loader = new KnowledgeBaseLoader(paths, NullLogger<KnowledgeBaseLoader>.Instance);
        loader.Initialize();
        var sync = new DeviationRiskEventSyncService(
            _repository, new DeviationAnalyzerService(new TargetValueService(loader)), new TreatmentRecommender(loader));
        _runner = new RisikoAbgleichRunner(_repository, sync, NullLogger<RisikoAbgleichRunner>.Instance);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    [Fact]
    public void GleichtOhneSeitenaufrufAbUndNurEinmalJeNeuerMessung()
    {
        var growId = CreateGrow();
        AddMeasurement(growId, Utc(2026, 5, 20), ec: 3.2);

        // Erster Durchlauf: der Grow ist neu für den Runner → abgleichen.
        Assert.Equal(1, _runner.Einmal());
        Assert.NotEmpty(OffeneRisiken(growId));

        // Nichts Neues: die nächsten Minuten gleichen nichts ab.
        Assert.Equal(0, _runner.Einmal());
        Assert.Equal(0, _runner.Einmal());

        // Eine neue Messung ohne die Abweichung: nächster Durchlauf gleicht ab
        // und schließt die Karte von selbst, ohne dass jemand hinsieht.
        AddMeasurement(growId, Utc(2026, 5, 21), ec: 0.7);
        AddMeasurement(growId, Utc(2026, 5, 22), ec: 0.7);
        Assert.Equal(1, _runner.Einmal());

        var ec = _repository.GetRiskEventsByGrow(growId).Single(risk => risk.DedupeKey!.EndsWith("hydro.ec"));
        Assert.Equal(RiskEventStatus.Resolved, ec.Status);
        Assert.Contains(DeviationRiskEventSyncService.AutoResolvedNote, ec.Notes);
    }

    /// <summary>
    /// Ein nachträglich korrigierter Wert einer bestehenden Messung ändert weder die
    /// Anzahl noch die neueste Nummer noch die Zeit — und muss trotzdem abgleichen.
    /// </summary>
    [Fact]
    public void EineKorrigierteMessungStoesstDenAbgleichAn()
    {
        var growId = CreateGrow();
        AddMeasurement(growId, Utc(2026, 5, 20), ec: 3.2);
        Assert.Equal(1, _runner.Einmal());
        Assert.Equal(0, _runner.Einmal());
        Assert.NotEmpty(OffeneRisiken(growId));

        var messung = _repository.GetMeasurementsForGrow(growId).Single();
        messung.ReservoirEc = 0.7;
        _repository.UpdateMeasurement(messung);

        Assert.Equal(1, _runner.Einmal());
        Assert.Empty(OffeneRisiken(growId));
    }

    [Fact]
    public void EinGrowOhneMessungenWirdEinmalGeprueftUndDanachInRuhegelassen()
    {
        CreateGrow();

        Assert.Equal(1, _runner.Einmal());
        Assert.Equal(0, _runner.Einmal());
    }

    private int CreateGrow()
    {
        var tent = _repository.GetTents().Single();
        return _repository.CreateGrow(new GrowRun
        {
            TentId = tent.Id,
            Name = "Abgleich Hydro",
            Status = GrowStatus.Running,
            MediumType = MediumType.Hydro,
            HydroStyle = HydroStyle.RDWC,
            IrrigationType = IrrigationType.ActiveHydro,
            StartDate = Utc(2026, 5, 1),
            VegStartedAt = Utc(2026, 5, 15)
        });
    }

    private List<RiskEvent> OffeneRisiken(int growId)
        => _repository.GetRiskEventsByGrow(growId)
            .Where(risk => risk.Source == RiskEventSource.Deviation && risk.Status == RiskEventStatus.Open)
            .ToList();

    private void AddMeasurement(int growId, DateTime takenAt, double ec)
    {
        _repository.CreateMeasurement(new Measurement
        {
            GrowId = growId,
            TakenAt = takenAt,
            Stage = GrowStage.Veg,
            Source = ValueOrigin.Manual,
            ReservoirPh = 6.0,
            ReservoirEc = ec,
            OrpMv = 350,
            ReservoirWaterTempC = 20,
            DissolvedOxygenMgL = 8.0
        });
    }

    private static DateTime Utc(int year, int month, int day)
        => new(year, month, day, 12, 0, 0, DateTimeKind.Utc);

    private static string FindProjectRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "GrowDiary.Web"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Project root not found");
    }

    private static void CopyDefaults(string source, string tempRoot)
    {
        var dest = Path.Combine(tempRoot, "wwwroot", "knowledge-defaults");
        foreach (var file in Directory.EnumerateFiles(source, "*.json", SearchOption.AllDirectories))
        {
            var target = Path.Combine(dest, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
