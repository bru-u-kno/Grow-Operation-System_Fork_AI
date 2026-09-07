using GrowDiary.Web.Models;
using GrowDiary.Web.Services.Knowledge;
using GrowDiary.Web.Services.Knowledge.Schema;

namespace GrowDiary.Web.Services;

public sealed class CultivationKnowledgeService
{
    private readonly List<NutrientProgram> _programs;
    private readonly List<MediumPlaybook> _mediumPlaybooks;

    public CultivationKnowledgeService(KnowledgeBaseLoader knowledgeBase)
    {
        _programs = knowledgeBase.NutrientPrograms.Select(MapProgram).ToList();
        // TODO Sprint B: MediumPlaybook in JSON-Knowledge-Base überführen,
        // sobald das Multi-Setup-Datenmodell steht.
        _mediumPlaybooks = BuildMediumPlaybooks();
    }

    public IReadOnlyList<NutrientProgram> GetPrograms() => _programs;
    public IReadOnlyList<MediumPlaybook> GetMediumPlaybooks() => _mediumPlaybooks;

    public NutrientProgram? MatchProgram(string? nutrientText)
    {
        if (string.IsNullOrWhiteSpace(nutrientText)) return null;
        var text = nutrientText.Trim();

        // Fork AI: exakter Name oder Key gewinnt immer.
        var exact = _programs.FirstOrDefault(x =>
            string.Equals(x.Name, text, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.Key, text, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        // Fork AI: bei mehreren Treffern der längste enthaltene Programmname —
        // sonst fällt „SKX Canna Aqua …" auf „Canna Aqua" zurück.
        var lower = text.ToLowerInvariant();
        return _programs
            .Where(x => x.Matches(text))
            .OrderByDescending(x => lower.Contains(x.Name.ToLowerInvariant()) ? x.Name.Length : 0)
            .FirstOrDefault();
    }

    private static NutrientProgram MapProgram(NutrientProgramDefinition def) => new()
    {
        Key = def.Id,
        Name = def.Name,
        Manufacturer = def.Manufacturer,
        Category = def.Category,
        Summary = def.Summary,
        BestFor = def.BestFor,
        WaterGuidance = def.WaterGuidance,
        PhGuidance = def.PhGuidance,
        EcGuidance = def.EcGuidance,
        ScheduleStyle = def.ScheduleStyle,
        OfficialHighlights = def.OfficialHighlights,
        PracticeNotes = def.PracticeNotes,
        Stages = def.Stages.Select(s => new NutrientProgramStage
        {
            Stage = s.Stage,
            Dose = s.Dose,
            Target = s.Target,
            Notes = s.Notes
        }).ToList(),
        Tips = def.Tips,
        SearchTerms = def.SearchTerms,
        FeedChart = def.FeedChart
    };

    private static List<MediumPlaybook> BuildMediumPlaybooks()
    {
        return new List<MediumPlaybook>
        {
            new()
            {
                Key = "hydro",
                Title = "Hydro / DWC / RDWC",
                Summary = "In RDWC/DWC zählt nie nur ein Einzelwert. Entscheidend sind Reservoir-pH, Reservoir-EC, Wasserstand, Wassertemperatur, DO, ORP, Addback und die Frage, ob sich diese Werte logisch miteinander bewegen.",
                FocusPoints = new() { "Reservoir-pH 5,8–6,2", "Reservoir-EC passend zur Phase", "Wasserstand in L/cm", "Wassertemperatur ideal 19–20 °C", "DO ideal >= 7 mg/L", "ORP meist 350–450 mV", "Addback-EC & Top-Off", "wöchentliche Changeouts" },
                RedFlags = new() { "pH und EC steigen gleichzeitig", "Wassertemperatur > 21–22 °C", "DO < 7 mg/L", "ORP < 350 mV oder > 500 mV", "EC steigt bei sinkendem Wasserstand", "hoher Buffer-Bedarf trotz Korrektur", "biofilmiger / fauliger Geruch" }
            },
        };
    }
}
