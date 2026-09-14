using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Api.Mapping;

public static class SettingsMapping
{
    public static HomeAssistantSettingsDto ToDto(this HomeAssistantSettings settings) => new(
        BaseUrl: settings.BaseUrl,
        AccessToken: string.IsNullOrWhiteSpace(settings.AccessToken) ? null : "********",
        Enabled: settings.Enabled,
        IsManagedByAddon: HomeAssistantAddon.IsRunningAsAddon
    );

    public static TentDto ToDto(this Tent tent) => new(
        Id: tent.Id,
        Name: tent.Name,
        Kind: tent.Kind,
        TentType: tent.TentType.ToString(),
        Status: tent.Status.ToString(),
        Notes: tent.Notes,
        DisplayOrder: tent.DisplayOrder,
        AccentColor: tent.AccentColor,
        WidthCm: tent.WidthCm,
        DepthCm: tent.DepthCm,
        TentHeightCm: tent.TentHeightCm,
        LightType: tent.LightType,
        LightWatt: tent.LightWatt,
        LightController: tent.LightController?.ToString(),
        LightControllerEntityId: tent.LightControllerEntityId,
        ExhaustFanCount: tent.ExhaustFanCount,
        ExhaustM3h: tent.ExhaustM3h,
        CirculationFanCount: tent.CirculationFanCount,
        HvacController: tent.HvacController?.ToString(),
        HvacControllerEntityId: tent.HvacControllerEntityId,
        Co2Available: tent.Co2Available,
        HasCo2Enrichment: tent.HasCo2Enrichment,
        CameraEntityId: tent.CameraEntityId,
        ActiveGrowCount: tent.ActiveGrowCount,
        ArchivedGrowCount: tent.ArchivedGrowCount,
        ActiveSetupCount: tent.ActiveSetupCount,
        ArchivedSetupCount: tent.ArchivedSetupCount,
        Sensors: tent.Sensors.Select(s => new TentSensorDto(
            s.Id, s.TentId, s.MetricType.ToString(), s.HaEntityId, s.DisplayLabel, s.IsActive
        )).ToList(),
        Cameras: TentCameraList.Parse(tent.CameraEntityIds, tent.CameraEntityId),
        LeafTempOffsetC: tent.LeafTempOffsetC,
        LeafOffsetSyncService: tent.LeafOffsetSyncService
    );
}
