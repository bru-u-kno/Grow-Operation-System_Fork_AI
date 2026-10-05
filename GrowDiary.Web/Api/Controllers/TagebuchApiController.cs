using System.Globalization;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Services.Tagebuch;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>
/// Das Grow-Tagebuch (A-006): der Tagesstrom eines Grows, die Sensorkurven eines
/// Tages und „War nichts" für eine Auffälligkeit.
/// </summary>
/// <remarks>
/// Lesen braucht keine Stufe. Das Ausblenden einer Auffälligkeit ist
/// Dokumentieren — es sagt etwas über das Becken aus („da war nichts"), wie ein
/// Journaleintrag, und lässt sich zurücknehmen.
/// </remarks>
[ApiController]
[Route("api")]
[Produces("application/json")]
[KiStufe(KiStufe.Dokumentieren)]
public sealed class TagebuchApiController : ApiControllerBase
{
    private readonly TagebuchService _tagebuch;

    public TagebuchApiController(TagebuchService tagebuch)
    {
        _tagebuch = tagebuch;
    }

    /// <summary>Tage mit Einträgen, neueste zuerst; ältere über <paramref name="bis"/>.</summary>
    /// <param name="growId">Der Grow.</param>
    /// <param name="bis">Ortstag yyyy-MM-dd: diesen und ältere liefern. Leer = ab heute.</param>
    /// <param name="tage">Wie viele Tage mit Einträgen (1–31).</param>
    /// <remarks>
    /// Auf der ersten Seite erkennt der Aufruf frische Sprünge in den Rohwerten
    /// und merkt sie sich (<see cref="TagebuchService.Erkennen"/>). Das ist eine
    /// Ableitung aus vorhandenen Daten, kein Eintrag des Bedieners — ein zweiter
    /// Aufruf ändert nichts mehr.
    /// </remarks>
    [HttpGet("grows/{growId:int}/tagebuch")]
    [ProducesResponseType(typeof(TagebuchSeiteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<TagebuchSeiteDto> Seite(int growId, [FromQuery] string? bis = null, [FromQuery] int tage = TagebuchService.TageJeSeite)
    {
        DateOnly? bisTag = null;
        if (!string.IsNullOrWhiteSpace(bis))
        {
            if (!DateOnly.TryParseExact(bis, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var tag))
            {
                return BadRequestError("tag_ungueltig", "Der Tag muss als yyyy-MM-dd angegeben werden.");
            }

            bisTag = tag;
        }

        var seite = _tagebuch.Seite(growId, bisTag, tage, DateTime.UtcNow);
        return seite is null
            ? NotFoundError("grow_not_found", $"Grow mit Id {growId} existiert nicht.")
            : Ok(seite);
    }

    /// <summary>Die sechs Sensorkurven eines Tages, mit Lichtphase.</summary>
    [HttpGet("grows/{growId:int}/tagebuch/kurven")]
    [ProducesResponseType(typeof(TagebuchKurvenDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<TagebuchKurvenDto> Kurven(int growId, [FromQuery] string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)
            || !DateOnly.TryParseExact(tag, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var datum))
        {
            return BadRequestError("tag_ungueltig", "Der Tag muss als yyyy-MM-dd angegeben werden.");
        }

        var kurven = _tagebuch.Kurven(growId, datum);
        return kurven is null
            ? NotFoundError("grow_not_found", $"Grow mit Id {growId} existiert nicht.")
            : Ok(kurven);
    }

    /// <summary>„War nichts": eine Auffälligkeit ausblenden — oder wieder zeigen.</summary>
    [HttpPut("tagebuch/auffaelligkeiten/{id:int}")]
    [ProducesResponseType(typeof(TagebuchAuffaelligkeitStandDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<TagebuchAuffaelligkeitStandDto> Verwerfen(int id, [FromBody] TagebuchVerwerfenRequest request)
    {
        if (_tagebuch.Auffaelligkeit(id) is null)
        {
            return NotFoundError("auffaelligkeit_not_found", $"Auffälligkeit mit Id {id} existiert nicht.");
        }

        _tagebuch.Verwerfen(id, request.Verworfen, DateTime.UtcNow);
        var stand = _tagebuch.Auffaelligkeit(id)!;
        return Ok(new TagebuchAuffaelligkeitStandDto(stand.Id, stand.VerworfenAmUtc is not null));
    }
}

/// <param name="Verworfen"><c>true</c> = „War nichts", <c>false</c> = wieder zeigen.</param>
public sealed record TagebuchVerwerfenRequest(bool Verworfen);

public sealed record TagebuchAuffaelligkeitStandDto(int Id, bool Verworfen);
