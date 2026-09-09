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
    private readonly HardwareRepository _hardware;

    public KostenApiController(
        KostenSeiteService seite,
        KostenRepository repo,
        JournalRepository journal,
        GrowRepository grows,
        HomeAssistantService ha,
        HomeAssistantSettingsRepository haSettings,
        HardwareRepository hardware)
    {
        _seite = seite;
        _repo = repo;
        _journal = journal;
        _grows = grows;
        _ha = ha;
        _haSettings = haSettings;
        _hardware = hardware;
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
        /// <summary>Anzeigename.</summary>
        public string Name { get; set; } = string.Empty;
        public string? Hersteller { get; set; }
        public string? Produkt { get; set; }
        /// <summary>Preis eines vollen Gebindes; belegt die Kosten beim Erfassen vor.</summary>
        public double? PreisEur { get; set; }
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
        if (request.PreisEur is < 0) return BadRequestError("preis_invalid", "Der Preis kann nicht negativ sein.");
        if (!VerbrauchsEinheiten.IstGueltig(request.Einheit)) return BadRequestError("einheit_invalid", $"Einheit muss eine von {string.Join(", ", VerbrauchsEinheiten.Alle)} sein.");
        var (herstellerA, produktA) = Angleichen(request.Hersteller, request.Produkt);
        var artikel = new Verbrauchsartikel { Name = request.Name, Hersteller = herstellerA, Produkt = produktA, PreisEur = request.PreisEur, Einheit = request.Einheit, Gebinde = request.Gebinde, TentId = request.TentId, Notiz = request.Notiz, Aktiv = request.Aktiv };
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
        if (request.PreisEur is < 0) return BadRequestError("preis_invalid", "Der Preis kann nicht negativ sein.");
        if (!VerbrauchsEinheiten.IstGueltig(request.Einheit)) return BadRequestError("einheit_invalid", $"Einheit muss eine von {string.Join(", ", VerbrauchsEinheiten.Alle)} sein.");
        var (herstellerU, produktU) = Angleichen(request.Hersteller, request.Produkt);
        artikel.Name = request.Name;
        artikel.Hersteller = herstellerU;
        artikel.Produkt = produktU;
        artikel.PreisEur = request.PreisEur;
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
        /// <summary>Ausdrücklich keinem Grow zuordnen („Lager"); ohne dieses Flag gilt bei leerer GrowId der laufende Grow.</summary>
        public bool OhneGrow { get; set; }
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

        var growId = request.OhneGrow ? null : request.GrowId ?? _seite.LaufenderGrow(zeitpunkt.ToLocalTime().Date).GrowId;
        if (growId is { } gidPruef && _grows.GetGrow(gidPruef) is null) return BadRequestError("grow_not_found", $"Grow {gidPruef} existiert nicht.");

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

    // ---------------------------------------------------- Anschaffungen (forkai.9)

    public sealed class AnschaffungRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Hersteller { get; set; }
        public string? Produkt { get; set; }
        /// <summary>Ortszeit oder ISO mit Offset; leer = jetzt.</summary>
        public DateTime? Datum { get; set; }
        public int Stueck { get; set; } = 1;
        public double EinzelpreisEur { get; set; }
        public int? GrowId { get; set; }
        /// <summary>Ausdrücklich keinem Grow zuordnen („Lager").</summary>
        public bool OhneGrow { get; set; }
        public string? Notiz { get; set; }
        /// <summary>Beim Anlegen zusätzlich einen Hardware-Artikel unter Sensoren &amp; Wartung erzeugen.</summary>
        public bool AlsHardware { get; set; }
        /// <summary>Einen Journal-Eintrag im Grow anlegen.</summary>
        public bool Journal { get; set; } = true;
    }

    private ActionResult? AnschaffungPruefen(AnschaffungRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequestError("name_missing", "Die Anschaffung braucht einen Namen.");
        if (request.Stueck <= 0) return BadRequestError("stueck_invalid", "Stückzahl muss mindestens 1 sein.");
        if (request.EinzelpreisEur < 0) return BadRequestError("preis_invalid", "Der Preis kann nicht negativ sein.");
        return null;
    }

    [HttpPost("anschaffungen")]
    [ProducesResponseType(typeof(Anschaffung), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public ActionResult<Anschaffung> CreateAnschaffung([FromBody] AnschaffungRequest request)
    {
        if (AnschaffungPruefen(request) is { } fehler) return fehler;
        var datum = ZuUtc(request.Datum);
        var growId = request.OhneGrow ? null : request.GrowId ?? _seite.LaufenderGrow(datum.ToLocalTime().Date).GrowId;
        if (growId is { } gidPruef && _grows.GetGrow(gidPruef) is null) return BadRequestError("grow_not_found", $"Grow {gidPruef} existiert nicht.");

        var (herstellerN, produktN) = Angleichen(request.Hersteller, request.Produkt);
        int? hardwareId = null;
        if (request.AlsHardware)
        {
            // Ein Hardware-Artikel ist das Werkzeug im Inventar — Kategorie
            // „Zubehör", damit er unter Sensoren & Wartung auffindbar ist.
            var item = _hardware.CreateHardwareItem(new HardwareItem
            {
                Name = request.Name.Trim(),
                Category = "Zubehör",
                Manufacturer = herstellerN,
                Model = produktN,
                GrowId = growId,
                InstalledAtUtc = datum,
            });
            hardwareId = item.Id;
        }

        var a = new Anschaffung
        {
            Name = request.Name, Hersteller = herstellerN, Produkt = produktN,
            DatumUtc = datum, Stueck = request.Stueck, EinzelpreisEur = request.EinzelpreisEur,
            GrowId = growId, Notiz = request.Notiz, HardwareItemId = hardwareId,
        };
        a.Id = _repo.CreateAnschaffung(a);

        if (request.Journal && growId is { } gid && _grows.GetGrow(gid) is not null)
        {
            var de = CultureInfo.GetCultureInfo("de-DE");
            var teile = new List<string> { $"{a.Stueck} × {a.EinzelpreisEur.ToString("0.00", de)} € = {a.GesamtEur.ToString("0.00", de)} €" };
            var herkunft = string.Join(" ", new[] { herstellerN, produktN }.Where(t => t is not null));
            if (herkunft.Length > 0) teile.Add(herkunft);
            if (!string.IsNullOrWhiteSpace(request.Notiz)) teile.Add(request.Notiz.Trim());
            _journal.Create(new JournalEntry
            {
                GrowId = gid,
                Title = $"{a.Name.Trim()} angeschafft",
                Body = string.Join(" · ", teile),
                EntryType = JournalEntryType.Action,
                Source = ValueOrigin.Manual,
                OccurredAtUtc = datum,
            });
        }

        return Created($"/api/kosten/anschaffungen/{a.Id}", _repo.GetAnschaffung(a.Id));
    }

    [HttpPut("anschaffungen/{id:int}")]
    [ProducesResponseType(typeof(Anschaffung), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public ActionResult<Anschaffung> UpdateAnschaffung(int id, [FromBody] AnschaffungRequest request)
    {
        var a = _repo.GetAnschaffung(id);
        if (a is null) return NotFoundError("anschaffung_not_found", $"Anschaffung {id} existiert nicht.");
        if (AnschaffungPruefen(request) is { } fehler) return fehler;
        var growId = request.OhneGrow ? null : request.GrowId ?? a.GrowId;
        if (growId is { } gidPruef && _grows.GetGrow(gidPruef) is null) return BadRequestError("grow_not_found", $"Grow {gidPruef} existiert nicht.");
        var (herstellerAU, produktAU) = Angleichen(request.Hersteller, request.Produkt);
        a.Name = request.Name;
        a.Hersteller = herstellerAU;
        a.Produkt = produktAU;
        a.DatumUtc = request.Datum is null ? a.DatumUtc : ZuUtc(request.Datum);
        a.Stueck = request.Stueck;
        a.EinzelpreisEur = request.EinzelpreisEur;
        a.GrowId = growId;
        a.Notiz = request.Notiz;
        _repo.UpdateAnschaffung(a);
        return Ok(_repo.GetAnschaffung(id));
    }

    [HttpDelete("anschaffungen/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public IActionResult DeleteAnschaffung(int id)
    {
        if (_repo.GetAnschaffung(id) is null) return NotFoundError("anschaffung_not_found", $"Anschaffung {id} existiert nicht.");
        _repo.DeleteAnschaffung(id);
        return NoContent();
    }

    private static string? Leer(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Hersteller/Produkt an die vorhandene Schreibweise angleichen (forkai.11).</summary>
    private (string? Hersteller, string? Produkt) Angleichen(string? hersteller, string? produkt)
    {
        var artikel = _repo.GetArtikel();
        var anschaffungen = _repo.GetAnschaffungen();
        var hardware = _hardware.GetHardwareItems();
        var bekannteHersteller = artikel.Select(a => a.Hersteller).Concat(anschaffungen.Select(a => a.Hersteller)).Concat(hardware.Select(h => h.Manufacturer)).Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h!);
        var bekannteProdukte = artikel.Select(a => a.Produkt).Concat(anschaffungen.Select(a => a.Produkt)).Concat(hardware.Select(h => h.Model)).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!);
        return (Stammdaten.Angleichen(hersteller, bekannteHersteller), Stammdaten.Angleichen(produkt, bekannteProdukte));
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
