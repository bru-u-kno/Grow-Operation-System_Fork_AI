namespace GrowDiary.Web.Services;

/// <summary>
/// Rechnet den Füllstand eines Sensors in Liter um — aus zwei gemessenen Punkten.
/// </summary>
/// <remarks>
/// <para>Ein eTape (oder jeder andere Pegelsensor) liefert Zentimeter. Für alles,
/// worauf es ankommt, braucht Cultivate Control aber Liter: die Dosis skaliert mit dem
/// Volumen, „noch 12 L" sagt mehr als „noch 22 cm", und Nachfüll-Grenzen setzt
/// man in Litern.</para>
///
/// <para><b>Zwei Punkte, nicht einer.</b> Ein eTape beginnt erst ein Stück über
/// der Unterkante zu messen und zeigt bei leerem Becken keine Null. Mit nur dem
/// Voll-Punkt liefe die Gerade durch den Ursprung — und wäre ausgerechnet unten
/// am stärksten daneben, also genau dort, wo der Füllstand zählt.</para>
///
/// <para>Zwischen den Punkten wird linear interpoliert. Das ist bei senkrechten
/// Wänden exakt und bei RDWC-Eimern nah genug; die Alternative wäre, dem Nutzer
/// eine Behälterform abzufragen, die er nicht kennt.</para>
/// </remarks>
public static class ReservoirVolume
{
    /// <summary>
    /// Liter bei diesem Sensorwert. Null, wenn die Kalibrierung unbrauchbar ist.
    /// </summary>
    /// <param name="rawValue">Was der Sensor gerade zeigt (z. B. cm).</param>
    /// <param name="emptyRaw">Sensorwert bei leerem System.</param>
    /// <param name="fullRaw">Sensorwert bei vollem System.</param>
    /// <param name="fullLiters">Wie viel beim Füllen wirklich hineinging.</param>
    /// <remarks>
    /// Über den Voll-Punkt hinaus wird weiter gerechnet — ein überfülltes Becken
    /// gibt es, und „mehr als voll" ist eine ehrlichere Antwort als „genau voll".
    /// Nach unten wird bei null gekappt: negative Liter gibt es nicht, und ein
    /// Sensor unterhalb seines Nullpunkts heisst schlicht leer.
    /// </remarks>
    public static double? Liters(double rawValue, double? emptyRaw, double? fullRaw, double? fullLiters)
    {
        if (emptyRaw is not { } leer || fullRaw is not { } voll || fullLiters is not { } literVoll)
        {
            return null;
        }

        // Gleiche Punkte ergeben keine Gerade, und ein Volumen von null wäre
        // keine Kalibrierung, sondern ein Tippfehler.
        if (Math.Abs(voll - leer) < 0.0001 || literVoll <= 0)
        {
            return null;
        }

        var anteil = (rawValue - leer) / (voll - leer);
        return Math.Max(0, Math.Round(anteil * literVoll, 1));
    }

    /// <summary>Wie voll das System ist, 0–1 — für die Anzeige und den Dosier-Faktor.</summary>
    public static double? Fraction(double rawValue, double? emptyRaw, double? fullRaw, double? fullLiters)
        => Liters(rawValue, emptyRaw, fullRaw, fullLiters) is { } liter && fullLiters is { } voll && voll > 0
            ? Math.Round(liter / voll, 3)
            : null;

    /// <summary>Sind alle drei Werte da und plausibel?</summary>
    public static bool IsCalibrated(double? emptyRaw, double? fullRaw, double? fullLiters)
        => Liters(0, emptyRaw, fullRaw, fullLiters) is not null;
}
