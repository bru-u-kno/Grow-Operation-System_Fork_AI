using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.GrowPlan;
using GrowDiary.Web.Services.Knowledge.Schema;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>Ein Planstand, wie ihn die Oberfläche liest.</summary>
public sealed record GrowPlanStandDto(
    int GrowId,
    string Stand,
    string ProgrammId,
    string ProgrammName,
    string? Vermerk,
    DateTime AngelegtUtc,
    DateTime GeaendertUtc,
    FeedChartDefinition Chart,
    Dictionary<string, Dictionary<string, string>> Herkunft);

/// <summary>Ein Eintrag im Änderungsbuch.</summary>
public sealed record GrowPlanEintragDto(
    long Id,
    DateTime ZeitUtc,
    string Art,
    string? SpalteId,
    string? Feld,
    string? Alt,
    string? Neu,
    string? Ziel,
    string? Grund);

public sealed class PlanWertDto
{
    public string Feld { get; set; } = string.Empty;
    public double? Wert { get; set; }
}

public sealed class PlanDosisDto
{
    public string Komponente { get; set; } = string.Empty;
    public double MlProLiter { get; set; }
}

/// <summary>Eine Woche des Plans speichern.</summary>
public sealed class PlanSpeichernRequest
{
    public string SpalteId { get; set; } = string.Empty;
    public List<PlanWertDto> Werte { get; set; } = [];
    /// <summary>Die ganze Dosierung der Woche — <c>null</c> lässt sie unberührt.</summary>
    public List<PlanDosisDto>? Dosierung { get; set; }
    public bool AuchInsProgramm { get; set; }
    public string? ProgrammName { get; set; }
    public string? Grund { get; set; }
}

public sealed record PlanGespeichertDto(
    int Aenderungen,
    string? ProgrammId,
    string? ProgrammName,
    int Uebergeben,
    string? Hinweis,
    GrowPlanStandDto Plan);

/// <summary>
/// Fork AI (Grow-Plan): Planstände und Änderungsbuch eines Grows lesen und eine
/// Planwoche speichern.
/// </summary>
[ApiController]
[Route("api/grows/{growId:int}/plan")]
public sealed class GrowPlanApiController : ApiControllerBase
{
    private readonly GrowRepository _grows;
    private readonly GrowPlanService _plaene;
    private readonly WochenplanSyncService _sync;

    public GrowPlanApiController(GrowRepository grows, GrowPlanService plaene, WochenplanSyncService sync)
    {
        _grows = grows;
        _plaene = plaene;
        _sync = sync;
    }

    /// <summary>Ein Stand des Plans: <c>arbeit</c> (Standard), <c>start</c> oder <c>ende</c>.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(GrowPlanStandDto), StatusCodes.Status200OK)]
    public ActionResult<GrowPlanStandDto> Get(int growId, [FromQuery] string stand = GrowPlanStaende.Arbeit)
    {
        if (_grows.GetGrow(growId) is null) return NotFoundError("grow_nicht_gefunden", "Diesen Grow gibt es nicht.");
        if (stand is not (GrowPlanStaende.Start or GrowPlanStaende.Arbeit or GrowPlanStaende.Ende))
            return ValidationError("Stand muss start, arbeit oder ende sein.");

        if (_plaene.Stand(growId, stand) is not { } gefunden)
            return NotFoundError("plan_nicht_gefunden", "Dieser Grow hat keinen Plan in diesem Stand.");

        return Ok(Dto(gefunden));
    }

    private static GrowPlanStandDto Dto(GrowPlanStand stand) => new(
        stand.GrowId,
        stand.Stand,
        stand.Inhalt.ProgrammId,
        stand.Inhalt.ProgrammName,
        stand.Vermerk,
        stand.AngelegtUtc,
        stand.GeaendertUtc,
        stand.Inhalt.Chart,
        stand.Inhalt.Herkunft);

