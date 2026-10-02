using System.Globalization;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Mapping;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>
/// Grows-API fuer React-freundliche JSON-Endpunkte.
/// </summary>
[ApiController]
[Route("api/grows")]
[Produces("application/json")]
public sealed class GrowsApiController : ApiControllerBase
{
    private readonly GrowRepository _repository;
    private readonly AuditRepository _auditRepository;
    private readonly SystemAuditRepository _systemAudit;
    private readonly SetupRepository _setups;
    private readonly HydroSetupRepository _hydro;
    private readonly DeviationAnalyzerService _deviationAnalyzer;
    private readonly TreatmentRecommender _treatmentRecommender;
    private readonly Services.GrowPlan.GrowPlanService? _plaene; // Fork AI (Grow-Plan)

    public GrowsApiController(
        GrowRepository repository,
        AuditRepository auditRepository,
        DeviationAnalyzerService deviationAnalyzer,
        TreatmentRecommender treatmentRecommender,
        SetupRepository setups,
        HydroSetupRepository hydro,
        SystemAuditRepository systemAudit,
        Services.GrowPlan.GrowPlanService? plaene = null)
    {
        _plaene = plaene;
        _repository = repository;
        _auditRepository = auditRepository;
        _systemAudit = systemAudit;
        _setups = setups;
        _hydro = hydro;
        _deviationAnalyzer = deviationAnalyzer;
        _treatmentRecommender = treatmentRecommender;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<GrowSummaryDto>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<GrowSummaryDto>> List(
        [FromQuery] bool archived = false,
        [FromQuery] string? search = null)
    {
        var grows = archived
            ? _repository.GetArchivedGrows(search)
            : _repository.GetActiveGrows(search);

        return Ok(grows.Select(grow => grow.ToSummaryDto()).ToList());
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(GrowDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<GrowDetailDto> Detail(int id)
    {
        var grow = _repository.GetGrow(id);
        if (grow is null)
        {
            return NotFoundError("grow_not_found", $"Grow mit Id {id} existiert nicht.");
        }

        return Ok(grow.ToDetailDto());
    }

    [HttpGet("{growId:int}/deviations")]
    [ProducesResponseType(typeof(IReadOnlyList<GrowDeviation>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<IReadOnlyList<GrowDeviation>> Deviations(int growId)
    {
        var grow = _repository.GetGrow(growId);
        if (grow is null)
        {
            return NotFoundError("grow_not_found", $"Grow mit Id {growId} existiert nicht.");
        }

        var measurements = _repository.GetMeasurementsForGrow(growId);
        return Ok(_deviationAnalyzer.Analyze(grow, measurements, LeafOffsetFor(grow), SystemProfilFuer(grow)).ToList());
    }

    [HttpGet("{growId:int}/treatment-recommendations")]
    [ProducesResponseType(typeof(GrowTreatmentRecommendationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<GrowTreatmentRecommendationDto> TreatmentRecommendations(int growId)
    {
        var grow = _repository.GetGrow(growId);
        if (grow is null)
        {
            return NotFoundError("grow_not_found", $"Grow mit Id {growId} existiert nicht.");
        }

        var measurements = _repository.GetMeasurementsForGrow(growId);
        var deviations = _deviationAnalyzer.Analyze(grow, measurements, LeafOffsetFor(grow), SystemProfilFuer(grow));
        return Ok(_treatmentRecommender.Recommend(grow, deviations));
    }

    [HttpPost]
    [ProducesResponseType(typeof(GrowDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public ActionResult<GrowDetailDto> Create([FromBody] GrowUpsertRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        GrowRun grow;
        try
        {
            grow = request.ToFormModel().ToGrow();
        }
        catch
        {
            ModelState.AddModelError(nameof(request.StartDate), "Startdatum konnte nicht gelesen werden.");
            return ValidationError();
        }

        if (!ValidateHydroStyle(grow.HydroStyle))
        {
            return ValidationError();
        }

        if (!ValidateHydroSetupAssignment(grow, nameof(request.SystemId), requireHydroSetup: true))
        {
            return ValidationError();
        }

        if (!ValidateSetupAssignment(grow, nameof(request.SetupId)))
        {
            return ValidationError();
        }

        var growId = _repository.CreateGrow(grow);

        var savedGrow = _repository.GetGrow(growId)!;
        // Läuft, sobald nichts mehr keimt oder bewurzelt — die Auskunft kommt
        // aus dem Phasenanker (bis 02.10.2026 aus einem eigenen Wochenzähler).
        var stand = Phasenanker.Fuer(savedGrow, DateTime.Today);
        if (savedGrow.Status == GrowStatus.Planning
            && stand.Anzucht is not (Anzuchtart.Keimung or Anzuchtart.Bewurzelung))
        {
            savedGrow.Status = GrowStatus.Running;
            _repository.UpdateGrow(savedGrow);
        }

        // Fork AI (Grow-Plan): das gewählte Programm wird als eigener Plan mit dem Grow gespeichert.
        _plaene?.Anlegen(savedGrow);

        /* Die Pflanzen gleich mit — eine je Topf, mit der Sorte des Grows.
           Gemeldet am 28.08.2026: „der User kann unter grow nur eine Sorte
           auswaehlen aber bei den Toepfen fuer den Grow 4 Stueck auswaehlen".
           Ein Grow mit `plantCount: 4` legte null Pflanzen an; wer vier Toepfe
           fuhr, klickte danach viermal „Pflanze hinzufuegen" und waehlte jedes
           Mal dieselbe Sorte. Siehe `GrowPflanzen`. */
        var angelegtePflanzen = GrowPflanzen.NachAnlage(
            _repository, _setups, _hydro, growId, Belegung(request));

        _auditRepository.Add(new AuditEntry
        {
            GrowId = growId,
            EntityType = "Grow",
            Action = "Grow angelegt",
            Summary = $"Grow '{request.Name}' wurde erstellt"
                + (request.TemplateId.HasValue ? $" auf Basis des Templates #{request.TemplateId}" : string.Empty)
                + (angelegtePflanzen > 0 ? $" mit {angelegtePflanzen} Pflanzen auf Topf 1–{angelegtePflanzen}" : string.Empty)
                + "."
        });

        return CreatedAtAction(nameof(Detail), new { id = growId }, _repository.GetGrow(growId)!.ToDetailDto());
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(GrowDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<GrowDetailDto> Update(int id, [FromBody] GrowUpsertRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var existing = _repository.GetGrow(id);
        if (existing is null)
        {
            return NotFoundError("grow_not_found", $"Grow mit Id {id} existiert nicht.");
        }

        GrowRun grow;
        try
        {
            grow = request.ToFormModel().ToGrow();
        }
        catch
        {
            ModelState.AddModelError(nameof(request.StartDate), "Startdatum oder Flip-Datum konnten nicht gelesen werden.");
            return ValidationError();
        }

        if (!ValidateHydroStyle(grow.HydroStyle))
        {
            return ValidationError();
        }

        if (!grow.SystemId.HasValue && existing.SystemId.HasValue)
        {
            grow.SystemId = existing.SystemId;
        }

        if (!ValidateHydroSetupAssignment(grow, nameof(request.SystemId), requireHydroSetup: !IsLegacyGrowWithoutHydroSetup(existing)))
        {
            return ValidationError();
        }

        if (!ValidateSetupAssignment(grow, nameof(request.SetupId)))
        {
            return ValidationError();
        }

        /* Fork AI (02.10.2026): ein Programmwechsel bei einem Grow MIT Plan geht nur
           über POST /api/grows/{id}/plan/programm — hier wird er abgelehnt.

           Der Plan gewinnt beim Lesen (MischplanService.ProgrammFuerGrow fragt
           zuerst das Register). Nahm dieser Weg eine neue FeedProgramId an, stand
           am Grow Programm B, während Zielwerte, Alarme und HA-Übergabe weiter
           Programm A folgten. Durchreichen an GrowPlanService.ProgrammWechseln
           ginge nicht ehrlich: der Wechsel braucht die Entscheidung „eigene
           Änderungen behalten oder verwerfen", und dieses Formular trägt sie
           nicht. Raten hieße im einen Fall Datenverlust, im anderen Werte aus
           einem Programm, das niemand mehr gewählt hat. Die Oberfläche ruft den
           Planweg vorher auf; danach ist die Id hier dieselbe und es gibt
           nichts abzulehnen. */
        // Verglichen wird mit dem Programm des PLANS, nicht mit dem Feld am Grow —
        // wer dasselbe Programm einträgt, wechselt nichts.
        //
        // Ein LEERES Programm ist ebenfalls ein Wechsel (zum 02.10.2026 nachgezogen,
        // Befund aus docs/pruefung-2026-10-01.md). Bis dahin rutschte null durch:
        // am Grow stand „kein Programm", der Plan blieb und lieferte weiter
        // Zielwerte, Alarme und HA-Übergabe aus Programm A — und der Wochenplan
        // (WochenplanApiController.WerteDto) zählt Grows über genau dieses Feld,
        // fand den Grow also nicht mehr. Einen Weg „Plan entfernen" gibt es nicht
        // (Pläne werden nie gelöscht, siehe GrowPlanService.RegisterLaden), also
        // kann der Grow sein Programm nicht loswerden — das Feld darf es dann auch
        // nicht behaupten. Die Oberfläche sperrt das Namensfeld bei Grows mit Plan.
        if (Services.GrowPlan.GrowPlanRegister.Programm(id) is { } planProgramm
            && !string.Equals(grow.FeedProgramId, planProgramm.Id, StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(request.FeedProgramId), grow.FeedProgramId is null
                ? $"Dieser Grow läuft nach seinem Plan („{(string.IsNullOrWhiteSpace(planProgramm.Name) ? planProgramm.Id : planProgramm.Name)}“) — ohne Programm "
                  + "geht es nicht. Ein anderes Programm wählst du über „Programm wechseln“ "
                  + "(POST /api/grows/{id}/plan/programm)."
                : "Dieser Grow hat einen Plan — das Programm wechselt über „Programm wechseln“ "
                  + "(POST /api/grows/{id}/plan/programm), damit klar ist, ob eigene Änderungen mitgehen.");
            return ValidationError();
        }

        grow.Id = id;
        grow.CreatedAtUtc = existing.CreatedAtUtc;

        // Das Bearbeiten-Formular kennt nur die Stammdaten. Alles, was aus
        // Workflow-Knoepfen und eigenen Endpunkten stammt — bestaetigte
        // Meilensteine, das Enddatum, die Nachtabsenkung — wuerde der
        // Zeilen-Ersatz sonst stillschweigend auf null zuruecksetzen:
        // ein harmloses "Notiz geaendert, speichern" nimmt dem Grow die
        // Keimung, dem Archiv die Laufzeit und der Rampe ihren Schalter.
        grow.GerminatedAt = existing.GerminatedAt;
        grow.RootedAt = existing.RootedAt;
        grow.VegStartedAt = existing.VegStartedAt;
        grow.FinishStartedAt = existing.FinishStartedAt;
        grow.EndDate = existing.EndDate;
        grow.NightRampEnabled = existing.NightRampEnabled;
        grow.NightRampFloorC = existing.NightRampFloorC;

        // Der Flip, in drei Faellen:
        //
        //   Autoflower          -> das Formular kennt das Feld nicht, bewahren:
        //                          ihr Bluetebeginn kommt aus „Bluete beginnt"
        //                          (flip-to-flower, siehe Phasenanker)
        //   FlipDate == null    -> das Feld kam gar nicht mit, bewahren
        //   FlipDate == ""      -> ausdruecklich geleert, loeschen
        //   FlipDate == Datum   -> setzen
        //
        // Frueher stand hier zusaetzlich EntryPoint == Flower. Das Formular
        // zeigt das Feld aber fuer JEDEN Nicht-Autoflower, und der Normalfall
        // ist der andere: ein Grow startet in der Keimung und wird spaeter
        // geflippt. Wer das Datum dann eintrug, bekam HTTP 200 und einen
        // unveraenderten Wert zurueck. Warum null trotzdem bewahrt: ein
        // fremder Aufrufer, der das Feld weglaesst, darf dem Grow seinen Flip
        // nicht nehmen — genau diese Klasse hat schon einmal Meilensteine
        // geloescht (GrowUpdatePreservationTests).
        var formKenntFlip = request.SeedType != SeedType.Autoflower && request.FlipDate is not null;
        if (!formKenntFlip)
        {
            grow.FlipDate = existing.FlipDate;
        }

        /* Die Sorten je Topf — VOR dem Zaehlen, weil ein neu belegter Topf
           die Pflanzenzahl veraendert. Stuende es danach, meldete das Formular
           beim Speichern eine Zahl und beim naechsten Laden eine andere. */
        if (Belegung(request) is { Count: > 0 } belegung)
        {
            GrowPflanzen.SortenSetzen(_repository, _setups, _hydro, id, belegung);
        }

        // Sind Pflanzen EINZELN erfasst, sind sie die Wahrheit ueber die Zahl.
        // Sonst zeigt die Detailseite die erfassten Pflanzen, waehrend
        // Grow-Liste, Live-Kachel, Flaeche je Pflanze, Archiv und g/Pflanze
        // weiter die Formularzahl lesen — fuenf Stellen gegen eine.
        var erfasst = _repository.GetPlantsByGrow(id).Count;
        if (erfasst > 0)
        {
            grow.PlantCount = erfasst;
        }

        // Gleiches Prinzip fuer das Feedchart-Opt-in: fehlt das Feld im
        // Request, bleibt der gespeicherte Schalter stehen.
        if (request.UseFeedChartTargets is null)
        {
            grow.UseFeedChartTargets = existing.UseFeedChartTargets;
        }

        _repository.UpdateGrow(grow);

        // Fork AI (Grow-Plan): wird einem Grow erstmals ein Programm gegeben, bekommt er seinen Plan;
        // wird er abgeschlossen oder wieder geöffnet, folgt der Endstand.
        if (_plaene is not null && _repository.GetGrow(id) is { } gespeichert)
        {
            if (!gespeichert.IsArchived && !Services.GrowPlan.GrowPlanService.HatPlan(id))
            {
                _plaene.Anlegen(gespeichert);
            }
            // Fork AI (02.10.2026): der Name der Anzucht im Plan folgt dem Startmaterial
            // (Steckling „Bewurzelung", Samen „Anzucht"). Ändert das Formular es, gleich
            // hier angleichen — nicht erst beim nächsten Start. Vor dem Abgleichen, damit
            // ein Abschluss im selben Speichern den angeglichenen Stand einfriert; einen
            // schon eingefrorenen Endstand fasst das Nachtragen nicht an.
            if (gespeichert.StartMaterial != existing.StartMaterial)
            {
                _plaene.FehlendeFelderNachtragen([gespeichert]);
            }
            _plaene.Abgleichen(gespeichert);
        }

        _auditRepository.Add(new AuditEntry
        {
            GrowId = id,
            EntityType = "Grow",
            EntityId = id,
            Action = "Grow geaendert",
            Summary = $"Grow '{grow.Name}' aktualisiert. Status: {grow.Status}, SystemId: {(grow.SystemId.HasValue ? grow.SystemId.Value.ToString(CultureInfo.InvariantCulture) : "Legacy")}."
        });

        return Ok(_repository.GetGrow(id)!.ToDetailDto());
    }

    /// <summary>
    /// Die Topf-Belegung aus dem Formular — ohne Sorten, die es nicht gibt.
    /// </summary>
    /// <remarks>
    /// <para>Eine erfundene <c>strainId</c> wird auf „ohne Sorte" gesetzt statt
    /// den ganzen Aufruf abzulehnen: der Topf ist trotzdem belegt, und ein
    /// Formular, das wegen einer inzwischen geloeschten Sorte gar nicht mehr
    /// speichert, ist schlimmer als eines, das die Luecke sichtbar laesst.</para>
    ///
    /// <para><c>null</c> bleibt <c>null</c> — „Feld nicht mitgeschickt" ist
    /// etwas anderes als „leere Liste".</para>
    /// </remarks>
    private List<TopfBelegung>? Belegung(GrowUpsertRequest request)
    {
        if (request.Toepfe is null)
        {
            return null;
        }

        return request.Toepfe
            .Where(eintrag => eintrag.Topf >= 1)
            .Select(eintrag => new TopfBelegung(
                eintrag.Topf,
                eintrag.StrainId is { } id && _repository.GetStrain(id) is not null ? id : null))
            .ToList();
    }

    /// <summary>Ereignisart im System-Protokoll fuer einen geloeschten Grow.</summary>
    public const string GrowGeloeschtProtokollTyp = "grow";

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public IActionResult Delete(int id)
    {
        var existing = _repository.GetGrow(id);
        if (existing is null)
        {
            return NotFoundError("grow_not_found", $"Grow mit Id {id} existiert nicht.");
        }

        _repository.DeleteGrow(id);

        // Das Loeschen selbst gehoert ins System-Protokoll, nicht in das des
        // Grows. `AuditEntries.GrowId` haengt mit ON DELETE CASCADE an `Grows`:
        // ein Eintrag dort — ob vor oder nach dem Loeschen geschrieben — war
        // im selben Augenblick weg, und „wer hat wann welchen Grow
        // geloescht" liess sich nirgends mehr nachlesen. `SystemAuditEvents`
        // ueberlebt den Grow (RelatedGrowId: ON DELETE SET NULL).
        //
        // Deshalb NACH dem Loeschen, ohne RelatedGrowId (die Zeile gibt es
        // nicht mehr, der Fremdschluessel wuerde scheitern) und mit Name und
        // Id im Text. Und ohne den Aufruf scheitern zu lassen: der Grow ist
        // weg, eine 500 dafuer luede nur zum zweiten Versuch ein.
        try
        {
            _systemAudit.Add(new SystemAuditEvent
            {
                EventType = GrowGeloeschtProtokollTyp,
                Action = "Grow geloescht",
                Summary = $"Grow '{existing.Name}' (Id {id.ToString(CultureInfo.InvariantCulture)}) wurde geloescht.",
                Source = "grows-api",
            });
        }
        catch
        {
            // Protokoll ist Nachweis, nicht Voraussetzung.
        }

        return NoContent();
    }

    [HttpPost("{id:int}/archive")]
    [ProducesResponseType(typeof(GrowDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<GrowDetailDto> Archive(int id)
    {
        var existing = _repository.GetGrow(id);
        if (existing is null)
        {
            return NotFoundError("grow_not_found", $"Grow mit Id {id} existiert nicht.");
        }

        if (existing.Status is GrowStatus.Planning or GrowStatus.Running)
        {
            existing.Status = GrowStatus.Completed;
            existing.EndDate ??= DateTime.Today;
            _repository.UpdateGrow(existing);
            _plaene?.Abgleichen(existing); // Fork AI (Grow-Plan): Plan einfrieren
            _auditRepository.Add(new AuditEntry
            {
                GrowId = id,
                EntityType = "Grow",
                EntityId = id,
                Action = "Grow archiviert",
                Summary = $"Grow '{existing.Name}' wurde beendet und archiviert."
            });
        }

        return Ok(_repository.GetGrow(id)!.ToDetailDto());
    }

    private bool ValidateHydroSetupAssignment(GrowRun grow, string fieldName, bool requireHydroSetup)
    {
        if (!grow.SystemId.HasValue)
        {
            if (requireHydroSetup)
            {
                ModelState.AddModelError(fieldName, "Neue Grows brauchen ein DWC/RDWC-Hydro-Setup.");
                return false;
            }

            return true;
        }

        var hydroSetup = _repository.GetHydroSetup(grow.SystemId.Value);
        if (hydroSetup is null)
        {
            ModelState.AddModelError(fieldName, $"Hydro-Setup mit Id {grow.SystemId.Value} existiert nicht.");
            return false;
        }

        if (hydroSetup.Status == HydroSetupStatus.Archived)
        {
            ModelState.AddModelError(fieldName, "Archivierte Hydro-Setups koennen keinem neuen oder aktiven Grow zugeordnet werden.");
            return false;
        }

        if (!hydroSetup.TentId.HasValue)
        {
            ModelState.AddModelError(fieldName, "Das Hydro-Setup ist keinem Zelt zugeordnet.");
            return false;
        }

        if (grow.TentId.HasValue && grow.TentId.Value != hydroSetup.TentId.Value)
        {
            ModelState.AddModelError(fieldName, "Das Hydro-Setup gehoert zu einem anderen Zelt als der Grow.");
            return false;
        }

        if (!Enum.TryParse<HydroStyle>(hydroSetup.HydroStyle, out var hydroStyle) || hydroStyle is not (HydroStyle.DWC or HydroStyle.RDWC))
        {
            ModelState.AddModelError(fieldName, "Das Hydro-Setup muss DWC oder RDWC sein.");
            return false;
        }

        grow.TentId = hydroSetup.TentId;
        grow.HydroStyle = hydroStyle;
        grow.MediumType = MediumType.Hydro;
        grow.FeedingStyle = FeedingStyle.None;
        grow.IrrigationType = IrrigationType.ActiveHydro;
        grow.MediumDetail = hydroStyle.ToString();
        grow.HasChiller = hydroSetup.HasChiller;
        grow.ContainerSize = FormatPotSize(hydroSetup);
        grow.ReservoirSize = FormatReservoirSize(hydroSetup);

        return true;
    }

    private bool ValidateSetupAssignment(GrowRun grow, string fieldName)
    {
        if (!grow.SetupId.HasValue)
        {
            return true;
        }

        var setup = _repository.GetSetup(grow.SetupId.Value);
        if (setup is null)
        {
            ModelState.AddModelError(fieldName, $"Setup mit Id {grow.SetupId.Value} existiert nicht.");
            return false;
        }

        if (setup.SetupType != SetupType.Production)
        {
            ModelState.AddModelError(fieldName, $"Setup-Typ {setup.SetupType} kann keinem GrowRun zugeordnet werden. Erlaubt ist nur Production.");
            return false;
        }

        var setupTent = _repository.GetTent(setup.TentId);
        if (setupTent is null)
        {
            ModelState.AddModelError(fieldName, $"Zelt mit Id {setup.TentId} existiert nicht.");
            return false;
        }

        if (!SetupTentCompatibilityPolicy.IsCompatible(setupTent.TentType, setup.SetupType))
        {
            ModelState.AddModelError(fieldName, $"Setup-Typ {setup.SetupType} ist fuer Tent-Typ {setupTent.TentType} nicht erlaubt.");
            return false;
        }

        if (grow.TentId.HasValue && grow.TentId.Value != setup.TentId)
        {
            ModelState.AddModelError(fieldName, "Das Production-Setup gehoert zu einem anderen Zelt als der GrowRun.");
            return false;
        }

        return true;
    }

    private bool ValidateHydroStyle(HydroStyle hydroStyle)
    {
        if (hydroStyle is HydroStyle.DWC or HydroStyle.RDWC)
        {
            return true;
        }

        ModelState.AddModelError(nameof(GrowUpsertRequest.HydroStyle), "Grow OS unterstuetzt neue Grows aktuell nur mit DWC oder RDWC.");
        return false;
    }

    private static bool IsLegacyGrowWithoutHydroSetup(GrowRun existing)
        => !existing.SystemId.HasValue;

    private static string? FormatPotSize(GrowSystem hydroSetup)
    {
        if (hydroSetup.PotSizeLiters is > 0 && hydroSetup.PotCount is > 0)
        {
            return $"{hydroSetup.PotCount.Value.ToString(CultureInfo.InvariantCulture)} x {FormatLiters(hydroSetup.PotSizeLiters.Value)} L";
        }

        if (hydroSetup.PotSizeLiters is > 0)
        {
            return $"{FormatLiters(hydroSetup.PotSizeLiters.Value)} L";
        }

        return null;
    }

    private static string? FormatReservoirSize(GrowSystem hydroSetup)
    {
        var totalVolume = CalculateTotalVolume(hydroSetup);
        if (totalVolume is > 0)
        {
            return $"{FormatLiters(totalVolume.Value)} L Gesamtvolumen";
        }

        if (hydroSetup.ReservoirLiters is > 0)
        {
            return $"{FormatLiters(hydroSetup.ReservoirLiters.Value)} L Tank";
        }

        return null;
    }

    private static double? CalculateTotalVolume(GrowSystem hydroSetup)
    {
        var potVolume = hydroSetup.PotCount.GetValueOrDefault() * hydroSetup.PotSizeLiters.GetValueOrDefault();
        var total = potVolume + hydroSetup.ReservoirLiters.GetValueOrDefault();
        return total > 0 ? total : null;
    }

    /// <summary>
    /// The tent's leaf offset, so VPD is judged as leaf VPD. Falls back to the documented
    /// RDWC value when the grow has no tent assigned.
    /// </summary>
    /// <summary>
    /// Das Sollwert-Profil des Hydro-Systems, an dem der Grow haengt.
    /// </summary>
    /// <remarks>
    /// Die mittlere Stufe der Kette Grow -&gt; System -&gt; Anbaustil. Ohne sie
    /// fiel die Diagnose immer auf den Anbaustil zurueck und zeigte damit das
    /// Standardprofil, waehrend die Live-Kachel daneben das eigene Profil des
    /// Nutzers auswertete — gemessen EC 0,6-0,8 gegen 0,9-1,1 fuer denselben
    /// Grow.
    /// </remarks>
    private string? SystemProfilFuer(GrowRun grow)
        => grow.SystemId is { } systemId ? _repository.GetSystem(systemId)?.SetpointProfileId : null;

    private double LeafOffsetFor(GrowRun grow) =>
        grow.TentId is { } tentId && _repository.GetTent(tentId) is { } tent
            ? tent.LeafTempOffsetC
            : Tent.DefaultLeafTempOffsetC;

    private static string FormatLiters(double value)
        => value.ToString("0.##", CultureInfo.InvariantCulture);
}
