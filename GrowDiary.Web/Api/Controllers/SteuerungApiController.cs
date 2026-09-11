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
    private readonly HomeAssistantService _ha;
    private readonly HomeAssistantSettingsRepository _haSettings;
    private readonly KostenRepository _kosten;

    public SteuerungApiController(Co2SteuerungService co2, HomeAssistantService ha, HomeAssistantSettingsRepository haSettings, KostenRepository kosten)
    {
        _co2 = co2;
        _ha = ha;
        _haSettings = haSettings;
        _kosten = kosten;
    }

    // ------------------------------------------------------------ Übersicht

    [HttpGet]
    [ProducesResponseType(typeof(SteuerungUebersichtDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SteuerungUebersichtDto>> Uebersicht(CancellationToken ct)
    {
        var live = await _co2.LiveAsync(ct);
        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        var entities = await _ha.GetEntitiesAsync(settings, ct);
        var nachId = entities.ToDictionary(x => x.EntityId, x => x, StringComparer.OrdinalIgnoreCase);
        var de = CultureInfo.GetCultureInfo("de-DE");

        string? Text(string id) => nachId.TryGetValue(id, out var s) ? s.State : null;
        double? Zahl(string id) => double.TryParse(Text(id), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        string F(double? v, string einheit, string format = "0") => v is { } x ? x.ToString(format, de) + einheit : "–";

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
                Status: Text("select.rdwc_dehumi_aktiver_modus") is "On" ? "an" : "aus",
                Kurz: Text("input_boolean.trotec_vpd_regelung") == "on"
                    ? $"VPD-Modus · ein ab {F(Zahl("sensor.trotec_feuchte_ein_aktiv"), " %", "0.0")}"
                    : $"Fest · ein ab {F(Zahl("sensor.trotec_feuchte_ein_aktiv"), " %", "0.0")}",
                Wert: F(live.RhProzent, " %", "0.0"),
                Unterzeile: $"{(Text("select.rdwc_dehumi_aktiver_modus") is "On" ? "läuft" : "bereit")} · VPD {F(live.Vpd, "", "0.00")}",
                HatDetail: false),
            new(
                Kennung: "chiller",
                Titel: "Water Chiller",
                Status: Text("binary_sensor.chiller_kuhlbedarf") == "on" ? "an" : "aus",
                Kurz: $"Tag {F(Zahl("input_number.chiller_zieltemperatur_tag"), " °C", "0.0")} · Nacht {F(Zahl("input_number.chiller_zieltemperatur_nacht"), " °C", "0.0")}",
                Wert: F(Zahl("sensor.bluelab_guardian_temperature"), " °C", "0.0"),
                Unterzeile: Text("binary_sensor.chiller_kuhlbedarf") == "on" ? "kühlt" : "bereit",
                HatDetail: false),
            new(
                Kennung: "abluft",
                Titel: "Abluft T6",
                Status: live.KlimaOk == false ? "warn" : "an",
                Kurz: $"Klima hat Vorrang · Stufen {Zahl(Co2SteuerungService.Entitaeten.T6Normal)?.ToString("0", de) ?? "–"} / {Zahl(Co2SteuerungService.Entitaeten.T6Dosierung)?.ToString("0", de) ?? "–"} / {Zahl(Co2SteuerungService.Entitaeten.T6Tief)?.ToString("0", de) ?? "–"}",
                Wert: $"Stufe {live.T6Stufe?.ToString(de) ?? "–"}",
                Unterzeile: live.KlimaOk == true ? "gedrosselt" : "Klima gesperrt",
                HatDetail: false),
            new(
                Kennung: "licht",
                Titel: "Licht LED Top",
                Status: live.LichtAn == true ? "an" : "aus",
                Kurz: $"Zeitplan {Text("time.klein_abluft_geplante_ein_zeit") ?? "–"} – {Text("time.klein_abluft_geplante_aus_zeit") ?? "–"} · Stufe {F(Zahl("number.klein_abluft_eingeschaltete_leistung"), "")}",
                Wert: live.LichtAn == true ? "an" : "aus",
                Unterzeile: live.LichtAn == true ? "Lichtphase" : "Dunkelphase",
                HatDetail: false),
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
        return Ok(new Co2SeiteDto(
            _co2.Einstellungen,
            live,
            tage.Select(ToDto).ToList(),
            artikel,
            grow?.Name,
            stage is { } st ? GeltendeZieleApiController.StageLabel(st) : null,
            _co2.PlanZielPpm(),
            _co2.PlanHerkunft()));
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
}
