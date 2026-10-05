using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services.Tagebuch;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Der Tagesstrom des Grow-Tagebuchs (A-006) gegen eine echte Datenbank.
/// </summary>
/// <remarks>
/// Die Tests laufen in Europe/Berlin (<c>zeitzone.runsettings</c>) — in UTC wären
/// Ortstag und UTC-Tag dasselbe, und jeder Fehler, der beide verwechselt, bliebe
/// unsichtbar. Genau dieser Fehler ist hier schon mehrfach passiert.
/// </remarks>
public sealed class TagebuchServiceTests : IDisposable
{
    private static readonly DateTime Jetzt = new(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc);

    private readonly string _temp;
    private readonly AppPaths _paths;
    private readonly GrowRepository _grows;
    private readonly JournalRepository _journal;
    private readonly MeasurementRepository _messungen;
    private readonly AddbackRepository _addback;
    private readonly SensorReadingRepository _rohwerte;
    private readonly HardwareRepository _hardware;
    private readonly LightRepository _licht;
    private readonly TagebuchService _dienst;
    private readonly int _zelt;
    private readonly int _grow;

    public TagebuchServiceTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "Tagebuch_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);
        _paths = new AppPaths(_temp);
        TestDatabase.Initialize(_paths);
        _grows = new GrowRepository(_paths);
        _journal = new JournalRepository(_paths);
        _messungen = new MeasurementRepository(_paths);
        _addback = new AddbackRepository(_paths);
        _rohwerte = new SensorReadingRepository(_paths);
        _hardware = new HardwareRepository(_paths);
        _licht = new LightRepository(_paths);
        _dienst = new TagebuchService(_grows, _journal, _rohwerte, new TagebuchRepository(_paths), _licht,
            new DosingRepository(_paths), new KostenRepository(_paths), _hardware);

