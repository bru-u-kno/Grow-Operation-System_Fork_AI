using System.Text.Json.Serialization;

namespace GrowDiary.Web.Models;

/// <summary>What a tile shows: a value Cultivate Control knows, or any Home Assistant entity.</summary>
public enum DashboardTileKind
{
    /// <summary>One of Cultivate Control's own metric keys (temperature, reservoir-ph, …).</summary>
    Metric,
    /// <summary>Any Home Assistant entity — including ones Cultivate Control knows nothing about.</summary>
    Entity,
    /// <summary>A camera entity, shown as a refreshing still image.</summary>
    Camera,
    /// <summary>Mehrere Messwerte als 24-Stunden-Verlauf in einem Bild.</summary>
    Chart
}

public sealed class DashboardTile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public DashboardTileKind Kind { get; set; } = DashboardTileKind.Metric;

    /// <summary>Set for <see cref="DashboardTileKind.Metric"/>.</summary>
    public string? MetricKey { get; set; }

    /// <summary>Set for <see cref="DashboardTileKind.Entity"/>.</summary>
    public string? EntityId { get; set; }

    /// <summary>
    /// Welche Messwerte die Verlaufs-Kachel zeichnet.
    /// </summary>
    /// <remarks>
    /// Nur für <see cref="DashboardTileKind.Chart"/>. Eine eigene Liste statt
    /// des einzelnen <see cref="MetricKey"/>, weil genau das Zusammensehen den
    /// Nutzen ausmacht: Temperatur, Feuchte und VPD nebeneinander erzählen
    /// etwas, das drei getrennte Kurven nicht erzählen.
    /// </remarks>
    public List<string>? MetricKeys { get; set; }

    /// <summary>Overrides the automatic caption; null keeps the default label.</summary>
    public string? Label { get; set; }

    /// <summary>Overrides the unit reported by Home Assistant.</summary>
    public string? Unit { get; set; }

    /// <summary>
    /// How many of the three grid columns the tile occupies. A value tile is fine at 1;
    /// a camera needs the room.
    /// </summary>
    public int Span { get; set; } = 1;
}

public sealed class DashboardSection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; set; } = "Bereich";
    public List<DashboardTile> Tiles { get; set; } = [];
}

/// <summary>A tent's dashboard arrangement. Absent = the built-in default is used.</summary>
public sealed class DashboardLayout
{
    /// <summary>
    /// What the editor that wrote this layout looked like.
    /// </summary>
    /// <remarks>
    /// Layouts written before the Live screen was rebuilt (version 0) are not revived:
    /// they were arranged against a different screen, are missing everything added since,
    /// and some hold camera tiles the screen no longer has a place for — the camera now
    /// has its own stage. Reviving one on update would silently take values off someone's
    /// dashboard. They are kept, not deleted; the moment the user arranges anything, the
    /// layout is rewritten at the current version.
    /// </remarks>
    public const int CurrentVersion = 2;

    public int TentId { get; set; }
    public int Version { get; set; }
    public List<DashboardSection> Sections { get; set; } = [];

    [JsonIgnore]
    public bool IsEmpty => Sections.Count == 0 || Sections.All(section => section.Tiles.Count == 0);

    /// <summary>
    /// What Cultivate Control shows out of the box — the same climate and reservoir grouping the
    /// dashboard always had, now as an editable starting point rather than fixed code.
    /// </summary>
    public static DashboardLayout Default(int tentId) => new()
    {
        TentId = tentId,
        Version = CurrentVersion,
        Sections =
        [
            new DashboardSection
            {
                Id = "climate",
                Title = "Klima",
                Tiles = Metrics("temperature", "humidity", "vpd", "light-cycle", "ppfd", "co2"),
            },
            new DashboardSection
            {
                Id = "reservoir",
                Title = "Reservoir",
                Tiles = Metrics("reservoir-ph", "reservoir-ec", "reservoir-temp", "reservoir-level", "reservoir-level-cm", "orp", "dissolved-oxygen"),
            },
        ],
    };

    private static List<DashboardTile> Metrics(params string[] keys) => keys
        .Select(key => new DashboardTile { Id = key, Kind = DashboardTileKind.Metric, MetricKey = key })
        .ToList();
}
