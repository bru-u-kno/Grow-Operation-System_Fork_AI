using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>Ein Profil, wie es die Oberfläche zeigt — mit aufgelösten Werten je Phase.</summary>
public sealed record SetpointProfileDto(
    string Id,
    string Name,
    string BaseProfileId,
    bool IsShipped,
    int ChangedValueCount,
    /// <summary>Je Phase die fertigen Werte (Basis plus Abweichungen).</summary>
    /// <remarks>
    /// Bewusst eine Liste, kein Wörterbuch: die JSON-Ausgabe schreibt
    /// Wörterbuch-Schlüssel klein, aus „Veg" würde „veg" — und die Oberfläche
    /// suchte dann nach einem Schlüssel, den es nicht gibt.
    /// </remarks>
    List<StageValuesDto> Stages);

/// <summary>Die Werte einer Phase, plus die Felder, die der Nutzer selbst gesetzt hat.</summary>
public sealed record StageValuesDto(
    string Stage,
    Dictionary<string, double> Values,
    List<string> Changed);

public sealed class SetpointProfileUpsertRequest
{
    public string Name { get; set; } = string.Empty;
    public string BaseProfileId { get; set; } = "rdwc-default";
    /// <summary>Nur die abweichenden Werte — was hier fehlt, bleibt bei der Basis.</summary>
    public Dictionary<string, Dictionary<string, double>>? Overrides { get; set; }
}

/// <summary>
/// Sollwert-Profile: die mitgelieferten und die eigenen des Nutzers.
/// </summary>
[ApiController]
[Route("api/setpoint-profiles")]
[Produces("application/json")]
[KiStufe(KiStufe.GrowPlanen)]
public sealed class SetpointProfilesApiController : ApiControllerBase
{
    private static readonly GrowStage[] Stages =
        [GrowStage.Seedling, GrowStage.Clone, GrowStage.Veg, GrowStage.Transition, GrowStage.Flower, GrowStage.Finish];

    private readonly SetpointProfileRepository _profiles;
    private readonly TargetValueService _targets;

    public SetpointProfilesApiController(SetpointProfileRepository profiles, TargetValueService targets)
    {
        _profiles = profiles;
        _targets = targets;
    }

    /// <summary>Alle Profile — mitgeliefert und eigene.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SetpointProfileDto>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<SetpointProfileDto>> GetAll()
    {
        var list = new List<SetpointProfileDto>();

        foreach (var shippedId in _targets.ProfileIds.OrderBy(id => id, StringComparer.Ordinal))
        {
            list.Add(Shipped(shippedId));
        }

        foreach (var own in _profiles.GetAll())
        {
            list.Add(Own(own));
        }

        return Ok(list);
    }