        _zelt = _grows.CreateTent(new Tent { Name = "Zelt-RDWC", TentType = TentType.Production }).Id;
        _grow = _grows.CreateGrow(new GrowRun
        {
            Name = "2026-01",
            TentId = _zelt,
            StartDate = new DateTime(2026, 8, 1),
            FlipDate = new DateTime(2026, 8, 20),
            Status = GrowStatus.Running,
        });
    }

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); } catch { /* Windows haelt manchmal fest */ }
    }

    private void BrusRohwerteEinspielen()
    {
        foreach (var w in SprungerkennungTests.BrusNachfuellen())
        {
            _rohwerte.AddReading(new TentSensorReading { TentId = _zelt, MetricKey = "reservoir-ec", Value = w.Wert, Unit = "mS/cm", CapturedAtUtc = w.Utc });
        }
    }

    private TagebuchSeiteDtoAnsicht Seite(DateOnly? bis = null, int tage = 7)
        => new(_dienst.Seite(_grow, bis, tage, Jetzt)!);

    private sealed record TagebuchSeiteDtoAnsicht(GrowDiary.Web.Api.Contracts.TagebuchSeiteDto Dto)
    {
        public IEnumerable<GrowDiary.Web.Api.Contracts.TagebuchEreignisDto> Alle => Dto.Tage.SelectMany(t => t.Ereignisse);
        public IEnumerable<GrowDiary.Web.Api.Contracts.TagebuchEreignisDto> Auffaellig => Alle.Where(e => e.Art == "auffaellig");
    }

    [Fact]
    public void Ortszeit_EntscheidetDenTag_NichtUtc()
    {
        // 00:29 Ortszeit am 03.10. ist 22:29 UTC am 02.10. — gehört zum 03.10.
        _messungen.CreateMeasurement(new Measurement { GrowId = _grow, TakenAt = new DateTime(2026, 10, 3, 0, 29, 0), Source = ValueOrigin.Manual, ReservoirPh = 6.22 });
        // 22:30 UTC am 03.10. ist 00:30 Ortszeit am 04.10.
        _addback.CreateChangeout(new ChangeoutEntry { GrowId = _grow, PerformedAtUtc = new DateTime(2026, 10, 3, 22, 30, 0, DateTimeKind.Utc), Kind = ChangeoutKind.Full });
        // 21:59 UTC am 03.10. ist 23:59 Ortszeit — noch der 03.10.
        _journal.Create(new JournalEntry { GrowId = _grow, Title = "Spät", EntryType = JournalEntryType.Note, OccurredAtUtc = new DateTime(2026, 10, 3, 21, 59, 0, DateTimeKind.Utc) });

        var seite = Seite().Dto;

        Assert.Equal(["2026-10-04", "2026-10-03"], seite.Tage.Select(t => t.Datum).ToArray());
        var vierter = seite.Tage[0];
        Assert.Equal("Sonntag", vierter.Wochentag);
        Assert.Equal("00:30", Assert.Single(vierter.Ereignisse).Uhrzeit);
        var dritter = seite.Tage[1];
        Assert.Equal(["23:59", "00:29"], dritter.Ereignisse.Select(e => e.Uhrzeit).ToArray());
        Assert.Equal(29, dritter.Ereignisse[1].Minute);
        Assert.Equal("Samstag", dritter.Wochentag);
    }

    [Fact]
    public void Tage_NeuesteZuerst_UndJeTagDieSpaetesteZeileOben()
    {
        foreach (var (tag, stunde) in new[] { (2, 9), (4, 7), (4, 21), (3, 12), (4, 13) })
        {
            _messungen.CreateMeasurement(new Measurement { GrowId = _grow, TakenAt = new DateTime(2026, 10, tag, stunde, 0, 0), Source = ValueOrigin.HomeAssistant, ReservoirEc = 1.6, Notes = "AutoMeasurement LightOnDelay" });
        }

        var seite = Seite().Dto;
        Assert.Equal(["2026-10-04", "2026-10-03", "2026-10-02"], seite.Tage.Select(t => t.Datum).ToArray());
        Assert.Equal(["21:00", "13:00", "07:00"], seite.Tage[0].Ereignisse.Select(e => e.Uhrzeit).ToArray());
        Assert.All(seite.Tage[0].Ereignisse, e => Assert.Equal("Licht an", e.Titel));
        Assert.All(seite.Tage[0].Ereignisse, e => Assert.Equal("sensor", e.Messung!.Herkunft));
        // Flip am 20.08. — der 04.10. ist Tag 46 der Blüte, Woche 7.
        Assert.Equal("Blüte · Woche 7 · Tag 46", seite.Tage[0].Phase);
    }

    [Fact]
    public void Seitenweise_SiebenTageMitEintraegen_DannDieAelteren()
    {
        for (var tag = 1; tag <= 10; tag++)
        {
            _messungen.CreateMeasurement(new Measurement { GrowId = _grow, TakenAt = new DateTime(2026, 9, 20 + tag, 12, 0, 0), Source = ValueOrigin.Manual, ReservoirEc = 1.5 });
        }

        var erste = Seite().Dto;
        Assert.Equal(7, erste.Tage.Count);
        Assert.Equal("2026-09-30", erste.Tage[0].Datum);
        Assert.Equal("2026-09-23", erste.AeltereAb);

        var zweite = Seite(DateOnly.Parse(erste.AeltereAb!)).Dto;
        Assert.Equal(["2026-09-23", "2026-09-22", "2026-09-21"], zweite.Tage.Select(t => t.Datum).ToArray());
        Assert.Null(zweite.AeltereAb);
    }

    [Fact]
    public void BrusNachfuellen_OhneEintrag_StehtAlsAuffaellig_MitOrtszeit()
    {
        BrusRohwerteEinspielen();

        var zeile = Assert.Single(Seite().Auffaellig);
        Assert.Equal("16:10", zeile.Uhrzeit);
        Assert.Equal("EC fiel in 25 Minuten um 0,14", zeile.Titel);
        var befund = Assert.Single(zeile.Auffaellig!.Befunde);
        Assert.Equal("16:35", befund.EndeUhrzeit);
        Assert.Contains("5 %", befund.Regel);
        Assert.Equal(1, Seite().Dto.Tage.Single(t => t.Datum == "2026-10-03").Auffaellig);
    }

    [Fact]
    public void EingetragenesNachfuellen_UnterdruecktDieMeldung()
    {
        BrusRohwerteEinspielen();
        Assert.Single(Seite().Auffaellig);

        // Bru trägt nach: 16:45 Ortszeit nachgefüllt.
        _addback.CreateAddbackLog(new AddbackLogEntry { GrowId = _grow, Kind = AddbackLogKind.TopOff, PerformedAtUtc = new DateTime(2026, 10, 3, 14, 45, 0, DateTimeKind.Utc), LitersAdded = 12 });

        var seite = Seite();
        Assert.Empty(seite.Auffaellig);
        Assert.Contains(seite.Alle, e => e.Art == "addback" && e.Titel == "Nachfüllen 12 L");
    }

    [Fact]
    public void EintragAusserhalbDesFensters_ErklaertNichts()
    {
        BrusRohwerteEinspielen();
        // Vier Stunden nach dem Sprung — das ist der Abend danach, nicht das Nachfüllen.
        _addback.CreateAddbackLog(new AddbackLogEntry { GrowId = _grow, Kind = AddbackLogKind.TopOff, PerformedAtUtc = new DateTime(2026, 10, 3, 18, 40, 0, DateTimeKind.Utc) });
        Assert.Single(Seite().Auffaellig);
    }

    [Fact]
    public void AutomatischerJournaleintrag_ErklaertKeinenSprung()
    {
        BrusRohwerteEinspielen();
        // Die CO₂-Tagesbilanz schreibt die Steuerung — sie sagt nichts über das Becken.
        _journal.Create(new JournalEntry { GrowId = _grow, Title = "CO₂-Begasung", EntryType = JournalEntryType.Action, Source = ValueOrigin.HomeAssistant, OccurredAtUtc = new DateTime(2026, 10, 3, 15, 0, 0, DateTimeKind.Utc) });
        Assert.Single(Seite().Auffaellig);

        _journal.Create(new JournalEntry { GrowId = _grow, Title = "Nachgefüllt", EntryType = JournalEntryType.Note, Source = ValueOrigin.Manual, OccurredAtUtc = new DateTime(2026, 10, 3, 15, 0, 0, DateTimeKind.Utc) });
        Assert.Empty(Seite().Auffaellig);
    }

    [Fact]
    public void Dosis_ErklaertNurDenSprungDenSieBewirkenKann()
    {
        BrusRohwerteEinspielen();
        var dosierung = new DosingRepository(_paths);
        var phMinus = dosierung.InsertPump(new DosingPump { TentId = _zelt, Name = "pH Minus", Purpose = DosingPurpose.PhDown, HaEntityId = "switch.ph_minus", MlPerMinute = 45 });
        dosierung.InsertEvent(new DoseEvent { PumpId = phMinus, TentId = _zelt, GrowId = _grow, OccurredAtUtc = new DateTime(2026, 10, 3, 14, 20, 0, DateTimeKind.Utc), Trigger = DoseTrigger.Manual, Outcome = DoseOutcome.Done, RequestedMl = 2, DosedMl = 2 });

        // Säure senkt den pH — einen EC-Fall erklärt sie nicht.
        var seite = Seite();
        Assert.Single(seite.Auffaellig);
        Assert.Contains(seite.Alle, e => e.Art == "dosierung" && e.Dosis!.Pumpe == "pH Minus");

        // Eine Pumpe mit eigenem Mittel: was sie bewirkt, weiß niemand — sie erklärt.
        var eigen = dosierung.InsertPump(new DosingPump { TentId = _zelt, Name = "Eigenes", Purpose = DosingPurpose.Custom, HaEntityId = "switch.eigen", MlPerMinute = 45 });
        dosierung.InsertEvent(new DoseEvent { PumpId = eigen, TentId = _zelt, GrowId = _grow, OccurredAtUtc = new DateTime(2026, 10, 3, 14, 20, 0, DateTimeKind.Utc), Trigger = DoseTrigger.Manual, Outcome = DoseOutcome.Done, RequestedMl = 5, DosedMl = 5 });
        Assert.Empty(Seite().Auffaellig);
    }

    [Fact]
    public void Verbrauchsbuchung_OhneMessung_ErklaertNichts()
    {
        BrusRohwerteEinspielen();
        var kosten = new KostenRepository(_paths);
        var artikel = kosten.CreateArtikel(new Verbrauchsartikel { Name = "CO₂", Einheit = "kg" });
        kosten.CreateVerbrauch(new Verbrauch { ArtikelId = artikel, GrowId = _grow, ZeitpunktUtc = new DateTime(2026, 10, 3, 14, 30, 0, DateTimeKind.Utc), Menge = 1.2, Quelle = "manuell" });

        var seite = Seite();
        Assert.Single(seite.Auffaellig);
        var buchung = Assert.Single(seite.Alle, e => e.Art == "verbrauch");
        Assert.Equal("CO₂", Assert.Single(buchung.Posten).Name);
    }

    [Fact]
    public void KalibrierungDerSonde_IstKeinSprung()
    {
        BrusRohwerteEinspielen();
        var sonde = _hardware.CreateHardwareItem(new HardwareItem { Name = "EC-Sonde", Category = "Sensor", TentId = _zelt });
        _hardware.CreateCalibrationEvent(new CalibrationEvent
        {
            HardwareItemId = sonde.Id,
            CalibrationType = CalibrationEventType.Ec,
            Status = CalibrationEventStatus.Completed,
            Title = "EC kalibriert",
            PerformedAtUtc = new DateTime(2026, 10, 3, 14, 20, 0, DateTimeKind.Utc),
        });

        Assert.Empty(Seite().Auffaellig);
    }

    [Fact]
    public void WarNichts_BleibtGemerkt_UndLaesstSichZuruecknehmen()
    {
        BrusRohwerteEinspielen();
        var id = Assert.Single(Seite().Auffaellig).Auffaellig!.Befunde[0].Id;

        Assert.True(_dienst.Verwerfen(id, true, Jetzt));
        Assert.Empty(Seite().Auffaellig);
        // Neu erkennen legt ihn nicht wieder an — derselbe Beginn ist schon gemerkt.
        Assert.Equal(0, _dienst.Erkennen(_zelt, Jetzt.AddDays(-8), Jetzt));
        Assert.Empty(Seite().Auffaellig);

        Assert.True(_dienst.Verwerfen(id, false, Jetzt));
        Assert.Single(Seite().Auffaellig);
    }

    [Fact]
    public void Befund_UeberlebtDasAufraeumenDerRohwerte()
    {
        BrusRohwerteEinspielen();
        Assert.Single(Seite().Auffaellig);

        // Was der nächtliche Lauf nach sieben Tagen tut: alle Rohwerte weg.
        _rohwerte.DeleteOlderThan(DateTime.UtcNow.AddYears(1));
        Assert.Empty(_rohwerte.GetReadings(_zelt, "reservoir-ec", DateTime.MinValue, DateTime.MaxValue));

        var zeile = Assert.Single(Seite().Auffaellig);
        Assert.Equal("EC fiel in 25 Minuten um 0,14", zeile.Titel);
    }

    [Fact]
    public void Wasserwechsel_MessungUndJournal_SindEinVorgang()
    {
        // Brus Wechsel am 04.10.: Changeout, Messung mit Haken und Journal — alle 17:30.
        var um = new DateTime(2026, 10, 4, 15, 30, 0, DateTimeKind.Utc);
        _addback.CreateChangeout(new ChangeoutEntry { GrowId = _grow, PerformedAtUtc = um, Kind = ChangeoutKind.Full, VolumeChangedLiters = 160, EcBefore = 1.62, PhBefore = 6.15, EcAfter = 1.15, PhAfter = 6.15, WaterUsed = WaterSource.Tap });
        var messung = _messungen.CreateMeasurement(new Measurement { GrowId = _grow, TakenAt = new DateTime(2026, 10, 4, 17, 30, 0), Source = ValueOrigin.Manual, SolutionChange = true, ReservoirEc = 1.15, ReservoirPh = 6.15, ReservoirWaterTempC = 18.1, OrpMv = 450 });
        _journal.Create(new JournalEntry { GrowId = _grow, Title = "Wasserwechsel RDWC 160 L", EntryType = JournalEntryType.ReservoirChange, OccurredAtUtc = um });

        var alle = Seite().Alle.ToList();
        var wechsel = Assert.Single(alle);
        Assert.Equal("wechsel", wechsel.Art);
        Assert.Equal("Wasserwechsel 160 L", wechsel.Titel);
        Assert.Equal("17:30", wechsel.Uhrzeit);
        Assert.Equal(messung, wechsel.Wechsel!.MessungId);
        Assert.Equal(1.62, wechsel.Wechsel.Vorher.Ec);
        Assert.Equal(18.1, wechsel.Wechsel.Nachher.WasserC);
        Assert.Equal(450, wechsel.Wechsel.Nachher.OrpMv);
        Assert.Equal("Wasserwechsel RDWC 160 L", wechsel.Wechsel.Journal!.Titel);
        Assert.Equal("Tap", wechsel.Wechsel.Wasser);
        Assert.True(wechsel.Wasser);
    }

    [Fact]
    public void Handmessung_WirdMitDemSensorAbgeglichen()
    {
        var um = new DateTime(2026, 10, 2, 22, 29, 0, DateTimeKind.Utc);
        foreach (var (minute, ecWert, phWert) in new[] { (-5, 1.72, 6.21), (0, 1.72, 6.21), (5, 1.73, 6.22) })
        {
            _rohwerte.AddReading(new TentSensorReading { TentId = _zelt, MetricKey = "reservoir-ec", Value = ecWert, CapturedAtUtc = um.AddMinutes(minute) });
            _rohwerte.AddReading(new TentSensorReading { TentId = _zelt, MetricKey = "reservoir-ph", Value = phWert, CapturedAtUtc = um.AddMinutes(minute) });
        }

        _messungen.CreateMeasurement(new Measurement { GrowId = _grow, TakenAt = um.ToLocalTime(), Source = ValueOrigin.Manual, ReservoirPh = 6.22, ReservoirEc = 1.98 });

        var messung = Assert.Single(Seite().Alle).Messung!;
        Assert.Equal("hand", messung.Herkunft);
        var ph = messung.Abgleich.Single(a => a.Name == "pH");
        Assert.True(ph.Passt);
        Assert.Equal(6.21, ph.Sensor, 2);
        var ec = messung.Abgleich.Single(a => a.Name == "EC");
        Assert.False(ec.Passt);
        // Ohne Sensorwert kein Abgleich — nichts vortäuschen.
        Assert.DoesNotContain(messung.Abgleich, a => a.Name == "Wasser");
    }

    [Fact]
    public void Kurven_RohwerteInOrtsminuten_UndLichtAusDemPlan()
    {
        BrusRohwerteEinspielen();
        _licht.CreateLightSchedule(new LightSchedule { TentId = _zelt, Name = "Blüte", LightsOnTime = "05:30", LightsOffTime = "17:30", IsActive = true });

        var kurven = _dienst.Kurven(_grow, new DateOnly(2026, 10, 3))!;
        Assert.Equal("roh", kurven.Aufloesung);
        Assert.Equal(["reservoir-ec", "reservoir-ph", "reservoir-temp", "temperature", "humidity", "vpd"], kurven.Kurven.Select(k => k.Schluessel).ToArray());
        var ec = kurven.Kurven[0];
        // 14:10 UTC ist 16:10 Ortszeit (Sommerzeit) — Minute 970.
        Assert.Contains(ec.Punkte, p => p.Minute == 970 && Math.Abs(p.Wert - 1.74) < 1e-9);
        Assert.Equal(1.29, ec.Min);
        Assert.Equal("plan", kurven.LichtQuelle);
        Assert.Equal(330, Assert.Single(kurven.Licht).Von);
        Assert.Equal(1050, kurven.Licht[0].Bis);
    }

    [Fact]
    public void Kurven_AelterAlsDieRohwerte_ZeigenEhrlichNurTageswerte()
    {
        _rohwerte.UpsertDailyStat(new TentSensorDailyStat { TentId = _zelt, MetricKey = "reservoir-ec", Date = new DateOnly(2026, 9, 20), Min = 1.5, Median = 1.6, Max = 1.7, Count = 288 });

        var kurven = _dienst.Kurven(_grow, new DateOnly(2026, 9, 20))!;
        Assert.Equal("tag", kurven.Aufloesung);
        var ec = kurven.Kurven[0];
        Assert.Empty(ec.Punkte);
        Assert.Equal(1.5, ec.Min);
        Assert.Equal(1.7, ec.Max);
        Assert.Null(kurven.Kurven[1].Min);
    }

    [Fact]
    public void Lichtflanken_SchlagenDenPlan()
    {
        _licht.CreateLightSchedule(new LightSchedule { TentId = _zelt, Name = "Plan", LightsOnTime = "08:00", LightsOffTime = "20:00", IsActive = true });
        _licht.CreateLightTransitionIfNotDuplicate(new LightTransitionEvent { TentId = _zelt, Kind = LightTransitionKind.LightOff, OccurredAtUtc = new DateTime(2026, 10, 2, 15, 30, 0, DateTimeKind.Utc) });
        _licht.CreateLightTransitionIfNotDuplicate(new LightTransitionEvent { TentId = _zelt, Kind = LightTransitionKind.LightOn, OccurredAtUtc = new DateTime(2026, 10, 3, 3, 33, 0, DateTimeKind.Utc) });
        _licht.CreateLightTransitionIfNotDuplicate(new LightTransitionEvent { TentId = _zelt, Kind = LightTransitionKind.LightOff, OccurredAtUtc = new DateTime(2026, 10, 3, 15, 30, 0, DateTimeKind.Utc) });

        var kurven = _dienst.Kurven(_grow, new DateOnly(2026, 10, 3))!;
        Assert.Equal("sensor", kurven.LichtQuelle);
        var spanne = Assert.Single(kurven.Licht);
        Assert.Equal(5 * 60 + 33, spanne.Von);
        Assert.Equal(17 * 60 + 30, spanne.Bis);
    }
}
