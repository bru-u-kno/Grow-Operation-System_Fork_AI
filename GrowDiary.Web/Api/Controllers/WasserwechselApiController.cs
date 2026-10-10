using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Mapping;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>
/// Der Wasserwechsel als ein Vorgang (A-006, 05.10.2026).
/// </summary>
/// <remarks>
/// <para>Ein Speichern legt Wechsel, Messung vorher und nachher,
/// Verbrauchsbuchungen und Tagebuchzeile an — in einer Transaktion
/// (<see cref="WasserwechselVorgangRepository"/>). Löschen nimmt alles mit.</para>
///
/// <para><b>KI-Zugriff: Dokumentieren.</b> Dieselbe Stufe wie die Teile, aus
/// denen der Vorgang besteht: Messung anlegen, Wechsel erfassen, Verbrauch
/// buchen, Journal. Der Vorgang darf nichts, was die Teile einzeln nicht
/// dürften — er schaltet nichts und ändert keinen Plan. Den Artikel
/// „Osmosewasser" legt er höchstens neu an; anlegen ist auch unter Kosten
/// Dokumentieren. Löschen sichert vorher (<see cref="KiSicherungVorherAttribute"/>),
/// wie beim Wechsel und der Messung.</para>
/// </remarks>
[ApiController]
[Route("api/grows/{growId:int}/wasserwechsel")]
[Produces("application/json")]
[KiStufe(KiStufe.Dokumentieren)]
public sealed class WasserwechselApiController : ApiControllerBase
{
    /// <summary>So weit sucht „vorher“ zurück — die Zahl steht beim Dienst (<see cref="TankSensorService"/>).</summary>
    public const int SensorFensterMinuten = TankSensorService.StandardFensterMinuten;

    private readonly GrowRepository _grows;
    private readonly WasserwechselVorgangRepository _vorgaenge;
    private readonly KostenRepository _kosten;
    private readonly JournalRepository _journal;
    private readonly AuditRepository _audit;
    private readonly MeasurementSanityService _sperre;
    private readonly TankSensorService _tank;
    private readonly MischplanService _mischplan;

    public WasserwechselApiController(
        GrowRepository grows,
        WasserwechselVorgangRepository vorgaenge,
        KostenRepository kosten,
        JournalRepository journal,
        AuditRepository audit,
        MeasurementSanityService sperre,
        TankSensorService tank,
        MischplanService mischplan)
    {
        _grows = grows;
        _vorgaenge = vorgaenge;
        _kosten = kosten;
        _journal = journal;
        _audit = audit;
        _sperre = sperre;
        _tank = tank;
        _mischplan = mischplan;
    }

