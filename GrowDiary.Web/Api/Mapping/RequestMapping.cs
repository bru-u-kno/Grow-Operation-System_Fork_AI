using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Models;
using GrowDiary.Web.ViewModels;

namespace GrowDiary.Web.Api.Mapping;

public static class RequestMapping
{
    public static GrowFormViewModel ToFormModel(this GrowUpsertRequest request) => new()
    {
        TemplateId = request.TemplateId,
        Name = request.Name,
        TentId = request.TentId,
        SystemId = request.SystemId,
        SetupId = request.SetupId,
        Strain = request.Strain,
        Breeder = request.Breeder,
        SeedType = request.SeedType,
        StartMaterial = request.StartMaterial,
        GerminationMethod = request.GerminationMethod,
        CloneSource = request.CloneSource,
        CloneIsRooted = request.CloneIsRooted,
        PhenoNumber = request.PhenoNumber,
        BreederFlowerWeeksMin = request.BreederFlowerWeeksMin,
        BreederFlowerWeeksMax = request.BreederFlowerWeeksMax,
        PlannedVegDays = request.PlannedVegDays,
        SetpointProfileId = string.IsNullOrWhiteSpace(request.SetpointProfileId) ? null : request.SetpointProfileId,
        StrainId = request.StrainId,
        HydroStyle = request.HydroStyle,
        PlantCount = request.PlantCount,
        ReservoirSize = request.ReservoirSize,
        ContainerSize = request.ContainerSize,
        PropagationMedium = request.PropagationMedium,
        Light = request.Light,
        HasChiller = request.HasChiller,
        WaterSource = request.WaterSource,
        FeedProgramId = string.IsNullOrWhiteSpace(request.FeedProgramId) ? null : request.FeedProgramId.Trim(),
        UseFeedChartTargets = request.UseFeedChartTargets ?? false,
        Nutrients = request.Nutrients,
        StartDate = request.StartDate,
        EntryPoint = request.EntryPoint,
        DaysAlreadyInPhase = request.DaysAlreadyInPhase,
        AutoflowerDaysSinceGermination = request.AutoflowerDaysSinceGermination,
        FlipDate = request.FlipDate,
        Notes = request.Notes,
        Status = request.Status,
        Environment = request.Environment
    };

    public static MeasurementFormViewModel ToFormModel(this MeasurementUpsertRequest request, GrowRun grow) => new()
    {
        GrowId = grow.Id,
        GrowName = grow.Name,
        MediumType = grow.MediumType,
        FeedingStyle = grow.FeedingStyle,
        HydroStyle = grow.HydroStyle,
        MediumDetail = grow.MediumDetail,
        IrrigationStyle = grow.IrrigationStyle,
        TakenAtLocal = request.TakenAtLocal,
        Stage = request.Stage,
        Source = request.Source,
        Notes = request.Notes,
        AirTemperatureC = request.AirTemperatureC,
        HumidityPercent = request.HumidityPercent,
        HeightCm = request.HeightCm,
        WaterAmountMl = request.WaterAmountMl,
        RunoffAmountMl = request.RunoffAmountMl,
        IrrigationPh = request.IrrigationPh,
        IrrigationEc = request.IrrigationEc,
        DrainPh = request.DrainPh,
        DrainEc = request.DrainEc,
        ReservoirPh = request.ReservoirPh,
        ReservoirEc = request.ReservoirEc,
        ReservoirWaterTempC = request.ReservoirWaterTempC,
        ReservoirLevelCm = request.ReservoirLevelCm,
        ReservoirLevelLiters = request.ReservoirLevelLiters,
        DissolvedOxygenMgL = request.DissolvedOxygenMgL,
        AirflowAtLeafMPerMin = request.AirflowAtLeafMPerMin,
        WaterFlow = Enum.TryParse<WaterFlowLevel>(request.WaterFlow, ignoreCase: true, out var waterFlow) ? waterFlow : null,
        OrpMv = request.OrpMv,
        TopOffLiters = request.TopOffLiters,
        AddbackEc = request.AddbackEc,
        SolutionChange = request.SolutionChange,
        PpfdMol = request.PpfdMol,
        Co2Ppm = request.Co2Ppm
    };

    public static GrowTask ToModel(this GrowTaskCreateRequest request, int growId)
        => new GrowTaskFormViewModel
        {
            Title = request.Title,
            Notes = request.Notes,
            DueAtLocal = request.DueAtLocal,
            Priority = request.Priority
        }.ToTask(growId);

    public static JournalEntry ToModel(this JournalEntryCreateRequest request, int growId)
        => new JournalEntryFormViewModel
        {
            Title = request.Title,
            Body = request.Body ?? string.Empty,
            EntryType = request.EntryType,
            Source = request.Source,
            OccurredAtLocal = request.OccurredAtLocal
        }.ToEntry(growId);

    public static HomeAssistantSettings ToModel(this SaveHomeAssistantSettingsRequest request) => new()
    {
        BaseUrl = string.IsNullOrWhiteSpace(request.BaseUrl) ? null : request.BaseUrl.Trim(),
        AccessToken = string.IsNullOrWhiteSpace(request.AccessToken) ? null : request.AccessToken.Trim(),
        Enabled = request.Enabled
    };

