using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (forkai.20): Die CO₂-Steuerung rechnet ohne Datenbank und ohne
/// Home Assistant prüfbar — Planstaffel, Grenzen der Eingaben, Buchung in
/// Gramm oder Kilogramm, und was der Tagestext sagt.
/// </summary>
/// <remarks>
/// Die Zahlen sind die vom Entwurf (11.09.2026): Planziel der Phase = 1200 ppm,
/// Staffel 55 / 70 / 80 % → 660 / 840 / 960 ppm.
/// </remarks>
public sealed class Co2SteuerungTests
{
    [Fact]
    public void PlanstaffelSkaliertDasPhasenzielJeCanopyBereich()
    {
        var e = new Co2Einstellungen { ZielQuelle = "plan", AnteilWarmProzent = 80, AnteilMittelProzent = 70, AnteilKuehlProzent = 55 };

        var (warm, mittel, kuehl) = Co2SteuerungService.WirksameZiele(e, 1200);

        Assert.Equal(960, warm);
        Assert.Equal(840, mittel);
        Assert.Equal(660, kuehl);
    }

    [Fact]
    public void PlanstaffelRundetAufZehnerUndFaelltOhnePlanAufFestZurueck()
    {
        var e = new Co2Einstellungen { ZielQuelle = "plan", AnteilWarmProzent = 77, ZielWarmPpm = 920, ZielMittelPpm = 820, ZielKuehlPpm = 650 };

        Assert.Equal(920, Co2SteuerungService.WirksameZiele(e, 1195).Warm); // 920,15 → 920
        // Kein laufender Grow, kein Band: die festen Werte gelten, damit HA nie ohne Ziel steht.
        Assert.Equal((920, 820, 650), Co2SteuerungService.WirksameZiele(e, null));
    }

    [Fact]
    public void FestNimmtDieDreiEingetragenenWerte()
    {
        var e = new Co2Einstellungen { ZielQuelle = "fest", ZielWarmPpm = 1000, ZielMittelPpm = 850, ZielKuehlPpm = 700 };
        Assert.Equal((1000, 850, 700), Co2SteuerungService.WirksameZiele(e, 1400));
    }

    [Fact]
    public void PruefungLehntWasDerWaechterNichtDecktAb()
    {
        var fehler = Co2SteuerungService.Pruefen(new Co2Einstellungen { ImpulsMaxSekunden = 60 });
        Assert.Contains(nameof(Co2Einstellungen.ImpulsMaxSekunden), fehler.Keys);

        fehler = Co2SteuerungService.Pruefen(new Co2Einstellungen { ImpulsMinSekunden = 20, ImpulsMaxSekunden = 15 });
        Assert.Contains(nameof(Co2Einstellungen.ImpulsMinSekunden), fehler.Keys);

        fehler = Co2SteuerungService.Pruefen(new Co2Einstellungen { T6StufeTief = 6, T6StufeDosierung = 5 });
        Assert.Contains(nameof(Co2Einstellungen.T6StufeTief), fehler.Keys);

        fehler = Co2SteuerungService.Pruefen(new Co2Einstellungen { ZielQuelle = "wochenplan" });
        Assert.Contains(nameof(Co2Einstellungen.ZielQuelle), fehler.Keys);
    }

    [Fact]
    public void DieVorgabenSelbstBestehenDiePruefung()
    {
        Assert.Empty(Co2SteuerungService.Pruefen(new Co2Einstellungen()));
    }

    [Fact]
    public void BuchungLandetInDerEinheitDesArtikels()
    {
        Assert.Equal(0.1523, Co2SteuerungService.InEinheit(152.3, "kg"));
        Assert.Equal(152.3, Co2SteuerungService.InEinheit(152.3, "g"));
    }

    [Fact]
    public void TagestextNenntImpulseVentilzeitGrammUndZielzeit()
    {
        var tag = new Co2Tag { Impulse = 96, VentilSekunden = 2460, Gramm = 150.4, ZielErreichtUm = "05:41" };
        var text = Co2SteuerungService.Tagestext(tag);
        Assert.Contains("96 Impulse", text);
        Assert.Contains("41 min", text);
        Assert.Contains("150 g", text);
        Assert.Contains("05:41 Uhr", text);

        var ohne = Co2SteuerungService.Tagestext(new Co2Tag { Impulse = 3 });
        Assert.Contains("nicht erreicht", ohne);
    }

