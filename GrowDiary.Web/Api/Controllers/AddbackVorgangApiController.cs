using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Mapping;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>
/// Das Nachfüllen (Addback) als ein Vorgang (A-006, Etappe 3, 05.10.2026).
/// </summary>
/// <remarks>
/// <para>Dasselbe Muster wie der Wasserwechsel (<see cref="WasserwechselApiController"/>):
/// ein Speichern legt Addback-Eintrag, Messung vorher und nachher,
/// Verbrauchsbuchungen und Tagebuchzeile an — in einer Transaktion
/// (<see cref="AddbackVorgangRepository"/>). Löschen nimmt alles mit.
/// Geprüft wird mit denselben Regeln (<see cref="VorgangEingabe"/>).</para>
///
/// <para>Die Sensorwerte „vorher" kommen vom selben Endpunkt wie beim Wechsel
/// (<c>GET …/wasserwechsel/sensor</c>) — die Frage „was zeigte der Tank kurz
/// davor?" ist dieselbe, und eine zweite Antwort darauf wäre eine zweite Wahrheit.</para>
///
/// <para><b>KI-Zugriff: Dokumentieren.</b> Wie die Teile, aus denen der Vorgang
/// besteht (Addback-Eintrag, Messung, Verbrauch, Journal). Er schaltet nichts
/// und ändert keinen Plan. Löschen sichert vorher.</para>
/// </remarks>
[ApiController]
[Route("api/grows/{growId:int}/addback/vorgaenge")]
[Produces("application/json")]
[KiStufe(KiStufe.Dokumentieren)]
public sealed class AddbackVorgangApiController : ApiControllerBase
{
    private readonly GrowRepository _grows;
    private readonly AddbackVorgangRepository _vorgaenge;
    private readonly KostenRepository _kosten;
    private readonly JournalRepository _journal;
    private readonly AuditRepository _audit;
    private readonly MeasurementSanityService _sperre;
    private readonly MischplanService _mischplan;

    public AddbackVorgangApiController(
        GrowRepository grows,
        AddbackVorgangRepository vorgaenge,
        KostenRepository kosten,
        JournalRepository journal,
        AuditRepository audit,
        MeasurementSanityService sperre,
        MischplanService mischplan)
    {
        _grows = grows;
        _vorgaenge = vorgaenge;
        _kosten = kosten;
        _journal = journal;
        _audit = audit;
        _sperre = sperre;
        _mischplan = mischplan;
    }

