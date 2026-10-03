using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

public static class LightStateNormalizer
{
    public static LightState Normalize(string? rawState)
    {
        var normalized = rawState?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return LightState.Unknown;
        }

        var wort = normalized.ToLowerInvariant() switch
        {
            "on" or "true" or "1" or "open" => LightState.On,
            "off" or "false" or "0" or "closed" => LightState.Off,
            _ => LightState.Unknown
        };

        if (wort != LightState.Unknown) return wort;

        // Ein Helligkeitssensor meldet keine Schalterstellung, sondern eine Zahl:
        // Lichtstärke in Prozent, Lux oder die Leistungsaufnahme der Lampe. Aus
        // dem Feld: ein gemappter Sensor mit „100.0 %" galt als unlesbar, und die
        // Kachel behauptete daraufhin, es sei gar keiner eingerichtet.
        //
        // Die Schwelle liegt bewusst knapp über null statt in der Mitte: eine
        // gedimmte Lampe ist an, eine dunkle Kammer misst nichts.
        if (Zahlenlesen.Maschine(normalized) is { } zahl)
        {
            return zahl >= 1 ? LightState.On : LightState.Off;
        }

        return LightState.Unknown;
    }
}
