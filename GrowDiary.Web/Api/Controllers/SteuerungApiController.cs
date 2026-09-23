using System.Globalization;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>
/// Fork AI (forkai.20): Die Seite „Steuerung" — Übersicht aller Regelungen des
/// Zelts und das CO₂-Modul mit Sollwerten, Livebild und Tageswerten.
/// </summary>
/// <remarks>
/// <para><b>Ein Rahmen, viele Module.</b> Die Übersicht liefert je Modul eine
/// Kennung, einen Titel, eine Kurzzeile und einen Wert. Heute ist nur CO₂ ein
/// vollständiges Modul mit eigener Detailseite; Entfeuchter, Chiller, Abluft
/// und Licht stehen als Anzeige-Zeilen da, damit die Übersicht das ganze Zelt
/// zeigt und ein späteres Modul nur noch seine Detailseite mitbringen muss.</para>
/// </remarks>
[ApiController]
[Route("api/steuerung")]
[Produces("application/json")]
public sealed class SteuerungApiController : ApiControllerBase
{
    private readonly Co2SteuerungService _co2;
    private readonly LichtSteuerungService _licht;
    private readonly ZuluftSteuerungService _zuluft;
    private readonly ChillerSteuerungService _chiller;
    private readonly EntfeuchterSteuerungService _entfeuchter;
    private readonly GrowRepository _grows;
    private readonly HomeAssistantService _ha;
    private readonly HomeAssistantSettingsRepository _haSettings;
    private readonly KostenRepository _kosten;
    private readonly SteuerungGeraeteService _geraete;
    private readonly SteuerungBestandService _bestand;
    private readonly SteuerungHelferService _helfer;
    private readonly SteuerungRechenwertService _rechenwerte;
    private readonly SteuerungAutomationService _automationen;
    private readonly SteuerungProbeService _probe;

    public SteuerungApiController(Co2SteuerungService co2, LichtSteuerungService licht, ZuluftSteuerungService zuluft, ChillerSteuerungService chiller, EntfeuchterSteuerungService entfeuchter, GrowRepository grows, HomeAssistantService ha, HomeAssistantSettingsRepository haSettings, KostenRepository kosten, SteuerungGeraeteService geraete, SteuerungBestandService bestand, SteuerungHelferService helfer, SteuerungRechenwertService rechenwerte, SteuerungAutomationService automationen, SteuerungProbeService probe)
    {
        _co2 = co2;
        _licht = licht;
        _zuluft = zuluft;
        _chiller = chiller;
        _entfeuchter = entfeuchter;
        _grows = grows;
        _ha = ha;
        _haSettings = haSettings;
        _kosten = kosten;
        _geraete = geraete;
        _bestand = bestand;
        _helfer = helfer;
        _rechenwerte = rechenwerte;
        _automationen = automationen;
        _probe = probe;
    }

    // ------------------------------------------------------------ Übersicht

