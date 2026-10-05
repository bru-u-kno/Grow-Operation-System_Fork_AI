using GrowDiary.Web.Models;

namespace GrowDiary.Web.Tests.Models;

/// <summary>
/// Die Zahlen hinter <see cref="SensorMetricType"/> bleiben, wie sie sind.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (05.10.2026, Prüfer zu den Außenwerten).</b> Zuordnung
/// und Geräte speichern den <i>Namen</i> der Messgröße, die Zelt-Momentaufnahme
/// eines Grows (<c>Grows.TentSnapshotJson</c>, ohne <c>JsonStringEnumConverter</c>)
/// aber die <i>Zahl</i>. Am Enum stand bis dahin „als Name gespeichert" — wer
/// das glaubte und umsortierte, hätte archivierte Grows still auf andere
/// Messgrößen verschoben, ohne dass irgendetwas rot wird.</para>
/// <para>Bewusst eine ausgeschriebene Liste: genau sie ist der Vertrag. Ein
/// neuer Wert wird hinten angehängt und hier nachgetragen; ein umsortierter
/// Wert macht diesen Test rot.</para>
/// </remarks>
public sealed class SensorMetricTypeZahlenTests
{
    private static readonly string[] Reihenfolge =
    [
        "AirTemperature", "Humidity", "Vpd", "Co2", "Ppfd", "LightStatus",
        "ReservoirPh", "ReservoirEc", "ReservoirOrp", "ReservoirDissolvedOxygen",
        "ReservoirWaterTemp", "ReservoirLevel", "ReservoirLevelCm",
        "PumpCirculation", "PumpAir", "PumpCirculationPower", "PumpAirPower",
        "Chiller", "UpsBattery", "UpsStatus",
        "OutsideTemperature", "OutsideHumidity", "OutsideVpd",
    ];

    [Fact]
    public void Jeder_Wert_behaelt_seine_Zahl()
    {
        var werte = Enum.GetValues<SensorMetricType>();

        // Mengenwächter: ohne Grundmenge prüfte die Schleife nichts.
        Assert.True(werte.Length >= 23, $"Nur {werte.Length} Werte gesehen.");
        Assert.Equal(Reihenfolge.Length, werte.Length);

        for (var zahl = 0; zahl < Reihenfolge.Length; zahl++)
        {
            Assert.True(
                Enum.TryParse<SensorMetricType>(Reihenfolge[zahl], out var wert),
                $"„{Reihenfolge[zahl]}\" gibt es nicht mehr — umbenannt?");
            Assert.True(
                (int)wert == zahl,
                $"{Reihenfolge[zahl]} hat die Zahl {(int)wert} statt {zahl}. Neue Werte nur am Ende "
                + "anhängen: die Zelt-Momentaufnahme archivierter Grows speichert die Zahl.");
        }
    }
}
