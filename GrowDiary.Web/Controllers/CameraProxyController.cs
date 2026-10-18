using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Controllers;

[ApiController]
[Route("api")]
public sealed class CameraProxyController : ControllerBase
{
    private readonly GrowRepository _repository;
    private readonly HomeAssistantService _homeAssistantService;
    private readonly CameraFrameCache _cameraCache;

    public CameraProxyController(GrowRepository repository, HomeAssistantService homeAssistantService, CameraFrameCache cameraCache)
    {
        _repository = repository;
        _homeAssistantService = homeAssistantService;
        _cameraCache = cameraCache;
    }

    // Nur EIN Weg zur Kamera. Der Alias "camera/tents/{tentId}" hatte am
    // 02.09.2026 keinen einzigen Aufrufer — die Oberflaeche nimmt an allen
    // vier Stellen "live/tents/{id}/camera".
    [HttpGet("live/tents/{tentId:int}/camera")]
    // Fork AI (Paket D): Die Antwort ist ein Bild — so steht es auch im Funktionsverzeichnis für KI-Assistenten.
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTentCamera(int tentId, [FromQuery] string? entity, CancellationToken cancellationToken)
    {
        var tent = _repository.GetTent(tentId);
        if (tent is null)
        {
            return NotFound(new CameraProxyStatusDto(false, "tent_not_found", "Zelt wurde nicht gefunden.", null, null));
        }

        var cameras = TentCameraList.Parse(tent.CameraEntityIds, tent.CameraEntityId);
        if (cameras.Count == 0)
        {
            return NotFound(new CameraProxyStatusDto(false, "camera_missing", "Für dieses Zelt ist keine Kamera-Entity hinterlegt.", null, null));
        }

        // A ?entity= switch is honoured only if it belongs to this tent — never proxy an
        // arbitrary entity the caller names. Naming one we don't know is an error rather
        // than a reason to substitute: serving camera one under camera three's caption
        // looks like a working feature and hides the mistake.
        string entityId;
        if (string.IsNullOrWhiteSpace(entity))
        {
            entityId = cameras[0];
        }
        else if (cameras.Contains(entity, StringComparer.OrdinalIgnoreCase))
        {
            entityId = entity;
        }
        else
        {
            return NotFound(new CameraProxyStatusDto(
                false,
                "camera_not_in_tent",
                "Diese Kamera ist dem Zelt nicht zugeordnet. Trage sie in den Zelt-Einstellungen ein.",
                entity,
                null));
        }
        var settings = _repository.GetEffectiveHomeAssistantSettings();

        // Try a fresh frame and only accept it if it is a real, valid image.
        if (settings.IsConfigured)
        {
            var snapshot = await _homeAssistantService.GetCameraSnapshotAsync(settings, entityId, cancellationToken);
            if (snapshot is { } value && CameraFrameCache.IsLikelyImage(value.Bytes, value.ContentType))
            {
                var fresh = new CameraFrame(value.Bytes, value.ContentType, DateTimeOffset.UtcNow);
                _cameraCache.Set(entityId, fresh);
                return ServeFrame(fresh, live: true);
            }
        }

        // Fresh fetch failed or returned an invalid image → fall back to the last
        // valid frame so the user always keeps a recent grow image.
        var cached = _cameraCache.Get(entityId);
        if (cached is not null)
        {
            return ServeFrame(cached, live: false);
        }

        // No valid frame has ever been captured for this camera.
        return settings.IsConfigured
            ? StatusCode(StatusCodes.Status502BadGateway, new CameraProxyStatusDto(false, "ha_camera_unavailable", "Home Assistant liefert für diese Kamera noch kein gültiges Bild.", entityId, null))
            : StatusCode(StatusCodes.Status503ServiceUnavailable, new CameraProxyStatusDto(false, "ha_not_configured", "Home Assistant ist nicht vollständig konfiguriert.", entityId, null));
    }

    private IActionResult ServeFrame(CameraFrame frame, bool live)
    {
        Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["X-Camera-Captured-At"] = frame.CapturedAt.ToString("o");
        Response.Headers["X-Camera-Live"] = live ? "true" : "false";
        return File(frame.Bytes, frame.ContentType);
    }

}

/// <summary>Was der Kamera-Umweg über eine Kamera sagt.</summary>
/// <param name="Ok">Kam ein Bild?</param>
/// <param name="Status">Die stabile Kennung des Falls, für die Oberfläche.</param>
/// <param name="Meldung">Derselbe Fall in einem Satz für den Nutzer.</param>
/// <param name="EntityId">Um welche Kamera es geht, sofern bekannt.</param>
/// <param name="VorschauUrl">Wo das Bild liegt, sofern es eines gibt.</param>
public sealed record CameraProxyStatusDto(
    bool Ok, string Status, string Meldung, string? EntityId, string? VorschauUrl);
