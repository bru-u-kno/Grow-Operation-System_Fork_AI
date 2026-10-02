namespace GrowDiary.Web.Services;

/// <summary>
/// Die Modi eines AC-Infinity-Geräts — die Werte, die Home Assistant im
/// <c>select.*_aktiver_modus</c> anbietet, und ihre deutschen Namen. An EINER
/// Stelle für die ganze App.
/// </summary>
/// <remarks>
/// <para><b>Die Liste ist nicht aus dem Kopf.</b> Sie ist die
/// <c>options</c>-Liste von <c>select.rdwc_fan3_aktiver_modus</c> in der Anlage
/// des Nutzers (02.10.2026).</para>
///
/// <para><b>Warum hier und nicht in der Oberfläche (02.10.2026).</b> Die Modi
/// standen an drei Stellen: <c>LichtSteuerungService.Modi</c> (drei Werte),
/// <c>LICHT_MODI</c> im Frontend (dieselben drei) und <c>AC_MODUS_NAMEN</c> im
/// Frontend (alle fünfzehn mit Übersetzung). Und das Backend baute für die
/// Steuerungs-Übersicht selbst <c>$"Modus {licht.Modus} · Stufe …"</c> — dort
/// stand „Modus Auto" roh auf dem Schirm, weil die Übersetzung nur die
/// Oberfläche kannte.</para>
///
/// <para>Das Backend MUSS die Namen kennen, weil es die Kurzzeilen der
/// Übersicht auf Deutsch baut. Also steht die Tabelle hier, und die Oberfläche
/// bekommt den Namen mitgeschickt (<see cref="LichtLive.ModusName"/>,
/// <see cref="AcGeraetStand.ModusName"/>) statt ihn selbst nachzuschlagen. Eine
/// Tabelle, keine Abschrift.</para>
///
/// <para><b>Die Werte gehen an Home Assistant</b> (<c>select.select_option</c>)
/// und werden deshalb NIE übersetzt: Kennung ist Kennung, Name ist Name. Die
/// Oberfläche vergleicht drei davon (<c>LICHT_MODI</c> in
/// <c>steuerung-typen.ts</c>); dass die dort mit diesen hier übereinstimmen,
/// hält <c>steuerung/licht-modi-vertrag.node.test.ts</c>, das diese Datei
/// liest.</para>
/// </remarks>
public static class AcModi
{
    public const string Aus = "Off";
    public const string An = "On";
    public const string Automatisch = "Auto";
    public const string CountdownBisAn = "Timer to On";
    public const string CountdownBisAus = "Timer to Off";
    public const string Zyklus = "Cycle";
    public const string Zeitplan = "Schedule";
    public const string Vpd = "VPD";
    public const string Co2 = "CO2";
    public const string Co2Luefter = "CO2 Fan";
    public const string Bodenfeuchte = "Moisture";
    public const string Wassertemperatur = "Water Temp";
    public const string Ph = "pH";
    public const string Ec = "EC";
    public const string Wassermelder = "Water Detect";

    /// <summary>Jede Kennung mit ihrem deutschen Namen — die Grundmenge.</summary>
    /// <remarks>
    /// Gleich bleiben dürfen nur die Abkürzungen, die auch im Deutschen so
    /// heißen (VPD, pH, EC). Gehalten von <c>AcModiTests</c>.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, string> Namen =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Aus] = "aus",
            [An] = "an",
            [Automatisch] = "automatisch",
            [CountdownBisAn] = "Countdown bis an",
            [CountdownBisAus] = "Countdown bis aus",
            [Zyklus] = "Zyklus",
            [Zeitplan] = "Zeitplan",
            [Vpd] = "VPD",
            [Co2] = "CO₂",
            [Co2Luefter] = "CO₂-Lüfter",
            [Bodenfeuchte] = "Bodenfeuchte",
            [Wassertemperatur] = "Wassertemperatur",
            [Ph] = "pH",
            [Ec] = "EC",
            [Wassermelder] = "Wassermelder",
        };

    /// <summary>
    /// Der deutsche Name eines Modus — „On" wird „an", „Timer to Off" wird
    /// „Countdown bis aus".
    /// </summary>
    /// <remarks>
    /// <para>Ohne Modus steht „–". Meldet Home Assistant statt eines Modus, dass
    /// das Gerät fehlt (<c>unavailable</c>/<c>unknown</c>), steht das auf
    /// Deutsch da — das sind keine Modi, aber genau das, was im
    /// <c>select</c> steht, wenn der Controller offline ist. Dieselben Wörter
    /// wie <c>HA_ZUSTAND_NAMEN</c> in <c>GrowDiary.React/src/deutsche-woerter.ts</c>
    /// — die Kurzzeile der Übersicht entsteht hier, also braucht das Backend
    /// sie auch.</para>
    /// <para>Ein Modus, den eine künftige Fassung der Integration neu anbietet,
    /// bleibt stehen, wie er kommt: eine erfundene Übersetzung wäre schlechter
    /// als das Original.</para>
    /// </remarks>
    public static string Name(string? kennung)
    {
        if (string.IsNullOrWhiteSpace(kennung)) return "–";
        var wert = kennung.Trim();
        if (Namen.TryGetValue(wert, out var name)) return name;
        return wert.ToLowerInvariant() switch
        {
            "unavailable" => "nicht erreichbar",
            "unknown" => "unbekannt",
            _ => wert,
        };
    }
}
