using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Die Regeln des Phasenankers — eine Wahrheit für Phase und Woche.
/// </summary>
/// <remarks>
/// Entscheidungen des Nutzers vom 02.10.2026: die Vegi beginnt mit der
/// Bestätigung (Steckling: „Bewurzelung abgeschlossen"), die Blüte einer
/// Autoflower ebenso; wo früher nach Tagen umgeschaltet wurde, erinnert der
/// Anker ab genau diesem Schätzwert.
/// </remarks>
public sealed class PhasenankerTests
{
    private static readonly DateTime Heute = new(2026, 10, 2);

    private static GrowRun Samen(int tageSeitStart, Action<GrowRun>? anpassen = null)
    {
        var grow = new GrowRun
        {
            Id = 1,
            Name = "Samen",
            StartDate = Heute.AddDays(-tageSeitStart),
            SeedType = SeedType.Feminized,
            StartMaterial = StartMaterial.Seed,
            EntryPoint = GrowEntryPoint.Germination,
            Status = GrowStatus.Running,
        };
        anpassen?.Invoke(grow);
        return grow;
    }

    // ---------------- Anzucht ----------------

    [Fact]
    public void OhneBestaetigungBleibtEsAnzucht_AuchNachSechzehnTagen()
    {
        var stand = Phasenanker.Fuer(Samen(16), Heute);

        Assert.Equal(Ankerphase.Anzucht, stand.Phase);
        Assert.Equal(Anzuchtart.Keimung, stand.Anzucht);
        Assert.Null(stand.VegAb);
        Assert.Equal(17, stand.TagInPhase);
        Assert.Equal(3, stand.WocheInPhase);
    }

    [Fact]
    public void DieErinnerungKommtAbDemBisherigenSchaetzwert()
    {
        // Tag 13: noch nicht. Tag 14: der alte Umschalttag — ab hier die Frage.
        Assert.Null(Phasenanker.Fuer(Samen(13), Heute).Erinnerung);

        var erinnerung = Phasenanker.Fuer(Samen(16), Heute).Erinnerung!;
        Assert.Equal("vegi-beginn", erinnerung.Art);
        Assert.Equal("confirm-veg", erinnerung.Aktion);
        Assert.Equal("Vegi beginnt", erinnerung.Knopf);
        Assert.Equal("Anzucht Tag 17 — Vegi-Beginn bestätigen?", erinnerung.Text);
        Assert.Equal(17, erinnerung.Tage);
        Assert.Equal(Heute.AddDays(-2), erinnerung.Ab);
    }

    [Fact]
    public void BestaetigteKeimungMachtAusDerKeimungDenSaemling()
    {
        var grow = Samen(10, g => g.GerminatedAt = Heute.AddDays(-6));

        Assert.Equal(Anzuchtart.Saemling, Phasenanker.Fuer(grow, Heute).Anzucht);
        Assert.Equal(Anzuchtart.Keimung, Phasenanker.Fuer(grow, Heute.AddDays(-8)).Anzucht);
    }

    [Fact]
    public void EinstiegSaemlingZaehltDieMitgebrachtenTageZurAnzucht()
    {
        var grow = Samen(3, g => { g.EntryPoint = GrowEntryPoint.Seedling; g.GerminatedAt = g.StartDate; g.DaysAlreadyInPhase = 12; });
        var stand = Phasenanker.Fuer(grow, Heute);

        Assert.Equal(Heute.AddDays(-15), stand.AnzuchtAb);
        Assert.Equal(Anzuchtart.Saemling, stand.Anzucht);
        // Richtwert 14 Tage, zwölf davon mitgebracht: seit gestern erinnert.
        Assert.Equal(Heute.AddDays(-1), stand.Erinnerung?.Ab);
    }

    // ---------------- Vegi ----------------

    [Fact]
    public void VegiBeginntMitDerBestaetigung_UndZaehltAbWocheEins()
    {
        var grow = Samen(16, g => g.VegStartedAt = Heute.AddHours(9));
        var stand = Phasenanker.Fuer(grow, Heute);

        Assert.Equal(Ankerphase.Veg, stand.Phase);
        Assert.Equal(Heute, stand.VegAb);
        Assert.Equal(1, stand.TagInPhase);
        Assert.Equal(1, stand.WocheInPhase);
        Assert.Null(stand.Erinnerung);
    }

