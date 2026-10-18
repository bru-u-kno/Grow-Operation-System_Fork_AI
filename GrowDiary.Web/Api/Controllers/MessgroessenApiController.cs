using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>
/// Fork AI (forkai.247): Welche Messgrößen ein Zelt kennt — mit Etikett, Schlüssel und passenden Domains.
/// </summary>
/// <remarks>
/// Lesend und ohne Verwaltung: der Fork-MCP braucht die Namen auch für <c>grenzwert_setzen</c> (Stufe Grow planen). Die Tabelle
/// selbst steht in <see cref="SensorMessgroessen"/>; die Meldegrenzen nennen manche Größen anders
/// (<see cref="AlertEvaluationService.MetricDisplay"/>), beides wird hier nur durchgereicht.
/// </remarks>
[ApiController]
[Route("api/messgroessen")]
[Produces("application/json")]
public sealed class MessgroessenApiController : ApiControllerBase
{
    [HttpGet("")]
    [ProducesResponseType(typeof(IReadOnlyList<SensorMessgroesseDto>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<SensorMessgroesseDto>> Liste()
        => Ok(SensorMessgroessen.Alle
            .OrderBy(p => (int)p.Key)
            .Select(p =>
            {
                var schluessel = TentSensorMetricKeyMap.Resolve(p.Key);
                var (melde, einheit) = AlertEvaluationService.MetricDisplay(schluessel);
                return new SensorMessgroesseDto(
                    p.Key.ToString(),
                    schluessel,
                    p.Value.Etikett,
                    melde == schluessel || melde == p.Value.Etikett ? null : melde,
                    p.Value.Domains,
                    string.IsNullOrWhiteSpace(einheit) ? null : einheit.Trim());
            })
            .ToList());
}
