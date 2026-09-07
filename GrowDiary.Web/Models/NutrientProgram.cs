using GrowDiary.Web.Services.Knowledge.Schema;

namespace GrowDiary.Web.Models;

public sealed class NutrientProgram
{
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Manufacturer { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string BestFor { get; init; } = string.Empty;
    public string WaterGuidance { get; init; } = string.Empty;
    public string PhGuidance { get; init; } = string.Empty;
    public string EcGuidance { get; init; } = string.Empty;
    public string ScheduleStyle { get; init; } = string.Empty;
    public string OfficialHighlights { get; init; } = string.Empty;
    public string PracticeNotes { get; init; } = string.Empty;
    public List<NutrientProgramStage> Stages { get; init; } = new();
    public List<string> Tips { get; init; } = new();
    public List<string> SearchTerms { get; init; } = new();
    /// <summary>Fork AI: Wochen-Feed-Chart der Definition (null, wenn das Programm keins hat).</summary>
    public FeedChartDefinition? FeedChart { get; init; }

    // Backward-compatible aliases for older views.
    public string Description => Summary;
    public IReadOnlyList<NutrientProgramPhase> Phases
        => Stages.Select(x => new NutrientProgramPhase
        {
            Name = x.Stage,
            Dose = x.Dose,
            Target = x.Target,
            Notes = x.Notes
        }).ToList();

    public bool Matches(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Trim().ToLowerInvariant();
        return normalized.Contains(Name.ToLowerInvariant()) || SearchTerms.Any(x => normalized.Contains(x.ToLowerInvariant()));
    }
}

public sealed class NutrientProgramStage
{
    public string Stage { get; init; } = string.Empty;
    public string Dose { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
}

public sealed class NutrientProgramPhase
{
    public string Name { get; init; } = string.Empty;
    public string Dose { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
}

public sealed class MediumPlaybook
{
    public string Key { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public List<string> FocusPoints { get; init; } = new();
    public List<string> RedFlags { get; init; } = new();

    // Backward-compatible alias for older views.
    public IReadOnlyList<string> Tips => FocusPoints.Concat(RedFlags).ToList();
}
