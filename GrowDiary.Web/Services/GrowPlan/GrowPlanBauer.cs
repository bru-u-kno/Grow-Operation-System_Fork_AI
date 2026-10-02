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
        int bluetewochen,
        StartMaterial startMaterial)
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
            spalte.Label = Wochenname(spalte, startMaterial);
        }

        return inhalt;
    }

    /// <summary>
    /// Fork AI (02.10.2026): der Name der Anzucht im Plan des Grows — die EINE
    /// Stelle, an der er entsteht. Steckling: „Bewurzelung", Samen: „Anzucht".
    /// </summary>
    /// <remarks>
    /// <para><b>Entscheidung des Nutzers (02.10.2026).</b> Der Name richtet sich
    /// nach dem Startmaterial. Ein Steckling bewurzelt sich (Knopf „Bewurzelung
    /// abgeschlossen"); ein Samen keimt und wächst als Sämling — dieselbe Phase
    /// heißt in der ganzen Oberfläche „Anzucht" (Zeitstrahl „9 T Anzucht",
    /// Erinnerung „Anzucht Tag N — Vegi-Beginn bestätigen?" in
    /// <see cref="Phasenanker"/>). Maßgeblich ist das Startmaterial, nicht der
    /// Einstieg: ein Steckling mit Einstieg „Sämling" bleibt ein Steckling.</para>
    /// <para><b>Alles außer <see cref="StartMaterial.Clone"/> heißt „Anzucht".</b>
    /// Dieselbe Grenze zieht <see cref="Phasenanker"/> (nur der Steckling hat die
    /// Anzuchtart Bewurzelung), und ein fehlendes oder unlesbares Startmaterial
    /// liest die Datenbank als <see cref="StartMaterial.Seed"/>
    /// (<c>GrowCoreRepository</c>, Spaltenvorgabe <c>'Seed'</c>). „Bewurzelung"
    /// an einem Grow, von dem niemand weiß, dass er ein Steckling ist, wäre eine
    /// Behauptung; „Anzucht" stimmt für beide.</para>
    /// </remarks>
    public static string Anzuchtname(StartMaterial startMaterial)
        => startMaterial == StartMaterial.Clone ? "Bewurzelung" : "Anzucht";

    /// <summary>
    /// Fork AI (forkai.132): Der Name einer Woche im Plan des Grows — einheitlich,
    /// egal wie der Hersteller sie nennt: „Bewurzelung" bzw. „Anzucht",
    /// „Vegiwoche 2", „Blütewoche 5", „Flush".
    /// </summary>
    /// <remarks>
    /// <para><b>Nur im Plan des Grows.</b> Die Herstellertabelle im Programm (Wissen)
    /// behält ihre Begriffe — „Vega", „Flores" sind die Namen des Düngers, und die
    /// bleiben unangetastet (Bru, 21.09.2026). Der Grow-Plan ist eine Kopie; nur
    /// dort wird umbenannt.</para>
    /// <para><b>Die Anzucht</b> heißt nach dem Startmaterial
    /// (<see cref="Anzuchtname"/>) — die Sonderspalte „Bewurzelung" bzw.
    /// „Anzucht", angehängte Wochen (Fork AI, 02.10.2026) „Bewurzelung 2" bzw.
    /// „Anzucht 2", passend zur Spalte davor. Bis zum 02.10.2026 stand dort
    /// „Anzuchtwoche 2"; der Nutzer sah zwei Namen für dieselbe Phase. Nicht
    /// „Bewurzelungswoche 2": das brach in der Plan-Tabelle am Grow bei 390 px mitten
    /// im Wort (Prüfer, 02.10.2026). <see cref="WochennamenAngleichen"/> benennt
    /// gespeicherte Pläne beim Start um.</para>
    /// <para><b>Angehängte Vegi- und Blütewochen</b> heißen wie ihre Phase:
    /// „Vegiwoche 5", „Blütewoche 10".</para>
    /// <para><b>Mehrere Schritte einer Phase</b> ohne Wochennummer (Athena: „Klon ·
    /// Vorweichen" / „Klon · Anfüttern") behalten ihren Namen — sie wären sonst nicht
    /// mehr zu unterscheiden.</para>
    /// </remarks>
    public static string Wochenname(FeedChartColumn spalte, StartMaterial startMaterial)
    {
        var label = spalte.Label ?? string.Empty;
        var anzucht = Anzuchtname(startMaterial);
        return Phase(spalte.Stage) switch
        {
            GrowStage.Veg when spalte.Week is { } w => $"Vegiwoche {w}",
            GrowStage.Flower when spalte.Week is { } w => $"Blütewoche {w}",
            // Fork AI (02.10.2026): angehängte Anzucht-Wochen (Planwochen.Anhaengen) —
            // die Sonderspalte davor ist Woche 1 und behält ihren Namen.
            GrowStage.Clone or GrowStage.Seedling when spalte.Week is { } w => $"{anzucht} {w}",
            // Die Sonderspalte: der Herstellername („Root · Bewurzelung"), das eigene
            // Raster und beide Namen, die sie im Plan schon trug — ein Grow, dessen
            // Startmaterial geändert wurde, wechselt so auch zurück.
            GrowStage.Clone or GrowStage.Seedling when label.Contains("Root", StringComparison.OrdinalIgnoreCase)
                || label.Contains("Bewurzel", StringComparison.OrdinalIgnoreCase)
                || label.Contains("Anzucht", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(label) => anzucht,
            GrowStage.Finish when label.Contains("Flush", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(label) => "Flush",
            _ => string.IsNullOrWhiteSpace(label) ? spalte.Id : label,
        };
    }

    /// <summary>
    /// Fork AI (forkai.132): Wochennamen eines bestehenden Plans angleichen; true, wenn sich etwas geändert hat.
    /// </summary>
    /// <remarks>
    /// Seit 02.10.2026 mit dem Startmaterial des Grows: ein Samen-Grow bekommt
    /// „Anzucht"/„Anzucht 2" statt „Bewurzelung"/„Bewurzelung 2"/„Anzuchtwoche 2",
    /// ein Steckling „Bewurzelung 2" statt „Anzuchtwoche 2".
    /// </remarks>
    public static bool WochennamenAngleichen(GrowPlanInhalt inhalt, StartMaterial startMaterial)
    {
        var geaendert = false;
        foreach (var spalte in inhalt.Chart.Columns)
        {
            var neu = Wochenname(spalte, startMaterial);
            if (string.Equals(neu, spalte.Label, StringComparison.Ordinal)) continue;
            spalte.Label = neu;
            geaendert = true;
        }
        return geaendert;
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

    /// <remarks>
    /// Der Name der Anzucht-Spalte ist hier nur ein Platzhalter — den Namen nach
    /// dem Startmaterial gibt ihr erst <see cref="Wochenname"/> in
    /// <see cref="AusProgramm"/>, wie jeder Spalte aus einem Programm.
    /// </remarks>
    private static FeedChartDefinition Raster(string programmName, int vegiWochen, int bluetewochen)
    {
        var chart = new FeedChartDefinition
        {
            Note = $"Wochenraster beim Anlegen erzeugt — {programmName} nennt keine Wochenwerte. Dosierung bitte eintragen.",
        };

        chart.Columns.Add(new FeedChartColumn { Id = "root", Label = "Bewurzelung", Stage = "Clone" });
        for (var w = 1; w <= Math.Max(1, vegiWochen); w++)
        {
            chart.Columns.Add(new FeedChartColumn { Id = $"veg-w{w}", Label = $"Vegiwoche {w}", Stage = "Veg", Week = w });
        }
        for (var w = 1; w <= Math.Max(1, bluetewochen); w++)
        {
            chart.Columns.Add(new FeedChartColumn { Id = $"flower-w{w}", Label = $"Blütewoche {w}", Stage = "Flower", Week = w });
        }
        chart.Columns.Add(new FeedChartColumn { Id = "flush", Label = "Flush", Stage = "Finish" });
        return chart;
    }

    /// <summary>
    /// Das EC-Band um das Wochenziel — mit der halben Breite des Standards. Nur,
    /// wenn das Programm kein Band nennt. Gibt zurück, ob etwas eingetragen wurde.
    /// </summary>
    public static bool EcBandFuellen(GrowPlanInhalt inhalt, FeedChartColumn spalte, HydroTargetValues? t)
    {
        if (spalte.EcMin is not null || spalte.EcMax is not null) return false;
        if (spalte.EcTarget is not { } ziel || t is null) return false;
        var halbeBreite = (t.EcMax - t.EcMin) / 2;
        spalte.EcMin = Math.Round(ziel - halbeBreite, 3);
        spalte.EcMax = Math.Round(ziel + halbeBreite, 3);
        inhalt.HerkunftSetzen(spalte.Id, "ecMin", GrowPlanHerkunft.Standard);
        inhalt.HerkunftSetzen(spalte.Id, "ecMax", GrowPlanHerkunft.Standard);
        return true;
    }

    /// <summary>
    /// Fork AI (forkai.130): „Luft Nacht" einmalig aus dem Tagwert vorbefüllen —
    /// Tag minus <see cref="WochenplanSyncService.Nachtabsenkung"/>. Danach ist es ein
    /// gewöhnlicher Planwert, den der Nutzer frei ändert; eine feste Regel im
    /// Hintergrund gibt es nicht mehr.
    /// </summary>
    public static bool NachtLuftFuellen(GrowPlanInhalt inhalt, FeedChartColumn spalte)
    {
        if (spalte.AirTempNightC is not null || spalte.AirTempC is not { } tag) return false;
        spalte.AirTempNightC = Math.Round(tag - WochenplanSyncService.Nachtabsenkung, 1);
        inhalt.HerkunftSetzen(spalte.Id, "airTempNightC", GrowPlanHerkunft.Standard);
        return true;
    }

    private static void LueckenFuellen(GrowPlanInhalt inhalt, FeedChartColumn spalte, HydroTargetValues? t)
    {
        EcBandFuellen(inhalt, spalte, t);
        NachtLuftFuellen(inhalt, spalte);
        foreach (var feld in Wochenwertfelder.Alle)
        {
            if (feld.Optional) continue;
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
        // Band ohne Ziel: das Standardband selbst (mit Ziel legt EcBandFuellen es um das Ziel).
        "ecMin" => t.EcMin,
        "ecMax" => t.EcMax,
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
