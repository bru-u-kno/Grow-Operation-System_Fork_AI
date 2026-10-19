using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Die Anschläge. Jeder einzelne ist hier belegt, weil jeder einzelne der
/// letzte sein kann, der zwischen einem Rechenfehler und einem gekippten
/// Becken steht.
/// </summary>
public sealed class DosingGuardTests
{
    private static readonly DateTime Now = new(2026, 7, 27, 14, 0, 0, DateTimeKind.Utc);

    private static DosingPump Pump(Action<DosingPump>? anpassen = null)
    {
        var pump = new DosingPump
        {
            Id = 1,
            TentId = 1,
            Name = "pH Minus",
            Purpose = DosingPurpose.PhDown,
            HaEntityId = "switch.dosier_ph_minus",
            MlPerMinute = 46,
            MaxSingleDoseMl = 5,
            MinIntervalMinutes = 18,
            MaxDosesPerDay = 6,
            MaxMlPerDay = 25,
            MaxReadingAgeMinutes = 10,
            AutomationEnabled = true,
            HasHomeAssistantAutoOff = true,
        };
        anpassen?.Invoke(pump);
        return pump;
    }

    private static DosingContext Context(
        double? reading = 6.42,
        int readingAgeMinutes = 2,
        bool probeCalibrated = true,
        bool probeOverdue = false,
        IReadOnlyList<DoseEvent>? today = null,
        bool? waterOk = true) => new(
            reading,
            TimeSpan.FromMinutes(readingAgeMinutes),
            probeCalibrated ? Now.AddDays(-9) : null,
            probeOverdue,
            today ?? [],
            waterOk,
            // Die Umwaelzung laeuft in diesen Tests: geprueft wird hier anderes,
            // und die Automatik verlangt sie seit dem Realitaets-Audit.
            CirculationOn: true);

    private static DoseEvent Done(double ml, DateTime at) => new()
    {
        Outcome = DoseOutcome.Done, DosedMl = ml, OccurredAtUtc = at,
    };

    // ---------- Grundfall ----------

    [Fact]
    public void AReasonableDose_IsAllowed()
    {
        var decision = DosingGuard.Evaluate(Pump(), 3.5, Context(), Now);

        Assert.True(decision.Allowed);
        Assert.Equal(3.5, decision.Ml);
        Assert.Equal(4.57, decision.Seconds, 2);
    }

    [Fact]
    public void AnUncalibratedPump_DosesNothing()
    {
        var decision = DosingGuard.Evaluate(Pump(p => p.MlPerMinute = null), 3.5, Context(), Now);

        Assert.False(decision.Allowed);
        Assert.Contains("nicht kalibriert", decision.Reason);
    }

    [Fact]
    public void WithoutAnEntity_DosesNothing()
    {
        var decision = DosingGuard.Evaluate(Pump(p => p.HaEntityId = ""), 3.5, Context(), Now);

        Assert.False(decision.Allowed);
    }

    // ---------- Deckelung statt Ablehnung ----------

    [Fact]
    public void TooLargeARequest_IsCappedRatherThanRefused()
    {
        // Die Wirkung kommt dann eben in zwei Schritten. Ablehnen hiesse, dass
        // gar nichts passiert — und der Wert bleibt, wo er ist.
        var decision = DosingGuard.Evaluate(Pump(), 40, Context(), Now);

        Assert.True(decision.Allowed);
        Assert.Equal(5, decision.Ml);
    }

    [Fact]
    public void TheRemainderOfTheDailyAllowance_IsWhatGoesOut()
    {
        var heute = new[] { Done(22, Now.AddHours(-5)) };
        var decision = DosingGuard.Evaluate(Pump(), 5, Context(today: heute), Now);

        Assert.True(decision.Allowed);
        Assert.Equal(3, decision.Ml);   // 25 − 22
    }

    // ---------- Tagesgrenzen ----------

    [Fact]
    public void TheDailyCount_StopsIt()
    {
        var heute = Enumerable.Range(1, 6).Select(i => Done(1, Now.AddHours(-i))).ToList();

        var decision = DosingGuard.Evaluate(Pump(), 2, Context(today: heute), Now);

        Assert.False(decision.Allowed);
        Assert.Contains("Tagesgrenze", decision.Reason);
    }

