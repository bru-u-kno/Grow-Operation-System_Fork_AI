using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Api.Mapping;

public static class GrowOperationLogMapping
{
    public static AddbackLogDto ToDto(this AddbackLogEntry entry) => new(
        entry.Id,
        entry.GrowId,
        entry.HydroSetupId,
        entry.Kind,
        entry.PerformedAtUtc,
        entry.ReservoirLiters,
        entry.EcBefore,
        entry.EcTarget,
        entry.EcStock,
        entry.EcAfter,
        entry.PhBefore,
        entry.PhAfter,
        entry.LitersAdded,
        entry.NewReservoirVolumeLiters,
        entry.UsedHydroSetupVolume,
        entry.WaterUsed,
        entry.WaterEcMsCm,
        entry.Notes,
        entry.CreatedAtUtc,
        entry.ConsumedLiters);

    public static ChangeoutDto ToDto(this ChangeoutEntry entry) => new(
        entry.Id,
        entry.GrowId,
        entry.HydroSetupId,
        entry.Kind,
        entry.PerformedAtUtc,
        entry.VolumeChangedLiters,
        entry.PercentChanged,
        entry.EcBefore,
        entry.EcAfter,
        entry.PhBefore,
        entry.PhAfter,
        entry.WaterUsed,
        entry.WaterEcMsCm,
        entry.Notes,
        entry.CreatedAtUtc,
        entry.ErinnerungNeuStarten);
}
