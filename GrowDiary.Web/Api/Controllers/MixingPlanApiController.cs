using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>Der Mischplan für heute — konkrete Milliliter statt „nach Plan".</summary>
[ApiController]
[Route("api/grows/{growId:int}/mixing-plan")]
[Produces("application/json")]
[KiStufe(KiStufe.GrowPlanen)]
public sealed class MixingPlanApiController : ApiControllerBase
{
    private readonly MischplanService _mischplan;
    private readonly Infrastructure.GrowRepository _grows;

    public MixingPlanApiController(MischplanService mischplan, Infrastructure.GrowRepository grows)
    {
        _mischplan = mischplan;
        _grows = grows;
    }

    /// <summary>Schaltet die Wochen-Ziele des Charts als Sollwerte an oder aus.</summary>
    /// <remarks>
    /// Der Schalter sitzt bewusst hier und nicht im Grow-Formular: er gehört an
    /// die Stelle, an der man die Ziele sieht. Wer beim Mischen liest „Ziel
    /// EC 1,5" und auf dem Bildschirm etwas anderes stehen hat, will genau dort
    /// entscheiden können, welches gilt.
    /// </remarks>
    [HttpPut("use-targets")]
    [ProducesResponseType(typeof(Mischplan), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<Mischplan> UseTargets(int growId, [FromBody] UseTargetsRequest request)
    {
        var grow = _grows.GetGrow(growId);
        if (grow is null) return NotFoundError("grow_not_found", $"Grow mit Id {growId} existiert nicht.");

        grow.UseFeedChartTargets = request.Use;
        _grows.UpdateGrow(grow);

        return Ok(_mischplan.FuerGrow(growId)!);
    }

    [HttpGet("")]
    [ProducesResponseType(typeof(Mischplan), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<Mischplan> Get(int growId)
    {
        var plan = _mischplan.FuerGrow(growId);
        return plan is null
            ? NotFoundError("grow_not_found", $"Grow mit Id {growId} existiert nicht.")
            : Ok(plan);
    }

    /// <summary>
    /// Der Plan dieser Woche als Vorschlag für einen Wasserwechsel (A-006).
    /// </summary>
    /// <remarks>
    /// Auf die angesetzten Liter gerechnet, mit Wasser-EC nach Wasserart und
    /// CalMag nach dem Calcium im Ausgangswasser. Die Regeln stehen in
    /// <see cref="MischplanVorschlagRechnung"/> — die Oberfläche rechnet nichts nach.
    /// </remarks>
    /// <param name="liter">Neu angesetzte Liter, größer 0.</param>
    /// <param name="wasser"><c>Tap</c>, <c>RO</c> oder <c>Mixed</c>.</param>
    /// <param name="osmoseProzent">Bei <c>Mixed</c>: Anteil Osmose 0–100.</param>
    /// <param name="wasserEc">Selbst gemessener Wasser-EC (mS/cm); überschreibt den Vorschlag.</param>
    [HttpGet("vorschlag")]
    [ProducesResponseType(typeof(MischplanVorschlag), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<MischplanVorschlag> Vorschlag(
        int growId,
        [FromQuery] double liter,
        [FromQuery] Models.WaterSource wasser = Models.WaterSource.Tap,
        [FromQuery] double? osmoseProzent = null,
        [FromQuery] double? wasserEc = null)
    {
        if (!double.IsFinite(liter) || liter <= 0)
            return BadRequestError("liter_invalid", "Die Literzahl muss größer als 0 sein.");
        if (!Enum.IsDefined(wasser))
            return BadRequestError("wasser_invalid", "Die Wasserart ist ungültig.");
        if (osmoseProzent is < 0 or > 100)
            return BadRequestError("osmose_invalid", "Der Anteil Osmose liegt zwischen 0 und 100 %.");
        if (wasserEc is { } ec && !MeasurementSanityService.IstPhysikalischMoeglich("ec", ec))
            return BadRequestError("wasser_ec_invalid", "Der EC des Wassers liegt außerhalb dessen, was physikalisch vorkommen kann.");

        var vorschlag = _mischplan.Vorschlag(growId, liter, wasser, osmoseProzent, wasserEc);
        return vorschlag is null
            ? NotFoundError("grow_not_found", $"Grow mit Id {growId} existiert nicht.")
            : Ok(vorschlag);
    }

    public sealed class UseTargetsRequest
    {
        public bool Use { get; set; }
    }
}
