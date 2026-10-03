using System.Globalization;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>
/// Das Aushärten im Glas — der Teil des Laufs, der bisher ohne die App lief.
/// </summary>
/// <remarks>
/// <para>Nach der Ernte setzt Grow OS den Grow auf „beendet". Das Aushärten
/// beginnt aber genau dann und dauert 30–60 Tage: die App verabschiedete sich
/// vor dem Schritt, der über die Qualität entscheidet.</para>
///
/// <para>Gläser gehören zum Grow, laufen aber weiter, wenn er beendet ist. Ein
/// Glas ist erst mit <c>FinishedAtUtc</c> durch.</para>
/// </remarks>
[ApiController]
[Route("api")]
[Produces("application/json")]
[KiStufe(KiStufe.Dokumentieren)]
public sealed class CuringApiController : ApiControllerBase
{
    private readonly CuringRepository _repository;
    private readonly GrowRepository _grows;
    private readonly SetupRepository _setups;

    public CuringApiController(CuringRepository repository, GrowRepository grows, SetupRepository setups)
    {
        _repository = repository;
        _grows = grows;
        _setups = setups;
    }

    /// <summary>Alle Gläser, die noch aushärten — über alle Grows hinweg.</summary>
    /// <remarks>
    /// Das ist die Liste, die „heute fällig" speist. Sie fragt bewusst nicht
    /// nach dem Grow-Status: ein beendeter Grow kann sehr wohl noch Gläser im
    /// Schrank haben, und genau die vergisst man.
    /// </remarks>
    [HttpGet("curing/jars")]
    [ProducesResponseType(typeof(IReadOnlyList<CuringJarDto>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<CuringJarDto>> OpenJars([FromQuery] bool auchKuerzlichFertige = false)
        => Ok(_repository.GetOpenJars(auchKuerzlichFertige).Select(Abbilden).ToList());

    [HttpGet("grows/{growId:int}/curing/jars")]
    [ProducesResponseType(typeof(IReadOnlyList<CuringJarDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<IReadOnlyList<CuringJarDto>> JarsForGrow(int growId)
    {
        if (_grows.GetGrow(growId) is null)
        {
            return NotFoundError("grow_not_found", $"Grow mit Id {growId} existiert nicht.");
        }

        return Ok(_repository.GetJarsForGrow(growId).Select(Abbilden).ToList());
    }

    [HttpPost("grows/{growId:int}/curing/jars")]
    [ProducesResponseType(typeof(CuringJarDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<CuringJarDto> CreateJar(int growId, [FromBody] CuringJarUpsertRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_grows.GetGrow(growId) is null)
        {
            return NotFoundError("grow_not_found", $"Grow mit Id {growId} existiert nicht.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        if (!TryLiesDatum(request.FilledAtLocal, out var eingeglast))
        {
            ModelState.AddModelError(nameof(request.FilledAtLocal), "Einglas-Datum konnte nicht gelesen werden.");
            return ValidationError();
        }

        // Ohne diese Pruefung schlaegt der Fremdschluessel zu und die Anfrage
        // endet in einem 500 — ein Serverfehler fuer eine Eingabe, die schlicht
        // falsch war.
        if (!SorteExistiert(request.StrainId))
        {
            ModelState.AddModelError(nameof(request.StrainId), $"Sorte mit Id {request.StrainId} existiert nicht.");
            return ValidationError();
        }

        var jar = new CuringJar
        {
            GrowId = growId,
            Label = request.Label.Trim(),
            StrainId = request.StrainId,
            FilledAtUtc = eingeglast,
            WeightG = request.WeightG,
            HasHumidityPack = request.HasHumidityPack,
            Notes = request.Notes,
        };
        jar.Id = _repository.CreateJar(jar);
        return CreatedAtAction(nameof(JarsForGrow), new { growId }, Abbilden(jar));
    }

    [HttpPut("curing/jars/{id:int}")]
    [ProducesResponseType(typeof(CuringJarDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<CuringJarDto> UpdateJar(int id, [FromBody] CuringJarUpsertRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var jar = _repository.GetJar(id);
        if (jar is null)
        {
            return NotFoundError("jar_not_found", $"Glas mit Id {id} existiert nicht.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        if (!TryLiesDatum(request.FilledAtLocal, out var eingeglast))
        {
            ModelState.AddModelError(nameof(request.FilledAtLocal), "Einglas-Datum konnte nicht gelesen werden.");
            return ValidationError();
        }

        if (!SorteExistiert(request.StrainId))
        {
            ModelState.AddModelError(nameof(request.StrainId), $"Sorte mit Id {request.StrainId} existiert nicht.");
            return ValidationError();
        }

        // FinishedAtUtc bleibt unangetastet: das Beenden hat einen eigenen
        // Endpunkt, damit ein Tippfehler im Namen kein Glas abschliesst.
        jar.Label = request.Label.Trim();
        jar.StrainId = request.StrainId;
        jar.FilledAtUtc = eingeglast;
        jar.WeightG = request.WeightG;
        jar.HasHumidityPack = request.HasHumidityPack;
        jar.Notes = request.Notes;
        _repository.UpdateJar(jar);
        return Ok(Abbilden(jar));
    }

    /// <summary>Das Glas ist durch.</summary>
    /// <remarks>
    /// Unter 14 Tagen ist ein Glas nicht ausgehärtet, sondern nur gestanden —
    /// das steht als Hinweis dabei, verhindert wird es nicht. Wer sein Glas
    /// früher schließen will, hat vielleicht einen Grund, den die App nicht
    /// kennt.
    /// </remarks>
    [HttpPost("curing/jars/{id:int}/finish")]
    [ProducesResponseType(typeof(CuringJarDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<CuringJarDto> FinishJar(int id)
    {
        var jar = _repository.GetJar(id);
        if (jar is null)
        {
            return NotFoundError("jar_not_found", $"Glas mit Id {id} existiert nicht.");
        }

        jar.FinishedAtUtc = DateTime.UtcNow;
        _repository.UpdateJar(jar);
        return Ok(Abbilden(jar));
    }

    /// <summary>Ein Glas doch noch einmal öffnen.</summary>
    /// <remarks>
    /// „Fertig" war die einzige Handlung ohne Rückweg. Ein Fehlgriff bedeutete:
    /// das Glas verschwindet aus der Liste, der Lüft-Rhythmus hört auf, und
    /// zurück kam man nur über die Datenbank. Aushärten dauert Wochen — in
    /// dieser Zeit klickt man sich auch mal vertan.
    /// </remarks>
    [HttpPost("curing/jars/{id:int}/reopen")]
    [ProducesResponseType(typeof(CuringJarDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<CuringJarDto> ReopenJar(int id)
    {
        var jar = _repository.GetJar(id);
        if (jar is null)
        {
            return NotFoundError("jar_not_found", $"Glas mit Id {id} existiert nicht.");
        }

        jar.FinishedAtUtc = null;
        _repository.UpdateJar(jar);
        return Ok(Abbilden(jar));
    }

    [HttpDelete("curing/jars/{id:int}")]
    [KiSicherungVorher]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult DeleteJar(int id)
    {
        if (_repository.GetJar(id) is null)
        {
            return NotFoundError("jar_not_found", $"Glas mit Id {id} existiert nicht.");
        }

        _repository.DeleteJar(id);
        return NoContent();
    }

    [HttpGet("curing/jars/{id:int}/readings")]
    [ProducesResponseType(typeof(IReadOnlyList<CuringReadingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<IReadOnlyList<CuringReadingDto>> Readings(int id)
    {
        if (_repository.GetJar(id) is null)
        {
            return NotFoundError("jar_not_found", $"Glas mit Id {id} existiert nicht.");
        }

        return Ok(_repository.GetReadings(id).Select(r => new CuringReadingDto(
            r.Id, r.JarId, r.ReadAtUtc, r.HumidityPercent, r.BurpedMinutes, r.Note, r.Source.ToString())).ToList());
    }

    /// <summary>Eine Ablesung eintragen: Feuchte, Lüften, oder beides.</summary>
    [HttpPost("curing/jars/{id:int}/readings")]
    [ProducesResponseType(typeof(CuringJarDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<CuringJarDto> AddReading(int id, [FromBody] CuringReadingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var jar = _repository.GetJar(id);
        if (jar is null)
        {
            return NotFoundError("jar_not_found", $"Glas mit Id {id} existiert nicht.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        // Ein leerer Eintrag wuerde als „gelueftet" durchgehen, ohne dass etwas
        // passiert ist — und den naechsten Termin verschieben.
        if (request.HumidityPercent is null && request.BurpedMinutes is null)
        {
            ModelState.AddModelError(nameof(request.HumidityPercent),
                "Trag entweder die abgelesene Feuchte ein oder wie lange du gelüftet hast — sonst ist nichts passiert.");
            return ValidationError();
        }

        _repository.CreateReading(new CuringReading
        {
            JarId = id,
            ReadAtUtc = DateTime.UtcNow,
            HumidityPercent = request.HumidityPercent,
            BurpedMinutes = request.BurpedMinutes,
            Note = request.Note,
            Source = CuringReadingSource.Manual,
        });

        return CreatedAtAction(nameof(Readings), new { id }, Abbilden(jar));
    }

    // ---------- Abbildung ----------

    private CuringJarDto Abbilden(CuringJar jar)
    {
        var duty = CuringSchedule.Evaluate(jar, _repository.GetLastBurp(jar.Id), DateTime.UtcNow);
        var letzte = _repository.GetLatestReading(jar.Id);

        CuringHumidityDto? feuchte = null;
        if (letzte?.HumidityPercent is { } prozent)
        {
            var urteil = CuringRating.Rate(prozent);
            feuchte = new CuringHumidityDto(prozent, letzte.ReadAtUtc, letzte.Source.ToString(),
                urteil.Level.ToString(), urteil.Summary, urteil.Action, urteil.Source);
        }

        return new CuringJarDto(
            jar.Id,
            jar.GrowId,
            _grows.GetGrow(jar.GrowId)?.Name ?? $"Grow {jar.GrowId}",
            jar.Label,
            jar.StrainId,
            jar.StrainId is { } sid ? _setups.GetStrain(sid)?.Name : null,
            jar.FilledAtUtc,
            jar.WeightG,
            jar.HasHumidityPack,
            jar.FinishedAtUtc,
            jar.Notes,
            new CuringDutyDto(duty.Level.ToString(), duty.DayInCure, duty.IntervalDays,
                duty.BurpMinutesMin, duty.BurpMinutesMax, duty.NextDueUtc, duty.Text, duty.Source),
            feuchte);
    }

    /// <summary>Gibt es diese Sorte? <c>null</c> ist erlaubt und heißt „keine".</summary>
    private bool SorteExistiert(int? strainId)
        => strainId is null || _setups.GetStrain(strainId.Value) is not null;

    /// <summary>„2026-08-17" als Ortszeit-Mittag, damit die Tageszählung stimmt.</summary>
    /// <remarks>
    /// Mittag statt Mitternacht: bei Mitternacht Ortszeit kippt der Wert nach
    /// UTC-Umrechnung auf den Vortag, und das Glas wäre einen Tag älter, als es
    /// ist. Dieselbe Falle wie bei den Grow-Datumsfeldern.
    /// </remarks>
    private static bool TryLiesDatum(string? roh, out DateTime utc)
    {
        utc = default;
        if (!DateTime.TryParse(roh, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var lokal))
        {
            return false;
        }

        utc = DateTime.SpecifyKind(lokal.Date.AddHours(12), DateTimeKind.Local).ToUniversalTime();
        return true;
    }
}