    [Fact]
    public void GebuchterVerbrauchSchlaegtDieSchaetzungDerKostenSeite()
    {
        // Ohne Buchung: keine abgeschlossene Füllung → keine Prognose, kein Füllstand.
        var co2 = new Verbrauchsartikel { Id = 1, Name = "CO₂-Flasche 10 kg", Einheit = "kg" };
        var fuellung = new Nachfuellung { Id = 1, ArtikelId = 1, Menge = 10, KostenEur = 36.75, GrowId = 1, ZeitpunktUtc = new DateTime(2026, 9, 7, 19, 0, 0, DateTimeKind.Utc) };
        var jetzt = new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);
        var grow = new GrowRun { Id = 1, Name = "2026-01", Status = GrowStatus.Running, StartDate = new DateTime(2026, 6, 26), FlipDate = new DateTime(2026, 8, 23), PlantCount = 6, BreederFlowerWeeksMin = 9, BreederFlowerWeeksMax = 9 };

        var ohne = KostenSeiteService.Berechnen(grow, [grow], new StromQuelle(), 32, null, [], [co2], [fuellung], [], jetzt);
        Assert.Null(Assert.Single(ohne.Artikel).Aktuell!.FuellstandProzent);

        // Mit vier Abendbuchungen der Steuerung: 0,4 kg von 10 kg weg → 96 %, gemessen, Prognose aus dem Tempo.
        var buchungen = Enumerable.Range(0, 4).Select(i => new Verbrauch
        {
            Id = i + 1, ArtikelId = 1, GrowId = 1, Menge = 0.1, Quelle = "co2-steuerung",
            ZeitpunktUtc = new DateTime(2026, 9, 8 + i, 17, 5, 0, DateTimeKind.Utc),
        }).ToList();

