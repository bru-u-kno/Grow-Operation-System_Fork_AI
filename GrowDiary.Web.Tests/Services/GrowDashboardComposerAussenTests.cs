using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Die Luft außerhalb des Zelts (Raumluft, z. B. der eingebaute Fühler eines
/// AC-Infinity-Controllers) als Live-Kacheln.
/// </summary>
/// <remarks>
/// Drei Zusagen: Kacheln nur mit zugeordnetem Sensor (sonst drei leere Kacheln
/// und eine falsche Zahl bei „N Sensoren live"), Name und Einheit aus derselben
/// Tabelle wie Verlauf und Meldungen, und kein Ziel — die Raumluft ist Umgebung,
/// kein Zeltklima. Und die Innenwerte bleiben unberührt: das VPD im Zelt wird nie
/// aus der Raumluft gerechnet.
/// </remarks>
public sealed class GrowDashboardComposerAussenTests
{
    // Ohne aktiven Grow liest BuildTentMetrics keinen der Dienste (siehe
    // GrowDashboardComposerReservoirTests).
    private static readonly GrowDashboardComposer Composer = new(null!, null!);

    private static Tent Zelt() => new() { Id = 1, Name = "Zelt 1", ActiveGrows = new() };

    private static HomeAssistantState Zustand(double wert, string? einheit = null)
        => new() { State = wert.ToString(System.Globalization.CultureInfo.InvariantCulture), NumericValue = wert, UnitOfMeasurement = einheit, FriendlyName = "BIG Controller" };

    private static readonly string[] AussenSchluessel =
    [
        TentSensorMetricKeyMap.Resolve(SensorMetricType.OutsideTemperature),
        TentSensorMetricKeyMap.Resolve(SensorMetricType.OutsideHumidity),
        TentSensorMetricKeyMap.Resolve(SensorMetricType.OutsideVpd),
    ];

    [Fact]
    public void Zugeordnete_Aussenwerte_stehen_als_Kacheln_da_mit_Name_und_Einheit_ohne_Ziel()
    {
        var zustaende = new Dictionary<string, HomeAssistantState>
        {
            ["outside-temperature"] = Zustand(20.4, "°C"),
            ["outside-humidity"] = Zustand(58, "%"),
            ["outside-vpd"] = Zustand(0.99, "kPa"),
        };

        var karten = Composer.BuildTentMetrics(Zelt(), zustaende, new List<Measurement>());

        Assert.Equal(["outside-temperature", "outside-humidity", "outside-vpd"], AussenSchluessel);
        foreach (var schluessel in AussenSchluessel)
        {
            var karte = Assert.Single(karten, k => k.Key == schluessel);
            var (name, einheit) = AlertEvaluationService.MetricDisplay(schluessel);
            Assert.Equal(name, karte.Label);
            Assert.StartsWith("Außen ", karte.Label);
            Assert.Equal(einheit.Trim(), karte.Unit);
            Assert.Equal("live", karte.ValueSource);
            Assert.NotEqual("–", karte.Value);
            Assert.Null(karte.TargetMin);
            Assert.Null(karte.TargetMax);
        }

        Assert.Equal(20.4, karten.Single(k => k.Key == "outside-temperature").NumericValue);
        Assert.Equal(0.99, karten.Single(k => k.Key == "outside-vpd").NumericValue);
    }

    [Fact]
    public void Ohne_Zuordnung_gibt_es_keine_Aussenkachel()
    {
        var zustaende = new Dictionary<string, HomeAssistantState> { ["temperature"] = Zustand(24.1) };

        var karten = Composer.BuildTentMetrics(Zelt(), zustaende, new List<Measurement>());

        // Mengenwaechter: die Innenkacheln entstehen, die Schleife lief also.
        Assert.Contains(karten, k => k.Key == "temperature");
        Assert.DoesNotContain(karten, k => AussenSchluessel.Contains(k.Key));
    }

    [Fact]
    public void Nur_ein_Teil_zugeordnet_nur_dieser_Teil()
    {
        var zustaende = new Dictionary<string, HomeAssistantState> { ["outside-humidity"] = Zustand(61) };

        var karten = Composer.BuildTentMetrics(Zelt(), zustaende, new List<Measurement>());

        Assert.Single(karten, k => k.Key == "outside-humidity");
        Assert.DoesNotContain(karten, k => k.Key is "outside-temperature" or "outside-vpd");
    }

    [Fact]
    public void Das_VPD_im_Zelt_wird_nie_aus_der_Raumluft_gerechnet()
    {
        // Nur außen zugeordnet: innen fehlen Temperatur und Feuchte. Das Zelt-VPD
        // muss dann „–" bleiben — rechnete es mit der Raumluft, stünde dort ein
        // plausibler, aber falscher Wert.
        var zustaende = new Dictionary<string, HomeAssistantState>
        {
            ["outside-temperature"] = Zustand(20.4),
            ["outside-humidity"] = Zustand(58),
        };

        var karten = Composer.BuildTentMetrics(Zelt(), zustaende, new List<Measurement>());

        var vpd = Assert.Single(karten, k => k.Key == "vpd");
        Assert.Null(vpd.NumericValue);
        Assert.Equal("–", vpd.Value);
    }
}
