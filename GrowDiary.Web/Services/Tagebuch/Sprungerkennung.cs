namespace GrowDiary.Web.Services.Tagebuch;

/// <summary>Ein Wert aus dem Sensorverlauf, wie die Erkennung ihn braucht.</summary>
public readonly record struct Rohwert(DateTime Utc, double Wert);

/// <summary>
/// Eine Stufe im Sensorverlauf: vorher ruhig auf einem Wert, nachher ruhig auf
/// einem anderen, dazwischen höchstens <see cref="Sprungerkennung.HoechstensUebergang"/>.
/// </summary>
/// <param name="MetricKey">Die Messgröße, z. B. <c>reservoir-ec</c>.</param>
/// <param name="LetzteRuheUtc">Der letzte ruhige Wert vor der Stufe.</param>
/// <param name="NeueRuheUtc">Der erste ruhige Wert danach.</param>
/// <param name="Vorher">Median der ruhigen Werte davor.</param>
/// <param name="Nachher">Median der ruhigen Werte danach.</param>
public sealed record Sprung(string MetricKey, DateTime LetzteRuheUtc, DateTime NeueRuheUtc, double Vorher, double Nachher)
{
    public double Aenderung => Nachher - Vorher;

    /// <summary>Wie lange der Übergang gedauert hat, auf volle Minuten.</summary>
    public int DauerMinuten => (int)Math.Round((NeueRuheUtc - LetzteRuheUtc).TotalMinutes);
}

/// <summary>
/// Findet Sprünge im Sensorverlauf des Beckens — für die „Auffällig"-Zeilen im
/// Grow-Tagebuch (A-006).
/// </summary>
/// <remarks>
/// <para><b>Der Anlass.</b> Am 03.10.2026 fiel der EC in Zelt 1 zwischen 16:10
/// und 16:35 Ortszeit von 1,74 auf 1,61 — und eingetragen war nichts. Bru:
/// „Nachgefüllt?" Genau diese Frage soll das Tagebuch selbst stellen.</para>
///
/// <para><b>Wie ein echter Sprung aussieht</b> (aus Brus Rohdaten, 5-Minuten-Takt):
/// davor 45 Minuten zwischen 1,73 und 1,75, dann 1,29 · 1,67 · 1,62 · 1,70 —
/// das Wasser mischt sich, die Sonde zappelt —, danach ruhig bei 1,60–1,61. Ein
/// Vergleich zweier Nachbarwerte fände hier entweder vier Sprünge oder keinen.
/// Deshalb vergleicht die Erkennung <b>zwei ruhige Abschnitte</b>: den vor dem
/// Übergang mit dem danach, je über ihren Median.</para>
///
/// <para><b>Was deshalb nicht gemeldet wird.</b>
/// <list type="bullet">
/// <item>Messrauschen: es bewegt sich innerhalb eines ruhigen Abschnitts, nicht
/// zwischen zweien.</item>
/// <item>Ein einzelner Ausreißer (pH 3,66 für fünf Minuten) und eine Kalibrierung
/// im Becher: danach kehrt der Wert auf seine alte Stufe zurück.</item>
/// <item>Der Tagesgang und der langsame Anstieg durch die Pflanze: zu langsam für
/// <see cref="HoechstensUebergang"/>.</item>
/// <item>Lücken im Verlauf (Sensor offline): ohne Werte lässt sich nicht sagen,
/// wie schnell es ging.</item>
/// <item>Eine Kalibrierung, die die Anzeige verschiebt, ist eine echte Stufe — die
/// unterdrückt erst <see cref="TagebuchService"/>, weil sie den Eintrag kennt.</item>
/// </list></para>
/// </remarks>
public static class Sprungerkennung
{
    /// <summary>So lange muss ein Wert vor und nach der Stufe ruhig stehen.</summary>
    /// <remarks>
    /// Drei Viertelstunden: der Testbestand hat einen Wert je Viertelstunde, die
    /// Anlage einen je fünf Minuten. Darunter wäre ein Abschnitt mit drei
    /// Werten im Testbestand nie vollständig.
    /// </remarks>
    public static readonly TimeSpan Ruhefenster = TimeSpan.FromMinutes(45);

    /// <summary>So lange darf das Durchmischen dauern.</summary>
    /// <remarks>
    /// Brus Nachfüllen am 03.10.2026 brauchte 25 Minuten vom letzten ruhigen bis
    /// zum ersten ruhigen Wert, sein Wasserwechsel am 04.10. (Ablassen, Füllen,
    /// Anmischen) 50 Minuten. Was länger dauert, ist keine Stufe mehr, sondern
    /// eine Drift — die beobachten Trend-Wächter und Diagnose.
    /// </remarks>
    public static readonly TimeSpan HoechstensUebergang = TimeSpan.FromMinutes(60);

