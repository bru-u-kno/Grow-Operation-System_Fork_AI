using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Wenn kein Temperatur-Ziel möglich ist.
/// </summary>
/// <remarks>
/// Der Fall aus dem echten Zelt: 40 % Luftfeuchte, VPD-Ziel 0,40–0,50 kPa.
/// Schon bei 5 °C — der untersten gesuchten Temperatur — liegt das VPD bei
/// 0,41 kPa, also über dem Ziel. Es gibt schlicht keine Temperatur, die das
/// trifft; die Luftfeuchte ist die falsche Schraube gewesen.
///
/// Das Band liefert dann korrekt nichts. Der Fehler lag darin, dass die Kachel
/// daraufhin einfach leer blieb — die Rechnung kannte den Grund und behielt ihn
/// für sich.
/// </remarks>
public sealed class ClimateBandUnreachableTests
{
    private const double LeafOffset = -2.0;

    [Fact]
    public void AtVeryDryAir_NoTemperatureReachesALowVpdTarget()
    {
        var (min, max) = ClimateBandCalculator.TemperatureBand(40, 0.40, 0.50, LeafOffset);

        Assert.Null(min);
        Assert.Null(max);
    }

    [Fact]
    public void AnUnusablyColdSolutionIsNoRecommendation()
    {
        // Dieselben 40 %, aber das Veg-Ziel: physikalisch gibt es eine Antwort
        // bei 12–17 °C. Frueher wurde sie angezeigt — „unbrauchbar kalt ist
        // fuer sich die Aussage". Der Betreiber hat das Gegenteil bewiesen: auf
        // seiner Keimlings-Kachel stand „5,3 °C empfohlen", und er las es als
        // das, was es aussah — als Empfehlung. Unter 18 °C wird nichts mehr
        // empfohlen; die Kachel sagt stattdessen, dass die Feuchte der Hebel ist.
        var (min, max) = ClimateBandCalculator.TemperatureBand(40, 0.70, 0.90, LeafOffset);

        Assert.Null(min);
        Assert.Null(max);
    }

    [Fact]
    public void TheSeedlingCaseFromTheField_GetsNoFiveDegreeAdvice()
    {
        // Der gemeldete Fall woertlich: Saemlings-Ziel 0,4–0,5 kPa bei 53,7 %
        // Raumfeuchte ergab „5,3 °C empfohlen".
        var (min, max) = ClimateBandCalculator.TemperatureBand(53.7, 0.40, 0.50, LeafOffset);

        Assert.Null(min);
        Assert.Null(max);
    }

    [Fact]
    public void ABandReachingIntoTheRoomIsTrimmedNotDropped()
    {
        // Bei 80 % laeuft das Saemlings-Band von ~17,5 bis ~21 °C. Der Teil
        // unter 18 wird beschnitten, der Rest bleibt — jede Temperatur darin
        // trifft das VPD-Ziel weiterhin, beschneiden erfindet nichts.
        var (min, max) = ClimateBandCalculator.TemperatureBand(80, 0.40, 0.50, LeafOffset);

        Assert.NotNull(min);
        Assert.True(min!.Value >= ClimateBandCalculator.RecommendMinC);
        Assert.True(max!.Value > min.Value);
    }

    [Fact]
    public void AtSensibleHumidity_TheBandLandsInATentRange()
    {
        // 75 % und das Sämlings-Ziel: so sieht es aus, wenn die Luftfeuchte passt.
        var (min, max) = ClimateBandCalculator.TemperatureBand(75, 0.40, 0.50, LeafOffset);

        Assert.NotNull(min);
        Assert.InRange(min!.Value, 15, 30);
        Assert.InRange(max!.Value, 18, 35);
    }

    [Fact]
    public void TheHumidityBandStillWorksInTheSameSituation()
    {
        // Wichtig fürs Verständnis der Kachel: die Luftfeuchte BEKOMMT ein Ziel,
        // die Temperatur nicht. Genau dieser Unterschied fiel im Zelt auf.
        var (min, max) = ClimateBandCalculator.HumidityBand(28.1, 0.40, 0.50, LeafOffset);

        Assert.NotNull(min);
        Assert.InRange(min!.Value, 70, 85);
        Assert.InRange(max!.Value, 70, 85);
    }
}
