using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>Was ein Grow gekostet hat — und der eine Preis, den das braucht.</summary>
[ApiController]
[Route("api/costs")]
[Produces("application/json")]
[KiStufe(KiStufe.Verwaltung)]
public sealed class CostsApiController : ApiControllerBase
{
    private readonly GrowCostService _kosten;

    public CostsApiController(GrowCostService kosten)
    {
        _kosten = kosten;
    }

    /// <summary>Der hinterlegte Strompreis in Cent je kWh.</summary>
    [HttpGet("settings")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetSettings()
        => Ok(new { strompreisCentProKwh = _kosten.StrompreisCentProKwh });

    public sealed class KostenEinstellungenRequest
    {
        public double? StrompreisCentProKwh { get; set; }
    }

    [HttpPut("settings")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public IActionResult PutSettings([FromBody] KostenEinstellungenRequest request)
    {
        if (request.StrompreisCentProKwh is < 0 or > 500)
        {
            return BadRequestError("price_out_of_range", "Der Strompreis muss zwischen 0 und 500 ct/kWh liegen.");
        }

        _kosten.StrompreisCentProKwh = request.StrompreisCentProKwh;
        return Ok(new { strompreisCentProKwh = _kosten.StrompreisCentProKwh });
    }

    /// <summary>Die Kostenaufstellung eines Grows — alles berechnet, alles mit Herkunft.</summary>
    [HttpGet("~/api/grows/{growId:int}/costs")]
    [ProducesResponseType(typeof(GrowKosten), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<GrowKosten> ForGrow(int growId)
    {
        var kosten = _kosten.FuerGrow(growId);
        return kosten is null
            ? NotFoundError("grow_not_found", $"Grow mit Id {growId} existiert nicht.")
            : Ok(kosten);
    }
}
