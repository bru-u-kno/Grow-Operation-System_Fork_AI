using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Mapping;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

[ApiController]
[Route("api/settings")]
[Produces("application/json")]
public sealed class SettingsApiController : ApiControllerBase
{
    private readonly GrowRepository _repository;
    private readonly TentSensorHardwareSyncService _sensorHardwareSync;
    private readonly HomeAssistantService _homeAssistant;
    private readonly ILogger<SettingsApiController> _logger;

    public SettingsApiController(
        GrowRepository repository,
        TentSensorHardwareSyncService sensorHardwareSync,
        HomeAssistantService homeAssistant,
        ILogger<SettingsApiController> logger)
    {
        _repository = repository;
        _sensorHardwareSync = sensorHardwareSync;
        _homeAssistant = homeAssistant;
        _logger = logger;
    }

    [HttpGet("")]
    [ProducesResponseType(typeof(SettingsOverviewDto), StatusCodes.Status200OK)]
    public ActionResult<SettingsOverviewDto> Overview()
        => Ok(new SettingsOverviewDto(
            HomeAssistant: _repository.GetHomeAssistantSettings().ToDto(),
            Tents: _repository.GetTents().Select(tent => tent.ToDto()).ToList()));

    [HttpGet("home-assistant")]
    [ProducesResponseType(typeof(HomeAssistantSettingsDto), StatusCodes.Status200OK)]
    public ActionResult<HomeAssistantSettingsDto> HomeAssistant()
        => Ok(_repository.GetHomeAssistantSettings().ToDto());

    [HttpGet("~/api/home-assistant/settings")]
    [ProducesResponseType(typeof(HomeAssistantSettingsDto), StatusCodes.Status200OK)]
    public ActionResult<HomeAssistantSettingsDto> HomeAssistantSettings()
        => HomeAssistant();

