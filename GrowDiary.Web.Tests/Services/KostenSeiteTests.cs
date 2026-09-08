using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (forkai.6): Die Kosten-Seite rechnet Strom aus Zählerständen und
/// Verbrauchsartikel aus Nachfüllungen — beides muss ohne Datenbank prüfbar sein.
/// </summary>
/// <remarks>
/// Die Zahlen hier sind die vom Mockup (08.09.2026): 10-kg-CO₂-Flasche für
/// 34,90 €, die letzte hielt 42 Tage; Strom 0,32 €/kWh. Wer die Rechnung
/// ändert, ändert erst diese Erwartungen.
/// </remarks>
public sealed class KostenSeiteTests
{
    private static readonly DateTime Jetzt = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private static GrowRun Grow(int id = 1, DateTime? start = null, GrowStatus status = GrowStatus.Running) => new()
    {
        Id = id,
        Name = "2026-01",
        Status = status,
        StartDate = start ?? new DateTime(2026, 6, 26),
        FlipDate = new DateTime(2026, 8, 23),
        BreederFlowerWeeksMin = 9,
        BreederFlowerWeeksMax = 9,
        PlantCount = 6,
    };

    private static Zaehlerstand Stand(int id, string zeit, double kwh, string phase, int? growId = 1, ZaehlerAnlass anlass = ZaehlerAnlass.Tag) => new()
    {
        Id = id,
        ZeitpunktUtc = DateTime.SpecifyKind(DateTime.Parse(zeit), DateTimeKind.Utc),
        Kwh = kwh,
        Phase = phase,
        GrowId = growId,
        Anlass = anlass,
    };

    private static readonly StromQuelle Quelle = new() { ZaehlerEntityId = "sensor.fritz_dect_210_1_total_energy" };

    [Fact]
    public void StromIstDifferenzDerZaehlerstaendeMalPreis()
    {
        // 100 → 340 kWh in 10 Tagen: 240 kWh, 24 kWh/Tag, bei 32 ct = 76,80 €.
        var staende = new[]
        {
            Stand(1, "2026-08-29T12:00:00", 100, "Flower", anlass: ZaehlerAnlass.GrowStart),
            Stand(2, "2026-09-08T12:00:00", 340, "Flower"),
        };

        var strom = KostenSeiteService.StromBerechnen(Grow(), Quelle, 32, 1433, staende, Jetzt);

        Assert.True(strom.Eingerichtet);
        Assert.Equal(240, strom.KwhSeitStart!.Value, precision: 3);
        Assert.Equal(76.80, strom.EurSeitStart!.Value, precision: 2);
        Assert.Equal(24, strom.KwhProTag!.Value, precision: 3);
        Assert.Equal(7.68, strom.EurProTag!.Value, precision: 2);
        Assert.Equal(1433, strom.LeistungW);
        Assert.Contains("Zähler erst seit", strom.Hinweis); // Grow lief schon vor dem ersten Stand
    }

    [Fact]
    public void OhneQuelleGibtEsKeinenStromUndEinenHinweis()
    {
        var strom = KostenSeiteService.StromBerechnen(Grow(), new StromQuelle(), 32, null, [], Jetzt);

        Assert.False(strom.Eingerichtet);
        Assert.Null(strom.KwhSeitStart);
        Assert.Contains("Keine Strom-Quelle", strom.Hinweis);
    }

    [Fact]
    public void EinZaehlerResetZaehltAlsNeustartBeiNull()
    {
        // 100 → 340 → 5: nach dem Sprung nach unten zählen die 5 kWh ab null.
        var staende = new[]
        {
            Stand(1, "2026-09-01T00:00:00", 100, "Flower"),
            Stand(2, "2026-09-05T00:00:00", 340, "Flower"),
            Stand(3, "2026-09-08T00:00:00", 5, "Flower"),
        };

        Assert.Equal(245, KostenSeiteService.KwhZwischen(staende, 0, 2), precision: 3);
    }

    [Fact]
    public void PhasenWerdenAnDenPhasenwechselnGeschnitten()
    {
        var staende = new[]
        {
            Stand(1, "2026-08-01T00:00:00", 1000, "Veg", anlass: ZaehlerAnlass.GrowStart),
            Stand(2, "2026-08-11T00:00:00", 1200, "Veg"),
            Stand(3, "2026-08-23T00:00:00", 1440, "Flower", anlass: ZaehlerAnlass.Phase),
            Stand(4, "2026-09-08T00:00:00", 1760, "Flower"),
        };

        var strom = KostenSeiteService.StromBerechnen(Grow(start: new DateTime(2026, 8, 1)), Quelle, 32, null, staende, Jetzt);

        Assert.True(strom.Phasen.Count >= 2, "Mengenwächter: zwei Phasen erwartet");
        Assert.Equal(2, strom.Phasen.Count);

        var veg = strom.Phasen[0];
        Assert.Equal("Wachstum", veg.Label);
        Assert.Equal(440, veg.Kwh, precision: 3); // 1000 → 1440, bis zum Wechsel-Stand
        Assert.Equal(22, veg.Tage, precision: 3);
        Assert.False(veg.Laeuft);

        var bluete = strom.Phasen[1];
        Assert.Equal("Blüte", bluete.Label);
        Assert.Equal(320, bluete.Kwh, precision: 3);
        Assert.Equal(102.40, bluete.Eur!.Value, precision: 2);
        Assert.True(bluete.Laeuft);

        Assert.Equal(760, strom.KwhSeitStart!.Value, precision: 3);
    }

