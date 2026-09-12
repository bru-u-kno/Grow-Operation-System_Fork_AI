using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge;
using GrowDiary.Web.Services.Knowledge.Schema;
using Microsoft.AspNetCore.Mvc;

namespace GrowDiary.Web.Api.Controllers;

/// <summary>Eine Woche des Plans, so wie sie auf der Seite steht.</summary>
public sealed record WochenplanWocheDto(
    string Id,
    string Label,
    string Stage,
    int? Woche,
    bool IstJetzt,
    bool WirdGehalten,
    string? Ec,
    string? Ph,
    string? Wasser,
    string? Vpd,
    string? Rh,
    string? Luft,
    string? Co2,
    string? Ppfd,
    string? Dosierung);

/// <summary>Der Plan eines laufenden Durchgangs.</summary>
public sealed record WochenplanDto(
    int GrowId,
    string GrowName,
    string? Sorte,
    string ProgrammName,
    bool WochenZieleAktiv,
    string? VegiStart,
    string? Flip,
    string? Erntefenster,
    string? JetztLabel,
    string? Haltehinweis,
    List<WochenplanWocheDto> Wochen);

/// <summary>
/// Fork AI: die Seite „Wochenplan" — was der Plan für diesen Durchgang vorgibt.
/// </summary>
/// <remarks>
/// <para><b>Warum eigene Seite.</b> Die Wochenwerte wirken seit forkai.46
/// überall, waren aber nirgends am Stück zu sehen: EC und pH im Mischplan, das
/// Klima nur indirekt über die Kacheln. Wer wissen will, was nächste Woche
/// passiert, musste die Wissensdatenbank aufschlagen.</para>
///
/// <para><b>Nichts gerechnet.</b> Die Spalten kommen aus dem Düngeprogramm, die
/// laufende aus <see cref="MischplanService.ZielSpalteFuerGrow"/> — dieselbe
/// Auswahl, die auch Kacheln und Alarme benutzen. Die Anker (Vegi-Start, Flip)
/// stehen am Grow; das Erntefenster folgt den Blütewochen der Sorte.</para>
/// </remarks>
[ApiController]
[Route("api/wochenplan")]
[Produces("application/json")]
public sealed class WochenplanApiController : ApiControllerBase
{
    private readonly GrowRepository _grows;
    private readonly KnowledgeBaseLoader _wissen;

