using System.Globalization;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Mapping;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

[ApiController]
[Route("api/light-schedules")]
[Produces("application/json")]
[KiStufe(KiStufe.GrowPlanen)]
public sealed class LightSchedulesApiController : ApiControllerBase
{
    private readonly GrowRepository _repository;

    public LightSchedulesApiController(GrowRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<LightScheduleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public ActionResult<IReadOnlyList<LightScheduleDto>> List([FromQuery] int tentId)
    {
        if (_repository.GetTent(tentId) is null)
        {
            ModelState.AddModelError(nameof(CreateLightScheduleRequest.TentId), $"Zelt mit Id {tentId} existiert nicht.");
            return ValidationError();
        }

        return Ok(_repository.GetLightSchedulesByTent(tentId).Select(schedule => schedule.ToDto()).ToList());
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(LightScheduleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<LightScheduleDto> Detail(int id)
    {
        var schedule = _repository.GetLightSchedule(id);
        return schedule is null
            ? NotFoundError("light_schedule_not_found", $"LightSchedule mit Id {id} existiert nicht.")
            : Ok(schedule.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(LightScheduleDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public ActionResult<LightScheduleDto> Create([FromBody] CreateLightScheduleRequest request)
    {
        Validate(request.TentId, request.Name, request.LightsOnTime, request.LightsOffTime, request.Source);
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var schedule = _repository.CreateLightSchedule(request.ToModel());
        return CreatedAtAction(nameof(Detail), new { id = schedule.Id }, schedule.ToDto());
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(LightScheduleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<LightScheduleDto> Update(int id, [FromBody] UpdateLightScheduleRequest request)
    {
        var schedule = _repository.GetLightSchedule(id);
        if (schedule is null)
        {
            return NotFoundError("light_schedule_not_found", $"LightSchedule mit Id {id} existiert nicht.");
        }

        Validate(schedule.TentId, request.Name, request.LightsOnTime, request.LightsOffTime, request.Source);
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        request.ApplyTo(schedule);
        _repository.UpdateLightSchedule(schedule);
        return Ok(_repository.GetLightSchedule(id)!.ToDto());
    }

    private void Validate(int tentId, string name, string lightsOnTime, string lightsOffTime, LightSource source)
    {
        if (_repository.GetTent(tentId) is null)
        {
            ModelState.AddModelError(nameof(CreateLightScheduleRequest.TentId), $"Zelt mit Id {tentId} existiert nicht.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            ModelState.AddModelError(nameof(CreateLightScheduleRequest.Name), "Name darf nicht leer sein.");
        }

        var onValid = IsValidLocalTime(lightsOnTime);
        var offValid = IsValidLocalTime(lightsOffTime);
        if (!onValid)
        {
            ModelState.AddModelError(nameof(CreateLightScheduleRequest.LightsOnTime), "LightsOnTime muss im Format HH:mm sein.");
        }

        if (!offValid)
        {
            ModelState.AddModelError(nameof(CreateLightScheduleRequest.LightsOffTime), "LightsOffTime muss im Format HH:mm sein.");
        }

        if (onValid && offValid && string.Equals(lightsOnTime.Trim(), lightsOffTime.Trim(), StringComparison.Ordinal))
        {
            ModelState.AddModelError(nameof(CreateLightScheduleRequest.LightsOffTime), "LightsOnTime und LightsOffTime duerfen nicht gleich sein.");
        }

        if (!Enum.IsDefined(source))
        {
            ModelState.AddModelError(nameof(CreateLightScheduleRequest.Source), "Source muss Manual oder HomeAssistant sein.");
        }
    }

    private static bool IsValidLocalTime(string value)
        => TimeSpan.TryParseExact(value.Trim(), "hh\\:mm", CultureInfo.InvariantCulture, out _);

    /// <summary>Einen Lichtplan entfernen.</summary>
    /// <remarks>
    /// <para><b>Der letzte Plan eines Zelts bleibt stehen.</b> An ihm haengen
    /// die Nachtabsenkung (<c>NachtabsenkungService</c> liest den Lichtplan),
    /// der Waechter gegen Lichteinbruch und beide Auto-Messungen. Ein Zelt
    /// ohne Plan verloere alle drei — und zwar ohne Meldung, weil jede dieser
    /// Stellen einen fehlenden Plan als „nichts zu tun" liest.</para>
    /// </remarks>
    [HttpDelete("{id:int}")]
    [KiStufe(KiStufe.Verwaltung)]
    [KiSicherungVorher]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public IActionResult Delete(int id)
    {
        var plan = _repository.GetLightSchedule(id);
        if (plan is null)
        {
            return NotFoundError("light_schedule_not_found", $"Lichtplan mit Id {id} existiert nicht.");
        }

        if (_repository.GetLightSchedulesByTent(plan.TentId).Count <= 1)
        {
            return ValidationError(
                "Das ist der einzige Lichtplan dieses Zelts. Ohne ihn laufen "
                + "Nachtabsenkung, Lichteinbruch-Wächter und Auto-Messungen ins "
                + "Leere. Lege erst einen neuen an.");
        }

        _repository.DeleteLightSchedule(id);
        return NoContent();
    }

}