    /// <summary>Alle Nachfüll-Vorgänge des Grows, neueste zuerst.</summary>
    [HttpGet("")]
    [ProducesResponseType(typeof(IReadOnlyList<AddbackVorgangDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<IReadOnlyList<AddbackVorgangDto>> Liste(int growId)
    {
        if (_grows.GetGrow(growId) is null) return NotFoundError("grow_not_found", $"Grow mit Id {growId} existiert nicht.");
        var artikel = _kosten.GetArtikel().ToDictionary(a => a.Id);
        var eintraege = _grows.GetAddbackLogsForGrow(growId).ToDictionary(e => e.Id);
        return Ok(_vorgaenge.FuerGrow(growId).Select(v => AlsDto(v, artikel, eintraege)).ToList());
    }

    /// <summary>Ein Nachfüll-Vorgang mit allem, was er angelegt hat.</summary>
    [HttpGet("{vorgangId:int}")]
    [ProducesResponseType(typeof(AddbackVorgangDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<AddbackVorgangDto> Einzeln(int growId, int vorgangId)
    {
        if (_grows.GetGrow(growId) is null) return NotFoundError("grow_not_found", $"Grow mit Id {growId} existiert nicht.");
        var vorgang = _vorgaenge.Get(growId, vorgangId);
        if (vorgang is null) return NotFoundError("vorgang_not_found", $"Zu diesem Grow gibt es keinen Nachfüll-Vorgang {vorgangId}.");
        return Ok(AlsDto(vorgang, _kosten.GetArtikel().ToDictionary(a => a.Id), _grows.GetAddbackLogsForGrow(growId).ToDictionary(e => e.Id)));
    }

    /// <summary>Legt den Vorgang an — alles in einer Transaktion.</summary>
    [HttpPost("")]
    [ProducesResponseType(typeof(AddbackVorgangDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<AddbackVorgangDto> Anlegen(int growId, [FromBody] AddbackVorgangRequest request)
    {
        var grow = _grows.GetGrow(growId);
        if (grow is null) return NotFoundError("grow_not_found", $"Grow mit Id {growId} existiert nicht.");
        if (!ModelState.IsValid) return ValidationError();

        var eingabe = new VorgangEingabe(ModelState, _sperre);
        var zeitpunkt = eingabe.Zeitpunkt(request.ZeitpunktLokal, nameof(request.ZeitpunktLokal), "Ein Nachfüllen wird erfasst, nachdem es war.");

        // ---- Nachfüllen
        eingabe.LiterPruefen(request.Liter, nameof(request.Liter), "Wie viele Liter hast du nachgefüllt?");
        if (!Enum.IsDefined(request.Art)) ModelState.AddModelError(nameof(request.Art), "Die Art des Nachfüllens ist ungültig.");
        eingabe.WasserPruefen(request.Wasser, request.OsmoseProzent, request.WasserEcMsCm);
        MeasurementSanityService.PhysikGrenze(ModelState, nameof(request.EcZiel), "ec", request.EcZiel, "Das EC-Ziel");

        // ---- Messungen: dieselbe Sperre wie jede Messung. Ein Nachfüllen ist
        // kein Lösungswechsel — „nachher" startet die Wechsel-Erinnerung nicht.
        var vorher = eingabe.AlsMessung(grow, request.Vorher, "Vorher", zeitpunkt.AddMinutes(-1), solutionChange: false,
            notiz: VorgangEingabe.VorherNotiz(request.Vorher, "Vor dem Nachfüllen"));
        var nachher = eingabe.AlsMessung(grow, request.Nachher, "Nachher", zeitpunkt, solutionChange: false,
            notiz: "Nach dem Nachfüllen.");
        eingabe.VorherVorDemVorgang(vorher, zeitpunkt, "Nachfüllen");

        // ---- Buchungen und Tagebuch (Art „Fütterung", wie es das Journal schon kennt)
        var buchungen = eingabe.Buchungen(request.Buchungen, _kosten.GetArtikel().ToDictionary(a => a.Id));
        var tagebuch = eingabe.Tagebuch(request.Tagebuch, growId, JournalEntryType.Feeding, zeitpunkt);

        if (!ModelState.IsValid) return ValidationError();

        var eintrag = new AddbackLogEntry
        {
            GrowId = growId,
            HydroSetupId = grow.SystemId,
            Kind = request.Art,
            PerformedAtUtc = zeitpunkt.ToUniversalTime(),
            // Das Anlagevolumen aus derselben Quelle wie der Mischplan — nicht
            // eine zweite Rechnung (Eine Wahrheit je Zahl).
            ReservoirLiters = _mischplan.FuerGrow(growId)?.VolumenLiter,
            EcBefore = vorher?.ReservoirEc,
            EcTarget = request.EcZiel,
            EcAfter = nachher?.ReservoirEc,
            PhBefore = vorher?.ReservoirPh,
            PhAfter = nachher?.ReservoirPh,
            LitersAdded = request.Liter,
            WaterUsed = request.Wasser,
            WaterEcMsCm = request.WasserEcMsCm,
            Notes = request.Notiz,
        };

        var (herkunft, sensorZeit) = VorgangEingabe.VorherVermerk(vorher, request.Vorher);
        var vorgang = _vorgaenge.Anlegen(new AddbackVorgangEntwurf
        {
            GrowId = growId,
            Eintrag = eintrag,
            Vorher = vorher,
            Nachher = nachher,
            Buchungen = buchungen,
            Tagebuch = tagebuch,
            OsmoseProzent = request.Wasser == WaterSource.Mixed ? request.OsmoseProzent : null,
            VorherHerkunft = herkunft,
            VorherSensorZeitUtc = sensorZeit,
        });

        foreach (var messung in new[] { vorher, nachher }.OfType<Measurement>())
        {
            _audit.LogMeasurementCreated(growId, messung.Id, messung.Stage, messung.TakenAt, messung.Source);
        }

        // Wie beim Anlegen einer Messung: der erste Eintrag startet den Grow.
        if (grow.Status == GrowStatus.Planning)
        {
            grow.Status = GrowStatus.Running;
            _grows.UpdateGrow(grow);
        }

        var dto = AlsDto(vorgang, _kosten.GetArtikel().ToDictionary(a => a.Id), _grows.GetAddbackLogsForGrow(growId).ToDictionary(e => e.Id));
        return CreatedAtAction(nameof(Einzeln), new { growId, vorgangId = vorgang.Id }, dto);
    }

    /// <summary>Löscht den Vorgang mit Addback-Eintrag, Messungen, Buchungen und Tagebuchzeile.</summary>
    [HttpDelete("{vorgangId:int}")]
    [KiSicherungVorher]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public IActionResult Loeschen(int growId, int vorgangId)
    {
        if (_grows.GetGrow(growId) is null) return NotFoundError("grow_not_found", $"Grow mit Id {growId} existiert nicht.");
        return _vorgaenge.Loeschen(growId, vorgangId)
            ? NoContent()
            : NotFoundError("vorgang_not_found", $"Zu diesem Grow gibt es keinen Nachfüll-Vorgang {vorgangId}.");
    }

    private AddbackVorgangDto AlsDto(AddbackVorgang v, IReadOnlyDictionary<int, Verbrauchsartikel> artikel, IReadOnlyDictionary<int, AddbackLogEntry> eintraege)
        => new(
            v.Id,
            v.GrowId,
            v.ErstelltAmUtc,
            v.AddbackLogId is { } lid && eintraege.TryGetValue(lid, out var e) ? e.ToDto() : null,
            v.MessungVorherId is { } vid ? _grows.GetMeasurement(vid)?.ToDto() : null,
            v.MessungNachherId is { } nid ? _grows.GetMeasurement(nid)?.ToDto() : null,
            VorgangEingabe.BuchungenAlsDto(_vorgaenge.Buchungen(v.Id), artikel),
            v.JournalId is { } jid ? _journal.Get(jid)?.ToDto() : null,
            v.OsmoseProzent,
            v.VorherHerkunft,
            v.VorherSensorZeitUtc);
}
