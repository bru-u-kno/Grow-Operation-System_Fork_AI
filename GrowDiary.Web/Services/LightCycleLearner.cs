using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>Der beobachtete Lichtzyklus eines Zelts.</summary>
/// <param name="HoursOn">Stunden Licht je Tag, auf eine Nachkommastelle.</param>
/// <param name="OnAt">Wann das Licht angeht (Ortszeit des Zelts).</param>
/// <param name="OffAt">Wann es ausgeht.</param>
/// <param name="Days">Über wie viele Tage beobachtet.</param>
public sealed record LearnedCycle(double HoursOn, TimeOnly OnAt, TimeOnly OffAt, int Days)
{
    /// <summary>„18/6", „12/12" — oder die krumme Wahrheit, wenn es keine ist.</summary>
    public string Label => IsClose(18) ? "18/6"
        : IsClose(12) ? "12/12"
        : IsClose(20) ? "20/4"
        : IsClose(24) ? "24/0"
        : $"{HoursOn:0.#}/{24 - HoursOn:0.#}";

    /// <summary>Eine halbe Stunde Toleranz — Schaltuhren und Sensoren sind nicht taktgenau.</summary>
    private bool IsClose(double hours) => Math.Abs(HoursOn - hours) <= 0.5;

    /// <summary>Ein Zyklus mit 13 h oder weniger Licht ist ein Blüte-Zyklus.</summary>
    public bool LooksLikeFlower => HoursOn <= 13.0;

    /// <summary>Ab 16 h ist es eindeutig vegetativ.</summary>
    public bool LooksLikeVeg => HoursOn >= 16.0;
}

/// <summary>
/// Liest den Lichtzyklus aus den beobachteten Schaltvorgängen.
/// </summary>
/// <remarks>
/// <para>Cultivate Control zeichnet jede An/Aus-Flanke des Licht-Sensors ohnehin auf. Daraus
/// ergibt sich der Zyklus von selbst — niemand muss ihn eintragen, und niemand
/// muss ihn pflegen, wenn er sich ändert.</para>
///
/// <para>Das schlägt auch den Lichtplan: beobachtete Flanken <b>sind</b> die
/// richtige Uhr. Ein Plan in der falschen Zeitzone (der Add-on-Container läuft
/// gern auf UTC) geht daneben, ein gemessener Einschaltzeitpunkt nie.</para>
///
/// <para>Gerechnet wird über volle An-Phasen: von einem „an" bis zum nächsten
/// „aus". Der Median über die letzten Tage — eine einzelne verlängerte Phase
/// (jemand hat zum Gießen das Licht angelassen) verschiebt ihn nicht.</para>
/// </remarks>
public static class LightCycleLearner
{
    /// <summary>So viele volle An-Phasen braucht es mindestens für eine Aussage.</summary>
    public const int MinPhases = 2;

    /// <summary>
    /// Der Zyklus aus den Flanken, oder null wenn es noch zu wenige sind.
    /// </summary>
    /// <param name="transitions">Alle Flanken, Reihenfolge egal.</param>
    /// <param name="localOffset">
    /// Verschiebung von UTC auf die Uhrzeit im Zelt — nur für die Anzeige der
    /// Schaltzeiten. Die Dauer ist davon unberührt.
    /// </param>
    public static LearnedCycle? Learn(IReadOnlyList<LightTransitionEvent> transitions, TimeSpan localOffset)
    {
        var sortiert = transitions.OrderBy(t => t.OccurredAtUtc).ToList();

        var dauern = new List<double>();
        var anZeiten = new List<DateTime>();
        var ausZeiten = new List<DateTime>();

        for (var i = 0; i < sortiert.Count - 1; i++)
        {
            if (sortiert[i].Kind != LightTransitionKind.LightOn) continue;
            if (sortiert[i + 1].Kind != LightTransitionKind.LightOff) continue;

            var stunden = (sortiert[i + 1].OccurredAtUtc - sortiert[i].OccurredAtUtc).TotalHours;

            // Unter einer Stunde ist kein Zyklus, sondern ein Schaltflattern oder
            // jemand, der kurz nachgesehen hat. Über 24 h ist eine Lücke in den
            // Daten, kein Dauerlicht — dafür gäbe es gar kein „aus".
            if (stunden is < 1 or > 24) continue;

            dauern.Add(stunden);
            anZeiten.Add(sortiert[i].OccurredAtUtc);
            ausZeiten.Add(sortiert[i + 1].OccurredAtUtc);
        }

        if (dauern.Count < MinPhases) return null;

        return new LearnedCycle(
            Math.Round(Median(dauern), 1),
            MedianTime(anZeiten, localOffset),
            MedianTime(ausZeiten, localOffset),
            dauern.Count);
    }

