using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>
/// Der geführte Kalibrierlauf: Sensor mitlesen, während der Nutzer füllt.
/// </summary>
/// <remarks>
/// Fork AI (A-003, 03.10.2026): Nichts schaltet — gelesen wird der Sensor,
/// eingetragen die Liter von der Wasseruhr. Über einen Schlüssel Dokumentieren.
/// </remarks>
[ApiController]
[KiStufe(KiStufe.Dokumentieren)]
[Route("api/hydro-setups/{systemId:int}/level-calibration")]
[Produces("application/json")]
public sealed class LevelCalibrationApiController : ApiControllerBase
{
    private readonly LevelCalibrationService _calibration;

    public LevelCalibrationApiController(LevelCalibrationService calibration)
    {
        _calibration = calibration;
    }

    /// <summary>Lauf starten — das System muss dabei leer sein.</summary>
    [HttpPost("start")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Start(int systemId, CancellationToken cancellationToken)
    {
        _calibration.Start(systemId);
        return Ok(await _calibration.PollAsync(systemId, cancellationToken));
    }

    /// <summary>
    /// Einmal ablesen. Die Oberfläche ruft das im Sekundentakt.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Poll(int systemId, CancellationToken cancellationToken)
        => Ok(await _calibration.PollAsync(systemId, cancellationToken));

    /// <summary>„Voll" bestätigen und die Liter von der Wasseruhr eintragen.</summary>
    [HttpPost("finish")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public IActionResult Finish(int systemId, [FromBody] FinishLevelCalibrationRequest request)
        => _calibration.Finish(systemId, request.Liters) is { } fehler
            ? BadRequestError("calibration_failed", fehler)
            : Ok(new { ok = true, message = "Volumen kalibriert — der Wasserstand steht ab jetzt in Litern." });

    /// <summary>Abbrechen.</summary>
    [HttpPost("cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Cancel(int systemId)
    {
        _calibration.Cancel(systemId);
        return NoContent();
    }
}

/// <summary>Was an der Wasseruhr stand, als das System voll war.</summary>
public sealed class FinishLevelCalibrationRequest
{
    public double Liters { get; set; }
}
