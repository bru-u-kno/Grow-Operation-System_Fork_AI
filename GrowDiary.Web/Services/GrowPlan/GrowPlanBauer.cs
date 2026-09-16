using System.Text.Json;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services.Knowledge.Schema;

namespace GrowDiary.Web.Services.GrowPlan;

/// <summary>
/// Fork AI (Grow-Plan, 16.09.2026): baut aus einem Düngeprogramm den Plan eines Grows.
/// </summary>
/// <remarks>
/// <para><b>Lücken werden einmal gefüllt, nicht dauerhaft nachgeschlagen.</b>
/// Nennt das Programm einen Wert nicht, kommt er beim Anlegen aus dem
/// mitgelieferten Standard (RDWC/DWC) und wird als
/// <see cref="GrowPlanHerkunft.Standard"/> vermerkt. Danach gehört er zum Plan —
/// ein späteres Profil ändert den Grow nicht mehr. Luftfeuchte und
/// Lufttemperatur kennt kein Standard; sie bleiben leer und tragen
/// <see cref="GrowPlanHerkunft.Fehlt"/>.</para>
/// <para><b>Programme ohne Wochenraster</b> (Canna Aqua, VBX) bekommen eines:
/// Bewurzelung, die Vegi-Wochen des Grows, die Blütewochen der Sorte, Flush.
/// Die Dosierung bleibt dort leer — das Programm nennt keine Zahlen.</para>
/// <para>Rein und ohne Datenbank: alles, was von außen kommt, wird übergeben.</para>
/// </remarks>
public static class GrowPlanBauer
{
    public const int StandardVegiWochen = 4;
    public const int StandardBluetewochen = 8;

    public static GrowPlanInhalt AusProgramm(
        NutrientProgramDefinition programm,
        Func<GrowStage, HydroTargetValues?> standard,
        int vegiWochen,
        int bluetewochen)
    {
        var inhalt = new GrowPlanInhalt
        {
            ProgrammId = programm.Id,
            ProgrammName = programm.Name,
            Chart = programm.FeedChart is { Columns.Count: > 0 } chart
                ? Kopie(chart)
                : Raster(programm.Name, vegiWochen, bluetewochen),
        };

        foreach (var spalte in inhalt.Chart.Columns)
        {
            LueckenFuellen(inhalt, spalte, standard(Phase(spalte.Stage)));
        }

        return inhalt;
    }

    /// <summary>Tiefe Kopie über JSON — der Plan darf das geladene Programm nie mitändern.</summary>
    public static FeedChartDefinition Kopie(FeedChartDefinition chart)
        => JsonSerializer.Deserialize<FeedChartDefinition>(
               JsonSerializer.Serialize(chart, GrowPlanRepository.Json), GrowPlanRepository.Json)
           ?? new FeedChartDefinition();

    /// <summary>Tiefe Kopie eines ganzen Planinhalts.</summary>
    public static GrowPlanInhalt Kopie(GrowPlanInhalt inhalt)
        => JsonSerializer.Deserialize<GrowPlanInhalt>(
               JsonSerializer.Serialize(inhalt, GrowPlanRepository.Json), GrowPlanRepository.Json)
           ?? new GrowPlanInhalt();

    /// <summary>Die Wachstumsphase, die zu einer Chart-Phase gehört.</summary>
    public static GrowStage Phase(string chartStage) => chartStage.ToLowerInvariant() switch
    {
        "clone" => GrowStage.Clone,
        "seedling" => GrowStage.Seedling,
        "veg" => GrowStage.Veg,
        "transition" => GrowStage.Transition,
        "flower" => GrowStage.Flower,
        _ => GrowStage.Finish,
    };

    private static FeedChartDefinition Raster(string programmName, int vegiWochen, int bluetewochen)
    {
        var chart = new FeedChartDefinition
        {
            Note = $"Wochenraster beim Anlegen erzeugt — {programmName} nennt keine Wochenwerte. Dosierung bitte eintragen.",
        };

        chart.Columns.Add(new FeedChartColumn { Id = "root", Label = "Bewurzelung", Stage = "Clone" });
        for (var w = 1; w <= Math.Max(1, vegiWochen); w++)
        {
            chart.Columns.Add(new FeedChartColumn { Id = $"veg-w{w}", Label = $"Vegi · Woche {w}", Stage = "Veg", Week = w });
        }
        for (var w = 1; w <= Math.Max(1, bluetewochen); w++)
        {
            chart.Columns.Add(new FeedChartColumn { Id = $"flower-w{w}", Label = $"Blüte · Woche {w}", Stage = "Flower", Week = w });
        }
        chart.Columns.Add(new FeedChartColumn { Id = "flush", Label = "Flush", Stage = "Finish" });
        return chart;
    }

    private static void LueckenFuellen(GrowPlanInhalt inhalt, FeedChartColumn spalte, HydroTargetValues? t)
    {
        foreach (var feld in Wochenwertfelder.Alle)
        {
            if (feld.Lesen(spalte) is not null) continue;

            var wert = t is null ? null : AusStandard(feld.Name, t);
            if (wert is { } zahl)
            {
                feld.Schreiben(spalte, zahl);
                inhalt.HerkunftSetzen(spalte.Id, feld.Name, GrowPlanHerkunft.Standard);
            }
            else
            {
                inhalt.HerkunftSetzen(spalte.Id, feld.Name, GrowPlanHerkunft.Fehlt);
            }
        }
    }

    private static double? AusStandard(string feld, HydroTargetValues t) => feld switch
    {
        "ecTarget" => Math.Round((t.EcMin + t.EcMax) / 2, 2),
        "phMin" => t.PhMin,
        "phMax" => t.PhMax,
        "orpMin" => t.OrpMin,
        "orpMax" => t.OrpMax,
        "waterTempDayC" => t.WaterTempDayC,
        "waterTempNightC" => t.WaterTempNightC,
        "vpdMin" => t.VpdMin,
        "vpdMax" => t.VpdMax,
        "co2Min" => t.Co2Min,
        "co2Max" => t.Co2Max,
        "ppfdMin" => t.PpfdMin,
        "ppfdMax" => t.PpfdMax,
        // RH und Luft kennt kein Standard — bleiben leer.
        _ => null,
    };
}
