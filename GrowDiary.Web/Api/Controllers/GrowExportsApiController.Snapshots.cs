using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Mapping;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

public sealed partial class GrowExportsApiController
{
    private static TentDto? TryReadTentSnapshotDto(string? snapshotJson)
    {
        var snapshot = TryDeserializeSnapshot<GrowTentSnapshot>(snapshotJson);
        if (snapshot is null)
        {
            return null;
        }

        return new TentDto(
            Id: snapshot.Id,
            Name: snapshot.Name,
            Kind: snapshot.Kind,
            TentType: snapshot.TentType.ToString(),
            Status: snapshot.Status.ToString(),
            Notes: snapshot.Notes,
            DisplayOrder: snapshot.DisplayOrder,
            AccentColor: snapshot.AccentColor,
            WidthCm: snapshot.WidthCm,
            DepthCm: snapshot.DepthCm,
            TentHeightCm: snapshot.TentHeightCm,
            LightType: snapshot.LightType,
            LightWatt: snapshot.LightWatt,
            LightController: snapshot.LightController?.ToString(),
            LightControllerEntityId: snapshot.LightControllerEntityId,
            ExhaustFanCount: snapshot.ExhaustFanCount,
            ExhaustM3h: snapshot.ExhaustM3h,
            CirculationFanCount: snapshot.CirculationFanCount,
            HvacController: snapshot.HvacController?.ToString(),
            HvacControllerEntityId: snapshot.HvacControllerEntityId,
            Co2Available: snapshot.Co2Available,
            // Aeltere Schnappschuesse kennen die Anreicherung nicht — false ist der ehrliche Default.
            HasCo2Enrichment: false,
            CameraEntityId: snapshot.CameraEntityId,
            ActiveGrowCount: 0,
            ArchivedGrowCount: 0,
            ActiveSetupCount: 0,
            ArchivedSetupCount: 0,
            Sensors: (snapshot.Sensors ?? Array.Empty<GrowTentSensorSnapshot>()).Select(sensor => new TentSensorDto(
                Id: sensor.Id,
                TentId: snapshot.Id,
                MetricType: sensor.MetricType.ToString(),
                HaEntityId: sensor.HaEntityId,
                DisplayLabel: sensor.DisplayLabel,
                IsActive: sensor.IsActive)).ToList(),
            Cameras: TentCameraList.Parse(null, snapshot.CameraEntityId),
            LeafTempOffsetC: 0,
            LeafOffsetSyncService: null,
            LeafOffsetSyncPort: Tent.DefaultLeafOffsetSyncPort);
    }


    private static HydroSetupDto? TryReadHydroSetupSnapshotDto(string? snapshotJson)
    {
        var snapshot = TryDeserializeSnapshot<GrowHydroSetupSnapshot>(snapshotJson);
        if (snapshot is null)
        {
            return null;
        }

        return new HydroSetupDto(
            Id: snapshot.Id,
            Name: snapshot.Name,
            TentId: snapshot.TentId,
            TentName: snapshot.TentName,
            HydroStyle: Enum.TryParse<HydroStyle>(snapshot.HydroStyle, out var hydroStyle) ? hydroStyle : HydroStyle.None,
            // Ein Schnappschuss haelt fest, wie es DAMALS war — das Profil gehoert
            // zur laufenden Einstellung, nicht zur Momentaufnahme.
            SetpointProfileId: null,
            PotCount: snapshot.PotCount,
            PotSizeLiters: snapshot.PotSizeLiters,
            ReservoirLiters: snapshot.ReservoirLiters,
            // Aeltere Schnappschuesse kennen die Pegel-Kalibrierung nicht.
            LevelSensorEmptyRaw: null,
            LevelSensorFullRaw: null,
            LevelSensorFullLiters: null,
            LevelCalibratedAtUtc: null,
            TotalVolumeLiters: snapshot.TotalVolumeLiters,
            LayoutType: snapshot.LayoutType,
            ReservoirPosition: snapshot.ReservoirPosition,
            Status: snapshot.Status,
            HasCirculationPump: snapshot.HasCirculationPump,
            CirculationPumpNotes: snapshot.CirculationPumpNotes,
            HasAirPump: snapshot.HasAirPump,
            AirPumpNotes: snapshot.AirPumpNotes,
            // Aeltere Schnappschuesse kennen die Pumpenleistung nicht — und ein
            // Rueckblick braucht auch keine frische Einschaetzung.
            AirPumpLitersPerHour: null,
            Aeration: null,
            AirStoneCount: snapshot.AirStoneCount,
            HasChiller: snapshot.HasChiller,
            HasUvSterilizer: snapshot.HasUvSterilizer,
            Notes: snapshot.Notes,
            DisplayOrder: snapshot.DisplayOrder,
            ActiveGrowCount: 0,
            CreatedAtUtc: snapshot.CreatedAtUtc,
            UpdatedAtUtc: snapshot.UpdatedAtUtc);
    }


