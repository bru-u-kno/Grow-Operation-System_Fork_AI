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

    public sealed class UseTargetsRequest
    {
        public bool Use { get; set; }
    }
}
