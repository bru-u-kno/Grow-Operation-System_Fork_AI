using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Api;

public sealed class GrowWorkflowApiControllerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _tempRoot;
    private readonly AppPaths _paths;
    private readonly GrowRepository _repository;
    private readonly GrowWorkflowApiController _controller;

    public GrowWorkflowApiControllerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"grow-workflow-test-{Guid.NewGuid():N}.db");
        _tempRoot = Path.Combine(Path.GetTempPath(), "GrowWorkflowControllerTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        CopyDefaults(Path.Combine(FindProjectRoot(), "GrowDiary.Web", "wwwroot", "knowledge-defaults"), _tempRoot);
        Environment.SetEnvironmentVariable("GROWDIARY_DB_PATH", _dbPath);
        _paths = new AppPaths(_tempRoot);
        TestDatabase.InitializeWithDefaultTent(_paths);
        _repository = new GrowRepository(_paths);
        _controller = new GrowWorkflowApiController(
            _repository,
            new HarvestRepository(_paths),
            new JournalRepository(_paths),
            new AuditRepository(_paths),
            CreateTargetValueService(),
            WissenLaden(),
            new WasserwechselStandService(_repository, WissenLaden()));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("GROWDIARY_DB_PATH", null);
        try { File.Delete(_dbPath); } catch { }
        try { File.Delete(_dbPath + "-shm"); } catch { }
        try { File.Delete(_dbPath + "-wal"); } catch { }
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    [Fact]
    public void AddbackDefaults_UsesHydroSetupTotalVolumeBeforeLegacyReservoirSize()
    {
        var tent = DefaultTent();
        var system = CreateRdwcHydroSetup(tent.Id);
        var growId = CreateGrow(tent.Id, system.Id, reservoirSize: "999 L Legacy");
        _repository.CreateMeasurement(new Measurement
        {
            GrowId = growId,
            Stage = GrowStage.Veg,
            TakenAt = new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc),
            ReservoirEc = 0.8
        });

        var result = _controller.AddbackDefaults(growId);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<AddbackDefaultsDto>(ok.Value);
        Assert.Equal(136, dto.SuggestedReservoirLiters);
        Assert.Equal(136, dto.ReservoirLiters);
        Assert.Equal(0.8, dto.SuggestedEcIst);
    }

    [Fact]
    public void AddbackDefaults_FallsBackToLegacyReservoirSizeWhenGrowHasNoHydroSetup()
    {
        var growId = _repository.CreateGrow(new GrowRun
        {
            Name = "Legacy Grow",
            StartDate = new DateTime(2026, 1, 1),
            Status = GrowStatus.Running,
            HydroStyle = HydroStyle.RDWC,
            ReservoirSize = "60 L Reservoir"
        });

        var result = _controller.AddbackDefaults(growId);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<AddbackDefaultsDto>(ok.Value);
        Assert.Equal(60, dto.SuggestedReservoirLiters);
        Assert.Equal(60, dto.ReservoirLiters);
    }

    [Fact]
    public void CalculateAddback_UsesHydroSetupTotalVolumeWhenRequestReservoirLitersIsMissing()
    {
        var tent = DefaultTent();
        var system = CreateRdwcHydroSetup(tent.Id);
        var growId = CreateGrow(tent.Id, system.Id, reservoirSize: "999 L Legacy");

        var result = _controller.CalculateAddback(growId, new AddbackCalculateRequest
        {
            ReservoirLiters = null,
            EcIst = 0.8,
            EcZiel = 1.2,
            EcStock = 3.0
        });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<AddbackResultDto>(ok.Value);
        Assert.True(dto.NeedsAddback);
        Assert.Equal(30.22, dto.LitersToAdd);
        Assert.Equal(166.2, dto.NewReservoirVolume);
    }

    [Fact]
    public void CalculateAddback_WithoutVolumeAndWithoutHydroSetupReturnsValidationError()
    {
        var growId = _repository.CreateGrow(new GrowRun
        {
            Name = "Legacy Grow ohne Volumen",
            StartDate = new DateTime(2026, 1, 1),
            Status = GrowStatus.Running,
            HydroStyle = HydroStyle.RDWC
        });

        var result = _controller.CalculateAddback(growId, new AddbackCalculateRequest
        {
            EcIst = 0.8,
            EcZiel = 1.2,
            EcStock = 3.0
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var error = Assert.IsType<ApiError>(badRequest.Value);
        Assert.Equal("validation_failed", error.Code);
        Assert.Contains(nameof(AddbackCalculateRequest.ReservoirLiters), error.FieldErrors!.Keys);
    }

    private Tent DefaultTent()
        => _repository.GetTents().Single();

    private GrowSystem CreateRdwcHydroSetup(int tentId)
        => _repository.CreateHydroSetup(new GrowSystem
        {
            TentId = tentId,
            Name = "RDWC 4 Site Test",
            HydroStyle = HydroStyle.RDWC.ToString(),
            PotCount = 4,
            PotSizeLiters = 19,
            ReservoirLiters = 60,
            LayoutType = HydroSetupLayoutType.Grid2x2,
            ReservoirPosition = ReservoirPosition.External,
            HasChiller = true,
            Status = HydroSetupStatus.Active
        });

    private int CreateGrow(int tentId, int systemId, string? reservoirSize = null)
        => _repository.CreateGrow(new GrowRun
        {
            TentId = tentId,
            SystemId = systemId,
            Name = "RDWC Grow",
            StartDate = new DateTime(2026, 1, 1),
            Status = GrowStatus.Running,
            MediumType = MediumType.Hydro,
            HydroStyle = HydroStyle.RDWC,
            ReservoirSize = reservoirSize
        });

    private TargetValueService CreateTargetValueService() => new(WissenLaden());

    private KnowledgeBaseLoader WissenLaden()
    {
        var loader = new KnowledgeBaseLoader(_paths, NullLogger<KnowledgeBaseLoader>.Instance);
        loader.Initialize();
        return loader;
    }

    private static string FindProjectRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.GetFiles(dir, "*.sln").Length > 0 ||
                Directory.Exists(Path.Combine(dir, "GrowDiary.Web")))
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

    /// <summary>
    /// Die Einzelgewichte je Pflanze kommen zurück, wie sie hineingingen.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Anlass (01.09.2026).</b> <c>HarvestUpsertRequest</c> trägt
    /// <c>PlantWeightsJson</c>, das Antwort-DTO gibt es zurück — und
    /// <c>GrowWorkflowMapping.ToEntry</c> kopierte es nicht. Wer auf der
    /// Ernteseite je Pflanze wog, bekam HTTP 200 und in derselben Antwort
    /// <c>plantWeightsJson: null</c>. Kein Fehler, keine Meldung, die Zahlen
    /// einfach weg.</para>
    ///
    /// <para>Bei einem Grow mit mehreren Sorten ist das die einzige Angabe, aus
    /// der sich der Ertrag je Sorte je ableiten liesse.</para>
    /// </remarks>
    [Fact]
    public void SaveHarvest_BehaeltDieEinzelgewichteJePflanze()
    {
        var grow = AngelegterGrow();
        const string gewichte = """[{"label":"PL-01","wetG":486,"dryG":108}]""";

        var gespeichert = _controller.SaveHarvest(grow, new HarvestUpsertRequest
        {
            HarvestedAtLocal = DateTime.Today.ToString("yyyy-MM-dd"),
            WetWeightG = 998,
            DryWeightG = 221,
            PlantWeightsJson = gewichte,
        });

        var dto = Assert.IsType<HarvestDto>(
            (gespeichert.Result as ObjectResult)?.Value ?? gespeichert.Value);

        Assert.True(dto.PlantWeightsJson == gewichte,
            $"Die Antwort traegt plantWeightsJson = {dto.PlantWeightsJson ?? "null"} statt der "
            + "geschickten Einzelgewichte. Wer je Pflanze wiegt, verliert die Zahlen stillschweigend.");

        // Und nach dem NEULADEN auch — sonst stuende der Wert nur im Speicher.
        var geholt = _controller.Harvest(grow);
        var wieder = Assert.IsType<HarvestDto>(
            (geholt.Result as ObjectResult)?.Value ?? geholt.Value);
        Assert.Equal(gewichte, wieder.PlantWeightsJson);
    }

    /// <summary>Ein Grow, an den sich eine Ernte haengen laesst.</summary>
    private int AngelegterGrow()
    {
        var zelt = _repository.CreateTent(new Tent { Name = "Zelt", TentType = TentType.Production });
        return _repository.CreateGrow(new GrowRun
        {
            Name = "Lauf",
            TentId = zelt.Id,
            HydroStyle = HydroStyle.RDWC,
            Status = GrowStatus.Running,
            StartDate = DateTime.Today.AddDays(-60),
        });
    }
}
