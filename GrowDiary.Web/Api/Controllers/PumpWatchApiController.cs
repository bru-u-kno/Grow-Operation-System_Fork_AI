using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>Was der Pumpen-Wächter je Zelt gerade sieht.</summary>
public sealed record PumpZeltLage(int TentId, string TentName, IReadOnlyList<PumpBefund> Befunde);

/// <summary>
/// Die Pumpen-Lage zum Nachsehen — der Push allein reicht nicht.
/// </summary>
/// <remarks>
/// Eine Nachricht kann man verpassen, überhören oder wegwischen. Wer danach in
/// die App schaut, muss dieselbe Auskunft dort finden; sonst bleibt die Frage
/// „war da was?" unbeantwortet.
/// </remarks>
[ApiController]
[Route("api/pump-watch")]
[Produces("application/json")]
public sealed class PumpWatchApiController : ApiControllerBase
{
    private readonly GrowRepository _repository;
    private readonly HomeAssistantService _homeAssistant;
    private readonly PumpWatchNotifier _waechter;

    public PumpWatchApiController(
        GrowRepository repository,
        HomeAssistantService homeAssistant,
        PumpWatchNotifier waechter)
    {
        _repository = repository;
        _homeAssistant = homeAssistant;
        _waechter = waechter;
    }

    [HttpGet("")]
    [ProducesResponseType(typeof(IReadOnlyList<PumpZeltLage>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PumpZeltLage>>> Get(CancellationToken cancellationToken)
    {
        var settings = _repository.GetEffectiveHomeAssistantSettings();
        if (!settings.IsConfigured) return Ok(Array.Empty<PumpZeltLage>());

        var lagen = new List<PumpZeltLage>();
        foreach (var tent in _repository.GetTents())
        {
            try
            {
                var zustaende = await _homeAssistant.GetStatesAsync(settings, tent, cancellationToken);
                var befunde = _waechter.Pruefen(zustaende, DateTime.UtcNow);
                if (befunde.Count > 0) lagen.Add(new PumpZeltLage(tent.Id, tent.Name, befunde));
            }
            catch
            {
                // Ein unerreichbares Home Assistant ist die Sache des Watchdogs,
                // nicht dieser Anzeige — hier lieber ein Zelt weniger als ein Fehler.
            }
        }

        return Ok(lagen);
    }

    /// <summary>Die Schonfrist, bevor ein Aus als Ausfall zählt.</summary>
    [HttpGet("grace")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public ActionResult Grace() => Ok(new { minutes = _waechter.SchonfristMinuten });

    /// <remarks>
    /// Fork AI (A-003, 03.10.2026): Eine Einstellung des Wächters — wer sie
    /// hochdreht, hört länger nichts von einer stehenden Pumpe. Verwaltung.
    /// </remarks>
    [HttpPut("grace")]
    [KiStufe(KiStufe.Verwaltung)]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public ActionResult SetGrace([FromBody] GraceRequest request)
    {
        _waechter.SchonfristMinuten = request.Minutes;
        return Ok(new { minutes = _waechter.SchonfristMinuten });
    }

    public sealed class GraceRequest
    {
        public int Minutes { get; set; }
    }
}
