using System.Globalization;

namespace GrowDiary.Web.Services;

/// <summary>
/// Ein Zielbereich als Text — an einer Stelle für das ganze Backend.
/// </summary>
/// <remarks>
/// Ein Ziel ohne Breite („1,4-1,4“, „5,8–5,8“) las sich wie ein Fehler; es ist ein
/// einzelner Wert. Verglichen wird der <b>geschriebene</b> Text, nicht die Zahl:
/// 1,35 und 1,44 stehen mit einer Nachkommastelle beide als „1,4“ da, und eine Spanne
/// aus zwei gleichen Zahlen sagt dem Leser nichts. Das Frontend macht es seit
/// forkai.250 genauso (<c>formatDeviationTarget</c>), der Lagebericht ebenfalls.
/// </remarks>
public static class Zielspanne
{
    /// <summary>
    /// „min-max“, bei gleichem geschriebenem Wert nur dieser eine; einseitige Grenzen
    /// als „ab …“ bzw. „bis …“; ohne beide Grenzen <paramref name="ohneGrenzen"/>.
    /// </summary>
    /// <param name="format">Zahlenformat, z. B. <c>0.0</c>.</param>
    /// <param name="trenner">Zeichen zwischen den Grenzen (bisher „-“ oder „–“).</param>
    /// <param name="kultur">Standard: die aktuelle Kultur, wie die übrigen Meldungen.</param>
    public static string Text(double? min, double? max, string format = "0.##", string trenner = "-",
        CultureInfo? kultur = null, string ohneGrenzen = "—")
    {
        kultur ??= CultureInfo.CurrentCulture;
        var unten = min?.ToString(format, kultur);
        var oben = max?.ToString(format, kultur);
        return (unten, oben) switch
        {
            ({ } u, { } o) when u == o => u,
            ({ } u, { } o) => $"{u}{trenner}{o}",
            ({ } u, null) => $"ab {u}",
            (null, { } o) => $"bis {o}",
            _ => ohneGrenzen,
        };
    }
}