    [Fact]
    public void TheDailyVolume_StopsIt()
    {
        var heute = new[] { Done(25, Now.AddHours(-6)) };

        var decision = DosingGuard.Evaluate(Pump(), 2, Context(today: heute), Now);

        Assert.False(decision.Allowed);
        Assert.Contains("Tagesmenge", decision.Reason);
    }

    // ---------- Mischzeit ----------

    [Fact]
    public void WithinTheMixingWindow_ItWaits()
    {
        // Gegen einen Wert zu dosieren, der die vorige Dosis noch nicht enthaelt,
        // ueberschiesst sicher.
        var heute = new[] { Done(2, Now.AddMinutes(-5)) };

        var decision = DosingGuard.Evaluate(Pump(), 2, Context(today: heute), Now);

        Assert.False(decision.Allowed);
        Assert.Contains("mischen", decision.Reason);
    }

    [Fact]
    public void AfterTheMixingWindow_ItProceeds()
    {
        var heute = new[] { Done(2, Now.AddMinutes(-19)) };

        Assert.True(DosingGuard.Evaluate(Pump(), 2, Context(today: heute), Now).Allowed);
    }

    [Fact]
    public void ACalibrationRun_DoesNotStartTheMixingClock()
    {
        // Der Kalibrierlauf geht in den Messbecher, nicht ins Becken. Beim
        // ersten Durchspielen war deshalb nach dem Kalibrieren 18 Minuten lang
        // keine Dosis moeglich — die Sperre gilt aber der Loesung, an der der
        // Lauf nichts geaendert hat.
        var kalibrierung = new DoseEvent
        {
            Outcome = DoseOutcome.Done, Trigger = DoseTrigger.Calibration,
            DosedMl = 0, SecondsRun = 15, OccurredAtUtc = Now.AddMinutes(-1),
        };

        Assert.True(DosingGuard.Evaluate(Pump(), 2, Context(today: [kalibrierung]), Now).Allowed);
    }

    [Fact]
    public void RejectedRequests_DoNotStartTheMixingClock()
    {
        // Aus einer abgelehnten Anfrage ist nichts geflossen — sie darf die
        // naechste nicht blockieren.
        var heute = new[] { new DoseEvent { Outcome = DoseOutcome.Rejected, DosedMl = 0, OccurredAtUtc = Now.AddMinutes(-1) } };

        Assert.True(DosingGuard.Evaluate(Pump(), 2, Context(today: heute), Now).Allowed);
    }

    // ---------- Harte Grenzen ----------

    [Fact]
    public void ALowReservoir_StopsIt()
    {
        var decision = DosingGuard.Evaluate(Pump(), 2, Context(waterOk: false), Now);

        Assert.False(decision.Allowed);
        Assert.Contains("Wasserstand", decision.Reason);
    }

    [Fact]
    public void AnUnknownWaterLevel_IsNotAnObstacle()
    {
        // Wer keinen Fuellstandssensor hat, soll trotzdem dosieren koennen.
        Assert.True(DosingGuard.Evaluate(Pump(), 2, Context(waterOk: null), Now).Allowed);
    }

    [Fact]
    public void ARuntimeBeyondTheHardCeiling_IsRefused()
    {
        // Sehr langsame Pumpe: 5 ml braeuchten 300 s. Das laeuft nicht.
        var decision = DosingGuard.Evaluate(Pump(p => p.MlPerMinute = 1), 5, Context(), Now);

        Assert.False(decision.Allowed);
        Assert.Contains("harten Grenze", decision.Reason);
    }

    // ---------- Nur für die Automatik ----------

    [Fact]
    public void AutomaticDosing_NeedsTheAutomationSwitch()
    {
        var decision = DosingGuard.EvaluateAutomatic(Pump(p => p.AutomationEnabled = false), 2, Context(), Now);

        Assert.False(decision.Allowed);
        Assert.Contains("Automatik", decision.Reason);
    }

