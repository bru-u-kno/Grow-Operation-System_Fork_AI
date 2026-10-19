using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure.KiZugriff;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>
/// Fork AI (Paket D, 18.10.2026): Das Funktionsverzeichnis für KI-Assistenten.
/// </summary>
/// <remarks>
/// <para>Ein eigener Controller, weil <see cref="KiZugriffApiController"/> als Ganzes <c>[KeinKiZugriff]</c> trägt.
/// Der Weg liegt nicht unter <c>/api/settings</c> (kein Verwaltungsweg): lesen darf jeder gültige Schlüssel, und
/// aus dem Add-on-Netz ohne Schlüssel geht er wie jeder lesende Weg. Darin steht nichts Geheimes — nur, welche
/// Funktionen es gibt und wie sie eingestuft sind.</para>
/// <para>Gerufen vom Cultivate Control MCP (<c>fork_funktionen</c>, <c>fork_ausfuehren</c>).</para>
/// </remarks>
[ApiController]
[Route("api/ki-zugriff/funktionen")]
[Produces("application/json")]
[NurMitKi]
public sealed class KiFunktionenApiController : ApiControllerBase
{
    private readonly KiFunktionsverzeichnis _verzeichnis;

    public KiFunktionenApiController(KiFunktionsverzeichnis verzeichnis) => _verzeichnis = verzeichnis;

    /// <summary>Alle Funktionen von Cultivate Control unter /api: Weg, Einstufung für Schlüssel, Beschreibung und erwartete Angaben.</summary>
    [HttpGet("")]
    [ProducesResponseType(typeof(KiFunktionenDto), StatusCodes.Status200OK)]
    public ActionResult<KiFunktionenDto> Verzeichnis() => Ok(_verzeichnis.Lesen());
}