    [HttpGet]
    [ProducesResponseType(typeof(SteuerungUebersichtDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SteuerungUebersichtDto>> Uebersicht(CancellationToken ct)
    {
        var live = await _co2.LiveAsync(ct);
        var licht = await _licht.LiveAsync(ct);
        var zuluft = await _zuluft.LiveAsync(ct);
        var chiller = await _chiller.LiveAsync(ct);
        var entfeuchter = await _entfeuchter.LiveAsync(ct);
        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        var entities = await _ha.GetEntitiesAsync(settings, ct);
        var nachId = entities.ToDictionary(x => x.EntityId, x => x, StringComparer.OrdinalIgnoreCase);
        var de = CultureInfo.GetCultureInfo("de-DE");

        string? Text(string id) => nachId.TryGetValue(id, out var s) ? s.State : null;
        double? Zahl(string id) => double.TryParse(Text(id), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        string F(double? v, string einheit, string format = "0") => v is { } x ? x.ToString(format, de) + einheit : "–";

        // Die Absenkung haengt am laufenden Grow, das Zielgeraet am Zelt — beides
        // gehoert dem Entwickler, wir lesen es nur.
        var zelte = _grows.GetTents();
        var absenkungAn = _grows.GetActiveGrows().Any(g => g.NightRampEnabled);
        var zielgeraet = zelte
            .Select(z => z.WaterTargetEntityId)
            .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));

        var module = new List<SteuerungModulDto>
        {
            new(
                Kennung: "co2",
                Titel: "CO₂-Begasung",
                Status: live.AutomatikAn == false ? "aus" : live.KlimaOk == false ? "warn" : "an",
                Kurz: live.ZielQuelle == "plan" && live.PlanPpm is { } p
                    ? $"Plan {p} ppm · Ziel {live.ZielPpm?.ToString(de) ?? "–"} · {(live.AutomatikAn == true ? "Automatik an" : "Automatik aus")}"
                    : $"Fest · Ziel {live.ZielPpm?.ToString(de) ?? "–"} · {(live.AutomatikAn == true ? "Automatik an" : "Automatik aus")}",
                Wert: F(live.Co2Ppm, " ppm"),
                Unterzeile: $"Ventil {(live.VentilOffen == true ? "offen" : "zu")} · T6 Stufe {live.T6Stufe?.ToString(de) ?? "–"}",
                HatDetail: true),
            new(
                Kennung: "entfeuchter",
                Titel: "Entfeuchter",
                Status: entfeuchter.AutomatikAn == false ? "aus" : entfeuchter.PortAn == true ? "an" : "aus",
                Kurz: $"{(Text(EntfeuchterSteuerungService.Entitaeten.VpdRegelung) == "on" ? "VPD-Modus" : "Fest")} · ein ab {F(entfeuchter.EinAktivProzent, " %", "0.0")} · aus unter {F(entfeuchter.AusAktivProzent, " %", "0.0")}",
                Wert: F(entfeuchter.FeuchteProzent, " %", "0.0"),
                Unterzeile: $"{(entfeuchter.PortAn == true ? "entfeuchtet" : "bereit")} · VPD {F(entfeuchter.Vpd, "", "0.00")}",
                HatDetail: true),
            new(
                Kennung: "chiller",
                Titel: "Water Chiller",
                // Der Zustand kommt vom SCHALTER, nicht vom Kuehlbedarf: die
                // Zeile meldete „kuehlt", waehrend die Steckdose an einer
                // Schaltsperre haengen blieb — der Bedarf ist der Wunsch, nicht
                // die Tat.
                // Fork AI (Chiller-Ansteuerung): ein Sollwert-Gerät regelt
                // selbst — dann zählt, ob es eingeschaltet ist, nicht die Steckdose.
                Status: chiller.AutomatikAn == false ? "aus"
                    : chiller.Ansteuerung is ChillerAnsteuerung.Regelbar or ChillerAnsteuerung.Beides
                        ? (chiller.KuehlerZustand is null or "off" or "unavailable" or "unknown" ? "aus" : "an")
                        : chiller.SteckdoseAn == true ? "an" : "aus",
                Kurz: $"Tag {F(chiller.ZielTagC, " °C", "0.0")} · Nacht {F(chiller.ZielNachtC, " °C", "0.0")} · {(chiller.AutomatikAn == true ? "Automatik an" : "Automatik aus")}",
                Wert: F(chiller.WasserC, " °C", "0.0"),
                Unterzeile: chiller.Ansteuerung is ChillerAnsteuerung.Regelbar or ChillerAnsteuerung.Beides
                    ? $"regelt selbst · Soll {F(chiller.KuehlerSollC, " °C", "0.0")}"
                    : chiller.SteckdoseAn == true
                        ? "kühlt"
                        : chiller.Kuehlbedarf == true ? "wartet auf Schaltsperre" : "bereit",
                HatDetail: true),
            // Fork AI: Crop Steering gehoert thematisch hierher — die Absenkung
            // fuehrt dasselbe Zielpaar, das der Kuehler abarbeitet. Die Zeile
            // verweist auf die Seite des Entwicklers (zweite Route, keine
            // Kopie), damit beide Haelften an einem Ort stehen.
            new(
                Kennung: "cropsteering",
                Titel: "Crop Steering",
                Status: absenkungAn ? "an" : "aus",
                Kurz: absenkungAn
                    ? "Absenkung führt die Wassertemperatur über den Tag"
                    : "Absenkung aus · das Ziel führt der Plan",
                Wert: F(chiller.ZielAktivC, " °C", "0.0"),
                Unterzeile: zielgeraet is { } ziel ? $"schreibt {ziel}" : "kein Zielgerät zugeordnet",
                HatDetail: true),
            new(
                Kennung: "abluft",
                Titel: "Abluft T6",
                Status: live.KlimaOk == false ? "warn" : "an",
                Kurz: $"Klima hat Vorrang · Stufen {Zahl(Co2SteuerungService.Entitaeten.T6Normal)?.ToString("0", de) ?? "–"} / {Zahl(Co2SteuerungService.Entitaeten.T6Dosierung)?.ToString("0", de) ?? "–"} / {Zahl(Co2SteuerungService.Entitaeten.T6Tief)?.ToString("0", de) ?? "–"}",
                Wert: $"Stufe {live.T6Stufe?.ToString(de) ?? "–"}",
                Unterzeile: live.KlimaOk == true ? "gedrosselt" : "Klima gesperrt",
                HatDetail: false),
            new(
                Kennung: "zuluft",
                Titel: "Zuluft Keller",
                Status: zuluft.AutomatikAn == false ? "aus" : zuluft.PortAn == true ? "an" : "aus",
                Kurz: $"Ansaugen ab {F(Zahl(ZuluftSteuerungService.Entitaeten.MindestDifferenz), " g/m³", "0.0")} · {(zuluft.AutomatikAn == true ? "Automatik an" : "Automatik aus")}",
                Wert: F(zuluft.DifferenzGm3, " g/m³", "0.00"),
                Unterzeile: zuluft.PortAn == true
                    ? $"saugt · Stufe {zuluft.IstStufe?.ToString(de) ?? "–"}"
                    : zuluft.Bedarf == true ? "wartet auf Schaltsperre"
                    : zuluft.PauseZeltKalt == true ? "pausiert · Zelt zu kalt" : "bereit",
                HatDetail: true),
            new(
                Kennung: "licht",
                Titel: "Licht LED Top",
                Status: licht.Fehlgeschlagen.Count > 0 ? "warn" : licht.LichtAn == true ? "an" : "aus",
                Kurz: LichtKurz(licht),
                Wert: licht.LichtAn == true ? "an" : "aus",
                Unterzeile: licht.NaechsterWechsel ?? (licht.LichtAn == true ? "Lichtphase" : "Dunkelphase"),
                HatDetail: true),
        };

        return Ok(new SteuerungUebersichtDto(live.HaErreichbar, module, DateTime.UtcNow));
    }

    // ------------------------------------------------------------------ CO₂

    [HttpGet("co2")]
    [ProducesResponseType(typeof(Co2SeiteDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<Co2SeiteDto>> Co2(CancellationToken ct)
    {
        var live = await _co2.LiveAsync(ct);
        var tage = _co2.LetzteTage(14);
        var artikel = _kosten.GetArtikel(nurAktive: true)
            .Select(a => new KostenArtikelKurzDto(a.Id, a.Name, a.Einheit))
            .ToList();
        var (grow, stage) = _co2.LaufenderGrow();
        var geraete = _geraete.EntitiesFuerModul(Co2SteuerungService.Modul);
        return Ok(new Co2SeiteDto(
            _co2.Einstellungen,
            live,
            tage.Select(ToDto).ToList(),
            artikel,
            grow?.Name,
            stage is { } st ? Phasenname.Fuer(st) : null,
            _co2.PlanZielPpm(),
            _co2.PlanHerkunft())
        {
            GeraeteZugeordnet = geraete.Values.Count(entity => !string.IsNullOrWhiteSpace(entity)),
            GeraeteGesamt = geraete.Count,
        });
    }

    [HttpPut("co2")]
    [ProducesResponseType(typeof(Co2SeiteDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<Co2SeiteDto>> Co2Speichern([FromBody] Co2Einstellungen request, CancellationToken ct)
    {
        if (request is null) return BadRequestError("co2_invalid", "Es wurde nichts übergeben.");
        var (gespeichert, fehler, erreicht) = await _co2.SpeichernAsync(request, ct);
        if (gespeichert is null)
        {
            foreach (var (feld, meldung) in fehler) ModelState.AddModelError(feld, meldung);
            return ValidationError();
        }
        var seite = await Co2(ct);
        if (seite.Result is OkObjectResult ok && ok.Value is Co2SeiteDto dto)
        {
            return Ok(dto with { HaAngenommen = erreicht });
        }
        return seite;
    }

    // --------------------------------------------------------------- Zuluft

    /// <summary>
    /// Fork AI (forkai.76): Die Zuluft-Seite — Sollwerte und das Livebild der
    /// Kellerzuluft. Geregelt wird weiter in Home Assistant.
    /// </summary>
    [HttpGet("zuluft")]
    [ProducesResponseType(typeof(ZuluftSeiteDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ZuluftSeiteDto>> Zuluft(CancellationToken ct)
    {
        var geraete = _geraete.EntitiesFuerModul(ZuluftSteuerungService.Modul);
        return Ok(new ZuluftSeiteDto(await _zuluft.EinstellungenAsync(ct), await _zuluft.LiveAsync(ct))
        {
            // Solange nichts gespeichert ist, stehen die Werte der vorhandenen
            // Helfer in der Seite. Die Seite sagt das, damit niemand sie für
            // Werkseinstellungen haelt und blind speichert.
            AusHomeAssistantUebernommen = _zuluft.Gespeichert is null,
            GeraeteZugeordnet = geraete.Count(g => g.Value is not null),
            GeraeteGesamt = SteuerungGeraeteRollen.FuerModul(ZuluftSteuerungService.Modul).Count,
        });
    }

    [HttpPut("zuluft")]
    [ProducesResponseType(typeof(ZuluftSeiteDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ZuluftSeiteDto>> ZuluftSpeichern([FromBody] ZuluftEinstellungen request, CancellationToken ct)
    {
        if (request is null) return BadRequestError("zuluft_invalid", "Es wurde nichts übergeben.");
        var (gespeichert, fehler, erreicht) = await _zuluft.SpeichernAsync(request, ct);
        if (gespeichert is null)
        {
            foreach (var (feld, meldung) in fehler) ModelState.AddModelError(feld, meldung);
            return ValidationError();
        }

        var seite = await Zuluft(ct);
        if (seite.Result is OkObjectResult ok && ok.Value is ZuluftSeiteDto dto) return Ok(dto with { HaAngenommen = erreicht });
        return seite;
    }

    // ---------------------------------------------------------- Entfeuchter

    /// <summary>
    /// Fork AI (forkai.129, F-023): Die Entfeuchter-Seite — Geräteverhalten und
    /// Livebild. Pflanzenziele werden nur gelesen; geregelt wird in Home Assistant.
    /// </summary>
    [HttpGet("entfeuchter")]
    [ProducesResponseType(typeof(EntfeuchterSeiteDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<EntfeuchterSeiteDto>> Entfeuchter(CancellationToken ct)
    {
        var geraete = _geraete.EntitiesFuerModul(EntfeuchterSteuerungService.Modul);
        return Ok(new EntfeuchterSeiteDto(await _entfeuchter.EinstellungenAsync(ct), await _entfeuchter.LiveAsync(ct))
        {
            AusHomeAssistantUebernommen = _entfeuchter.Gespeichert is null,
            GeraeteZugeordnet = geraete.Count(g => g.Value is not null),
            GeraeteGesamt = SteuerungGeraeteRollen.FuerModul(EntfeuchterSteuerungService.Modul).Count,
        });
    }

    [HttpPut("entfeuchter")]
    [ProducesResponseType(typeof(EntfeuchterSeiteDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<EntfeuchterSeiteDto>> EntfeuchterSpeichern([FromBody] EntfeuchterEinstellungen request, CancellationToken ct)
    {
        if (request is null) return BadRequestError("entfeuchter_invalid", "Es wurde nichts übergeben.");
        var (gespeichert, fehler, erreicht) = await _entfeuchter.SpeichernAsync(request, ct);
        if (gespeichert is null)
        {
            foreach (var (feld, meldung) in fehler) ModelState.AddModelError(feld, meldung);
            return ValidationError();
        }

        var seite = await Entfeuchter(ct);
        if (seite.Result is OkObjectResult ok && ok.Value is EntfeuchterSeiteDto dto) return Ok(dto with { HaAngenommen = erreicht });
        return seite;
    }

    // -------------------------------------------------------------- Chiller

    /// <summary>
    /// Fork AI: Die Kühler-Seite — Sollwerte, Schutz und das Livebild der
    /// Steckdose. Geschaltet wird weiter in Home Assistant.
    /// </summary>
    [HttpGet("chiller")]
    [ProducesResponseType(typeof(ChillerSeiteDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChillerSeiteDto>> Chiller(CancellationToken ct)
    {
        var geraete = _geraete.EntitiesFuerModul(ChillerSteuerungService.Modul);
        return Ok(new ChillerSeiteDto(await _chiller.EinstellungenAsync(ct), await _chiller.LiveAsync(ct))
        {
            AusHomeAssistantUebernommen = _chiller.Gespeichert is null,
            GeraeteZugeordnet = geraete.Count(g => g.Value is not null),
            GeraeteGesamt = SteuerungGeraeteRollen.FuerModul(ChillerSteuerungService.Modul).Count,
        });
    }

    [HttpPut("chiller")]
    [ProducesResponseType(typeof(ChillerSeiteDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChillerSeiteDto>> ChillerSpeichern([FromBody] ChillerEinstellungen request, CancellationToken ct)
    {
        if (request is null) return BadRequestError("chiller_invalid", "Es wurde nichts übergeben.");
        var (gespeichert, fehler, erreicht) = await _chiller.SpeichernAsync(request, ct);
        if (gespeichert is null)
        {
            foreach (var (feld, meldung) in fehler) ModelState.AddModelError(feld, meldung);
            return ValidationError();
        }

        var seite = await Chiller(ct);
        if (seite.Result is OkObjectResult ok && ok.Value is ChillerSeiteDto dto) return Ok(dto with { HaAngenommen = erreicht });
        return seite;
    }

    // ---------------------------------------------------------------- Licht

    /// <summary>
    /// Fork AI: Die Licht-Seite — Zeitpläne, Stufe und der Zustand am Controller.
    /// </summary>
    [HttpGet("licht")]
    [ProducesResponseType(typeof(LichtSeiteDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<LichtSeiteDto>> Licht(CancellationToken ct)
    {
        var geraete = _geraete.EntitiesFuerModul(LichtSteuerungService.Modul);
        return Ok(new LichtSeiteDto(_licht.Einstellungen, await _licht.LiveAsync(ct))
        {
            GeraeteZugeordnet = geraete.Values.Count(entity => !string.IsNullOrWhiteSpace(entity)),
            GeraeteGesamt = geraete.Count,
        });
    }

    [HttpPut("licht")]
    [ProducesResponseType(typeof(LichtSeiteDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<LichtSeiteDto>> LichtSpeichern([FromBody] LichtEinstellungen request, CancellationToken ct)
    {
        if (request is null) return BadRequestError("licht_invalid", "Es wurde nichts übergeben.");
        var (gespeichert, fehler, erreicht) = await _licht.SpeichernAsync(request, ct);
        if (gespeichert is null)
        {
            foreach (var (feld, meldung) in fehler) ModelState.AddModelError(feld, meldung);
            return ValidationError();
        }
        var seite = await Licht(ct);
        if (seite.Result is OkObjectResult ok && ok.Value is LichtSeiteDto dto) return Ok(dto with { HaAngenommen = erreicht });
        return seite;
    }

    /// <summary>Aus, An, Preset anwenden, Stufe setzen — oder eine gemeldete Fehlmeldung wegräumen.</summary>
    [HttpPost("licht/befehl")]
    [ProducesResponseType(typeof(LichtSeiteDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<LichtSeiteDto>> LichtBefehl([FromBody] LichtBefehlRequest request, CancellationToken ct)
    {
        var art = request?.Art?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(art)) return BadRequestError("befehl_invalid", "Es wurde kein Befehl übergeben.");

        if (art == "quittieren")
        {
            LichtSteuerungService.OffeneVergessen();
            return await Licht(ct);
        }

        if (art is not ("aus" or "an" or "preset" or "stufe"))
            return BadRequestError("befehl_unbekannt", $"Der Befehl „{art}\" ist nicht bekannt.");

        var erreicht = await _licht.BefehlAsync(art, request!.Preset, request.Stufe, ct);
        var seite = await Licht(ct);
        if (seite.Result is OkObjectResult ok && ok.Value is LichtSeiteDto dto) return Ok(dto with { HaAngenommen = erreicht });
        return seite;
    }

    private static string LichtKurz(LichtLive licht)
    {
        var stufe = licht.Stufe?.ToString(CultureInfo.InvariantCulture) ?? "–";
        if (licht.Modus == LichtSteuerungService.Modi.Aus) return $"Aus · Stufe {stufe}";
        if (licht.Modus == LichtSteuerungService.Modi.An) return $"Dauerlicht · Stufe {stufe}";
        if (licht.Modus != LichtSteuerungService.Modi.Zeitplan) return $"Modus {licht.Modus ?? "–"} · Stufe {stufe}";

        var preset = licht.AktivesPreset switch
        {
            "veggie" => "Veggie",
            "bluete" => "Blüte",
            _ => "eigen",
        };
        return $"Zeitplan {preset} {licht.EinZeit ?? "–"} – {licht.AusZeit ?? "–"} · Stufe {stufe}";
    }

    // -------------------------------------------------------------- Geräte

    /// <summary>
    /// Fork AI (forkai.21): Die Geräte-Zuordnung aller Steuerungen samt Livewert,
    /// damit in der Oberfläche sichtbar ist, ob hinter einer Rolle wirklich das
    /// gemeinte Gerät hängt.
    /// </summary>
    /// <summary>
    /// Was die Steuerung in Home Assistant an eigenen Objekten braucht — und was
    /// davon fehlt.
    /// </summary>
    /// <remarks>
    /// Fork AI (forkai.45): Ohne das zeigt die Seite bei einer fremden
    /// Installation nur „nicht verfügbar", ohne den Grund zu nennen. Der
    /// Endpunkt stellt fest und erklärt; angelegt wird hier nichts.
    /// </remarks>
    /// <summary>Die fehlenden Helfer einer Steuerung in Home Assistant anlegen.</summary>
    /// <remarks>
    /// <para>Fork AI (forkai.58): Legt nur die einfachen Arten an — Zahlen,
    /// Schalter, Zeitstempel, Zähler. Rechen-Sensoren und Automationen brauchen
    /// andere Wege und kommen getrennt, weil sie getrennt schiefgehen.</para>
    /// <para>POST, obwohl nichts im Fork entsteht: Der Aufruf verändert den
    /// Zustand von Home Assistant und darf nicht durch einen Vorlauf des
    /// Browsers ausgelöst werden.</para>
    /// </remarks>
    /// <summary>Die fehlenden Rechenwerte einer Steuerung anlegen.</summary>
    /// <remarks>
    /// Fork AI (forkai.69): Getrennt von den Helfern, weil Template-Helfer über
    /// den Einrichtungsdialog entstehen und damit anders scheitern. Zuerst die
    /// Helfer anlegen — die Rechenwerte lesen sie.
    /// </remarks>
    /// <summary>Die Automationen einer Steuerung anlegen.</summary>
    /// <remarks>
    /// <para>Fork AI (forkai.72): Getrennt von Helfern und Rechenwerten und mit
    /// eigener Zustimmung, weil am Ende dieser Automationen ein Ventil an einer
    /// Gasflasche hängt.</para>
    /// <para><c>vorschau=true</c> schreibt nichts und sagt nur, was geschähe —
    /// das ist der Stand, den der Knopf vor der Bestätigung zeigt.</para>
    /// </remarks>
    /// <summary>Das Dosier-Ventil zwei Sekunden öffnen und nachsehen.</summary>
    /// <remarks>
    /// Fork AI (forkai.73): In dieser Anlage hat es Tage gedauert
    /// herauszufinden, dass der Port „an" meldete und trotzdem kein Gas kam.
    /// Zwei Sekunden beim Einrichten ersparen dem Nächsten diese Suche.
    /// </remarks>
    [HttpPost("{modul}/probe")]
    public async Task<ActionResult<SteuerungProbeService.Ergebnis>> Probeschaltung(
        string modul, CancellationToken ct)
    {
        if (SteuerungBauteile.FuerModul(modul).Count == 0) return NotFound();

        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        var zuordnung = _geraete.EntitiesFuerModul(modul)
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .ToDictionary(p => p.Key, p => p.Value!, StringComparer.Ordinal);

        return Ok(await _probe.ProbierenAsync(zuordnung, settings, ct));
    }

    [HttpPost("{modul}/automationen")]
    public async Task<ActionResult<SteuerungAutomationService.Bilanz>> AutomationenAnlegen(
        string modul, [FromQuery] bool vorschau, CancellationToken ct)
    {
        if (SteuerungBauteile.FuerModul(modul).Count == 0) return NotFound();

        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        var zuordnung = _geraete.EntitiesFuerModul(modul)
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .ToDictionary(p => p.Key, p => p.Value!, StringComparer.Ordinal);

        return Ok(await _automationen.AnlegenAsync(modul, zuordnung, settings, vorschau, ct));
    }

    [HttpPost("{modul}/rechenwerte")]
    public async Task<ActionResult<SteuerungRechenwertService.Bilanz>> RechenwerteAnlegen(
        string modul, CancellationToken ct)
    {
        if (SteuerungBauteile.FuerModul(modul).Count == 0) return NotFound();

        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        var zuordnung = _geraete.EntitiesFuerModul(modul)
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .ToDictionary(p => p.Key, p => p.Value!, StringComparer.Ordinal);

        var bilanz = await _rechenwerte.AnlegenAsync(modul, zuordnung, settings, ct);
        if (!bilanz.Erreichbar)
        {
            return ConflictError(
                "home_assistant_stumm",
                "Es wurde nichts angelegt. Ohne Antwort von Home Assistant ist nicht zu erkennen, welche Rechenwerte es schon gibt.");
        }

        return Ok(bilanz);
    }

    [HttpPost("{modul}/helfer")]
    public async Task<ActionResult<SteuerungHelferService.Bilanz>> HelferAnlegen(
        string modul, CancellationToken ct)
    {
        if (SteuerungBauteile.FuerModul(modul).Count == 0) return NotFound();

        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        var belegt = _geraete.EntitiesFuerModul(modul)
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .Select(p => p.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var bilanz = await _helfer.AnlegenAsync(modul, belegt, settings, ct);
        if (!bilanz.Erreichbar)
        {
            // Der Fork hat ein eigenes Fehlerformat; Problem() waere das von
            // ASP.NET und damit das zweite, das die Oberflaeche kennen muesste.
            return ConflictError(
                "home_assistant_stumm",
                "Es wurde nichts angelegt. Ohne Antwort von Home Assistant ist nicht zu erkennen, welche Helfer es schon gibt.");
        }

        return Ok(bilanz);
    }

    [HttpGet("{modul}/bestand")]
    public async Task<ActionResult<SteuerungBestandService.Bestandsaufnahme>> Bestand(
        string modul, CancellationToken ct)
    {
        if (SteuerungBauteile.FuerModul(modul).Count == 0) return NotFound();

        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        var belegt = _geraete.EntitiesFuerModul(modul)
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .Select(p => p.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Ok(await _bestand.AufnehmenAsync(modul, belegt, settings, ct));
    }

    [HttpGet("geraete")]
    [ProducesResponseType(typeof(SteuerungGeraeteSeiteDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SteuerungGeraeteSeiteDto>> Geraete(CancellationToken ct)
    {
        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        var entities = await _ha.GetEntitiesAsync(settings, ct);
        var nachId = entities.ToDictionary(x => x.EntityId, x => x, StringComparer.OrdinalIgnoreCase);

        string? Wert(string? id) => id is not null && nachId.TryGetValue(id, out var s) ? s.State : null;

        var module = new List<SteuerungGeraeteModulDto>();
        foreach (var modul in SteuerungGeraeteRollen.Module)
        {
            var gespeichert = _geraete.Gespeichert(modul);
            var aufgeloest = _geraete.EntitiesFuerModul(modul);
            var zeilen = SteuerungGeraeteRollen.FuerModul(modul).Select(rolle =>
            {
                var roh = gespeichert.TryGetValue(rolle.Schluessel, out var eigen) && !string.IsNullOrWhiteSpace(eigen)
                    ? eigen
                    : rolle.Vorgabe;
                // Bewusst leer gelassen: das Feld zeigt leer, nicht die Marke.
                if (roh == SteuerungGeraeteRollen.BewusstLeer) roh = string.Empty;
                var ziel = aufgeloest.TryGetValue(rolle.Schluessel, out var id) ? id : null;
                return new SteuerungGeraetZeileDto(
                    rolle.Schluessel, rolle.Label, rolle.Gruppe, rolle.Einheit, rolle.Hinweis,
                    rolle.Pflicht, rolle.Domains, rolle.Vorgabe, roh, ziel, Wert(ziel),
                    ziel is not null && nachId.ContainsKey(ziel));
            }).ToList();
            module.Add(new SteuerungGeraeteModulDto(modul, ModulTitel(modul), zeilen));
        }

        var eigene = _geraete.EigeneGeraete()
            .Select(g => new EigenesGeraetDto(g.Rolle, g.EntityId, Wert(g.EntityId), nachId.ContainsKey(g.EntityId)))
            .ToList();

        return Ok(new SteuerungGeraeteSeiteDto(entities.Count > 0, module, eigene));
    }

    [HttpPut("geraete/{modul}")]
    [ProducesResponseType(typeof(SteuerungGeraeteSeiteDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SteuerungGeraeteSeiteDto>> GeraeteSpeichern(string modul, [FromBody] SteuerungGeraeteRequest request, CancellationToken ct)
    {
        if (request?.Zuordnungen is null) return BadRequestError("geraete_invalid", "Es wurde nichts übergeben.");
        if (!SteuerungGeraeteRollen.Module.Contains(modul, StringComparer.OrdinalIgnoreCase))
            return BadRequestError("modul_unbekannt", $"Die Steuerung „{modul}\" hat keine Geräte-Rollen.");

        var fehler = _geraete.Speichern(modul, request.Zuordnungen);
        if (fehler.Count > 0)
        {
            foreach (var (feld, meldung) in fehler) ModelState.AddModelError(feld, meldung);
            return ValidationError();
        }
        return await Geraete(ct);
    }

    [HttpPost("geraete/eigene")]
    [ProducesResponseType(typeof(SteuerungGeraeteSeiteDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SteuerungGeraeteSeiteDto>> EigenesGeraet([FromBody] EigenesGeraetRequest request, CancellationToken ct)
    {
        if (request is null) return BadRequestError("geraet_invalid", "Es wurde nichts übergeben.");
        if (_geraete.EigenesGeraetSpeichern(request.Name ?? string.Empty, request.EntityId ?? string.Empty, request.AlterName) is { } fehler)
        {
            ModelState.AddModelError("name", fehler);
            return ValidationError();
        }
        return await Geraete(ct);
    }

    [HttpDelete("geraete/eigene/{name}")]
    [ProducesResponseType(typeof(SteuerungGeraeteSeiteDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SteuerungGeraeteSeiteDto>> EigenesGeraetLoeschen(string name, CancellationToken ct)
    {
        if (_geraete.EigenesGeraetLoeschen(name) is { } fehler)
            return BadRequestError("geraet_in_benutzung", fehler);
        return await Geraete(ct);
    }

    private static string ModulTitel(string modul) => modul switch
    {
        "co2" => "CO₂ · Begasung",
        "licht" => "Licht · LED Top",
        "zuluft" => "Zuluft · Keller",
        "chiller" => "Water Chiller",
        "entfeuchter" => "Entfeuchter",
        "cropsteering" => "Crop Steering",
        _ => modul,
    };

    private static Co2TagDto ToDto(Co2Tag t) => new(
        t.Datum, t.Impulse, Math.Round(t.VentilSekunden), Math.Round(t.Gramm), t.ZielErreichtUm, t.Abgeschlossen,
        t.JournalEntryId is not null, t.VerbrauchId is not null, t.Flaschenwechsel);
}

public sealed record SteuerungModulDto(string Kennung, string Titel, string Status, string Kurz, string Wert, string Unterzeile, bool HatDetail);
public sealed record SteuerungUebersichtDto(bool HaErreichbar, IReadOnlyList<SteuerungModulDto> Module, DateTime StandUtc);
public sealed record KostenArtikelKurzDto(int Id, string Name, string Einheit);
public sealed record Co2TagDto(string Datum, int Impulse, double VentilSekunden, double Gramm, string? ZielErreichtUm, bool Abgeschlossen, bool ImJournal, bool InKosten, bool Flaschenwechsel);
public sealed record Co2SeiteDto(
    Co2Einstellungen Einstellungen,
    Co2Live Live,
    IReadOnlyList<Co2TagDto> Tage,
    IReadOnlyList<KostenArtikelKurzDto> Artikel,
    string? GrowName,
    string? Phase,
    int? PlanPpm,
    string? PlanHerkunft)
{
    /// <summary>Nach dem Speichern: ob Home Assistant alle Sollwerte angenommen hat (null beim Lesen).</summary>
    public bool? HaAngenommen { get; init; }

    /// <summary>
    /// Fork AI (forkai.21): Wie viele Rollen dieser Steuerung eine Entität haben.
    /// Die Seite zeigt damit eine Zeile zur Geräte-Zuordnung — bearbeitet wird dort,
    /// nicht hier, damit ein Gerät genau eine Wahrheit behält.
    /// </summary>
    public int GeraeteZugeordnet { get; init; }
    public int GeraeteGesamt { get; init; }
}

public sealed record ZuluftSeiteDto(ZuluftEinstellungen Einstellungen, ZuluftLive Live)
{
    /// <summary>Nach dem Speichern: ob Home Assistant alle Sollwerte angenommen hat (null beim Lesen).</summary>
    public bool? HaAngenommen { get; init; }

    /// <summary>True, solange die Werte aus den vorhandenen Helfern kommen und nicht aus der Datenbank.</summary>
    public bool AusHomeAssistantUebernommen { get; init; }

    public int GeraeteZugeordnet { get; init; }
    public int GeraeteGesamt { get; init; }
}

public sealed record EntfeuchterSeiteDto(EntfeuchterEinstellungen Einstellungen, EntfeuchterLive Live)
{
    /// <summary>Nach dem Speichern: ob Home Assistant alle Sollwerte angenommen hat (null beim Lesen).</summary>
    public bool? HaAngenommen { get; init; }

    /// <summary>True, solange die Werte aus den vorhandenen Helfern kommen und nicht aus der Datenbank.</summary>
    public bool AusHomeAssistantUebernommen { get; init; }

    public int GeraeteZugeordnet { get; init; }
    public int GeraeteGesamt { get; init; }
}

public sealed record ChillerSeiteDto(ChillerEinstellungen Einstellungen, ChillerLive Live)
{
    /// <summary>Nach dem Speichern: ob Home Assistant alle Sollwerte angenommen hat (null beim Lesen).</summary>
    public bool? HaAngenommen { get; init; }

    /// <summary>True, solange die Werte aus den vorhandenen Helfern kommen und nicht aus der Datenbank.</summary>
    public bool AusHomeAssistantUebernommen { get; init; }

    public int GeraeteZugeordnet { get; init; }
    public int GeraeteGesamt { get; init; }
}

public sealed record LichtBefehlRequest(string? Art, string? Preset, int? Stufe);

public sealed record LichtSeiteDto(LichtEinstellungen Einstellungen, LichtLive Live)
{
    /// <summary>Nach einem Befehl: ob Home Assistant alle Aufrufe angenommen hat (null beim Lesen).</summary>
    public bool? HaAngenommen { get; init; }
    public int GeraeteZugeordnet { get; init; }
    public int GeraeteGesamt { get; init; }
}

public sealed record SteuerungGeraetZeileDto(
    string Rolle,
    string Label,
    string Gruppe,
    string? Einheit,
    string? Hinweis,
    bool Pflicht,
    IReadOnlyList<string> Domains,
    string Vorgabe,
    // Eingetragen: was in der Oberfläche im Feld steht — Entity-ID oder @Verweis.
    string Eingetragen,
    // EntityId: worauf es am Ende hinausläuft; null bei leerem oder totem Verweis.
    string? EntityId,
    string? Livewert,
    bool Gefunden);

public sealed record SteuerungGeraeteModulDto(string Modul, string Titel, IReadOnlyList<SteuerungGeraetZeileDto> Zeilen);
public sealed record EigenesGeraetDto(string Name, string EntityId, string? Livewert, bool Gefunden);
public sealed record EigenesGeraetRequest(string? Name, string? EntityId, string? AlterName);
public sealed record SteuerungGeraeteRequest(Dictionary<string, string?> Zuordnungen);
public sealed record SteuerungGeraeteSeiteDto(bool HaErreichbar, IReadOnlyList<SteuerungGeraeteModulDto> Module, IReadOnlyList<EigenesGeraetDto> Eigene);
