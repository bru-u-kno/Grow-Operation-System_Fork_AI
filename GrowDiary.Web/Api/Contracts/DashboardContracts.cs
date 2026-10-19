namespace GrowDiary.Web.Api.Contracts;

public sealed record DashboardTileDto(
    string? Id,
    string? Kind,
    string? MetricKey,
    string? EntityId,
    string? Label,
    string? Unit,
    int? Span = 1,
    /// <summary>Nur für Kind = Chart: welche Messwerte zusammen gezeichnet werden.</summary>
    List<string>? MetricKeys = null);

public sealed record DashboardSectionDto(
    string? Id,
    string? Title,
    List<DashboardTileDto>? Tiles);

public sealed record DashboardLayoutDto(
    int TentId,
    List<DashboardSectionDto>? Sections,
    /// <summary>
    /// True once the user arranged this tent themselves. False means these sections are
    /// what Cultivate Control ships — the screen then draws its own built-in arrangement, which
    /// knows things a stored layout cannot (which water-level unit actually reports).
    /// </summary>
    bool IsCustom = false);

/// <summary>The live reading of a Home Assistant entity a custom tile points at.</summary>
public sealed record DashboardEntityValueDto(
    string EntityId,
    string? FriendlyName,
    string? State,
    string? Unit);
