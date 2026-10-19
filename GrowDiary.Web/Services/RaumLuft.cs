using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI (cc.263, Raum Stufe 2): Die Raumluft-Werte des Zelts („{Raumname} Temp./RLF/VPD", Schlüssel <c>outside-*</c>)
/// kommen vom Raum, nicht aus eigenen Zeilen am Zelt.
/// </summary>
/// <remarks>
/// Nichts wird gelöscht: die eigenen „Außen"-Zeilen des Zelts bleiben gespeichert und gelten wieder, sobald das Zelt den Raum
/// verlässt oder der Raum einen Fühler verliert. Die VPD wird gerechnet (Blatt = Lufttemperatur), damit der Raum keinen dritten
/// Fühler braucht; beim Fühler am AC-Infinity-Controller deckt sich das mit dessen eigenem VPD-Wert (gemessen: 18,7 °C und 65,8 % ergeben 0,74 kPa).
/// </remarks>
public static class RaumLuft
{
    private static readonly SensorMetricType[] Ersetzt =
        [SensorMetricType.OutsideTemperature, SensorMetricType.OutsideHumidity, SensorMetricType.OutsideVpd];

    /// <summary>Die Zuordnung, mit der das Zelt gelesen wird (mit Raum-Fühlern statt der drei Außen-Zeilen, falls es einen Raum gibt).</summary>
    public static IReadOnlyList<TentSensor> Wirksam(Tent zelt, RaumFuehler? fuehler)
    {
        if (fuehler is null) return zelt.Sensors;
        var uebrig = zelt.Sensors.Where(s => !Ersetzt.Contains(s.MetricType)).ToList();
        uebrig.Add(Zeile(zelt, SensorMetricType.OutsideTemperature, fuehler.TempEntityId));
        uebrig.Add(Zeile(zelt, SensorMetricType.OutsideHumidity, fuehler.RhEntityId));
        return uebrig;
    }

    private static TentSensor Zeile(Tent zelt, SensorMetricType art, string entityId)
        => new() { TentId = zelt.Id, MetricType = art, HaEntityId = entityId, IsActive = true };

    /// <summary>Dampfdruckdefizit in kPa aus Lufttemperatur (°C) und relativer Feuchte (%): Blatt = Luft. <c>null</c> bei unsinnigen Werten.</summary>
    public static double? Vpd(double tempC, double rhProzent)
    {
        if (double.IsNaN(tempC) || double.IsNaN(rhProzent) || rhProzent is < 0 or > 100 || tempC is < -40 or > 80) return null;
        var saettigung = 0.6108 * Math.Exp(17.27 * tempC / (tempC + 237.3));
        return Math.Round(saettigung * (1 - rhProzent / 100.0), 2);
    }

    /// <summary>
    /// Ergänzt <c>outside-vpd</c> aus <c>outside-temperature</c> und <c>outside-humidity</c> und lässt eine eigene Zuordnung dafür
    /// nicht gelten (im Raum gibt es nur die gerechnete VPD). Fehlt einer der beiden Werte, entfällt die VPD.
    /// </summary>
    public static void VpdErgaenzen(IDictionary<string, HomeAssistantState> zustaende)
    {
        const string temp = "outside-temperature", rh = "outside-humidity", vpd = "outside-vpd";
        zustaende.Remove(vpd);
        if (!zustaende.TryGetValue(temp, out var t) || !zustaende.TryGetValue(rh, out var f)) return;
        var tw = Zahlenlesen.Maschine(t.State);
        var fw = Zahlenlesen.Maschine(f.State);
        if (tw is null || fw is null || Vpd(tw.Value, fw.Value) is not { } wert) return;
        zustaende[vpd] = new HomeAssistantState
        {
            EntityId = $"{t.EntityId}+{f.EntityId}",
            State = wert.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            NumericValue = wert,
            UnitOfMeasurement = "kPa",
            LastChanged = Spaeter(t.LastChanged, f.LastChanged),
            LastUpdated = Frueher(t.LastUpdated, f.LastUpdated),
        };
    }

    private static DateTime? Spaeter(DateTime? a, DateTime? b) => a is null ? b : b is null ? a : a > b ? a : b;
    private static DateTime? Frueher(DateTime? a, DateTime? b) => a is null || b is null ? null : a < b ? a : b;
}

/// <summary>
/// Die Beschriftung der drei Raumluft-Werte. Die Tabellen der Beschriftungen sind statisch (<c>AlertEvaluationService.MetricDisplay</c>);
/// den Raumnamen liefert die Quelle, die <c>Program</c> beim Start einsetzt (in Tests bleibt sie leer, dann heißt es „Außen").
/// </summary>
public static class AussenBeschriftung
{
    public static Func<string?>? Quelle { get; set; }

    /// <summary>„Keller Temp." statt „Außen Temp.", solange die Raumwerte vom Raum kommen.</summary>
    public static string Vorsatz() => Quelle?.Invoke() is { Length: > 0 } name ? name : "Außen";
}
