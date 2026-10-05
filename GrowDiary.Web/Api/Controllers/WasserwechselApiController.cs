using System.Globalization;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Mapping;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

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
    /// <summary>Format von <see cref="WasserwechselVorgangRequest.ZeitpunktLokal"/>.</summary>
    private const string ZeitFormat = "yyyy-MM-ddTHH:mm";

    /// <summary>
    /// So weit sucht „vorher" zurück: 30 Minuten.
    /// </summary>
    /// <remarks>
    /// Annahme, kein Messwert: die Sensoren schreiben alle 5 Minuten
    /// (<see cref="SensorHistoryApiController"/>). Sechs Takte Luft decken einen
    /// kurzen Ausfall ab; was älter ist, zeigt nicht mehr den Tank „kurz vor dem
    /// Wechsel". Dann trägt man von Hand ein.
    /// </remarks>
    public const int SensorFensterMinuten = 30;

    private readonly GrowRepository _grows;
    private readonly WasserwechselVorgangRepository _vorgaenge;
    private readonly KostenRepository _kosten;
    private readonly JournalRepository _journal;
    private readonly AuditRepository _audit;
    private readonly MeasurementSanityService _sperre;
    private readonly SensorReadingRepository _sensoren;
    private readonly MischplanService _mischplan;

    public WasserwechselApiController(
        GrowRepository grows,
        WasserwechselVorgangRepository vorgaenge,
        KostenRepository kosten,
        JournalRepository journal,
        AuditRepository audit,
        MeasurementSanityService sperre,
        SensorReadingRepository sensoren,
        MischplanService mischplan)
    {
        _grows = grows;
        _vorgaenge = vorgaenge;
        _kosten = kosten;
        _journal = journal;
        _audit = audit;
        _sperre = sperre;
        _sensoren = sensoren;
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

        var bis = zeitpunkt is { } z ? ZuUtc(z) : DateTime.UtcNow;
        if (grow.TentId is not { } zelt)
        {
            return Ok(new WasserwechselSensorDto(bis, SensorFensterMinuten, null, null, null, "Der Grow steht in keinem Zelt — es gibt keine Sensoren dazu."));
        }

        var von = bis.AddMinutes(-SensorFensterMinuten);
        SensorWertDto? Juengster(string metrik)
            => _sensoren.GetReadings(zelt, metrik, von, bis).LastOrDefault() is { } r ? new SensorWertDto(r.Value, r.CapturedAtUtc) : null;

        var ec = Juengster("reservoir-ec");
        var ph = Juengster("reservoir-ph");
        var wt = Juengster("reservoir-temp");
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

        // ---- Zeitpunkt
        var zeitpunkt = DateTime.Now;
        if (!string.IsNullOrWhiteSpace(request.ZeitpunktLokal))
        {
            if (!DateTime.TryParse(request.ZeitpunktLokal, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out zeitpunkt))
            {
                ModelState.AddModelError(nameof(request.ZeitpunktLokal), "Datum oder Uhrzeit konnten nicht gelesen werden.");
                return ValidationError();
            }
        }

        // Wie beim Wechsel: ein Zeitpunkt in der Zukunft ist ein Plan, keine Erfassung.
        if (zeitpunkt.ToUniversalTime() > DateTime.UtcNow.AddHours(1))
        {
            ModelState.AddModelError(nameof(request.ZeitpunktLokal), "Der Zeitpunkt liegt in der Zukunft. Ein Wasserwechsel wird erfasst, nachdem er war.");
        }

        // ---- Wechsel
        if (request.Liter is not { } liter || !double.IsFinite(liter) || liter <= 0)
        {
            ModelState.AddModelError(nameof(request.Liter), "Wie viele Liter hast du neu angesetzt? Die Menge muss größer als 0 sein.");
        }

        if (!Enum.IsDefined(request.Art)) ModelState.AddModelError(nameof(request.Art), "Die Art des Wechsels ist ungültig.");
        if (!Enum.IsDefined(request.Wasser)) ModelState.AddModelError(nameof(request.Wasser), "Die Wasserart ist ungültig.");

        if (request.Wasser == WaterSource.Mixed && request.OsmoseProzent is not (>= 0 and <= 100))
        {
            ModelState.AddModelError(nameof(request.OsmoseProzent), "Bei einer Mischung fehlt der Anteil Osmose (0–100 %).");
        }

        MeasurementSanityService.PhysikGrenze(ModelState, nameof(request.WasserEcMsCm), "ec", request.WasserEcMsCm, "Der EC des Wassers");

        // ---- Messungen: dieselbe Sperre wie jede Messung
        var vorher = AlsMessung(grow, request.Vorher, "Vorher", zeitpunkt.AddMinutes(-1), solutionChange: false,
            notiz: VorherNotiz(request.Vorher));
        var nachher = AlsMessung(grow, request.Nachher, "Nachher", zeitpunkt, solutionChange: request.ErinnerungNeuStarten,
            notiz: "Nach dem Wasserwechsel.");

        if (vorher is not null && vorher.TakenAt > zeitpunkt)
        {
            ModelState.AddModelError("Vorher.ZeitpunktLokal", "Die Werte „vorher“ liegen nach dem Wechsel.");
        }

        // ---- Buchungen
        var artikel = _kosten.GetArtikel().ToDictionary(a => a.Id);
        var buchungen = new List<VorgangBuchungEntwurf>();
        for (var i = 0; i < request.Buchungen.Count; i++)
        {
            var b = request.Buchungen[i];
            var feld = $"Buchungen[{i}]";
            if (!double.IsFinite(b.Menge) || b.Menge <= 0)
            {
                ModelState.AddModelError($"{feld}.Menge", "Die Menge muss größer als 0 sein.");
                continue;
            }

            if (b.ArtikelId is { } id)
            {
                if (!artikel.ContainsKey(id)) ModelState.AddModelError($"{feld}.ArtikelId", $"Verbrauchsartikel {id} existiert nicht.");
                else buchungen.Add(new VorgangBuchungEntwurf(id, null, b.Menge));
            }
            else if (b.Wasser is WaterSource.Tap or WaterSource.RO)
            {
                buchungen.Add(new VorgangBuchungEntwurf(null,
                    b.Wasser == WaterSource.RO ? WasserwechselVorgangRepository.OsmosewasserArtikel : WasserwechselVorgangRepository.LeitungswasserArtikel,
                    b.Menge));
            }
            else
            {
                ModelState.AddModelError($"{feld}.ArtikelId", "Jede Buchung braucht einen Artikel oder die Wasserart (Leitung oder Osmose).");
            }
        }

        // ---- Tagebuch
        JournalEntry? tagebuch = null;
        if (request.Tagebuch is { } tb)
        {
            var titel = tb.Titel?.Trim();
            var text = tb.Text?.Trim();
            if (string.IsNullOrEmpty(titel) && string.IsNullOrEmpty(text))
            {
                ModelState.AddModelError("Tagebuch.Titel", "Für die Tagebuchzeile fehlt Titel oder Text.");
            }
            else
            {
                tagebuch = new JournalEntry
                {
                    GrowId = growId,
                    Title = string.IsNullOrEmpty(titel) ? null : titel,
                    Body = string.IsNullOrEmpty(text) ? null : text,
                    // Seit A-006 setzt nur noch der Ablauf diese Art — das freie
                    // Journal-Formular bietet sie nicht mehr an.
                    EntryType = JournalEntryType.ReservoirChange,
                    Source = ValueOrigin.Manual,
                    OccurredAtUtc = zeitpunkt.ToUniversalTime(),
                };
            }
        }

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
            VorherHerkunft = vorher is null ? null : NormalisierteHerkunft(request.Vorher?.Herkunft),
            VorherSensorZeitUtc = vorher is null ? null : request.Vorher?.SensorZeitUtc is { } sz ? ZuUtc(sz) : null,
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

    /// <summary>
    /// Die Messwerte des Ablaufs als Messung — oder <c>null</c>, wenn keiner da ist.
    /// </summary>
    /// <remarks>
    /// Geprüft mit <see cref="MeasurementSanityService.ApplyBlockingValidation"/>,
    /// also mit denselben Grenzen wie jede Messung (<c>MessfelderVollstaendigTests</c>).
    /// Die Fehler bekommen den Abschnitt vorangestellt („Vorher.ReservoirEc"),
    /// damit die Oberfläche weiß, in welchem Schritt sie stehen.
    /// </remarks>
    private Measurement? AlsMessung(GrowRun grow, VorgangMessungRequest? werte, string abschnitt, DateTime standardZeit, bool solutionChange, string notiz)
    {
        if (werte is null) return null;
        if (werte.ReservoirEc is null && werte.ReservoirPh is null && werte.ReservoirWaterTempC is null
            && werte.DissolvedOxygenMgL is null && werte.OrpMv is null)
        {
            return null;
        }

        var zeit = standardZeit;
        if (!string.IsNullOrWhiteSpace(werte.ZeitpunktLokal)
            && !DateTime.TryParse(werte.ZeitpunktLokal, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out zeit))
        {
            ModelState.AddModelError($"{abschnitt}.ZeitpunktLokal", "Datum oder Uhrzeit konnten nicht gelesen werden.");
            return null;
        }

        var messung = new Measurement
        {
            GrowId = grow.Id,
            TakenAt = zeit,
            Stage = GrowStageResolver.Resolve(grow, zeit.Date),
            Source = NormalisierteHerkunft(werte.Herkunft) == "Sensor" ? ValueOrigin.HomeAssistant : ValueOrigin.Manual,
            Notes = notiz,
            ReservoirEc = werte.ReservoirEc,
            ReservoirPh = werte.ReservoirPh,
            ReservoirWaterTempC = werte.ReservoirWaterTempC,
            DissolvedOxygenMgL = werte.DissolvedOxygenMgL,
            OrpMv = werte.OrpMv,
            SolutionChange = solutionChange,
        };

        var fehler = new ModelStateDictionary();
        _sperre.ApplyBlockingValidation(fehler, grow, messung);
        foreach (var (feld, eintrag) in fehler)
        {
            foreach (var e in eintrag.Errors) ModelState.AddModelError($"{abschnitt}.{feld}", e.ErrorMessage);
        }

        return messung;
    }

    private static string VorherNotiz(VorgangMessungRequest? vorher)
        => NormalisierteHerkunft(vorher?.Herkunft) switch
        {
            "Sensor" => "Vor dem Wasserwechsel — Werte vom Sensor übernommen.",
            "gemischt" => "Vor dem Wasserwechsel — teils vom Sensor, teils von Hand.",
            _ => "Vor dem Wasserwechsel.",
        };

    private static string NormalisierteHerkunft(string? herkunft)
        => herkunft?.Trim().ToLowerInvariant() switch
        {
            "sensor" => "Sensor",
            "gemischt" => "gemischt",
            _ => "Hand",
        };

    private WasserwechselVorgangDto AlsDto(WasserwechselVorgang v, IReadOnlyDictionary<int, Verbrauchsartikel> artikel, IReadOnlyDictionary<int, ChangeoutEntry> wechsel)
    {
        var buchungen = _vorgaenge.Buchungen(v.Id)
            .Select(b => new VorgangBuchungDto(b.Id, b.ArtikelId,
                artikel.TryGetValue(b.ArtikelId, out var a) ? a.Name : $"Artikel {b.ArtikelId}",
                artikel.TryGetValue(b.ArtikelId, out var e) ? e.Einheit : string.Empty,
                b.Menge))
            .ToList();
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

    /// <summary>Ein Zeitpunkt aus der Anfrage: ohne Kennzeichnung gilt Ortszeit.</summary>
    private static DateTime ZuUtc(DateTime wert) => wert.Kind switch
    {
        DateTimeKind.Utc => wert,
        DateTimeKind.Local => wert.ToUniversalTime(),
        _ => DateTime.SpecifyKind(wert, DateTimeKind.Local).ToUniversalTime(),
    };
}
