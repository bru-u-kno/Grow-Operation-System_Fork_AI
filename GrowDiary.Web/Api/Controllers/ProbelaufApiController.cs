using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>
/// Fork AI (A-010, 07.10.2026): Der Probelauf — eine Steuerung für einige Minuten abschalten, aufzeichnen,
/// zurückstellen, auswerten.
/// </summary>
/// <remarks>
/// <para>Starten und Abbrechen wirken sofort aufs Zelt: Stufe „Geräte schalten", über einen Schlüssel also nur
/// mit dieser Freigabe und gezählt gegen die Höchstwerte des Schlüssels.</para>
/// <para>Die Empfehlung der KI (<c>…/empfehlung</c>) gibt es nur bei „KI an".</para>
/// </remarks>
[ApiController]
[Route("api/steuerung/probelauf")]
[Produces("application/json")]
public sealed class ProbelaufApiController : ApiControllerBase
{
    private readonly ProbelaufService _dienst;
    private readonly ProbelaufRepository _repo;
    private readonly IProbelaufMessung _messung;
    private readonly KiHauptschalter _ki;
    private readonly KenntnisstandService _kenntnis;
    private readonly TimeProvider _zeit;

    public ProbelaufApiController(
        ProbelaufService dienst, ProbelaufRepository repo, IProbelaufMessung messung, KiHauptschalter ki,
        KenntnisstandService kenntnis, TimeProvider? zeit = null)
    {
        _kenntnis = kenntnis;
        _dienst = dienst;
        _repo = repo;
        _messung = messung;
        _ki = ki;
        _zeit = zeit ?? TimeProvider.System;
    }

