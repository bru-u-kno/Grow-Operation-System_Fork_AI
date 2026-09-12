using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>
/// Fork AI (forkai.22): Die Geräteliste — alles, was der Fork an Entitäten benutzt,
/// nach Geräten sortiert.
/// </summary>
/// <remarks>
/// <para>Diese Etappe liest nur. Geändert wird weiter an den bisherigen Stellen;
/// die Liste sagt dafür, wo das ist — je Entität steht, wofür sie benutzt wird und
/// aus welcher Quelle das kommt.</para>
/// </remarks>
[ApiController]
[Route("api/geraete")]
[Produces("application/json")]
public sealed class GeraeteApiController : ControllerBase
{
    private readonly GeraeteUebersichtService _geraete;

    public GeraeteApiController(GeraeteUebersichtService geraete) => _geraete = geraete;

    [HttpGet]
    [ProducesResponseType(typeof(GeraeteSeiteDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<GeraeteSeiteDto>> Liste(CancellationToken ct)
    {
        var geraete = await _geraete.AlleAsync(ct);

        var zeilen = geraete.Select(g => new GeraetDto(
            g.Schluessel,
            g.Name,
            g.ElternSchluessel,
            g.Anschluss,
            g.IstController,
            g.Bestaetigt,
            g.Modell,
            g.TentId,
            g.HardwareItemId,
            g.Entitaeten.Select(e => new GeraetEntitaetDto(
                e.EntityId,
                e.Verwendungen.Select(v => new GeraetVerwendungDto(v.Zweck, v.Quelle)).ToList())).ToList()))
            .ToList();

        return Ok(new GeraeteSeiteDto(
            zeilen,
            zeilen.Count,
            zeilen.Sum(z => z.Entitaeten.Count),
            zeilen.Count(z => !z.Bestaetigt)));
    }
}

public sealed record GeraetVerwendungDto(string Zweck, string Quelle);

public sealed record GeraetEntitaetDto(string EntityId, IReadOnlyList<GeraetVerwendungDto> Verwendungen);

/// <param name="ElternSchluessel">Der Controller, in dessen Port das Gerät steckt.</param>
/// <param name="Anschluss">Die Steckstelle am Eltern-Gerät, etwa „Port 5".</param>
/// <param name="Vermutet">Weder vom Nutzer noch von Home Assistant bestätigt — aus dem Namen geraten.</param>
public sealed record GeraetDto(
    string Schluessel,
    string Name,
    string? ElternSchluessel,
    string? Anschluss,
    bool IstController,
    bool Bestaetigt,
    string? Modell,
    int? TentId,
    int? HardwareItemId,
    IReadOnlyList<GeraetEntitaetDto> Entitaeten)
{
    public bool Vermutet => !Bestaetigt;
}

public sealed record GeraeteSeiteDto(
    IReadOnlyList<GeraetDto> Geraete,
    int AnzahlGeraete,
    int AnzahlEntitaeten,
    int AnzahlVermutet);
