using System.Text.RegularExpressions;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>
/// Fork AI (A-003 Etappe B, 03.10.2026): Home Assistant über den Fork — lesen und
/// schalten für einen KI-Assistenten mit Schlüssel.
/// </summary>
/// <remarks>
/// <para><b>Wozu.</b> Ein Assistent soll mit EINEM Connector (Grow MCP Fork AI) den
/// Fork und Home Assistant bedienen, ohne dass ein eigener HA-MCP installiert sein
/// muss. Der Weg führt durch den Fork, damit dieselben Stufen, Höchstwerte und
/// dasselbe Prüfprotokoll gelten wie für jeden anderen Schlüsselweg
/// (<c>docs/ki-zugriff.md</c>, „Home Assistant über den Fork").</para>
///
/// <para><b>Jeder Weg verlangt einen Schlüssel</b> — auch die lesenden. Lesen aus
/// dem Add-on-Netz ist in Grow OS sonst ohne Schlüssel erlaubt; hier hiesse das,
/// dass jedes Nachbar-Add-on über Grow OS ALLE Zustände von Home Assistant
/// abgreifen könnte, mit dem Token des Forks. Ohne Kontext: 401
/// <c>ki_schluessel_fehlt</c>, wie <c>GET /api/ki-zugriff/ich</c>.</para>
///
/// <para><b>Dienste</b> tragen <see cref="KiStufe.GeraeteSchalten"/>: die Sperre
/// prüft die Stufe vor dem Controller und zählt jeden Aufruf ins Stundenfenster
/// der Schaltbefehle. Was je Domain zusätzlich gilt (Verwaltung oder nie), steht
/// in EINER Tabelle: <see cref="KiHaEinstufung"/>.</para>
/// </remarks>
[ApiController]
[Route("api/ki-ha")]
[Produces("application/json")]
public sealed class KiHomeAssistantApiController : ApiControllerBase
{
    public const int VorgabeAnzahl = 100;
    public const int HoechstAnzahl = 500;
    public const int VorgabeStunden = 24;
    public const int HoechstStunden = 168;