    /// <summary>
    /// Eine Woche speichern: Zielwerte, optional die ganze Dosierung, optional
    /// zusätzlich ins eigene Programm. Alles oder nichts; danach Übergabe an HA.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(PlanGespeichertDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PlanGespeichertDto>> Speichern(
        int growId, [FromBody] PlanSpeichernRequest anfrage, CancellationToken ct)
    {
        if (_grows.GetGrow(growId) is null) return NotFoundError("grow_nicht_gefunden", "Diesen Grow gibt es nicht.");
        if (_plaene.Stand(growId, GrowPlanStaende.Ende) is not null)
            return ValidationError("Der Grow ist abgeschlossen — sein Plan ist eingefroren.");
        if (_plaene.Stand(growId, GrowPlanStaende.Arbeit) is not { } arbeit)
            return NotFoundError("plan_nicht_gefunden", "Dieser Grow hat keinen Plan.");

        var spalte = arbeit.Inhalt.Chart.Columns.FirstOrDefault(
            c => string.Equals(c.Id, anfrage.SpalteId, StringComparison.OrdinalIgnoreCase));
        if (spalte is null) return ValidationError($"Die Woche „{anfrage.SpalteId}“ gibt es im Plan nicht.");

        // Zielwerte prüfen: bekannt, im Bereich, Paare in Ordnung (nach dem Speichern gedacht).
        var danach = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
        foreach (var w in anfrage.Werte)
        {
            if (Wochenwertfelder.Finden(w.Feld) is not { } feld)
                return ValidationError($"„{w.Feld}“ lässt sich nicht bearbeiten.");
            if (w.Wert is { } zahl && (double.IsNaN(zahl) || double.IsInfinity(zahl) || zahl < feld.Min || zahl > feld.Max))
                return ValidationError($"{spalte.Label}: {feld.Bezeichnung} muss zwischen {Zahl(feld.Min)} und {Zahl(feld.Max)} liegen.");
            danach[feld.Name] = w.Wert ?? _plaene.Startwert(growId, spalte.Id, feld);
        }
        foreach (var feld in Wochenwertfelder.Alle.Where(f => f.Paar is not null))
        {
            var partner = Wochenwertfelder.Finden(feld.Paar!)!;
            var von = danach.TryGetValue(feld.Name, out var a) ? a : feld.Lesen(spalte);
            var bis = danach.TryGetValue(partner.Name, out var b) ? b : partner.Lesen(spalte);
            if (von is { } v && bis is { } z && v > z + 1e-9)
                return ValidationError($"{spalte.Label}: {feld.Bezeichnung} ({Zahl(v)}) liegt über {partner.Bezeichnung} ({Zahl(z)}).");
        }

        List<PlanDosis>? dosierung = null;
        if (anfrage.Dosierung is { } liste)
        {
            dosierung = [];
            var namen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in liste)
            {
                var name = d.Komponente?.Trim() ?? string.Empty;
                if (name.Length == 0) return ValidationError("Jede Zutat braucht einen Namen.");
                if (name.Length > 80) return ValidationError($"„{name[..20]}…“ ist als Name zu lang.");
                if (!namen.Add(name)) return ValidationError($"„{name}“ steht zweimal in der Dosierung.");
                if (double.IsNaN(d.MlProLiter) || d.MlProLiter < 0 || d.MlProLiter > 50)
                    return ValidationError($"{name}: die Menge muss zwischen 0 und 50 ml je Liter liegen.");
                dosierung.Add(new PlanDosis(name, Math.Round(d.MlProLiter, 3)));
            }
        }

        var grund = string.IsNullOrWhiteSpace(anfrage.Grund) ? null : anfrage.Grund.Trim();
        if (grund is { Length: > 300 }) return ValidationError("Der Grund darf höchstens 300 Zeichen lang sein.");
        var programmName = string.IsNullOrWhiteSpace(anfrage.ProgrammName) ? null : anfrage.ProgrammName.Trim();
        if (programmName is { Length: > 80 }) return ValidationError("Der Programmname darf höchstens 80 Zeichen lang sein.");

        var ergebnis = _plaene.Speichern(growId, new PlanSpeichernAnfrage(
            spalte.Id,
            anfrage.Werte.Select(w => (w.Feld, w.Wert)).ToList(),
            dosierung,
            anfrage.AuchInsProgramm,
            programmName,
            grund));

        var uebergeben = 0;
        string? hinweis = null;
        try
        {
            uebergeben = await _sync.UebergebenAsync(ct);
        }
        catch (Exception)
        {
            hinweis = "Gespeichert. Home Assistant war gerade nicht erreichbar — die Übergabe holt der nächste Lauf nach.";
        }

        return Ok(new PlanGespeichertDto(
            ergebnis.Aenderungen, ergebnis.ProgrammId, ergebnis.ProgrammName, uebergeben, hinweis,
            Dto(_plaene.Stand(growId, GrowPlanStaende.Arbeit)!)));
    }

    private static string Zahl(double wert) => wert.ToString("0.##", AppCulture.German);

    /// <summary>Das Änderungsbuch, neueste Einträge zuerst.</summary>
    [HttpGet("buch")]
    [ProducesResponseType(typeof(IReadOnlyList<GrowPlanEintragDto>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<GrowPlanEintragDto>> Buch(int growId)
    {
        if (_grows.GetGrow(growId) is null) return NotFoundError("grow_nicht_gefunden", "Diesen Grow gibt es nicht.");
        return Ok(_plaene.Buch(growId)
            .Select(e => new GrowPlanEintragDto(e.Id, e.ZeitUtc, e.Art, e.SpalteId, e.Feld, e.Alt, e.Neu, e.Ziel, e.Grund))
            .ToList());
    }
}
