using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Api;

public sealed class GrowsApiControllerSetupTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _tempRoot;
    private readonly AppPaths _paths;
    private readonly GrowRepository _growRepository;
    private readonly GrowsApiController _controller;

    public GrowsApiControllerSetupTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"grow-test-{Guid.NewGuid():N}.db");
        _tempRoot = Path.Combine(Path.GetTempPath(), "GrowControllerTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        CopyDefaults(Path.Combine(FindProjectRoot(), "GrowDiary.Web", "wwwroot", "knowledge-defaults"), _tempRoot);
        Environment.SetEnvironmentVariable("GROWDIARY_DB_PATH", _dbPath);
        _paths = new AppPaths(_tempRoot);
        GrowDiary.Web.Tests.TestDatabase.InitializeWithDefaultTent(_paths);
        _growRepository = new GrowRepository(_paths);
        _controller = new GrowsApiController(
            _growRepository,
            new AuditRepository(_paths),
            CreateDeviationAnalyzer(),
            CreateTreatmentRecommender(),
            new SetupRepository(_paths),
            new HydroSetupRepository(_paths, new TentRepository(_paths)),
            new SystemAuditRepository(_paths));
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
    public void Create_WithoutSystemId_IsRejected()
    {
        var request = NewGrowRequest("Grow ohne HydroSetup");
        request.SystemId = null;

        var result = _controller.Create(request);

        AssertSystemValidationError(result.Result);
    }

    [Fact]
    public void Create_WithoutSetupId_RemainsValidWhenHydroSetupIsSelected()
    {
        var result = _controller.Create(NewGrowRequest("Grow ohne Plant-Setup"));

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<GrowDetailDto>(created.Value);
        Assert.Null(dto.SetupId);
        Assert.NotNull(dto.SystemId);
    }

    [Fact]
    public void Create_WithProductionSetupAndMatchingTent_IsAccepted()
    {
        var tent = DefaultTent();
        var setup = CreateSetup(tent.Id, SetupType.Production);
        var system = CreateHydroSetup(tent.Id, HydroStyle.RDWC);

        var result = _controller.Create(NewGrowRequest("Production Grow", tent.Id, setup.Id, system.Id));

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<GrowDetailDto>(created.Value);
        Assert.Equal(setup.Id, dto.SetupId);
        Assert.Equal(system.Id, dto.SystemId);
        Assert.Equal(tent.Id, dto.TentId);
        var loaded = _growRepository.GetGrow(dto.Id)!;
        Assert.Equal(setup.Id, loaded.SetupId);
        Assert.Equal(system.Id, loaded.SystemId);
    }

    [Fact]
    public void Archive_RunningGrowMarksCompletedAndRemovesActiveBlocker()
    {
        var tent = DefaultTent();
        var system = CreateHydroSetup(tent.Id, HydroStyle.RDWC);
        var created = Assert.IsType<GrowDetailDto>(Assert.IsType<CreatedAtActionResult>(_controller.Create(NewGrowRequest("Archive Grow", tent.Id, null, system.Id)).Result).Value);
        var grow = _growRepository.GetGrow(created.Id)!;
        grow.Status = GrowStatus.Running;
        _growRepository.UpdateGrow(grow);

        var result = _controller.Archive(created.Id);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<GrowDetailDto>(ok.Value);
        Assert.Equal(GrowStatus.Completed, dto.Status);
        Assert.NotNull(dto.EndDate);
        Assert.DoesNotContain(_growRepository.GetActiveGrows(), item => item.Id == created.Id);
        Assert.Equal(0, _growRepository.GetHydroSetup(system.Id)!.ActiveGrowCount);
    }

    [Theory]
    [InlineData(HydroStyle.DWC)]
    [InlineData(HydroStyle.RDWC)]
    public void Create_WithDwcOrRdwcHydroSetup_IsAcceptedAndAlignsHydroStyle(HydroStyle hydroStyle)
    {
        var tent = DefaultTent();
        var system = CreateHydroSetup(tent.Id, hydroStyle);
        var request = NewGrowRequest($"{hydroStyle} Grow", tent.Id, systemId: system.Id);
        request.HydroStyle = HydroStyle.RDWC;

        var result = _controller.Create(request);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<GrowDetailDto>(created.Value);
        Assert.Equal(system.Id, dto.SystemId);
        Assert.Equal(hydroStyle, dto.HydroStyle);
    }

    [Fact]
    public void Create_WithHydroSetup_AlignsTechnicalFieldsFromHydroSetup()
    {
        var tent = DefaultTent();
        var system = CreateHydroSetup(tent.Id, HydroStyle.RDWC, hasChiller: true);
        var request = NewGrowRequest("Aligned Grow", tent.Id, systemId: system.Id);
        request.HydroStyle = HydroStyle.RDWC;
        request.ContainerSize = "Legacy Container";
        request.ReservoirSize = "Legacy Reservoir";
        request.HasChiller = false;

        var result = _controller.Create(request);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<GrowDetailDto>(created.Value);
        Assert.Equal(system.Id, dto.SystemId);
        Assert.Equal(HydroStyle.RDWC, dto.HydroStyle);
        Assert.True(dto.HasChiller);
        Assert.Equal("4 x 19 L", dto.ContainerSize);
        Assert.Equal("136 L Gesamtvolumen", dto.ReservoirSize);
    }

    [Theory]
    [InlineData(HydroStyle.NFT)]
    [InlineData(HydroStyle.Aeroponic)]
    [InlineData(HydroStyle.Other)]
    [InlineData(HydroStyle.None)]
    public void Create_WithNonDwcHydroStyle_IsRejected(HydroStyle hydroStyle)
    {
        var request = NewGrowRequest($"{hydroStyle} Grow");
        request.HydroStyle = hydroStyle;

        var result = _controller.Create(request);

        AssertHydroStyleValidationError(result.Result);
    }

    [Theory]
    [InlineData(HydroStyle.NFT)]
    [InlineData(HydroStyle.Aeroponic)]
    [InlineData(HydroStyle.Other)]
    [InlineData(HydroStyle.None)]
    public void Update_WithNonDwcHydroStyle_IsRejected(HydroStyle hydroStyle)
    {
        var tent = DefaultTent();
        var system = CreateHydroSetup(tent.Id, HydroStyle.RDWC);
        var growId = _growRepository.CreateGrow(new GrowRun
        {
            TentId = tent.Id,
            SystemId = system.Id,
            Name = "Original",
            StartDate = new DateTime(2026, 1, 1),
            Status = GrowStatus.Planning,
            HydroStyle = HydroStyle.RDWC
        });
        var request = NewGrowRequest("Updated", tent.Id, systemId: system.Id);
        request.HydroStyle = hydroStyle;

        var result = _controller.Update(growId, request);

        AssertHydroStyleValidationError(result.Result);
    }

    [Fact]
    public void Create_WithMotherSetup_IsRejected()
    {
        var tent = DefaultTent();
        var setup = CreateSetup(tent.Id, SetupType.Mother);
        var system = CreateHydroSetup(tent.Id, HydroStyle.RDWC);

        var result = _controller.Create(NewGrowRequest("Mother Grow", tent.Id, setup.Id, system.Id));

        AssertSetupValidationError(result.Result);
    }

    [Fact]
    public void Create_WithQuarantineSetup_IsRejected()
    {
        var tent = DefaultTent();
        var setup = CreateSetup(tent.Id, SetupType.Quarantine);
        var system = CreateHydroSetup(tent.Id, HydroStyle.RDWC);

        var result = _controller.Create(NewGrowRequest("Quarantine Grow", tent.Id, setup.Id, system.Id));

        AssertSetupValidationError(result.Result);
    }

    [Fact]
    public void Create_WithProductionSetupFromDifferentTent_IsRejected()
    {
        var growTent = DefaultTent();
        var setupTent = _growRepository.CreateTent("Second Tent");
        var setup = CreateSetup(setupTent.Id, SetupType.Production);
        var system = CreateHydroSetup(growTent.Id, HydroStyle.RDWC);

        var result = _controller.Create(NewGrowRequest("Wrong Tent Grow", growTent.Id, setup.Id, system.Id));

        AssertSetupValidationError(result.Result);
    }

    [Fact]
    public void Create_WithHydroSetupFromDifferentTent_IsRejected()
    {
        var growTent = DefaultTent();
        var systemTent = _growRepository.CreateTent("System Tent");
        var system = CreateHydroSetup(systemTent.Id, HydroStyle.RDWC);

        var result = _controller.Create(NewGrowRequest("Wrong System Grow", growTent.Id, systemId: system.Id));

        AssertSystemValidationError(result.Result);
    }

    [Fact]
    public void Create_WithArchivedHydroSetup_IsRejected()
    {
        var tent = DefaultTent();
        var system = CreateHydroSetup(tent.Id, HydroStyle.RDWC);
        _growRepository.ArchiveHydroSetup(system.Id);

        var result = _controller.Create(NewGrowRequest("Archived System Grow", tent.Id, systemId: system.Id));

        AssertSystemValidationError(result.Result);
    }

    [Fact]
    public void Update_WithProductionSetup_PersistsSetupIdAndHydroSetupId()
    {
        var tent = DefaultTent();
        var setup = CreateSetup(tent.Id, SetupType.Production);
        var system = CreateHydroSetup(tent.Id, HydroStyle.RDWC);
        var growId = _growRepository.CreateGrow(new GrowRun
        {
            TentId = tent.Id,
            SystemId = system.Id,
            Name = "Original",
            StartDate = new DateTime(2026, 1, 1),
            Status = GrowStatus.Planning,
            HydroStyle = HydroStyle.RDWC
        });

        var result = _controller.Update(growId, NewGrowRequest("Updated", tent.Id, setup.Id, system.Id));

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<GrowDetailDto>(ok.Value);
        Assert.Equal(setup.Id, dto.SetupId);
        Assert.Equal(system.Id, dto.SystemId);
        var loaded = _growRepository.GetGrow(growId)!;
        Assert.Equal(setup.Id, loaded.SetupId);
        Assert.Equal(system.Id, loaded.SystemId);
    }

    [Fact]
    public void Update_WithoutSystemId_PreservesExistingSystemId()
    {
        var tent = DefaultTent();
        var system = CreateHydroSetup(tent.Id, HydroStyle.RDWC);
        var growId = _growRepository.CreateGrow(new GrowRun
        {
            TentId = tent.Id,
            SystemId = system.Id,
            Name = "Original",
            StartDate = new DateTime(2026, 1, 1),
            Status = GrowStatus.Planning,
            HydroStyle = HydroStyle.RDWC
        });
        var request = NewGrowRequest("Updated", tent.Id, systemId: system.Id);
        request.SystemId = null;

        var result = _controller.Update(growId, request);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<GrowDetailDto>(ok.Value);
        Assert.Equal(system.Id, dto.SystemId);
        Assert.Equal(system.Id, _growRepository.GetGrow(growId)!.SystemId);
    }

    [Fact]
    public void Update_LegacyGrowWithoutHydroSetup_CanRemainLegacy()
    {
        var growId = _growRepository.CreateGrow(new GrowRun
        {
            Name = "Legacy",
            StartDate = new DateTime(2026, 1, 1),
            Status = GrowStatus.Planning,
            HydroStyle = HydroStyle.RDWC
        });
        var request = NewGrowRequest("Legacy Updated");
        request.SystemId = null;
        request.TentId = null;

        var result = _controller.Update(growId, request);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<GrowDetailDto>(ok.Value);
        Assert.Null(dto.SystemId);
        Assert.Null(dto.TentId);
    }

    [Fact]
    public void Deviations_MissingGrow_Returns404()
    {
        var result = _controller.Deviations(9999).Result;

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var error = Assert.IsType<ApiError>(notFound.Value);
        Assert.Equal("grow_not_found", error.Code);
    }

    [Fact]
    public void Deviations_WithHydroMeasurement_ReturnsStructuredDeviations()
    {
        var growId = _growRepository.CreateGrow(new GrowRun
        {
            Name = "Hydro Grow",
            StartDate = new DateTime(2026, 1, 1),
            MediumType = MediumType.Hydro,
            IrrigationType = IrrigationType.ActiveHydro,
            HydroStyle = HydroStyle.RDWC,
            Status = GrowStatus.Running
        });
        _growRepository.CreateMeasurement(new Measurement
        {
            GrowId = growId,
            Stage = GrowStage.Veg,
            TakenAt = DateTime.UtcNow,
            ReservoirWaterTempC = 25
        });

        var result = _controller.Deviations(growId).Result;

        var ok = Assert.IsType<OkObjectResult>(result);
        var deviations = Assert.IsAssignableFrom<IReadOnlyList<GrowDeviation>>(ok.Value);
        var deviation = Assert.Single(deviations);
        Assert.Equal(DeviationMetric.WaterTemp, deviation.Metric);
        Assert.Equal(DeviationSeverity.Critical, deviation.Severity);
    }

    [Fact]
    public void TreatmentRecommendations_MissingGrow_Returns404()
    {
        var result = _controller.TreatmentRecommendations(9999).Result;

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var error = Assert.IsType<ApiError>(notFound.Value);
        Assert.Equal("grow_not_found", error.Code);
    }

    [Fact]
    public void TreatmentRecommendations_WithMatchingDeviation_ReturnsRecommendations()
    {
        var growId = _growRepository.CreateGrow(new GrowRun
        {
            Name = "Hydro Grow",
            StartDate = new DateTime(2026, 1, 1),
            MediumType = MediumType.Hydro,
            IrrigationType = IrrigationType.ActiveHydro,
            HydroStyle = HydroStyle.RDWC,
            Status = GrowStatus.Running
        });
        _growRepository.CreateMeasurement(new Measurement
        {
            GrowId = growId,
            Stage = GrowStage.Veg,
            TakenAt = DateTime.UtcNow,
            ReservoirPh = 6.7
        });

        var result = _controller.TreatmentRecommendations(growId).Result;

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<GrowTreatmentRecommendationDto>(ok.Value);
        Assert.Contains(dto.Recommendations, item => item.TreatmentId == "ph-correction-down");
    }

    private Tent DefaultTent()
        => _growRepository.GetTents().Single();

    private Setup CreateSetup(int tentId, SetupType setupType)
        => _growRepository.CreateSetup(new Setup
        {
            TentId = tentId,
            Name = $"{setupType} Setup",
            SetupType = setupType,
            Status = SetupStatus.Active
        });

    private GrowSystem CreateHydroSetup(int tentId, HydroStyle hydroStyle, bool hasChiller = false)
        => _growRepository.CreateHydroSetup(new GrowSystem
        {
            TentId = tentId,
            Name = $"{hydroStyle} System",
            HydroStyle = hydroStyle.ToString(),
            PotCount = hydroStyle == HydroStyle.RDWC ? 4 : 1,
            PotSizeLiters = hydroStyle == HydroStyle.RDWC ? 19 : 25,
            ReservoirLiters = hydroStyle == HydroStyle.RDWC ? 60 : 0,
            LayoutType = hydroStyle == HydroStyle.RDWC ? HydroSetupLayoutType.Grid2x2 : HydroSetupLayoutType.SingleBucket,
            ReservoirPosition = hydroStyle == HydroStyle.RDWC ? ReservoirPosition.External : ReservoirPosition.None,
            HasChiller = hasChiller,
            Status = HydroSetupStatus.Active
        });

    private GrowUpsertRequest NewGrowRequest(string name, int? tentId = null, int? setupId = null, int? systemId = null)
    {
        var resolvedTentId = tentId ?? DefaultTent().Id;
        var resolvedSystemId = systemId ?? CreateHydroSetup(resolvedTentId, HydroStyle.RDWC).Id;
        return new GrowUpsertRequest
        {
            Name = name,
            TentId = resolvedTentId,
            SystemId = resolvedSystemId,
            SetupId = setupId,
            HydroStyle = HydroStyle.RDWC,
            StartDate = "2026-01-01",
            Status = GrowStatus.Planning,
            Environment = GrowEnvironment.Indoor
        };
    }

    private DeviationAnalyzerService CreateDeviationAnalyzer()
    {
        var loader = new KnowledgeBaseLoader(_paths, NullLogger<KnowledgeBaseLoader>.Instance);
        loader.Initialize();
        return new DeviationAnalyzerService(new TargetValueService(loader));
    }

    private TreatmentRecommender CreateTreatmentRecommender()
    {
        var loader = new KnowledgeBaseLoader(_paths, NullLogger<KnowledgeBaseLoader>.Instance);
        loader.Initialize();
        return new TreatmentRecommender(loader);
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

    private static void AssertSetupValidationError(ActionResult? result)
    {
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = Assert.IsType<ApiError>(badRequest.Value);
        Assert.Equal("validation_failed", error.Code);
        Assert.Contains(nameof(GrowUpsertRequest.SetupId), error.FieldErrors!.Keys);
    }

    private static void AssertHydroStyleValidationError(ActionResult? result)
    {
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = Assert.IsType<ApiError>(badRequest.Value);
        Assert.Equal("validation_failed", error.Code);
        Assert.Contains(nameof(GrowUpsertRequest.HydroStyle), error.FieldErrors!.Keys);
    }

    private static void AssertSystemValidationError(ActionResult? result)
    {
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = Assert.IsType<ApiError>(badRequest.Value);
        Assert.Equal("validation_failed", error.Code);
        Assert.Contains(nameof(GrowUpsertRequest.SystemId), error.FieldErrors!.Keys);
    }
}
