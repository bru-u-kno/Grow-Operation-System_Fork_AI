using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services.GrowPlan;
using GrowDiary.Web.Services.Knowledge.Schema;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>Ein Planstand, wie ihn die Oberfläche liest.</summary>
public sealed record GrowPlanStandDto(
    int GrowId,
    string Stand,
    string ProgrammId,
    string ProgrammName,
    string? Vermerk,
    DateTime AngelegtUtc,
    DateTime GeaendertUtc,
    FeedChartDefinition Chart,
    Dictionary<string, Dictionary<string, string>> Herkunft);

/// <summary>Ein Eintrag im Änderungsbuch.</summary>
public sealed record GrowPlanEintragDto(
    long Id,
    DateTime ZeitUtc,
    string Art,
    string? SpalteId,
    string? Feld,
    string? Alt,
    string? Neu,
    string? Ziel,
    string? Grund);

/// <summary>
/// Fork AI (Grow-Plan, 16.09.2026): Planstände und Änderungsbuch eines Grows lesen.
/// </summary>
/// <remarks>
/// Nur Lesewege. Geschrieben wird der Arbeitsstand vorerst über
/// <c>POST /api/wochenplan/werte/{growId}</c>; der Plan-Reiter bekommt seinen
/// eigenen Speicherweg (Dosierung, Ziel „auch ins Programm", Grund).
/// </remarks>
[ApiController]
[Route("api/grows/{growId:int}/plan")]
public sealed class GrowPlanApiController : ApiControllerBase
{
    private readonly GrowRepository _grows;
    private readonly GrowPlanService _plaene;

    public GrowPlanApiController(GrowRepository grows, GrowPlanService plaene)
    {
        _grows = grows;
        _plaene = plaene;
    }

    /// <summary>Ein Stand des Plans: <c>arbeit</c> (Standard), <c>start</c> oder <c>ende</c>.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(GrowPlanStandDto), StatusCodes.Status200OK)]
    public ActionResult<GrowPlanStandDto> Get(int growId, [FromQuery] string stand = GrowPlanStaende.Arbeit)
    {
        if (_grows.GetGrow(growId) is null) return NotFoundError("grow_nicht_gefunden", "Diesen Grow gibt es nicht.");
        if (stand is not (GrowPlanStaende.Start or GrowPlanStaende.Arbeit or GrowPlanStaende.Ende))
            return ValidationError("Stand muss start, arbeit oder ende sein.");

        if (_plaene.Stand(growId, stand) is not { } gefunden)
            return NotFoundError("plan_nicht_gefunden", "Dieser Grow hat keinen Plan in diesem Stand.");

        return Ok(new GrowPlanStandDto(
            gefunden.GrowId,
            gefunden.Stand,
            gefunden.Inhalt.ProgrammId,
            gefunden.Inhalt.ProgrammName,
            gefunden.Vermerk,
            gefunden.AngelegtUtc,
            gefunden.GeaendertUtc,
            gefunden.Inhalt.Chart,
            gefunden.Inhalt.Herkunft));
    }

    /// <summary>Das Änderungsbuch, neueste Einträge zuerst.</summary>
    [HttpGet("buch")]
    [ProducesResponseType(typeof(IReadOnlyList<GrowPlanEintragDto>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<GrowPlanEintragDto>> Buch(int growId)
    {
        if (_grows.GetGrow(growId) is null) return NotFoundError("grow_nicht_gefunden", "Diesen Grow gibt es nicht.");
        return Ok(_plaene.Buch(growId)
            .Select(e => new GrowPlanEintragDto(e.Id, e.ZeitUtc, e.Art, e.SpalteId, e.Feld, e.Alt, e.Neu, e.Ziel, e.Grund))
            .ToList());
    }
}
