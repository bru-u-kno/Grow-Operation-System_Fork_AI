using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Mapping;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

[ApiController]
[Route("api/auto-measurements")]
[Produces("application/json")]
[KiStufe(KiStufe.GrowPlanen)]
public sealed class AutoMeasurementsApiController : ApiControllerBase
{
    private readonly GrowRepository _repository;

    public AutoMeasurementsApiController(GrowRepository repository)
    {
        _repository = repository;
    }

    [HttpGet("configs")]
    [ProducesResponseType(typeof(IReadOnlyList<AutoMeasurementConfigDto>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<AutoMeasurementConfigDto>> ListConfigs([FromQuery] int? growId = null)
    {
        var configs = growId.HasValue
            ? _repository.GetAutoMeasurementConfigsByGrow(growId.Value)
            : _repository.GetAutoMeasurementConfigs();

        return Ok(configs.Select(config => config.ToDto()).ToList());
    }

    [HttpGet("configs/{id:int}")]
    [ProducesResponseType(typeof(AutoMeasurementConfigDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<AutoMeasurementConfigDto> DetailConfig(int id)
    {
        var config = _repository.GetAutoMeasurementConfig(id);
        return config is null
            ? NotFoundError("auto_measurement_config_not_found", $"AutoMeasurement-Konfiguration mit Id {id} existiert nicht.")
            : Ok(config.ToDto());
    }

    [HttpPost("configs")]
    [ProducesResponseType(typeof(AutoMeasurementConfigDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public ActionResult<AutoMeasurementConfigDto> CreateConfig([FromBody] CreateAutoMeasurementConfigRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        ValidateConfig(request.GrowId, request.TentId, request.Name, request.Status, request.TriggerKind, request.DelayMinutes, request.WindowMinutes);
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var config = _repository.CreateAutoMeasurementConfig(request.ToModel());
        return CreatedAtAction(nameof(DetailConfig), new { id = config.Id }, config.ToDto());
    }

    [HttpPut("configs/{id:int}")]
    [ProducesResponseType(typeof(AutoMeasurementConfigDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<AutoMeasurementConfigDto> UpdateConfig(int id, [FromBody] UpdateAutoMeasurementConfigRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var config = _repository.GetAutoMeasurementConfig(id);
        if (config is null)
        {
            return NotFoundError("auto_measurement_config_not_found", $"AutoMeasurement-Konfiguration mit Id {id} existiert nicht.");
        }

        ValidateConfig(config.GrowId, request.TentId, request.Name, request.Status, request.TriggerKind, request.DelayMinutes, request.WindowMinutes);
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        request.ApplyTo(config);
        _repository.UpdateAutoMeasurementConfig(config);
        return Ok(_repository.GetAutoMeasurementConfig(id)!.ToDto());
    }

    [HttpGet("configs/{id:int}/mappings")]
    [ProducesResponseType(typeof(IReadOnlyList<AutoMeasurementFieldMappingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<IReadOnlyList<AutoMeasurementFieldMappingDto>> GetMappings(int id)
    {
        if (_repository.GetAutoMeasurementConfig(id) is null)
        {
            return NotFoundError("auto_measurement_config_not_found", $"AutoMeasurement-Konfiguration mit Id {id} existiert nicht.");
        }

        return Ok(_repository.GetAutoMeasurementFieldMappings(id).Select(mapping => mapping.ToDto()).ToList());
    }

    [HttpPut("configs/{id:int}/mappings")]
    [ProducesResponseType(typeof(IReadOnlyList<AutoMeasurementFieldMappingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<IReadOnlyList<AutoMeasurementFieldMappingDto>> ReplaceMappings(int id, [FromBody] ReplaceAutoMeasurementFieldMappingsRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        if (_repository.GetAutoMeasurementConfig(id) is null)
        {
            return NotFoundError("auto_measurement_config_not_found", $"AutoMeasurement-Konfiguration mit Id {id} existiert nicht.");
        }

        ValidateMappings(request.Mappings);
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        _repository.ReplaceAutoMeasurementFieldMappings(id, request.Mappings.Select(mapping => mapping.ToModel()).ToList());
        return Ok(_repository.GetAutoMeasurementFieldMappings(id).Select(mapping => mapping.ToDto()).ToList());
    }

    private void ValidateConfig(
        int growId,
        int? tentId,
        string name,
        AutoMeasurementStatus status,
        AutoMeasurementTriggerKind triggerKind,
        int? delayMinutes,
        int windowMinutes)
    {
        if (_repository.GetGrow(growId) is null)
        {
            ModelState.AddModelError(nameof(CreateAutoMeasurementConfigRequest.GrowId), $"Grow mit Id {growId} existiert nicht.");
        }

        if (tentId.HasValue && _repository.GetTent(tentId.Value) is null)
        {
            ModelState.AddModelError(nameof(CreateAutoMeasurementConfigRequest.TentId), $"Zelt mit Id {tentId.Value} existiert nicht.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            ModelState.AddModelError(nameof(CreateAutoMeasurementConfigRequest.Name), "Name darf nicht leer sein.");
        }

        if (!Enum.IsDefined(status))
        {
            ModelState.AddModelError(nameof(CreateAutoMeasurementConfigRequest.Status), "Status muss Enabled oder Disabled sein.");
        }

        if (!Enum.IsDefined(triggerKind))
        {
            ModelState.AddModelError(nameof(CreateAutoMeasurementConfigRequest.TriggerKind), "TriggerKind muss Manual, LightOnDelay oder LightOffDelay sein.");
        }

        if (windowMinutes <= 0 || windowMinutes > 180)
        {
            ModelState.AddModelError(nameof(CreateAutoMeasurementConfigRequest.WindowMinutes), "WindowMinutes muss zwischen 1 und 180 liegen.");
        }

        if (delayMinutes is < 0 or > 1440)
        {
            ModelState.AddModelError(nameof(CreateAutoMeasurementConfigRequest.DelayMinutes), "DelayMinutes muss zwischen 0 und 1440 liegen.");
        }
    }

    private void ValidateMappings(IReadOnlyList<AutoMeasurementFieldMappingUpsertRequest> mappings)
    {
        for (var i = 0; i < mappings.Count; i++)
        {
            var mapping = mappings[i];
            if (!Enum.IsDefined(mapping.MeasurementField))
            {
                ModelState.AddModelError(nameof(AutoMeasurementFieldMappingUpsertRequest.MeasurementField), $"MeasurementField in Zeile {i + 1} ist ungueltig.");
            }

            if (string.IsNullOrWhiteSpace(mapping.MetricKey))
            {
                ModelState.AddModelError(nameof(AutoMeasurementFieldMappingUpsertRequest.MetricKey), $"MetricKey in Zeile {i + 1} darf nicht leer sein.");
            }

            if (!Enum.IsDefined(mapping.Aggregation))
            {
                ModelState.AddModelError(nameof(AutoMeasurementFieldMappingUpsertRequest.Aggregation), $"Aggregation in Zeile {i + 1} muss Latest, Median oder Average sein.");
            }
        }
    }

    /// <summary>Eine Auto-Messung entfernen.</summary>
    /// <remarks>
    /// Ihre Feldzuordnungen und ihre Laufhistorie gehen mit
    /// (<c>ON DELETE CASCADE</c>) — beide gehoeren zur Vorlage und haben ohne
    /// sie keinen Sinn.
    /// </remarks>
    [HttpDelete("configs/{id:int}")]
    [KiStufe(KiStufe.Verwaltung)]
    [KiSicherungVorher]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public IActionResult DeleteConfig(int id)
    {
        if (_repository.GetAutoMeasurementConfig(id) is null)
        {
            return NotFoundError("auto_measurement_config_not_found", $"Auto-Messung mit Id {id} existiert nicht.");
        }

        _repository.DeleteAutoMeasurementConfig(id);
        return NoContent();
    }

}
