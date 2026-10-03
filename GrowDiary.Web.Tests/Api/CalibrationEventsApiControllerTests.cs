using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Api;

public sealed class CalibrationEventsApiControllerTests : IDisposable
{
    private readonly string _contentRoot;
    private readonly AppPaths _paths;
    private readonly GrowRepository _repository;
    private readonly CalibrationEventsApiController _controller;

    public CalibrationEventsApiControllerTests()
    {
        _contentRoot = Path.Combine(Path.GetTempPath(), $"grow-calibration-api-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_contentRoot);
        _paths = new AppPaths(_contentRoot);
        GrowDiary.Web.Tests.TestDatabase.InitializeWithDefaultTent(_paths);
        _repository = new GrowRepository(_paths);
        _controller = new CalibrationEventsApiController(_repository);
    }

    public void Dispose()
    {
        try { Directory.Delete(_contentRoot, recursive: true); } catch { }
    }

    [Fact]
    public void Api_CreateListsGetsAndUpdatesCalibrationEvent()
    {
        var hardware = CreateHardware();
        var dueAt = Utc(2026, 6, 25);

        var create = _controller.Create(new CreateCalibrationEventRequest
        {
            HardwareItemId = hardware.Id,
            CalibrationType = CalibrationEventType.Ph,
            Status = CalibrationEventStatus.Planned,
            Result = CalibrationResult.Unknown,
            Title = "pH 7.00",
            ReferenceSolution = "pH 7.00",
            ReferenceValue = 7.00m,
            BeforeValue = 6.90m,
            TemperatureC = 22.0m,
            DueAtUtc = dueAt,
            Notes = "Vorbereitet"
        });
        var created = Assert.IsType<CreatedAtActionResult>(create.Result);
        var dto = Assert.IsType<CalibrationEventDto>(created.Value);
        Assert.Equal(hardware.Id, dto.HardwareItemId);

        var detail = Assert.IsType<OkObjectResult>(_controller.Detail(dto.Id).Result);
        Assert.Equal(dto.Id, Assert.IsType<CalibrationEventDto>(detail.Value).Id);

        var listByHardware = Assert.IsType<OkObjectResult>(_controller.List(hardware.Id, null).Result);
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<CalibrationEventDto>>(listByHardware.Value));

        var dueList = Assert.IsType<OkObjectResult>(_controller.List(null, Utc(2026, 6, 26)).Result);
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<CalibrationEventDto>>(dueList.Value));

