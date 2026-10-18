using GrowDiary.Web.Infrastructure;

namespace GrowDiary.Web.Services;

/// <summary>Die Einschätzung der Belüftung — ohne DO-Messgerät.</summary>
/// <param name="Stufe">zu_wenig, knapp, gut oder sehr_hoch.</param>
/// <param name="Satz">Ein Satz für den Betreiber, mit dem Rechenweg.</param>
/// <param name="LiterLuftJeMinuteJeLiter">Die Kennzahl hinter der Stufe.</param>
public sealed record BelueftungsUrteil(string Stufe, string Satz, double LiterLuftJeMinuteJeLiter);

/// <summary>
/// Rechnet, statt zu messen — und sagt das auch dazu.
/// </summary>
/// <remarks>
/// <para>Viele Betreiber haben kein DO-Messgerät. Was jeder kennt: die Zahl auf
/// dem Pumpenkarton und die Grösse seines Systems. Daraus entsteht keine
/// Messung, sondern eine Einordnung — „im grünen Bereich" —, und genau so wird
/// sie auch beschriftet. Ein erfundener mg/L-Wert stünde hier nie: er sähe aus
/// wie ein Sensorwert und würde Alarme füttern.</para>
///
/// <para>Die Stufen sind eine Faustregel aus der DWC-Praxis (verbreitete
/// Community-Empfehlung: rund 1 L Luft je Minute auf 10 L Nährlösung ist
/// reichlich; die Hälfte gilt als knapp). Bewusst grob — der Betreiber hat
/// „ungefähr reicht" ausdrücklich so bestellt. Wer es genau wissen will, misst
/// DO: Ziel laut den eigenen Abläufen 6–8 mg/L.</para>
///
/// <para>Die Sättigungstabelle dagegen ist Physik, keine Faustregel: Sauerstoff-
/// Löslichkeit in Süsswasser auf Meereshöhe nach den USGS-Tabellen
/// (Standardwerte, z. B. 9,09 mg/L bei 20 °C).</para>
/// </remarks>
public static class AerationCheck
{
    /// <summary>Das Optimum in L Luft je Minute und Liter Wasser.</summary>
    /// <remarks>
    /// Von SKX, dem Autor der RDWC-Abläufe in dieser Wissensbasis: darüber wird
    /// es schädlich. Die untere Kante (0,10) stammt aus der allgemeinen
    /// DWC-Literatur — beide Zahlen stehen nebeneinander, keine ersetzt die
    /// andere.
    /// </remarks>
    public const double OptimumJeLiter = 0.5;

    /// <summary>Wie weit über dem Optimum es noch unbedenklich ist.</summary>
    /// <remarks>
    /// Eine Faustregel ist ein Ziel, keine Klippe. Wer bei 0,56 statt 0,50
    /// liegt, bekommt sonst eine Belehrung für 11 % — und lernt, die Hinweise
    /// zu überlesen. Erst ab der Hälfte darüber lohnt der Satz.
    /// </remarks>
    private const double ToleranzFaktor = 1.5;

    /// <summary>Sauerstoff-Sättigung (mg/L) je Wassertemperatur, USGS-Tabelle.</summary>
    private static readonly (double TempC, double MgL)[] Saettigung =
    [
        (10, 11.29), (12, 10.78), (14, 10.31), (16, 9.87), (18, 9.47),
        (20, 9.09), (22, 8.74), (24, 8.42), (26, 8.11), (28, 7.83),
        (30, 7.56), (32, 7.31),
    ];

    /// <summary>
    /// Wie viel Sauerstoff das Wasser bei dieser Temperatur überhaupt halten kann.
    /// </summary>
    /// <remarks>
    /// Der oft übersehene Hebel: warmes Wasser KANN nicht viel Sauerstoff halten,
    /// egal wie gross die Pumpe ist. Wer bei 28 °C mehr Luft kauft, kauft am
    /// Problem vorbei.
    /// </remarks>
    public static double SaettigungMgL(double wasserTempC)
    {
        if (wasserTempC <= Saettigung[0].TempC) return Saettigung[0].MgL;
        if (wasserTempC >= Saettigung[^1].TempC) return Saettigung[^1].MgL;

        for (var i = 1; i < Saettigung.Length; i++)
        {
            if (wasserTempC > Saettigung[i].TempC) continue;

            var (t0, m0) = Saettigung[i - 1];
            var (t1, m1) = Saettigung[i];
            return Math.Round(m0 + (m1 - m0) * (wasserTempC - t0) / (t1 - t0), 2);
        }

        return Saettigung[^1].MgL;
    }

    /// <summary>Pumpe gegen Volumen: reicht die Luft ungefähr?</summary>
    public static BelueftungsUrteil? Beurteilen(double? pumpeLiterProStunde, double? volumenLiter)
    {
        if (pumpeLiterProStunde is not { } lph || lph <= 0) return null;
        if (volumenLiter is not { } liter || liter <= 0) return null;

        var jeLiter = Math.Round(lph / 60.0 / liter, 3);
        var basis = $"{lph:0}\u00a0L/h auf {liter:0}\u00a0L Wasser";

        // Zwei Faustregeln, die nicht dasselbe sagen — und beide bleiben stehen:
        //
        //   0,10 L/min je Liter  = die untere Kante, ab der es laeuft (etwa
        //                          „1 W Belueftung je Gallone" aus der
        //                          DWC-Literatur).
        //   0,50 L/min je Liter  = das Optimum nach SKX; darueber wird es
        //                          schaedlich, weil sich zwischen Loesung und
        //                          Deckel ein Luftpolster bildet, in dem
        //                          freiliegende Wurzeln austrocknen.
        //
        // Die App verwirft keine der beiden. Sie nennt den gruenen Bereich enger
        // als frueher und sagt beim Ueberschreiten, WESSEN Grenze das ist —
        // damit niemand seine funktionierende Anlage wegen einer Zahl umbaut,
        // die er nicht einordnen kann.
        return jeLiter switch
        {
            >= 1.0 => new BelueftungsUrteil(
                "sehr_hoch",
                $"Sehr viel Luft ({basis}). Eher drosseln oder auf mehrere Ausströmer verteilen — zu starke Verwirbelung schadet jungen Wurzeln.",
                jeLiter),
            > OptimumJeLiter * ToleranzFaktor => new BelueftungsUrteil(
                "mehr_als_noetig",
                $"Mehr als nötig ({basis}). Als Optimum gelten {OptimumJeLiter.ToString("0.##", AppCulture.German)} L/min je Liter — "
                    + "darüber sammelt sich Luft zwischen Lösung und Deckel, und freiliegende Wurzeln können austrocknen. "
                    + "Läuft es bei dir gut, ist das kein Grund umzubauen; beim nächsten Ausströmer aber die kleinere Nummer.",
                jeLiter),
            >= 0.10 => new BelueftungsUrteil(
                "gut",
                $"Im grünen Bereich ({basis}).",
                jeLiter),
            >= 0.05 => new BelueftungsUrteil(
                "knapp",
                $"Eher knapp ({basis}). Läuft, aber ohne Reserve — bei warmem Wasser zuerst kühlen, dann mehr Luft.",
                jeLiter),
            _ => new BelueftungsUrteil(
                "zu_wenig",
                $"Zu wenig Luft ({basis}). Eine stärkere Pumpe oder weniger Volumen je Pumpe einplanen.",
                jeLiter),
        };
    }
}
