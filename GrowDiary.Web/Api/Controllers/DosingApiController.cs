using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>
/// Dosierpumpen: einrichten, kalibrieren, von Hand dosieren, Protokoll lesen.
/// </summary>
/// <remarks>
/// Stufe 1 — nichts läuft von allein. Jede Dosis wird hier ausgelöst, weil
/// jemand gedrückt hat. Die Automatik kommt erst, wenn Rechnung und Anschläge
/// sich an echten Zelten bewährt haben.
/// <para>Fork AI (A-003, 03.10.2026): Über einen Schlüssel sind Dosieren,
/// Kalibrierlauf und Stoppen „Geräte schalten". Pumpen einrichten ist
/// Verwaltung — die Einrichtung entscheidet über Automatik, Testbetrieb und die
/// Grenzen der Pumpe selbst. Das Kalibrier-Ergebnis festhalten ist
/// Dokumentieren. Dazu gilt der KI-Höchstwert je Befehl
/// (<see cref="KiHoechstwertGrund"/>).</para>
/// </remarks>
[ApiController]
[KiStufe(KiStufe.GeraeteSchalten)]
[Route("api/dosing")]
[Produces("application/json")]
public sealed class DosingApiController : ApiControllerBase
{
    private readonly GrowRepository _repository;
    private readonly DosingRepository _dosing;
    private readonly AlertRuleRepository _alertRules;
    private readonly DosingService _service;
    private readonly DosingContextBuilder _situations;
    private readonly HomeAssistantService _homeAssistant;

    public DosingApiController(
        GrowRepository repository,
        DosingRepository dosing,
        AlertRuleRepository alertRules,
        DosingService service,
        DosingContextBuilder situations,
        HomeAssistantService homeAssistant)
    {
        _repository = repository;
        _dosing = dosing;
        _alertRules = alertRules;
        _service = service;
        _situations = situations;
        _homeAssistant = homeAssistant;
    }

