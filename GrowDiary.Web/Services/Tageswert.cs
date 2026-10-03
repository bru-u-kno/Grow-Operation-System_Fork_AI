using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Der dauerhafte Tageswert (Min/Median/Max …) einer Messgröße aus ihren Rohwerten.
/// </summary>
/// <remarks>
/// Eine Stelle für die nächtliche Aggregation im
/// <see cref="HomeAssistantSnapshotWorker"/> und für die Neuberechnung nach dem
/// Entfernen eines Nullbilds (<see cref="WassersondenNullbild"/>) — damit beide
/// dieselbe Mindestmenge verlangen.
/// </remarks>
public static class Tageswert
{
    /// <summary>Unter so vielen Rohwerten entsteht kein Tageswert.</summary>
    public const int MindestAnzahl = 3;

    /// <summary>Der Tageswert eines lokalen Kalendertags — oder null bei zu wenig Rohwerten.</summary>
    public static TentSensorDailyStat? Berechnen(SensorReadingRepository rohwerte, int tentId, string metricKey, DateOnly tagLokal)
        => Berechnen(rohwerte.GetReadingsForDay(tentId, metricKey, tagLokal), tentId, metricKey, tagLokal);

    /// <summary>Der Tageswert aus diesen Rohwerten — oder null bei zu wenig.</summary>
    /// <remarks>
    /// Für die Nullbild-Bereinigung: sie rechnet den neuen Tageswert, BEVOR sie
    /// etwas löscht, und reicht dafür die Rohwerte ohne das Nullbild herein.
    /// </remarks>
    public static TentSensorDailyStat? Berechnen(IReadOnlyList<TentSensorReading> readings, int tentId, string metricKey, DateOnly tagLokal)
    {
        if (readings.Count < MindestAnzahl) return null;

        return PercentileCalculator.ComputeStats(
            tentId, metricKey, tagLokal, readings.Select(r => r.Value).ToList(), readings[0].Unit);
    }
}
