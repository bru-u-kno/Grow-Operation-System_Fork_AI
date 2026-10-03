using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Mapping;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

[ApiController]
[Route("api/maintenance-events")]
[Produces("application/json")]
[KiStufe(KiStufe.Dokumentieren)]
public sealed class MaintenanceEventsApiController : ApiControllerBase
{
    private readonly GrowRepository _repository;

    public MaintenanceEventsApiController(GrowRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<MaintenanceEventDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public ActionResult<IReadOnlyList<MaintenanceEventDto>> List([FromQuery] int? hardwareItemId = null, [FromQuery] DateTime? dueBeforeUtc = null)
    {
        if (hardwareItemId.HasValue && _repository.GetHardwareItem(hardwareItemId.Value) is null)
        {
            ModelState.AddModelError(nameof(hardwareItemId), $"HardwareItem mit Id {hardwareItemId.Value} existiert nicht.");
            return ValidationError();
        }

        var items = hardwareItemId.HasValue
            ? _repository.GetMaintenanceEventsByHardwareItem(hardwareItemId.Value)
            : dueBeforeUtc.HasValue
                ? _repository.GetDueMaintenanceEvents(dueBeforeUtc.Value)
                : _repository.GetMaintenanceEvents();

        return Ok(items.Select(item => item.ToDto()).ToList());
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(MaintenanceEventDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<MaintenanceEventDto> Detail(int id)
    {
        var item = _repository.GetMaintenanceEvent(id);
        return item is null
            ? NotFoundError("maintenance_event_not_found", $"MaintenanceEvent mit Id {id} existiert nicht.")
            : Ok(item.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(MaintenanceEventDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public ActionResult<MaintenanceEventDto> Create([FromBody] CreateMaintenanceEventRequest request)
    {
        Validate(request.HardwareItemId, request.EventType, request.Status, request.Result, request.Title, request.PerformedAtUtc, request.NextDueAtUtc);
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var item = _repository.CreateMaintenanceEvent(request.ToModel());
        return CreatedAtAction(nameof(Detail), new { id = item.Id }, item.ToDto());
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(MaintenanceEventDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<MaintenanceEventDto> Update(int id, [FromBody] UpdateMaintenanceEventRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var item = _repository.GetMaintenanceEvent(id);
        if (item is null)
        {
            return NotFoundError("maintenance_event_not_found", $"MaintenanceEvent mit Id {id} existiert nicht.");
        }

        Validate(request.HardwareItemId, request.EventType, request.Status, request.Result, request.Title, request.PerformedAtUtc, request.NextDueAtUtc);
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        request.ApplyTo(item);
        _repository.UpdateMaintenanceEvent(item);
        return Ok(_repository.GetMaintenanceEvent(id)!.ToDto());
    }

    /// <summary>„Ist gemacht" — Wartung abschliessen, naechster Termin steht.</summary>
    [HttpPost("{id:int}/complete")]
    [ProducesResponseType(typeof(MaintenanceEventDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<MaintenanceEventDto> Complete(int id, [FromBody] CompleteMaintenanceEventRequest request)
    {
        var item = _repository.GetMaintenanceEvent(id);
        if (item is null)
        {
            return NotFoundError("maintenance_event_not_found", $"MaintenanceEvent mit Id {id} existiert nicht.");
        }

        // Die Wartung HAT stattgefunden — auch wenn sie etwas zutage gefoerdert
        // hat. Deshalb immer Completed; der Befund steht im Ergebnis.
        item.Status = MaintenanceEventStatus.Completed;
        item.Result = request.ActionNeeded ? MaintenanceResult.ActionNeeded : MaintenanceResult.Passed;
        item.PerformedAtUtc = request.PerformedAtUtc?.ToUniversalTime() ?? DateTime.UtcNow;
        item.NextDueAtUtc = null; // wird neu gerechnet, der alte Wert gehoerte zur letzten Runde
        if (!string.IsNullOrWhiteSpace(request.Notes)) item.Notes = request.Notes.Trim();

        return Ok(_repository.CompleteMaintenanceEvent(item).ToDto());
    }

    private void Validate(
        int hardwareItemId,
        MaintenanceEventType eventType,
        MaintenanceEventStatus status,
        MaintenanceResult result,
        string? title,
        DateTime? performedAtUtc,
        DateTime? nextDueAtUtc)
    {
        if (_repository.GetHardwareItem(hardwareItemId) is null)
        {
            ModelState.AddModelError(nameof(CreateMaintenanceEventRequest.HardwareItemId), $"HardwareItem mit Id {hardwareItemId} existiert nicht.");
        }

        if (!Enum.IsDefined(eventType))
        {
            ModelState.AddModelError(nameof(CreateMaintenanceEventRequest.EventType), "EventType ist ungueltig.");
        }

        if (!Enum.IsDefined(status))
        {
            ModelState.AddModelError(nameof(CreateMaintenanceEventRequest.Status), "Status ist ungueltig.");
        }

        if (!Enum.IsDefined(result))
        {
            ModelState.AddModelError(nameof(CreateMaintenanceEventRequest.Result), "Result ist ungueltig.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            ModelState.AddModelError(nameof(CreateMaintenanceEventRequest.Title), "Title darf nicht leer sein.");
        }

        if (performedAtUtc.HasValue &&
            nextDueAtUtc.HasValue &&
            nextDueAtUtc.Value.ToUniversalTime() < performedAtUtc.Value.ToUniversalTime())
        {
            ModelState.AddModelError(nameof(CreateMaintenanceEventRequest.NextDueAtUtc), "NextDueAtUtc darf nicht vor PerformedAtUtc liegen.");
        }
    }

    /// <summary>Einen falsch eingetragenen Wartungsvorgang entfernen.</summary>
    /// <remarks>Gleiche Begruendung wie bei der Kalibrierung.</remarks>
    [HttpDelete("{id:int}")]
    [KiSicherungVorher]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public IActionResult Delete(int id)
    {
        if (_repository.GetMaintenanceEvent(id) is null)
        {
            return NotFoundError("maintenance_event_not_found", $"Wartungsvorgang mit Id {id} existiert nicht.");
        }

        _repository.DeleteMaintenanceEvent(id);
        return NoContent();
    }

}