        var mit = KostenSeiteService.Berechnen(grow, [grow], new StromQuelle(), 32, null, [], [co2], [fuellung], [], jetzt, null, buchungen);
        var f = Assert.Single(mit.Artikel).Aktuell!;
        Assert.Equal(96, f.FuellstandProzent!.Value, 0);
        Assert.Equal("gemessen", f.FuellstandQuelle);
        Assert.Equal(0.4, f.VerbrauchtMenge, 3);
        Assert.NotNull(f.PrognoseTage);
        Assert.InRange(f.PrognoseTage!.Value, 80, 100); // 0,4 kg in ~3,7 Tagen → ~92 Tage für 10 kg
        Assert.NotNull(f.EurProTag);
    }

    [Fact]
    public void EinFlaschenwechselKostetNichtDenGanzenTag()
    {
        // Vormittag an einer fast vollen Flasche: 200 g durchs Ventil.
        var tag = new Co2Tag { FlascheStartKg = 9.5 };
        Co2SteuerungService.Fortschreiben(tag, 40, 9.3, 0.05);
        Assert.Equal(200, tag.Gramm, 1);

        // Mittags neue Flasche — der Helfer springt auf 10,0 zurueck.
        Co2SteuerungService.Fortschreiben(tag, 41, 10.0, 0.05);
        Assert.True(tag.Flaschenwechsel);
        Assert.Equal(200, tag.Gramm, 1);

        // Danach wird ab dem neuen Stand weitergezaehlt, nicht bei null.
        Co2SteuerungService.Fortschreiben(tag, 70, 9.85, 0.05);
        Assert.Equal(350, tag.Gramm, 1);
        Assert.Contains("Flasche gewechselt", Co2SteuerungService.Tagestext(tag));
    }

    [Fact]
    public void OhneWechselBleibtDieZaehlungEinfach()
    {
        var tag = new Co2Tag { FlascheStartKg = 9.2 };
        Co2SteuerungService.Fortschreiben(tag, 10, 9.15, 0.05);
        Assert.False(tag.Flaschenwechsel);
        Assert.Equal(50, tag.Gramm, 1);
        // Ventilzeit ist die Ruecktrechnung aus den Gramm, keine zweite Messung.
        Assert.Equal(1000, tag.VentilSekunden, 0);
    }

    [Fact]
    public void EineHandkorrekturNachObenGiltNichtAlsWechsel()
    {
        // 20 g Unterschied: Rundung oder eine Korrektur von Hand, kein Wechsel.
        var tag = new Co2Tag { FlascheStartKg = 9.2, Gramm = 100, GrammVorher = 100 };
        Co2SteuerungService.Fortschreiben(tag, 5, 9.22, 0.05);
        Assert.False(tag.Flaschenwechsel);
    }

    [Fact]
    public void DieEndzeitWirdGeprueftWieDieStartzeit()
    {
        Assert.Empty(Co2SteuerungService.Pruefen(new Co2Einstellungen { EndeVorLichtAusMinuten = 0 }));
        Assert.Empty(Co2SteuerungService.Pruefen(new Co2Einstellungen { EndeVorLichtAusMinuten = 240 }));
        Assert.Contains(nameof(Co2Einstellungen.EndeVorLichtAusMinuten),
            Co2SteuerungService.Pruefen(new Co2Einstellungen { EndeVorLichtAusMinuten = 241 }));
    }
    /// <summary>Ein Livebild mit nur den Feldern, die der Tageslauf liest.</summary>
    private static Co2Live Live(double restKg, int impulse, double gps = 0.05) => new(
        HaErreichbar: true, Co2Ppm: 900, ZielPpm: 960, ZielQuelle: "plan", PlanPpm: 1200, PlanHerkunft: null,
        ZielWarm: 960, ZielMittel: 840, ZielKuehl: 660, HysteresePpm: 50, NachschubUnterPpm: 910,
        Bedarf: false, KlimaOk: true, VentilOffen: false, AutomatikAn: true, LichtAn: true,
        T6Stufe: 4, CanopyC: 27.6, RhProzent: 61, Vpd: 1.28, ImpulseHeute: impulse,
        GrammProSekunde: gps, LetzteMessungGps: gps, FlascheRestKg: restKg,
        ImpulsBedarfSekunden: 15, LetzterImpuls: null);

    [Fact]
    public void VerbrauchIstDerFallDesFlaschenrests()
    {
        var tag = new Co2Tag { FlascheStartKg = 9.6 };
        Co2SteuerungService.Zwischenstand(tag, Live(restKg: 9.45, impulse: 40));

        Assert.Equal(150, tag.Gramm, 3);
        Assert.Equal(40, tag.Impulse);
        Assert.False(tag.Flaschenwechsel);
    }

    [Fact]
    public void FlaschenwechselAmTagVerliertDasBisDahinGezaehlteNicht()
    {
        // Vormittag: 9,60 -> 9,45 kg, also 150 g.
        var tag = new Co2Tag { FlascheStartKg = 9.6 };
        Co2SteuerungService.Zwischenstand(tag, Live(restKg: 9.45, impulse: 40));

        // Mittags neue Flasche: der Helfer springt auf 10,0 kg.
        Co2SteuerungService.Zwischenstand(tag, Live(restKg: 10.0, impulse: 41));
        Assert.True(tag.Flaschenwechsel);
        Assert.Equal(150, tag.Gramm, 3);

        // Nachmittag: 10,00 -> 9,90 kg, also 100 g dazu.
        Co2SteuerungService.Zwischenstand(tag, Live(restKg: 9.9, impulse: 70));
        Assert.Equal(250, tag.Gramm, 3);
        Assert.Contains("Flasche gewechselt", Co2SteuerungService.Tagestext(tag));
    }

    [Fact]
    public void KleineKorrekturVonHandGiltNichtAlsFlaschenwechsel()
    {
        // 20 g nach oben - Rundung oder eine Korrektur, kein Wechsel. Der
        // Abschnitt zaehlt dann null statt einen Sprung zu buchen.
        var tag = new Co2Tag { FlascheStartKg = 9.6 };
        Co2SteuerungService.Zwischenstand(tag, Live(restKg: 9.62, impulse: 5));

        Assert.False(tag.Flaschenwechsel);
        Assert.Equal(0, tag.Gramm, 3);
    }

    [Fact]
    public void DerZaehlerFaelltNichtZurueck()
    {
        // Nach einem HA-Neustart steht der Tageszaehler kurz auf 0 - das darf
        // den Tagesstand nicht loeschen.
        var tag = new Co2Tag { FlascheStartKg = 9.6, Impulse = 40 };
        Co2SteuerungService.Zwischenstand(tag, Live(restKg: 9.45, impulse: 0));
        Assert.Equal(40, tag.Impulse);
    }

    [Fact]
    public void EndeVorLichtAusWirdGeprueft()
    {
        Assert.Contains(nameof(Co2Einstellungen.EndeVorLichtAusMinuten),
            Co2SteuerungService.Pruefen(new Co2Einstellungen { EndeVorLichtAusMinuten = 300 }).Keys);
        Assert.DoesNotContain(nameof(Co2Einstellungen.EndeVorLichtAusMinuten),
            Co2SteuerungService.Pruefen(new Co2Einstellungen { EndeVorLichtAusMinuten = 30 }).Keys);
    }

}
