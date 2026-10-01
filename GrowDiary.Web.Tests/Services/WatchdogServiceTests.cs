using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

public sealed class WatchdogServiceTests
{
    private static readonly DateTime Now = new(2026, 7, 26, 12, 0, 0, DateTimeKind.Utc);

    private static WatchdogTentPulse Tent(string name = "Hauptzelt", int sensors = 4, int? readingAgeMinutes = 3)
        => new(name, sensors, readingAgeMinutes is { } age ? Now.AddMinutes(-age) : null);

    private static WatchdogInput Healthy(
        bool configured = true,
        int snapshotAgeMinutes = 2,
        int haAgeMinutes = 2,
        string? haError = null,
        params WatchdogTentPulse[] tents) => new(
            configured,
            Now.AddMinutes(-snapshotAgeMinutes),
            Now.AddMinutes(-haAgeMinutes),
            haError,
            tents.Length > 0 ? tents : [Tent()]);

    [Fact]
    public void EverythingFresh_IsHealthy_AndSaysHowOldTheDataIs()
    {
        var verdict = WatchdogService.Evaluate(Healthy(tents: Tent(readingAgeMinutes: 3)), Now);

        Assert.Equal(WatchdogService.Ok, verdict.Code);
        Assert.False(verdict.IsProblem);
        Assert.Contains("3 Minuten", verdict.Detail);
    }

    [Fact]
    public void WithoutHomeAssistant_NothingToWatch()
    {
        var verdict = WatchdogService.Evaluate(Healthy(configured: false), Now);

        Assert.Equal(WatchdogService.Idle, verdict.Code);
        Assert.False(verdict.IsProblem);
    }

    [Fact]
    public void WithoutMappedSensors_NothingToWatch()
    {
        var verdict = WatchdogService.Evaluate(Healthy(tents: Tent(sensors: 0)), Now);

        Assert.Equal(WatchdogService.Idle, verdict.Code);
        Assert.False(verdict.IsProblem);
    }

    [Fact]
    public void StalledWorker_IsReportedFirst()
    {
        // Worker stopped: everything else is unknowable, so this must win over the others.
        var verdict = WatchdogService.Evaluate(
            Healthy(snapshotAgeMinutes: 40, haAgeMinutes: 40, tents: Tent(readingAgeMinutes: 40)), Now);

        Assert.Equal(WatchdogService.WorkerStalled, verdict.Code);
        Assert.True(verdict.IsProblem);
    }

    [Fact]
    public void NeverRanWorker_CountsAsStalled()
        => Assert.Equal(
            WatchdogService.WorkerStalled,
            WatchdogService.Evaluate(new WatchdogInput(true, null, null, null, [Tent()]), Now).Code);

    [Fact]
    public void JustStarted_IsNotAStall()
    {
        // Nach einem Neustart ist noch keine Runde gedreht. Ohne diese Ausnahme
        // schlug der Watchdog nach jedem Update erst einmal Alarm — ausgerechnet
        // in dem Moment, in dem der Nutzer hinschaut.
        var verdict = WatchdogService.Evaluate(
            new WatchdogInput(true, null, null, null, [Tent()], StartedAtUtc: Now.AddMinutes(-1)), Now);

        Assert.Equal(WatchdogService.Starting, verdict.Code);
        Assert.False(verdict.IsProblem);
    }

    [Fact]
    public void StartedLongAgoAndStillNoRound_IsAStallAfterAll()
    {
        // Die Nachsicht gilt nur fuer den Anfang. Wer nach einer Viertelstunde
        // noch keine Runde gedreht hat, haengt wirklich.
        var verdict = WatchdogService.Evaluate(
            new WatchdogInput(true, null, null, null, [Tent()], StartedAtUtc: Now.AddHours(-2)), Now);

        Assert.Equal(WatchdogService.WorkerStalled, verdict.Code);
        Assert.True(verdict.IsProblem);
    }

    [Fact]
    public void AfterARoundHasRun_AStallIsStillAStall()
    {
        // Der Startzeitpunkt darf einen echten Stillstand nicht verdecken.
        var verdict = WatchdogService.Evaluate(
            new WatchdogInput(true, Now.AddMinutes(-40), Now.AddMinutes(-2), null, [Tent()], StartedAtUtc: Now.AddMinutes(-1)), Now);

        Assert.Equal(WatchdogService.WorkerStalled, verdict.Code);
    }

    [Fact]
    public void HomeAssistantSilent_IsReported_WithReason()
    {
        var verdict = WatchdogService.Evaluate(Healthy(haAgeMinutes: 30, haError: "HttpRequestException"), Now);

        Assert.Equal(WatchdogService.HaUnreachable, verdict.Code);
        Assert.True(verdict.IsProblem);
        Assert.Contains("HttpRequestException", verdict.Detail);
    }

    [Fact]
    public void ConnectionUpButNoFreshReadings_IsReported()
    {
        var verdict = WatchdogService.Evaluate(Healthy(tents: Tent(readingAgeMinutes: 45)), Now);

        Assert.Equal(WatchdogService.NoData, verdict.Code);
        Assert.True(verdict.IsProblem);
    }

