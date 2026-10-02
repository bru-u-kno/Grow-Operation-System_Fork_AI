using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Die Kurzform des Phasenankers. Seit dem 02.10.2026 schaltet nichts mehr
/// nach Tagen um: Vegi und (bei der Autoflower) Blüte beginnen mit der
/// Bestätigung, vorher erinnert der Anker.
/// </summary>
public sealed class GrowStageResolverTests
{
    private static readonly DateTime Heute = new(2026, 7, 27);

    private static GrowRun Grow(Action<GrowRun>? anpassen = null)
    {
        var grow = new GrowRun
        {
            Name = "Test",
            StartDate = Heute.AddDays(-7),
            SeedType = SeedType.Feminized,
            StartMaterial = StartMaterial.Seed,
            EntryPoint = GrowEntryPoint.Germination,
            GerminatedAt = Heute.AddDays(-7),
        };
        anpassen?.Invoke(grow);
        return grow;
    }

    [Fact]
    public void AGrowWithoutAnyMeasurement_StillHasAStage()
    {
        // Der Fall aus dem Alltag: Grow läuft seit einer Woche, Sensoren liefern,
        // von Hand gemessen wurde noch nie. Vorher gab es hier gar keine Phase
        // und damit auf dem ganzen Bildschirm keinen einzigen Zielbereich.
        Assert.Equal(GrowStage.Seedling, GrowStageResolver.Resolve(Grow(), Heute));
    }

    [Fact]
    public void TheFirstTwoWeeksAfterGermination_AreSeedling()
    {
        var grow = Grow(g => { g.StartDate = Heute.AddDays(-3); g.GerminatedAt = Heute.AddDays(-3); });

        Assert.Equal(GrowStage.Seedling, GrowStageResolver.Resolve(grow, Heute));
    }

    [Fact]
    public void AfterTheSeedlingWeeks_ItStaysInTheAnzuchtUntilConfirmed()
    {
        // Entscheidung des Nutzers (02.10.2026): die Anzucht dauert eine, zwei
        // oder mehr Wochen — Vegi-Woche 1 beginnt erst mit „Vegi beginnt".
        var grow = Grow(g => { g.StartDate = Heute.AddDays(-30); g.GerminatedAt = Heute.AddDays(-30); });

        Assert.Equal(GrowStage.Seedling, GrowStageResolver.Resolve(grow, Heute));

        grow.VegStartedAt = Heute.AddDays(-16);
        Assert.Equal(GrowStage.Veg, GrowStageResolver.Resolve(grow, Heute));
    }

    [Fact]
    public void ARecordedFlip_BeatsEveryCalculation()
    {
        var grow = Grow(g => g.FlipDate = Heute.AddDays(-30));

        Assert.Equal(GrowStage.Flower, GrowStageResolver.Resolve(grow, Heute));
    }

    [Fact]
    public void TheFirstDaysAfterTheFlip_AreTransition()
    {
        var grow = Grow(g => g.FlipDate = Heute.AddDays(-3));

        Assert.Equal(GrowStage.Transition, GrowStageResolver.Resolve(grow, Heute));
    }

    [Fact]
    public void TheLastTwoWeeksBeforeHarvest_AreFinish()
    {
        // Neun Wochen Blüte, geflippt vor 58 Tagen ⇒ Ernte in 5 Tagen.
        var grow = Grow(g =>
        {
            g.FlipDate = Heute.AddDays(-58);
            g.BreederFlowerWeeksMax = 9;
        });

        Assert.Equal(GrowStage.Finish, GrowStageResolver.Resolve(grow, Heute));
    }

    [Fact]
    public void WithoutBreederWeeks_FinishIsNotGuessed()
    {
        var grow = Grow(g => g.FlipDate = Heute.AddDays(-58));

        Assert.Equal(GrowStage.Flower, GrowStageResolver.Resolve(grow, Heute));
    }

