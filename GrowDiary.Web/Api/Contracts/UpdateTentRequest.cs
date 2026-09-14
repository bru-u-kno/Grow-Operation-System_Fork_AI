namespace GrowDiary.Web.Api.Contracts;

public sealed class CreateTentRequest
{
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "Grow Tent";
    public string? TentType { get; set; }
    public string? Status { get; set; }
    public string? Notes { get; set; }
    public int DisplayOrder { get; set; } = 99;
    public string AccentColor { get; set; } = "#69b578";
    public int? WidthCm { get; set; }
    public int? DepthCm { get; set; }
    public int? TentHeightCm { get; set; }
    public string? LightType { get; set; }
    public int? LightWatt { get; set; }
    public string? LightController { get; set; }
    public string? LightControllerEntityId { get; set; }
    public int? ExhaustFanCount { get; set; }
    public int? ExhaustM3h { get; set; }
    public int? CirculationFanCount { get; set; }
    public string? HvacController { get; set; }
    public string? HvacControllerEntityId { get; set; }
    public bool Co2Available { get; set; }
    public bool HasCo2Enrichment { get; set; }
    public string? CameraEntityId { get; set; }
    public List<string>? Cameras { get; set; }

    /// <summary>°C the leaf sits below air temperature (leaf VPD). Null keeps the stored value.</summary>
    public double? LeafTempOffsetC { get; set; }

    /// <summary>HA-Dienst, der das Offset an den Controller weiterreicht. Null behaelt den gespeicherten Wert, "" schaltet ab.</summary>
    public string? LeafOffsetSyncService { get; set; }

    /// <summary>Sensor-Port am Controller. Null behaelt den gespeicherten Wert.</summary>
    public int? LeafOffsetSyncPort { get; set; }
    public List<UpdateTentSensorRequest>? Sensors { get; set; }
}

public sealed class UpdateTentRequest
{
    public string? Status { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "Grow Tent";
    public string? TentType { get; set; }
    public string? Notes { get; set; }
    public int DisplayOrder { get; set; }
    public string AccentColor { get; set; } = "#69b578";
    public int? WidthCm { get; set; }
    public int? DepthCm { get; set; }
    public int? TentHeightCm { get; set; }
    public string? LightType { get; set; }
    public int? LightWatt { get; set; }
    public string? LightController { get; set; }
    public string? LightControllerEntityId { get; set; }
    public int? ExhaustFanCount { get; set; }
    public int? ExhaustM3h { get; set; }
    public int? CirculationFanCount { get; set; }
    public string? HvacController { get; set; }
    public string? HvacControllerEntityId { get; set; }
    public bool Co2Available { get; set; }
    public bool HasCo2Enrichment { get; set; }
    public string? CameraEntityId { get; set; }
    public List<string>? Cameras { get; set; }

    /// <summary>°C the leaf sits below air temperature (leaf VPD). Null keeps the stored value.</summary>
    public double? LeafTempOffsetC { get; set; }

    /// <summary>HA-Dienst, der das Offset an den Controller weiterreicht. Null behaelt den gespeicherten Wert, "" schaltet ab.</summary>
    public string? LeafOffsetSyncService { get; set; }

    /// <summary>Sensor-Port am Controller. Null behaelt den gespeicherten Wert.</summary>
    public int? LeafOffsetSyncPort { get; set; }
    public List<UpdateTentSensorRequest>? Sensors { get; set; }
}

public sealed class UpdateTentSensorRequest
{
    public int Id { get; set; }
    public string MetricType { get; set; } = string.Empty;
    public string? HaEntityId { get; set; }
    public string? DisplayLabel { get; set; }
    public bool IsActive { get; set; }
}