    [HttpPost]
    [ProducesResponseType(typeof(SetpointProfileDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public ActionResult<SetpointProfileDto> Create([FromBody] SetpointProfileUpsertRequest? request)
    {
        if (Validate(request) is { } error) return error;

        var profile = new SetpointProfile
        {
            Name = request!.Name.Trim(),
            BaseProfileId = request.BaseProfileId,
            Overrides = Clean(request.Overrides),
        };
        var id = _profiles.Insert(profile);
        profile.Id = id;
        return CreatedAtAction(nameof(GetAll), Own(profile));
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(SetpointProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<SetpointProfileDto> Update(int id, [FromBody] SetpointProfileUpsertRequest? request)
    {
        var existing = _profiles.Get(id);
        if (existing is null) return NotFoundError("profile_not_found", $"Profil {id} existiert nicht.");
        if (Validate(request) is { } error) return error;

        existing.Name = request!.Name.Trim();
        existing.BaseProfileId = request.BaseProfileId;
        existing.Overrides = Clean(request.Overrides);
        _profiles.Update(existing);
        return Ok(Own(existing));
    }

    [HttpDelete("{id:int}")]
    [KiStufe(KiStufe.Verwaltung)]
    [KiSicherungVorher]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Delete(int id)
    {
        _profiles.Delete(id);
        return NoContent();
    }

    // ---------- Innenleben ----------

    private ActionResult? Validate(SetpointProfileUpsertRequest? request)
    {
        if (request is null)
            return BadRequestError("invalid_body", "Der Anfrage-Rumpf ist leer oder unlesbar.");
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequestError("name_required", "Das Profil braucht einen Namen.");
        if (!_targets.ProfileIds.Contains(request.BaseProfileId))
            return BadRequestError("base_not_found", $"Es gibt kein mitgeliefertes Profil \"{request.BaseProfileId}\".");

        /* Und die WERTE. Bis zum 01.09.2026 wurde hier nur der Name geprueft:
           phMin 6,5 mit phMax 5,5 ging durch, und danach war JEDE pH-Messung
           „daneben" (wert < min ? Below : wert > max ? Above : InTarget). Noch
           schwerer wiegt waterTempNightC — der Wert geht ueber die
           Nachtabsenkung an das Zielgeraet in Home Assistant, also an den
           Kuehler im Zelt. Die Grenzen stehen in
           MeasurementSanityService.PhysikalischeGrenzen und nur dort. */
        /* Unbekannte Phasen und Felder werden ABGELEHNT, nicht weggeworfen.

           Bis zum 02.09.2026 raeumte `Clean` sie still weg und der Endpunkt
           antwortete mit 201. Der Kommentar an `Clean` beklagt genau das
           („dem Nutzer eine Aenderung zu bestaetigen, die nie wirkt") — und
           genau das tat der Endpunkt. Wer die Phase deutsch schrieb, bekam
           „Gespeichert" und ein leeres Profil; beim naechsten Oeffnen waren
           seine Zahlen weg, ohne ein Wort. */
        foreach (var (phase, felder) in request.Overrides ?? [])
        {
            if (!Stages.Any(s => string.Equals(s.ToString(), phase, StringComparison.OrdinalIgnoreCase)))
            {
                ModelState.AddModelError(phase,
                    $"\"{phase}\" ist keine Phase. Moeglich sind: {string.Join(", ", Stages)}.");
                continue;
            }

            foreach (var feld in felder.Keys.Where(k => !SetpointProfile.Fields.Contains(k, StringComparer.Ordinal)))
            {
                ModelState.AddModelError($"{phase}.{feld}",
                    $"\"{feld}\" ist kein Sollwert-Feld. Moeglich sind: "
                    + string.Join(", ", SetpointProfile.Fields) + ".");
            }
        }

        if (!ModelState.IsValid)
        {
            return ValidationError("Das Profil laesst sich so nicht speichern.");
        }

        // Gegen die Basis zusammengesetzt pruefen (Fork AI, 02.10.2026): phMin 7
        // allein lag sonst ungesehen ueber dem phMax 6,2 des Basisprofils.
        var basisProfil = request.BaseProfileId;
        var maengel = SetpointProfilGrenzen.Pruefe(
            SetpointProfile.PhasenNormalisiert(request.Overrides),
            phase => Enum.TryParse<GrowStage>(phase, out var stufe)
                     && _targets.GetTargets(basisProfil, stufe) is { } werte
                ? SetpointProfile.WerteAus(werte)
                : null);
        if (maengel.Count > 0)
        {
            foreach (var mangel in maengel)
            {
                ModelState.AddModelError(mangel.Feld, mangel.Meldung);
            }

            return ValidationError("Das Profil laesst sich so nicht speichern.");
        }

        return null;
    }

    /// <summary>
    /// Wirft weg, was nicht in die Tabelle gehört.
    /// </summary>
    /// <remarks>
    /// <para>Unbekannte Phasen oder Felder würden beim Anwenden still ignoriert — sie
    /// aber zu speichern hiesse, dem Nutzer eine Änderung zu bestätigen, die
    /// nie wirkt.</para>
    /// <para>Dasselbe für die Schreibweise der Phase (Fork AI, 02.10.2026): die
    /// Prüfung oben nimmt „flower" an, <c>Apply</c> sucht „Flower". Gespeichert
    /// wird deshalb der kanonische Name.</para>
    /// </remarks>
    private static Dictionary<string, Dictionary<string, double>> Clean(
        Dictionary<string, Dictionary<string, double>>? raw)
    {
        var result = new Dictionary<string, Dictionary<string, double>>();
        if (raw is null) return result;

        foreach (var (stageName, felder) in SetpointProfile.PhasenNormalisiert(raw))
        {
            if (!Stages.Any(s => string.Equals(s.ToString(), stageName, StringComparison.Ordinal))) continue;

            var sauber = felder
                .Where(paar => SetpointProfile.Fields.Contains(paar.Key, StringComparer.Ordinal))
                .Where(paar => double.IsFinite(paar.Value))
                .ToDictionary(paar => paar.Key, paar => paar.Value);

            if (sauber.Count > 0) result[stageName] = sauber;
        }

        return result;
    }

    private SetpointProfileDto Shipped(string id) => new(
        id, NameFor(id), id, IsShipped: true, ChangedValueCount: 0, Stages: StagesFor(id, null));

    private SetpointProfileDto Own(SetpointProfile profile) => new(
        profile.ReferenceId, profile.Name, profile.BaseProfileId,
        IsShipped: false, profile.ChangedValueCount,
        Stages: StagesFor(profile.ReferenceId, profile));

    private static string NameFor(string id) => id switch
    {
        "rdwc-default" => "RDWC Standard",
        "dwc-default" => "DWC Standard",
        _ => id,
    };

    /// <summary>Die fertigen Werte je Phase — so, wie sie am Ende gelten.</summary>
    private List<StageValuesDto> StagesFor(string profileId, SetpointProfile? profile)
    {
        var result = new List<StageValuesDto>();
        foreach (var stage in Stages)
        {
            if (_targets.GetTargets(profileId, stage) is not { } t) continue;
            var geaendert = profile?.Overrides.TryGetValue(stage.ToString(), out var felder) == true
                ? felder.Keys.ToList()
                : [];
            var werte = SetpointProfile.WerteAus(t);
            result.Add(new StageValuesDto(stage.ToString(), werte, geaendert));
        }
        return result;
    }
}
