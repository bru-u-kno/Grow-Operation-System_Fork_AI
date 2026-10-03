namespace GrowDiary.Web.Models;

/// <summary>
/// A single Home Assistant entity as returned by <c>GET /api/states</c>, reduced
/// to the fields Grow OS needs to offer a searchable sensor picker (friendly
/// name, current value, unit and device class for filtering).
/// </summary>
public sealed class HomeAssistantEntity
{
    public required string EntityId { get; init; }
    public string? FriendlyName { get; init; }
    public string? State { get; init; }
    public string? UnitOfMeasurement { get; init; }
    public string? DeviceClass { get; init; }

    /// <summary>
    /// Fork AI (01.10.2026): Bei einer Automation die Kennung ihrer Konfiguration
    /// (<c>attributes.id</c>, zugleich ihre <c>unique_id</c>). Home Assistant leitet
    /// die Entity-ID aus dem Namen ab, nicht aus dieser Kennung — wer eine vom Fork
    /// angelegte Automation wiederfinden will, sucht deshalb hiernach.
    /// </summary>
    public string? KonfigKennung { get; init; }

    /// <summary>
    /// Fork AI (A-003 Etappe B, 03.10.2026): Wann sich der Zustand zuletzt geändert hat
    /// (<c>last_changed</c>, UTC). Für <c>GET /api/ki-ha/zustaende</c>; null im Testbetrieb.
    /// </summary>
    public DateTime? LastChangedUtc { get; init; }

    /// <summary>The entity domain (the part before the first dot, e.g. "sensor").</summary>
    public string Domain { get; init; } = string.Empty;
}
