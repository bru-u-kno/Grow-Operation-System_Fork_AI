using System.Globalization;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>Fork AI (forkai.6): Die Kosten-Seite — Strom vom Zähler, Verbrauchsartikel mit Nachfüllungen.</summary>
[ApiController]
[Route("api/kosten")]
[Produces("application/json")]
public sealed class KostenApiController : ApiControllerBase
{
    private readonly KostenSeiteService _seite;
    private readonly KostenRepository _repo;
    private readonly JournalRepository _journal;
    private readonly GrowRepository _grows;
    private readonly HomeAssistantService _ha;
    private readonly HomeAssistantSettingsRepository _haSettings;

    public KostenApiController(
        KostenSeiteService seite,
        KostenRepository repo,
        JournalRepository journal,
        GrowRepository grows,
        HomeAssistantService ha,
        HomeAssistantSettingsRepository haSettings)
    {
        _seite = seite;
        _repo = repo;
        _journal = journal;
        _grows = grows;
        _ha = ha;
        _haSettings = haSettings;
    }

    // ------------------------------------------------------------- Seite

    [HttpGet]
    [ProducesResponseType(typeof(KostenSeite), StatusCodes.Status200OK)]
    public async Task<ActionResult<KostenSeite>> Get([FromQuery] int? growId, CancellationToken ct)
        => Ok(await _seite.FuerGrowAsync(growId, ct));

    // -------------------------------------------------------- Strom-Quelle

    [HttpGet("strom-quelle")]
    [ProducesResponseType(typeof(StromQuelle), StatusCodes.Status200OK)]
    public ActionResult<StromQuelle> GetStromQuelle() => Ok(_seite.StromQuelle);

    [HttpPut("strom-quelle")]
    [ProducesResponseType(typeof(StromQuelle), StatusCodes.Status200OK)]
    public async Task<ActionResult<StromQuelle>> PutStromQuelle([FromBody] StromQuelle request, CancellationToken ct)
    {
        _seite.StromQuelle = request;
        // Sofort einen Stand holen: sonst wartet die Seite bis zu 10 Minuten
        // auf den Worker und zeigt derweil „noch kein Zählerstand".
        await _seite.ZaehlerstandFesthaltenAsync(ZaehlerAnlass.Manuell, ct);
        return Ok(_seite.StromQuelle);
    }

    public sealed record EntitaetTest(string EntityId, bool Gefunden, string? State, double? Wert, string? Einheit, string? Name);

