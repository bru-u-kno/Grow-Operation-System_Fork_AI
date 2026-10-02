using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>
/// Was gewartet, getauscht oder gesichert gehört — ohne dass man es von Hand eingetragen hat.
/// </summary>
/// <remarks>
/// Getrennt von <c>maintenance-events</c>: dort stehen Termine, die jemand
/// angelegt hat. Hier steht, was sich aus den Angaben am Gerät selbst ergibt —
/// genau die Termine, die bisher niemand las.
/// </remarks>
[ApiController]
[Route("api/maintenance-due")]
[Produces("application/json")]
public sealed class MaintenanceDueApiController : ApiControllerBase
{
    private readonly WartungDueService _wartung;

    public MaintenanceDueApiController(WartungDueService wartung)
    {
        _wartung = wartung;
    }

    [HttpGet("")]
    [ProducesResponseType(typeof(IReadOnlyList<WartungsPunkt>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<WartungsPunkt>> Get()
        => Ok(_wartung.Offen(DateTime.UtcNow));

    /// <summary>Die Frist je Gerät und Art — was der Wartungs-Reiter zeigt.</summary>
    /// <remarks>
    /// Fork AI (02.10.2026): Vorher rechnete der Reiter die Fristen selbst aus
    /// den Einträgen, und dieser Dienst dieselbe Frage anders. Jetzt gibt es eine
    /// Rechnung (<see cref="WartungDueService.FristenRechnen"/>), aus der beide lesen.
    /// </remarks>
    [HttpGet("fristen")]
    [ProducesResponseType(typeof(IReadOnlyList<WartungsFrist>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<WartungsFrist>> Fristen()
        => Ok(_wartung.Fristen());
}
