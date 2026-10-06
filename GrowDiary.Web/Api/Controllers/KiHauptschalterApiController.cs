using GrowDiary.Web.Infrastructure.KiZugriff;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>Fork AI (A-011): Zustand des globalen Schalters „KI-Funktionen".</summary>
public sealed record KiHauptschalterDto(bool Aktiv);

/// <summary>
/// Fork AI (A-011, 06.10.2026): Der globale Schalter „KI-Funktionen" — lesen und setzen.
/// </summary>
/// <remarks>
/// Gelesen wird er von der ganzen Oberfläche (Menü, Suche, Seiten). Gesetzt wird er
/// nur von einem Menschen in der Oberfläche: ein Schlüssel kann sich die KI nicht
/// selbst einschalten.
/// </remarks>
[ApiController]
[Route("api/settings/ki")]
[Produces("application/json")]
public sealed class KiHauptschalterApiController : ApiControllerBase
{
    private readonly KiHauptschalter _schalter;

    public KiHauptschalterApiController(KiHauptschalter schalter) => _schalter = schalter;

    [HttpGet]
    [ProducesResponseType(typeof(KiHauptschalterDto), StatusCodes.Status200OK)]
    public ActionResult<KiHauptschalterDto> Lesen() => Ok(new KiHauptschalterDto(_schalter.Aktiv));

    [HttpPut]
    [KeinKiZugriff("Der Hauptschalter für die KI gehört dem Menschen in der Oberfläche; ein Schlüssel darf sich die KI nicht selbst einschalten.")]
    [ProducesResponseType(typeof(KiHauptschalterDto), StatusCodes.Status200OK)]
    public ActionResult<KiHauptschalterDto> Setzen([FromBody] KiHauptschalterDto anfrage)
    {
        if (anfrage is null) return BadRequestError("ki_schalter_invalid", "Es wurde nichts übergeben.");
        _schalter.Setzen(anfrage.Aktiv);
        return Ok(new KiHauptschalterDto(_schalter.Aktiv));
    }
}