    [Fact]
    public void WithoutTheHomeAssistantAutoOff_AutomationStaysLocked()
    {
        // Stuerzt Cultivate Control zwischen Ein- und Ausschalten ab, laeuft die Pumpe
        // weiter. Unbeaufsichtigt ist das nicht vertretbar.
        var decision = DosingGuard.EvaluateAutomatic(Pump(p => p.HasHomeAssistantAutoOff = false), 2, Context(), Now);

        Assert.False(decision.Allowed);
        Assert.Contains("Abschaltung", decision.Reason);
    }

    [Fact]
    public void AStaleReading_IsNotDosedAgainst()
    {
        var decision = DosingGuard.EvaluateAutomatic(Pump(), 2, Context(readingAgeMinutes: 45), Now);

        Assert.False(decision.Allowed);
        Assert.Contains("45 min alt", decision.Reason);
    }

    [Fact]
    public void AnUncalibratedProbe_BlocksAutomaticDosing()
    {
        var decision = DosingGuard.EvaluateAutomatic(Pump(), 2, Context(probeCalibrated: false), Now);

        Assert.False(decision.Allowed);
        Assert.Contains("nie kalibriert", decision.Reason);
    }

    [Fact]
    public void AnOverdueProbe_BlocksAutomaticDosing()
    {
        var decision = DosingGuard.EvaluateAutomatic(Pump(), 2, Context(probeOverdue: true), Now);

        Assert.False(decision.Allowed);
        Assert.Contains("überfällig", decision.Reason);
    }

    [Fact]
    public void WithoutAReading_AutomaticDosingRefuses()
    {
        var decision = DosingGuard.EvaluateAutomatic(Pump(), 2, Context(reading: null), Now);

        Assert.False(decision.Allowed);
    }

    [Fact]
    public void ManualDosing_DoesNotNeedAFreshReading()
    {
        // Wer selbst drueckt, steht daneben. Die strengeren Riegel gelten nur
        // fuer die Automatik — sonst koennte man eine frisch eingerichtete
        // Pumpe nie pruefen.
        var context = Context(reading: null, readingAgeMinutes: 999, probeCalibrated: false, probeOverdue: true);

        Assert.True(DosingGuard.Evaluate(Pump(), 2, context, Now).Allowed);
    }

    // ---------- Testbetrieb ----------

    [Fact]
    public void InSimulation_NoEntityIsNeeded()
    {
        // Ohne Hardware laesst sich sonst kein Schritt durchspielen.
        var pump = Pump(p => { p.SimulationMode = true; p.HaEntityId = ""; });

        Assert.True(DosingGuard.Evaluate(pump, 3.5, Context(), Now).Allowed);
    }

    [Fact]
    public void SimulationDoesNotLoosenTheOtherLimits()
    {
        // Sonst pruefte der Test etwas anderes als der Ernstfall.
        var pump = Pump(p => { p.SimulationMode = true; p.HaEntityId = ""; });
        var heute = new[] { Done(2, Now.AddMinutes(-5)) };

        var decision = DosingGuard.Evaluate(pump, 2, Context(today: heute), Now);

        Assert.False(decision.Allowed);
        Assert.Contains("mischen", decision.Reason);
    }

    [Fact]
    public void AnUncalibratedSimulatedPump_StillRefuses()
    {
        var pump = Pump(p => { p.SimulationMode = true; p.MlPerMinute = null; });

        Assert.False(DosingGuard.Evaluate(pump, 3.5, Context(), Now).Allowed);
    }

    [Fact]
    public void SimulationNeedsNoHomeAssistantAutoOff()
    {
        // Es gibt nichts, das weiterlaufen koennte.
        var pump = Pump(p => { p.SimulationMode = true; p.HasHomeAssistantAutoOff = false; });

        Assert.True(DosingGuard.EvaluateAutomatic(pump, 2, Context(), Now).Allowed);
    }

    [Fact]
    public void EntitySplit_SeparatesDomainFromName()
    {
        Assert.Equal(("switch", "dosier_ph_minus"), DosingService.SplitEntity("switch.dosier_ph_minus"));
        // Ohne Punkt: switch ist die vernuenftige Annahme.
        Assert.Equal(("switch", "pumpe"), DosingService.SplitEntity("pumpe"));
    }
}
