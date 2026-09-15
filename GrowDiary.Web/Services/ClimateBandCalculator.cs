namespace GrowDiary.Web.Services;

/// <summary>
/// Das VPD-Ziel, rückwärts gelesen: welche Lufttemperatur bei der gemessenen
/// Feuchte im Zielband landet — und welche Feuchte bei der gemessenen Temperatur.
/// </summary>
/// <remarks>
/// Luft und Luftfeuchte sind die zwei größten Kacheln auf dem Bildschirm und
/// hatten trotzdem nie eine Bewertung: das Wissen kennt nur ein VPD-Band, und
/// VPD steht in einer dritten Kachel daneben. Auf die Frage „sind 25,6 °C bei
/// 46 % gut?" antwortete der Bildschirm mit Schweigen.
///
/// Hier wird nichts erfunden. Es ist dasselbe Band aus derselben Quelle, nur
/// nach der anderen Variablen aufgelöst — mit <see cref="VpdCalculator"/> als
/// einziger Formel, damit Hin- und Rückweg nicht auseinanderlaufen können.
/// </remarks>
public static class ClimateBandCalculator
{
    // Ein Growraum ausserhalb dieser Spanne ist kein Growraum mehr. Die Grenzen
    // begrenzen die Suche, nicht das Ergebnis.
    private const double MinTempC = 5;
    private const double MaxTempC = 45;

    /// <summary>
    /// Was als Temperatur EMPFOHLEN werden darf — enger als die Suche.
    /// </summary>
    /// <remarks>
    /// Die Rückrechnung ist reine Physik, und Physik kennt keine Pflanze: beim
    /// Keimlings-Ziel von 0,4 kPa und 54 % Raumfeuchte landet sie bei 5 °C —
    /// korrekt gerechnet und trotzdem Unsinn, denn bei 5 °C wächst nichts. Der
    /// richtige Hebel ist dort die Feuchte (Haube), nie die Kälte. Was
    /// ausserhalb dieser Spanne liegt, wird deshalb nicht empfohlen; ein in die
    /// Spanne ragendes Band wird beschnitten — jede Temperatur darin trifft das
    /// VPD-Ziel weiterhin, das Beschneiden erfindet nichts.
    /// </remarks>
    public const double RecommendMinC = 18;
    public const double RecommendMaxC = 32;

    /// <summary>
    /// Das Feuchteband (in %), das bei dieser Lufttemperatur im VPD-Ziel landet.
    /// Geschlossen lösbar: VPD hängt linear von der Feuchte ab.
    /// </summary>
    /// <param name="maxHumidityPercent">
    /// Der Schimmeldeckel der Phase (<see cref="MoldGuard"/>). Ohne ihn kannte
    /// diese Rechnung nur Physik: bei 32 °C in der Blüte kam „64–68 % RLF"
    /// heraus — korrekt gerechnet und trotzdem gefährlicher Rat, weil ab ~60 %
    /// in dichten Blüten Grauschimmel wahrscheinlich wird. Drückt der Deckel
    /// das Band auf nichts zusammen, ist die Antwort ehrlich: bei dieser
    /// Temperatur gibt es keine sichere Feuchte für dieses VPD-Ziel — die
    /// Temperatur muss runter, nicht die Feuchte rauf.
    /// </param>
    public static (double? Min, double? Max) HumidityBand(
        double airTemperatureC, double vpdMin, double vpdMax, double leafOffsetC,
        double? maxHumidityPercent = null)
    {
        if (vpdMin > vpdMax) (vpdMin, vpdMax) = (vpdMax, vpdMin);

        var luft = VpdCalculator.SaturationKpa(airTemperatureC);
        if (luft <= 0) return (null, null);

        var blatt = VpdCalculator.SaturationKpa(airTemperatureC + leafOffsetC);

        // VPD = blatt − luft · rh/100  ⇒  rh = 100 · (blatt − VPD) / luft.
        // Viel VPD heisst wenig Feuchte, deshalb kreuzen sich die Grenzen.
        var min = 100.0 * (blatt - vpdMax) / luft;
        var max = 100.0 * (blatt - vpdMin) / luft;

        min = Math.Clamp(min, 0, 100);
        max = Math.Clamp(max, 0, Math.Min(100, maxHumidityPercent ?? 100));
        return min >= max ? (null, null) : (Math.Round(min, 1), Math.Round(max, 1));
    }

    /// <summary>
    /// Das Temperaturband (in °C), das bei dieser Feuchte im VPD-Ziel landet.
    /// Nicht geschlossen lösbar — VPD steckt zweimal in der Exponentialfunktion —,
    /// deshalb eingeschachtelt. VPD steigt mit der Temperatur, das genügt.
    /// </summary>
    public static (double? Min, double? Max) TemperatureBand(
        double humidityPercent, double vpdMin, double vpdMax, double leafOffsetC)
    {
        if (humidityPercent is < 0 or > 100) return (null, null);
        if (vpdMin > vpdMax) (vpdMin, vpdMax) = (vpdMax, vpdMin);

        var min = SolveTemperature(humidityPercent, vpdMin, leafOffsetC);
        var max = SolveTemperature(humidityPercent, vpdMax, leafOffsetC);
        if (min is null || max is null || min >= max) return (null, null);

        // Nur empfehlen, was ein Growraum sein kann. Liegt das ganze Band
        // ausserhalb, gibt es kein Temperatur-Ziel — der Aufrufer sagt dann,
        // dass die Feuchte der Hebel ist.
        var geschnittenMin = Math.Max(min.Value, RecommendMinC);
        var geschnittenMax = Math.Min(max.Value, RecommendMaxC);
        if (geschnittenMin >= geschnittenMax) return (null, null);

        return (Math.Round(geschnittenMin, 1), Math.Round(geschnittenMax, 1));
    }

    /// <summary>Die Temperatur, bei der sich genau dieses VPD einstellt; null, wenn keine im Raum liegt.</summary>
    private static double? SolveTemperature(double humidityPercent, double vpd, double leafOffsetC)
    {
        double Bei(double tempC) => VpdCalculator.Calculate(tempC, humidityPercent, leafOffsetC) ?? double.NaN;

        var unten = Bei(MinTempC);
        var oben = Bei(MaxTempC);
        if (double.IsNaN(unten) || double.IsNaN(oben)) return null;

        // Ausserhalb des Suchbereichs gibt es keine Antwort — lieber keine als
        // eine an die Grenze geklemmte, die eine Genauigkeit vortäuscht.
        if (vpd < unten || vpd > oben) return null;

        var lo = MinTempC;
        var hi = MaxTempC;
        for (var i = 0; i < 60; i++)
        {
            var mitte = (lo + hi) / 2;
            if (Bei(mitte) < vpd) lo = mitte; else hi = mitte;
        }

        return (lo + hi) / 2;
    }
}
