using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Keeps the hardware inventory in sync with the tent's Home Assistant mapping: every
/// mapped measurement entity automatically appears under "Sensoren" as a trackable
/// hardware item with sensible calibration defaults, so calibration reminders work from
/// day one without a second manual step.
///
/// Link key is (TentId, MetricType) — TentSensor rows are deleted and re-inserted on
/// every mapping save, so their ids cannot anchor the relationship. TentSensorId and
/// HaEntityId are refreshed on each sync. User edits (name, intervals, status) are never
/// overwritten, and unmapping does not delete the physical item.
/// </summary>
public sealed class TentSensorHardwareSyncService
{
    private readonly HardwareRepository _hardware;
    private readonly ILogger<TentSensorHardwareSyncService> _logger;

    public TentSensorHardwareSyncService(HardwareRepository hardware, ILogger<TentSensorHardwareSyncService> logger)
    {
        _hardware = hardware;
        _logger = logger;
    }

    /// <summary>Metric types that represent a physical measurement sensor worth tracking.</summary>
    private static readonly Dictionary<SensorMetricType, (string Name, HardwareItemCriticality Criticality)> TrackableMetrics = new()
    {
        [SensorMetricType.ReservoirPh] = ("pH-Sonde", HardwareItemCriticality.High),
        [SensorMetricType.ReservoirEc] = ("EC-Sensor", HardwareItemCriticality.High),
        [SensorMetricType.ReservoirOrp] = ("ORP-Sensor", HardwareItemCriticality.High),
        [SensorMetricType.ReservoirDissolvedOxygen] = ("DO-Sensor", HardwareItemCriticality.High),
        [SensorMetricType.ReservoirWaterTemp] = ("Wassertemperatur-Sensor", HardwareItemCriticality.High),
        [SensorMetricType.ReservoirLevel] = ("Wasserstand-Sensor (L)", HardwareItemCriticality.High),
        [SensorMetricType.ReservoirLevelCm] = ("Wasserstand-Sensor (cm)", HardwareItemCriticality.High),
        [SensorMetricType.AirTemperature] = ("Temperatursensor", HardwareItemCriticality.Medium),
        [SensorMetricType.Humidity] = ("Luftfeuchte-Sensor", HardwareItemCriticality.Medium),
        [SensorMetricType.Vpd] = ("VPD-Sensor", HardwareItemCriticality.Medium),
        [SensorMetricType.Co2] = ("CO₂-Sensor", HardwareItemCriticality.Medium),
        [SensorMetricType.Ppfd] = ("PPFD-Sensor", HardwareItemCriticality.Medium),
        // Bewusst NICHT die Außenwerte (OutsideTemperature/-Humidity/-Vpd): das ist
        // meist der eingebaute Fühler des Controllers, ohne Kalibrierung und ohne
        // eigene Wartung. Drei Einträge unter „Sensoren" für ein Gerät, das schon
        // als Controller geführt wird, wären Rauschen. Ein Ausfall wird trotzdem
        // gemeldet — die Ausfallprüfung geht über die Zuordnung, nicht über diese Liste.
    };

    /// <summary>Default calibration cadence per probe type; null = not calibrated on a schedule.</summary>
    public static int? DefaultCalibrationIntervalDays(SensorMetricType metricType) => metricType switch
    {
        SensorMetricType.ReservoirPh => 14,
        SensorMetricType.ReservoirEc => 30,
        SensorMetricType.ReservoirOrp => 30,
        SensorMetricType.ReservoirDissolvedOxygen => 30,
        _ => null,
    };

    private static CalibrationEventType CalibrationTypeFor(SensorMetricType metricType) => metricType switch
    {
        SensorMetricType.ReservoirPh => CalibrationEventType.Ph,
        SensorMetricType.ReservoirEc => CalibrationEventType.Ec,
        SensorMetricType.ReservoirOrp => CalibrationEventType.Orp,
        SensorMetricType.ReservoirDissolvedOxygen => CalibrationEventType.Do,
        _ => CalibrationEventType.Other,
    };

    /// <summary>Syncs the hardware inventory to the tent's current sensor mapping.</summary>
    public void SyncForTent(Tent tent)
    {
        var items = _hardware.GetHardwareItemsByTent(tent.Id);

        foreach (var sensor in tent.Sensors.Where(s => s.IsActive && !string.IsNullOrWhiteSpace(s.HaEntityId)))
        {
            if (!TrackableMetrics.TryGetValue(sensor.MetricType, out var defaults))
            {
                continue;
            }

            var existing = items.FirstOrDefault(item => item.MetricType == sensor.MetricType);
            if (existing is not null)
            {
                if (existing.HaEntityId != sensor.HaEntityId || existing.TentSensorId != sensor.Id)
                {
                    existing.HaEntityId = sensor.HaEntityId;
                    existing.TentSensorId = sensor.Id;
                    _hardware.UpdateHardwareItem(existing);
                }

                continue;
            }

            var calibrationDays = DefaultCalibrationIntervalDays(sensor.MetricType);
            var created = _hardware.CreateHardwareItem(new HardwareItem
            {
                Name = string.IsNullOrWhiteSpace(sensor.DisplayLabel) ? defaults.Name : sensor.DisplayLabel!,
                Category = "Sensor",
                Status = HardwareItemStatus.Active,
                Criticality = defaults.Criticality,
                TentId = tent.Id,
                TentSensorId = sensor.Id,
                HaEntityId = sensor.HaEntityId,
                MetricType = sensor.MetricType,
                DeviceKind = HardwareDeviceKind.FixedSensor,
                CalibrationIntervalDays = calibrationDays,
                InstalledAtUtc = DateTime.UtcNow,
                Notes = "Automatisch aus dem Home-Assistant-Mapping angelegt.",
            });

            // Arm the calibration cycle right away: a planned event with a due date makes
            // the daily calibration push reminder work without a first manual calibration.
            if (calibrationDays is { } days)
            {
                _hardware.CreateCalibrationEvent(new CalibrationEvent
                {
                    HardwareItemId = created.Id,
                    CalibrationType = CalibrationTypeFor(sensor.MetricType),
                    Status = CalibrationEventStatus.Planned,
                    Title = created.Name,
                    DueAtUtc = DateTime.UtcNow.AddDays(days),
                });
            }

            _logger.LogInformation(
                "Sensor-Hardware automatisch angelegt: {Name} ({MetricType}) für Zelt {TentId}.",
                created.Name,
                sensor.MetricType,
                tent.Id);
        }
    }
}
