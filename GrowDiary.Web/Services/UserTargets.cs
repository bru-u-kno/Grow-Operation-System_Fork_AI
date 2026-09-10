using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Die eine Stelle, an der entschieden wird, welcher Zielbereich gilt.
/// </summary>
/// <remarks>
/// Vorher fragte jeder Teil der App selbst: die Live-Kacheln und die Diagnose
/// nahmen die mitgelieferten Phasenwerte, die Alarme und die Dosierung nahmen
/// die des Nutzers. Bei einem eingetragenen pH von 5,60–5,90 und einem Wert von
/// 5,99 sagte die Kachel „zu niedrig" (Ziel 6,00–6,10) und der Alarm „zu hoch" —
/// derselbe Wert, entgegengesetzte Antworten, und nichts auf dem Bildschirm
/// verriet, welche gilt. Wer der Kachel folgte, hätte pH angehoben; wer dem
/// eigenen Grenzwert folgte, gesenkt.
///
/// **Der eingetragene Wert des Nutzers gewinnt.** Wer nichts einträgt, folgt
/// weiter dem mitgelieferten Wissen. Abgeschaltete Grenzwerte zählen nicht: aus
/// heißt aus, nicht „gilt heimlich weiter".
/// </remarks>
public static class UserTargets
{
    /// <summary>
    /// Was der Nutzer für diese Messgröße eingetragen hat — null, wenn nichts.
    /// </summary>
    /// <remarks>
    /// Eine halbe Grenze ist erlaubt: wer nur „nicht über 6,2" will, bekommt
    /// genau das, und nach unten bleibt es offen.
    /// </remarks>
    public static (double? Min, double? Max)? For(string metricKey, IEnumerable<TentAlertRule>? rules)
    {
        if (rules is null) return null;

        /* Plan-Regeln zaehlen hier NICHT (10.09.2026).
         *
         * Sie tragen keine eigene Zahl, sondern leiten ihre Grenzen aus genau
         * dem Band ab, das diese Klasse gerade zusammensetzt. Liesse man sie
         * mit, legte sich das Band spaeter ueber sich selbst — und jede
         * Toleranz waere nach einem Durchlauf Teil des Ziels. Ausdruecklich
         * geprueft und nicht dem Zufall ueberlassen, dass Min/Max leer sind. */
        var rule = rules.FirstOrDefault(r =>
            r.Enabled
            && r.Quelle != Grenzwertquelle.Plan
            && string.Equals(r.MetricKey, metricKey, StringComparison.OrdinalIgnoreCase)
            && (r.MinValue is not null || r.MaxValue is not null));

        return rule is null ? null : (rule.MinValue, rule.MaxValue);
    }

    /// <summary>true, sobald der Nutzer für diese Messgröße etwas eingetragen hat.</summary>
    public static bool IsUserSet(string metricKey, IEnumerable<TentAlertRule>? rules)
        => For(metricKey, rules) is not null;

    /// <summary>
    /// Legt die Werte des Nutzers über die Phasenwerte des Wissens.
    /// </summary>
    /// <remarks>
    /// Für alles, was mit <see cref="HydroTargetValues"/> arbeitet — vor allem
    /// die Abweichungsanalyse. Nur überschrieben wird, was der Nutzer wirklich
    /// gesetzt hat; alles andere bleibt beim Wissensstand, damit die
    /// Phasenstaffelung erhalten bleibt.
    /// </remarks>
    public static HydroTargetValues Overlay(HydroTargetValues knowledge, IEnumerable<TentAlertRule>? rules)
    {
        if (rules is null) return knowledge;

        var liste = rules as IReadOnlyList<TentAlertRule> ?? rules.ToList();
        var result = knowledge;

        if (For("reservoir-ph", liste) is { } ph)
            result = result with { PhMin = ph.Min ?? result.PhMin, PhMax = ph.Max ?? result.PhMax };

        if (For("reservoir-ec", liste) is { } ec)
            result = result with { EcMin = ec.Min ?? result.EcMin, EcMax = ec.Max ?? result.EcMax };

        if (For("orp", liste) is { } orp)
            result = result with { OrpMin = orp.Min ?? result.OrpMin, OrpMax = orp.Max ?? result.OrpMax };

        if (For("vpd", liste) is { } vpd)
            result = result with { VpdMin = vpd.Min ?? result.VpdMin, VpdMax = vpd.Max ?? result.VpdMax };

        if (For("ppfd", liste) is { } ppfd)
            result = result with { PpfdMin = ppfd.Min ?? result.PpfdMin, PpfdMax = ppfd.Max ?? result.PpfdMax };

        if (For("co2", liste) is { } co2)
            result = result with { Co2Min = co2.Min ?? result.Co2Min, Co2Max = co2.Max ?? result.Co2Max };

        // Wassertemperatur steht im Wissen als Tag/Nacht-Paar; der Nutzer trägt
        // eine Spanne ein. Nacht = untere, Tag = obere Grenze.
        if (For("reservoir-temp", liste) is { } wasser)
            result = result with
            {
                WaterTempNightC = wasser.Min ?? result.WaterTempNightC,
                WaterTempDayC = wasser.Max ?? result.WaterTempDayC,
            };

        return result;
    }

    /// <summary>Was auf der Kachel steht, wenn der Wert vom Nutzer kommt.</summary>
    public const string SourceLabel = "dein Wert";
}
