using System.Globalization;
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

        if (!Enum.IsDefined(request.EntryType))
        {
            ModelState.AddModelError(nameof(request.EntryType), "Diese Art kennt das Journal nicht.");
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

    /// <summary>Einen Journaleintrag nachträglich ändern.</summary>
    /// <remarks>
    /// Anlass (04.10.2026): ein Wasserwechsel stand mit „pH- Menge nicht
    /// notiert" im Journal, und die Menge ließ sich nicht nachtragen — es gab
    /// nur Anlegen und Entfernen. Felder, die <c>null</c> bleiben, behalten
    /// ihren Wert (siehe <see cref="JournalEntryUpdateRequest"/>); Herkunft,
    /// Messungsbezug und Fotos bleiben unberührt.
    /// </remarks>
    [HttpPut("journal/{entryId:int}")]
    [ProducesResponseType(typeof(JournalEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<JournalEntryDto> Update(int entryId, [FromBody] JournalEntryUpdateRequest request)
    {
        var eintrag = _journalRepository.Get(entryId);
        if (eintrag is null)
        {
            return NotFoundError("journal_entry_not_found", $"Journaleintrag mit Id {entryId} existiert nicht.");
        }

        if (request.Title is not null)
        {
            eintrag.Title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim();
        }

        if (request.Body is not null)
        {
            eintrag.Body = string.IsNullOrWhiteSpace(request.Body) ? null : request.Body.Trim();
        }

        if (request.EntryType is { } art)
        {
            // Eine Zahl wie 99 bindet ASP.NET still als Enum — gespeichert
            // waere sie eine Art, die keine Oberflaeche kennt (Pruefer 05.10.2026).
            if (!Enum.IsDefined(art))
            {
                ModelState.AddModelError(nameof(request.EntryType), "Diese Art kennt das Journal nicht.");
                return ValidationError();
            }

            eintrag.EntryType = art;
        }

        if (request.OccurredAtLocal is not null)
        {
            if (!DateTime.TryParse(request.OccurredAtLocal, CultureInfo.InvariantCulture, DateTimeStyles.None, out var ortszeit))
            {
                ModelState.AddModelError(nameof(request.OccurredAtLocal), "Datum oder Uhrzeit konnten nicht gelesen werden.");
                return ValidationError();
            }

            eintrag.OccurredAtUtc = DateTime.SpecifyKind(ortszeit, DateTimeKind.Local).ToUniversalTime();
        }

        // Dieselbe Regel wie beim Anlegen — geprüft am Ergebnis, nicht an der
        // Anfrage: wer nur den Text leert, darf den Titel behalten.
        if (string.IsNullOrWhiteSpace(eintrag.Title) && string.IsNullOrWhiteSpace(eintrag.Body))
        {
            ModelState.AddModelError(nameof(request.Body), "Bitte gib mindestens einen Titel oder Text ein.");
            return ValidationError();
        }

        if (!_journalRepository.Update(eintrag))
        {
            return NotFoundError("journal_entry_not_found", $"Journaleintrag mit Id {entryId} existiert nicht.");
        }

        _auditRepository.LogJournalUpdated(eintrag.GrowId, entryId, eintrag.Title, eintrag.EntryType);
        return Ok(eintrag.ToDto());
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
