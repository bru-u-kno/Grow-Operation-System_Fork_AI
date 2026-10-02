using GrowDiary.Web.Models;
using GrowDiary.Web.Services.Knowledge.Schema;

namespace GrowDiary.Web.Services.GrowPlan;

/// <summary>
/// Fork AI (02.10.2026): welche Spalte eines Charts für welche Woche einer Phase
/// steht — und das Anhängen von Wochen, wenn eine Phase länger läuft als der Plan.
/// </summary>
/// <remarks>
/// <para><b>Entscheidung des Nutzers.</b> „Nicht nur die Blüte starr auf acht
/// oder neun Wochen: erstreckt sich die Blüte über zehn Wochen, zeigt das Schema
/// auch zehn Wochen. Genauso in der Bewurzelungsphase und in der vegetativen
/// Phase." Die zusätzliche Woche übernimmt die Werte der letzten Woche der
/// Phase, ist als „verlängert" markiert, im Plan bearbeitbar und steht im
/// Änderungsbuch. Bis dahin hielt <see cref="MischplanService.SpalteFuer(FeedChartDefinition, GrowRun, DateTime)"/>
/// stumm die letzte Spalte — Woche 10 zeigte „Blütewoche 9".</para>
///
/// <para><b>Die Anzucht.</b> Programme führen dort keine Wochen, sondern
/// Sonderspalten („Bewurzelung", Athena: „Vorweichen" und „Anfüttern"). Die
/// letzte davon ist die, die der Mischplan in der Anzucht immer gewählt hat; sie
/// zählt als Woche 1. Ab Woche 2 wird sie fortgeschrieben („Anzuchtwoche 2" —
/// „Anzucht" ist der Name, den der Zeitstrahl und die Erinnerung für die Phase
/// benutzen). Die übrigen Sonderspalten (Vorweichen, Flush) wachsen nicht: sie
/// sind Schritte, keine Wochen.</para>
///
/// <para>Rein und ohne Datenbank — das Speichern und das Änderungsbuch macht
/// <see cref="GrowPlanService.WochenNachziehen"/>.</para>
/// </remarks>
public static class Planwochen
{
    /// <summary>Die Chart-Phasen, deren Wochen mitwachsen — in Plan-Reihenfolge.</summary>
    public static readonly IReadOnlyList<string> WachsendePhasen = ["Clone", "Veg", "Flower"];

    /// <summary>Die Chart-Phase („Clone", „Veg", „Flower", „Finish") zu einer Wachstumsstufe.</summary>
    /// <remarks>
    /// Sämling und Steckling teilen sich die Anzucht-Spalten, Übergang und Blüte die
    /// Blütewochen — wie seit jeher im Mischplan.
    /// </remarks>
    public static string ChartPhase(GrowStage stufe) => stufe switch
    {
        GrowStage.Seedling or GrowStage.Clone => "Clone",
        GrowStage.Veg => "Veg",
        GrowStage.Transition or GrowStage.Flower => "Flower",
        _ => "Finish",
    };

    /// <summary>
    /// Die Wochen einer Chart-Phase, aufsteigend: nummerierte Spalten mit ihrer
    /// Nummer — in der Anzucht dazu die letzte Sonderspalte als Woche 1.
    /// </summary>
    public static List<(FeedChartColumn Spalte, int Woche)> Wochen(IEnumerable<FeedChartColumn> spalten, string chartPhase)
    {
        var kandidaten = spalten.Where(c => string.Equals(c.Stage, chartPhase, StringComparison.OrdinalIgnoreCase)).ToList();
        var wochen = kandidaten.Where(c => c.Week is not null).Select(c => (Spalte: c, Woche: c.Week!.Value)).ToList();
        if (IstAnzucht(chartPhase)
            && kandidaten.LastOrDefault(c => c.Week is null) is { } basis
            && !wochen.Any(w => w.Woche <= 1))
        {
            wochen.Add((basis, 1));
        }
        return wochen.OrderBy(w => w.Woche).ToList();
    }

