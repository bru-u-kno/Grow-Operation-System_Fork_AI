using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Mapping;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

[ApiController]
[Route("api/calibration-events")]
[Produces("application/json")]
public sealed class CalibrationEventsApiController : ApiControllerBase
{
    private readonly GrowRepository _repository;

    public CalibrationEventsApiController(GrowRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CalibrationEventDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public ActionResult<IReadOnlyList<CalibrationEventDto>> List([FromQuery] int? hardwareItemId = null, [FromQuery] DateTime? dueBeforeUtc = null)
    {
        if (hardwareItemId.HasValue && _repository.GetHardwareItem(hardwareItemId.Value) is null)
        {
            ModelState.AddModelError(nameof(hardwareItemId), $"HardwareItem mit Id {hardwareItemId.Value} existiert nicht.");
            return ValidationError();
        }

        var items = hardwareItemId.HasValue
            ? _repository.GetCalibrationEventsByHardwareItem(hardwareItemId.Value)
            : dueBeforeUtc.HasValue
                ? _repository.GetDueCalibrationEvents(dueBeforeUtc.Value)
                : _repository.GetCalibrationEvents();

        return Ok(items.Select(item => item.ToDto()).ToList());
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(CalibrationEventDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<CalibrationEventDto> Detail(int id)
    {
        var item = _repository.GetCalibrationEvent(id);
        return item is null
            ? NotFoundError("calibration_event_not_found", $"CalibrationEvent mit Id {id} existiert nicht.")
            : Ok(item.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(CalibrationEventDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public ActionResult<CalibrationEventDto> Create([FromBody] CreateCalibrationEventRequest request)
    {
        Validate(request.HardwareItemId, request.CalibrationType, request.Status, request.Result, request.Title, request.TemperatureC, request.PerformedAtUtc, request.NextDueAtUtc);
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var item = _repository.CreateCalibrationEvent(request.ToModel());
        return CreatedAtAction(nameof(Detail), new { id = item.Id }, item.ToDto());
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(CalibrationEventDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<CalibrationEventDto> Update(int id, [FromBody] UpdateCalibrationEventRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var item = _repository.GetCalibrationEvent(id);
        if (item is null)
        {
            return NotFoundError("calibration_event_not_found", $"CalibrationEvent mit Id {id} existiert nicht.");
        }

        Validate(request.HardwareItemId, request.CalibrationType, request.Status, request.Result, request.Title, request.TemperatureC, request.PerformedAtUtc, request.NextDueAtUtc);
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        request.ApplyTo(item);
        _repository.UpdateCalibrationEvent(item);
        return Ok(_repository.GetCalibrationEvent(id)!.ToDto());
    }

    /// <summary>„Habe kalibriert" — der Termin ist erledigt, der naechste steht.</summary>
    /// <remarks>
    /// Eigener Endpunkt statt eines PUT mit Status=Completed: das ist die
    /// Handlung, die der Nutzer wirklich ausfuehrt, und nur hier wird der
    /// Folgetermin geplant. Ein generisches Update darf das nicht heimlich tun.
    /// </remarks>
    [HttpPost("{id:int}/complete")]
    [ProducesResponseType(typeof(CalibrationEventDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<CalibrationEventDto> Complete(int id, [FromBody] CompleteCalibrationEventRequest request)
    {
        var item = _repository.GetCalibrationEvent(id);
        if (item is null)
        {
            return NotFoundError("calibration_event_not_found", $"CalibrationEvent mit Id {id} existiert nicht.");
        }

        if (request.TemperatureC is < -10m or > 60m)
        {
            ModelState.AddModelError(nameof(request.TemperatureC), "TemperatureC muss zwischen -10 und 60 liegen.");
            return ValidationError();
        }

        item.Status = request.Failed ? CalibrationEventStatus.Failed : CalibrationEventStatus.Completed;
        item.Result = request.Failed ? CalibrationResult.Failed : CalibrationResult.Passed;
        item.PerformedAtUtc = request.PerformedAtUtc?.ToUniversalTime() ?? DateTime.UtcNow;
        // Ein neuer Folgetermin wird gerechnet, nicht uebernommen: das alte
        // NextDueAtUtc gehoerte zur vorherigen Runde.
        item.NextDueAtUtc = null;
        if (!string.IsNullOrWhiteSpace(request.ReferenceSolution)) item.ReferenceSolution = request.ReferenceSolution.Trim();
        if (request.ReferenceValue.HasValue) item.ReferenceValue = request.ReferenceValue;
        if (request.BeforeValue.HasValue) item.BeforeValue = request.BeforeValue;
        if (request.AfterValue.HasValue) item.AfterValue = request.AfterValue;
        if (request.TemperatureC.HasValue) item.TemperatureC = request.TemperatureC;

        /* Die einzelnen Messpunkte — und daraus die Zusammenfassung.
           Eine pH-Sonde wird gegen 4 UND 7 abgeglichen, oft auch 10. Die
           Einzelfelder darueber bleiben gefuellt, damit aeltere Auswertungen
           weiterrechnen: dasselbe Muster wie die Erntegewichte je Pflanze. */
        if (!string.IsNullOrWhiteSpace(request.PointsJson))
        {
            var punkte = Kalibrierpunkte.Lesen(request.PointsJson);

            /* Ein pH-Puffer ausserhalb 0–14 oder eine EC-Lösung in der falschen
               Einheit („1413" statt „1,413" mS/cm) verdirbt die Steilheit, an der
               die Fälligkeit der Sonde hängt. Die Regeln stehen an
               Kalibrierpunkte.Pruefen (offene Punkte 03.10.2026, B10). */
            foreach (var (feld, meldung) in Kalibrierpunkte.Pruefen(item.CalibrationType, punkte))
            {
                ModelState.AddModelError(feld, meldung);
            }
            if (!ModelState.IsValid) return ValidationError();

            item.PointsJson = Kalibrierpunkte.Schreiben(punkte);

            // Der letzte Punkt fuehrt die Zusammenfassung — bei pH 4/7 also 7,00.
            if (punkte.Count > 0)
            {
                var letzter = punkte[^1];
                if (!string.IsNullOrWhiteSpace(letzter.Loesung)) item.ReferenceSolution = letzter.Loesung.Trim();
                if (letzter.Sollwert is { } soll) item.ReferenceValue = (decimal)soll;
                if (letzter.Vorher is { } vor) item.BeforeValue = (decimal)vor;
                if (letzter.Nachher is { } nach) item.AfterValue = (decimal)nach;
            }
        }
        if (!string.IsNullOrWhiteSpace(request.Notes)) item.Notes = request.Notes.Trim();

        return Ok(_repository.CompleteCalibrationEvent(item).ToDto());
    }

    private void Validate(
        int hardwareItemId,
        CalibrationEventType calibrationType,
        CalibrationEventStatus status,
        CalibrationResult result,
        string? title,
        decimal? temperatureC,
        DateTime? performedAtUtc,
        DateTime? nextDueAtUtc)
    {
        if (_repository.GetHardwareItem(hardwareItemId) is null)
        {
            ModelState.AddModelError(nameof(CreateCalibrationEventRequest.HardwareItemId), $"HardwareItem mit Id {hardwareItemId} existiert nicht.");
        }

        if (!Enum.IsDefined(calibrationType))
        {
            ModelState.AddModelError(nameof(CreateCalibrationEventRequest.CalibrationType), "CalibrationType ist ungueltig.");
        }

        if (!Enum.IsDefined(status))
        {
            ModelState.AddModelError(nameof(CreateCalibrationEventRequest.Status), "Status ist ungueltig.");
        }

        if (!Enum.IsDefined(result))
        {
            ModelState.AddModelError(nameof(CreateCalibrationEventRequest.Result), "Result ist ungueltig.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            ModelState.AddModelError(nameof(CreateCalibrationEventRequest.Title), "Title darf nicht leer sein.");
        }

        if (temperatureC is < -10m or > 60m)
        {
            ModelState.AddModelError(nameof(CreateCalibrationEventRequest.TemperatureC), "TemperatureC muss zwischen -10 und 60 liegen.");
        }

        if (performedAtUtc.HasValue &&
            nextDueAtUtc.HasValue &&
            nextDueAtUtc.Value.ToUniversalTime() < performedAtUtc.Value.ToUniversalTime())
        {
            ModelState.AddModelError(nameof(CreateCalibrationEventRequest.NextDueAtUtc), "NextDueAtUtc darf nicht vor PerformedAtUtc liegen.");
        }
    }

    /// <summary>Einen falsch eingetragenen Kalibriervorgang entfernen.</summary>
    /// <remarks>
    /// Ein Protokoll ist erst eines, wenn es stimmt. Bleibt ein Vertipper
    /// stehen, rechnet der Faelligkeits-Waechter mit einem Datum, das nie
    /// stattgefunden hat — und meldet die naechste Kalibrierung zu spaet.
    /// </remarks>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public IActionResult Delete(int id)
    {
        if (_repository.GetCalibrationEvent(id) is null)
        {
            return NotFoundError("calibration_event_not_found", $"Kalibriervorgang mit Id {id} existiert nicht.");
        }

        _repository.DeleteCalibrationEvent(id);
        return NoContent();
    }

}
