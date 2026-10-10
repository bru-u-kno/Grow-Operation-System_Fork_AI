namespace GrowDiary.Web.Models;

public sealed class AddbackLogEntry
{
    public int Id { get; set; }
    public int GrowId { get; set; }
    public int? HydroSetupId { get; set; }
    public AddbackLogKind Kind { get; set; } = AddbackLogKind.Addback;
    public DateTime PerformedAtUtc { get; set; } = DateTime.UtcNow;
    public double? ReservoirLiters { get; set; }
    public double? EcBefore { get; set; }
    public double? EcTarget { get; set; }
    public double? EcStock { get; set; }
    public double? EcAfter { get; set; }
    public double? PhBefore { get; set; }
    public double? PhAfter { get; set; }
    public double? LitersAdded { get; set; }
    public double? NewReservoirVolumeLiters { get; set; }
    public bool UsedHydroSetupVolume { get; set; }

    /// <summary>Womit aufgefuellt wurde — null heisst „nicht festgehalten".</summary>
    /// <remarks>
    /// Der Grow traegt eine Wasserquelle, aber die gilt fuer den ganzen Lauf.
    /// Wer einmal mit Leitungswasser nachfuellt, weil der Osmose-Tank leer war,
    /// soll genau das hier stehen haben — sonst erklaert spaeter niemand mehr
    /// den EC-Sprung.
    /// </remarks>
    public WaterSource? WaterUsed { get; set; }

    /// <summary>EC des verwendeten Wassers in mS/cm, vor dem Duenger.</summary>
    public double? WaterEcMsCm { get; set; }

    /// <summary>Wasserverbrauch seit dem letzten Nachfüllen oder Wasserwechsel in Litern — nur zum Festhalten, wenn der Nutzer ihn kennt.</summary>
    /// <remarks>
    /// Die Rechnung braucht ihn nicht: Sie startet bei den Messwerten, in denen der
    /// Verbrauch schon steckt. Er dient der Statistik und als Gegenprobe zur nachgefüllten Menge.
    /// </remarks>
    public double? ConsumedLiters { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
