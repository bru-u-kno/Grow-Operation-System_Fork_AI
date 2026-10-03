using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>
/// The pheno hunt: every plant of a grow with its score sheet and the resulting weighted
/// score, so siblings can be compared and a keeper picked.
/// </summary>
[ApiController]
[Route("api/pheno")]
[Produces("application/json")]
[KiStufe(KiStufe.Dokumentieren)]
public sealed class PhenoApiController : ApiControllerBase
{
    private readonly GrowRepository _repository;
    private readonly PhenoRepository _pheno;

    public PhenoApiController(GrowRepository repository, PhenoRepository pheno)
    {
        _repository = repository;
        _pheno = pheno;
    }

    [HttpGet("grows/{growId:int}")]
    [ProducesResponseType(typeof(PhenoHuntDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<PhenoHuntDto> Hunt(int growId)
    {
        if (_repository.GetGrow(growId) is null)
        {
            return NotFoundError("grow_not_found", $"Grow mit Id {growId} existiert nicht.");
        }

        var plants = _repository.GetPlantsByGrow(growId);
        var sheets = _pheno.GetForGrow(growId).ToDictionary(sheet => sheet.PlantInstanceId);
        var weights = _pheno.GetWeights();

        // Bewertet wird JE SORTE, nicht je Grow.
        //
        // Ertrag und Wirkstoff werden relativ gerechnet: die beste Pflanze im
        // Feld bekommt 1, die schwaechste 0. Stehen drei Sorten im selben Zelt,
        // vergleicht das nicht mehr Phaenotypen, sondern Genetiken — eine Sorte
        // mit von Haus aus weniger THC bekaeme die 0, ohne dass ihr bester
        // Phaenotyp etwas dafuer kann. Genau das ist einem Tester passiert:
        // sechs Pflanzen aus drei Sorten landeten in einem Topf.
        //
        // Pflanzen ohne Sorte bilden ihre eigene Gruppe — sie gegen benannte
        // Sorten zu normieren waere dieselbe Vermischung.
        var scores = plants
            .GroupBy(plant => plant.StrainId)
            .SelectMany(gruppe => PhenoScoreCalculator.Score(
                gruppe
                    .Select(plant => sheets.TryGetValue(plant.Id, out var sheet) ? sheet : new PhenoEvaluation { PlantInstanceId = plant.Id })
                    .ToList(),
                weights))
            .ToDictionary(score => score.PlantInstanceId);

        var entries = plants.Select(plant =>
        {
            var sheet = sheets.TryGetValue(plant.Id, out var found) ? found : null;
            var score = scores[plant.Id];
            return new PhenoPlantDto(
                plant.Id,
                plant.Label,
                plant.PhenoLabel,
                plant.StrainName,
                plant.StrainId,
                plant.PlantRole.ToString(),
                plant.PlantStatus.ToString(),
                plant.ParentPlantId,
                sheet is null ? null : ToDto(sheet),
                new PhenoScoreDto(score.Total, score.Yield, score.Quality, score.Potency, score.Resilience, score.Structure, score.IsManual));
        }).ToList();

        return Ok(new PhenoHuntDto(growId, ToDto(weights), entries));
    }

    [HttpPut("plants/{plantId:int}")]
    [ProducesResponseType(typeof(PhenoEvaluationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<PhenoEvaluationDto> SaveSheet(int plantId, [FromBody] PhenoEvaluationDto request)
    {
        if (_repository.GetPlant(plantId) is null)
        {
            return NotFoundError("plant_not_found", $"Pflanze mit Id {plantId} existiert nicht.");
        }

        foreach (var (name, value) in new (string, int?)[]
                 {
                     (nameof(request.VigorScore), request.VigorScore),
                     (nameof(request.BranchingScore), request.BranchingScore),
                     (nameof(request.LeafToBudScore), request.LeafToBudScore),
                     (nameof(request.TrainingResponseScore), request.TrainingResponseScore),
                     (nameof(request.StressToleranceScore), request.StressToleranceScore),
                     (nameof(request.BudDensityScore), request.BudDensityScore),
                     (nameof(request.ResinScore), request.ResinScore),
                     (nameof(request.TrimEaseScore), request.TrimEaseScore),
                     (nameof(request.AromaScore), request.AromaScore),
                     (nameof(request.FlavorScore), request.FlavorScore),
                     (nameof(request.EffectScore), request.EffectScore),
                 })
        {
            if (value is { } score && score is < 1 or > 10)
            {
                ModelState.AddModelError(name, "Bewertungen liegen zwischen 1 und 10.");
            }
        }

        if (request.PestResistanceScore is { } pest && pest is < 1 or > 5)
        {
            ModelState.AddModelError(nameof(request.PestResistanceScore), "Schaedlingsdruck wird von 1 bis 5 bewertet.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var saved = _pheno.Save(new PhenoEvaluation
        {
            PlantInstanceId = plantId,
            VigorScore = request.VigorScore,
            InternodeSpacing = Enum.TryParse<InternodeSpacing>(request.InternodeSpacing, true, out var spacing) ? spacing : InternodeSpacing.Unknown,
            BranchingScore = request.BranchingScore,
            LeafToBudScore = request.LeafToBudScore,
            HeightAtFlipCm = request.HeightAtFlipCm,
            TrainingMethods = request.TrainingMethods is { Count: > 0 } ? string.Join('\n', request.TrainingMethods) : null,
            TrainingResponseScore = request.TrainingResponseScore,
            StressToleranceScore = request.StressToleranceScore,
            PestResistanceScore = request.PestResistanceScore,
            FloweringDays = request.FloweringDays,
            HeightAtHarvestCm = request.HeightAtHarvestCm,
            WetYieldG = request.WetYieldG,
            DryYieldG = request.DryYieldG,
            BudDensityScore = request.BudDensityScore,
            ResinScore = request.ResinScore,
            TrimEaseScore = request.TrimEaseScore,
            AromaScore = request.AromaScore,
            AromaNotes = request.AromaNotes,
            FlavorScore = request.FlavorScore,
            EffectScore = request.EffectScore,
            EffectNotes = request.EffectNotes,
            ThcPercent = request.ThcPercent,
            CbdPercent = request.CbdPercent,
            TerpeneNotes = request.TerpeneNotes,
            ManualOverallScore = request.ManualOverallScore,
            IsKeeper = request.IsKeeper,
            ConfirmedInSecondRun = request.ConfirmedInSecondRun,
            Notes = request.Notes,
        });

        return Ok(ToDto(saved));
    }

    [HttpGet("weights")]
    [ProducesResponseType(typeof(PhenoWeightsDto), StatusCodes.Status200OK)]
    public ActionResult<PhenoWeightsDto> GetWeights() => Ok(ToDto(_pheno.GetWeights()));

    // KI-Zugriff: Verwaltung — die Gewichtung gilt für jede Bewertung (Einstellung).
    [HttpPut("weights")]
    [KiStufe(KiStufe.Verwaltung)]
    [ProducesResponseType(typeof(PhenoWeightsDto), StatusCodes.Status200OK)]
    public ActionResult<PhenoWeightsDto> SaveWeights([FromBody] PhenoWeightsDto request)
    {
        var weights = new PhenoWeights(request.Yield, request.Quality, request.Potency, request.Resilience, request.Structure);
        _pheno.SaveWeights(weights);
        return Ok(ToDto(_pheno.GetWeights()));
    }

    private static PhenoWeightsDto ToDto(PhenoWeights weights)
        => new(weights.Yield, weights.Quality, weights.Potency, weights.Resilience, weights.Structure);

    private static PhenoEvaluationDto ToDto(PhenoEvaluation e) => new(
        e.PlantInstanceId,
        e.VigorScore,
        e.InternodeSpacing.ToString(),
        e.BranchingScore,
        e.LeafToBudScore,
        e.HeightAtFlipCm,
        string.IsNullOrWhiteSpace(e.TrainingMethods)
            ? new List<string>()
            : e.TrainingMethods.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
        e.TrainingResponseScore,
        e.StressToleranceScore,
        e.PestResistanceScore,
        e.FloweringDays,
        e.HeightAtHarvestCm,
        e.WetYieldG,
        e.DryYieldG,
        e.BudDensityScore,
        e.ResinScore,
        e.TrimEaseScore,
        e.AromaScore,
        e.AromaNotes,
        e.FlavorScore,
        e.EffectScore,
        e.EffectNotes,
        e.ThcPercent,
        e.CbdPercent,
        e.TerpeneNotes,
        e.ManualOverallScore,
        e.IsKeeper,
        e.ConfirmedInSecondRun,
        e.Notes,
        e.StretchFactor);
}
