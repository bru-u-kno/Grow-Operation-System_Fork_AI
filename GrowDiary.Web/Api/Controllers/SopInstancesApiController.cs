using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Mapping;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

[ApiController]
[Route("api/sop-instances")]
[Produces("application/json")]
[KiStufe(KiStufe.Dokumentieren)]
public sealed class SopInstancesApiController : ApiControllerBase
{
    private readonly GrowRepository _repository;
    private readonly TaskRepository _taskRepository;
    private readonly KnowledgeBaseLoader _knowledgeBase;

    public SopInstancesApiController(GrowRepository repository, TaskRepository taskRepository, KnowledgeBaseLoader knowledgeBase)
    {
        _repository = repository;
        _taskRepository = taskRepository;
        _knowledgeBase = knowledgeBase;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SopInstanceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public ActionResult<IReadOnlyList<SopInstanceDto>> List([FromQuery] int growId)
    {
        if (_repository.GetGrow(growId) is null)
        {
            ModelState.AddModelError(nameof(StartSopInstanceRequest.GrowId), $"Grow mit Id {growId} existiert nicht.");
            return ValidationError();
        }

        return Ok(_repository.GetSopInstancesByGrow(growId).Select(instance => instance.ToDto()).ToList());
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(SopInstanceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<SopInstanceDto> Detail(int id)
    {
        var instance = _repository.GetSopInstance(id);
        return instance is null
            ? NotFoundError("sop_instance_not_found", $"SOP-Instanz mit Id {id} existiert nicht.")
            : Ok(instance.ToDto());
    }

    [HttpGet("{id:int}/steps")]
    [ProducesResponseType(typeof(IReadOnlyList<SopStepInstanceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<IReadOnlyList<SopStepInstanceDto>> Steps(int id)
    {
        if (_repository.GetSopInstance(id) is null)
        {
            return NotFoundError("sop_instance_not_found", $"SOP-Instanz mit Id {id} existiert nicht.");
        }

        return Ok(_repository.GetSopStepInstances(id).Select(step => step.ToDto()).ToList());
    }

    [HttpPut("steps/{stepInstanceId:int}")]
    [ProducesResponseType(typeof(SopStepInstanceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    public ActionResult<SopStepInstanceDto> UpdateStep(int stepInstanceId, [FromBody] UpdateSopStepInstanceRequest request)
    {
        if (_repository.GetSopStepInstance(stepInstanceId) is null)
        {
            return NotFoundError("sop_step_not_found", $"SOP-Step mit Id {stepInstanceId} existiert nicht.");
        }

        if (!Enum.IsDefined(request.Status))
        {
            ModelState.AddModelError(nameof(request.Status), "Status muss Pending, InProgress, Done oder Skipped sein.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        try
        {
            var step = _repository.UpdateSopStepInstance(
                stepInstanceId,
                request.Status,
                request.Notes,
                request.MeasurementId,
                request.JournalEntryId,
                request.PhotoAssetId);
            return Ok(step.ToDto());
        }
        catch (InvalidOperationException)
        {
            return ConflictError("sop_instance_not_active", "SOP-Instanz ist nicht aktiv und kann nicht mehr geaendert werden.");
        }
    }

    /// <summary>
    /// What the SOP needs to know before it can be planned — the branches it takes and the
    /// things it repeats for. Asked up front rather than half-way through, because finding
    /// out mid-treatment that a different path applied is exactly what a procedure is meant
    /// to prevent.
    /// </summary>
    [HttpGet("plan-questions/{sopId}")]
    [ProducesResponseType(typeof(SopPlanQuestionsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<SopPlanQuestionsDto> PlanQuestions(string sopId, [FromQuery] int? growId = null)
    {
        var sop = _knowledgeBase.Sops.FirstOrDefault(item => string.Equals(item.Id, sopId, StringComparison.OrdinalIgnoreCase));
        if (sop is null)
        {
            return NotFoundError("sop_not_found", $"SOP mit Id '{sopId}' existiert nicht.");
        }

        // Der Grow weiss laengst, womit angemischt wird — das Feld WaterSource
        // wird beim Anlegen abgefragt. Die waterSource-Frage der Ablaeufe jedes
        // Mal neu zu stellen hiess, eine gegebene Antwort zu ignorieren.
        var wasserVorschlag = growId is { } id ? WasserVorschlag(_repository.GetGrow(id)?.WaterSource) : null;

        var choices = SopStepPlanner.RequiredChoices(sop)
            .Select(choice => new SopChoiceDto(
                choice.Key,
                choice.Prompt,
                choice.Options,
                Suggested: string.Equals(choice.Key, "waterSource", StringComparison.OrdinalIgnoreCase)
                    && wasserVorschlag is { } vorschlag
                    && choice.Options.Contains(vorschlag, StringComparer.OrdinalIgnoreCase)
                        ? vorschlag
                        : null))
            .ToList();

        var subjects = sop.Steps
            .Where(step => !string.IsNullOrWhiteSpace(step.RepeatFor))
            .Select(step => step.RepeatFor!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Ok(new SopPlanQuestionsDto(sop.Id, choices, subjects));
    }

    /// <summary>
    /// Übersetzt die Wasserquelle des Grows in die Option der Abläufe.
    /// </summary>
    /// <remarks>
    /// Die Abläufe kennen zwei Wege: <c>ro</c> und <c>soft</c> („Weichwasser
    /// oder gemischtes Leitungswasser"). Leitungs- und Mischwasser werden also
    /// auf <c>soft</c> abgebildet. Öffentlich statisch, damit die Zuordnung
    /// testbar ist — sie entscheidet, welche Mischreihenfolge vorausgewählt wird.
    /// </remarks>
    public static string? WasserVorschlag(WaterSource? quelle) => quelle switch
    {
        WaterSource.RO => "ro",
        WaterSource.Tap or WaterSource.Mixed => "soft",
        _ => null,
    };

    [HttpPost("start")]
    [ProducesResponseType(typeof(SopInstanceDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    public ActionResult<SopInstanceDto> Start([FromBody] StartSopInstanceRequest request)
    {
        if (_repository.GetGrow(request.GrowId) is null)
        {
            return NotFoundError("grow_not_found", $"Grow mit Id {request.GrowId} existiert nicht.");
        }

        if (!Enum.IsDefined(request.Source))
        {
            ModelState.AddModelError(nameof(request.Source), "Source muss Manual, Recommendation oder System sein.");
        }

        var sop = _knowledgeBase.Sops.FirstOrDefault(item => string.Equals(item.Id, request.SopId, StringComparison.OrdinalIgnoreCase));
        if (sop is null)
        {
            ModelState.AddModelError(nameof(request.SopId), $"SOP mit Id '{request.SopId}' existiert nicht.");
        }
        else if (sop.Steps.Count == 0)
        {
            ModelState.AddModelError(nameof(request.SopId), $"SOP '{request.SopId}' hat keine ausfuehrbaren Steps.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        try
        {
            var instance = _repository.StartSopInstance(
                request.GrowId,
                sop!,
                request.Source,
                request.SourceRecommendationKey,
                request.TreatmentRecommendationStableKey,
                request.Notes,
                request.Answers,
                request.RepeatCounts);

            CreateReminderTasksForSteps(instance);

            return CreatedAtAction(nameof(Detail), new { id = instance.Id }, _repository.GetSopInstance(instance.Id)!.ToDto());
        }
        catch (InvalidOperationException)
        {
            return ConflictError("active_sop_exists", "Fuer diesen Grow ist diese SOP bereits aktiv.");
        }
    }

    private void CreateReminderTasksForSteps(SopInstance instance)
    {
        var steps = _repository.GetSopStepInstances(instance.Id);
        foreach (var step in steps.Where(s => s.DueAtUtc.HasValue))
        {
            var task = new GrowTask
            {
                GrowId = instance.GrowId,
                Title = $"SOP: {instance.SopName} \u2013 {step.Title}",
                DueAtUtc = step.DueAtUtc,
                Priority = TaskPriority.Normal,
                Status = GrowTaskStatus.Open
            };
            var taskId = _taskRepository.Create(task);
            _repository.UpdateSopStepReminderTaskId(step.Id, taskId);
        }
    }

    /// <summary>Einen Ablauf abbrechen.</summary>
    /// <remarks>
    /// <para>Wer den falschen Ablauf startet, hatte ihn bis zum 25.08.2026 fuer
    /// immer offen stehen — mitsamt seinen Erinnerungen in den Aufgaben.</para>
    ///
    /// <para><b>Die Erinnerungen gehen mit.</b> <c>ReminderTaskId</c> ist eine
    /// blosse Zahl ohne Fremdschluessel; die Datenbank raeumt dort nichts weg.
    /// Bliebe die Aufgabe stehen, erinnerte sie an einen Ablauf, den es nicht
    /// mehr gibt.</para>
    /// </remarks>
    [HttpDelete("{id:int}")]
    [KiSicherungVorher]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public IActionResult Delete(int id)
    {
        if (_repository.GetSopInstance(id) is null)
        {
            return NotFoundError("sop_instance_not_found", $"Ablauf mit Id {id} existiert nicht.");
        }

        foreach (var aufgabeId in _repository.DeleteSopInstance(id))
        {
            _taskRepository.Delete(aufgabeId);
        }

        return NoContent();
    }

}