    /// <summary>Alle Vorgänge des Grows, neueste zuerst.</summary>
    [HttpGet("")]
    [ProducesResponseType(typeof(IReadOnlyList<WasserwechselVorgangDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<IReadOnlyList<WasserwechselVorgangDto>> Liste(int growId)
    {
        if (_grows.GetGrow(growId) is null) return NotFoundError("grow_not_found", $"Grow mit Id {growId} existiert nicht.");
        var artikel = _kosten.GetArtikel().ToDictionary(a => a.Id);
        var wechsel = _grows.GetChangeoutsForGrow(growId).ToDictionary(w => w.Id);
        return Ok(_vorgaenge.FuerGrow(growId).Select(v => AlsDto(v, artikel, wechsel)).ToList());
    }

    /// <summary>Ein Vorgang mit allem, was er angelegt hat.</summary>
    [HttpGet("{vorgangId:int}")]
    [ProducesResponseType(typeof(WasserwechselVorgangDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<WasserwechselVorgangDto> Einzeln(int growId, int vorgangId)
    {
        if (_grows.GetGrow(growId) is null) return NotFoundError("grow_not_found", $"Grow mit Id {growId} existiert nicht.");
        var vorgang = _vorgaenge.Get(growId, vorgangId);
        if (vorgang is null) return NotFoundError("vorgang_not_found", $"Zu diesem Grow gibt es keinen Wasserwechsel-Vorgang {vorgangId}.");
        return Ok(AlsDto(vorgang, _kosten.GetArtikel().ToDictionary(a => a.Id), _grows.GetChangeoutsForGrow(growId).ToDictionary(w => w.Id)));
    }

    /// <summary>
    /// Was die Sensoren des Zelts kurz vor einem Zeitpunkt zeigten — EC, pH, Wassertemperatur.
    /// </summary>
    /// <remarks>
    /// <para><b>Woher.</b> Aus den Rohwerten des Sensorverlaufs (dieselbe Tabelle
    /// wie <c>/api/tents/{id}/history?resolution=raw</c>): je Messgröße der
    /// jüngste Wert im Fenster <see cref="SensorFensterMinuten"/> vor dem
    /// Zeitpunkt. Bewusst nicht die Auto-Messungen „Licht an/aus": die liegen
    /// bis zu zwölf Stunden auseinander und zeigen nicht den Tank kurz vor dem
    /// Wechsel.</para>
    /// <para><b>Grenze.</b> Rohwerte bleiben sieben Tage. Wer einen Wechsel
    /// später nachträgt, bekommt hier nichts — dann gelten die Handwerte.</para>
    /// <para>DO und ORP fehlen absichtlich: Brus Anlage hat dafür keinen Sensor.</para>
    /// </remarks>
    [HttpGet("sensor")]
    [ProducesResponseType(typeof(WasserwechselSensorDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<WasserwechselSensorDto> Sensor(int growId, [FromQuery] DateTime? zeitpunkt = null)
    {
        var grow = _grows.GetGrow(growId);
        if (grow is null) return NotFoundError("grow_not_found", $"Grow mit Id {growId} existiert nicht.");

        var bis = zeitpunkt is { } z ? VorgangEingabe.ZuUtc(z) : DateTime.UtcNow;
        if (grow.TentId is not { } zelt)
        {
            return Ok(new WasserwechselSensorDto(bis, SensorFensterMinuten, null, null, null, "Der Grow steht in keinem Zelt — es gibt keine Sensoren dazu."));
        }

        var (ec, ph, wt) = _tank.Tankwerte(zelt, bis, SensorFensterMinuten);
        var hinweis = ec is null && ph is null && wt is null
            ? $"In den {SensorFensterMinuten} Minuten davor hat kein Sensor einen Tankwert geliefert — trag die Werte von Hand ein."
            : null;
        return Ok(new WasserwechselSensorDto(bis, SensorFensterMinuten, ec, ph, wt, hinweis));
    }

    /// <summary>Legt den Vorgang an — alles in einer Transaktion.</summary>
    [HttpPost("")]
    [ProducesResponseType(typeof(WasserwechselVorgangDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<WasserwechselVorgangDto> Anlegen(int growId, [FromBody] WasserwechselVorgangRequest request)
    {
        var grow = _grows.GetGrow(growId);
        if (grow is null) return NotFoundError("grow_not_found", $"Grow mit Id {growId} existiert nicht.");
        if (!ModelState.IsValid) return ValidationError();

        var eingabe = new VorgangEingabe(ModelState, _sperre);
        var zeitpunkt = eingabe.Zeitpunkt(request.ZeitpunktLokal, nameof(request.ZeitpunktLokal), "Ein Wasserwechsel wird erfasst, nachdem er war.");

        // ---- Wechsel
        eingabe.LiterPruefen(request.Liter, nameof(request.Liter), "Wie viele Liter hast du neu angesetzt?");
        if (!Enum.IsDefined(request.Art)) ModelState.AddModelError(nameof(request.Art), "Die Art des Wechsels ist ungültig.");
        eingabe.WasserPruefen(request.Wasser, request.OsmoseProzent, request.WasserEcMsCm);

        // ---- Messungen: dieselbe Sperre wie jede Messung
        var vorher = eingabe.AlsMessung(grow, request.Vorher, "Vorher", zeitpunkt.AddMinutes(-1), solutionChange: false,
            notiz: VorgangEingabe.VorherNotiz(request.Vorher, "Vor dem Wasserwechsel"));
        var nachher = eingabe.AlsMessung(grow, request.Nachher, "Nachher", zeitpunkt, solutionChange: request.ErinnerungNeuStarten,
            notiz: "Nach dem Wasserwechsel.");
        eingabe.VorherVorDemVorgang(vorher, zeitpunkt, "Wechsel");

        // ---- Buchungen und Tagebuch
        var buchungen = eingabe.Buchungen(request.Buchungen, _kosten.GetArtikel().ToDictionary(a => a.Id));
        var tagebuch = eingabe.Tagebuch(request.Tagebuch, growId, JournalEntryType.ReservoirChange, zeitpunkt);

        if (!ModelState.IsValid) return ValidationError();

        var volumen = _mischplan.FuerGrow(growId)?.VolumenLiter;
        var wechsel = new ChangeoutEntry
        {
            GrowId = growId,
            HydroSetupId = grow.SystemId,
            Kind = request.Art,
            PerformedAtUtc = zeitpunkt.ToUniversalTime(),
            VolumeChangedLiters = request.Liter,
            PercentChanged = request.Art == ChangeoutKind.Full
                ? 100
                : volumen is > 0 && request.Liter <= volumen ? Math.Round(request.Liter!.Value / volumen.Value * 100, 0) : null,
            EcBefore = vorher?.ReservoirEc,
            EcAfter = nachher?.ReservoirEc,
            PhBefore = vorher?.ReservoirPh,
            PhAfter = nachher?.ReservoirPh,
            WaterUsed = request.Wasser,
            WaterEcMsCm = request.WasserEcMsCm,
            ErinnerungNeuStarten = request.ErinnerungNeuStarten,
            Notes = request.Notiz,
        };

        var vorgang = _vorgaenge.Anlegen(new WasserwechselVorgangEntwurf
        {
            GrowId = growId,
            Wechsel = wechsel,
            Vorher = vorher,
            Nachher = nachher,
            Buchungen = buchungen,
            Tagebuch = tagebuch,
            OsmoseProzent = request.Wasser == WaterSource.Mixed ? request.OsmoseProzent : null,
            VorherHerkunft = VorgangEingabe.VorherVermerk(vorher, request.Vorher).Herkunft,
            VorherSensorZeitUtc = VorgangEingabe.VorherVermerk(vorher, request.Vorher).SensorZeitUtc,
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

        var dto = AlsDto(vorgang, _kosten.GetArtikel().ToDictionary(a => a.Id), _grows.GetChangeoutsForGrow(growId).ToDictionary(w => w.Id));
        return CreatedAtAction(nameof(Einzeln), new { growId, vorgangId = vorgang.Id }, dto);
    }

    /// <summary>Löscht den Vorgang mit Wechsel, Messungen, Buchungen und Tagebuchzeile.</summary>
    [HttpDelete("{vorgangId:int}")]
    [KiSicherungVorher]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public IActionResult Loeschen(int growId, int vorgangId)
    {
        if (_grows.GetGrow(growId) is null) return NotFoundError("grow_not_found", $"Grow mit Id {growId} existiert nicht.");
        return _vorgaenge.Loeschen(growId, vorgangId)
            ? NoContent()
            : NotFoundError("vorgang_not_found", $"Zu diesem Grow gibt es keinen Wasserwechsel-Vorgang {vorgangId}.");
    }

    private WasserwechselVorgangDto AlsDto(WasserwechselVorgang v, IReadOnlyDictionary<int, Verbrauchsartikel> artikel, IReadOnlyDictionary<int, ChangeoutEntry> wechsel)
    {
        var buchungen = VorgangEingabe.BuchungenAlsDto(_vorgaenge.Buchungen(v.Id), artikel);
        return new WasserwechselVorgangDto(
            v.Id,
            v.GrowId,
            v.ErstelltAmUtc,
            v.ChangeoutId is { } cid && wechsel.TryGetValue(cid, out var w) ? w.ToDto() : null,
            v.MessungVorherId is { } vid ? _grows.GetMeasurement(vid)?.ToDto() : null,
            v.MessungNachherId is { } nid ? _grows.GetMeasurement(nid)?.ToDto() : null,
            buchungen,
            v.JournalId is { } jid ? _journal.Get(jid)?.ToDto() : null,
            v.OsmoseProzent,
            v.VorherHerkunft,
            v.VorherSensorZeitUtc);
    }
}