    public WochenplanApiController(GrowRepository grows, KnowledgeBaseLoader wissen)
    {
        _grows = grows;
        _wissen = wissen;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<WochenplanDto>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<WochenplanDto>> Get()
    {
        var liste = new List<WochenplanDto>();

        foreach (var grow in _grows.GetActiveGrows())
        {
            var programm = _wissen.NutrientPrograms
                .FirstOrDefault(p => string.Equals(p.Id, grow.FeedProgramId, StringComparison.OrdinalIgnoreCase));

            if (programm?.FeedChart is not { } chart || chart.Columns.Count == 0) continue;

            var jetzt = MischplanService.ZielSpalteFuerGrow(grow, _wissen.NutrientPrograms);
            var aktiveId = jetzt?.Spalte.Id;

            var wochen = chart.Columns
                .Select(spalte => Zeile(spalte, istJetzt: spalte.Id == aktiveId, grow))
                .ToList();

            liste.Add(new WochenplanDto(
                grow.Id,
                string.IsNullOrWhiteSpace(grow.Name) ? $"Grow {grow.Id}" : grow.Name,
                grow.Strain,
                programm.Name,
                // Ohne den Haken am Addback gilt der Plan nur fürs Anmischen,
                // nicht für Kacheln und Alarme. Das muss auf der Seite stehen,
                // sonst liest man Zahlen, die nirgends wirken.
                grow.UseFeedChartTargets,
                Datum(grow.VegStartedAt),
                Datum(grow.FlipDate),
                Erntefenster(grow),
                jetzt?.Spalte.Label,
                jetzt is { } j ? Haltehinweis(grow, j.Spalte) : null,
                wochen));
        }

        return Ok(liste);
    }

    private static WochenplanWocheDto Zeile(FeedChartColumn spalte, bool istJetzt, GrowRun grow)
        => new(
            spalte.Id,
            spalte.Label,
            spalte.Stage,
            spalte.Week,
            istJetzt,
            istJetzt && Haltehinweis(grow, spalte) is not null,
            spalte.EcTarget is { } ec ? Zahl(ec) : null,
            Spanne(spalte.PhMin, spalte.PhMax),
            Wasser(spalte),
            Spanne(spalte.VpdMin, spalte.VpdMax),
            spalte.RhMax is { } rh ? $"max {Zahl(rh)} %" : null,
            spalte.AirTempC is { } luft ? $"{Zahl(luft)} °C" : null,
            Spanne(spalte.Co2Min, spalte.Co2Max),
            Spanne(spalte.PpfdMin, spalte.PpfdMax),
            Dosierung(spalte));

    /// <summary>Die zwei, drei Komponenten, die beim Anmischen wirklich zählen.</summary>
    /// <remarks>
    /// Alle sechs würden die Zeile sprengen; die Vollständigkeit steht im
    /// Mischplan, der dafür gebaut ist.
    /// </remarks>
    private static string? Dosierung(FeedChartColumn spalte)
    {
        var teile = spalte.Items
            .Take(3)
            .Select(i => $"{i.Component} {Zahl(i.MinMlPerLiter)}")
            .ToList();

        return teile.Count == 0 ? null : string.Join(" · ", teile);
    }

    private static string? Wasser(FeedChartColumn spalte)
    {
        if (spalte.WaterTempDayC is not { } tag) return null;
        return spalte.WaterTempNightC is { } nacht && Math.Abs(tag - nacht) > 0.01
            ? $"{Zahl(tag)} / {Zahl(nacht)} °C"
            : $"{Zahl(tag)} °C";
    }

    /// <summary>
    /// Wann geerntet werden kann — Flip plus die Blütewochen der Sorte.
    /// </summary>
    /// <remarks>
    /// Ohne Flip gibt es kein Fenster: vor dem Umstellen ist die Blütedauer eine
    /// Eigenschaft der Sorte, kein Datum. Lieber nichts anzeigen als ein Datum,
    /// das sich beim Flip um Wochen verschiebt.
    /// </remarks>
    private static string? Erntefenster(GrowRun grow)
    {
        if (grow.FlipDate is not { } flip) return null;
        if (grow.BreederFlowerWeeksMin is not { } min && grow.BreederFlowerWeeksMax is not { } _) return null;

        var von = flip.AddDays(7 * (grow.BreederFlowerWeeksMin ?? grow.BreederFlowerWeeksMax!.Value));
        var bis = flip.AddDays(7 * (grow.BreederFlowerWeeksMax ?? grow.BreederFlowerWeeksMin!.Value));

        return von.Date == bis.Date
            ? von.ToString("dd.MM.", AppCulture.German)
            : $"{von:dd.MM.}–{bis:dd.MM.}";
    }

    private static string? Haltehinweis(GrowRun grow, FeedChartColumn spalte)
    {
        if (spalte.Week is not { } spaltenWoche) return null;

        var ist = MischplanService.WocheInPhase(grow, spalte.Stage);
        return ist > spaltenWoche
            ? $"gehalten seit Woche {spaltenWoche + 1} — du bist in Woche {ist} dieser Phase"
            : null;
    }

    private static string? Datum(DateTime? wert)
        => wert?.ToString("dd.MM.yyyy", AppCulture.German);

    private static string? Spanne(double? min, double? max)
    {
        if (min is null && max is null) return null;
        if (min is null) return $"bis {Zahl(max!.Value)}";
        if (max is null) return $"ab {Zahl(min.Value)}";
        return Math.Abs(min.Value - max.Value) < 0.001
            ? Zahl(min.Value)
            : $"{Zahl(min.Value)}–{Zahl(max.Value)}";
    }

    private static string Zahl(double wert) => wert.ToString("0.##", AppCulture.German);
}