    [Fact]
    public void DieFlaschePrognostiziertAusDerLetztenLaufzeit()
    {
        var co2 = new Verbrauchsartikel { Id = 1, Name = "CO₂-Flasche 10 kg", Einheit = "kg", Gebinde = 10 };
        var vorherige = new Nachfuellung
        {
            Id = 1, ArtikelId = 1, Menge = 10, KostenEur = 34.90, GrowId = 1,
            ZeitpunktUtc = new DateTime(2026, 7, 27, 10, 0, 0, DateTimeKind.Utc),
            LeerAmUtc = new DateTime(2026, 9, 7, 10, 0, 0, DateTimeKind.Utc), // 42 Tage
        };
        var aktuelle = new Nachfuellung
        {
            Id = 2, ArtikelId = 1, Menge = 10, KostenEur = 34.90, GrowId = 1,
            ZeitpunktUtc = new DateTime(2026, 9, 7, 10, 0, 0, DateTimeKind.Utc),
        };

        var seite = KostenSeiteService.Berechnen(Grow(), [Grow()], Quelle, 32, null, [], [co2], [vorherige, aktuelle], Jetzt);

        Assert.True(seite.Artikel.Count >= 1, "Mengenwächter");
        var a = Assert.Single(seite.Artikel);
        Assert.Equal(42, a.MittlereLaufzeitTage!.Value, precision: 3);
        Assert.Equal(69.80, a.SummeEurImGrow, precision: 2);

        var f = a.Aktuell!;
        Assert.Equal(2, f.Tag);
        Assert.Equal(42, f.PrognoseTage!.Value, precision: 3);
        Assert.Equal(new DateTime(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc), f.PrognoseLeerAmUtc);
        Assert.Equal(34.90 / 42, f.EurProTag!.Value, precision: 4);
        Assert.InRange(f.FuellstandProzent!.Value, 96, 98);

        // Historie: neueste zuerst, Laufzeit nur bei der geleerten.
        Assert.Equal(2, seite.Nachfuellungen.Count);
        Assert.Equal(2, seite.Nachfuellungen[0].Id);
        Assert.Null(seite.Nachfuellungen[0].LaufzeitTage);
        Assert.Equal(42, seite.Nachfuellungen[1].LaufzeitTage!.Value, precision: 3);
        Assert.Equal(34.90 / 42, seite.Nachfuellungen[1].EurProTag!.Value, precision: 4);
    }

    [Fact]
    public void EinFehlgriffUnterEinemTagVerdirbtDiePrognoseNicht()
    {
        var co2 = new Verbrauchsartikel { Id = 1, Name = "CO₂", Einheit = "kg" };
        var t0 = new DateTime(2026, 9, 7, 10, 0, 0, DateTimeKind.Utc);
        var echte = new Nachfuellung { Id = 1, ArtikelId = 1, Menge = 10, GrowId = 1, ZeitpunktUtc = t0.AddDays(-42), LeerAmUtc = t0 };
        var fehlgriff = new Nachfuellung { Id = 2, ArtikelId = 1, Menge = 10, GrowId = 1, ZeitpunktUtc = t0, LeerAmUtc = t0.AddHours(2) };
        var laufend = new Nachfuellung { Id = 3, ArtikelId = 1, Menge = 10, GrowId = 1, ZeitpunktUtc = t0.AddHours(2) };

        var seite = KostenSeiteService.Berechnen(Grow(), [Grow()], Quelle, 32, null, [], [co2], [echte, fehlgriff, laufend], Jetzt);

        var a = Assert.Single(seite.Artikel);
        Assert.Equal(42, a.MittlereLaufzeitTage!.Value, precision: 3);
        Assert.Equal(42, a.Aktuell!.PrognoseTage!.Value, precision: 3);
    }