    /// <summary>Größter erlaubter Abstand zweier Werte.</summary>
    /// <remarks>Größer heißt: der Sensor war weg, und wie schnell es ging, ist unbekannt.</remarks>
    public static readonly TimeSpan GroessteLuecke = TimeSpan.FromMinutes(15);

    /// <summary>Mindestens so viele Werte je ruhigem Abschnitt.</summary>
    public const int MindestWerte = 3;

    /// <summary>Die Messgrößen des Beckens, auf die die Erkennung schaut.</summary>
    /// <remarks>
    /// Nur das Becken: Luft, Feuchte und VPD springen bei jedem Lichtwechsel —
    /// das ist der Plan, kein Ereignis.
    /// </remarks>
    public static readonly string[] Messgroessen = ["reservoir-ec", "reservoir-ph", "reservoir-level", "reservoir-level-cm"];

    /// <summary>
    /// EC: ab 5 % des Werts davor (mindestens 0,05 mS/cm).
    /// </summary>
    /// <remarks>
    /// Faustregel Grow OS, aus der Mischungsrechnung: wer 1/20 des Beckens mit
    /// Wasser auffüllt, verdünnt die Lösung um rund 5 %. Kleiner als das ist ein
    /// Nachfüllen nicht vom Tagesgang (±0,01) zu trennen. Brus Fall: 0,13 bei
    /// 1,74 — das Siebenfache des Rauschens.
    /// </remarks>
    public const double EcAnteil = 0.05;

    /// <summary>Untergrenze für den EC-Sprung — bei sehr dünner Lösung.</summary>
    public const double EcMindestens = 0.05;

    /// <summary>
    /// pH: ab der halben Breite des Komfortbands (<see cref="DeviationAnalyzerService.PhComfortMin"/>
    /// bis <see cref="DeviationAnalyzerService.PhComfortMax"/>), also 0,2.
    /// </summary>
    /// <remarks>
    /// Faustregel Grow OS: eine Stufe, die den pH mit einem Schlag um ein halbes
    /// Zielband verschiebt, ist eine Zugabe (pH−, Dünger, frisches Wasser). SOP-RDWC-CAN-N1
    /// §2.1 nennt 0,1–0,4 je TAG als normale Schwankung — 0,2 in Minuten ist
    /// keine Schwankung mehr. Die Zahl steht nicht hier, sondern kommt vom Band.
    /// </remarks>
    public static double PhSchwelle => (DeviationAnalyzerService.PhComfortMax - DeviationAnalyzerService.PhComfortMin) / 2;

    /// <summary>Wasserstand: ab 5 % des Werts davor — dieselbe 1/20-Regel wie beim EC.</summary>
    public const double PegelAnteil = 0.05;

    /// <summary>
    /// Ab welcher Änderung ein Sprung zählt — oder <c>null</c>, wenn die
    /// Messgröße nicht erkannt wird.
    /// </summary>
    public static double? Schwelle(string metricKey, double vorher) => metricKey switch
    {
        "reservoir-ec" => Math.Max(EcMindestens, Math.Abs(vorher) * EcAnteil),
        "reservoir-ph" => PhSchwelle,
        "reservoir-level" => Math.Max(1, Math.Abs(vorher) * PegelAnteil),
        // Das eTape zittert um LevelStability.ToleranceCm; das Dreifache ist sicher kein Zittern.
        "reservoir-level-cm" => Math.Max(LevelStability.ToleranceCm * 3, Math.Abs(vorher) * PegelAnteil),
        _ => null,
    };

    /// <summary>Die Regel in einem Satz — so steht sie in der Oberfläche.</summary>
    public static string Regel(string metricKey) => metricKey switch
    {
        "reservoir-ec" => "Gemeldet ab 5 % EC-Änderung in höchstens einer Stunde (Faustregel: so viel verdünnt 1/20 des Beckens Wasser).",
        "reservoir-ph" => "Gemeldet ab 0,2 pH in höchstens einer Stunde (Faustregel: ein halbes Zielband 5,8–6,2 auf einmal).",
        "reservoir-level" or "reservoir-level-cm" => "Gemeldet ab 5 % Wasserstand in höchstens einer Stunde (Faustregel: 1/20 des Beckens).",
        _ => string.Empty,
    };

