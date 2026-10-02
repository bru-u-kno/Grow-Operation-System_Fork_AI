namespace GrowDiary.Web.Models;

public enum GrowStatus
{
    Planning,
    Running,
    Completed,
    Aborted
}

public enum MediumType
{
    Hydro
}

public enum FeedingStyle
{
    None
}

public enum HydroStyle
{
    None,
    DWC,
    RDWC,
    NFT,
    Aeroponic,
    Other
}

public enum IrrigationType
{
    ActiveHydro
}

public enum WaterSource
{
    Tap,
    RO,
    Mixed
}

public enum GrowEnvironment
{
    Indoor,
    Outdoor,
    Greenhouse
}

public enum GrowStage
{
    Seedling,
    Clone,
    Veg,
    Transition,
    Flower,
    Finish,
    Dry,
    Cure
}

public enum ValueOrigin
{
    Manual,
    HomeAssistant,
    Imported,
    Derived
}

public enum JournalEntryType
{
    Note,
    Observation,
    Action,
    Problem,
    Solution,
    Training,
    Transplant,
    Feeding,
    ReservoirChange,
    GerminationConfirmed,
    CloneRooted,
    /// <summary>Saemling vorbei — echte Blaetter da, ab hier Veg.</summary>
    VegStarted,
    FlipToFlower,
    /// <summary>Trichome so weit — Finish beginnt, es wird gespuelt.</summary>
    FinishStarted
}

public enum GrowTaskStatus
{
    Open,
    Done,
    Skipped
}

public enum HardwareItemStatus
{
    Active,
    MaintenanceDue,
    Offline,
    Retired
}

public enum HardwareItemCriticality
{
    Low,
    Medium,
    High,
    Critical
}

public enum MaintenanceEventType
{
    Inspection,
    Cleaning,
    Replacement,
    Repair,
    Other
}

public enum MaintenanceEventStatus
{
    Planned,
    Completed,
    Skipped,
    Cancelled
}

public enum MaintenanceResult
{
    Unknown,
    Passed,
    ActionNeeded,
    Replaced,
    Failed
}

public enum CalibrationEventType
{
    Ph,
    Ec,
    Orp,
    Do,
    Other
}

public enum CalibrationEventStatus
{
    Planned,
    Completed,
    Failed,
    Skipped,
    Cancelled
}

public enum CalibrationResult
{
    Unknown,
    Passed,
    AdjustmentNeeded,
    Failed
}

public enum RiskEventType
{
    PowerOutage,
    UpsOnBattery,
    PumpOffline,
    ChillerOffline,
    LightMismatch,
    HomeAssistantUnavailable,
    CriticalDo,
    SensorUnavailable,
    Other
}

public enum RiskEventSeverity
{
    Info,
    Warning,
    Critical
}

public enum RiskEventStatus
{
    Open,
    Acknowledged,
    Resolved,
    Ignored
}

public enum RiskEventSource
{
    Manual,
    HomeAssistant,
    AutoMeasurement,
    Deviation,
    System
}

public enum SopInstanceStatus
{
    Active,
    Completed,
    Cancelled
}

public enum SopStepInstanceStatus
{
    Pending,
    InProgress,
    Done,
    Skipped
}

public enum SopStartSource
{
    Manual,
    Recommendation,
    System
}

public enum TaskPriority
{
    Low,
    Normal,
    High,
    Critical
}

public enum PhotoTag
{
    Overview,
    Canopy,
    Leaf,
    Root,
    Training,
    Flower,
    Problem,
    Comparison,
    Other
}

public enum SeedType
{
    Feminized,
    Autoflower,
    Regular
}

public enum StartMaterial
{
    Seed,
    Clone
}

public enum GerminationMethod
{
    PaperTowel,
    Rockwool,
    RapidRooter,
    DirectInSystem
}

public enum PropagationMedium
{
    Rockwool,
    Hydroton,
    RapidRooter,
    Neoprene
}

public enum GrowEntryPoint
{
    Germination,
    Seedling,
    Veg,
    Flower,
    Flush
}

public enum TentType
{
    Production,
    Mother,
    Quarantine,
    Propagation,
    MultiPurpose
}

