using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services.Knowledge;
using GrowDiary.Web.Services.Knowledge.Schema;

namespace GrowDiary.Web.Services;

/// <summary>Eine Zeile des Mischplans — eine Komponente mit ihrer Menge.</summary>
/// <param name="Komponente">Grow A, CaMg, Cleanse …</param>
/// <param name="MlProLiter">Dosis laut Chart (bei Spannen der Mittelwert-freie Text unten).</param>
/// <param name="MlGesamtMin">Menge für das Betriebsvolumen, untere Kante.</param>
/// <param name="MlGesamtMax">Obere Kante; gleich Min, wenn das Chart keine Spanne nennt.</param>
public sealed record MischplanZeile(
    string Komponente,
    string MlProLiter,
    double MlGesamtMin,
    double MlGesamtMax);

/// <summary>Der Mischplan für heute — oder der Grund, warum es keinen gibt.</summary>
public sealed record Mischplan(
    string? ProgrammName,
    string? SpalteLabel,
    double? VolumenLiter,
    IReadOnlyList<MischplanZeile> Zeilen,
    double? EcZiel,
    double? PhMin,
    double? PhMax,
    string? Herkunft,
    string? Luecke,
    /// <summary>Ob diese Wochen-Ziele auch als Sollwerte gelten.</summary>
    bool ZieleAktiv = false);

/// <summary>
/// Rechnet das Wochen-Chart des Programms auf das Betriebsvolumen des Grows um.
/// </summary>
/// <remarks>
/// <para>Der Kern der Begleitung: „Hauptkomponenten nach Plan zugeben" wird zu
/// „Grow A 375 ml". Programm und Woche kommen vom Grow, das Volumen von seiner
/// Anlage — der Betreiber rechnet nichts und rät nichts.</para>
///
/// <para>Fehlt ein Glied der Kette, sagt der Plan WELCHES, statt zu raten:
/// kein Programm gewählt, Programm ohne Zahlen-Chart, kein Volumen an der
/// Anlage. Eine leere Antwort ohne Grund wäre die alte Stummheit in neu.</para>
/// </remarks>
public sealed class MischplanService
{
    private readonly GrowRepository _grows;
    private readonly HydroSetupRepository _setups;
    private readonly KnowledgeBaseLoader _wissen;
    private readonly WeekCounterService _wochen;

    public MischplanService(
        GrowRepository grows,
        HydroSetupRepository setups,
        KnowledgeBaseLoader wissen,
        WeekCounterService wochen)
    {
        _grows = grows;
        _setups = setups;
        _wissen = wissen;
        _wochen = wochen;
    }

    /// <summary>
    /// Legt die Wochen-Ziele des Feedcharts über die Sollwerte des Phasenprofils.
    /// </summary>
    /// <remarks>
    /// <para>Nur wenn der Grow es ausdrücklich will — das Chart ist der
    /// Vorschlag eines Herstellers, kein Gesetz.</para>
    ///
    /// <para><b>Was übernommen wird und was nicht:</b> das Chart nennt EINE
    /// EC-Zahl je Woche, kein Band. Ein Ziel ohne Breite wäre unbrauchbar — jede
    /// Messung läge daneben. Deshalb wird das Band des Phasenprofils um die
    /// Chart-Zahl herum <b>zentriert</b>, seine Breite bleibt. Der pH kommt als
    /// echtes Band aus dem Chart, weil er dort als Spanne steht. Temperatur,
    /// ORP, VPD, PPFD und CO₂ rührt das Chart nicht an — davon weiss es nichts.</para>
    /// </remarks>
    public static HydroTargetValues MitFeedchart(HydroTargetValues basis, FeedChartColumn spalte)
    {
        var ergebnis = basis;

        if (spalte.EcTarget is { } ec)
        {
            var halbeBreite = (basis.EcMax - basis.EcMin) / 2;
            ergebnis = ergebnis with { EcMin = ec - halbeBreite, EcMax = ec + halbeBreite };
        }

        if (spalte.PhMin is { } phMin && spalte.PhMax is { } phMax)
        {
            ergebnis = ergebnis with { PhMin = phMin, PhMax = phMax };
        }

        return ergebnis;
    }

