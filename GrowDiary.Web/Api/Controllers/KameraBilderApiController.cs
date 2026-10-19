using System.Globalization;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Mapping;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>
/// Fork AI (Paket C2, 18.10.2026): Die Tagesbilder der Zelt-Kamera und „aktuelles Kamerabild als Grow-Foto speichern".
/// </summary>
/// <remarks>
/// <para><b>Tagesbilder:</b> <see cref="HomeAssistantSnapshotWorker"/> legt einmal am Tag (ab 12 Uhr) ein Bild der ersten
/// Kamera eines Zelts unter <c>{SnapshotsPath}/{tentId}/{yyyy-MM-dd}.jpg</c> ab. Bis hierher lagen sie nur auf der Platte,
/// kein Weg zeigte sie. Das Datum ist streng <c>yyyy-MM-dd</c> und wird über <see cref="DateOnly"/> neu geschrieben — der
/// Dateiname entsteht nie aus dem, was die Anfrage schickt.</para>
/// <para><b>Kamerafoto:</b> wie die Auto-Messung (<see cref="PhotoStorageService.SaveSnapshotAsync"/>), nur mit Motiv und
/// Bildunterschrift. Die Kamera muss zum Zelt des Grows gehören (nie eine beliebige Entität), der Grow muss laufen.</para>
/// <para>Gerufen vom Cultivate Control MCP (<c>tagesbilder</c>, <c>kamera_foto_speichern</c>).</para>
/// </remarks>
[ApiController]
[Route("api")]
[KiStufe(KiStufe.Dokumentieren)]
public sealed class KameraBilderApiController : ApiControllerBase
{
    /// <summary>Das einzige Format, das als Datum durchgeht.</summary>
    public const string DatumFormat = "yyyy-MM-dd";

    private readonly GrowRepository _repository;
    private readonly AppPaths _paths;
    private readonly HomeAssistantService _ha;
    private readonly PhotoStorageService _fotos;

    public KameraBilderApiController(GrowRepository repository, AppPaths paths, HomeAssistantService ha, PhotoStorageService fotos)
    {
        _repository = repository;
        _paths = paths;
        _ha = ha;
        _fotos = fotos;
    }