    /// <summary>
    /// Live-Zustaende des Zelts — daraus liest der Waechter die Umwaelzung.
    /// </summary>
    /// <remarks>
    /// Vor einer echten Dosis lohnt der frische Blick: eine stehende
    /// Umwaelzpumpe heisst Konzentrat an einer Stelle. Nicht erreichbar heisst
    /// unbekannt, und unbekannt blockt die Hand-Dosis nicht — wer selbst
    /// drueckt, steht daneben.
    /// </remarks>
    private async Task<IReadOnlyDictionary<string, HomeAssistantState>?> LiveStatesAsync(
        DosingPump pump, CancellationToken cancellationToken)
    {
        if (pump.SimulationMode) return null;

        try
        {
            var settings = _repository.GetEffectiveHomeAssistantSettings();
            var tent = _repository.GetTent(pump.TentId);
            if (!settings.IsConfigured || tent is null) return null;
            return await _homeAssistant.GetStatesAsync(settings, tent, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    // ---------- Pumpen ----------

    [HttpGet("pumps")]
    [ProducesResponseType(typeof(IReadOnlyList<DosingPumpDto>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<DosingPumpDto>> GetPumps([FromQuery] int? tentId)
        => Ok(_dosing.GetPumps(tentId).Select(ToDto).ToList());

    [HttpGet("pumps/{id:int}")]
    [ProducesResponseType(typeof(DosingPumpDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<DosingPumpDto> GetPump(int id)
    {
        var pump = _dosing.GetPump(id);
        return pump is null ? NotFoundError("pump_not_found", $"Pumpe {id} existiert nicht.") : Ok(ToDto(pump));
    }

    [HttpPost("pumps")]
    [KiStufe(KiStufe.Verwaltung)]
    [ProducesResponseType(typeof(DosingPumpDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public ActionResult<DosingPumpDto> Create([FromBody] DosingPumpUpsertRequest? request)
    {
        if (Validate(request) is { } error) return error;

        var pump = Apply(new DosingPump(), request!);
        if (ValidatePartner(pump) is { } paarFehler) return paarFehler;
        pump.TubeChangedAtUtc = DateTime.UtcNow;   // frisch eingerichtet = frischer Schlauch
        var id = _dosing.InsertPump(pump);
        return CreatedAtAction(nameof(GetPump), new { id }, ToDto(_dosing.GetPump(id)!));
    }

    [HttpPut("pumps/{id:int}")]
    [KiStufe(KiStufe.Verwaltung)]
    [ProducesResponseType(typeof(DosingPumpDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<DosingPumpDto> Update(int id, [FromBody] DosingPumpUpsertRequest? request)
    {
        var existing = _dosing.GetPump(id);
        if (existing is null) return NotFoundError("pump_not_found", $"Pumpe {id} existiert nicht.");
        if (Validate(request) is { } error) return error;

        var pump = Apply(existing, request!);
        if (ValidatePartner(pump) is { } paarFehler) return paarFehler;
        if (request!.TubeChangedNow) pump.TubeChangedAtUtc = DateTime.UtcNow;
        _dosing.UpdatePump(pump);
        return Ok(ToDto(_dosing.GetPump(id)!));
    }

    [HttpDelete("pumps/{id:int}")]
    [KiStufe(KiStufe.Verwaltung)]
    [KiSicherungVorher]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Delete(int id)
    {
        _dosing.DeletePump(id);
        return NoContent();
    }

    // ---------- Kalibrieren ----------

    /// <summary>
    /// Lässt die Pumpe für die angegebene Zeit laufen — Schlauchende im
    /// Messbecher. Was herauskommt, trägt der Nutzer danach ein.
    /// </summary>
    [HttpPost("pumps/{id:int}/calibration/run")]
    [ProducesResponseType(typeof(DoseResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DoseResultDto>> CalibrationRun(int id, [FromBody] CalibrationRunRequest request, CancellationToken cancellationToken)
    {
        var pump = _dosing.GetPump(id);
        if (pump is null) return NotFoundError("pump_not_found", $"Pumpe {id} existiert nicht.");

        // Zielmenge schlaegt feste Zeit, sobald eine grobe Foerdermenge bekannt
        // ist. Der Kalibrierlauf darf laenger als eine Dosis — er geht in den
        // Messbecher, nicht ins Becken.
        var gewuenscht = request.TargetMl is { } ziel
            ? DosingCalculator.SecondsForTarget(ziel, pump.MlPerMinute) ?? request.Seconds
            : request.Seconds;
        var seconds = Math.Clamp(gewuenscht, 5, DosingGuard.MaxCalibrationSeconds);

        // Fork AI (A-003, 03.10.2026): Auch ein Kalibrierlauf fördert — fünf
        // Minuten sind bei einer schnellen Pumpe mehr als jede Einzeldosis. Ob
        // das Schlauchende im Becher oder im Becken steckt, sieht über einen
        // Schlüssel niemand; deshalb gilt der KI-Höchstwert auch hier.
        if (KiZugriffKontext.Aus(HttpContext) is { } ki && KiKalibrierlaufGrund(ki, pump, seconds) is { } kiGrund)
        {
            _dosing.InsertEvent(new DoseEvent
            {
                PumpId = pump.Id,
                TentId = pump.TentId,
                OccurredAtUtc = DateTime.UtcNow,
                Trigger = DoseTrigger.Calibration,
                Outcome = DoseOutcome.Rejected,
                RequestedMl = 0,
                Reason = kiGrund,
                Simulated = pump.SimulationMode,
            });
            return KiHoechstwertAbgewiesen(kiGrund);
        }

        var lauf = await _service.RunForSecondsAsync(pump, seconds, cancellationToken, DosingGuard.MaxCalibrationSeconds);
        var ok = lauf == Pumpenlauf.Gelaufen;

        _dosing.InsertEvent(new DoseEvent
        {
            PumpId = pump.Id,
            TentId = pump.TentId,
            OccurredAtUtc = DateTime.UtcNow,
            Trigger = DoseTrigger.Calibration,
            Outcome = DosingService.Ausgang(lauf),
            RequestedMl = 0,
            // Vor der Kalibrierung ist die Fördermenge unbekannt — was hier
            // geflossen ist, weiss erst der Messbecher.
            DosedMl = 0,
            SecondsRun = ok ? seconds : 0,
            Reason = ok ? $"Kalibrierlauf {seconds:0.#} s" : "Kalibrierlauf: " + DosingService.Grund(lauf),
            Simulated = pump.SimulationMode,
        });

        return Ok(new DoseResultDto(ok, 0, ok ? seconds : 0,
            ok ? $"{seconds:0.#} s gelaufen — jetzt genau ablesen, was im Becher steht."
               : DosingService.Grund(lauf)));
    }

    /// <summary>Trägt ein, was im Becher stand, und rechnet die Fördermenge daraus.</summary>
    [HttpPost("pumps/{id:int}/calibration")]
    // Fork AI (A-003, Prüfer 03.10.2026): Verwaltung, nicht Dokumentieren. Das
    // Ergebnis setzt MlPerMinute — und darüber rechnet jede Dosisgrenze. Ein
    // Schlüssel, der nur dokumentieren darf, stellte die Pumpe auf 1 ml/min und
    // liess danach einen Kalibrierlauf von 300 s durch die 10-ml-Grenze: real
    // etwa 225 ml. Die Fördermenge ist Einrichtung der Pumpe wie Create/Update.
    [KiStufe(KiStufe.Verwaltung)]
    [ProducesResponseType(typeof(DosingPumpDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public ActionResult<DosingPumpDto> SaveCalibration(int id, [FromBody] CalibrationResultRequest request)
    {
        var pump = _dosing.GetPump(id);
        if (pump is null) return NotFoundError("pump_not_found", $"Pumpe {id} existiert nicht.");

        var mlPerMinute = DosingCalculator.MlPerMinuteFrom(request.MeasuredMl, request.Seconds);
        if (mlPerMinute is null)
        {
            return BadRequestError("invalid_calibration", "Menge und Laufzeit müssen beide größer als null sein.");
        }

        _dosing.SaveCalibration(id, mlPerMinute.Value, DateTime.UtcNow);
        return Ok(ToDto(_dosing.GetPump(id)!));
    }

    // ---------- Von Hand dosieren ----------

    [HttpPost("pumps/{id:int}/dose")]
    [ProducesResponseType(typeof(DoseResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DoseResultDto>> Dose(int id, [FromBody] ManualDoseRequest request, CancellationToken cancellationToken)
    {
        var pump = _dosing.GetPump(id);
        if (pump is null) return NotFoundError("pump_not_found", $"Pumpe {id} existiert nicht.");

        var nowUtc = DateTime.UtcNow;
        var context = _situations.Build(pump, nowUtc, await LiveStatesAsync(pump, cancellationToken)).Context;

        // Fork AI (A-003, 03.10.2026): Über einen Schlüssel zuerst der
        // KI-Höchstwert — zusätzlich zur Grenze der Pumpe, die DosingGuard
        // danach unverändert prüft. Abgewiesen wird ganz, nicht gedeckelt: wer
        // „12 ml" diktiert, soll nicht stillschweigend 10 bekommen.
        if (KiZugriffKontext.Aus(HttpContext) is { } ki && KiHoechstwertGrund(ki, pump, request.Ml) is { } kiGrund)
        {
            _dosing.InsertEvent(new DoseEvent
            {
                PumpId = pump.Id,
                TentId = pump.TentId,
                OccurredAtUtc = nowUtc,
                Trigger = DoseTrigger.Manual,
                Outcome = DoseOutcome.Rejected,
                RequestedMl = request.Ml,
                ValueBefore = context.Reading,
                Reason = kiGrund,
                Simulated = pump.SimulationMode,
            });
            return KiHoechstwertAbgewiesen(kiGrund);
        }

        var decision = DosingGuard.Evaluate(pump, request.Ml, context, nowUtc);

        if (!decision.Allowed)
        {
            // Auch das Nicht-Dosieren wird protokolliert — sonst raetselt man
            // spaeter, warum nichts passiert ist.
            _dosing.InsertEvent(new DoseEvent
            {
                PumpId = pump.Id,
                TentId = pump.TentId,
                OccurredAtUtc = nowUtc,
                Trigger = DoseTrigger.Manual,
                Outcome = DoseOutcome.Rejected,
                RequestedMl = request.Ml,
                ValueBefore = context.Reading,
                Reason = decision.Reason,
                Simulated = pump.SimulationMode,
            });
            return Ok(new DoseResultDto(false, 0, 0, decision.Reason));
        }

        var lauf = await _service.RunForSecondsAsync(pump, decision.Seconds, cancellationToken);
        var ok = lauf == Pumpenlauf.Gelaufen;
        var ereignisId = _dosing.InsertEvent(new DoseEvent
        {
            PumpId = pump.Id,
            TentId = pump.TentId,
            OccurredAtUtc = nowUtc,
            Trigger = DoseTrigger.Manual,
            Outcome = DosingService.Ausgang(lauf),
            RequestedMl = request.Ml,
            DosedMl = ok ? decision.Ml : 0,
            SecondsRun = ok ? decision.Seconds : 0,
            ValueBefore = context.Reading,
            Simulated = pump.SimulationMode,
            Reason = ok ? (pump.SimulationMode ? "Testbetrieb — es ist nichts geflossen." : "Von Hand ausgelöst.") : DosingService.Grund(lauf),
        });

        var partnerHinweis = ok ? PlanPartner(pump, decision.Ml, nowUtc, ereignisId) : null;

        return Ok(new DoseResultDto(ok, ok ? decision.Ml : 0, ok ? decision.Seconds : 0,
            ok ? $"{decision.Ml:0.##} ml gegeben." + (partnerHinweis ?? " Erst mischen, dann neu messen.")
               : DosingService.Grund(lauf)));
    }

    /// <summary>
    /// Fork AI (A-003, 03.10.2026): Warum diese Dosis über einen Schlüssel zu
    /// gross ist — oder null.
    /// </summary>
    /// <remarks>
    /// <para>Zwei Mengen zählen: die Dosis selbst und die zweite Hälfte, die sie
    /// bei einem Zweikomponenten-Paar nach sich zieht
    /// (<see cref="PartnerDosing.PartnerMl"/>). Bei einem Verhältnis über 1
    /// liefe sonst aus einem einzigen Befehl mehr nach, als der Betreiber dem
    /// Assistenten je Befehl erlaubt hat.</para>
    /// <para>Gerechnet wird mit der angefragten Menge, nicht mit der von
    /// <see cref="DosingGuard"/> gedeckelten: die Grenze der Pumpe ist eine
    /// andere Zusage als die des Assistenten.</para>
    /// </remarks>
    private string? KiHoechstwertGrund(KiZugriffKontext ki, DosingPump pump, double ml)
    {
        var hoechstens = ki.Hoechstwerte.MaxDosisMlJeBefehl;
        if (ml > hoechstens)
        {
            return $"KI-Höchstwert: {ml:0.##} ml angefragt, über den Assistenten sind je Befehl höchstens {hoechstens:0.##} ml erlaubt.";
        }

        if (PartnerDosing.PartnerMl(pump, ml) is { } partnerMl && partnerMl > hoechstens)
        {
            var partner = pump.PartnerPumpId is { } partnerId ? _dosing.GetPump(partnerId)?.Name : null;
            return $"KI-Höchstwert: {ml:0.##} ml zögen {partnerMl:0.##} ml aus {partner ?? "der Partnerpumpe"} nach, "
                   + $"über den Assistenten sind je Befehl höchstens {hoechstens:0.##} ml erlaubt.";
        }

        return null;
    }

    /// <summary>
    /// Fork AI (A-003, 03.10.2026): Warum dieser Kalibrierlauf über einen
    /// Schlüssel zu viel fördern kann — oder null.
    /// </summary>
    /// <remarks>
    /// Ohne Fördermenge lässt sich ein Lauf nicht in Millilitern begrenzen;
    /// dann läuft er über einen Schlüssel gar nicht. Den ersten Kalibrierlauf
    /// macht ohnehin ein Mensch: er hält den Messbecher.
    /// </remarks>
    private static string? KiKalibrierlaufGrund(KiZugriffKontext ki, DosingPump pump, double seconds)
    {
        var hoechstens = ki.Hoechstwerte.MaxDosisMlJeBefehl;
        if (pump.MlPerMinute is not { } rate || rate <= 0)
        {
            return $"KI-Höchstwert: {pump.Name} ist nicht kalibriert — ein Kalibrierlauf von {seconds:0.#} s lässt sich nicht "
                   + $"auf höchstens {hoechstens:0.##} ml begrenzen. Den ersten Kalibrierlauf startet ein Mensch am Messbecher.";
        }

        var erwartetMl = seconds * rate / 60.0;
        return erwartetMl > hoechstens
            ? $"KI-Höchstwert: ein Kalibrierlauf von {seconds:0.#} s fördert etwa {erwartetMl:0.##} ml, "
              + $"über den Assistenten sind je Befehl höchstens {hoechstens:0.##} ml erlaubt."
            : null;
    }

    /// <summary>Fork AI (A-003, 03.10.2026): 422 im gewohnten Fehlerformat, Code <c>ki_hoechstwert</c>.</summary>
    private ObjectResult KiHoechstwertAbgewiesen(string grund)
    {
        // Fork AI (A-003, 03.10.2026): fürs Prüfprotokoll — „Höchstwert erreicht" statt nur „422".
        KiZugriffSperre.FehlercodeMerken(HttpContext, "ki_hoechstwert");
        return StatusCode(
            StatusCodes.Status422UnprocessableEntity,
            ApiErrorFactory.Create("ki_hoechstwert", grund, StatusCodes.Status422UnprocessableEntity, traceId: HttpContext?.TraceIdentifier));
    }

    /// <summary>Alles, was fuer eines der beiden Pumpen des Paares noch aussteht.</summary>
    private List<PendingDose> PendingForPair(DosingPump pump)
    {
        var offen = _dosing.GetPendingForPump(pump.Id);
        if (pump.PartnerPumpId is { } partnerId)
        {
            offen.AddRange(_dosing.GetPendingForPump(partnerId));
        }
        return offen;
    }

    /// <summary>
    /// Die zweite Haelfte einplanen — sie laeuft spaeter, nicht jetzt.
    /// </summary>
    /// <remarks>
    /// Nicht sofort und nicht im selben Aufruf: A und B duerfen sich nicht
    /// konzentriert begegnen, und ein HTTP-Aufruf, der fuenf Minuten stehen
    /// bleibt, ist keine Loesung. Der Dosier-Worker holt sie ab. Die
    /// Einplanung selbst steht in <see cref="PartnerDosing.Einplanen"/> — die
    /// Automatik braucht dieselbe, und bis zum 01.10.2026 hatte sie keine.
    /// </remarks>
    private string? PlanPartner(DosingPump pump, double dosedMl, DateTime nowUtc, int ereignisId)
        => PartnerDosing.Einplanen(_dosing, pump, dosedMl, nowUtc, ereignisId) is { } haelfte
            ? $" {haelfte.Partner.Name} gibt in {haelfte.Minuten} min {haelfte.Ml:0.##} ml nach."
            : null;

    /// <summary>
    /// Sofort aus. Der wichtigste Knopf auf der ganzen Seite.
    /// </summary>
    /// <remarks>
    /// Fragt nichts und prüft nichts — Ausschalten darf nie an einer Bedingung
    /// scheitern. Läuft auch dann, wenn die Pumpe aus Sicht von Grow OS längst
    /// steht: dann kostet es einen wirkungslosen Aufruf, und das ist der
    /// richtige Preis.
    /// </remarks>
    [HttpPost("pumps/{id:int}/stop")]
    [KiOhneHoechstwert("Ausschalten darf nie an einer Grenze scheitern — auch nicht an der Stundengrenze für Schaltbefehle über einen Schlüssel (A-003).")]
    [ProducesResponseType(typeof(DoseResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DoseResultDto>> Stop(int id, CancellationToken cancellationToken)
    {
        var pump = _dosing.GetPump(id);
        if (pump is null) return NotFoundError("pump_not_found", $"Pumpe {id} existiert nicht.");

        var ok = await _service.TurnOffAsync(pump, cancellationToken);
        return Ok(new DoseResultDto(false, 0, 0,
            ok ? $"{pump.Name} ausgeschaltet." : $"{pump.Name} liess sich nicht schalten — in Home Assistant nachsehen."));
    }

    /// <summary>Nur rechnen, nicht dosieren — was würde jetzt herauskommen.</summary>
    /// <remarks>
    /// Stufe 2: Grow OS rechnet, der Mensch entscheidet. Der Vorschlag geht durch
    /// dieselben Anschläge wie eine echte Dosis, damit hier nie eine Menge steht,
    /// die beim Druck auf „Dosieren" abgelehnt würde.
    /// </remarks>
    [HttpGet("pumps/{id:int}/suggestion")]
    [ProducesResponseType(typeof(DoseSuggestionDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<DoseSuggestionDto>> Suggestion(int id, CancellationToken cancellationToken)
    {
        var pump = _dosing.GetPump(id);
        if (pump is null) return NotFoundError("pump_not_found", $"Pumpe {id} existiert nicht.");

        var nowUtc = DateTime.UtcNow;
        var situation = _situations.Build(pump, nowUtc, await LiveStatesAsync(pump, cancellationToken));
        var history = _dosing.GetEvents(pumpId: pump.Id, limit: 50);
        // Gelernt wird nur aus Dosen seit dem letzten Wasserwechsel — frisches
        // Wasser puffert anders, Dosen davor beschreiben ein anderes Becken.
        var gelernt = DosingCalculator.LearnedChangePerMl(history, situation.LearnSinceUtc);
        var gelerntAus = history.Count(dose =>
            dose.Outcome == DoseOutcome.Done && !dose.Simulated
            && dose.DosedMl > 0 && dose.ValueBefore is not null && dose.ValueAfter is not null
            && (situation.LearnSinceUtc is not { } schnitt || dose.OccurredAtUtc >= schnitt));

        DoseSuggestionDto Antwort(bool allowed, double ml, double seconds, string reason) => new(
            allowed, ml, seconds, reason,
            situation.Context.Reading,
            Bezeichnung(situation.ReadingFrom),
            situation.Context.ReadingAge is { } alter ? (int)alter.TotalMinutes : null,
            situation.Target,
            Bezeichnung(situation.TargetFrom),
            gelernt,
            gelerntAus);

        if (situation.Context.Reading is not { } ist)
        {
            return Ok(Antwort(false, 0, 0,
                "Kein Messwert für diese Pumpe — weder vom Sensor noch von Hand eingetragen."));
        }

        if (situation.Target is not { } ziel)
        {
            return Ok(Antwort(false, 0, 0,
                "Kein Zielwert: trag einen Grenzwert für das Zelt ein oder leg dem Grow ein Sollwert-Profil zu."));
        }

        var ml = DosingCalculator.MlToReach(ist, ziel, gelernt);
        if (ml is null)
        {
            return Ok(Antwort(false, 0, 0, gelernt is null
                ? "Noch keine Erfahrung — die ersten Dosen gibst du von Hand, danach rechnet Grow OS."
                : "Nichts zu tun: der Wert liegt schon richtig, oder diese Pumpe wirkt andersherum."));
        }

        // Halb leeres Becken, halbe Dosis: die gelernte Wirkung stammt aus dem
        // vollen Becken, in weniger Wasser wirkt dieselbe Menge staerker.
        var skaliert = Math.Round(ml.Value * situation.VolumeFactor, 2);

        var decision = DosingGuard.Evaluate(pump, skaliert, situation.Context, nowUtc);
        var begruendung = situation.VolumeFactor < 0.95
            ? $"{decision.Reason} Becken ist zu {situation.VolumeFactor:P0} voll — Dosis entsprechend verkleinert."
            : decision.Reason;
        return Ok(Antwort(decision.Allowed, decision.Ml, decision.Seconds, begruendung));
    }

    private static string Bezeichnung(ReadingSource source) => source switch
    {
        ReadingSource.Sensor => "sensor",
        ReadingSource.Manual => "manual",
        _ => "none",
    };

    private static string Bezeichnung(TargetSource source) => source switch
    {
        TargetSource.User => "user",
        TargetSource.Profile => "profile",
        _ => "none",
    };

    // ---------- Protokoll ----------

    [HttpGet("log")]
    [ProducesResponseType(typeof(IReadOnlyList<DoseEventDto>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<DoseEventDto>> Log([FromQuery] int? pumpId, [FromQuery] int? tentId, [FromQuery] int limit = 50)
    {
        var namen = _dosing.GetPumps().ToDictionary(pump => pump.Id, pump => pump.Name);
        var events = _dosing.GetEvents(pumpId, tentId, Math.Clamp(limit, 1, 500));
        return Ok(events.Select(dose => new DoseEventDto(
            dose.Id, dose.PumpId, namen.GetValueOrDefault(dose.PumpId, "—"),
            dose.OccurredAtUtc, dose.Trigger.ToString(), dose.Outcome.ToString(),
            dose.RequestedMl, dose.DosedMl, dose.SecondsRun,
            dose.ValueBefore, dose.ValueAfter, dose.TargetValue, dose.Reason, dose.Simulated)).ToList());
    }

    // ---------- Innenleben ----------

    /// <summary>
    /// Ein falsch eingerichtetes Paar darf gar nicht erst entstehen.
    /// </summary>
    /// <remarks>
    /// Zwei Zelte, ein Paar: B liefe in ein anderes Becken als A, und im ersten
    /// staende A allein. Das faellt erst auf, wenn die Pflanzen es zeigen.
    /// </remarks>
    private ActionResult? ValidatePartner(DosingPump pump)
    {
        var partner = pump.PartnerPumpId is { } id ? _dosing.GetPump(id) : null;
        return PartnerDosing.Validate(pump, partner) is { } fehler
            ? BadRequestError("invalid_partner", fehler)
            : null;
    }

    private ActionResult? Validate(DosingPumpUpsertRequest? request)
    {
        // Ein unlesbarer Rumpf kommt hier als null an. Ohne diesen Riegel wird
        // daraus ein 500 — ein Serverfehler fuer einen Fehler des Aufrufers.
        if (request is null)
            return BadRequestError("invalid_body", "Der Anfrage-Rumpf ist leer oder unlesbar.");
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequestError("name_required", "Die Pumpe braucht einen Namen.");
        if (!request.SimulationMode && string.IsNullOrWhiteSpace(request.HaEntityId))
            return BadRequestError("entity_required", "Ohne Home-Assistant-Entität lässt sich nichts schalten — oder schalte den Testbetrieb ein.");
        if (_repository.GetTent(request.TentId) is null)
            return BadRequestError("tent_not_found", $"Zelt {request.TentId} existiert nicht.");
        if (request.MaxSingleDoseMl is <= 0)
            return BadRequestError("invalid_limit", "Die größte Einzeldosis muss über null liegen.");
        return null;
    }

    /// <summary>
    /// Eine Sicherung uebernehmen — oder, wenn sie unter dem Erlaubten liegt,
    /// den Standard statt der Kante.
    /// </summary>
    /// <remarks>
    /// <b>Warum nicht <c>Math.Max</c>.</b> Eine 0 aus einem geleerten Feld ist
    /// keine Absicht, sondern eine fehlende Angabe. Sie auf das Minimum zu
    /// heben, ergaebe „1 Minute Mischpause" — auch das ist keine Pause. Wer
    /// nichts angibt, bekommt den Wert, der ohne ihn gegolten haette.
    /// </remarks>
    private static double SperreOderStandard(double? gewuenscht, double minimum, double bisher, double standard)
    {
        if (gewuenscht is not { } wert) return bisher;
        return wert < minimum ? standard : wert;
    }

    private static double SperreOderStandard(int? gewuenscht, double minimum, double bisher, double standard)
        => SperreOderStandard((double?)gewuenscht, minimum, bisher, standard);
    private static DosingPump Apply(DosingPump pump, DosingPumpUpsertRequest request)
    {
        pump.TentId = request.TentId;
        pump.Name = request.Name.Trim();
        pump.Purpose = Enum.TryParse<DosingPurpose>(request.Purpose, ignoreCase: true, out var purpose) ? purpose : DosingPurpose.Custom;
        pump.Agent = string.IsNullOrWhiteSpace(request.Agent) ? null : request.Agent.Trim();
        pump.ConcentrationPercent = request.ConcentrationPercent;
        pump.CostPerLiterEur = request.CostPerLiterEur is > 0 ? request.CostPerLiterEur : null;
        pump.HaEntityId = request.HaEntityId.Trim();
        pump.CalibrationIntervalDays = request.CalibrationIntervalDays;
        pump.TubeIntervalDays = request.TubeIntervalDays;
        // Sperren, die auf 0 stehen, sind keine Sperren.
        //
        // Eine 0 kommt hier nicht aus Absicht, sondern aus einem geleerten Feld:
        // `Number("")` ist im Browser 0 und `Number.isFinite(0)` ist true, also
        // ging die Null als gueltiger Wert durch. Beim Mindestabstand hiess das
        // stumme Katastrophe: `DosingService` prueft
        // `seit < TimeSpan.FromMinutes(MinIntervalMinutes)`, und bei 0 ist das nie
        // wahr — die Pumpe haette ohne jede Mischpause dosiert, mit
        // Erfolgsmeldung. Die Oberflaeche schickt seit dem gleichen Stand keine
        // 0 mehr; diese Sperre haelt sie trotzdem, weil die Schnittstelle auch
        // von aussen erreichbar ist.
        //
        // Unter dem Erlaubten gilt der STANDARD, nicht die Kante — dasselbe
        // Muster wie bei der Kuehler-Steuerung.
        pump.MaxSingleDoseMl = SperreOderStandard(request.MaxSingleDoseMl, 0.1, pump.MaxSingleDoseMl, 5);
        pump.MinIntervalMinutes = (int)SperreOderStandard(request.MinIntervalMinutes, 1, pump.MinIntervalMinutes, 18);
        pump.MaxDosesPerDay = (int)SperreOderStandard(request.MaxDosesPerDay, 1, pump.MaxDosesPerDay, 6);
        pump.MaxMlPerDay = SperreOderStandard(request.MaxMlPerDay, 0.1, pump.MaxMlPerDay, 25);
        pump.MaxReadingAgeMinutes = (int)SperreOderStandard(request.MaxReadingAgeMinutes, 1, pump.MaxReadingAgeMinutes, 10);
        pump.AutomationEnabled = request.AutomationEnabled;
        pump.HasHomeAssistantAutoOff = request.HasHomeAssistantAutoOff;
        pump.SimulationMode = request.SimulationMode;
        pump.PartnerPumpId = request.PartnerPumpId is > 0 ? request.PartnerPumpId : null;
        if (request.PartnerRatio is { } ratio) pump.PartnerRatio = ratio;
        if (request.PartnerDelayMinutes is { } delay) pump.PartnerDelayMinutes = delay;
        return pump;
    }

    private DosingPumpDto ToDto(DosingPump pump)
    {
        var history = _dosing.GetEvents(pumpId: pump.Id, limit: 50);

        var nowUtc = DateTime.UtcNow;
        var situation = _situations.Build(pump, nowUtc);
        var auswertbar = history.Count(dose =>
            dose.Outcome == DoseOutcome.Done && !dose.Simulated
            && dose.DosedMl > 0 && dose.ValueBefore is not null && dose.ValueAfter is not null
            && (situation.LearnSinceUtc is not { } schnitt || dose.OccurredAtUtc >= schnitt));

        var decision = DosingGuard.Evaluate(pump, 0.1, situation.Context, nowUtc);

        return new DosingPumpDto(
            pump.Id, pump.TentId, pump.Name, pump.Purpose.ToString(), pump.Agent, pump.ConcentrationPercent,
            pump.HaEntityId, pump.MlPerMinute, pump.CostPerLiterEur, pump.CalibratedAtUtc, pump.TubeChangedAtUtc,
            pump.CalibrationIntervalDays, pump.TubeIntervalDays,
            pump.MaxSingleDoseMl, pump.MinIntervalMinutes, pump.MaxDosesPerDay, pump.MaxMlPerDay,
            pump.MaxReadingAgeMinutes, pump.AutomationEnabled, pump.HasHomeAssistantAutoOff, pump.SimulationMode,
            pump.MetricKey,
            DosingCalculator.LearnedChangePerMl(history, situation.LearnSinceUtc),
            auswertbar,
            decision.Allowed ? null : decision.Reason,
            pump.PartnerPumpId, pump.PartnerRatio, pump.PartnerDelayMinutes,
            PendingForPair(pump).Count > 0);
    }
}