    public static Tent ToModel(this UpdateTentRequest request, int id) => new()
    {
        Id = id,
        Name = string.IsNullOrWhiteSpace(request.Name) ? string.Empty : request.Name.Trim(),
        Kind = string.IsNullOrWhiteSpace(request.Kind) ? "Grow Tent" : request.Kind.Trim(),
        TentType = Enum.TryParse<TentType>(request.TentType, out var tt) ? tt : TentType.MultiPurpose,
        Status = Enum.TryParse<TentStatus>(request.Status, out var status) ? status : TentStatus.Active,
        Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
        DisplayOrder = request.DisplayOrder,
        AccentColor = string.IsNullOrWhiteSpace(request.AccentColor) ? "#69b578" : request.AccentColor.Trim(),
        WidthCm = request.WidthCm,
        DepthCm = request.DepthCm,
        TentHeightCm = request.TentHeightCm,
        LightType = Normalize(request.LightType),
        LightWatt = request.LightWatt,
        LightController = Enum.TryParse<LightControllerType>(request.LightController, out var lc) ? lc : (LightControllerType?)null,
        LightControllerEntityId = Normalize(request.LightControllerEntityId),
        ExhaustFanCount = request.ExhaustFanCount,
        ExhaustM3h = request.ExhaustM3h,
        CirculationFanCount = request.CirculationFanCount,
        HvacController = Enum.TryParse<HvacControllerType>(request.HvacController, out var hc) ? hc : (HvacControllerType?)null,
        HvacControllerEntityId = Normalize(request.HvacControllerEntityId),
        Co2Available = request.Co2Available,
        HasCo2Enrichment = request.HasCo2Enrichment,
        CameraEntityId = Normalize(request.CameraEntityId),
        LeafOffsetSyncService = Normalize(request.LeafOffsetSyncService),
        LeafOffsetSyncPort = request.LeafOffsetSyncPort ?? Tent.DefaultLeafOffsetSyncPort
    };

    public static Tent ToModel(this CreateTentRequest request) => new()
    {
        Name = string.IsNullOrWhiteSpace(request.Name) ? string.Empty : request.Name.Trim(),
        Kind = string.IsNullOrWhiteSpace(request.Kind) ? "Grow Tent" : request.Kind.Trim(),
        TentType = Enum.TryParse<TentType>(request.TentType, out var tt) ? tt : TentType.MultiPurpose,
        Status = Enum.TryParse<TentStatus>(request.Status, out var status) ? status : TentStatus.Active,
        Notes = Normalize(request.Notes),
        DisplayOrder = request.DisplayOrder,
        AccentColor = string.IsNullOrWhiteSpace(request.AccentColor) ? "#69b578" : request.AccentColor.Trim(),
        WidthCm = request.WidthCm,
        DepthCm = request.DepthCm,
        TentHeightCm = request.TentHeightCm,
        LightType = Normalize(request.LightType),
        LightWatt = request.LightWatt,
        LightController = Enum.TryParse<LightControllerType>(request.LightController, out var lc) ? lc : (LightControllerType?)null,
        LightControllerEntityId = Normalize(request.LightControllerEntityId),
        ExhaustFanCount = request.ExhaustFanCount,
        ExhaustM3h = request.ExhaustM3h,
        CirculationFanCount = request.CirculationFanCount,
        HvacController = Enum.TryParse<HvacControllerType>(request.HvacController, out var hc) ? hc : (HvacControllerType?)null,
        HvacControllerEntityId = Normalize(request.HvacControllerEntityId),
        Co2Available = request.Co2Available,
        HasCo2Enrichment = request.HasCo2Enrichment,
        CameraEntityId = Normalize(request.CameraEntityId),
        LeafOffsetSyncService = Normalize(request.LeafOffsetSyncService),
        LeafOffsetSyncPort = request.LeafOffsetSyncPort ?? Tent.DefaultLeafOffsetSyncPort
    };

    public static List<TentSensor> ToSensors(this CreateTentRequest request, int tentId)
        => MapTentSensors(request.Sensors, tentId);

    public static List<TentSensor> ToSensors(this UpdateTentRequest request, int tentId)
        => MapTentSensors(request.Sensors, tentId);

    private static List<TentSensor> MapTentSensors(List<UpdateTentSensorRequest>? sensors, int tentId)
        => (sensors ?? [])
            .Select(sensor =>
            {
                if (!Enum.TryParse<SensorMetricType>(sensor.MetricType, out var metricType))
                {
                    return null;
                }

                var haEntityId = Normalize(sensor.HaEntityId);
                var displayLabel = Normalize(sensor.DisplayLabel);

                return new TentSensor
                {
                    Id = sensor.Id,
                    TentId = tentId,
                    MetricType = metricType,
                    HaEntityId = haEntityId ?? string.Empty,
                    DisplayLabel = displayLabel,
                    IsActive = sensor.IsActive
                };
            })
            .Where(sensor => sensor is not null)
            .Select(sensor => sensor!)
            .GroupBy(sensor => sensor.MetricType)
            .Select(group => group.Last())
            .Where(sensor => !string.IsNullOrWhiteSpace(sensor.HaEntityId) || !string.IsNullOrWhiteSpace(sensor.DisplayLabel) || sensor.IsActive)
            .ToList();

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
