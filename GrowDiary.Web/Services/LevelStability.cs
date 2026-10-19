namespace GrowDiary.Web.Services;

/// <summary>Eine Sensor-Ablesung im Kalibrierlauf.</summary>
public readonly record struct LevelSample(DateTime AtUtc, double Value);

/// <summary>
/// Steht der Füllstand still?
/// </summary>
/// <remarks>
/// <para>Der Kern des Kalibrier-Assistenten. Cultivate Control liest den Pegelsensor
/// laufend mit, während der Nutzer Wasser einfüllt und die Menge an seiner
/// Wasseruhr abliest. Wann der Nullpunkt steht und wann „voll" erreicht ist,
/// erkennt die App am Stillstand des Werts — der Nutzer soll nicht auf die Uhr
/// schauen müssen.</para>
///
/// <para><b>Warum ein Band und keine Gleichheit:</b> die Umwälzpumpe kräuselt
/// die Oberfläche, der eTape zittert um ein paar Millimeter. „Sechzig Sekunden
/// exakt derselbe Wert" tritt nie ein; „sechzig Sekunden innerhalb ±0,3 cm"
/// beschreibt genau das, was ein Mensch als „steht" sehen würde.</para>
///
/// <para><b>Warum beim Vollstand länger gewartet wird:</b> in einem RDWC
/// gleichen sich die Töpfe nach dem Füllstopp erst über die Verrohrung aus. Wer
/// zu früh abliest, kalibriert auf den Pegel im Einfülleimer statt auf den des
/// Systems.</para>
/// </remarks>
public static class LevelStability
{
    /// <summary>Wie weit der Wert schwanken darf und trotzdem als ruhig gilt.</summary>
    public const double ToleranceCm = 0.3;

    /// <summary>Der Nullpunkt braucht weniger Geduld: da bewegt sich nichts mehr.</summary>
    public const int EmptySeconds = 15;

    /// <summary>Nach dem Füllstopp muss sich das System erst ausgleichen.</summary>
    public const int FullSeconds = 60;

    /// <summary>
    /// Der stabile Wert, oder null solange es noch schwankt.
    /// </summary>
    /// <remarks>
    /// Gerechnet wird über die Ablesungen der letzten <paramref name="seconds"/>
    /// Sekunden. Zurückgegeben wird der Median — ein einzelner Ausreisser (eine
    /// Welle, ein Funkaussetzer) verschiebt ihn nicht, ein Mittelwert schon.
    /// </remarks>
    public static double? StableValue(
        IReadOnlyList<LevelSample> samples, DateTime nowUtc, int seconds, double tolerance = ToleranceCm)
    {
        if (samples.Count == 0) return null;

        var fenster = samples
            .Where(sample => (nowUtc - sample.AtUtc).TotalSeconds <= seconds)
            .Select(sample => sample.Value)
            .ToList();

        // Zu wenige Ablesungen heisst: noch nicht lange genug beobachtet. Drei
        // ist das Minimum, aus dem sich „ruhig" überhaupt ablesen lässt.
        if (fenster.Count < 3) return null;

        // Und das Fenster muss wirklich so lange offen sein — sonst gölte eine
        // gerade erst begonnene Messung sofort als stabil.
        var aeltester = samples.Min(sample => sample.AtUtc);
        if ((nowUtc - aeltester).TotalSeconds < seconds) return null;

        if (fenster.Max() - fenster.Min() > tolerance) return null;

        var sortiert = fenster.OrderBy(value => value).ToList();
        var mitte = sortiert.Count / 2;
        var median = sortiert.Count % 2 == 1
            ? sortiert[mitte]
            : (sortiert[mitte - 1] + sortiert[mitte]) / 2;

        return Math.Round(median, 2);
    }

    /// <summary>Wie lange der Wert schon im Band liegt — für den Fortschritt in der Anzeige.</summary>
    public static int SecondsSteady(IReadOnlyList<LevelSample> samples, DateTime nowUtc, double tolerance = ToleranceCm)
    {
        if (samples.Count == 0) return 0;

        var absteigend = samples.OrderByDescending(sample => sample.AtUtc).ToList();
        var referenz = absteigend[0].Value;

        foreach (var sample in absteigend)
        {
            if (Math.Abs(sample.Value - referenz) > tolerance)
            {
                return (int)(nowUtc - sample.AtUtc).TotalSeconds;
            }
        }

        return (int)(nowUtc - absteigend[^1].AtUtc).TotalSeconds;
    }

    /// <summary>
    /// Der nächste Schritt des Assistenten — rein, damit der ganze Ablauf
    /// prüfbar ist und nicht nur seine Teile.
    /// </summary>
    /// <returns>
    /// Der Schritt und, falls schon bekannt, der stabile Wert (Nullpunkt bzw.
    /// Vollstand).
    /// </returns>
    public static (int Step, double? Value) NextStep(
        double? emptyRaw, IReadOnlyList<LevelSample> samples, DateTime nowUtc)
    {
        // 0 = warte auf leer, 1 = füllen, 2 = bestätigen.
        if (emptyRaw is not { } leer)
        {
            var null_ = StableValue(samples, nowUtc, EmptySeconds);
            return null_ is null ? (0, null) : (1, null_);
        }

        var voll = StableValue(samples, nowUtc, FullSeconds);
        if (voll is not { } vollWert) return (1, null);

        // Steht der Wert noch auf Höhe des Nullpunkts, ist schlicht nichts
        // hineingegangen — das ist kein „voll", auch wenn es ruhig ist.
        return vollWert > leer ? (2, vollWert) : (1, null);
    }
}
