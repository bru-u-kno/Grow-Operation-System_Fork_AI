using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Mapping;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

[ApiController]
[Route("api/hydro-setups")]
[Produces("application/json")]
[KiStufe(KiStufe.GrowPlanen)]
public sealed class HydroSetupsApiController : ApiControllerBase
{
    private readonly GrowRepository _repository;

    public HydroSetupsApiController(GrowRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<HydroSetupDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public ActionResult<IReadOnlyList<HydroSetupDto>> List([FromQuery] int? tentId = null, [FromQuery] bool includeArchived = false)
    {
        if (tentId.HasValue && _repository.GetTent(tentId.Value) is null)
        {
            ModelState.AddModelError(nameof(tentId), $"Zelt mit Id {tentId.Value} existiert nicht.");
            return ValidationError();
        }

        var systems = tentId.HasValue
            ? _repository.GetHydroSetupsByTent(tentId.Value, includeArchived)
            : _repository.GetHydroSetups(includeArchived);

        return Ok(systems.Select(system => system.ToDto()).ToList());
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(HydroSetupDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<HydroSetupDto> Detail(int id)
    {
        var setup = _repository.GetHydroSetup(id);
        return setup is null
            ? NotFoundError("hydro_setup_not_found", $"HydroSetup mit Id {id} existiert nicht.")
            : Ok(setup.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(HydroSetupDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public ActionResult<HydroSetupDto> Create([FromBody] CreateHydroSetupRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        Validate(request);
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var created = _repository.CreateHydroSetup(request.ToModel());
        return CreatedAtAction(nameof(Detail), new { id = created.Id }, created.ToDto());
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(HydroSetupDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<HydroSetupDto> Update(int id, [FromBody] UpdateHydroSetupRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var existing = _repository.GetHydroSetup(id);
        if (existing is null)
        {
            return NotFoundError("hydro_setup_not_found", $"HydroSetup mit Id {id} existiert nicht.");
        }

        Validate(request);
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        _repository.UpdateHydroSetup(request.ToModel(id, existing.CreatedAtUtc));
        return Ok(_repository.GetHydroSetup(id)!.ToDto());
    }

    [HttpPost("{id:int}/archive")]
    [ProducesResponseType(typeof(HydroSetupDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<HydroSetupDto> Archive(int id)
    {
        if (_repository.GetHydroSetup(id) is null)
        {
            return NotFoundError("hydro_setup_not_found", $"HydroSetup mit Id {id} existiert nicht.");
        }

        _repository.ArchiveHydroSetup(id);
        return Ok(_repository.GetHydroSetup(id)!.ToDto());
    }

    [HttpDelete("{id:int}")]
    [KiStufe(KiStufe.Verwaltung)]
    [KiSicherungVorher]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(HydroSetupDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public IActionResult Delete(int id)
    {
        var existing = _repository.GetHydroSetup(id);
        if (existing is null)
        {
            return NotFoundError("hydro_setup_not_found", $"HydroSetup mit Id {id} existiert nicht.");
        }

        if (existing.ActiveGrowCount > 0)
        {
            return ConflictError("hydro_setup_has_active_grows", "HydroSetup kann nicht geloescht werden, solange aktive oder geplante Grows damit verknuepft sind. Oeffne die betroffenen Grows oder archiviere das HydroSetup.");
        }

        try
        {
            _repository.DeleteSystem(id);
            return NoContent();
        }
        catch
        {
            return ConflictError("hydro_setup_has_dependencies", "HydroSetup kann wegen vorhandener Abhaengigkeiten nicht geloescht werden. Oeffne die verknuepften Datensaetze oder archiviere das HydroSetup.");
        }
    }

    private void Validate(CreateHydroSetupRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            ModelState.AddModelError(nameof(CreateHydroSetupRequest.Name), "Bitte gib dem HydroSetup einen Namen.");
        }

        if (!request.TentId.HasValue)
        {
            ModelState.AddModelError(nameof(CreateHydroSetupRequest.TentId), "HydroSetup braucht ein Zelt.");
        }
        else if (_repository.GetTent(request.TentId.Value) is null)
        {
            ModelState.AddModelError(nameof(CreateHydroSetupRequest.TentId), $"Zelt mit Id {request.TentId.Value} existiert nicht.");
        }

        if (!Enum.IsDefined(request.HydroStyle) || request.HydroStyle is not (HydroStyle.DWC or HydroStyle.RDWC))
        {
            ModelState.AddModelError(nameof(CreateHydroSetupRequest.HydroStyle), "HydroSetups dürfen nur DWC oder RDWC sein.");
        }

        if (!Enum.IsDefined(request.LayoutType))
        {
            ModelState.AddModelError(nameof(CreateHydroSetupRequest.LayoutType), "LayoutType ist ungültig.");
        }

        if (!Enum.IsDefined(request.ReservoirPosition))
        {
            ModelState.AddModelError(nameof(CreateHydroSetupRequest.ReservoirPosition), "ReservoirPosition ist ungültig.");
        }

        if (request is UpdateHydroSetupRequest updateRequest && !Enum.IsDefined(updateRequest.Status))
        {
            ModelState.AddModelError(nameof(UpdateHydroSetupRequest.Status), "HydroSetupStatus ist ungültig.");
        }

        if (request.DisplayOrder < 0)
        {
            ModelState.AddModelError(nameof(CreateHydroSetupRequest.DisplayOrder), "DisplayOrder darf nicht negativ sein.");
        }

        if (request.PotCount.HasValue && request.PotCount.Value < 1)
        {
            ModelState.AddModelError(nameof(CreateHydroSetupRequest.PotCount), "Anzahl Sites muss positiv sein.");
        }

        if (request.PotSizeLiters.HasValue && request.PotSizeLiters.Value < 0)
        {
            ModelState.AddModelError(nameof(CreateHydroSetupRequest.PotSizeLiters), "Liter pro Topf dürfen nicht negativ sein.");
        }

        if (request.ReservoirLiters.HasValue && request.ReservoirLiters.Value < 0)
        {
            ModelState.AddModelError(nameof(CreateHydroSetupRequest.ReservoirLiters), "Reservoirvolumen darf nicht negativ sein.");
        }

        if (request.AirStoneCount.HasValue && request.AirStoneCount.Value < 0)
        {
            ModelState.AddModelError(nameof(CreateHydroSetupRequest.AirStoneCount), "Luftstein-Anzahl darf nicht negativ sein.");
        }

        if (request.HydroStyle == HydroStyle.DWC && request.PotSizeLiters is not > 0 && request.ReservoirLiters is not > 0)
        {
            ModelState.AddModelError(nameof(CreateHydroSetupRequest.PotSizeLiters), "DWC braucht mindestens Topf- oder Reservoirvolumen.");
        }

        if (request.HydroStyle == HydroStyle.RDWC)
        {
            if (request.PotCount is null or < 2)
            {
                ModelState.AddModelError(nameof(CreateHydroSetupRequest.PotCount), "RDWC braucht mindestens zwei Sites.");
            }

            if (request.PotSizeLiters is not > 0)
            {
                ModelState.AddModelError(nameof(CreateHydroSetupRequest.PotSizeLiters), "RDWC braucht Liter pro Site.");
            }

            if (request.LayoutType == HydroSetupLayoutType.SingleBucket)
            {
                ModelState.AddModelError(nameof(CreateHydroSetupRequest.LayoutType), "RDWC braucht ein RDWC-Layout.");
            }

            if (request.ReservoirPosition == ReservoirPosition.None)
            {
                ModelState.AddModelError(nameof(CreateHydroSetupRequest.ReservoirPosition), "RDWC braucht eine Tankposition.");
            }
        }
    }
}
