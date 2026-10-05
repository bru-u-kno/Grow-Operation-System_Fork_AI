using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

public static class TentSensorMetricKeyMap
{
    public static string Resolve(SensorMetricType metricType)
        => metricType switch
        {
            SensorMetricType.AirTemperature => "temperature",
            SensorMetricType.Humidity => "humidity",
            SensorMetricType.Vpd => "vpd",
            SensorMetricType.Co2 => "co2",
            SensorMetricType.Ppfd => "ppfd",
            SensorMetricType.LightStatus => "light-status",
            SensorMetricType.ReservoirPh => "reservoir-ph",
            SensorMetricType.ReservoirEc => "reservoir-ec",
            SensorMetricType.ReservoirOrp => "orp",
            SensorMetricType.ReservoirDissolvedOxygen => "dissolved-oxygen",
            SensorMetricType.ReservoirWaterTemp => "reservoir-temp",
            SensorMetricType.ReservoirLevel => "reservoir-level",
            SensorMetricType.ReservoirLevelCm => "reservoir-level-cm",
            SensorMetricType.PumpCirculation => "pump-circulation",
            SensorMetricType.PumpAir => "pump-air",
            SensorMetricType.PumpCirculationPower => "pump-circulation-power",
            SensorMetricType.PumpAirPower => "pump-air-power",
            SensorMetricType.Chiller => "chiller",
            SensorMetricType.UpsBattery => "ups-battery",
            SensorMetricType.UpsStatus => "ups-status",
            // „outside", nicht „outdoor": die Luft um das Zelt, nicht die von draußen.
            SensorMetricType.OutsideTemperature => "outside-temperature",
            SensorMetricType.OutsideHumidity => "outside-humidity",
            SensorMetricType.OutsideVpd => "outside-vpd",
            _ => metricType.ToString()
        };
}