public enum TentStatus
{
    Active,
    Archived
}

public enum SetupType
{
    Production,
    Mother,
    Quarantine,
    Propagation
}

public enum SetupStatus
{
    Planning,
    Active,
    Archived
}

public enum HydroSetupStatus
{
    Active,
    Archived
}

public enum HydroSetupLayoutType
{
    SingleBucket,
    Row,
    Grid2x2,
    Grid2x3,
    Grid2x4,
    Custom
}

public enum ReservoirPosition
{
    None,
    Left,
    Right,
    Top,
    Bottom,
    External
}

public enum PlantRole
{
    Production,
    Mother,
    Clone,
    Quarantine
}

public enum PlantStatus
{
    Planned,
    Active,
    Archived,
    Culled,
    Harvested
}

/// <summary>Wie der Züchter die Samen verkauft — die Filterachse Nummer eins des Testers.</summary>
public enum SeedKind
{
    Feminized,
    Automatic,
    Regular
}

public enum StrainDominance
{
    Unknown,
    Indica,
    Sativa,
    Hybrid
}

public enum AutoMeasurementStatus
{
    Enabled,
    Disabled
}

public enum AutoMeasurementAggregation
{
    Latest,
    Median,
    Average
}

public enum AutoMeasurementField
{
    AirTemperatureC,
    HumidityPercent,
    ReservoirPh,
    ReservoirEc,
    ReservoirWaterTempC,
    ReservoirLevelLiters,
    ReservoirLevelCm,
    DissolvedOxygenMgL,
    OrpMv,
    PpfdMol,
    Co2Ppm
}

public enum AutoMeasurementTriggerKind
{
    Manual,
    LightOnDelay,
    LightOffDelay
}

public enum AutoMeasurementRunStatus
{
    Pending,
    Created,
    Skipped,
    Failed
}

public enum AddbackLogKind
{
    Addback,
    TopOff,
    Correction
}

public enum ChangeoutKind
{
    Partial,
    Full
}

public enum LightState
{
    Unknown,
    On,
    Off
}

public enum LightTransitionKind
{
    LightOn,
    LightOff
}

public enum LightSource
{
    Manual,
    HomeAssistant
}

/// <summary>
/// What kind of physical device a hardware item is. Drives which expectations apply:
/// fixed sensors are HA-mapped (live values), handheld meters are calibrated but never
/// mapped, equipment (pumps, chillers, UPS) only gets maintenance.
/// </summary>
public enum HardwareDeviceKind
{
    FixedSensor,
    HandheldMeter,
    Equipment
}

/// <summary>
/// Wie stark das Wasser zirkuliert — in Stufen, nicht in Litern.
/// </summary>
/// <remarks>
/// Starker, gleichmäßiger Flow erzeugt Wurzelstress; moderate Bewegung ist das
/// Ziel. Der häufigste Fehler ist die zu groß gewählte Pumpe, in der Annahme,
/// mehr Umwälzung sei grundsätzlich besser.
/// </remarks>
public enum WaterFlowLevel
{
    Weak,
    Moderate,
    Strong
}

public enum SensorMetricType
{
    AirTemperature,
    Humidity,
    Vpd,
    Co2,
    Ppfd,
    LightStatus,
    ReservoirPh,
    ReservoirEc,
    ReservoirOrp,
    ReservoirDissolvedOxygen,
    ReservoirWaterTemp,
    ReservoirLevel,
    ReservoirLevelCm,
    PumpCirculation,
    PumpAir,
    /// <summary>Leistungsaufnahme der Umwaelzpumpe in Watt — verraet die Pumpe, die „an" meldet und nichts tut.</summary>
    PumpCirculationPower,
    /// <summary>Leistungsaufnahme der Luftpumpe in Watt.</summary>
    PumpAirPower,
    Chiller,
    UpsBattery,
    UpsStatus
}

public enum LightControllerType
{
    AcInfinityPro69,
    AcInfinityCloudline,
    GenericRelay,
    Manual,
    Other
}

public enum HvacControllerType
{
    AcInfinityPro69,
    AcInfinityCloudline,
    GenericRelay,
    Manual,
    Other
}
