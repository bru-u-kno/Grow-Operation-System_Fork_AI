using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// forkai.157: Eine Anschaffung über ihre Nutzungsdauer auf die Grows verteilen.
/// </summary>
/// <remarks>
/// <para>Die Zahlen sind so gewählt, dass man sie im Kopf nachrechnet: 365 € über
/// 12 Monate ab dem 01.01.2026 sind genau 1 € je Tag (2026 hat 365 Tage).
/// „Heute" ist der 09.06.2026.</para>
///
/// <para>Die drei Regeln, die der Nutzer am 01.10.2026 bestätigt hat: Tage ohne
/// Grow verfallen als Leerlauf; parallele Grows teilen sich den Tag; bei
/// vorzeitiger Ausmusterung fällt der Rest auf die Grows dieses Tages.</para>
/// </remarks>
public sealed class AnschaffungVerteilungTests
{
    private static readonly DateTime Heute = new(2026, 6, 9);
    private static readonly DateTime JetztUtc = Mittag(2026, 6, 9);

    /// <summary>
    /// Mittag ORTSZEIT, in UTC. Die Rechnung arbeitet mit Ortstagen; ein fester
    /// UTC-Mittag fiele in Auckland schon auf den Folgetag, und die Tests wären
    /// dort rot, ohne dass die Rechnung falsch ist.
    /// </summary>
    private static DateTime Mittag(int jahr, int monat, int tag) => new DateTime(jahr, monat, tag, 12, 0, 0, DateTimeKind.Local).ToUniversalTime();

    private static Anschaffung Lampe(int monate = 12, int? zelt = null, DateTime? ausgemustert = null) => new()
    {
        Id = 1,
        Name = "LED 480 W",
        Stueck = 1,
        EinzelpreisEur = 365,
        DatumUtc = Mittag(2026, 1, 1),
        NutzungsdauerMonate = monate,
        TentId = zelt,
        AusgemustertAmUtc = ausgemustert,
    };

