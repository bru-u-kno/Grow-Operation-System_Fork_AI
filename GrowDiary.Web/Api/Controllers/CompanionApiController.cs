using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>Wie eng die App begleitet — und was gerade überfällig ist.</summary>
[ApiController]
[Route("api/companion")]
[Produces("application/json")]
[KiStufe(KiStufe.Verwaltung)]
public sealed class CompanionApiController : ApiControllerBase
{
    private readonly SopDueService _due;

    public CompanionApiController(SopDueService due)
    {
        _due = due;
    }

    [HttpGet("settings")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetSettings() => Ok(new { level = _due.Stufe });

    public sealed class LevelRequest
    {
        public string Level { get; set; } = "full";
    }

    [HttpPut("settings")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public IActionResult PutSettings([FromBody] LevelRequest request)
    {
        if (request.Level is not ("full" or "important" or "expert"))
        {
            return BadRequestError("level_invalid", "Erlaubt sind: full, important, expert.");
        }

        _due.Stufe = request.Level;
        return Ok(new { level = _due.Stufe });
    }

    /// <summary>Überfällige Routinen dieses Grows — leer im Expertenmodus.</summary>
    [HttpGet("~/api/grows/{growId:int}/due-sops")]
    [ProducesResponseType(typeof(IReadOnlyList<FaelligeRoutine>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<FaelligeRoutine>> DueForGrow(int growId)
        => Ok(_due.FuerGrow(growId));
}
