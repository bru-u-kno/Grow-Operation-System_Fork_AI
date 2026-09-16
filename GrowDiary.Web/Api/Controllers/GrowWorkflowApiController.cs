using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Mapping;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

[ApiController]
[Route("api/grows")]
[Produces("application/json")]
public sealed class GrowWorkflowApiController : ApiControllerBase
{
    private readonly GrowRepository _repository;
    private readonly HarvestRepository _harvestRepository;
    private readonly JournalRepository _journalRepository;
    private readonly AuditRepository _auditRepository;
    private readonly TargetValueService _targetValueService;
    // Fork AI (forkai.107): fuer die Wochenspalte im Addback-Vorschlag.
    private readonly Services.Knowledge.KnowledgeBaseLoader _wissen;

    public GrowWorkflowApiController(
        GrowRepository repository,
        HarvestRepository harvestRepository,
        JournalRepository journalRepository,
        AuditRepository auditRepository,
        TargetValueService targetValueService,
        Services.Knowledge.KnowledgeBaseLoader wissen,
        WasserwechselStandService wasserwechselStand,
        WaterProfileStore? waterProfile = null,
        Services.GrowPlan.GrowPlanService? plaene = null)
    {
        _plaene = plaene;
        _repository = repository;
        _harvestRepository = harvestRepository;
        _journalRepository = journalRepository;
        _auditRepository = auditRepository;
        _targetValueService = targetValueService;
        _wissen = wissen;
        _wasserwechselStand = wasserwechselStand;
        _waterProfile = waterProfile;
    }

    private readonly WasserwechselStandService _wasserwechselStand;

    private readonly WaterProfileStore? _waterProfile;

    // Fork AI (Grow-Plan): die Ernte schließt den Grow ab — dann wird sein Plan eingefroren.
    private readonly Services.GrowPlan.GrowPlanService? _plaene;

    /// <summary>
    /// Womit der Nutzer angesetzt hat — angegeben, sonst aus dem Grow und dem
    /// Wasserprofil erschlossen.
    /// </summary>
    /// <remarks>
    /// <para>Erschlossen heisst NICHT behauptet: der Vorschlag stammt aus der
    /// Wasserquelle des Grows und dem gespeicherten Profil. Wer einmal anders
    /// auffuellt, ueberschreibt ihn im Formular — genau darum ging es dem
    /// Tester, dessen Osmose-Tank auch mal leer ist.</para>
    ///
    /// <para>Die Einheit wechselt hier bewusst: das Profil haelt µS/cm (so
    /// steht es im Stadtbericht), der Vorgang mS/cm (so misst das Handgeraet
    /// am Becken). 1 mS/cm = 1000 µS/cm.</para>
    /// </remarks>
    private (WaterSource? Quelle, double? EcMsCm) WasserFuer(GrowRun grow, WaterSource? angegeben, double? ecAngegeben)
    {
        var quelle = angegeben ?? grow.WaterSource;
        if (ecAngegeben.HasValue)
        {
            return (quelle, ecAngegeben);
        }

        var profil = _waterProfile?.Get();
        if (profil is null)
        {
            return (quelle, null);
        }

        // Bei Osmose zaehlt der Wert NACH der Anlage, sonst der aus dem Bericht.
        var mikroSiemens = quelle == WaterSource.RO
            ? profil.TreatedConductivityUsCm
            : profil.TreatedConductivityUsCm ?? profil.ConductivityUsCm;

        return (quelle, mikroSiemens is { } us ? Math.Round(us / 1000, 3) : null);
    }