    /// <summary>Felder in <c>daten</c>, die ein Ziel setzen — das Ziel geht nur über <c>entityId</c>.</summary>
    /// <remarks>
    /// Sonst liefe die Prüfung „Entität passt zur Domain" ins Leere: <c>daten.entity_id</c>
    /// überschriebe <c>entityId</c> (siehe <see cref="HomeAssistantService.RufeEntitaetsDienstAsync"/>),
    /// und <c>area_id</c> schaltete einen ganzen Bereich auf einmal.
    /// </remarks>
    public static readonly IReadOnlySet<string> ZielFelder = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "entity_id", "device_id", "area_id", "floor_id", "label_id", "target",
    };

    // Kleinbuchstaben, Ziffern, Unterstrich — so benennt Home Assistant Domains,
    // Dienste und Objekt-IDs. Alles andere landete sonst im Pfad
    // api/services/{domain}/{dienst} (etwa „../states").
    private static readonly Regex NameMuster = new("^[a-z0-9_]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EntityMuster = new("^[a-z0-9_]+\\.[a-z0-9_]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly HomeAssistantService _ha;
    private readonly HomeAssistantSettingsRepository _haEinstellungen;

    public KiHomeAssistantApiController(HomeAssistantService ha, HomeAssistantSettingsRepository haEinstellungen)
    {
        _ha = ha;
        _haEinstellungen = haEinstellungen;
    }

    // ------------------------------------------------------------- lesen

    /// <summary>Die Bereiche (Areas) von Home Assistant.</summary>
    [HttpGet("bereiche")]
    [ProducesResponseType(typeof(IReadOnlyList<KiHaBereichDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<KiHaBereichDto>>> Bereiche(CancellationToken cancellationToken)
    {
        if (OhneSchluessel() is { } fehlt) return fehlt;
        if (NichtEingerichtet(out var einstellungen) is { } aus) return aus;

        var bereiche = await _ha.GetBereicheAsync(einstellungen, cancellationToken);
        if (bereiche is null) return NichtErreichbar("Die Bereiche konnten");

        return Ok(bereiche
            .Select(b => new KiHaBereichDto(b.Id, b.Name))
            .OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList());
    }

    /// <summary>Zustände der Entitäten, wahlweise gefiltert.</summary>
    /// <param name="bereich">Kennung oder Name eines Bereichs (ohne Rücksicht auf Groß-/Kleinschreibung).</param>
    /// <param name="domain">Etwa <c>sensor</c>.</param>
    /// <param name="suche">Teilstück der Entity-ID oder des Namens, ohne Rücksicht auf Groß-/Kleinschreibung.</param>
    /// <param name="anzahl">Höchstens so viele Einträge, Vorgabe 100, erlaubt 1–500.</param>
    [HttpGet("zustaende")]
    [ProducesResponseType(typeof(IReadOnlyList<KiHaZustandDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<KiHaZustandDto>>> Zustaende(
        [FromQuery] string? bereich,
        [FromQuery] string? domain,
        [FromQuery] string? suche,
        [FromQuery] int? anzahl,
        CancellationToken cancellationToken)
    {
        if (OhneSchluessel() is { } fehlt) return fehlt;

        var hoechstens = anzahl ?? VorgabeAnzahl;
        if (hoechstens is < 1 or > HoechstAnzahl)
        {
            ModelState.AddModelError("anzahl", $"Die Anzahl muss zwischen 1 und {HoechstAnzahl} liegen (Vorgabe {VorgabeAnzahl}).");
            return ValidationError("Die Abfrage der Zustände ist ungültig.");
        }

        if (NichtEingerichtet(out var einstellungen) is { } aus) return aus;

        var entitaeten = await _ha.GetEntitiesAsync(einstellungen, cancellationToken);
        // Leer UND Schutzschalter offen heisst: Home Assistant hat nicht geantwortet.
        // Ohne diese Unterscheidung bekäme der Assistent „keine Entitäten" und glaubte es.
        if (entitaeten.Count == 0 && _ha.UnreachableUntilUtc is not null) return NichtErreichbar("Die Zustände konnten");

        var bereiche = await _ha.GetBereicheAsync(einstellungen, cancellationToken);
        var filterBereich = string.IsNullOrWhiteSpace(bereich) ? null : bereich.Trim();
        if (filterBereich is not null && bereiche is null) return NichtErreichbar("Die Bereiche konnten");

        // Eine Entität kann über ihr Gerät und direkt einem Bereich zugeordnet sein;
        // Home Assistant führt sie dann nur unter einem. Der erste gewinnt.
        var bereichJeEntitaet = new Dictionary<string, HaBereich>(StringComparer.Ordinal);
        foreach (var b in bereiche ?? [])
        {
            foreach (var e in b.Entitaeten) bereichJeEntitaet.TryAdd(e, b);
        }

        var filterDomain = string.IsNullOrWhiteSpace(domain) ? null : domain.Trim();
        var filterSuche = string.IsNullOrWhiteSpace(suche) ? null : suche.Trim();

        var ergebnis = entitaeten
            .Where(e => filterDomain is null || string.Equals(e.Domain, filterDomain, StringComparison.OrdinalIgnoreCase))
            .Where(e => filterSuche is null
                        || e.EntityId.Contains(filterSuche, StringComparison.OrdinalIgnoreCase)
                        || (e.FriendlyName?.Contains(filterSuche, StringComparison.OrdinalIgnoreCase) ?? false))
            .Select(e => (Entitaet: e, Bereich: bereichJeEntitaet.GetValueOrDefault(e.EntityId)))
            .Where(x => filterBereich is null
                        || (x.Bereich is { } b
                            && (string.Equals(b.Id, filterBereich, StringComparison.OrdinalIgnoreCase)
                                || string.Equals(b.Name, filterBereich, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(x => x.Entitaet.EntityId, StringComparer.Ordinal)
            .Take(hoechstens)
            .Select(x => new KiHaZustandDto(
                x.Entitaet.EntityId,
                string.IsNullOrWhiteSpace(x.Entitaet.FriendlyName) ? x.Entitaet.EntityId : x.Entitaet.FriendlyName!,
                x.Entitaet.State,
                x.Entitaet.UnitOfMeasurement,
                x.Bereich?.Name,
                x.Entitaet.LastChangedUtc))
            .ToList();

        return Ok(ergebnis);
    }

    /// <summary>Der Verlauf einer Entität über die letzten Stunden.</summary>
    /// <param name="entityId">Genau eine Entität, etwa <c>sensor.zelt_temperatur</c>.</param>
    /// <param name="stunden">1–168, Vorgabe 24.</param>
    [HttpGet("verlauf")]
    [ProducesResponseType(typeof(KiHaVerlaufDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<KiHaVerlaufDto>> Verlauf(
        [FromQuery] string? entityId,
        [FromQuery] int? stunden,
        CancellationToken cancellationToken)
    {
        if (OhneSchluessel() is { } fehlt) return fehlt;

        var entitaet = entityId?.Trim() ?? string.Empty;
        if (!EntityMuster.IsMatch(entitaet))
        {
            ModelState.AddModelError("entityId", "Bitte genau eine Entität angeben, etwa „sensor.zelt_temperatur“.");
        }
        var dauer = stunden ?? VorgabeStunden;
        if (dauer is < 1 or > HoechstStunden)
        {
            ModelState.AddModelError("stunden", $"Die Stunden müssen zwischen 1 und {HoechstStunden} liegen (Vorgabe {VorgabeStunden}).");
        }
        if (!ModelState.IsValid) return ValidationError("Die Abfrage des Verlaufs ist ungültig.");

        if (NichtEingerichtet(out var einstellungen) is { } aus) return aus;

        var bis = DateTime.UtcNow;
        var punkte = await _ha.GetVerlaufAsync(einstellungen, entitaet, bis.AddHours(-dauer), bis, cancellationToken);
        if (punkte is null) return NichtErreichbar("Der Verlauf konnte");

        return Ok(new KiHaVerlaufDto(entitaet, punkte.Select(p => new KiHaVerlaufsPunktDto(p.ZeitUtc, p.Zustand)).ToList()));
    }

    // ----------------------------------------------------------- schalten

    /// <summary>Einen Dienst von Home Assistant rufen.</summary>
    /// <remarks>
    /// <para>Reihenfolge: Schlüssel, Form, Tabelle (nie → Verwaltung), Entität passt
    /// zur Domain, Ziele nur über <c>entityId</c>, Home Assistant eingerichtet —
    /// dann erst der Aufruf. Die Stufe <see cref="KiStufe.GeraeteSchalten"/> und das
    /// Stundenfenster hat die Sperre zu diesem Zeitpunkt schon geprüft.</para>
    /// <para>Antwortet Home Assistant, ist die Antwort immer 200 mit <c>erfolg</c>:
    /// abgelehnt und „Antwort blieb aus" sind Auskünfte über Home Assistant, kein
    /// Fehler dieses Aufrufs.</para>
    /// </remarks>
    [HttpPost("dienst")]
    [KiStufe(KiStufe.GeraeteSchalten)]
    [ProducesResponseType(typeof(KiHaDienstErgebnisDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<KiHaDienstErgebnisDto>> Dienst(
        [FromBody] KiHaDienstRequest? request,
        CancellationToken cancellationToken)
    {
        if (OhneSchluessel() is { } ohne) return ohne;
        var kontext = KiZugriffKontext.Aus(HttpContext)!;

        var domain = request?.Domain?.Trim() ?? string.Empty;
        var dienst = request?.Dienst?.Trim() ?? string.Empty;
        var entityId = string.IsNullOrWhiteSpace(request?.EntityId) ? null : request!.EntityId!.Trim();

        if (!NameMuster.IsMatch(domain))
        {
            ModelState.AddModelError("domain", "Bitte eine Domain angeben, etwa „light“ (Kleinbuchstaben, Ziffern, Unterstrich).");
        }
        if (!NameMuster.IsMatch(dienst))
        {
            ModelState.AddModelError("dienst", "Bitte einen Dienst angeben, etwa „turn_on“ (Kleinbuchstaben, Ziffern, Unterstrich).");
        }
        if (!ModelState.IsValid) return ValidationError("Der Dienstaufruf ist ungültig.");

        var regel = KiHaEinstufung.Einstufen(domain, dienst);
        if (regel.Urteil == KiHaUrteil.Nie)
        {
            return ForbiddenError("ki_kein_zugriff",
                $"{domain}.{dienst} ist über einen KI-Assistenten nie erreichbar: {regel.Grund}");
        }
        if (regel.Urteil == KiHaUrteil.Verwaltung && !kontext.Darf(KiStufe.Verwaltung))
        {
            var fehlt = KiZugriffSperre.StufeFehlt(KiStufe.Verwaltung);
            return ForbiddenError(fehlt.Code!, $"{fehlt.Meldung} ({domain}: {regel.Grund})");
        }

        if (entityId is not null
            && (!EntityMuster.IsMatch(entityId) || !entityId.StartsWith(domain + ".", StringComparison.Ordinal)))
        {
            ModelState.AddModelError("entityId",
                $"Die Entität muss genau eine der Domain „{domain}“ sein, etwa „{domain}.zelt“ — „{entityId}“ passt nicht.");
            return ValidationError("Der Dienstaufruf ist ungültig.");
        }

        var ziele = request?.Daten?.Keys.Where(ZielFelder.Contains).ToList() ?? [];
        if (ziele.Count > 0)
        {
            ModelState.AddModelError("daten",
                $"Ziele gehen nur über „entityId“, nicht über {string.Join(", ", ziele.Select(z => $"„{z}“"))} in den Daten.");
            return ValidationError("Der Dienstaufruf ist ungültig.");
        }

        if (NichtEingerichtet(out var einstellungen) is { } aus) return aus;

        var daten = request?.Daten?.ToDictionary(e => e.Key, e => (object)e.Value, StringComparer.Ordinal);
        var name = entityId is null ? $"{domain}.{dienst}" : $"{domain}.{dienst} für {entityId}";

        if (entityId is null)
        {
            var angenommen = await _ha.CallServiceAsync(einstellungen, domain, dienst, daten, cancellationToken);
            return Ok(angenommen
                ? new KiHaDienstErgebnisDto(true, $"{name}: von Home Assistant angenommen.")
                : new KiHaDienstErgebnisDto(false, $"{name}: von Home Assistant abgelehnt oder nicht erreichbar."));
        }

        var antwort = await _ha.RufeEntitaetsDienstAsync(einstellungen, domain, dienst, entityId, cancellationToken, daten);
        return Ok(antwort switch
        {
            HaDienstAntwort.Angenommen => new KiHaDienstErgebnisDto(true, $"{name}: von Home Assistant angenommen."),
            HaDienstAntwort.Unbestaetigt => new KiHaDienstErgebnisDto(false,
                $"{name}: gesendet, aber Home Assistant hat nicht rechtzeitig geantwortet. Ob es gewirkt hat, zeigt der Zustand der Entität."),
            _ => new KiHaDienstErgebnisDto(false, $"{name}: von Home Assistant abgelehnt oder nicht erreichbar."),
        });
    }

    // --------------------------------------------------------------- Hilfe

    /// <summary>401, wenn die Anfrage nicht über einen geprüften Schlüssel kam.</summary>
    private ActionResult? OhneSchluessel()
        => KiZugriffKontext.Aus(HttpContext) is not null
            ? null
            : StatusCode(StatusCodes.Status401Unauthorized, ApiErrorFactory.Create(
                "ki_schluessel_fehlt",
                "Home Assistant über Grow OS gibt es nur für einen KI-Assistenten mit Schlüssel: "
                + "Authorization: Bearer gok_… mitschicken.",
                StatusCodes.Status401Unauthorized,
                traceId: HttpContext?.TraceIdentifier));

    /// <summary>503, wenn Grow OS keine Verbindung zu Home Assistant eingerichtet hat.</summary>
    /// <remarks>Im Testbetrieb (<see cref="DemoData.IsEnabled"/>) antwortet der Dienst selbst.</remarks>
    private ActionResult? NichtEingerichtet(out HomeAssistantSettings einstellungen)
    {
        einstellungen = _haEinstellungen.GetEffectiveHomeAssistantSettings();
        if (DemoData.IsEnabled || einstellungen.IsConfigured) return null;

        return StatusCode(StatusCodes.Status503ServiceUnavailable, ApiErrorFactory.Create(
            "ha_nicht_eingerichtet",
            "Grow OS hat keine Verbindung zu Home Assistant eingerichtet (Einstellungen → Home Assistant).",
            StatusCodes.Status503ServiceUnavailable,
            traceId: HttpContext?.TraceIdentifier));
    }

    private ActionResult NichtErreichbar(string was)
        => StatusCode(StatusCodes.Status502BadGateway, ApiErrorFactory.Create(
            "ha_nicht_erreichbar",
            $"{was} nicht von Home Assistant geholt werden — es antwortet gerade nicht oder lehnt ab.",
            StatusCodes.Status502BadGateway,
            traceId: HttpContext?.TraceIdentifier));
}
