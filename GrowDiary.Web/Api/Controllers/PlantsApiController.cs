using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Mapping;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

[ApiController]
[Route("api/plants")]
[Produces("application/json")]
[KiStufe(KiStufe.GrowPlanen)]
public sealed class PlantsApiController : ApiControllerBase
{
    private readonly GrowRepository _repository;

    public PlantsApiController(GrowRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PlantInstanceDto>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<PlantInstanceDto>> List([FromQuery] int? setupId = null, [FromQuery] int? growId = null)
    {
        var plants = setupId.HasValue
            ? _repository.GetPlantsBySetup(setupId.Value)
            : growId.HasValue
                ? _repository.GetPlantsByGrow(growId.Value)
                : _repository.GetPlants();

        return Ok(plants.Select(plant => plant.ToDto()).ToList());
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(PlantInstanceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<PlantInstanceDto> Detail(int id)
    {
        var plant = _repository.GetPlant(id);
        return plant is null
            ? NotFoundError("plant_not_found", $"Pflanze mit Id {id} existiert nicht.")
            : Ok(plant.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(PlantInstanceDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public ActionResult<PlantInstanceDto> Create([FromBody] CreatePlantInstanceRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        ValidatePlant(request.Label, null, request.ParentPlantId, request.StrainId, request.SetupId, request.GrowId, request.StartedAt, request.EndedAt);
        var topfGrund = ValidateTopf(null, request.GrowId, request.SiteIndex, istNeu: true);
        if (!ModelState.IsValid)
        {
            return ValidationError(topfGrund);
        }

        var plant = _repository.CreatePlant(request.ToModel());
        PflanzenzahlNachtragen(plant.GrowId);
        return CreatedAtAction(nameof(Detail), new { id = plant.Id }, plant.ToDto());
    }

    [HttpPost("clone-from-mother")]
    [ProducesResponseType(typeof(PlantInstanceDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public ActionResult<PlantInstanceDto> CloneFromMother([FromBody] CreateCloneFromMotherRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var mother = _repository.GetPlant(request.MotherPlantId);
        if (mother is null)
        {
            ModelState.AddModelError(nameof(CreateCloneFromMotherRequest.MotherPlantId), $"Mutterpflanze mit Id {request.MotherPlantId} existiert nicht.");
        }
        else if (mother.PlantRole != PlantRole.Mother)
        {
            ModelState.AddModelError(nameof(CreateCloneFromMotherRequest.MotherPlantId), "Nur Pflanzen mit PlantRole Mother koennen als Clone-Quelle genutzt werden.");
        }

        if (string.IsNullOrWhiteSpace(request.Label))
        {
            ModelState.AddModelError(nameof(CreateCloneFromMotherRequest.Label), "Label darf nicht leer sein.");
        }

        if (request.TargetSetupId.HasValue)
        {
            var targetSetup = _repository.GetSetup(request.TargetSetupId.Value);
            if (targetSetup is null)
            {
                ModelState.AddModelError(nameof(CreateCloneFromMotherRequest.TargetSetupId), $"Ziel-Setup mit Id {request.TargetSetupId.Value} existiert nicht.");
            }
            else if (targetSetup.SetupType != SetupType.Quarantine)
            {
                ModelState.AddModelError(nameof(CreateCloneFromMotherRequest.TargetSetupId), "Clone-Ziel muss ein Quarantine-Setup sein.");
            }
        }

        if (request.StrainId.HasValue && _repository.GetStrain(request.StrainId.Value) is null)
        {
            ModelState.AddModelError(nameof(CreateCloneFromMotherRequest.StrainId), $"StrainId {request.StrainId.Value} existiert nicht.");
        }

        if (!ModelState.IsValid || mother is null)
        {
            return ValidationError();
        }

        var cutAt = request.CutAt ?? DateTime.Now;
        var clone = new PlantInstance
        {
            StrainId = request.StrainId ?? mother.StrainId,
            SetupId = request.TargetSetupId,
            GrowId = null,
            ParentPlantId = mother.Id,
            Label = request.Label.Trim(),
            PlantRole = PlantRole.Clone,
            PlantStatus = PlantStatus.Active,
            PhenoLabel = Normalize(request.PhenoLabel),
            StartedAt = cutAt,
            Notes = Normalize(request.Notes)
        };

        var created = _repository.CreateCloneFromMother(clone, mother.SetupId, cutAt);
        return CreatedAtAction(nameof(Detail), new { id = created.Id }, created.ToDto());
    }

    [HttpPost("decide-quarantine")]
    [ProducesResponseType(typeof(PlantInstanceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public ActionResult<PlantInstanceDto> DecideQuarantine([FromBody] DecideQuarantinePlantRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var plant = _repository.GetPlant(request.PlantId);
        Setup? quarantineSetup = null;
        if (plant is null)
        {
            ModelState.AddModelError(nameof(DecideQuarantinePlantRequest.PlantId), $"Pflanze mit Id {request.PlantId} existiert nicht.");
        }
        else if (!plant.SetupId.HasValue)
        {
            ModelState.AddModelError(nameof(DecideQuarantinePlantRequest.PlantId), "Pflanze ist keinem Quarantine-Setup zugeordnet.");
        }
        else
        {
            quarantineSetup = _repository.GetSetup(plant.SetupId.Value);
            if (quarantineSetup is null || quarantineSetup.SetupType != SetupType.Quarantine)
            {
                ModelState.AddModelError(nameof(DecideQuarantinePlantRequest.PlantId), "Nur Plants aus einem Quarantine-Setup koennen entschieden werden.");
            }
        }

        var isCleared = string.Equals(request.Decision, "Cleared", StringComparison.Ordinal);
        var isRejected = string.Equals(request.Decision, "Rejected", StringComparison.Ordinal);
        if (!isCleared && !isRejected)
        {
            ModelState.AddModelError(nameof(DecideQuarantinePlantRequest.Decision), "Decision muss Cleared oder Rejected sein.");
        }

        Setup? targetSetup = null;
        GrowRun? targetGrow = null;
        if (isCleared)
        {
            if (request.TargetSetupId.HasValue)
            {
                targetSetup = _repository.GetSetup(request.TargetSetupId.Value);
                if (targetSetup is null)
                {
                    ModelState.AddModelError(nameof(DecideQuarantinePlantRequest.TargetSetupId), $"Ziel-Setup mit Id {request.TargetSetupId.Value} existiert nicht.");
                }
                else if (targetSetup.SetupType != SetupType.Production)
                {
                    ModelState.AddModelError(nameof(DecideQuarantinePlantRequest.TargetSetupId), "Freigabe-Ziel muss ein Production-Setup sein.");
                }
            }

            if (request.TargetGrowId.HasValue)
            {
                targetGrow = _repository.GetGrow(request.TargetGrowId.Value);
                if (targetGrow is null)
                {
                    ModelState.AddModelError(nameof(DecideQuarantinePlantRequest.TargetGrowId), $"Grow mit Id {request.TargetGrowId.Value} existiert nicht.");
                }
            }

            if (targetSetup is not null && targetGrow is not null)
            {
                if (targetGrow.SetupId.HasValue && targetGrow.SetupId.Value != targetSetup.Id)
                {
                    ModelState.AddModelError(nameof(DecideQuarantinePlantRequest.TargetGrowId), "Grow passt nicht zum gewaehlten Production-Setup.");
                }

                if (targetGrow.TentId.HasValue && targetGrow.TentId.Value != targetSetup.TentId)
                {
                    ModelState.AddModelError(nameof(DecideQuarantinePlantRequest.TargetGrowId), "Grow und Production-Setup liegen in unterschiedlichen Zelten.");
                }
            }
        }

        if (isRejected)
        {
            if (request.TargetSetupId.HasValue)
            {
                ModelState.AddModelError(nameof(DecideQuarantinePlantRequest.TargetSetupId), "Rejected darf kein Ziel-Setup enthalten.");
            }

            if (request.TargetGrowId.HasValue)
            {
                ModelState.AddModelError(nameof(DecideQuarantinePlantRequest.TargetGrowId), "Rejected darf keinen Ziel-Grow enthalten.");
            }
        }

        // Eine Freigabe in einen Grow ist ein Einzug — und ein Einzug braucht
        // einen Platz. Ohne diese Zeile war das der zweite Weg an der
        // Topf-Pruefung vorbei (gefunden vom Pruefer, am laufenden Stand
        // nachgestellt: fuenfte Pflanze in ein Vier-Topf-System).
        string? topfGrund = null;
        if (isCleared && plant is not null && request.TargetGrowId.HasValue)
        {
            topfGrund = ValidateTopf(plant.Id, request.TargetGrowId.Value, plant.SiteIndex, istNeu: false);
        }

        if (!ModelState.IsValid || plant is null || quarantineSetup is null)
        {
            return ValidationError(topfGrund);
        }

        var decidedAt = request.DecidedAt ?? DateTime.Now;
        plant.Notes = Normalize(request.Notes);
        if (isCleared)
        {
            plant.SetupId = request.TargetSetupId ?? plant.SetupId;
            plant.GrowId = request.TargetGrowId ?? plant.GrowId;
            if (request.TargetSetupId.HasValue || request.TargetGrowId.HasValue)
            {
                plant.PlantRole = PlantRole.Production;
            }
            plant.PlantStatus = PlantStatus.Active;
            plant.EndedAt = null;
        }
        else
        {
            plant.PlantStatus = PlantStatus.Culled;
            plant.EndedAt = decidedAt;
        }

        var updated = _repository.DecideQuarantinePlant(plant, quarantineSetup.Id, request.Decision);
        return Ok(updated.ToDto());
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(PlantInstanceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<PlantInstanceDto> Update(int id, [FromBody] UpdatePlantInstanceRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationError();
        }

        var plant = _repository.GetPlant(id);
        if (plant is null)
        {
            return NotFoundError("plant_not_found", $"Pflanze mit Id {id} existiert nicht.");
        }

        ValidatePlant(request.Label, id, request.ParentPlantId, request.StrainId, request.SetupId, request.GrowId, request.StartedAt, request.EndedAt);
        var topfGrund = ValidateTopf(id, request.GrowId ?? plant.GrowId, request.SiteIndex, istNeu: false);
        if (!ModelState.IsValid)
        {
            return ValidationError(topfGrund);
        }

        var vorherigerGrow = plant.GrowId;
        request.ApplyTo(plant);
        _repository.UpdatePlant(plant);
        PflanzenzahlNachtragen(vorherigerGrow);
        if (plant.GrowId != vorherigerGrow) PflanzenzahlNachtragen(plant.GrowId);
        return Ok(_repository.GetPlant(id)!.ToDto());
    }

    /// <summary>Eine Pflanze entfernen.</summary>
    /// <remarks>
    /// <para><b>Warum es das erst jetzt gibt.</b> Pflanzen liessen sich anlegen
    /// und ändern, aber nie entfernen — im ganzen Backend gab es kein
    /// <c>HttpDelete</c> und im Repository keine Löschung. Wer eine Pflanze zu
    /// viel anlegte, behielt sie. Genau darauf lief die gemeldete Sache hinaus:
    /// mehr Pflanzen als Töpfe, und kein Weg zurück.</para>
    ///
    /// <para><b>Was nicht gelöscht wird.</b> Eine Mutter mit Stecklingen —
    /// sonst verlöre die Abstammung ihren Anfang (<c>ParentPlantId</c> stünde
    /// auf <c>NULL</c>, und niemand wüsste mehr, wovon der Klon stammt). Die
    /// Pheno-Bewertung dagegen gehört zur Pflanze und geht mit ihr
    /// (<c>ON DELETE CASCADE</c>).</para>
    /// </remarks>
    [HttpDelete("{id:int}")]
    [KiStufe(KiStufe.Verwaltung)]
    [KiSicherungVorher]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public IActionResult Delete(int id)
    {
        var plant = _repository.GetPlant(id);
        if (plant is null)
        {
            return NotFoundError("plant_not_found", $"Pflanze mit Id {id} existiert nicht.");
        }

        var kinder = _repository.CountPlantChildren(id);
        if (kinder > 0)
        {
            ModelState.AddModelError(
                nameof(id),
                $"'{plant.Label}' ist die Mutter von {kinder} Pflanzen. "
                + "Solange die stehen, bleibt die Abstammung erhalten.");
            return ValidationError();
        }

        _repository.DeletePlant(id);
        PflanzenzahlNachtragen(plant.GrowId);
        return NoContent();
    }

    /// <summary>
    /// Die Pflanzenzahl am Grow der erfassten Wirklichkeit nachziehen.
    /// </summary>
    /// <remarks>
    /// <para><b>Eine Wahrheit je Zahl.</b> <c>GrowRun.PlantCount</c> kommt aus
    /// dem Grow-Formular, die einzeln erfassten Pflanzen aus dieser Tabelle.
    /// Der gemeldete Screenshot zeigte beides nebeneinander: Kachel „Pflanzen
    /// 6", darunter acht Zeilen. Fuenf weitere Stellen lesen <c>PlantCount</c>
    /// (Grow-Liste, Live-Kachel, Flaeche je Pflanze, Archiv, g/Pflanze) — ohne
    /// diesen Abgleich widersprechen sie der Detailseite.</para>
    ///
    /// <para>Sobald Pflanzen einzeln erfasst sind, sind sie die Wahrheit; steht
    /// keine einzeln da, bleibt die Zahl aus dem Formular unangetastet.</para>
    /// </remarks>
    private void PflanzenzahlNachtragen(int? growId)
    {
        if (growId is not { } schluessel) return;
        if (_repository.GetGrow(schluessel) is not { } grow) return;

        var erfasst = _repository.GetPlantsByGrow(schluessel).Count;
        if (erfasst == 0 || grow.PlantCount == erfasst) return;

        grow.PlantCount = erfasst;
        _repository.UpdateGrow(grow);
    }

    /// <summary>
    /// Ein Topf, den es gibt — und in dem noch keine andere Pflanze steht.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Anlass (25.08.2026).</b> Gemeldet als „du kannst mehr Sorten
    /// angeben, als es Töpfe gibt". Am laufenden Stand belegt: in ein
    /// Vier-Topf-System liessen sich acht Pflanzen legen, zwei davon in
    /// denselben Topf 1, eine in einen Topf 999 — jedes Mal HTTP 201.
    /// <see cref="ValidatePlant"/> sah den <c>SiteIndex</c> überhaupt nicht an.</para>
    ///
    /// <para><b>Warum eine Regel für zwei Fehler reicht.</b> Ist jeder Topf
    /// höchstens einmal belegt und liegt jede Nummer in 1..n, kann es gar
    /// nicht mehr Pflanzen als Töpfe geben. Die Zählung darüber fängt nur den
    /// Fall ab, dass jemand Pflanzen ganz ohne Topf anlegt.</para>
    ///
    /// <para><b>Warum beim Ändern milder geprüft wird.</b> Bestände, die vor
    /// dieser Prüfung entstanden sind, verletzen sie bereits — der Nutzer, der
    /// das gemeldet hat, hat acht Pflanzen bei sechs Töpfen stehen. Würde jedes
    /// PUT daran scheitern, könnte er seine eigenen Daten nicht mehr in Ordnung
    /// bringen. Geprüft wird deshalb nur ein Topf, der sich <i>ändert</i>;
    /// den Rest räumt er über das Löschen auf.</para>
    /// </remarks>
    /// <param name="plantId">Beim Ändern die eigene Id, damit sie sich nicht selbst blockiert.</param>
    /// <param name="istNeu">Nur beim Anlegen wird zusätzlich die Anzahl geprüft.</param>
    private string? ValidateTopf(int? plantId, int? growId, int? siteIndex, bool istNeu)
    {
        // Ohne Grow gibt es kein System und keine Töpfe — Mutterpflanzen und
        // Stecklinge in einem Setup fallen bewusst nicht darunter.
        if (growId is not { } growSchluessel) return null;
        if (_repository.GetGrow(growSchluessel) is not { } grow) return null;
        if (grow.SystemId is not { } systemSchluessel) return null;
        if (_repository.GetSystem(systemSchluessel) is not { PotCount: > 0 } system) return null;

        var toepfe = system.PotCount.Value;
        var andere = _repository.GetPlantsByGrow(growSchluessel)
            .Where(p => p.Id != plantId)
            .ToList();

        var bisher = plantId is { } eigene ? _repository.GetPlant(eigene) : null;

        // „Geändert" heisst NICHT nur „andere Nummer": ein Umzug in einen
        // anderen Grow mit derselben Nummer ist ebenfalls ein neuer Platz.
        // Genau diese Lücke hat der Prüfer am laufenden Stand aufgemacht — die
        // Pflanze wanderte mit Topf 1 in einen Grow, dessen Topf 1 belegt war.
        var kommtNeuHinzu = istNeu || bisher is null || bisher.GrowId != growSchluessel;
        var topfGeaendert = kommtNeuHinzu || siteIndex != bisher!.SiteIndex;

        string? Melden(string feld, string satz)
        {
            ModelState.AddModelError(feld, satz);
            return satz;
        }

        string? grund = null;

        if (siteIndex is { } topf && topfGeaendert)
        {
            if (topf < 1 || topf > toepfe)
            {
                grund = Melden(nameof(CreatePlantInstanceRequest.SiteIndex),
                    $"Das System hat {toepfe} Töpfe — Topf {topf} gibt es dort nicht.");
            }
            else if (andere.FirstOrDefault(p => p.SiteIndex == topf) is { } besetzt)
            {
                grund = Melden(nameof(CreatePlantInstanceRequest.SiteIndex),
                    $"In Topf {topf} steht schon '{besetzt.Label}'. Ein Topf trägt eine Pflanze.");
            }
        }

        // Die Anzahl zählt bei jedem, der neu in diesen Grow kommt — nicht nur
        // beim Anlegen. Sonst schleust ein Umzug beliebig viele Pflanzen ein.
        if (kommtNeuHinzu && andere.Count >= toepfe)
        {
            grund ??= Melden(nameof(CreatePlantInstanceRequest.GrowId),
                $"Das System hat {toepfe} Töpfe, und {andere.Count} Pflanzen sind schon erfasst. "
                + "Entferne erst eine Pflanze oder vergrößere das System.");
        }

        return grund;
    }

    private void ValidatePlant(string label, int? plantId, int? parentPlantId, int? strainId, int? setupId, int? growId, DateTime? startedAt, DateTime? endedAt)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            ModelState.AddModelError(nameof(CreatePlantInstanceRequest.Label), "Label darf nicht leer sein.");
        }

        if (plantId.HasValue && parentPlantId == plantId)
        {
            ModelState.AddModelError(nameof(UpdatePlantInstanceRequest.ParentPlantId), "ParentPlantId darf nicht auf dieselbe Pflanze zeigen.");
        }

        if (parentPlantId.HasValue && _repository.GetPlant(parentPlantId.Value) is null)
        {
            ModelState.AddModelError(nameof(CreatePlantInstanceRequest.ParentPlantId), $"ParentPlantId {parentPlantId.Value} existiert nicht.");
        }

        if (strainId.HasValue && _repository.GetStrain(strainId.Value) is null)
        {
            ModelState.AddModelError(nameof(CreatePlantInstanceRequest.StrainId), $"StrainId {strainId.Value} existiert nicht.");
        }

        if (setupId.HasValue && _repository.GetSetup(setupId.Value) is null)
        {
            ModelState.AddModelError(nameof(CreatePlantInstanceRequest.SetupId), $"SetupId {setupId.Value} existiert nicht.");
        }

        if (growId.HasValue && _repository.GetGrow(growId.Value) is null)
        {
            ModelState.AddModelError(nameof(CreatePlantInstanceRequest.GrowId), $"GrowId {growId.Value} existiert nicht.");
        }

        if (startedAt.HasValue && endedAt.HasValue && endedAt.Value < startedAt.Value)
        {
            ModelState.AddModelError(nameof(CreatePlantInstanceRequest.EndedAt), "EndedAt darf nicht vor StartedAt liegen.");
        }
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