    /// <summary>Die Steuerungen, an denen ein Probelauf möglich ist, mit ihrem zentral gepflegten Titel.</summary>
    [HttpGet("module")]
    [ProducesResponseType(typeof(IReadOnlyList<ProbelaufModulDto>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<ProbelaufModulDto>> Module()
        => Ok(ProbelaufEingriff.Module.Select(m => new ProbelaufModulDto(m, SteuerungApiController.ModulTitel(m))).ToList());

    /// <summary>Die vorgeschlagenen Grenzen aus den Pflanzenzielen.</summary>
    [HttpGet("voreinstellung")]
    [ProducesResponseType(typeof(ProbelaufGrenzen), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProbelaufGrenzen>> Voreinstellung(CancellationToken ct)
        => Ok(await _messung.VoreinstellungAsync(ct));

    /// <summary>
    /// Der Kenntnisstand: Kann das System die Zielwerte des Plans halten? Was bewirkt jedes Gerät? Wo fehlen Messungen?
    /// Vom Fork berechnet, ohne KI.
    /// </summary>
    [HttpGet("kenntnisstand")]
    [ProducesResponseType(typeof(Kenntnisstand), StatusCodes.Status200OK)]
    public async Task<ActionResult<Kenntnisstand>> Kenntnisstand(CancellationToken ct)
        => Ok(await _kenntnis.BerechnenAsync(ct));

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ProbelaufLaufDto>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<ProbelaufLaufDto>> Liste()
        => Ok(_repo.Liste().Select(l => Dto(l, mitMessreihe: false)).ToList());

    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(ProbelaufLaufDto), StatusCodes.Status200OK)]
    public ActionResult<ProbelaufLaufDto> Einzeln(long id)
        => _repo.Holen(id) is { } lauf ? Ok(Dto(lauf, mitMessreihe: true)) : NotFoundError("probelauf_nicht_gefunden", "Diesen Probelauf gibt es nicht.");

    /// <summary>Startet einen Probelauf. Greift sofort ins Zelt ein.</summary>
    [HttpPost]
    [KiStufe(KiStufe.GeraeteSchalten)]
    [ProducesResponseType(typeof(ProbelaufLaufDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<ProbelaufLaufDto>> Starten([FromBody] ProbelaufStartRequest request, CancellationToken ct)
    {
        if (request is null) return BadRequestError("probelauf_invalid", "Es wurde nichts übergeben.");

        var ergebnis = await _dienst.StartenAsync(
            new ProbelaufStart(request.Modul ?? string.Empty, request.DauerMinuten ?? ProbelaufService.VorgabeDauerMinuten, request.Grenzen), ct);

        return ergebnis.Fehler switch
        {
            ProbelaufFehler.Keiner => Created($"api/steuerung/probelauf/{ergebnis.Lauf!.Id}", Dto(ergebnis.Lauf, mitMessreihe: true)),
            ProbelaufFehler.ModulUnbekannt => BadRequestError("probelauf_modul_unbekannt", ergebnis.Meldung!),
            ProbelaufFehler.DauerUngueltig => BadRequestError("probelauf_dauer_ungueltig", ergebnis.Meldung!),
            ProbelaufFehler.KeineMessung => BadRequestError("probelauf_keine_messung", ergebnis.Meldung!),
            ProbelaufFehler.GrenzeVerletzt => BadRequestError("probelauf_grenze_verletzt", ergebnis.Meldung!),
            ProbelaufFehler.LaeuftSchon => ConflictError("probelauf_laeuft_schon", ergebnis.Meldung!),
            _ => ConflictError("probelauf_eingriff_fehlgeschlagen", ergebnis.Meldung!),
        };
    }

    /// <summary>Beendet den Eingriff von Hand: es wird zurückgestellt, der Nachlauf wird noch aufgezeichnet.</summary>
    [HttpPost("{id:long}/abbrechen")]
    [KiStufe(KiStufe.GeraeteSchalten)]
    [ProducesResponseType(typeof(ProbelaufLaufDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProbelaufLaufDto>> Abbrechen(long id, CancellationToken ct)
    {
        var lauf = await _dienst.AbbrechenAsync(id, "Von Hand abgebrochen.", ct);
        return lauf is null ? NotFoundError("probelauf_nicht_gefunden", "Diesen Probelauf gibt es nicht.") : Ok(Dto(lauf, mitMessreihe: true));
    }

    /// <summary>Legt die Empfehlung der KI zu einem Lauf ab. Nur bei „KI an".</summary>
    [HttpPost("{id:long}/empfehlung")]
    [KiStufe(KiStufe.Dokumentieren)]
    [ProducesResponseType(typeof(ProbelaufLaufDto), StatusCodes.Status200OK)]
    public ActionResult<ProbelaufLaufDto> Empfehlung(long id, [FromBody] ProbelaufEmpfehlungRequest request)
    {
        if (!_ki.Aktiv) return NotFoundError(KiHauptschalter.FehlerCode, KiHauptschalter.FehlerText);
        if (string.IsNullOrWhiteSpace(request?.Text)) return BadRequestError("probelauf_empfehlung_leer", "Die Empfehlung ist leer.");
        if (_repo.Holen(id) is not { } lauf) return NotFoundError("probelauf_nicht_gefunden", "Diesen Probelauf gibt es nicht.");

        lauf.Empfehlung = request.Text.Trim();
        _repo.Speichern(lauf);
        return Ok(Dto(lauf, mitMessreihe: true));
    }

    private ProbelaufLaufDto Dto(ProbelaufLauf l, bool mitMessreihe)
    {
        var rest = l.Status == ProbelaufStatus.Laeuft
            ? (int)Math.Max(0, (l.GeplantesEndeUtc - _zeit.GetUtcNow().UtcDateTime).TotalSeconds)
            : 0;
        return new ProbelaufLaufDto(
            l.Id, l.Modul, SteuerungApiController.ModulTitel(l.Modul), l.Status.ToString(), l.StartUtc, l.GeplantesEndeUtc,
            l.EingriffEndeUtc, l.EndeUtc, rest, l.AbbruchGrund, l.Grenzen, l.Auswertung,
            // Ohne KI sieht niemand eine Empfehlung — auch keine, die früher abgelegt wurde.
            _ki.Aktiv ? l.Empfehlung : null,
            mitMessreihe ? l.Messreihe : null);
    }
}