    private static bool IstAnzucht(string chartPhase) => string.Equals(chartPhase, "Clone", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Wie viele Wochen jede wachsende Phase an diesem Stichtag braucht — so viele,
    /// wie sie bisher dauert bzw. gedauert hat. Eine Phase, die noch nicht begonnen
    /// hat, braucht keine.
    /// </summary>
    public static IEnumerable<(string ChartPhase, int Wochen)> Bedarf(Phasenstand stand)
    {
        yield return ("Clone", stand.WocheIn("Clone"));
        if (stand.VegAb is { } vegi && vegi <= stand.Stichtag) yield return ("Veg", stand.WocheIn("Veg"));
        if (stand.BlueteAb is { } bluete && bluete <= stand.Stichtag) yield return ("Flower", stand.WocheIn("Flower"));
    }

    /// <summary>Fehlt dem Plan eine Woche, die eine Phase schon erreicht hat?</summary>
    public static bool FehltEineWoche(GrowPlanInhalt inhalt, Phasenstand stand)
        => Bedarf(stand).Any(b => Wochen(inhalt.Chart.Columns, b.ChartPhase) is { Count: > 0 } wochen && wochen[^1].Woche < b.Wochen);

    /// <summary>
    /// Hängt jeder Phase, die länger läuft als der Plan, die fehlenden Wochen an.
    /// </summary>
    /// <returns>Die neuen Spalten mit der Woche, deren Werte sie übernommen haben —
    /// in der Reihenfolge des Anhängens. Leer, wenn nichts fehlt.</returns>
    /// <remarks>
    /// <para>Je neue Woche eine Kopie der bis dahin letzten Woche der Phase: Werte,
    /// Dosierung, Herkunft der Felder und eine abweichende Nacht-Einstellung. Was
    /// der Nutzer an der letzten Woche geändert hat, geht also mit — er hat es für
    /// die laufende Phase so gewollt. Woche 11 übernimmt von Woche 10.</para>
    /// <para>Die neue Spalte steht direkt hinter ihrer Vorlage, damit der Plan in
    /// Phasen-Reihenfolge bleibt (Vegiwoche 5 vor Blütewoche 1). Eine Phase ohne
    /// Wochen im Plan bekommt keine: es gibt nichts, dessen Werte sie übernehmen
    /// könnte.</para>
    /// <para>Nie wieder entfernt: auch wenn ein Phasenbeginn später verschoben
    /// wird, bleibt die Woche samt etwaiger Änderungen stehen — sie hat dann nur
    /// keinen Zeitraum mehr (<see cref="PlanAuswertung.Zeitraeume"/>).</para>
    /// </remarks>
    public static List<(FeedChartColumn Neu, FeedChartColumn Vorlage)> Anhaengen(GrowPlanInhalt inhalt, Phasenstand stand)
    {
        var neu = new List<(FeedChartColumn, FeedChartColumn)>();
        foreach (var (phase, bedarf) in Bedarf(stand))
        {
            var wochen = Wochen(inhalt.Chart.Columns, phase);
            if (wochen.Count == 0) continue;
            var (vorlage, letzte) = wochen[^1];

            while (letzte < bedarf)
            {
                letzte++;
                var spalte = Kopie(vorlage);
                spalte.Stage = vorlage.Stage;
                spalte.Week = letzte;
                spalte.Id = FreieId(inhalt, phase, letzte);
                spalte.Label = GrowPlanBauer.Wochenname(spalte);

                var position = inhalt.Chart.Columns.IndexOf(vorlage);
                inhalt.Chart.Columns.Insert(position + 1, spalte);

                if (inhalt.Herkunft.TryGetValue(vorlage.Id, out var felder))
                {
                    inhalt.Herkunft[spalte.Id] = new Dictionary<string, string>(felder, StringComparer.OrdinalIgnoreCase);
                }
                if (inhalt.NachtWieTagJeWoche.TryGetValue(vorlage.Id, out var nacht))
                {
                    inhalt.NachtWieTagJeWoche[spalte.Id] = nacht;
                }
                inhalt.Verlaengert[spalte.Id] = vorlage.Id;

                neu.Add((spalte, vorlage));
                vorlage = spalte;
            }
        }
        return neu;
    }

    /// <summary>
    /// Id einer angehängten Woche — im Muster der Programme (<c>veg-w5</c>,
    /// <c>flower-w10</c>, für die Anzucht <c>clone-w2</c>), bei Kollision mit Zusatz.
    /// </summary>
    private static string FreieId(GrowPlanInhalt inhalt, string phase, int woche)
    {
        var grund = $"{phase.ToLowerInvariant()}-w{woche}";
        var id = grund;
        for (var n = 2; inhalt.Chart.Columns.Any(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)); n++)
        {
            id = $"{grund}-{n}";
        }
        return id;
    }

    private static FeedChartColumn Kopie(FeedChartColumn spalte)
    {
        var chart = GrowPlanBauer.Kopie(new FeedChartDefinition { Columns = [spalte] });
        return chart.Columns[0];
    }
}