    [Fact]
    public void DieWocheHatKeineObergrenze()
    {
        var grow = Samen(100, g => g.VegStartedAt = Heute.AddDays(-80));

        Assert.Equal(12, Phasenanker.Fuer(grow, Heute).WocheInPhase);
        Assert.Equal(12, Phasenanker.Fuer(grow, Heute).WocheIn("Veg"));
    }

    [Fact]
    public void DasAutomatischeKeimdatumDesFormularsIstKeineVegiBestaetigung()
    {
        // GrowFormViewModel.ToGrow setzt bei jedem Einstieg ≠ Keimung
        // GerminatedAt = Start. Beim Einstieg Sämling heißt das: gekeimt — aber
        // nicht: Vegi.
        var grow = Samen(30, g => { g.EntryPoint = GrowEntryPoint.Seedling; g.GerminatedAt = g.StartDate; });

        Assert.Equal(Ankerphase.Anzucht, Phasenanker.Fuer(grow, Heute).Phase);
    }

    [Fact]
    public void EinstiegVegZaehltAlsBestaetigung_MitDenMitgebrachtenTagen()
    {
        var grow = Samen(5, g => { g.EntryPoint = GrowEntryPoint.Veg; g.GerminatedAt = g.StartDate; g.DaysAlreadyInPhase = 20; });
        var stand = Phasenanker.Fuer(grow, Heute);

        Assert.Equal(Ankerphase.Veg, stand.Phase);
        Assert.Equal(Heute.AddDays(-25), stand.VegAb);
        Assert.Equal(4, stand.WocheInPhase);
    }

    [Fact]
    public void Steckling_BewurzelungAbgeschlossenIstVegiBeginn()
    {
        var klon = new GrowRun
        {
            Name = "Klon",
            StartDate = Heute.AddDays(-20),
            StartMaterial = StartMaterial.Clone,
            Status = GrowStatus.Running,
        };

        var vorher = Phasenanker.Fuer(klon, Heute);
        Assert.Equal(Ankerphase.Anzucht, vorher.Phase);
        Assert.Equal(Anzuchtart.Bewurzelung, vorher.Anzucht);
        Assert.Equal(GrowStage.Clone, vorher.Stufe);
        Assert.Equal("confirm-rooting", vorher.Erinnerung?.Aktion);
        Assert.Equal("Bewurzelung abgeschlossen", vorher.Erinnerung?.Knopf);

        klon.RootedAt = Heute.AddDays(-9);
        klon.CloneIsRooted = true;
        var nachher = Phasenanker.Fuer(klon, Heute);
        Assert.Equal(Ankerphase.Veg, nachher.Phase);
        Assert.Equal(Heute.AddDays(-9), nachher.VegAb);
        Assert.Equal(2, nachher.WocheInPhase);
        // Vor dem Bewurzelungstag war es Bewurzelung — rückwirkend wird nichts Veg.
        Assert.Equal(GrowStage.Clone, Phasenanker.Fuer(klon, Heute.AddDays(-12)).Stufe);
    }

    [Fact]
    public void BewurzeltAngelegterStecklingIstAbStartVegi()
    {
        var klon = new GrowRun { Name = "Klon", StartDate = Heute.AddDays(-3), StartMaterial = StartMaterial.Clone, CloneIsRooted = true };

        Assert.Equal(Heute.AddDays(-3), Phasenanker.Fuer(klon, Heute).VegAb);
        Assert.Equal(Ankerphase.Veg, Phasenanker.Fuer(klon, Heute).Phase);
    }

    // ---------------- Blüte ----------------

    [Fact]
    public void EinstiegBluete_OhneFlipdatum_IstBluete_AuchMitKeimdatumAusDemFormular()
    {
        // Der Fehler aus der Ausgangslage: Samen-Grow „Einstieg Blüte" galt als Veg.
        var grow = Samen(2, g => { g.EntryPoint = GrowEntryPoint.Flower; g.GerminatedAt = g.StartDate; g.DaysAlreadyInPhase = 21; });
        var stand = Phasenanker.Fuer(grow, Heute);

        Assert.Equal(Ankerphase.Bluete, stand.Phase);
        Assert.Equal(GrowStage.Flower, stand.Stufe);
        Assert.Equal(Heute.AddDays(-23), stand.BlueteAb);
        Assert.Equal(4, stand.WocheInPhase);
    }