    /// <summary>Die Tagesbilder eines Zelts (neueste zuerst) und seine Kameras — die erste macht die Tagesbilder.</summary>
    [HttpGet("tents/{tentId:int}/tagesbilder")]
    [ProducesResponseType(typeof(TagesbilderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<TagesbilderDto> Tagesbilder(int tentId)
    {
        if (_repository.GetTent(tentId) is not { } zelt) return NotFoundError("tent_not_found", $"Zelt {tentId} existiert nicht.");

        var ordner = Ordner(zelt.Id);
        var bilder = Directory.Exists(ordner)
            ? Directory.EnumerateFiles(ordner, "*.jpg")
                .Select(datei => (Datei: datei, Datum: DatumAus(Path.GetFileNameWithoutExtension(datei))))
                .Where(x => x.Datum is not null)
                .OrderByDescending(x => x.Datum)
                .Select(x => new TagesbildDto(
                    x.Datum!.Value.ToString(DatumFormat, CultureInfo.InvariantCulture),
                    new FileInfo(x.Datei).Length,
                    $"/api/tents/{zelt.Id}/tagesbilder/{x.Datum!.Value.ToString(DatumFormat, CultureInfo.InvariantCulture)}"))
                .ToList()
            : [];

        return Ok(new TagesbilderDto(zelt.Id, TentCameraList.Parse(zelt.CameraEntityIds, zelt.CameraEntityId), bilder));
    }

    /// <summary>Ein Tagesbild als JPEG.</summary>
    [HttpGet("tents/{tentId:int}/tagesbilder/{datum}")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public IActionResult Tagesbild(int tentId, string datum)
    {
        if (DatumAus(datum) is not { } tag)
        {
            return BadRequestError("datum_ungueltig", $"Das Datum muss die Form {DatumFormat} haben, etwa 2026-10-17.");
        }
        if (_repository.GetTent(tentId) is not { } zelt) return NotFoundError("tent_not_found", $"Zelt {tentId} existiert nicht.");

        // Der Dateiname kommt aus dem geprüften Datum und der Id aus der Datenbank — nie aus dem Text der Anfrage.
        var datei = Path.Combine(Ordner(zelt.Id), tag.ToString(DatumFormat, CultureInfo.InvariantCulture) + ".jpg");
        if (!System.IO.File.Exists(datei))
        {
            return NotFoundError("tagesbild_fehlt", $"Für Zelt {zelt.Id} gibt es kein Tagesbild vom {tag.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}.");
        }

        Response.Headers.CacheControl = "no-store";
        return PhysicalFile(datei, "image/jpeg");
    }

    /// <summary>Das aktuelle Bild der Zelt-Kamera als Foto im Grow ablegen (Tagebuch, Fotos).</summary>
    [HttpPost("grows/{growId:int}/kamerafoto")]
    [ProducesResponseType(typeof(PhotoAssetDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<PhotoAssetDto>> KamerafotoSpeichern(int growId, [FromBody] KamerafotoRequest? request, CancellationToken ct)
    {
        request ??= new KamerafotoRequest(null, null, null);
        if (_repository.GetGrow(growId) is not { } grow) return NotFoundError("grow_not_found", $"Grow {growId} existiert nicht.");
        if (grow.Status != GrowStatus.Running) return BadRequestError("grow_nicht_aktiv", $"Grow {growId} läuft nicht — Fotos nur in laufende Grows.");
        if (grow.TentId is not { } zeltId || _repository.GetTent(zeltId) is not { } zelt)
        {
            return BadRequestError("grow_ohne_zelt", $"Grow {growId} hängt an keinem Zelt, also gibt es keine Kamera.");
        }

        var kameras = TentCameraList.Parse(zelt.CameraEntityIds, zelt.CameraEntityId);
        if (kameras.Count == 0) return BadRequestError("camera_missing", "Für dieses Zelt ist keine Kamera-Entity hinterlegt.");
        string kamera;
        if (string.IsNullOrWhiteSpace(request.Kamera))
        {
            kamera = kameras[0];
        }
        else if (kameras.FirstOrDefault(k => string.Equals(k, request.Kamera.Trim(), StringComparison.OrdinalIgnoreCase)) is { } passend)
        {
            kamera = passend;
        }
        else
        {
            return BadRequestError("camera_not_in_tent", $"Diese Kamera gehört nicht zum Zelt. Möglich: {string.Join(", ", kameras)}.");
        }

        var motiv = PhotoTag.Overview;
        // Nur die Namen — Enum.TryParse nähme auch „99" und „Overview,Leaf" (Prüfer 18.10.2026).
        if (!string.IsNullOrWhiteSpace(request.Motiv)
            && (Enum.GetNames<PhotoTag>().FirstOrDefault(n => string.Equals(n, request.Motiv.Trim(), StringComparison.OrdinalIgnoreCase)) is not { } motivName
                || !Enum.TryParse(motivName, out motiv)))
        {
            return BadRequestError("motiv_ungueltig", $"Unbekanntes Motiv. Möglich: {string.Join(", ", Enum.GetNames<PhotoTag>())}.");
        }
        if (request.Bildunterschrift is { Length: > 500 }) return BadRequestError("bildunterschrift_zu_lang", "Die Bildunterschrift darf höchstens 500 Zeichen haben.");

        var settings = _repository.GetEffectiveHomeAssistantSettings();
        if (!settings.IsConfigured) return StatusCode(StatusCodes.Status503ServiceUnavailable, Fehler("ha_nicht_eingerichtet", "Home Assistant ist nicht eingerichtet.", 503));

        var bild = await _ha.GetCameraSnapshotAsync(settings, kamera, ct);
        if (bild is not { } frisch || !CameraFrameCache.IsLikelyImage(frisch.Bytes, frisch.ContentType))
        {
            return StatusCode(StatusCodes.Status502BadGateway, Fehler("ha_camera_unavailable", "Home Assistant liefert für diese Kamera gerade kein gültiges Bild. Es wurde nichts gespeichert.", 502));
        }

        var foto = await _fotos.SaveSnapshotAsync(grow, null, frisch.Bytes, ct,
            caption: string.IsNullOrWhiteSpace(request.Bildunterschrift) ? $"Kamerabild {kamera}" : request.Bildunterschrift, tag: motiv);
        return StatusCode(StatusCodes.Status201Created, foto.ToDto());
    }

    private ApiError Fehler(string code, string meldung, int status)
        => ApiErrorFactory.Create(code, meldung, status, traceId: HttpContext?.TraceIdentifier);

    private string Ordner(int zeltId) => Path.Combine(_paths.SnapshotsPath, zeltId.ToString(CultureInfo.InvariantCulture));

    /// <summary>Ein Datum genau in der Form <see cref="DatumFormat"/> — alles andere ist null.</summary>
    public static DateOnly? DatumAus(string? text)
        => text is { Length: 10 } && DateOnly.TryParseExact(text, DatumFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var tag)
            ? tag
            : null;
}