    /// <summary>
    /// Passt der beobachtete Zyklus zur Phase, in der der Grow steht?
    /// </summary>
    /// <returns>Ein Klartext-Hinweis, oder null wenn alles zusammenpasst.</returns>
    /// <remarks>
    /// Der eigentliche Nutzen des Lernens. Zwei Fälle aus der Praxis:
    /// <list type="bullet">
    /// <item>Licht läuft 12/12, der Grow steht auf Veg — der Flip ist passiert,
    /// nur nicht eingetragen. Ohne Eintrag rechnet Cultivate Control mit Veg-Zielen
    /// weiter, und die sind in der Blüte zu scharf.</item>
    /// <item>Licht läuft 18/6, der Grow steht auf Blüte — dann stimmt etwas am
    /// Controller nicht, und das kostet die Ernte.</item>
    /// </list>
    /// Autoflower ist ausgenommen: die blüht bei jedem Zyklus, 18/6 in der
    /// Blüte ist dort völlig normal.
    /// </remarks>
    public static string? Mismatch(LearnedCycle cycle, GrowStage stage, SeedType seedType)
    {
        if (seedType == SeedType.Autoflower) return null;

        var inBluete = stage is GrowStage.Transition or GrowStage.Flower or GrowStage.Finish;
        var inVeg = stage is GrowStage.Seedling or GrowStage.Clone or GrowStage.Veg;

        if (inVeg && cycle.LooksLikeFlower)
        {
            return $"Das Licht läuft {cycle.Label}, der Grow steht aber noch auf {StageLabel(stage)}. "
                 + "Wenn du geflippt hast, trag den Flip ein — sonst rechnet Cultivate Control weiter mit Veg-Zielen.";
        }

        if (inBluete && cycle.LooksLikeVeg)
        {
            return $"Der Grow ist in der Blüte, das Licht läuft aber {cycle.Label}. "
                 + "Das verhindert die Blüte — Zeitschaltuhr oder Lichtsteuerung prüfen.";
        }

        return null;
    }

    private static string StageLabel(GrowStage stage) => stage switch
    {
        GrowStage.Seedling => "Sämling",
        GrowStage.Clone => "Steckling",
        GrowStage.Veg => "Vegetativ",
        _ => stage.ToString(),
    };

    private static double Median(List<double> werte)
    {
        var sortiert = werte.OrderBy(wert => wert).ToList();
        var mitte = sortiert.Count / 2;
        return sortiert.Count % 2 == 1 ? sortiert[mitte] : (sortiert[mitte - 1] + sortiert[mitte]) / 2;
    }

    /// <summary>
    /// Die typische Uhrzeit mehrerer Zeitpunkte.
    /// </summary>
    /// <remarks>
    /// Über Minuten seit Mitternacht gemittelt — und über den Median, damit ein
    /// einzelner Ausreisser (Stromausfall, manuelles Einschalten) die Zeit nicht
    /// verschiebt.
    /// </remarks>
    private static TimeOnly MedianTime(List<DateTime> zeitpunkte, TimeSpan offset)
        => MedianZeit(zeitpunkte, offset);

    /// <summary>
    /// Der Median über Uhrzeiten — auch wenn die Flanken um Mitternacht liegen.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Anlass (01.09.2026).</b> Die Rechnung sortierte Minuten seit
    /// Mitternacht und nahm die Mitte. Liegen die Flanken um 00:00, mischt das
    /// Werte nahe 0 mit Werten nahe 1440: aus 23:58, 23:59, 00:01, 00:02 wurde
    /// der Median 12:00 statt 00:00 — der gelernte Zyklus war um zwölf Stunden
    /// verschoben.</para>
    ///
    /// <para>Ein Blüte-Zelt mit 12/12 und Licht aus um Mitternacht ist der
    /// Normalfall, und die Flanken streuen um ein, zwei Minuten, weil sie aus
    /// dem Poll-Takt des Snapshot-Workers kommen.</para>
    ///
    /// <para><b>Die Lösung:</b> Uhrzeiten sind ein Kreis, keine Gerade. Liegen
    /// die Werte weiter als einen halben Tag auseinander, werden die kleinen um
    /// 24 Stunden aufgerollt, der Median gebildet und am Ende zurückgefaltet.
    /// Bei Flanken, die dicht beieinanderliegen — und das tun sie, sonst wäre
    /// es kein Zyklus — ist das eindeutig.</para>
    ///
    /// <para>Öffentlich, damit die Entscheidung eine eigene Prüfung bekommt:
    /// über <c>Lernen</c> wäre sie nur mit einem ganzen Flankenverlauf zu
    /// erreichen, und der Grenzfall ginge in der Kulisse unter.</para>
    /// </remarks>
    public static TimeOnly MedianZeit(List<DateTime> zeitpunkte, TimeSpan offset)
    {
        var minuten = zeitpunkte
            .Select(zeit => (zeit + offset).TimeOfDay.TotalMinutes)
            .OrderBy(wert => wert)
            .ToList();

        const double Tag = 24 * 60;

        // Spannen die Werte mehr als einen halben Tag, liegt der Bruch bei
        // Mitternacht: die kleinen gehoeren ans obere Ende.
        if (minuten.Count > 1 && minuten[^1] - minuten[0] > Tag / 2)
        {
            minuten = minuten
                .Select(wert => wert < Tag / 2 ? wert + Tag : wert)
                .OrderBy(wert => wert)
                .ToList();
        }

        var mitte = minuten.Count / 2;
        var median = minuten.Count % 2 == 1 ? minuten[mitte] : (minuten[mitte - 1] + minuten[mitte]) / 2;

        // Zurueckfalten — der Median kann jenseits von 24 h gelandet sein.
        return TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(median % Tag));
    }
}
