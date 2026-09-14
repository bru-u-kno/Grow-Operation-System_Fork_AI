using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Api;

public sealed class SettingsApiControllerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AppPaths _paths;
    private readonly GrowRepository _repository;
    private readonly SettingsApiController _controller;

    public SettingsApiControllerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"grow-test-{Guid.NewGuid():N}.db");
        Environment.SetEnvironmentVariable("GROWDIARY_DB_PATH", _dbPath);
        _paths = new AppPaths(Path.GetTempPath());
        GrowDiary.Web.Tests.TestDatabase.InitializeWithDefaultTent(_paths);
        _repository = new GrowRepository(_paths);
        _controller = new SettingsApiController(
            _repository,
            new GrowDiary.Web.Services.TentSensorHardwareSyncService(
                new HardwareRepository(_paths),
                NullLogger<GrowDiary.Web.Services.TentSensorHardwareSyncService>.Instance),
            // Ohne hinterlegten Dienst am Zelt ruft der Controller Home Assistant
            // gar nicht erst — der Client bleibt im Test unbenutzt.
            new GrowDiary.Web.Services.HomeAssistantService(
                new GrowDiary.Web.Tests.TestFakes.StubHttpClientFactory(new HttpClientHandler()),
                NullLogger<GrowDiary.Web.Services.HomeAssistantService>.Instance),
            NullLogger<SettingsApiController>.Instance);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("GROWDIARY_DB_PATH", null);
        try { File.Delete(_dbPath); } catch { }
        try { File.Delete(_dbPath + "-shm"); } catch { }
        try { File.Delete(_dbPath + "-wal"); } catch { }
    }

    [Fact]
    public void HomeAssistantSettingsAlias_UsesApiHomeAssistantSettingsRoute()
    {
        var method = typeof(SettingsApiController).GetMethod(nameof(SettingsApiController.HomeAssistantSettings));
        var attribute = Assert.Single(method!.GetCustomAttributes(typeof(HttpGetAttribute), inherit: false));

        Assert.Equal("~/api/home-assistant/settings", Assert.IsType<HttpGetAttribute>(attribute).Template);
    }

    [Fact]
    public void CreateTent_WithValidRequest_CreatesTentAndAppearsInList()
    {
        var result = _controller.CreateTent(new CreateTentRequest
        {
            Name = "Mutter Zelt 1",
            TentType = TentType.Mother.ToString(),
            Notes = "Mutterpflanzen und Stecklinge",
            DisplayOrder = 2
        });

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<TentDto>(created.Value);
        Assert.True(dto.Id > 0);
        Assert.Equal("Mutter Zelt 1", dto.Name);
        Assert.Equal(TentType.Mother.ToString(), dto.TentType);

        var tents = Assert.IsAssignableFrom<IReadOnlyList<TentDto>>(Assert.IsType<OkObjectResult>(_controller.Tents().Result).Value);
        Assert.Contains(tents, tent => tent.Id == dto.Id && tent.Name == "Mutter Zelt 1");
    }

    [Fact]
    public void CreateTent_WithBlankName_ReturnsValidationError()
    {
        var result = _controller.CreateTent(new CreateTentRequest
        {
            Name = " ",
            TentType = TentType.Production.ToString()
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var error = Assert.IsType<ApiError>(badRequest.Value);
        Assert.Equal("validation_failed", error.Code);
        Assert.Contains(nameof(CreateTentRequest.Name), error.FieldErrors!.Keys);
    }

    [Fact]
    public void CreateTent_WithInvalidTentType_ReturnsValidationError()
    {
        var result = _controller.CreateTent(new CreateTentRequest
        {
            Name = "Testzelt",
            TentType = "FlowerOnly"
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var error = Assert.IsType<ApiError>(badRequest.Value);
        Assert.Equal("validation_failed", error.Code);
        Assert.Contains(nameof(CreateTentRequest.TentType), error.FieldErrors!.Keys);
    }

    [Fact]
    public void DeleteTent_WithoutBlockingDependencies_RemovesTent()
    {
        var created = _repository.CreateTent(new Tent
        {
            Name = "Leeres Testzelt",
            TentType = TentType.Production,
            Status = TentStatus.Active
        });

        var result = _controller.DeleteTent(created.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(_repository.GetTent(created.Id));
    }

    [Fact]
    public void DeleteTent_WithActiveGrow_ReturnsStructuredDependencies()
    {
        var tent = _repository.CreateTent(new Tent
        {
            Name = "Blockiertes Testzelt",
            TentType = TentType.Production,
            Status = TentStatus.Active
        });
        var growId = _repository.CreateGrow(new GrowRun
        {
            Name = "Aktiver Blocker",
            TentId = tent.Id,
            StartDate = new DateTime(2026, 5, 1),
            Status = GrowStatus.Running
        });

        var result = _controller.DeleteTent(tent.Id);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var error = Assert.IsType<TentDependencyError>(conflict.Value);
        Assert.Equal("tent_has_active_dependencies", error.Code);
        Assert.Contains(error.Dependencies.ActiveGrows, grow => grow.Id == growId && grow.Name == "Aktiver Blocker");
        Assert.Empty(error.Dependencies.ArchivedGrows);
        Assert.Empty(error.Dependencies.Sensors);
    }

    [Fact]
    public void DeleteTent_WithOnlyArchivedGrow_DetachesHistoricalGrowAndRemovesTent()
    {
        var tent = _repository.CreateTent(new Tent
        {
            Name = "Historisches Testzelt",
            TentType = TentType.Production,
            Status = TentStatus.Active
        });
        var growId = _repository.CreateGrow(new GrowRun
        {
            Name = "Archivierter Grow",
            TentId = tent.Id,
            StartDate = new DateTime(2025, 1, 1),
            Status = GrowStatus.Completed
        });

        var result = _controller.DeleteTent(tent.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(_repository.GetTent(tent.Id));
        Assert.Null(_repository.GetGrow(growId)!.TentId);
    }
    [Fact]
    public void CreateTent_WithDetailedRequest_PersistsAllTentDetailsAndSensors()
    {
        var result = _controller.CreateTent(new CreateTentRequest
        {
            Name = "Blütezelt 120",
            Kind = "Grow Tent",
            TentType = TentType.Production.ToString(),
            Status = TentStatus.Active.ToString(),
            Notes = "RDWC Blüte",
            DisplayOrder = 3,
            AccentColor = "#4ecb5b",
            WidthCm = 120,
            DepthCm = 120,
            TentHeightCm = 200,
            LightType = "LED Bar",
            LightWatt = 720,
            LightController = LightControllerType.AcInfinityPro69.ToString(),
            LightControllerEntityId = "switch.light_controller",
            ExhaustFanCount = 1,
            ExhaustM3h = 680,
            CirculationFanCount = 2,
            HvacController = HvacControllerType.Manual.ToString(),
            HvacControllerEntityId = "climate.tent",
            Co2Available = true,
            CameraEntityId = "camera.bloom",
            Sensors =
            [
                new UpdateTentSensorRequest
                {
                    MetricType = SensorMetricType.AirTemperature.ToString(),
                    HaEntityId = "sensor.bloom_temp",
                    DisplayLabel = "Temperatur Blüte",
                    IsActive = true
                }
            ]
        });

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<TentDto>(created.Value);

        Assert.Equal("Blütezelt 120", dto.Name);
        Assert.Equal(TentType.Production.ToString(), dto.TentType);
        Assert.Equal(TentStatus.Active.ToString(), dto.Status);
        Assert.Equal("RDWC Blüte", dto.Notes);
        Assert.Equal(3, dto.DisplayOrder);
        Assert.Equal("#4ecb5b", dto.AccentColor);
        Assert.Equal(120, dto.WidthCm);
        Assert.Equal(120, dto.DepthCm);
        Assert.Equal(200, dto.TentHeightCm);
        Assert.Equal("LED Bar", dto.LightType);
        Assert.Equal(720, dto.LightWatt);
        Assert.Equal(LightControllerType.AcInfinityPro69.ToString(), dto.LightController);
        Assert.Equal("switch.light_controller", dto.LightControllerEntityId);
        Assert.Equal(1, dto.ExhaustFanCount);
        Assert.Equal(680, dto.ExhaustM3h);
        Assert.Equal(2, dto.CirculationFanCount);
        Assert.Equal(HvacControllerType.Manual.ToString(), dto.HvacController);
        Assert.Equal("climate.tent", dto.HvacControllerEntityId);
        Assert.True(dto.Co2Available);
        Assert.Equal("camera.bloom", dto.CameraEntityId);
        Assert.Single(dto.Sensors);
        Assert.Equal("sensor.bloom_temp", dto.Sensors[0].HaEntityId);
    }

    [Fact]
    public void GetTent_ReturnsSingleTentWithDetails()
    {
        var createResult = _controller.CreateTent(new CreateTentRequest
        {
            Name = "Anzuchtbox",
            TentType = TentType.Propagation.ToString(),
            WidthCm = 80,
            DepthCm = 60,
            TentHeightCm = 120
        });
        var created = Assert.IsType<TentDto>(Assert.IsType<CreatedAtActionResult>(createResult.Result).Value);

        var result = _controller.Tent(created.Id);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<TentDto>(ok.Value);
        Assert.Equal(created.Id, dto.Id);
        Assert.Equal("Anzuchtbox", dto.Name);
        Assert.Equal(TentType.Propagation.ToString(), dto.TentType);
        Assert.Equal(80, dto.WidthCm);
        Assert.Equal(60, dto.DepthCm);
        Assert.Equal(120, dto.TentHeightCm);
    }

    [Fact]
    public async Task UpdateTent_WithDetailedRequest_PersistsAllTentDetails()
    {
        var created = _repository.CreateTent("Update Zelt");

        var result = await _controller.SaveTent(created.Id, new UpdateTentRequest
        {
            Name = "Update Blüte",
            Kind = "Grow Tent",
            TentType = TentType.Production.ToString(),
            Status = TentStatus.Active.ToString(),
            Notes = "aktualisiert",
            DisplayOrder = 7,
            AccentColor = "#111111",
            WidthCm = 150,
            DepthCm = 150,
            TentHeightCm = 220,
            LightType = "Quantum Board",
            LightWatt = 500,
            LightController = LightControllerType.GenericRelay.ToString(),
            LightControllerEntityId = "switch.light",
            ExhaustFanCount = 2,
            ExhaustM3h = 800,
            CirculationFanCount = 3,
            HvacController = HvacControllerType.Other.ToString(),
            HvacControllerEntityId = "climate.other",
            Co2Available = true,
            CameraEntityId = "camera.updated",
            Sensors =
            [
                new UpdateTentSensorRequest
                {
                    MetricType = SensorMetricType.Humidity.ToString(),
                    HaEntityId = "sensor.humidity",
                    DisplayLabel = "RH",
                    IsActive = true
                }
            ]
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<TentDto>(ok.Value);
        Assert.Equal("Update Blüte", dto.Name);
        Assert.Equal(150, dto.WidthCm);
        Assert.Equal(500, dto.LightWatt);
        Assert.Equal("camera.updated", dto.CameraEntityId);
        Assert.Single(dto.Sensors);
        Assert.Equal(SensorMetricType.Humidity.ToString(), dto.Sensors[0].MetricType);
    }

    [Fact]
    public void CreateTent_WithInvalidTechnicalFields_ReturnsValidationError()
    {
        var result = _controller.CreateTent(new CreateTentRequest
        {
            Name = "Ungültig",
            TentType = TentType.Production.ToString(),
            WidthCm = 0,
            DepthCm = -10,
            LightWatt = -1,
            LightController = "BadController",
            HvacController = "BadHvac",
            Sensors =
            [
                new UpdateTentSensorRequest
                {
                    MetricType = "BadMetric",
                    HaEntityId = "sensor.bad",
                    IsActive = true
                },
                new UpdateTentSensorRequest
                {
                    MetricType = SensorMetricType.Vpd.ToString(),
                    HaEntityId = " ",
                    IsActive = true
                }
            ]
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var error = Assert.IsType<ApiError>(badRequest.Value);
        Assert.Equal("validation_failed", error.Code);
        Assert.Contains(nameof(CreateTentRequest.WidthCm), error.FieldErrors!.Keys);
        Assert.Contains(nameof(CreateTentRequest.DepthCm), error.FieldErrors!.Keys);
        Assert.Contains(nameof(CreateTentRequest.LightWatt), error.FieldErrors!.Keys);
        Assert.Contains(nameof(CreateTentRequest.LightController), error.FieldErrors!.Keys);
        Assert.Contains(nameof(CreateTentRequest.HvacController), error.FieldErrors!.Keys);
        Assert.Contains("Sensors[0].MetricType", error.FieldErrors!.Keys);
        Assert.Contains("Sensors[1].HaEntityId", error.FieldErrors!.Keys);
    }

    [Fact]
    public void DeleteTent_WithoutDependencies_DeletesTent()
    {
        var created = _repository.CreateTent("Leeres Zelt");

        var result = _controller.DeleteTent(created.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(_repository.GetTent(created.Id));
    }

    [Fact]
    public void DeleteTent_WithDependencies_ReturnsConflictAndKeepsTent()
    {
        var created = _repository.CreateTent("Zelt mit System");
        _repository.CreateHydroSetup(new GrowSystem
        {
            TentId = created.Id,
            Name = "RDWC Test",
            HydroStyle = HydroStyle.RDWC.ToString(),
            PotCount = 4,
            PotSizeLiters = 19,
            ReservoirLiters = 60,
            LayoutType = HydroSetupLayoutType.Grid2x2,
            ReservoirPosition = ReservoirPosition.External
        });

        var result = _controller.DeleteTent(created.Id);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var error = Assert.IsType<TentDependencyError>(conflict.Value);
        Assert.Equal("tent_has_active_dependencies", error.Code);
        Assert.Contains(error.Dependencies.HydroSetups, setup => setup.Name == "RDWC Test");
        Assert.Contains(_repository.GetTents(), tent => tent.Id == created.Id && tent.Status == TentStatus.Active);
    }

}
