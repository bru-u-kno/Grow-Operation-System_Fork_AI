using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services;

public sealed class DeviationRiskEventSyncServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly AppPaths _paths;
    private readonly GrowRepository _repository;
    private readonly DeviationRiskEventSyncService _service;

    public DeviationRiskEventSyncServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "DeviationRiskSyncTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        CopyDefaults(Path.Combine(FindProjectRoot(), "GrowDiary.Web", "wwwroot", "knowledge-defaults"), _tempRoot);

        _paths = new AppPaths(_tempRoot);
        TestDatabase.InitializeWithDefaultTent(_paths, tentType: TentType.Production);
        _repository = new GrowRepository(_paths);

        var loader = new KnowledgeBaseLoader(_paths, NullLogger<KnowledgeBaseLoader>.Instance);
        loader.Initialize();
        var analyzer = new DeviationAnalyzerService(new TargetValueService(loader));
        _service = new DeviationRiskEventSyncService(_repository, analyzer, new TreatmentRecommender(loader));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    [Fact]
    public void SyncActiveGrowDeviations_CreatesGenericDeviationRisksWithoutDuplicates()
    {
        var growId = CreateHydroGrow("Risk Sync Hydro");
        AddMeasurement(growId, Utc(2026, 5, 20), ec: 3.2, orp: 700, waterTemp: 25);

        _service.SyncActiveGrowDeviations();
        _service.SyncActiveGrowDeviations();

        var risks = _repository.GetRiskEventsByGrow(growId)
            .Where(risk => risk.Source == RiskEventSource.Deviation)
            .ToList();

        Assert.Equal(3, risks.Count);
        Assert.Contains(risks, risk => risk.DedupeKey == $"deviation:grow:{growId}:hydro.ec");
        Assert.Contains(risks, risk => risk.DedupeKey == $"deviation:grow:{growId}:hydro.orp");
        Assert.Contains(risks, risk => risk.DedupeKey == $"deviation:grow:{growId}:hydro.water-temp");
        Assert.All(risks, risk => Assert.Equal(RiskEventStatus.Open, risk.Status));
        Assert.All(risks, risk => Assert.Contains("Handlung:", risk.Description));
    }

    [Fact]
    public void SyncActiveGrowDeviations_ResolvesDeviationRisksWhenCurrentValuesRecover()
    {
        var growId = CreateHydroGrow("Recovered Hydro");
        AddMeasurement(growId, Utc(2026, 5, 20), ec: 3.2, orp: 700, waterTemp: 25);
        _service.SyncActiveGrowDeviations();

        /* 350 mV, nicht 410.
           Bis zum 01.09.2026 urteilte die Diagnose ueber ORP gegen fest
           verdrahtete 300-500 statt gegen das Profil. Der Grow hier steht in
           Veg, und rdwc-default nennt dort 300-400 — 410 war „erholt" nur
           gegenueber dem alten, eigenen Band. Seit die Diagnose dasselbe Band
           benutzt wie Kachel und Messprotokoll, ist 410 zu Recht weiter ein
           Befund. */
        AddMeasurement(growId, Utc(2026, 5, 21), ec: 0.7, orp: 350, waterTemp: 20);
        AddMeasurement(growId, Utc(2026, 5, 22), ec: 0.7, orp: 350, waterTemp: 20);
        _service.SyncActiveGrowDeviations();

        var risks = _repository.GetRiskEventsByGrow(growId)
            .Where(risk => risk.Source == RiskEventSource.Deviation)
            .ToList();

        Assert.Equal(3, risks.Count);
        Assert.All(risks, risk => Assert.Equal(RiskEventStatus.Resolved, risk.Status));
        Assert.All(risks, risk => Assert.NotNull(risk.ResolvedAtUtc));
    }

    /// <summary>
    /// „Erledigt" muss halten — sonst ist der Knopf eine Lüge.
    /// </summary>
    /// <remarks>
    /// Aus dem Feld: „Diese WasserTemp-Abweichung bekomme ich nicht weg, egal ob
    /// ich Erledigt oder Bestätigt markiere." Die Dedup-Suche kannte nur offene
    /// Ereignisse; ein erledigtes fand sie nicht und legte beim naechsten
    /// Durchlauf ein neues an, weil die Abweichung aus derselben unveraenderten
    /// Messung weiterhin folgte.
    /// </remarks>
    [Fact]
    public void SyncActiveGrowDeviations_KeepsResolvedRisksClosedUntilNewDataArrives()
    {
        var growId = CreateHydroGrow("Abgehakt Hydro");
        AddMeasurement(growId, Utc(2026, 5, 20), ec: 3.2, orp: 700, waterTemp: 25);
        _service.SyncActiveGrowDeviations();

        var offen = _repository.GetRiskEventsByGrow(growId)
            .Where(risk => risk.Source == RiskEventSource.Deviation)
            .ToList();
        foreach (var risk in offen)
        {
            _repository.ResolveRiskEvent(risk.Id, Utc(2026, 5, 20).AddHours(1), "vom Betreiber erledigt");
        }

        // Kein neuer Messwert: der Durchlauf darf nichts wiederbeleben.
        _service.SyncActiveGrowDeviations();
        _service.SyncActiveGrowDeviations();

        var danach = _repository.GetRiskEventsByGrow(growId)
            .Where(risk => risk.Source == RiskEventSource.Deviation)
            .ToList();

        Assert.Equal(offen.Count, danach.Count);
        Assert.All(danach, risk => Assert.Equal(RiskEventStatus.Resolved, risk.Status));
    }

    [Fact]
    public void SyncActiveGrowDeviations_ReportsAgainWhenANewerMeasurementStillShowsIt()
    {
        var growId = CreateHydroGrow("Wieder da Hydro");
        AddMeasurement(growId, Utc(2026, 5, 20), ec: 3.2, orp: 700, waterTemp: 25);
        _service.SyncActiveGrowDeviations();

        foreach (var risk in _repository.GetRiskEventsByGrow(growId).Where(r => r.Source == RiskEventSource.Deviation))
        {
            _repository.ResolveRiskEvent(risk.Id, Utc(2026, 5, 20).AddHours(1), "erledigt");
        }

        // Eine NEUE Messung zeigt dieselbe Abweichung erneut — das ist eine
        // Neuigkeit und muss sich wieder melden, sonst verschweigt die App
        // ein weiterbestehendes Problem.
        AddMeasurement(growId, Utc(2026, 5, 22), ec: 3.2, orp: 700, waterTemp: 25);
        _service.SyncActiveGrowDeviations();

        var wiederOffen = _repository.GetRiskEventsByGrow(growId)
            .Where(risk => risk.Source == RiskEventSource.Deviation)
            .Where(risk => risk.Status == RiskEventStatus.Open)
            .ToList();

        Assert.NotEmpty(wiederOffen);
    }

    private int CreateHydroGrow(string name)
    {
        var tent = _repository.GetTents().Single();
        return _repository.CreateGrow(new GrowRun
        {
            TentId = tent.Id,
            Name = name,
            Status = GrowStatus.Running,
            MediumType = MediumType.Hydro,
            HydroStyle = HydroStyle.RDWC,
            IrrigationType = IrrigationType.ActiveHydro,
            StartDate = Utc(2026, 5, 1),
            // „Vegi beginnt" — seit dem Phasenanker schaltet nichts mehr nach Tagen.
            VegStartedAt = Utc(2026, 5, 15)
        });
    }

    private void AddMeasurement(int growId, DateTime takenAt, double ec, double orp, double waterTemp)
    {
        _repository.CreateMeasurement(new Measurement
        {
            GrowId = growId,
            TakenAt = takenAt,
            Stage = GrowStage.Veg,
            Source = ValueOrigin.Manual,
            ReservoirPh = 6.0,
            ReservoirEc = ec,
            OrpMv = orp,
            ReservoirWaterTempC = waterTemp,
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
            if (Directory.Exists(Path.Combine(dir, "GrowDiary.Web")))
            {
                return dir;
            }

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
}