    [Fact]
    public void ShortHiccup_IsNotAProblem()
    {
        // One missed 5-minute round must not raise an alarm.
        var verdict = WatchdogService.Evaluate(
            Healthy(snapshotAgeMinutes: 7, haAgeMinutes: 7, tents: Tent(readingAgeMinutes: 8)), Now);

        Assert.Equal(WatchdogService.Ok, verdict.Code);
        Assert.False(verdict.IsProblem);
    }

    [Fact]
    public void OneDarkTentAmongLiveOnes_IsReported_ByName()
    {
        // The case a global "newest reading anywhere" could never see: tent B keeps
        // reporting, so the freshest reading looks fine while tent A is dead.
        var verdict = WatchdogService.Evaluate(
            Healthy(tents: [Tent("Hauptzelt", readingAgeMinutes: 45), Tent("Klon-Box", readingAgeMinutes: 3)]), Now);

        Assert.Equal(WatchdogService.TentDark, verdict.Code);
        Assert.True(verdict.IsProblem);
        Assert.Contains("Hauptzelt", verdict.Detail);
        Assert.Contains("45 Minuten", verdict.Detail);
    }

    [Fact]
    public void TentThatNeverDelivered_IsNamedAsSuch()
    {
        var verdict = WatchdogService.Evaluate(
            Healthy(tents: [Tent("Neues Zelt", readingAgeMinutes: null), Tent("Hauptzelt", readingAgeMinutes: 2)]), Now);

        Assert.Equal(WatchdogService.TentDark, verdict.Code);
        Assert.Contains("noch nie Werte geliefert", verdict.Detail);
    }

    [Fact]
    public void AllTentsDark_IsStillTheGlobalNoDataVerdict()
    {
        var verdict = WatchdogService.Evaluate(
            Healthy(tents: [Tent("A", readingAgeMinutes: 45), Tent("B", readingAgeMinutes: 60)]), Now);

        Assert.Equal(WatchdogService.NoData, verdict.Code);
    }

    [Fact]
    public void SecondTentGoingDark_IsANewState_NotARepetition()
    {
        // The ChangeKey carries WHICH tents are dark: one more dark tent must produce a
        // different key, so the dedup in CheckAndNotifyAsync sends a fresh push.
        var oneDark = WatchdogService.Evaluate(
            Healthy(tents: [Tent("A", readingAgeMinutes: 45), Tent("B", readingAgeMinutes: 3), Tent("C", readingAgeMinutes: 3)]), Now);
        var twoDark = WatchdogService.Evaluate(
            Healthy(tents: [Tent("A", readingAgeMinutes: 55), Tent("B", readingAgeMinutes: 45), Tent("C", readingAgeMinutes: 3)]), Now);

        Assert.Equal(WatchdogService.TentDark, oneDark.Code);
        Assert.Equal(WatchdogService.TentDark, twoDark.Code);
        Assert.NotEqual(oneDark.ChangeKey, twoDark.ChangeKey);
    }

    [Fact]
    public void TentsWithoutSensors_DoNotCountAsDark()
    {
        // An empty spare tent must not keep the watchdog barking forever.
        var verdict = WatchdogService.Evaluate(
            Healthy(tents: [Tent("Leer", sensors: 0, readingAgeMinutes: null), Tent("Hauptzelt", readingAgeMinutes: 2)]), Now);

        Assert.Equal(WatchdogService.Ok, verdict.Code);
    }

    [Fact]
    public void OkVerdict_ReportsTheFreshestTent()
    {
        var verdict = WatchdogService.Evaluate(
            Healthy(tents: [Tent("A", readingAgeMinutes: 12), Tent("B", readingAgeMinutes: 4)]), Now);

        Assert.Equal(WatchdogService.Ok, verdict.Code);
        Assert.Contains("4 Minuten", verdict.Detail);
    }

    /// <summary>
    /// Das kritische Risiko schließt, sobald alles wieder läuft — auch wenn der
    /// Warn-Push nie rausging (keine Benachrichtigung eingerichtet, Ruhezeit)
    /// oder ein Neustart die Merkmarke gelöscht hat.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("ha-offline")]
    public void Recovery_AlwaysClosesTheRisk_PushOnlyWhenOneWasSent(string? gemeldet)
    {
        var schritt = WatchdogService.Entscheiden(istProblem: false, gemeldet, "idle");

        Assert.True(schritt.RisikoSchliessen);
        Assert.False(schritt.Warnen);
        Assert.Equal(gemeldet is not null, schritt.EntwarnungSenden);
    }

    [Fact]
    public void Problem_WarnsOncePerState()
    {
        Assert.True(WatchdogService.Entscheiden(true, null, "ha-offline").Warnen);
        Assert.False(WatchdogService.Entscheiden(true, "ha-offline", "ha-offline").Warnen);
        Assert.True(WatchdogService.Entscheiden(true, "ha-offline", "dark:Zelt 2").Warnen);
        Assert.False(WatchdogService.Entscheiden(true, null, "ha-offline").RisikoSchliessen);
    }
}