    /// <summary>
    /// Die Chart-Spalte, die für diesen Grow gerade gilt — oder null, wenn kein
    /// Programm gewählt ist, es kein Chart hat oder der Grow es nicht will.
    /// </summary>
    public (FeedChartColumn Spalte, string Herkunft)? ZielSpalteFuerGrow(GrowRun grow)
        => ZielSpalteFuerGrow(grow, _wissen.NutrientPrograms);

    /// <inheritdoc cref="ZielSpalteFuerGrow(GrowRun)"/>
    /// <remarks>
    /// Statisch, weil der Live-Bildschirm ein Singleton ist: haenge ich ihn an
    /// den Scoped-Mischplan, faellt genau das auf die Fuesse, was hier keiner
    /// sucht — ein Dienst, der die erste Anfrage fuer immer festhaelt.
    /// </remarks>
    public static (FeedChartColumn Spalte, string Herkunft)? ZielSpalteFuerGrow(
        GrowRun grow, IEnumerable<NutrientProgramDefinition> programme)
    {
        if (!grow.UseFeedChartTargets || string.IsNullOrWhiteSpace(grow.FeedProgramId)) return null;

        var programm = programme.FirstOrDefault(
            p => string.Equals(p.Id, grow.FeedProgramId, StringComparison.OrdinalIgnoreCase));
        if (programm?.FeedChart is not { Columns.Count: > 0 } chart) return null;

        var spalte = SpalteFuer(chart, grow);
        return spalte is null ? null : (spalte, $"{programm.Name} · {spalte.Label}");
    }