        var update = _controller.Update(dto.Id, new UpdateCalibrationEventRequest
        {
            HardwareItemId = hardware.Id,
            CalibrationType = CalibrationEventType.Ph,
            Status = CalibrationEventStatus.Completed,
            Result = CalibrationResult.Passed,
            Title = "pH 7.00 erledigt",
            ReferenceSolution = "pH 7.00",
            ReferenceValue = 7.00m,
            BeforeValue = 6.90m,
            AfterValue = 7.00m,
            TemperatureC = 22.0m,
            DueAtUtc = dueAt,
            PerformedAtUtc = Utc(2026, 6, 24),
            Notes = "OK"
        });
        var ok = Assert.IsType<OkObjectResult>(update.Result);
        var updated = Assert.IsType<CalibrationEventDto>(ok.Value);
        Assert.Equal(CalibrationEventStatus.Completed, updated.Status);
        Assert.Equal(CalibrationResult.Passed, updated.Result);
        Assert.Equal(7.00m, updated.AfterValue);
    }

    [Fact]
    public void Api_ReturnsNotFoundForMissingCalibrationEvent()
    {
        var result = _controller.Detail(9999).Result;

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var error = Assert.IsType<ApiError>(notFound.Value);
        Assert.Equal("calibration_event_not_found", error.Code);
    }

    [Fact]
    public void Api_RejectsInvalidReferencesEnumsTemperatureAndDates()
    {
        var missingHardware = _controller.Create(new CreateCalibrationEventRequest
        {
            HardwareItemId = 9999,
            CalibrationType = CalibrationEventType.Ph,
            Status = CalibrationEventStatus.Planned,
            Result = CalibrationResult.Unknown,
            Title = "Bad"
        });
        Assert.Contains(nameof(CreateCalibrationEventRequest.HardwareItemId), AssertValidationError(missingHardware.Result).FieldErrors!.Keys);

        var hardware = CreateHardware();

        _controller.ModelState.Clear();
        var badType = _controller.Create(new CreateCalibrationEventRequest
        {
            HardwareItemId = hardware.Id,
            CalibrationType = (CalibrationEventType)99,
            Status = CalibrationEventStatus.Planned,
            Result = CalibrationResult.Unknown,
            Title = "Bad"
        });
        Assert.Contains(nameof(CreateCalibrationEventRequest.CalibrationType), AssertValidationError(badType.Result).FieldErrors!.Keys);

        _controller.ModelState.Clear();
        var badStatus = _controller.Create(new CreateCalibrationEventRequest
        {
            HardwareItemId = hardware.Id,
            CalibrationType = CalibrationEventType.Ph,
            Status = (CalibrationEventStatus)99,
            Result = CalibrationResult.Unknown,
            Title = "Bad"
        });
        Assert.Contains(nameof(CreateCalibrationEventRequest.Status), AssertValidationError(badStatus.Result).FieldErrors!.Keys);

        _controller.ModelState.Clear();
        var badResult = _controller.Create(new CreateCalibrationEventRequest
        {
            HardwareItemId = hardware.Id,
            CalibrationType = CalibrationEventType.Ph,
            Status = CalibrationEventStatus.Planned,
            Result = (CalibrationResult)99,
            Title = "Bad"
        });
        Assert.Contains(nameof(CreateCalibrationEventRequest.Result), AssertValidationError(badResult.Result).FieldErrors!.Keys);

        _controller.ModelState.Clear();
        var badTemperature = _controller.Create(new CreateCalibrationEventRequest
        {
            HardwareItemId = hardware.Id,
            CalibrationType = CalibrationEventType.Ph,
            Status = CalibrationEventStatus.Completed,
            Result = CalibrationResult.Passed,
            Title = "Bad Temp",
            TemperatureC = -20m
        });
        Assert.Contains(nameof(CreateCalibrationEventRequest.TemperatureC), AssertValidationError(badTemperature.Result).FieldErrors!.Keys);

        _controller.ModelState.Clear();
        var badDate = _controller.Create(new CreateCalibrationEventRequest
        {
            HardwareItemId = hardware.Id,
            CalibrationType = CalibrationEventType.Ph,
            Status = CalibrationEventStatus.Completed,
            Result = CalibrationResult.Passed,
            Title = "Bad Date",
            PerformedAtUtc = Utc(2026, 6, 20),
            NextDueAtUtc = Utc(2026, 6, 19)
        });
        Assert.Contains(nameof(CreateCalibrationEventRequest.NextDueAtUtc), AssertValidationError(badDate.Result).FieldErrors!.Keys);
    }

    /// <summary>
    /// Ein pH-Puffer außerhalb von 0–14 oder eine EC-Lösung in der falschen
    /// Einheit ist ein Tippfehler und wird nicht eingetragen (offene Punkte
    /// 03.10.2026, B10).
    /// </summary>
    /// <remarks>
    /// Puffer „70" statt „7,0" ergibt aus den Werten unten eine Steilheit von
    /// 4,1 % — die Sonde gälte als fällig, obwohl sie 91 % spreizt.
    /// </remarks>
    [Theory]
    [InlineData(CalibrationEventType.Ph, """[{"sollwert":4.01,"vorher":4.1},{"sollwert":70,"vorher":6.82}]""", false)]
    [InlineData(CalibrationEventType.Ph, """[{"sollwert":4.01,"vorher":-0.5}]""", false)]
    [InlineData(CalibrationEventType.Ph, """[{"sollwert":4.01,"vorher":4.1},{"sollwert":7.0,"vorher":6.82,"nachher":7.0}]""", true)]
    // EC: 12,88 mS/cm ist eine übliche Lösung über der Messgrenze 10 — angenommen,
    // ebenso die stärkste übliche (1 mol/l KCl, 111,3 mS/cm).
    [InlineData(CalibrationEventType.Ec, """[{"sollwert":12.88,"vorher":12.5}]""", true)]
    [InlineData(CalibrationEventType.Ec, """[{"sollwert":111.3,"vorher":110.9}]""", true)]
    // µS/cm im mS/cm-Feld und Negatives: abgelehnt.
    [InlineData(CalibrationEventType.Ec, """[{"sollwert":1413,"vorher":1.39}]""", false)]
    [InlineData(CalibrationEventType.Ec, """[{"sollwert":1.413,"vorher":-0.1}]""", false)]
    // ORP und Sauerstoff bleiben offen (Sauerstoff oft in %, 100 %).
    [InlineData(CalibrationEventType.Do, """[{"sollwert":100,"vorher":96}]""", true)]
    public void Complete_PrueftKalibrierpunkte(CalibrationEventType art, string punkte, bool angenommen)
    {
        var hardware = CreateHardware();
        var create = Assert.IsType<CreatedAtActionResult>(_controller.Create(new CreateCalibrationEventRequest
        {
            HardwareItemId = hardware.Id,
            CalibrationType = art,
            Status = CalibrationEventStatus.Planned,
            Result = CalibrationResult.Unknown,
            Title = "Zweipunkt",
            DueAtUtc = Utc(2026, 10, 4),
        }).Result);
        var id = Assert.IsType<CalibrationEventDto>(create.Value).Id;

        var ergebnis = _controller.Complete(id, new CompleteCalibrationEventRequest { PointsJson = punkte });

        if (angenommen)
        {
            Assert.IsType<OkObjectResult>(ergebnis.Result);
            return;
        }

        AssertValidationError(ergebnis.Result);
        var gespeichert = Assert.IsType<CalibrationEventDto>(Assert.IsType<OkObjectResult>(_controller.Detail(id).Result).Value);
        Assert.Equal(CalibrationEventStatus.Planned, gespeichert.Status);
    }

    /// <summary>Die Meldung nennt die Einheit und den gemeinten Wert.</summary>
    [Fact]
    public void Complete_NenntBeiMikrosiemensDenGemeintenWert()
    {
        var hardware = CreateHardware();
        var create = Assert.IsType<CreatedAtActionResult>(_controller.Create(new CreateCalibrationEventRequest
        {
            HardwareItemId = hardware.Id, CalibrationType = CalibrationEventType.Ec,
            Status = CalibrationEventStatus.Planned, Result = CalibrationResult.Unknown,
            Title = "EC 1413", DueAtUtc = Utc(2026, 10, 4),
        }).Result);
        var id = Assert.IsType<CalibrationEventDto>(create.Value).Id;

        var fehler = AssertValidationError(_controller.Complete(id,
            new CompleteCalibrationEventRequest { PointsJson = """[{"sollwert":1413}]""" }).Result);

        var satz = Assert.Single(fehler.FieldErrors!.SelectMany(f => f.Value));
        Assert.Contains("µS/cm", satz);
        Assert.Contains("1,413 eintragen", satz);
    }

    /// <summary>
    /// Vorher/Nachher sind Anzeigen der Sonde, keine Lösungen — ihr Satz sagt
    /// nicht „gibt es als Kalibrierlösung nicht" (Prüfer, 03.10.2026).
    /// </summary>
    [Fact]
    public void Complete_NenntBeiDerSondenanzeigeKeineLoesung()
    {
        var hardware = CreateHardware();
        var create = Assert.IsType<CreatedAtActionResult>(_controller.Create(new CreateCalibrationEventRequest
        {
            HardwareItemId = hardware.Id, CalibrationType = CalibrationEventType.Ec,
            Status = CalibrationEventStatus.Planned, Result = CalibrationResult.Unknown,
            Title = "EC 1413", DueAtUtc = Utc(2026, 10, 4),
        }).Result);
        var id = Assert.IsType<CalibrationEventDto>(create.Value).Id;

        var fehler = AssertValidationError(_controller.Complete(id,
            new CompleteCalibrationEventRequest { PointsJson = """[{"sollwert":1.413,"vorher":1390}]""" }).Result);

        var satz = Assert.Single(fehler.FieldErrors!.SelectMany(f => f.Value));
        Assert.StartsWith("Vorher 1", satz);
        Assert.DoesNotContain("Kalibrierlösung nicht", satz);
        Assert.Contains("1,39 eintragen", satz);
    }

    private HardwareItem CreateHardware()
    {
        var tent = _repository.GetTents().Single();
        return _repository.CreateHardwareItem(new HardwareItem
        {
            Name = "pH Sonde",
            Category = "Sensor",
            Status = HardwareItemStatus.Active,
            Criticality = HardwareItemCriticality.Medium,
            TentId = tent.Id
        });
    }

    private static ApiError AssertValidationError(ActionResult? result)
    {
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = Assert.IsType<ApiError>(badRequest.Value);
        Assert.Equal("validation_failed", error.Code);
        return error;
    }

    private static DateTime Utc(int year, int month, int day)
        => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);
}