    private static T? TryDeserializeSnapshot<T>(string? snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(snapshotJson, ExportJsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }


    private static GrowTentSnapshot ToTentSnapshot(TentDto dto)
        => new(
            Id: dto.Id,
            Name: dto.Name,
            Kind: dto.Kind,
            TentType: Enum.TryParse<TentType>(dto.TentType, out var tentType) ? tentType : TentType.MultiPurpose,
            Status: Enum.TryParse<TentStatus>(dto.Status, out var status) ? status : TentStatus.Active,
            Notes: dto.Notes,
            DisplayOrder: dto.DisplayOrder,
            AccentColor: dto.AccentColor,
            WidthCm: dto.WidthCm,
            DepthCm: dto.DepthCm,
            TentHeightCm: dto.TentHeightCm,
            LightType: dto.LightType,
            LightWatt: dto.LightWatt,
            LightController: Enum.TryParse<LightControllerType>(dto.LightController, out var lightController) ? (LightControllerType?)lightController : null,
            LightControllerEntityId: dto.LightControllerEntityId,
            ExhaustFanCount: dto.ExhaustFanCount,
            ExhaustM3h: dto.ExhaustM3h,
            CirculationFanCount: dto.CirculationFanCount,
            HvacController: Enum.TryParse<HvacControllerType>(dto.HvacController, out var hvacController) ? (HvacControllerType?)hvacController : null,
            HvacControllerEntityId: dto.HvacControllerEntityId,
            Co2Available: dto.Co2Available,
            CameraEntityId: dto.CameraEntityId,
            Sensors: (dto.Sensors ?? Array.Empty<TentSensorDto>()).Select(sensor => new GrowTentSensorSnapshot(
                Id: sensor.Id,
                MetricType: Enum.TryParse<SensorMetricType>(sensor.MetricType, out var metricType) ? metricType : SensorMetricType.AirTemperature,
                HaEntityId: sensor.HaEntityId,
                DisplayLabel: sensor.DisplayLabel,
                IsActive: sensor.IsActive)).ToList());


    private static GrowHydroSetupSnapshot ToHydroSetupSnapshot(HydroSetupDto dto)
        => new(
            Id: dto.Id,
            TentId: dto.TentId,
            TentName: dto.TentName,
            Name: dto.Name,
            HydroStyle: dto.HydroStyle.ToString(),
            PotCount: dto.PotCount,
            PotSizeLiters: dto.PotSizeLiters,
            ReservoirLiters: dto.ReservoirLiters,
            TotalVolumeLiters: dto.TotalVolumeLiters,
            Status: dto.Status,
            LayoutType: dto.LayoutType,
            ReservoirPosition: dto.ReservoirPosition,
            HasCirculationPump: dto.HasCirculationPump,
            CirculationPumpNotes: dto.CirculationPumpNotes,
            HasAirPump: dto.HasAirPump,
            AirPumpNotes: dto.AirPumpNotes,
            AirStoneCount: dto.AirStoneCount,
            HasChiller: dto.HasChiller,
            HasUvSterilizer: dto.HasUvSterilizer,
            Notes: dto.Notes,
            DisplayOrder: dto.DisplayOrder,
            CreatedAtUtc: dto.CreatedAtUtc,
            UpdatedAtUtc: dto.UpdatedAtUtc);

}
