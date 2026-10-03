using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Mapping;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

[ApiController]
[Route("api")]
[Produces("application/json")]
[KiStufe(KiStufe.Dokumentieren)]
public sealed class JournalApiController : ApiControllerBase
{
    private readonly GrowRepository _growRepository;
    private readonly JournalRepository _journalRepository;
    private readonly AuditRepository _auditRepository;

    public JournalApiController(
        GrowRepository growRepository,
        JournalRepository journalRepository,
        AuditRepository auditRepository)
    {
        _growRepository = growRepository;
        _journalRepository = journalRepository;
        _auditRepository = auditRepository;
    }

    [HttpGet("grows/{growId:int}/journal")]
    [ProducesResponseType(typeof(IReadOnlyList<JournalEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<IReadOnlyList<JournalEntryDto>> List(int growId)
    {
        var grow = _growRepository.GetGrow(growId);
        if (grow is null)
        {
            return NotFoundError("grow_not_found", $"Grow mit Id {growId} existiert nicht.");
        }

        return Ok(_journalRepository.GetForGrow(growId).Select(entry => entry.ToDto()).ToList());
    }

    [HttpGet("journal/{entryId:int}")]
    [ProducesResponseType(typeof(JournalEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<JournalEntryDto> Detail(int entryId)
    {
        var entry = _journalRepository.Get(entryId);
        if (entry is null)
        {
            return NotFoundError("journal_entry_not_found", $"Journal-Eintrag mit Id {entryId} existiert nicht.");
        }

        return Ok(entry.ToDto());
    }

    [HttpPost("grows/{growId:int}/journal")]
    [ProducesResponseType(typeof(JournalEntryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<JournalEntryDto> Create(int growId, [FromBody] JournalEntryCreateRequest request)
    {
        var grow = _growRepository.GetGrow(growId);
        if (grow is null)
        {
            return NotFoundError("grow_not_found", $"Grow mit Id {growId} existiert nicht.");
        }

        if (string.IsNullOrWhiteSpace(request.Title) && string.IsNullOrWhiteSpace(request.Body))
        {
            ModelState.AddModelError(nameof(request.Body), "Bitte gib mindestens einen Titel oder Text ein.");
            return ValidationError();
        }

        try
        {
            var model = request.ToModel(growId);
            model.Id = _journalRepository.Create(model);
            _auditRepository.LogJournalCreated(growId, model.Id, model.Title, model.EntryType);

            return CreatedAtAction(nameof(Detail), new { entryId = model.Id }, model.ToDto());
        }
        catch
        {
            ModelState.AddModelError(nameof(request.OccurredAtLocal), "Datum oder Uhrzeit konnten nicht gelesen werden.");
            return ValidationError();
        }
    }

    /// <summary>Einen Journaleintrag entfernen.</summary>
    /// <remarks>
    /// Ein Journal ist ein Tagebuch, kein Gesetzblatt. Wer den falschen Grow
    /// erwischt oder sich vertippt, muss den Eintrag loswerden — bis zum
    /// 25.08.2026 ging das nirgends.
    /// </remarks>
    [HttpDelete("journal/{entryId:int}")]
    [KiSicherungVorher]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public IActionResult DeleteEntry(int entryId)
    {
        var eintrag = _journalRepository.Get(entryId);
        if (eintrag is null)
        {
            return NotFoundError("journal_entry_not_found", $"Journaleintrag mit Id {entryId} existiert nicht.");
        }

        _journalRepository.Delete(entryId);
        _auditRepository.Add(new AuditEntry
        {
            GrowId = eintrag.GrowId,
            EntityType = "JournalEntry",
            EntityId = entryId,
            Action = "Journaleintrag entfernt",
            Summary = $"'{eintrag.Title}' wurde entfernt.",
        });
        return NoContent();
    }

}