    [HttpGet("{id:int}/addback")]
    [ProducesResponseType(typeof(AddbackDefaultsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<AddbackDefaultsDto> AddbackDefaults(int id)
    {
        var grow = _repository.GetGrow(id);
        if (grow is null)
        {
            return NotFoundError("grow_not_found", $"Grow mit Id {id} existiert nicht.");
        }

        var measurements = _repository.GetMeasurementsForGrow(id);
        var latestEc = measurements
            .OrderByDescending(measurement => measurement.TakenAt)
            .Where(measurement => measurement.ReservoirEc.HasValue)
            .Select(measurement => measurement.ReservoirEc)
            .FirstOrDefault();
        // Die Phase kommt aus dem Grow, nicht aus der letzten Messung — sonst
        // widersprechen die Zielbaender hier der Kopfzeile daneben, die schon
        // immer GrowStageResolver fragt (GrowMapping.CurrentStage).
        var stage = GrowStageResolver.Resolve(grow, DateTime.Today);
        // Die Profil-Kette Grow -> System -> Anbaustil, nicht die Abkuerzung.
        //
        // `GetTargets(HydroStyle, stage)` landet immer beim Standardprofil und
        // uebergeht damit das eigene Profil des Nutzers. Genau dieser Fehler
        // stand in der Diagnose und hat dort EC 0,6-0,8 gemeldet, waehrend die
        // Live-Kachel fuer denselben Grow 0,9-1,1 sagte.
        // Fork AI (forkai.107): ueber Zielband.FuerGrow statt nur ueber das
        // Phasenprofil. Der Mischplan daneben rechnet mit der Wochenspalte;
        // schlug der Addback die Mitte des Phasenbands vor, standen auf einem
        // Bildschirm zwei EC-Ziele.
        var targets = Zielband.FuerGrow(
            _targetValueService,
            _wissen,
            grow,
            stage,
            grow.SystemId is { } systemId ? _repository.GetSystem(systemId)?.SetpointProfileId : null,
            null);
        double? suggestedEcTarget = targets is null
            ? null
            : Math.Round((targets.EcMin + targets.EcMax) / 2, 2);
        var suggestedReservoir = ResolveAddbackReservoirLiters(grow);

        return Ok(new AddbackDefaultsDto(
            id,
            grow.Name,
            suggestedReservoir,
            latestEc,
            suggestedEcTarget,
            suggestedReservoir,
            latestEc,
            suggestedEcTarget,
            3.0));
    }

    [HttpPost("{id:int}/addback/calculate")]
    [ProducesResponseType(typeof(AddbackResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<AddbackResultDto> CalculateAddback(int id, [FromBody] AddbackCalculateRequest request)
    {
        var grow = _repository.GetGrow(id);
        if (grow is null)
        {
            return NotFoundError("grow_not_found", $"Grow mit Id {id} existiert nicht.");
        }

        if (!request.EcIst.HasValue)
        {
            ModelState.AddModelError(nameof(request.EcIst), "Ist-EC ist erforderlich.");
        }

        if (!request.EcZiel.HasValue)
        {
            ModelState.AddModelError(nameof(request.EcZiel), "Ziel-EC ist erforderlich.");
        }

        if (!request.EcStock.HasValue)
        {
            ModelState.AddModelError(nameof(request.EcStock), "Addback-EC ist erforderlich.");
        }

        var reservoirLiters = request.ReservoirLiters ?? ResolveAddbackReservoirLiters(grow);
        if (!reservoirLiters.HasValue)
        {
            ModelState.AddModelError(nameof(request.ReservoirLiters), "Reservoir-Volumen konnte nicht aus dem HydroSetup oder Legacy-Grow gelesen werden.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var result = AddbackCalculator.Calculate(
            reservoirLiters!.Value,
            request.EcIst!.Value,
            request.EcZiel!.Value,
            request.EcStock!.Value);

        return Ok(result.ToDto());
    }

    [HttpGet("{id:int}/addback/logs")]
    [ProducesResponseType(typeof(IReadOnlyList<AddbackLogDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<IReadOnlyList<AddbackLogDto>> GetAddbackLogs(int id)
    {
        if (_repository.GetGrow(id) is null)
        {
            return NotFoundError("grow_not_found", $"Grow mit Id {id} existiert nicht.");
        }

        return Ok(_repository.GetAddbackLogsForGrow(id).Select(entry => entry.ToDto()).ToList());
    }

    [HttpPost("{id:int}/addback/logs")]
    [ProducesResponseType(typeof(AddbackLogDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<AddbackLogDto> CreateAddbackLog(int id, [FromBody] CreateAddbackLogRequest request)
    {
        var grow = _repository.GetGrow(id);
        if (grow is null)
        {
            return NotFoundError("grow_not_found", $"Grow mit Id {id} existiert nicht.");
        }

        ValidateOperationLogValues(
            (request.ReservoirLiters, nameof(request.ReservoirLiters)),
            (request.EcBefore, nameof(request.EcBefore)),
            (request.EcTarget, nameof(request.EcTarget)),
            (request.EcStock, nameof(request.EcStock)),
            (request.EcAfter, nameof(request.EcAfter)),
            (request.LitersAdded, nameof(request.LitersAdded)),
            (request.NewReservoirVolumeLiters, nameof(request.NewReservoirVolumeLiters)));
        ValidatePh(request.PhBefore, nameof(request.PhBefore));
        ValidatePh(request.PhAfter, nameof(request.PhAfter));

        if (!Enum.IsDefined(request.Kind))
        {
            ModelState.AddModelError(nameof(request.Kind), "Addback-Art ist ungueltig.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var resolvedReservoir = request.ReservoirLiters ?? ResolveAddbackReservoirLiters(grow);
        var usedHydroVolume = request.UsedHydroSetupVolume
            ?? (!request.ReservoirLiters.HasValue && grow.SystemId.HasValue && CalculateHydroSetupTotalVolumeLiters(_repository.GetHydroSetup(grow.SystemId.Value)).HasValue);

        var wasser = WasserFuer(grow, request.WaterUsed, request.WaterEcMsCm);

        var created = _repository.CreateAddbackLog(new AddbackLogEntry
        {
            GrowId = id,
            HydroSetupId = grow.SystemId,
            Kind = request.Kind,
            PerformedAtUtc = request.PerformedAtUtc ?? DateTime.UtcNow,
            ReservoirLiters = resolvedReservoir,
            EcBefore = request.EcBefore,
            EcTarget = request.EcTarget,
            EcStock = request.EcStock,
            EcAfter = request.EcAfter,
            PhBefore = request.PhBefore,
            PhAfter = request.PhAfter,
            LitersAdded = request.LitersAdded,
            NewReservoirVolumeLiters = request.NewReservoirVolumeLiters,
            UsedHydroSetupVolume = usedHydroVolume,
            WaterUsed = wasser.Quelle,
            WaterEcMsCm = wasser.EcMsCm,
            Notes = request.Notes
        });

        return CreatedAtAction(nameof(GetAddbackLogs), new { id }, created.ToDto());
    }

    [HttpGet("{id:int}/changeouts")]
    [ProducesResponseType(typeof(IReadOnlyList<ChangeoutDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<IReadOnlyList<ChangeoutDto>> GetChangeouts(int id)
    {
        if (_repository.GetGrow(id) is null)
        {
            return NotFoundError("grow_not_found", $"Grow mit Id {id} existiert nicht.");
        }

        return Ok(_repository.GetChangeoutsForGrow(id).Select(entry => entry.ToDto()).ToList());
    }

    /// <summary>
    /// Wie es um den Wasserwechsel steht — die Zahl, die auf der Seite gross
    /// dasteht und in jeder Mahnung wieder vorkommt.
    /// </summary>
    /// <remarks>
    /// Gerechnet wird sie nicht hier, sondern in
    /// <see cref="WasserwechselStandService"/> — aus derselben Quelle, aus der
    /// die Mahnung kommt. Sonst stuenden auf der Seite und in der Aufgabe zwei
    /// verschiedene Zahlen fuer denselben Grow.
    /// </remarks>
    [HttpGet("{id:int}/changeouts/stand")]
    [ProducesResponseType(typeof(WasserwechselStand), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<WasserwechselStand> GetChangeoutStand(int id)
        => _wasserwechselStand.Fuer(id) is { } stand
            ? Ok(stand)
            : NotFoundError("grow_not_found", $"Grow mit Id {id} existiert nicht.");

    [HttpPost("{id:int}/changeouts")]
    [ProducesResponseType(typeof(ChangeoutDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<ChangeoutDto> CreateChangeout(int id, [FromBody] CreateChangeoutRequest request)
    {
        var grow = _repository.GetGrow(id);
        if (grow is null)
        {
            return NotFoundError("grow_not_found", $"Grow mit Id {id} existiert nicht.");
        }

        ValidateOperationLogValues(
            (request.VolumeChangedLiters, nameof(request.VolumeChangedLiters)),
            (request.PercentChanged, nameof(request.PercentChanged)),
            (request.EcBefore, nameof(request.EcBefore)),
            (request.EcAfter, nameof(request.EcAfter)));
        ValidatePh(request.PhBefore, nameof(request.PhBefore));
        ValidatePh(request.PhAfter, nameof(request.PhAfter));

        if (request.PercentChanged is < 0 or > 100)
        {
            ModelState.AddModelError(nameof(request.PercentChanged), "Prozentwert muss zwischen 0 und 100 liegen.");
        }

        // Ein Teilwechsel ohne Menge sagt nichts.
        //
        // Bis zum 31.08.2026 liess sich das Formular vollstaendig LEER
        // abschicken, und das war folgenlos: die Mahnung „Woechentlicher
        // Wasserwechsel" las diese Tabelle ohnehin nicht. Seit sie es tut,
        // legt ein leerer Eintrag die Mahnung fuer eine Woche stumm — ein
        // Fehlgriff waere damit schlimmer als gar kein Eintrag.
        //
        // Verlangt wird genau EINE Zahl, und nur beim Teilwechsel: der Anteil
        // oder die Menge. Der Komplettwechsel traegt seine Auskunft im Namen.
        /* Ein Wechsel in der Zukunft ist keine Erfassung, sondern ein Plan.
           Das Formular verbietet es (`max` am Datumsfeld), der Server nahm es
           an — und der Testbestand hat prompt einen erzeugt. Wer nur ueber die
           API kommt, umging die Sperre. Eine Stunde Luft fuer Uhren, die
           auseinanderlaufen. Gefunden vom Pruefer. */
        if (request.PerformedAtUtc is { } zeitpunkt && zeitpunkt > DateTime.UtcNow.AddHours(1))
        {
            ModelState.AddModelError(nameof(request.PerformedAtUtc),
                "Der Zeitpunkt liegt in der Zukunft. Ein Wasserwechsel wird erfasst, nachdem er war.");
        }

        if (request.Kind == ChangeoutKind.Partial
            && request.PercentChanged is null
            && request.VolumeChangedLiters is null)
        {
            ModelState.AddModelError(nameof(request.PercentChanged),
                "Beim Teilwechsel fehlt die Menge: trag den Anteil in Prozent oder die Liter ein.");
        }

        if (!Enum.IsDefined(request.Kind))
        {
            ModelState.AddModelError(nameof(request.Kind), "Changeout-Art ist ungueltig.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var wasser = WasserFuer(grow, request.WaterUsed, request.WaterEcMsCm);

        var created = _repository.CreateChangeout(new ChangeoutEntry
        {
            GrowId = id,
            HydroSetupId = grow.SystemId,
            Kind = request.Kind,
            PerformedAtUtc = request.PerformedAtUtc ?? DateTime.UtcNow,
            VolumeChangedLiters = request.VolumeChangedLiters,
            PercentChanged = request.PercentChanged,
            EcBefore = request.EcBefore,
            EcAfter = request.EcAfter,
            PhBefore = request.PhBefore,
            PhAfter = request.PhAfter,
            WaterUsed = wasser.Quelle,
            WaterEcMsCm = wasser.EcMsCm,
            Notes = request.Notes
        });

        return CreatedAtAction(nameof(GetChangeouts), new { id }, created.ToDto());
    }

    /// <summary>Einen falsch eingetragenen Wasserwechsel entfernen.</summary>
    /// <remarks>
    /// Es gab keinen Weg zurueck. Solange die Mahnung diese Tabelle nicht las,
    /// war das folgenlos; seit dem 31.08.2026 legt ein Fehleintrag sie fuer
    /// eine Woche still — dann muss er sich zuruecknehmen lassen.
    /// </remarks>
    [HttpDelete("{id:int}/changeouts/{changeoutId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public IActionResult DeleteChangeout(int id, int changeoutId)
    {
        if (_repository.GetGrow(id) is null)
        {
            return NotFoundError("grow_not_found", $"Grow mit Id {id} existiert nicht.");
        }

        return _repository.DeleteChangeout(id, changeoutId)
            ? NoContent()
            : NotFoundError("changeout_not_found",
                $"Zu diesem Grow gibt es keinen Wasserwechsel mit Id {changeoutId}.");
    }

    [HttpGet("{id:int}/harvest")]
    [ProducesResponseType(typeof(HarvestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<HarvestDto> Harvest(int id)
    {
        var grow = _repository.GetGrow(id);
        if (grow is null)
        {
            return NotFoundError("grow_not_found", $"Grow mit Id {id} existiert nicht.");
        }

        var harvest = _harvestRepository.GetForGrow(id);
        return Ok(harvest is null
            ? GrowWorkflowMapping.CreateDefaultHarvestDto(id, grow.Name)
            : harvest.ToDto(grow.Name));
    }

    [HttpPut("{id:int}/harvest")]
    [ProducesResponseType(typeof(HarvestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<HarvestDto> SaveHarvest(int id, [FromBody] HarvestUpsertRequest request)
    {
        var grow = _repository.GetGrow(id);
        if (grow is null)
        {
            return NotFoundError("grow_not_found", $"Grow mit Id {id} existiert nicht.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        HarvestEntry entry;
        try
        {
            entry = request.ToEntry(id);
        }
        catch
        {
            ModelState.AddModelError(nameof(request.HarvestedAtLocal), "Erntedatum konnte nicht gelesen werden.");
            return ValidationError();
        }

        var existing = _harvestRepository.GetForGrow(id);
        if (existing is null)
        {
            _harvestRepository.Create(entry);
            if (grow.Status == GrowStatus.Running)
            {
                grow.Status = GrowStatus.Completed;
                grow.EndDate = entry.HarvestedAt.Date;
                _repository.UpdateGrow(grow);
                _plaene?.Abgleichen(grow);
            }

            _auditRepository.LogHarvestCreated(id, request.HarvestedAtLocal);
        }
        else
        {
            entry.Id = existing.Id;
            entry.CreatedAtUtc = existing.CreatedAtUtc;
            _harvestRepository.Update(entry);
        }

        return Ok(_harvestRepository.GetForGrow(id)!.ToDto(grow.Name));
    }

    [HttpPost("{id:int}/actions/confirm-germination")]
    [ProducesResponseType(typeof(GrowActionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<GrowActionResultDto> ConfirmGermination(int id)
    {
        var grow = _repository.GetGrow(id);
        if (grow is null)
        {
            return NotFoundError("grow_not_found", $"Grow mit Id {id} existiert nicht.");
        }

        if (grow.StartMaterial != StartMaterial.Seed)
        {
            return BadRequestError("invalid_action", "Keimungsbestaetigung ist nur fuer Samen-Grows moeglich.");
        }

        if (!grow.GerminatedAt.HasValue)
        {
            grow.GerminatedAt = DateTime.Now;
            if (grow.Status == GrowStatus.Planning)
            {
                grow.Status = GrowStatus.Running;
            }

            _repository.UpdateGrow(grow);
            _journalRepository.Create(new JournalEntry
            {
                GrowId = id,
                EntryType = JournalEntryType.GerminationConfirmed,
                Body = "Keimung bestaetigt.",
                OccurredAtUtc = DateTime.UtcNow
            });
        }

        return Ok(new GrowActionResultDto(_repository.GetGrow(id)!.ToDetailDto(), "Keimung bestaetigt."));
    }

    /// <summary>
    /// Der Saemling ist durch — ab hier Veg.
    /// </summary>
    /// <remarks>
    /// Bewusst ein Knopf und keine Rechnung: der Uebergang haengt am Aussehen,
    /// nicht am Kalender. Echte gezackte Blaetter statt der zwei runden
    /// Keimblaetter, dickerer Stengel, regelmaessig neue Blattpaare,
    /// Seitentriebe an den Knoten, spuerbar mehr Wasserverbrauch — das sieht
    /// nur, wer davorsteht. Typisch ein bis drei Wochen nach der Keimung, aber
    /// eben typisch.
    ///
    /// Bis hierhin schaetzt <see cref="GrowStageResolver"/> ueber die Tage.
    /// Danach zaehlt dieses Datum, und die Zielwerte springen auf Veg.
    /// </remarks>
    /// <summary>
    /// Finish beginnt — am Trichom entschieden, nicht am Kalender.
    /// </summary>
    /// <remarks>
    /// Real schaut man mit der Lupe: ueberwiegend milchige Trichome, erste
    /// bernsteinfarbene — dann wird gespuelt. Die Breeder-Wochen sind nur die
    /// Schaetzung, bis jemand hingesehen hat. Ab dem Druck gelten die
    /// Finish-Ziele (weniger EC, kuehler, trockener).
    /// </remarks>
    [HttpPost("{id:int}/actions/confirm-finish")]
    [ProducesResponseType(typeof(GrowActionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<GrowActionResultDto> ConfirmFinish(int id)
    {
        var grow = _repository.GetGrow(id);
        if (grow is null)
        {
            return NotFoundError("grow_not_found", $"Grow mit Id {id} existiert nicht.");
        }

        // Die Phase sagt der Resolver — dieselbe Quelle wie Kacheln und Ziele.
        // Eine Nebenrechnung hier draussen waere die naechste Stelle, die
        // irgendwann widerspricht. (Der erste Wurf prüfte nur „Autoflower?" —
        // damit waere eine drei Tage alte Auto schon finish-faehig gewesen.)
        var stage = GrowStageResolver.Resolve(grow, DateTime.Today);
        if (stage is not (GrowStage.Transition or GrowStage.Flower or GrowStage.Finish))
        {
            return BadRequestError("invalid_action", "Finish gibt es erst in der Bluete.");
        }

        if (!grow.FinishStartedAt.HasValue)
        {
            grow.FinishStartedAt = DateTime.Now;
            _repository.UpdateGrow(grow);
            _journalRepository.Create(new JournalEntry
            {
                GrowId = id,
                EntryType = JournalEntryType.FinishStarted,
                Body = "Trichome sind so weit — Finish beginnt, es wird gespuelt.",
                OccurredAtUtc = DateTime.UtcNow
            });
        }

        return Ok(new GrowActionResultDto(_repository.GetGrow(id)!.ToDetailDto(), "Finish festgehalten."));
    }

    [HttpPost("{id:int}/actions/confirm-veg")]
    [ProducesResponseType(typeof(GrowActionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<GrowActionResultDto> ConfirmVeg(int id)
    {
        var grow = _repository.GetGrow(id);
        if (grow is null)
        {
            return NotFoundError("grow_not_found", $"Grow mit Id {id} existiert nicht.");
        }

        if (grow.FlipDate.HasValue)
        {
            return BadRequestError("invalid_action", "Dieser Grow ist bereits in der Bluete.");
        }

        if (!grow.VegStartedAt.HasValue)
        {
            grow.VegStartedAt = DateTime.Now;
            if (grow.Status == GrowStatus.Planning)
            {
                grow.Status = GrowStatus.Running;
            }

            _repository.UpdateGrow(grow);
            _journalRepository.Create(new JournalEntry
            {
                GrowId = id,
                EntryType = JournalEntryType.VegStarted,
                Body = "Saemling vorbei — echte Blaetter da, ab hier Veg.",
                OccurredAtUtc = DateTime.UtcNow
            });
        }

        return Ok(new GrowActionResultDto(_repository.GetGrow(id)!.ToDetailDto(), "Veg-Phase festgehalten."));
    }

    [HttpPost("{id:int}/actions/confirm-rooting")]
    [ProducesResponseType(typeof(GrowActionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<GrowActionResultDto> ConfirmRooting(int id)
    {
        var grow = _repository.GetGrow(id);
        if (grow is null)
        {
            return NotFoundError("grow_not_found", $"Grow mit Id {id} existiert nicht.");
        }

        if (grow.StartMaterial != StartMaterial.Clone)
        {
            return BadRequestError("invalid_action", "Bewurzelungsbestaetigung ist nur fuer Stecklinge moeglich.");
        }

        if (!grow.RootedAt.HasValue)
        {
            grow.RootedAt = DateTime.Now;
            grow.CloneIsRooted = true;
            if (grow.Status == GrowStatus.Planning)
            {
                grow.Status = GrowStatus.Running;
            }

            _repository.UpdateGrow(grow);
            _journalRepository.Create(new JournalEntry
            {
                GrowId = id,
                EntryType = JournalEntryType.CloneRooted,
                Body = "Bewurzelung bestaetigt.",
                OccurredAtUtc = DateTime.UtcNow
            });
        }

        return Ok(new GrowActionResultDto(_repository.GetGrow(id)!.ToDetailDto(), "Bewurzelung bestaetigt."));
    }

    [HttpPost("{id:int}/actions/flip-to-flower")]
    [ProducesResponseType(typeof(GrowActionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<GrowActionResultDto> FlipToFlower(int id)
    {
        var grow = _repository.GetGrow(id);
        if (grow is null)
        {
            return NotFoundError("grow_not_found", $"Grow mit Id {id} existiert nicht.");
        }

        if (grow.SeedType == SeedType.Autoflower)
        {
            return BadRequestError("invalid_action", "Autoflower braucht keinen Flip.");
        }

        if (!grow.FlipDate.HasValue)
        {
            grow.FlipDate = DateTime.Today;
            _repository.UpdateGrow(grow);
            _journalRepository.Create(new JournalEntry
            {
                GrowId = id,
                EntryType = JournalEntryType.FlipToFlower,
                Body = "Auf 12/12 geflippt.",
                OccurredAtUtc = DateTime.UtcNow
            });
        }

        return Ok(new GrowActionResultDto(_repository.GetGrow(id)!.ToDetailDto(), "Flip zu 12/12 eingetragen."));
    }

    private void ValidateOperationLogValues(params (double? Value, string FieldName)[] values)
    {
        foreach (var (value, fieldName) in values)
        {
            if (value is < 0)
            {
                ModelState.AddModelError(fieldName, "Wert darf nicht negativ sein.");
            }
        }
    }

    private void ValidatePh(double? value, string fieldName)
    {
        if (value is < 0 or > 14)
        {
            ModelState.AddModelError(fieldName, "pH-Wert muss zwischen 0 und 14 liegen.");
        }
    }

    private double? ResolveAddbackReservoirLiters(GrowRun grow)
    {
        if (grow.SystemId.HasValue)
        {
            var hydroSetup = _repository.GetHydroSetup(grow.SystemId.Value);
            var totalVolume = CalculateHydroSetupTotalVolumeLiters(hydroSetup);
            if (totalVolume.HasValue)
            {
                return totalVolume;
            }
        }

        return TryParseReservoirSize(grow.ReservoirSize);
    }

    private static double? CalculateHydroSetupTotalVolumeLiters(GrowSystem? hydroSetup)
    {
        if (hydroSetup is null)
        {
            return null;
        }

        // Gemessen schlaegt geschaetzt — dieselbe Regel wie in
        // HydroSetupMapping.BetriebsvolumenLiter, und dort steht der Anlass.
        var volumen = HydroSetupMapping.BetriebsvolumenLiter(hydroSetup);
        return volumen is { } wert && wert > 0 ? Math.Round(wert, 1) : null;
    }

    private static double? TryParseReservoirSize(string? reservoirSize)
    {
        if (string.IsNullOrWhiteSpace(reservoirSize))
        {
            return null;
        }

        var match = System.Text.RegularExpressions.Regex.Match(reservoirSize, @"(\d+([.,]\d+)?)");
        if (!match.Success)
        {
            return null;
        }

        return double.TryParse(
            match.Value.Replace(',', '.'),
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
    }
}