    public Mischplan? FuerGrow(int growId)
    {
        var grow = _grows.GetGrow(growId);
        if (grow is null) return null;

        if (string.IsNullOrWhiteSpace(grow.FeedProgramId))
        {
            return Leer("Kein Düngerprogramm gewählt — am Grow unter Bearbeiten festlegen.");
        }

        var programm = _wissen.NutrientPrograms.FirstOrDefault(
            p => string.Equals(p.Id, grow.FeedProgramId, StringComparison.OrdinalIgnoreCase));
        if (programm is null)
        {
            return Leer($"Das Programm „{grow.FeedProgramId}“ gibt es im Wissen nicht mehr.");
        }

        if (programm.FeedChart is not { Columns.Count: > 0 } chart)
        {
            return Leer($"{programm.Name} hat noch kein Zahlen-Chart hinterlegt — der Plan kann keine Mengen nennen.",
                programm.Name);
        }

        var volumen = grow.SystemId is { } systemId
            ? _setups.GetSystem(systemId) is { } anlage
                ? HydroSetupMappingVolumen(anlage)
                : null
            : null;

        var spalte = SpalteFuer(chart, grow);
        if (spalte is null)
        {
            return Leer("Für die aktuelle Phase kennt das Chart keine Spalte.", programm.Name);
        }

        var zeilen = spalte.Items.Select(item =>
        {
            var proLiter = item.MinMlPerLiter == item.MaxMlPerLiter
                ? item.MinMlPerLiter.ToString("0.##", AppCulture.German)
                : $"{item.MinMlPerLiter.ToString("0.##", AppCulture.German)}–{item.MaxMlPerLiter.ToString("0.##", AppCulture.German)}";
            return new MischplanZeile(
                item.Component,
                proLiter,
                volumen is { } v ? Math.Round(item.MinMlPerLiter * v, 0) : 0,
                volumen is { } v2 ? Math.Round(item.MaxMlPerLiter * v2, 0) : 0);
        }).ToList();

        var herkunft = $"{programm.Name} · {spalte.Label}"
            + (volumen is { } vol ? $" · gerechnet auf {vol.ToString("0.#", AppCulture.German)} L" : "")
            + (string.IsNullOrWhiteSpace(chart.Note) ? "" : $" — {chart.Note}");

        return new Mischplan(
            programm.Name,
            spalte.Label,
            volumen,
            zeilen,
            spalte.EcTarget,
            spalte.PhMin,
            spalte.PhMax,
            herkunft,
            volumen is null ? "Kein Volumen an der Anlage hinterlegt — die Spalten zeigen deshalb nur ml je Liter." : null,
            grow.UseFeedChartTargets);
    }

    private static Mischplan Leer(string luecke, string? programmName = null)
        => new(programmName, null, null, [], null, null, null, null, luecke);

    private static double? HydroSetupMappingVolumen(GrowSystem anlage)
        => Api.Mapping.HydroSetupMapping.CalculateTotalVolumeLiters(
            anlage.PotCount, anlage.PotSizeLiters, anlage.ReservoirLiters);

    /// <summary>
    /// Die Spalte des Charts, die zur Lage des Grows passt.
    /// </summary>
    /// <remarks>
    /// Wochen über das Chart hinaus halten die letzte Spalte der Phase — Woche
    /// 6 einer 4-Wochen-Veg mischt weiter wie Woche 4, statt ins Leere zu
    /// laufen. Sortenabhängig verschieben bleibt Sache des Betreibers; genau
    /// das sagt die Chart-Notiz.
    /// </remarks>
    public static FeedChartColumn? SpalteFuer(FeedChartDefinition chart, GrowRun grow)
    {
        var stage = GrowStageResolver.Resolve(grow, DateTime.Today);

        string chartStage = stage switch
        {
            GrowStage.Seedling or GrowStage.Clone => "Clone",
            GrowStage.Veg => "Veg",
            GrowStage.Transition or GrowStage.Flower => "Flower",
            GrowStage.Finish => "Finish",
            _ => "Finish",
        };

        var kandidaten = chart.Columns.Where(c => string.Equals(c.Stage, chartStage, StringComparison.OrdinalIgnoreCase)).ToList();
        if (kandidaten.Count == 0) return null;

        var wochenSpalten = kandidaten.Where(c => c.Week is not null).OrderBy(c => c.Week).ToList();
        if (wochenSpalten.Count == 0)
        {
            // Sonderspalten (Vorweichen/Anfüttern/Flush): die letzte ist der
            // Normalfall — beim Klon ist das „Anfüttern".
            return kandidaten[^1];
        }

        var woche = WocheInPhase(grow, chartStage);
        return wochenSpalten.LastOrDefault(c => c.Week <= Math.Max(1, woche)) ?? wochenSpalten[0];
    }

    /// <summary>Woche innerhalb der Chart-Phase, ab 1.</summary>
    /// <remarks>
    /// Fork AI: öffentlich, seit der Banner „Gilt gerade" zeigen soll, dass eine
    /// Spalte GEHALTEN wird. Streckt man die Vegi über die letzte Vega-Spalte
    /// hinaus, bleibt <see cref="SpalteFuer"/> auf dieser stehen — richtig, aber
    /// stumm. Die Antwort darauf muss aus derselben Rechnung kommen wie die
    /// Spaltenwahl; eine zweite Zählung im Controller wäre die nächste
    /// abweichende Auskunft.
    /// </remarks>
    public static int WocheInPhase(GrowRun grow, string chartStage)
    {
        var heute = DateTime.Today;

        // Blütewochen zählen ab Blütebeginn — beim Photoperioden-Grow ist das
        // der Flip, bei der Autoflower der gerechnete Start aus dem
        // GrowStageResolver. Ohne diesen Anker bekam eine Autoflower in
        // Blütewoche 2 die Spalte ihrer GESAMTwoche — falsche ml und ein
        // falsches EC-Ziel.
        if (chartStage == "Flower" && (grow.FlipDate?.Date ?? GrowStageResolver.AutoflowerBluetenStart(grow)) is { } bluetenStart)
        {
            return Math.Max(1, ((heute - bluetenStart).Days / 7) + 1);
        }

        var start = grow.VegStartedAt?.Date ?? grow.StartDate.Date;
        var ende = chartStage == "Flower" ? heute : (grow.FlipDate?.Date ?? heute);
        return Math.Max(1, ((ende - start).Days / 7) + 1);
    }
}