    [Fact]
    public void EinstiegSpuelenIstFinish()
    {
        var grow = Samen(2, g => { g.EntryPoint = GrowEntryPoint.Flush; g.GerminatedAt = g.StartDate; g.DaysAlreadyInPhase = 3; });

        Assert.Equal(Ankerphase.Finish, Phasenanker.Fuer(grow, Heute).Phase);
        Assert.Equal(Heute.AddDays(-5), Phasenanker.Fuer(grow, Heute).FinishAb);
    }

    [Fact]
    public void UebergangUndBlueteZaehlenBluetewochenAbDemFlip()
    {
        var grow = Samen(60, g => { g.VegStartedAt = Heute.AddDays(-46); g.FlipDate = Heute.AddDays(-15); });
        var stand = Phasenanker.Fuer(grow, Heute);

        Assert.Equal(Ankerphase.Bluete, stand.Phase);
        Assert.Equal(3, stand.WocheInPhase);
        Assert.Equal(3, stand.WocheIn("Flower"));
        // Die Vegi-Woche hält am Flip an: 31 Tage Vegi = Woche 5.
        Assert.Equal(5, stand.WocheIn("Veg"));
        Assert.Equal(Ankerphase.Uebergang, Phasenanker.Fuer(grow, Heute.AddDays(-10)).Phase);
    }

    [Fact]
    public void FinishAusBreederWochen_WieBisher_AberNieImUebergang()
    {
        var grow = Samen(80, g => { g.VegStartedAt = Heute.AddDays(-70); g.FlipDate = Heute.AddDays(-50); g.BreederFlowerWeeksMax = 10; });

        Assert.Equal(Heute.AddDays(-50 + 70 - 14), Phasenanker.Fuer(grow, Heute).FinishAb);
        Assert.Equal(Ankerphase.Bluete, Phasenanker.Fuer(grow, Heute).Phase);

        grow.BreederFlowerWeeksMax = 2; // unsinnig kurz: Finish nicht vor Tag 10 der Blüte
        Assert.Equal(Heute.AddDays(-40), Phasenanker.Fuer(grow, Heute).FinishAb);
    }

    [Fact]
    public void Autoflower_BlueteNurMitBestaetigung_ErinnerungAbKeimungPlus28()
    {
        var auto = Samen(40, g => { g.SeedType = SeedType.Autoflower; g.GerminatedAt = g.StartDate; g.VegStartedAt = Heute.AddDays(-25); });
        var stand = Phasenanker.Fuer(auto, Heute);

        Assert.Equal(Ankerphase.Veg, stand.Phase);
        Assert.Equal("bluete-beginn", stand.Erinnerung?.Art);
        Assert.Equal("flip-to-flower", stand.Erinnerung?.Aktion);
        Assert.Equal("Blüte beginnt", stand.Erinnerung?.Knopf);
        Assert.Equal(Heute.AddDays(-12), stand.Erinnerung?.Ab);

        auto.FlipDate = Heute.AddDays(-3);
        var bluehend = Phasenanker.Fuer(auto, Heute);
        Assert.Equal(Ankerphase.Uebergang, bluehend.Phase);
        Assert.Null(bluehend.Erinnerung);
    }

    // ---------------- Ende ----------------

    [Fact]
    public void NachDemErntetagIstEnde_DieStufeBleibtDieDesErntetags()
    {
        var grow = Samen(120, g =>
        {
            g.VegStartedAt = Heute.AddDays(-105);
            g.FlipDate = Heute.AddDays(-80);
            g.FinishStartedAt = Heute.AddDays(-25);
            g.EndDate = Heute.AddDays(-20);
            g.Status = GrowStatus.Completed;
        });
        var stand = Phasenanker.Fuer(grow, Heute);

        Assert.Equal(Ankerphase.Ende, stand.Phase);
        Assert.Equal(GrowStage.Finish, stand.Stufe);
        Assert.Null(stand.Erinnerung);
        Assert.Equal(Ankerphase.Finish, Phasenanker.Fuer(grow, Heute.AddDays(-20)).Phase);
    }

    [Fact]
    public void AbgeschlosseneLaeufeErinnernNicht()
    {
        var grow = Samen(30, g => { g.Status = GrowStatus.Aborted; g.EndDate = Heute.AddDays(-2); });

        Assert.Null(Phasenanker.Fuer(grow, Heute.AddDays(-3)).Erinnerung);
    }
}