    /// <summary>Alle Sprünge einer Messgröße in diesem Verlauf.</summary>
    /// <param name="metricKey">Die Messgröße.</param>
    /// <param name="werte">Die Rohwerte, in beliebiger Reihenfolge.</param>
    public static IReadOnlyList<Sprung> Finden(string metricKey, IEnumerable<Rohwert> werte)
    {
        var p = werte.Where(w => double.IsFinite(w.Wert)).OrderBy(w => w.Utc).ToArray();
        var funde = new List<Sprung>();
        if (Schwelle(metricKey, 1) is null || p.Length < MindestWerte * 2) return funde;

        var i = 0;
        while (i < p.Length)
        {
            if (Vorher(p, i) is not { } vor)
            {
                i++;
                continue;
            }

            var schwelle = Schwelle(metricKey, vor)!.Value;
            if (!Ruhig(p, VorherAnfang(p, i), i, vor, schwelle))
            {
                i++;
                continue;
            }

            var treffer = NachherSuchen(p, i, vor, schwelle);
            if (treffer is not { } t)
            {
                i++;
                continue;
            }

            // Der letzte ruhige Wert VOR dem Übergang — nicht der erste, bei dem
            // die Stufe schon in Reichweite lag. Sonst begänne Brus Sprung um
            // 15:50 statt um 16:10.
            var letzte = i;
            while (letzte + 1 < t.Index
                   && Vorher(p, letzte + 1) is { } weiter
                   && Ruhig(p, VorherAnfang(p, letzte + 1), letzte + 1, weiter, schwelle)
                   && Math.Abs(weiter - vor) < schwelle / 2)
            {
                letzte++;
            }

            var vorLetzte = Vorher(p, letzte) ?? vor;
            funde.Add(new Sprung(metricKey, p[letzte].Utc, p[t.Index].Utc, Runden(vorLetzte), Runden(t.Median)));
            i = t.Index;
        }

        return funde;
    }

    /// <summary>Median des ruhigen Abschnitts, der bei <paramref name="ende"/> endet — oder null, wenn er unvollständig ist.</summary>
    private static double? Vorher(Rohwert[] p, int ende)
    {
        var anfang = VorherAnfang(p, ende);
        if (ende - anfang + 1 < MindestWerte) return null;
        // Der Abschnitt muss das Fenster abdecken, nicht nur irgendwo darin liegen.
        if (p[anfang].Utc > p[ende].Utc - Ruhefenster + GroessteLuecke) return null;
        if (!OhneLuecke(p, anfang, ende)) return null;
        return Median(p, anfang, ende);
    }

    private static int VorherAnfang(Rohwert[] p, int ende)
    {
        var grenze = p[ende].Utc - Ruhefenster;
        var anfang = ende;
        while (anfang > 0 && p[anfang - 1].Utc >= grenze) anfang--;
        return anfang;
    }

    private readonly record struct Nachher(int Index, double Median);

    /// <summary>Der erste ruhige Abschnitt nach <paramref name="i"/> — mit Stufe, oder null.</summary>
    private static Nachher? NachherSuchen(Rohwert[] p, int i, double vor, double schwelle)
    {
        for (var j = i + 1; j < p.Length; j++)
        {
            if (p[j].Utc - p[i].Utc > HoechstensUebergang) return null;
            if (p[j].Utc - p[j - 1].Utc > GroessteLuecke) return null;

            // Ein Abschnitt, der noch nicht zu Ende gemessen ist, zählt nicht:
            // sonst stünde ein Fund im Speicher, den die nächsten Werte widerlegen.
            var grenze = p[j].Utc + Ruhefenster;
            var ende = j;
            while (ende + 1 < p.Length && p[ende + 1].Utc <= grenze) ende++;
            if (ende - j + 1 < MindestWerte) continue;
            if (p[ende].Utc < grenze - GroessteLuecke) continue;
            if (!OhneLuecke(p, j, ende)) continue;

            var median = Median(p, j, ende);
            if (!Ruhig(p, j, ende, median, schwelle)) continue;

            // Der erste ruhige Abschnitt entscheidet. Liegt er auf der alten
            // Stufe, war es ein Ausreißer — kein Sprung.
            return Math.Abs(median - vor) >= schwelle ? new Nachher(j, median) : null;
        }

        return null;
    }

    /// <summary>Ruhig: kein Wert weiter als ein Drittel der Schwelle vom Median.</summary>
    private static bool Ruhig(Rohwert[] p, int anfang, int ende, double median, double schwelle)
    {
        for (var k = anfang; k <= ende; k++)
        {
            if (Math.Abs(p[k].Wert - median) > schwelle / 3) return false;
        }

        return true;
    }

    private static bool OhneLuecke(Rohwert[] p, int anfang, int ende)
    {
        for (var k = anfang + 1; k <= ende; k++)
        {
            if (p[k].Utc - p[k - 1].Utc > GroessteLuecke) return false;
        }

        return true;
    }

    private static double Median(Rohwert[] p, int anfang, int ende)
    {
        var werte = new double[ende - anfang + 1];
        for (var k = anfang; k <= ende; k++) werte[k - anfang] = p[k].Wert;
        Array.Sort(werte);
        var mitte = werte.Length / 2;
        return werte.Length % 2 == 1 ? werte[mitte] : (werte[mitte - 1] + werte[mitte]) / 2;
    }

    private static double Runden(double wert) => Math.Round(wert, 3);
}