    [Fact]
    public void OhneAbgeschlosseneFuellungGibtEsKeinePrognose()
    {
        var co2 = new Verbrauchsartikel { Id = 1, Name = "CO₂-Flasche 10 kg", Einheit = "kg" };
        var erste = new Nachfuellung { Id = 1, ArtikelId = 1, Menge = 10, KostenEur = 34.90, GrowId = 1, ZeitpunktUtc = new DateTime(2026, 9, 7, 10, 0, 0, DateTimeKind.Utc) };

        var seite = KostenSeiteService.Berechnen(Grow(), [Grow()], Quelle, 32, null, [], [co2], [erste], Jetzt);

        var f = Assert.Single(seite.Artikel).Aktuell!;
        Assert.Null(f.PrognoseTage);
        Assert.Null(f.PrognoseLeerAmUtc);
        Assert.Null(f.FuellstandProzent);
        Assert.Null(f.EurProTag);
    }

    [Fact]
    public void DieSummeIstStromPlusArtikelUndGehtDurchPflanzenUndTage()
    {
        var grow = Grow(start: new DateTime(2026, 8, 30)); // Tag 10 am 08.09.
        var staende = new[]
        {
            Stand(1, "2026-08-30T00:00:00", 100, "Flower", anlass: ZaehlerAnlass.GrowStart),
            Stand(2, "2026-09-08T00:00:00", 340, "Flower"),
        };
        var co2 = new Verbrauchsartikel { Id = 1, Name = "CO₂", Einheit = "kg" };
        var fuellung = new Nachfuellung { Id = 1, ArtikelId = 1, Menge = 10, KostenEur = 34.90, GrowId = 1, ZeitpunktUtc = new DateTime(2026, 9, 7, 10, 0, 0, DateTimeKind.Utc) };

        var seite = KostenSeiteService.Berechnen(grow, [grow], Quelle, 32, null, staende, [co2], [fuellung], Jetzt);

        Assert.Equal(10, seite.Grow!.Tag);
        Assert.Equal(76.80, seite.Summe.StromEur!.Value, precision: 2);
        Assert.Equal(34.90, seite.Summe.ArtikelEur, precision: 2);
        Assert.Equal(111.70, seite.Summe.GesamtEur, precision: 2);
        Assert.Equal(11.17, seite.Summe.ProTagEur!.Value, precision: 2);
        Assert.Equal(111.70 / 6, seite.Summe.ProPflanzeEur!.Value, precision: 2);
        Assert.NotNull(seite.Summe.PrognoseErnteEur); // Flip + 9 Wochen liegt in der Zukunft
        Assert.True(seite.Summe.PrognoseErnteEur > seite.Summe.GesamtEur);

        var d = Assert.Single(seite.Durchgaenge);
        Assert.Equal(111.70, d.GesamtEur!.Value, precision: 2);
        Assert.True(d.Laeuft);
    }

    [Fact]
    public void FuellungenEinesAnderenGrowsZaehlenNichtInDieSumme()
    {
        var grow = Grow();
        var alt = Grow(id: 9, start: new DateTime(2026, 1, 1), status: GrowStatus.Completed);
        alt.EndDate = new DateTime(2026, 4, 1);
        var co2 = new Verbrauchsartikel { Id = 1, Name = "CO₂", Einheit = "kg" };
        var alteFuellung = new Nachfuellung { Id = 1, ArtikelId = 1, Menge = 10, KostenEur = 30, GrowId = 9, ZeitpunktUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc) };

        var seite = KostenSeiteService.Berechnen(grow, [grow, alt], Quelle, 32, null, [], [co2], [alteFuellung], Jetzt);

        Assert.Equal(0, seite.Summe.ArtikelEur);
        Assert.Equal(2, seite.Durchgaenge.Count);
        Assert.Equal(30, seite.Durchgaenge.Single(d => d.GrowId == 9).ArtikelEur);
    }

    [Theory]
    [InlineData(null, null, 1, "Flower", ZaehlerAnlass.Manuell)]     // noch nie festgehalten
    [InlineData(1, "Flower", 2, "Flower", ZaehlerAnlass.GrowStart)] // anderer Grow läuft
    [InlineData(1, "Veg", 1, "Flower", ZaehlerAnlass.Phase)]       // Phase gewechselt
    [InlineData(1, "Flower", 1, "Flower", ZaehlerAnlass.Tag)]      // neuer Tag
    public void DerWorkerWeissWarumEinStandFaelligIst(int? letzterGrow, string? letztePhase, int growId, string phase, ZaehlerAnlass erwartet)
    {
        var jetzt = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
        Zaehlerstand? letzter = letzterGrow is null
            ? null
            : new Zaehlerstand { GrowId = letzterGrow, Phase = letztePhase, ZeitpunktUtc = jetzt.AddDays(-1) };

        Assert.Equal(erwartet, ZaehlerstandWorker.Entscheiden(letzter, growId, phase, jetzt));
    }

    [Fact]
    public void AmSelbenTagOhneWechselIstNichtsFaellig()
    {
        var jetzt = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
        var letzter = new Zaehlerstand { GrowId = 1, Phase = "Flower", ZeitpunktUtc = jetzt.AddHours(-2) };

        Assert.Null(ZaehlerstandWorker.Entscheiden(letzter, 1, "Flower", jetzt));
    }
}
