using System.ComponentModel.DataAnnotations;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Api.Contracts;

public sealed class CreateAddbackLogRequest
{
    public AddbackLogKind Kind { get; set; } = AddbackLogKind.Addback;
    public DateTime? PerformedAtUtc { get; set; }
    public double? ReservoirLiters { get; set; }
    public double? EcBefore { get; set; }
    public double? EcTarget { get; set; }
    public double? EcStock { get; set; }
    public double? EcAfter { get; set; }
    public double? PhBefore { get; set; }
    public double? PhAfter { get; set; }
    public double? LitersAdded { get; set; }
    public double? NewReservoirVolumeLiters { get; set; }
    public bool? UsedHydroSetupVolume { get; set; }

    /// <summary>Womit aufgefuellt wurde; null = nicht angegeben.</summary>
    public WaterSource? WaterUsed { get; set; }

    /// <summary>EC des verwendeten Wassers in mS/cm, vor dem Duenger.</summary>
    public double? WaterEcMsCm { get; set; }

    [StringLength(4000)]
    public string? Notes { get; set; }
}

public sealed record AddbackLogDto(
    int Id,
    int GrowId,
    int? HydroSetupId,
    AddbackLogKind Kind,
    DateTime PerformedAtUtc,
    double? ReservoirLiters,
    double? EcBefore,
    double? EcTarget,
    double? EcStock,
    double? EcAfter,
    double? PhBefore,
    double? PhAfter,
    double? LitersAdded,
    double? NewReservoirVolumeLiters,
    bool UsedHydroSetupVolume,
    WaterSource? WaterUsed,
    double? WaterEcMsCm,
    string? Notes,
    DateTime CreatedAtUtc,
    double? ConsumedLiters = null);

public sealed class CreateChangeoutRequest
{
    public ChangeoutKind Kind { get; set; } = ChangeoutKind.Partial;
    public DateTime? PerformedAtUtc { get; set; }
    public double? VolumeChangedLiters { get; set; }
    public double? PercentChanged { get; set; }
    public double? EcBefore { get; set; }
    public double? EcAfter { get; set; }
    public double? PhBefore { get; set; }
    public double? PhAfter { get; set; }

    /// <summary>Womit gewechselt wurde; null = nicht angegeben.</summary>
    public WaterSource? WaterUsed { get; set; }

    /// <summary>EC des verwendeten Wassers in mS/cm, vor dem Duenger.</summary>
    public double? WaterEcMsCm { get; set; }

    [StringLength(4000)]
    public string? Notes { get; set; }
}

public sealed record ChangeoutDto(
    int Id,
    int GrowId,
    int? HydroSetupId,
    ChangeoutKind Kind,
    DateTime PerformedAtUtc,
    double? VolumeChangedLiters,
    double? PercentChanged,
    double? EcBefore,
    double? EcAfter,
    double? PhBefore,
    double? PhAfter,
    WaterSource? WaterUsed,
    double? WaterEcMsCm,
    string? Notes,
    DateTime CreatedAtUtc,
    bool ErinnerungNeuStarten = true);
