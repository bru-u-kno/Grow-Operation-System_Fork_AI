using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Licht in der Dunkelphase der Blüte — der teuerste Fehler im Zyklus.
/// </summary>
/// <remarks>
/// Eine versagende Zeitschaltuhr oder jemand, der zum Nachsehen die Lampe
/// anmacht: die Pflanze liest das als Sommer und antwortet mit Rückwuchs oder
/// Zwitterblüten. Beides fällt erst Wochen später auf, und dann ist es zu spät.
/// </remarks>
public sealed class LightIntrusionGuardTests
{
    /// <summary>12/12, an um 08:00, aus um 20:00.</summary>
    private static readonly LearnedCycle Bluete = new(12, new TimeOnly(8, 0), new TimeOnly(20, 0), 4);

    /// <summary>18/6, an um 06:00, aus um 00:00.</summary>
    private static readonly LearnedCycle Veg = new(18, new TimeOnly(6, 0), new TimeOnly(0, 0), 4);

    [Fact]
    public void LightAtMidnightInFlowerIsAnIntrusion()
    {
        Assert.True(LightIntrusionGuard.IsIntrusion(
            Bluete, new TimeOnly(2, 30), GrowStage.Flower, SeedType.Feminized));
    }

    [Fact]
    public void LightDuringTheLightPhaseIsNormal()
    {
        // Etwa nach einem Stromausfall: mitten am Tag wieder an — kein Alarm.
        Assert.False(LightIntrusionGuard.IsIntrusion(
            Bluete, new TimeOnly(12, 0), GrowStage.Flower, SeedType.Feminized));
    }

    [Fact]
    public void AShortOvershootAfterLightsOffIsTolerated()
    {
        // Schaltuhren sind nicht taktgenau. Fuenf Minuten Nachzucken um 20:05
        // ist kein Einbruch, 20:30 schon.
        Assert.False(LightIntrusionGuard.IsIntrusion(
            Bluete, new TimeOnly(20, 5), GrowStage.Flower, SeedType.Feminized));
        Assert.True(LightIntrusionGuard.IsIntrusion(
            Bluete, new TimeOnly(20, 30), GrowStage.Flower, SeedType.Feminized));
    }

    [Fact]
    public void AnEarlySwitchOnBeforeTheLearnedTimeIsTolerated()
    {
        // 23.10.2026: Licht ging um 05:01:16 an, der gelernte Median lag bei
        // 05:02 — 46 Sekunden zu frueh, und der Push "Licht in der Dunkelphase"
        // kam. Die Flanken streuen um Minuten, weil sie aus dem Poll-Takt
        // kommen. Zehn Minuten vor dem gelernten Einschalten ist kein Einbruch,
        // eine halbe Stunde davor schon.
        Assert.False(LightIntrusionGuard.IsIntrusion(
            Bluete, new TimeOnly(7, 59), GrowStage.Flower, SeedType.Feminized));
        Assert.False(LightIntrusionGuard.IsIntrusion(
            Bluete, new TimeOnly(7, 50), GrowStage.Flower, SeedType.Feminized));
        Assert.True(LightIntrusionGuard.IsIntrusion(
            Bluete, new TimeOnly(7, 30), GrowStage.Flower, SeedType.Feminized));
    }

    [Fact]
    public void AnEarlySwitchOnIsToleratedAcrossMidnight()
    {
        // Licht an um 00:05: 23:58 davor liegt vor Mitternacht und ist noch
        // innerhalb der Schonfrist.
        var nachMitternacht = new LearnedCycle(12, new TimeOnly(0, 5), new TimeOnly(12, 5), 4);
        Assert.False(LightIntrusionGuard.IsIntrusion(
            nachMitternacht, new TimeOnly(23, 58), GrowStage.Flower, SeedType.Feminized));
        Assert.True(LightIntrusionGuard.IsIntrusion(
            nachMitternacht, new TimeOnly(23, 30), GrowStage.Flower, SeedType.Feminized));
    }

    [Fact]
    public void InVegItDoesNotMatter()
    {
        // Eine Stunde Licht mehr kostet in der Veg nichts.
        Assert.False(LightIntrusionGuard.IsIntrusion(
            Veg, new TimeOnly(3, 0), GrowStage.Veg, SeedType.Feminized));
    }

    [Fact]
    public void AnAutoflowerIsNotAtRisk()
    {
        // Die blueht unabhaengig vom Zyklus.
        Assert.False(LightIntrusionGuard.IsIntrusion(
            Bluete, new TimeOnly(2, 30), GrowStage.Flower, SeedType.Autoflower));
    }

    [Fact]
    public void WithoutALearnedCycleNothingIsClaimed()
    {
        // In den ersten Tagen nach dem Mappen gibt es noch keinen Zyklus — dann
        // lieber schweigen als raten.
        Assert.False(LightIntrusionGuard.IsIntrusion(
            null, new TimeOnly(2, 30), GrowStage.Flower, SeedType.Feminized));
    }

    [Fact]
    public void ATwentyFourHourCycleHasNoDarkPhaseToDisturb()
    {
        var dauerlicht = new LearnedCycle(24, new TimeOnly(0, 0), new TimeOnly(0, 0), 3);

        Assert.False(LightIntrusionGuard.IsIntrusion(
            dauerlicht, new TimeOnly(3, 0), GrowStage.Flower, SeedType.Feminized));
    }

    [Fact]
    public void AnOvernightLightPhaseIsHandledToo()
    {
        // Licht ueber Nacht (20:00–08:00) ist gaengige Praxis — die Dunkelphase
        // liegt dann tagsueber.
        var ueberNacht = new LearnedCycle(12, new TimeOnly(20, 0), new TimeOnly(8, 0), 4);

        Assert.False(LightIntrusionGuard.IsIntrusion(
            ueberNacht, new TimeOnly(23, 0), GrowStage.Flower, SeedType.Feminized));
        Assert.True(LightIntrusionGuard.IsIntrusion(
            ueberNacht, new TimeOnly(13, 0), GrowStage.Flower, SeedType.Feminized));
    }

    [Fact]
    public void TheMessageSaysWhatHappenedAndWhyItMatters()
    {
        var text = LightIntrusionGuard.Message("Hauptzelt", Bluete, new TimeOnly(2, 30));

        Assert.Contains("Hauptzelt", text);
        Assert.Contains("02:30", text);
        Assert.Contains("Dunkelphase", text);
        Assert.Contains("Zwittern", text);
    }
}
