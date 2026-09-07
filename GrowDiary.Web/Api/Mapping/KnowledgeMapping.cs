using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Api.Mapping;

public static class KnowledgeMapping
{
    public static NutrientProgramDto ToDto(this NutrientProgram program) => new(
        Key: program.Key,
        Name: program.Name,
        Manufacturer: program.Manufacturer,
        Category: program.Category,
        Summary: program.Summary,
        BestFor: program.BestFor,
        WaterGuidance: program.WaterGuidance,
        PhGuidance: program.PhGuidance,
        EcGuidance: program.EcGuidance,
        Stages: program.Stages.Select(stage => new NutrientProgramStageDto(
            stage.Stage,
            stage.Dose,
            stage.Target,
            stage.Notes)).ToList(),
        Tips: program.Tips.ToList(),
        FeedChart: program.FeedChart is { Columns.Count: > 0 } chart
            ? new FeedChartDto(
                chart.Unit,
                chart.Note,
                chart.Columns.Select(c => new FeedChartColumnDto(
                    c.Id, c.Label, c.Stage, c.Week,
                    c.Items.Select(i => new FeedChartItemDto(i.Component, i.MinMlPerLiter, i.MaxMlPerLiter)).ToList(),
                    c.EcTarget, c.PhMin, c.PhMax)).ToList())
            : null
    );

    public static MediumPlaybookDto ToDto(this MediumPlaybook playbook) => new(
        Key: playbook.Key,
        Title: playbook.Title,
        Summary: playbook.Summary,
        FocusPoints: playbook.FocusPoints.ToList(),
        RedFlags: playbook.RedFlags.ToList()
    );
}