    /// <summary>Januar bis März, abgeschlossen: 90 Tage.</summary>
    private static GrowRun Erster(int zelt = 1) => new()
    {
        Id = 1, Name = "2026-01", TentId = zelt, Status = GrowStatus.Completed,
        StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 3, 31),
    };

    /// <summary>Seit dem 01.05., läuft: bis heute 40 Tage.</summary>
    private static GrowRun Zweiter(int zelt = 1) => new()
    {
        Id = 2, Name = "2026-02", TentId = zelt, Status = GrowStatus.Running,
        StartDate = new DateTime(2026, 5, 1),
    };

    [Fact]
    public void JederGrowTraegtSeineTageUndDiePauseVerfaellt()
    {
        var v = AnschaffungVerteilung.Berechnen(Lampe(), [Erster(), Zweiter()], Heute);

        Assert.Equal(1.0, v.EurProTag, precision: 6);
        Assert.Equal(90, v.JeGrow[1], precision: 6);   // 01.01.–31.03.
        Assert.Equal(40, v.JeGrow[2], precision: 6);   // 01.05.–09.06., heute zählt mit
        Assert.Equal(30, v.LeerlaufEur, precision: 6); // April: kein Grow
        Assert.Equal(205, v.OffenEur, precision: 6);   // 10.06.–31.12.
        Assert.Equal(365, v.VerteiltEur + v.LeerlaufEur + v.OffenEur, precision: 6);
        Assert.Equal(new DateTime(2027, 1, 1), v.BisTag);
    }

    [Fact]
    public void ParalleleGrowsTeilenSichDenTag()
    {
        var nebenan = new GrowRun
        {
            Id = 3, Name = "Zelt 2", TentId = 2, Status = GrowStatus.Running,
            StartDate = new DateTime(2026, 5, 31),
        };

        var v = AnschaffungVerteilung.Berechnen(Lampe(), [Erster(), Zweiter(), nebenan], Heute);

        // 01.05.–30.05. allein (30 €), 31.05.–09.06. zu zweit (10 Tage × 0,50 €)
        Assert.Equal(35, v.JeGrow[2], precision: 6);
        Assert.Equal(5, v.JeGrow[3], precision: 6);
        Assert.Equal(365, v.VerteiltEur + v.LeerlaufEur + v.OffenEur, precision: 6);
    }

    [Fact]
    public void MitZeltTragenNurGrowsInDiesemZelt()
    {
        var v = AnschaffungVerteilung.Berechnen(Lampe(zelt: 1), [Erster(zelt: 1), Zweiter(zelt: 2)], Heute);

        Assert.Equal(90, v.JeGrow[1], precision: 6);
        Assert.False(v.JeGrow.ContainsKey(2));
        Assert.Equal(70, v.LeerlaufEur, precision: 6); // April + 40 Tage im falschen Zelt
    }

    [Fact]
    public void VorzeitigAusgemustertFaelltDerRestAufDenLaufendenGrow()
    {
        // Kaputt am 01.06.: bis 31.05. regulär (31 Tage), der Rest 01.06.–31.12. (214 Tage) dazu.
        var v = AnschaffungVerteilung.Berechnen(Lampe(ausgemustert: Mittag(2026, 6, 1)), [Erster(), Zweiter()], Heute);

        Assert.Equal(new DateTime(2026, 6, 1), v.AusgemustertTag);
        Assert.Equal(214, v.RestwertEur, precision: 6);
        Assert.Equal(31 + 214, v.JeGrow[2], precision: 6);
        Assert.Equal(0, v.OffenEur, precision: 6);
        Assert.Equal(365, v.VerteiltEur + v.LeerlaufEur, precision: 6);
    }

    [Fact]
    public void AusgemustertOhneLaufendenGrowIstDerRestLeerlauf()
    {
        var v = AnschaffungVerteilung.Berechnen(Lampe(ausgemustert: Mittag(2026, 4, 15)), [Erster(), Zweiter()], Heute);

        Assert.Equal(90, v.JeGrow[1], precision: 6);
        Assert.False(v.JeGrow.ContainsKey(2)); // startete erst nach der Ausmusterung
        Assert.Equal(365 - 90, v.LeerlaufEur, precision: 6);
    }

    [Fact]
    public void EinAbgeschlossenerGrowAendertSichNichtMehr()
    {
        // Der Grund, warum über die Zeit verteilt wird und nicht über die Zahl
        // der Grows: ein neuer Grow darf die Zahlen eines alten nicht verschieben.
        var vorher = AnschaffungVerteilung.Berechnen(Lampe(), [Erster()], Heute);
        var nachher = AnschaffungVerteilung.Berechnen(Lampe(), [Erster(), Zweiter()], Heute);

        Assert.Equal(vorher.JeGrow[1], nachher.JeGrow[1], precision: 6);
    }

    [Fact]
    public void GeplanteGrowsUndAltbestandOhneEnddatumTragenNichts()
    {
        var geplant = new GrowRun { Id = 5, Name = "geplant", Status = GrowStatus.Planning, StartDate = new DateTime(2026, 4, 1) };
        var ohneEnde = new GrowRun { Id = 6, Name = "alt", Status = GrowStatus.Completed, StartDate = new DateTime(2026, 4, 1) };

        var v = AnschaffungVerteilung.Berechnen(Lampe(), [geplant, ohneEnde], Heute);

        Assert.Empty(v.JeGrow);
    }

    [Fact]
    public void DieKostenSeiteRechnetDenAnteilInSummeUndDurchgaenge()
    {
        var erster = Erster();
        var zweiter = Zweiter();
        var lampe = Lampe();
        var schere = new Anschaffung { Id = 2, Name = "Schere", Stueck = 1, EinzelpreisEur = 9.90, GrowId = 2, DatumUtc = JetztUtc.AddDays(-3) };

        var seite = KostenSeiteService.Berechnen(zweiter, [erster, zweiter], new StromQuelle(), null, null, [], [], [], [lampe, schere], JetztUtc);

        Assert.Equal(40 + 9.90, seite.Summe.AnschaffungenEur, precision: 2);
        Assert.Equal(40 + 9.90, seite.Durchgaenge.Single(d => d.GrowId == 2).AnschaffungenEur, precision: 2);
        Assert.Equal(90, seite.Durchgaenge.Single(d => d.GrowId == 1).AnschaffungenEur, precision: 2);

        var zeile = seite.Anschaffungen.Single(a => a.Id == 1);
        Assert.Null(zeile.GrowId);
        Assert.Equal(12, zeile.NutzungsdauerMonate);
        Assert.Equal(40, zeile.ImGrowEur, precision: 2);
        Assert.NotNull(zeile.Verteilung);
        Assert.Equal(130, zeile.Verteilung!.VerteiltEur, precision: 2);
        Assert.Equal(2, zeile.Verteilung.AnzahlGrows);
        Assert.Equal(new DateTime(2026, 12, 31), zeile.Verteilung.LetzterTag); // die Oberfläche zeigt „bis 31.12.", nicht „bis 01.01."
        Assert.Equal(DateTimeKind.Unspecified, zeile.Verteilung.LetzterTag.Kind); // ein Kalendertag, ohne „+02:00" in der API
        Assert.Equal(9.90, seite.Anschaffungen.Single(a => a.Id == 2).ImGrowEur, precision: 2);
    }

    [Fact]
    public void OhneNutzungsdauerBleibtEsBeimEinmaligenZaehlen()
    {
        var a = Lampe();
        a.NutzungsdauerMonate = null;
        a.GrowId = 1;

        Assert.False(AnschaffungVerteilung.IstVerteilt(a));
        var seite = KostenSeiteService.Berechnen(Erster(), [Erster(), Zweiter()], new StromQuelle(), null, null, [], [], [], [a], JetztUtc);
        Assert.Equal(365, seite.Summe.AnschaffungenEur, precision: 2);
        Assert.Null(seite.Anschaffungen.Single().Verteilung);
    }
}
