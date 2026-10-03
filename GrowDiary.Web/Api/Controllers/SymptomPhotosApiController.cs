using System.ComponentModel.DataAnnotations;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Services.Knowledge;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

public sealed class SymptomPhotoAssignRequest
{
    /// <summary>Der Symptom-Schlüssel aus der Wissensbasis; <c>null</c> löst die Zuordnung.</summary>
    [StringLength(120)]
    public string? SymptomId { get; set; }
}

/// <summary>Ein eigenes Bild zu einem Symptom.</summary>
public sealed record SymptomPhotoDto(
    int PhotoId,
    int GrowId,
    string GrowName,
    string RelativePath,
    string? Caption,
    string Tag,
    DateTime TakenAtUtc);

/// <summary>
/// Die eigene Fotosammlung als Nachschlagewerk zu den Symptomen.
/// </summary>
/// <remarks>
/// <para><b>Warum nur eigene Bilder.</b> Zu den 20 Symptomen und 8 Erregern der
/// Wissensbasis gab es nie ein Bild — und fremde Beispielbilder sind nicht zu
/// haben, ohne fremde Rechte zu verletzen. Es geht aber auch besser: wer im
/// eigenen Zelt einmal braune, schleimige Wurzeln fotografiert hat, hat damit
/// das brauchbarste Vergleichsbild überhaupt. Gleiches Licht, gleiche Kamera,
/// gleiche Anlage.</para>
///
/// <para>Deshalb bekommt ein Foto lediglich einen Symptom-Schlüssel. Kein
/// zweiter Bildbestand, kein Download von irgendwoher — nur eine Zuordnung
/// zwischen dem, was schon da ist, und dem, was die Wissensbasis schon
/// beschreibt.</para>
///
/// <para>Der Nutzen wächst mit den Läufen: beim dritten Mal Wurzelfäule sieht
/// man, wie die ersten beiden aussahen und wie es danach weiterging.</para>
/// </remarks>
[ApiController]
[Route("api")]
[Produces("application/json")]
[KiStufe(KiStufe.Dokumentieren)]
public sealed class SymptomPhotosApiController : ApiControllerBase
{
    private readonly PhotoRepository _photos;
    private readonly GrowRepository _grows;
    private readonly KnowledgeBaseLoader _knowledge;

    public SymptomPhotosApiController(PhotoRepository photos, GrowRepository grows, KnowledgeBaseLoader knowledge)
    {
        _photos = photos;
        _grows = grows;
        _knowledge = knowledge;
    }

    /// <summary>Die eigenen Aufnahmen zu einem Symptom.</summary>
    [HttpGet("knowledge/symptoms/{symptomId}/photos")]
    [ProducesResponseType(typeof(IReadOnlyList<SymptomPhotoDto>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<SymptomPhotoDto>> ForSymptom(string symptomId)
        => Ok(_photos.GetBySymptom(symptomId).Select(foto => new SymptomPhotoDto(
            foto.Id,
            foto.GrowId,
            _grows.GetGrow(foto.GrowId)?.Name ?? $"Grow {foto.GrowId}",
            foto.RelativePath,
            foto.Caption,
            foto.Tag.ToString(),
            foto.TakenAtUtc)).ToList());

    /// <summary>Ein Bild einem Symptom zuordnen oder die Zuordnung lösen.</summary>
    [HttpPatch("photos/{photoId:int}/symptom")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult Assign(int photoId, [FromBody] SymptomPhotoAssignRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_photos.GetById(photoId) is null)
        {
            return NotFoundError("photo_not_found", $"Foto mit Id {photoId} existiert nicht.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var schluessel = string.IsNullOrWhiteSpace(request.SymptomId) ? null : request.SymptomId.Trim();

        // Ein Schluessel, den die Wissensbasis nicht kennt, waere eine Zuordnung
        // ins Leere: das Bild taucht dann nirgends wieder auf, und niemand
        // merkt es. Lieber jetzt ablehnen.
        if (schluessel is not null && !_knowledge.Symptoms.Any(s => string.Equals(s.Id, schluessel, StringComparison.Ordinal)))
        {
            ModelState.AddModelError(nameof(request.SymptomId), $"„{schluessel}“ ist kein Symptom aus der Wissensbasis.");
            return ValidationError();
        }

        _photos.SetSymptom(photoId, schluessel);
        return NoContent();
    }
}