    [Fact]
    public void AFlipInTheFuture_IsStillVeg()
    {
        var grow = Grow(g =>
        {
            g.StartDate = Heute.AddDays(-30);
            g.GerminatedAt = Heute.AddDays(-30);
            g.VegStartedAt = Heute.AddDays(-16);
            g.FlipDate = Heute.AddDays(5);
        });

        Assert.Equal(GrowStage.Veg, GrowStageResolver.Resolve(grow, Heute));
    }

    [Fact]
    public void AnUnrootedClone_IsAClone()
    {
        var grow = Grow(g =>
        {
            g.StartMaterial = StartMaterial.Clone;
            g.GerminatedAt = null;
            g.CloneIsRooted = false;
            g.RootedAt = null;
        });

        Assert.Equal(GrowStage.Clone, GrowStageResolver.Resolve(grow, Heute));
    }

    [Fact]
    public void EnteringInBloom_TrustsTheEntryPoint_EvenWithTheFormsGerminationDate()
    {
        // Der Fehler vom 02.10.2026: das Formular trägt bei jedem späteren
        // Einstieg GerminatedAt = Start ein, und der alte Resolver sah den
        // Einstieg nur OHNE Keimdatum — „Einstieg Blüte" galt so als Veg.
        var grow = Grow(g => { g.EntryPoint = GrowEntryPoint.Flower; g.GerminatedAt = g.StartDate; });

        // Sieben Tage in der Blüte: noch Übergang, wie nach einem Flip.
        Assert.Equal(GrowStage.Transition, GrowStageResolver.Resolve(grow, Heute));

        grow.DaysAlreadyInPhase = 20;
        Assert.Equal(GrowStage.Flower, GrowStageResolver.Resolve(grow, Heute));
    }

    [Fact]
    public void Autoflower_BloomsOnlyWhenConfirmed()
    {
        // Vorher schaltete sie nach 28 Tagen von selbst in die Blüte.
        var grow = Grow(g =>
        {
            g.SeedType = SeedType.Autoflower;
            g.StartDate = Heute.AddDays(-40);
            g.GerminatedAt = Heute.AddDays(-40);
            g.VegStartedAt = Heute.AddDays(-26);
        });

        Assert.Equal(GrowStage.Veg, GrowStageResolver.Resolve(grow, Heute));

        grow.FlipDate = Heute.AddDays(-12); // „Blüte beginnt"
        Assert.Equal(GrowStage.Flower, GrowStageResolver.Resolve(grow, Heute));
    }

    [Fact]
    public void YoungAutoflower_IsStillVeg()
    {
        var grow = Grow(g =>
        {
            g.SeedType = SeedType.Autoflower;
            g.StartDate = Heute.AddDays(-20);
            g.GerminatedAt = Heute.AddDays(-20);
            g.VegStartedAt = Heute.AddDays(-6);
        });

        Assert.Equal(GrowStage.Veg, GrowStageResolver.Resolve(grow, Heute));
    }

    [Fact]
    public void AutoflowerEnteringMidGrow_CountsTheBroughtDaysForTheReminderToo()
    {
        // Einstieg mit 30 mitgebrachten Keimtagen, vor 10 Tagen angelegt: die
        // Pflanze ist real an Tag 40. Früher zählte das für die automatische
        // Blüte; heute für die Erinnerung — ab Keimung + 28, also vor 12 Tagen.
        var grow = Grow(g =>
        {
            g.SeedType = SeedType.Autoflower;
            g.StartDate = Heute.AddDays(-10);
            g.GerminatedAt = null;
            g.AutoflowerDaysSinceGermination = 30;
            g.VegStartedAt = Heute.AddDays(-10);
        });

        var stand = Phasenanker.Fuer(grow, Heute);
        Assert.Equal(GrowStage.Veg, stand.Stufe);
        Assert.Equal("bluete-beginn", stand.Erinnerung?.Art);
        Assert.Equal(Heute.AddDays(-12), stand.Erinnerung!.Ab);
    }
}