    /// <summary>Was Home Assistant für eine Entität gerade liefert — zum Prüfen vor dem Speichern.</summary>
    [HttpGet("entitaet")]
    [ProducesResponseType(typeof(EntitaetTest), StatusCodes.Status200OK)]
    public async Task<ActionResult<EntitaetTest>> TestEntitaet([FromQuery] string entityId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entityId)) return BadRequestError("entity_missing", "Entitäts-Kennung fehlt.");
        var state = await _ha.GetEntityStateAsync(_haSettings.GetEffectiveHomeAssistantSettings(), entityId.Trim(), ct);
        return Ok(new EntitaetTest(entityId.Trim(), state is not null, state?.State, state?.NumericValue, state?.UnitOfMeasurement, state?.FriendlyName));
    }

    [HttpPost("zaehlerstand")]
    [ProducesResponseType(typeof(Zaehlerstand), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Zaehlerstand>> ZaehlerstandJetzt(CancellationToken ct)
    {
        var stand = await _seite.ZaehlerstandFesthaltenAsync(ZaehlerAnlass.Manuell, ct);
        return stand is null
            ? BadRequestError("meter_unavailable", "Kein Zählerstand: Strom-Quelle fehlt oder Home Assistant liefert keinen Zahlenwert.")
            : Ok(stand);
    }

    [HttpGet("zaehlerstaende")]
    [ProducesResponseType(typeof(IReadOnlyList<Zaehlerstand>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<Zaehlerstand>> GetZaehlerstaende() => Ok(_repo.GetZaehlerstaende());

    // ------------------------------------------------------------ Artikel

    public sealed class ArtikelRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Einheit { get; set; } = "kg";
        public double? Gebinde { get; set; }
        public int? TentId { get; set; }
        public string? Notiz { get; set; }
        public bool Aktiv { get; set; } = true;
    }

    [HttpGet("artikel")]
    [ProducesResponseType(typeof(IReadOnlyList<Verbrauchsartikel>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<Verbrauchsartikel>> GetArtikel() => Ok(_repo.GetArtikel());

    [HttpPost("artikel")]
    [ProducesResponseType(typeof(Verbrauchsartikel), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public ActionResult<Verbrauchsartikel> CreateArtikel([FromBody] ArtikelRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequestError("name_missing", "Der Artikel braucht einen Namen.");
        if (request.Gebinde is <= 0) return BadRequestError("gebinde_invalid", "Das Gebinde muss größer als 0 sein.");
        var artikel = new Verbrauchsartikel { Name = request.Name, Einheit = request.Einheit, Gebinde = request.Gebinde, TentId = request.TentId, Notiz = request.Notiz, Aktiv = request.Aktiv };
        artikel.Id = _repo.CreateArtikel(artikel);
        return Created($"/api/kosten/artikel/{artikel.Id}", _repo.GetArtikel(artikel.Id));
    }

    [HttpPut("artikel/{id:int}")]
    [ProducesResponseType(typeof(Verbrauchsartikel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<Verbrauchsartikel> UpdateArtikel(int id, [FromBody] ArtikelRequest request)
    {
        var artikel = _repo.GetArtikel(id);
        if (artikel is null) return NotFoundError("artikel_not_found", $"Verbrauchsartikel {id} existiert nicht.");
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequestError("name_missing", "Der Artikel braucht einen Namen.");
        if (request.Gebinde is <= 0) return BadRequestError("gebinde_invalid", "Das Gebinde muss größer als 0 sein.");
        artikel.Name = request.Name;
        artikel.Einheit = request.Einheit;
        artikel.Gebinde = request.Gebinde;
        artikel.TentId = request.TentId;
        artikel.Notiz = request.Notiz;
        artikel.Aktiv = request.Aktiv;
        _repo.UpdateArtikel(artikel);
        return Ok(_repo.GetArtikel(id));
    }

    [HttpDelete("artikel/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public IActionResult DeleteArtikel(int id)
    {
        if (_repo.GetArtikel(id) is null) return NotFoundError("artikel_not_found", $"Verbrauchsartikel {id} existiert nicht.");
        _repo.DeleteArtikel(id);
        return NoContent();
    }

    // ------------------------------------------------------ Nachfüllungen

    public sealed class NachfuellungRequest
    {
        public int ArtikelId { get; set; }
        /// <summary>Ortszeit oder ISO mit Offset; leer = jetzt.</summary>
        public DateTime? Zeitpunkt { get; set; }
        public double Menge { get; set; }
        public double? KostenEur { get; set; }
        public int? GrowId { get; set; }
        public string? Notiz { get; set; }
        /// <summary>Die bisher offene Füllung dieses Artikels mit diesem Zeitpunkt als leer schließen.</summary>
        public bool VorherigeLeer { get; set; } = true;
        /// <summary>Einen Journal-Eintrag im Grow anlegen.</summary>
        public bool Journal { get; set; } = true;
    }

    [HttpPost("nachfuellungen")]
    [ProducesResponseType(typeof(Nachfuellung), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public ActionResult<Nachfuellung> CreateNachfuellung([FromBody] NachfuellungRequest request)
    {
        var artikel = _repo.GetArtikel(request.ArtikelId);
        if (artikel is null) return BadRequestError("artikel_not_found", "Der Verbrauchsartikel existiert nicht.");
        if (request.Menge <= 0) return BadRequestError("menge_invalid", "Die Menge muss größer als 0 sein.");
        if (request.KostenEur is < 0) return BadRequestError("kosten_invalid", "Die Kosten dürfen nicht negativ sein.");

        var zeitpunkt = ZuUtc(request.Zeitpunkt);
        if (zeitpunkt > DateTime.UtcNow.AddMinutes(5)) return BadRequestError("zeitpunkt_future", "Der Zeitpunkt liegt in der Zukunft.");

        var growId = request.GrowId ?? _seite.LaufenderGrow(zeitpunkt.ToLocalTime().Date).GrowId;

        double? vorherigeLaufzeit = null;
        if (request.VorherigeLeer)
        {
            var offen = _repo.GetNachfuellungen(artikel.Id).Where(f => f.LeerAmUtc is null && f.ZeitpunktUtc < zeitpunkt).ToList();
            foreach (var f in offen)
            {
                f.LeerAmUtc = zeitpunkt;
                _repo.UpdateNachfuellung(f);
                vorherigeLaufzeit = (zeitpunkt - f.ZeitpunktUtc).TotalDays;
            }
        }

        var fuellung = new Nachfuellung
        {
            ArtikelId = artikel.Id,
            ZeitpunktUtc = zeitpunkt,
            Menge = request.Menge,
            KostenEur = request.KostenEur,
            GrowId = growId,
            Notiz = request.Notiz,
        };
        fuellung.Id = _repo.CreateNachfuellung(fuellung);

        if (request.Journal && growId is { } gid && _grows.GetGrow(gid) is not null)
        {
            var de = CultureInfo.GetCultureInfo("de-DE");
            var teile = new List<string> { $"{request.Menge.ToString("0.##", de)} {artikel.Einheit}" };
            if (request.KostenEur is { } k) teile.Add($"{k.ToString("0.00", de)} €");
            if (vorherigeLaufzeit is { } l) teile.Add($"vorherige Füllung hielt {Math.Round(l)} Tage");
            if (!string.IsNullOrWhiteSpace(request.Notiz)) teile.Add(request.Notiz.Trim());
            _journal.Create(new JournalEntry
            {
                GrowId = gid,
                Title = $"{artikel.Name} nachgefüllt",
                Body = string.Join(" · ", teile),
                EntryType = JournalEntryType.Action,
                Source = ValueOrigin.Manual,
                OccurredAtUtc = zeitpunkt,
            });
        }

        return Created($"/api/kosten/nachfuellungen/{fuellung.Id}", _repo.GetNachfuellung(fuellung.Id));
    }

    public sealed class NachfuellungUpdateRequest
    {
        public DateTime? Zeitpunkt { get; set; }
        public double Menge { get; set; }
        public double? KostenEur { get; set; }
        public int? GrowId { get; set; }
        public string? Notiz { get; set; }
        public DateTime? LeerAm { get; set; }
    }

    [HttpPut("nachfuellungen/{id:int}")]
    [ProducesResponseType(typeof(Nachfuellung), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<Nachfuellung> UpdateNachfuellung(int id, [FromBody] NachfuellungUpdateRequest request)
    {
        var f = _repo.GetNachfuellung(id);
        if (f is null) return NotFoundError("nachfuellung_not_found", $"Nachfüllung {id} existiert nicht.");
        if (request.Menge <= 0) return BadRequestError("menge_invalid", "Die Menge muss größer als 0 sein.");
        if (request.KostenEur is < 0) return BadRequestError("kosten_invalid", "Die Kosten dürfen nicht negativ sein.");
        f.ZeitpunktUtc = request.Zeitpunkt is null ? f.ZeitpunktUtc : ZuUtc(request.Zeitpunkt);
        f.Menge = request.Menge;
        f.KostenEur = request.KostenEur;
        f.GrowId = request.GrowId;
        f.Notiz = request.Notiz;
        f.LeerAmUtc = request.LeerAm is null ? null : ZuUtc(request.LeerAm);
        if (f.LeerAmUtc is { } leer && leer < f.ZeitpunktUtc) return BadRequestError("leer_before_start", "„Leer am“ liegt vor dem Zeitpunkt der Füllung.");
        _repo.UpdateNachfuellung(f);
        return Ok(_repo.GetNachfuellung(id));
    }

    public sealed class LeerRequest
    {
        public DateTime? Zeitpunkt { get; set; }
    }

    [HttpPost("nachfuellungen/{id:int}/leer")]
    [ProducesResponseType(typeof(Nachfuellung), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<Nachfuellung> AlsLeerMarkieren(int id, [FromBody] LeerRequest? request)
    {
        var f = _repo.GetNachfuellung(id);
        if (f is null) return NotFoundError("nachfuellung_not_found", $"Nachfüllung {id} existiert nicht.");
        var zeitpunkt = ZuUtc(request?.Zeitpunkt);
        if (zeitpunkt < f.ZeitpunktUtc) return BadRequestError("leer_before_start", "„Leer am“ liegt vor dem Zeitpunkt der Füllung.");
        f.LeerAmUtc = zeitpunkt;
        _repo.UpdateNachfuellung(f);
        return Ok(_repo.GetNachfuellung(id));
    }

    [HttpDelete("nachfuellungen/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public IActionResult DeleteNachfuellung(int id)
    {
        if (_repo.GetNachfuellung(id) is null) return NotFoundError("nachfuellung_not_found", $"Nachfüllung {id} existiert nicht.");
        _repo.DeleteNachfuellung(id);
        return NoContent();
    }

    /// <summary>Ein Zeitpunkt aus dem Formular: ohne Kennzeichnung gilt Ortszeit des Add-ons.</summary>
    private static DateTime ZuUtc(DateTime? wert)
    {
        if (wert is null) return DateTime.UtcNow;
        var w = wert.Value;
        return w.Kind switch
        {
            DateTimeKind.Utc => w,
            DateTimeKind.Local => w.ToUniversalTime(),
            _ => DateTime.SpecifyKind(w, DateTimeKind.Local).ToUniversalTime(),
        };
    }
}
