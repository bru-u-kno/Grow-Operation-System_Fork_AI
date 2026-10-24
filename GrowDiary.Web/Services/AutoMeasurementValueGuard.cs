using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

public enum AutoMeasurementValueSeverity
{
    None,
    Warning,
    Reject
}

public sealed record AutoMeasurementValueGuardResult(
    bool IsValid,
    AutoMeasurementValueSeverity Severity,
    string? Message);

public sealed class AutoMeasurementValueGuard
{
    public AutoMeasurementValueGuardResult Check(AutoMeasurementField field, double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return Reject(field, value, "Wert ist nicht numerisch plausibel.");
        }

        // ZWEI SCHICHTEN, nicht zwei Wahrheiten.
        //
        // Die aeussere Schranke ist die physikalische Tabelle in
        // MeasurementSanityService — sie gilt fuer JEDEN Weg in die Datenbank,
        // von Hand wie automatisch. Vorher fuehrte dieser Waechter eine eigene,
        // dritte Tabelle, und bei 7 von 11 Feldern wichen die Zahlen ab. Beim
        // ORP widersprachen sie sich sogar in beide Richtungen: 1100 mV kam
        // automatisch durch, war von Hand aber gesperrt.
        //
        // Die engeren Baender unten BLEIBEN und sind die zweite Schicht: was
        // unbeaufsichtigt geschrieben wird, darf strenger geprueft werden als
        // das, was jemand tippt und dabei ansieht. Sie gleichzusetzen waere eine
        // Lockerung — bei sechs der sieben Felder ist das Auto-Band das engere,
        // und ein defekter Sensor duerfte dann pH 13 und 55 Grad schreiben.
        if (PhysikSchluessel(field) is { } schluessel
            && !MeasurementSanityService.IstPhysikalischMoeglich(schluessel, value))
        {
            var g = MeasurementSanityService.PhysikalischeGrenzen[schluessel];
            return Reject(field, value, $"Außerhalb dessen, was physikalisch vorkommen kann ({g.Min:0}–{g.Max:0}).");
        }

        return field switch
        {
            AutoMeasurementField.AirTemperatureC => CheckRange(field, value, 0, 50, 10, 40),
            AutoMeasurementField.HumidityPercent => CheckRange(field, value, 0, 100),
            AutoMeasurementField.ReservoirPh => CheckRange(field, value, 3.0, 9.0, 5.0, 7.0),
            AutoMeasurementField.ReservoirEc => CheckRange(field, value, 0, 5.0, warningMin: null, warningMax: 3.0),
            AutoMeasurementField.ReservoirWaterTempC => CheckRange(field, value, 0, 35, 15, 26),
            AutoMeasurementField.ReservoirLevelLiters => CheckRange(field, value, 0, null),
            AutoMeasurementField.ReservoirLevelCm => CheckRange(field, value, 0, null),
            AutoMeasurementField.DissolvedOxygenMgL => CheckRange(field, value, 0, 20, warningMin: 4, warningMax: null),
            AutoMeasurementField.OrpMv => CheckRange(field, value, -500, 1200, 250, 650),
            AutoMeasurementField.PpfdMol => CheckRange(field, value, 0, 2500, warningMin: null, warningMax: 1500),
            AutoMeasurementField.Co2Ppm => CheckRange(field, value, 0, 5000, warningMin: null, warningMax: 2000),
            _ => Valid()
        };
    }

    /// <summary>
    /// Der Name, unter dem diese Groesse in der physikalischen Tabelle steht.
    /// </summary>
    /// <remarks>
    /// Die beiden Seiten heissen verschieden — hier Feldnamen des Modells, dort
    /// Kachel-Schluessel. Ohne diese Uebersetzung liefe die aeussere Schranke
    /// ins Leere und meldete trotzdem nichts: unbekannte Groessen gelten in
    /// <see cref="MeasurementSanityService.IstPhysikalischMoeglich"/> absichtlich
    /// als plausibel.
    ///
    /// Felder ohne Eintrag (die beiden Fuellstaende) sind nach oben offen —
    /// Tankgroessen reichen von 20 bis ueber 1000 Liter. Fuer sie greift allein
    /// das engere Band unten.
    /// </remarks>
    private static string? PhysikSchluessel(AutoMeasurementField field) => field switch
    {
        AutoMeasurementField.AirTemperatureC => "air-temp",
        AutoMeasurementField.HumidityPercent => "humidity",
        AutoMeasurementField.ReservoirPh => "ph",
        AutoMeasurementField.ReservoirEc => "ec",
        AutoMeasurementField.ReservoirWaterTempC => "water-temp",
        AutoMeasurementField.DissolvedOxygenMgL => "do",
        AutoMeasurementField.OrpMv => "orp",
        AutoMeasurementField.PpfdMol => "ppfd",
        AutoMeasurementField.Co2Ppm => "co2",
        _ => null,
    };

    private static AutoMeasurementValueGuardResult CheckRange(
        AutoMeasurementField field,
        double value,
        double? rejectMin,
        double? rejectMax,
        double? warningMin = null,
        double? warningMax = null)
    {
        if (rejectMin.HasValue && value < rejectMin.Value)
        {
            return Reject(field, value, $"Wert liegt unter dem Hard-Limit {Format(rejectMin.Value)}.");
        }

        if (rejectMax.HasValue && value > rejectMax.Value)
        {
            return Reject(field, value, $"Wert liegt über dem Hard-Limit {Format(rejectMax.Value)}.");
        }

        if (warningMin.HasValue && value < warningMin.Value)
        {
            return Warning(field, value, $"Wert liegt unter dem Warnbereich {Format(warningMin.Value)}.");
        }

        if (warningMax.HasValue && value > warningMax.Value)
        {
            return Warning(field, value, $"Wert liegt über dem Warnbereich {Format(warningMax.Value)}.");
        }

        return Valid();
    }

    private static AutoMeasurementValueGuardResult Valid()
        => new(true, AutoMeasurementValueSeverity.None, null);

    private static AutoMeasurementValueGuardResult Warning(AutoMeasurementField field, double value, string reason)
        => new(true, AutoMeasurementValueSeverity.Warning, $"{field} Wert {Format(value)}: {reason}");

    private static AutoMeasurementValueGuardResult Reject(AutoMeasurementField field, double value, string reason)
        => new(false, AutoMeasurementValueSeverity.Reject, $"{field} Wert {Format(value)}: {reason}");

    private static string Format(double value)
        => value.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
}
