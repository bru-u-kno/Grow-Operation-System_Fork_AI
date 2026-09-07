namespace GrowDiary.Web.Api.Contracts;

public sealed record KnowledgeOverviewDto(
    IReadOnlyList<NutrientProgramDto> Programs,
    IReadOnlyList<MediumPlaybookDto> Playbooks
);

public sealed record NutrientProgramDto(
    string Key,
    string Name,
    string Manufacturer,
    string Category,
    string Summary,
    string BestFor,
    string WaterGuidance,
    string PhGuidance,
    string EcGuidance,
    IReadOnlyList<NutrientProgramStageDto> Stages,
    IReadOnlyList<string> Tips,
    FeedChartDto? FeedChart = null
);

/// <summary>Fork AI: Wochen-Feed-Chart eines Programms für die Wissensseite (ml je Liter je Spalte).</summary>
public sealed record FeedChartDto(
    string Unit,
    string? Note,
    IReadOnlyList<FeedChartColumnDto> Columns
);

public sealed record FeedChartColumnDto(
    string Id,
    string Label,
    string Stage,
    int? Week,
    IReadOnlyList<FeedChartItemDto> Items,
    double? EcTarget,
    double? PhMin,
    double? PhMax
);

public sealed record FeedChartItemDto(
    string Component,
    double MinMlPerLiter,
    double MaxMlPerLiter
);

public sealed record NutrientProgramStageDto(
    string Stage,
    string Dose,
    string Target,
    string Notes
);

public sealed record MediumPlaybookDto(
    string Key,
    string Title,
    string Summary,
    IReadOnlyList<string> FocusPoints,
    IReadOnlyList<string> RedFlags
);