    [HttpPut("home-assistant")]
    [ProducesResponseType(typeof(HomeAssistantSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public ActionResult<HomeAssistantSettingsDto> SaveHomeAssistant([FromBody] SaveHomeAssistantSettingsRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var settings = request.ToModel();
        if (string.Equals(request.AccessToken, "********", StringComparison.Ordinal))
        {
            settings.AccessToken = _repository.GetHomeAssistantSettings().AccessToken;
        }

        _repository.SaveHomeAssistantSettings(settings);
        return Ok(_repository.GetHomeAssistantSettings().ToDto());
    }

    [HttpGet("tents")]
    [ProducesResponseType(typeof(IReadOnlyList<TentDto>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<TentDto>> Tents([FromQuery] bool includeArchived = false)
        => Ok(_repository.GetTents(includeArchived).Select(tent => tent.ToDto()).ToList());

    [HttpGet("tents/{id:int}")]
    [ProducesResponseType(typeof(TentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<TentDto> Tent(int id)
    {
        var tent = _repository.GetTent(id);
        if (tent is null)
        {
            return NotFoundError("tent_not_found", $"Zelt mit Id {id} existiert nicht.");
        }

        return Ok(tent.ToDto());
    }

    [HttpPost("tents")]
    [ProducesResponseType(typeof(TentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public ActionResult<TentDto> CreateTent([FromBody] CreateTentRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        ValidateTentRequest(request);
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var created = _repository.CreateTent(request.ToModel());
        if (request.Sensors is not null)
        {
            _repository.ReplaceTentSensors(created.Id, request.ToSensors(created.Id));
            created = _repository.GetTent(created.Id)!;
            _sensorHardwareSync.SyncForTent(created);
        }

        return CreatedAtAction(nameof(Tent), new { id = created.Id }, created.ToDto());
    }

    [HttpPut("tents/{id:int}")]
    [ProducesResponseType(typeof(TentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TentDto>> SaveTent(int id, [FromBody] UpdateTentRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var existing = _repository.GetTent(id);
        if (existing is null)
        {
            return NotFoundError("tent_not_found", $"Zelt mit Id {id} existiert nicht.");
        }

        ValidateTentRequest(request);
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var tentToSave = request.ToModel(id);
        if (string.IsNullOrWhiteSpace(request.Status))
        {
            tentToSave.Status = existing.Status;
        }

        if (request.Cameras is not null)
        {
            var (cameraIds, firstCamera) = TentCameraList.Serialize(request.Cameras);
            tentToSave.CameraEntityIds = cameraIds;
            tentToSave.CameraEntityId = firstCamera;
        }
        else
        {
            // No camera list in the request → keep the existing cameras — auch das
            // Einzelkamera-Altfeld, aus dem Zelte von vor der Mehrkamera-Liste
            // ihr Bild noch beziehen.
            tentToSave.CameraEntityIds = existing.CameraEntityIds;
            if (string.IsNullOrWhiteSpace(request.CameraEntityId))
            {
                tentToSave.CameraEntityId = existing.CameraEntityId;
            }
        }

        // Leaf-temperature offset: only replaced when the request actually carries one, so
        // a partial update (e.g. the HA mapping form) never resets it.
        tentToSave.LeafTempOffsetC = request.LeafTempOffsetC is { } offset
            ? Math.Clamp(offset, 0, 10)
            : existing.LeafTempOffsetC;

        // Derselbe Teilaktualisierungs-Schutz wie oben: ein Sensor-Mapping-Formular
        // traegt den Dienst nicht mit und darf ihn deshalb nicht loeschen.
        tentToSave.LeafOffsetSyncService = request.LeafOffsetSyncService is { } dienst
            ? (string.IsNullOrWhiteSpace(dienst) ? null : dienst.Trim())
            : existing.LeafOffsetSyncService;

        // Das Zielgeraet der Nachtabsenkung setzt allein der Night-Ramp-Endpunkt.
        // Kein Zelt-Formular traegt es — ohne diese Zeile nahm jedes Speichern
        // (auch ein blosses Sensor-Mapping) der Rampe still ihr Ziel.
        tentToSave.WaterTargetEntityId = existing.WaterTargetEntityId;

        // Dasselbe fuer die Kuehler-Steuerung. Sie wird auf der
        // Crop-Steering-Seite eingestellt, nicht im Zelt-Formular — ohne diese
        // Zeilen naehme jedes Sensor-Mapping ihr still die Steckdose und alle
        // Kompressor-Grenzen. Genau dieser Fehler ist bei der Nachtabsenkung
        // schon einmal passiert, siehe den Kommentar darueber.
        tentToSave.ChillerSwitchEntityId = existing.ChillerSwitchEntityId;
        tentToSave.ChillerControlEnabled = existing.ChillerControlEnabled;
        tentToSave.ChillerHysteresisC = existing.ChillerHysteresisC;
        tentToSave.ChillerMinRunMinutes = existing.ChillerMinRunMinutes;
        tentToSave.ChillerMinPauseMinutes = existing.ChillerMinPauseMinutes;
        tentToSave.ChillerMaxReadingAgeMinutes = existing.ChillerMaxReadingAgeMinutes;

        _repository.UpdateTent(tentToSave);
        if (request.Sensors is not null)
        {
            _repository.ReplaceTentSensors(id, request.ToSensors(id));
        }

        var saved = _repository.GetTent(id)!;
        if (request.Sensors is not null)
        {
            _sensorHardwareSync.SyncForTent(saved);
        }

        await BlattversatzWeitergebenAsync(existing, saved, cancellationToken);

        return Ok(saved.ToDto());
    }


    /// <summary>
    /// Reicht einen geaenderten Blattversatz an die Hardware weiter.
    /// </summary>
    /// <remarks>
    /// <para>Nur wenn am Zelt ein Dienst hinterlegt ist UND sich der Wert
    /// tatsaechlich geaendert hat. Ohne die Aenderungspruefung schriebe jedes
    /// Speichern des Zelt-Formulars in die Hersteller-Cloud.</para>
    /// <para><b>Vorzeichen:</b> Grow OS fuehrt den Versatz positiv, die
    /// AC-Infinity-App negativ. Deshalb das Minus.</para>
    /// <para><b>Ganze Grad:</b> Die App nimmt nur ganze Zahlen an. Ein
    /// gerundeter Wert ist besser als einer, den der Betreiber dort nicht mehr
    /// sehen und korrigieren kann.</para>
    /// <para>Ein Fehlschlag kippt das Speichern NICHT — der Wert in Grow OS
    /// steht dann, die Hardware nicht. Das steht im Log.</para>
    /// </remarks>
    private async Task BlattversatzWeitergebenAsync(Tent vorher, Tent nachher, CancellationToken cancellationToken)
    {
        var dienst = nachher.LeafOffsetSyncService;
        if (string.IsNullOrWhiteSpace(dienst))
        {
            return;
        }

        if (Math.Abs(vorher.LeafTempOffsetC - nachher.LeafTempOffsetC) < 0.001)
        {
            return;
        }

        var teile = dienst.Split('.', 2);
        if (teile.Length != 2 || string.IsNullOrWhiteSpace(teile[0]) || string.IsNullOrWhiteSpace(teile[1]))
        {
            _logger.LogWarning(
                "Zelt {TentId}: \"{Dienst}\" ist keine Dienstkennung der Form domain.service — Blattversatz nicht weitergegeben.",
                nachher.Id, dienst);
            return;
        }

        var ganzeGrad = Math.Round(nachher.LeafTempOffsetC, MidpointRounding.AwayFromZero);
        if (Math.Abs(ganzeGrad - nachher.LeafTempOffsetC) > 0.001)
        {
            _logger.LogInformation(
                "Zelt {TentId}: Blattversatz {Wert} °C auf {Gerundet} °C gerundet — die AC-Infinity-App nimmt nur ganze Grad.",
                nachher.Id, nachher.LeafTempOffsetC, ganzeGrad);
        }

        var einstellungen = _repository.GetEffectiveHomeAssistantSettings();
        var daten = new Dictionary<string, object>
        {
            ["blatt_offset_c"] = -ganzeGrad,
            ["dry_run"] = false
        };

        var erfolg = await _homeAssistant.CallServiceAsync(
            einstellungen, teile[0], teile[1], daten, cancellationToken);

        if (erfolg)
        {
            _logger.LogInformation(
                "Zelt {TentId}: Blattversatz {Alt} → {Neu} °C an {Dienst} weitergegeben (gesendet: {Gesendet}).",
                nachher.Id, vorher.LeafTempOffsetC, ganzeGrad, dienst, -ganzeGrad);
        }
        else
        {
            _logger.LogWarning(
                "Zelt {TentId}: {Dienst} schlug fehl — Grow OS steht jetzt auf {Neu} °C, die Hardware nicht.",
                nachher.Id, dienst, ganzeGrad);
        }
    }

    [HttpPost("tents/{id:int}/archive")]
    [ProducesResponseType(typeof(TentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<TentDto> ArchiveTent(int id)
    {
        var existing = _repository.GetTent(id);
        if (existing is null)
        {
            return NotFoundError("tent_not_found", $"Zelt mit Id {id} existiert nicht.");
        }

        _repository.ArchiveTent(id);
        return Ok(_repository.GetTent(id)!.ToDto());
    }

    [HttpDelete("tents/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(TentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(TentDependencyError), StatusCodes.Status409Conflict)]
    public IActionResult DeleteTent(int id)
    {
        var existing = _repository.GetTent(id);
        if (existing is null)
        {
            return NotFoundError("tent_not_found", $"Zelt mit Id {id} existiert nicht.");
        }

        var dependencies = BuildTentDependencies(id);
        if (HasBlockingTentDependencies(dependencies))
        {
            return Conflict(new TentDependencyError(
                "tent_has_active_dependencies",
                "Zelt kann wegen aktiver Abhängigkeiten nicht gelöscht werden. Öffne die verknüpften Grows oder Hydro-Setups und beende sie zuerst.",
                dependencies,
                TraceId: HttpContext?.TraceIdentifier));
        }

        _repository.DeleteTentWithCleanup(id);
        return NoContent();
    }

    private TentDependencySummaryDto BuildTentDependencies(int id)
    {
        var activeGrows = _repository.GetActiveGrowsForTent(id)
            .Select(grow => new DependencyItemDto(grow.Id, grow.Name, grow.Status.ToString(), "Grow"))
            .ToList();

        var archivedGrows = _repository.GetArchivedGrowsForTent(id)
            .Select(grow => new DependencyItemDto(grow.Id, grow.Name, grow.Status.ToString(), "Grow"))
            .ToList();

        var hydroSetups = _repository.GetHydroSetupsByTent(id, includeArchived: true)
            .Select(setup => new DependencyItemDto(setup.Id, setup.Name, setup.Status.ToString(), "Hydro"))
            .ToList();

        var hardwareItems = _repository.GetHardwareItemsByTent(id)
            .Select(item => new DependencyItemDto(item.Id, item.Name, item.Status.ToString(), "Hardware"))
            .ToList();

        var tentSensors = _repository.GetTent(id)?.Sensors
            .Select(sensor => new DependencyItemDto(
                sensor.Id,
                string.IsNullOrWhiteSpace(sensor.DisplayLabel) ? sensor.MetricType.ToString() : sensor.DisplayLabel,
                sensor.IsActive ? "Active" : "Inactive",
                "Sensor"))
            .ToList() ?? new List<DependencyItemDto>();

        var setups = _repository.GetSetupsForTent(id)
            .Select(setup => new DependencyItemDto(setup.Id, setup.Name, setup.Status.ToString(), "Setup"))
            .ToList();

        var measurements = _repository.GetArchivedGrowsForTent(id)
            .Concat(_repository.GetActiveGrowsForTent(id))
            .Where(grow => grow.MeasurementCount > 0)
            .Select(grow => new DependencyItemDto(grow.Id, grow.Name, $"{grow.MeasurementCount} Messungen", "Messung"))
            .ToList();

        return new TentDependencySummaryDto(
            activeGrows,
            archivedGrows,
            hydroSetups,
            hardwareItems.Concat(tentSensors).ToList(),
            measurements,
            setups);
    }

    private static bool HasBlockingTentDependencies(TentDependencySummaryDto dependencies)
        => dependencies.ActiveGrows.Count > 0
           || dependencies.HydroSetups.Any(item => !string.Equals(item.Status, "Archived", StringComparison.OrdinalIgnoreCase))
           || dependencies.Other.Any(item => !string.Equals(item.Status, "Archived", StringComparison.OrdinalIgnoreCase));

    private void ValidateTentRequest(CreateTentRequest request)
    {
        ValidateTentBase(
            request.Name,
            request.TentType,
            request.Status,
            request.LightController,
            request.HvacController,
            request.WidthCm,
            request.DepthCm,
            request.TentHeightCm,
            request.LightWatt,
            request.ExhaustFanCount,
            request.ExhaustM3h,
            request.CirculationFanCount,
            request.Sensors);
    }

    private void ValidateTentRequest(UpdateTentRequest request)
    {
        ValidateTentBase(
            request.Name,
            request.TentType,
            request.Status,
            request.LightController,
            request.HvacController,
            request.WidthCm,
            request.DepthCm,
            request.TentHeightCm,
            request.LightWatt,
            request.ExhaustFanCount,
            request.ExhaustM3h,
            request.CirculationFanCount,
            request.Sensors);
    }

    private void ValidateTentBase(
        string name,
        string? tentType,
        string? status,
        string? lightController,
        string? hvacController,
        int? widthCm,
        int? depthCm,
        int? tentHeightCm,
        int? lightWatt,
        int? exhaustFanCount,
        int? exhaustM3h,
        int? circulationFanCount,
        IReadOnlyList<UpdateTentSensorRequest>? sensors)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            ModelState.AddModelError(nameof(UpdateTentRequest.Name), "Bitte gib dem Zelt einen Namen.");
        }

        if (!string.IsNullOrWhiteSpace(tentType) && !Enum.TryParse<TentType>(tentType, out _))
        {
            ModelState.AddModelError(nameof(UpdateTentRequest.TentType), $"Tent-Typ {tentType} ist nicht erlaubt.");
        }

        if (!string.IsNullOrWhiteSpace(status) && !Enum.TryParse<TentStatus>(status, out _))
        {
            ModelState.AddModelError(nameof(UpdateTentRequest.Status), $"Tent-Status {status} ist nicht erlaubt.");
        }

        if (!string.IsNullOrWhiteSpace(lightController) && !Enum.TryParse<LightControllerType>(lightController, out _))
        {
            ModelState.AddModelError(nameof(UpdateTentRequest.LightController), $"Light-Controller {lightController} ist nicht erlaubt.");
        }

        if (!string.IsNullOrWhiteSpace(hvacController) && !Enum.TryParse<HvacControllerType>(hvacController, out _))
        {
            ModelState.AddModelError(nameof(UpdateTentRequest.HvacController), $"HVAC-Controller {hvacController} ist nicht erlaubt.");
        }

        AddPositiveError(widthCm, nameof(UpdateTentRequest.WidthCm), "Breite muss größer als 0 sein.");
        AddPositiveError(depthCm, nameof(UpdateTentRequest.DepthCm), "Tiefe muss größer als 0 sein.");
        AddPositiveError(tentHeightCm, nameof(UpdateTentRequest.TentHeightCm), "Höhe muss größer als 0 sein.");
        AddPositiveError(lightWatt, nameof(UpdateTentRequest.LightWatt), "Lichtleistung muss größer als 0 sein.");
        AddNonNegativeError(exhaustFanCount, nameof(UpdateTentRequest.ExhaustFanCount), "Abluft-Anzahl darf nicht negativ sein.");
        AddNonNegativeError(exhaustM3h, nameof(UpdateTentRequest.ExhaustM3h), "Abluftleistung darf nicht negativ sein.");
        AddNonNegativeError(circulationFanCount, nameof(UpdateTentRequest.CirculationFanCount), "Umluft-Anzahl darf nicht negativ sein.");

        if (sensors is null) return;
        for (var index = 0; index < sensors.Count; index++)
        {
            var sensor = sensors[index];
            if (!Enum.TryParse<SensorMetricType>(sensor.MetricType, out _))
            {
                ModelState.AddModelError($"Sensors[{index}].{nameof(UpdateTentSensorRequest.MetricType)}", $"Sensor-Metrik {sensor.MetricType} ist nicht erlaubt.");
            }

            if (sensor.IsActive && string.IsNullOrWhiteSpace(sensor.HaEntityId))
            {
                ModelState.AddModelError($"Sensors[{index}].{nameof(UpdateTentSensorRequest.HaEntityId)}", "Aktive Sensoren benötigen eine Home-Assistant Entity ID.");
            }
        }
    }

    private void AddPositiveError(int? value, string fieldName, string message)
    {
        if (value.HasValue && value.Value <= 0)
        {
            ModelState.AddModelError(fieldName, message);
        }
    }

    private void AddNonNegativeError(int? value, string fieldName, string message)
    {
        if (value.HasValue && value.Value < 0)
        {
            ModelState.AddModelError(fieldName, message);
        }
    }
}
