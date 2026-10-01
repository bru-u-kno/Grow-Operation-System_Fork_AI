using System.Net;
using System.Text.RegularExpressions;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge;
using GrowDiary.Web.Tests.TestFakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Zweikomponenten-Dünger am echten Dosierweg: A, Trennzeit, B — und B nie mehr,
/// als die Grenzen erlauben.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (01.10.2026).</b> Eine Durchsicht fand vier Lücken im
/// Weg von A nach B, alle mit derselben Folge — das Verhältnis im Becken kippt,
/// ohne dass es jemand sieht:</para>
/// <list type="number">
/// <item>Die <b>Automatik</b> gab A und plante B nie ein. Das tat nur das
/// Hand-Dosieren.</item>
/// <item>B wurde mit der <b>ungekappten</b> Menge protokolliert, obwohl die
/// Pumpe nach 60 s abschaltete (4 ml/min, 5 ml ausstehend: 4 ml geflossen,
/// 5 ml im Protokoll).</item>
/// <item>B lief an <b>Einzel- und Tagesgrenze</b> vorbei.</item>
/// <item>Bei nicht erreichbarem Home Assistant — nachweislich nichts geflossen —
/// war B <b>endgültig weg</b>.</item>
/// </list>
/// <para>Die Fälle hier gehen durch <see cref="DosingWorker"/> selbst, mit
/// echter Datenbank und einem Home Assistant, das sich danebenbenehmen kann —
/// nicht durch eine Nachbildung davon.</para>
/// </remarks>
public sealed class ZweiKomponentenDosierungTests : IDisposable
{
    private static readonly DateTime Jetzt = new(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);

    private readonly string _wurzel;
    private readonly AppPaths _pfade;
    private readonly GrowRepository _grows;
    private readonly DosingRepository _dosing;
    private readonly int _zelt;
    private readonly Anlage _anlage = new();
    private readonly List<TimeSpan> _laeufe = new();
    private readonly DosingService _dienst;
    private readonly DosingWorker _takt;

    public ZweiKomponentenDosierungTests()
    {
        _wurzel = Path.Combine(Path.GetTempPath(), "ZweiKomponenten_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_wurzel);
        _pfade = new AppPaths(_wurzel);
        _zelt = TestDatabase.InitializeWithDefaultTent(_pfade).Id;
        _grows = new GrowRepository(_pfade);
        _dosing = new DosingRepository(_pfade);
        _grows.SaveHomeAssistantSettings(new HomeAssistantSettings
        {
            BaseUrl = "http://ha.local:8123", AccessToken = "token", Enabled = true,
        });

        // Jede Wartezeit wird mitgeschrieben statt abgewartet. Bei Pumpen im
        // Testbetrieb ist das genau die Laufzeit — dort fragt kein Ausschalter nach.
        _dienst = new DosingService(_grows, _dosing, _anlage,
            new Ausschalter(_anlage, NullLogger<Ausschalter>.Instance),
            NullLogger<DosingService>.Instance,
            (dauer, _) => { _laeufe.Add(dauer); return Task.CompletedTask; });
        _takt = new DosingWorker(new ServiceCollection().BuildServiceProvider(), NullLogger<DosingWorker>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_wurzel, recursive: true); } catch { /* Windows haelt manchmal fest */ }
    }

    // ------------------------------------------------- 1. Automatik plant B ein

    /// <summary>Eine automatische Dosis von A plant B ein — wie von Hand.</summary>
    [Fact]
    public async Task AutomatischeDosisVonA_PlantBEin()
    {
        var (a, b) = Paar();

        var dosiert = await Automatik(a, Lage());

        Assert.True(dosiert, "Die Automatik hat A gar nicht gegeben — der Aufbau prueft dann nichts.");
        var offen = _dosing.GetPendingForPump(b.Id);
        Assert.True(offen.Count == 1,
            $"Nach einer automatischen Dosis von A stehen {offen.Count} zweite Haelften fuer B aus. "
            + "Erwartet war genau eine — sonst steht A ohne B im Becken, Takt fuer Takt.");

        var gegebenA = _dosing.GetEvents(pumpId: a.Id).Single(e => e.Trigger == DoseTrigger.Automatic);
        Assert.Equal(gegebenA.DosedMl * a.PartnerRatio, offen[0].Ml, 2);
        Assert.Equal(Jetzt.AddMinutes(a.PartnerDelayMinutes), offen[0].DueAtUtc);
        Assert.Equal(gegebenA.Id, offen[0].SourceDoseEventId);
    }

    /// <summary>
    /// Und <b>nochmal</b>: solange B aussteht, gibt die Automatik kein zweites A.
    /// </summary>
    /// <remarks>
    /// Der zweite Durchgang ist der, auf den es ankommt (CLAUDE.md: die
    /// Reparatur einmal wiederholen). Die Sperre kommt aus der Datenbank, über
    /// denselben <see cref="DosingContextBuilder"/>, den der Takt benutzt —
    /// nicht aus einem von Hand gesetzten Schalter.
    /// </remarks>
    [Fact]
    public async Task SolangeBAussteht_GibtDieAutomatikKeinZweitesA()
    {
        var (a, _) = Paar();
        Assert.True(await Automatik(a, Lage()), "Die erste Dosis von A fiel nicht — der Aufbau prueft nichts.");

        // Nach der Mischpause von A, aber bevor B lief.
        var spaeter = Jetzt.AddMinutes(a.MinIntervalMinutes + 1);
        var ausDerDatenbank = Bauer().Build(a, spaeter).Context;
        Assert.True(ausDerDatenbank.TentHasPendingDose,
            "Der Kontext aus der Datenbank sieht die ausstehende Haelfte nicht — dann haelt nichts ein zweites A auf.");

        var zweites = await Automatik(a, Lage(tentHasPending: ausDerDatenbank.TentHasPendingDose), spaeter);

        Assert.False(zweites, "Die Automatik gab ein zweites A, waehrend B noch aussteht.");
        Assert.Single(_dosing.GetEvents(pumpId: a.Id), e => e.Trigger == DoseTrigger.Automatic);
        Assert.Single(_dosing.GetDuePending(spaeter.AddDays(1)));
    }

    /// <summary>Kann B nicht laufen, gibt die Automatik auch A nicht.</summary>
    /// <remarks>
    /// Unbeaufsichtigt ist A ohne B schlimmer als gar nichts: das Verhältnis
    /// kippt jeden Takt weiter. Drei Formen: B unkalibriert, B gelöscht, und A
    /// echt / B im Testbetrieb (B würde „gegeben", es flösse nichts).
    /// </remarks>
    [Theory]
    [InlineData("unkalibriert")]
    [InlineData("geloescht")]
    [InlineData("anderer Betrieb")]
    public async Task KannBNichtLaufen_GibtDieAutomatikAuchANicht(string fall)
    {
        var (a, b) = Paar();
        switch (fall)
        {
            case "unkalibriert":
                b.MlPerMinute = null;
                _dosing.UpdatePump(b);
                break;
            case "geloescht":
                _dosing.DeletePump(b.Id);
                break;
            default:
                b.SimulationMode = false;
                b.HaEntityId = "switch.duenger_b";
                _dosing.UpdatePump(b);
                break;
        }

        var dosiert = await Automatik(_dosing.GetPump(a.Id)!, Lage());

        Assert.False(dosiert, $"B ist {fall}, und die Automatik gab trotzdem A — im Becken steht A ohne B.");
        Assert.DoesNotContain(_dosing.GetEvents(pumpId: a.Id), e => e.Trigger == DoseTrigger.Automatic);
    }

    /// <summary>Die Gegenprobe: ohne Partner dosiert die Automatik wie immer.</summary>
    [Fact]
    public async Task OhnePartner_DosiertDieAutomatikWieImmer()
    {
        var (a, _) = Paar();
        a.PartnerPumpId = null;
        _dosing.UpdatePump(a);

        Assert.True(await Automatik(_dosing.GetPump(a.Id)!, Lage()),
            "Eine Pumpe ohne Partner dosiert nicht mehr — die neue Sperre greift zu weit.");
        Assert.Empty(_dosing.GetDuePending(Jetzt.AddDays(1)));
    }

    // --------------------------------------- 2. Protokoll = was geflossen ist

    /// <summary>
    /// Der Fall aus der Durchsicht: 4 ml/min, 5 ml ausstehend.
    /// </summary>
    /// <remarks>
    /// Ein Lauf darf höchstens 60 s dauern; das sind 4 ml. Protokolliert werden
    /// diese 4 ml und 60 s — und der Rest von 1 ml folgt im nächsten Takt, damit
    /// das Verhältnis am Ende stimmt.
    /// </remarks>
    [Fact]
    public async Task GekappterLauf_ProtokolliertWasGeflossenIst_UndDerRestFolgt()
    {
        var b = PumpeB(mlProMinute: 4);
        var offen = Einplanen(b, 5);

        await Haelfte(b, offen, Jetzt);

        var erster = _dosing.GetEvents(pumpId: b.Id).Single(e => e.Trigger == DoseTrigger.Partner);
        Assert.True(erster.DosedMl == 4.0 && erster.SecondsRun == 60,
            $"Protokolliert: {erster.DosedMl} ml in {erster.SecondsRun} s. Geflossen sind bei 4 ml/min "
            + "und 60 s Grenze genau 4 ml — das Protokoll luegt sonst ueber das Becken.");
        Assert.True(_laeufe.Single() == TimeSpan.FromSeconds(60),
            $"Die Pumpe lief {string.Join(", ", _laeufe)} — erwartet war ein Lauf von 60 s.");

        var rest = Assert.Single(_dosing.GetDuePending(Jetzt.AddMinutes(PartnerDosing.MinDelayMinutes)));
        Assert.Equal(1.0, rest.Ml, 2);

        // Und nochmal: der Rest im naechsten Takt.
        var naechster = Jetzt.AddMinutes(PartnerDosing.MinDelayMinutes);
        await Haelfte(b, rest, naechster);

        var alle = _dosing.GetEvents(pumpId: b.Id).Where(e => e.Trigger == DoseTrigger.Partner).ToList();
        Assert.Equal(2, alle.Count);
        Assert.Equal(5.0, alle.Sum(e => e.DosedMl), 2);
        Assert.Empty(_dosing.GetDuePending(naechster.AddDays(1)));
    }

    /// <summary>Kein Lauf überschreitet je die harte Grenze — bei keiner Fördermenge.</summary>
    [Fact]
    public void HoechstensJeLauf_BleibtImmerUnterDerGrenze()
    {
        var geprueft = 0;
        for (var rate = 0.37; rate < 400; rate *= 1.07)
        {
            var ml = DosingGuard.HoechstensJeLauf(rate);
            var sekunden = DosingCalculator.SecondsFor(ml, rate);
            Assert.True(sekunden <= DosingGuard.AbsoluteMaxSeconds,
                $"Bei {rate:0.###} ml/min ergaeben {ml} ml {sekunden} s — ueber der harten Grenze, "
                + "und der Waechter lehnte B dann jeden Takt aufs Neue ab.");
            Assert.True(sekunden >= DosingGuard.AbsoluteMaxSeconds - 2,
                $"Bei {rate:0.###} ml/min nur {sekunden} s je Lauf — das verschenkt Laufzeit.");
            geprueft++;
        }

        Assert.True(geprueft >= 50, $"Nur {geprueft} Foerdermengen geprueft — die Schleife sieht ihre Grundmenge nicht.");
    }

    // ------------------------------------------- 3. B hält die Grenzen ein

    /// <summary>B gibt nie mehr, als die Tagesmenge der Pumpe B erlaubt.</summary>
    [Fact]
    public async Task ZweiteHaelfte_HaeltDieTagesmengeEin()
    {
        var b = PumpeB(mlProMinute: 30);
        b.MaxMlPerDay = 6;
        _dosing.UpdatePump(b);
        Gelaufen(b, Jetzt.AddHours(-2), 5);

        await Haelfte(b, Einplanen(b, 3), Jetzt);

        var heute = _dosing.GetDosesSince(b.Id, Jetzt.Date).Where(DosingService.KannGelaufenSein).Sum(e => e.DosedMl);
        Assert.True(heute <= 6.0 + 1e-9,
            $"Pumpe B hat heute {heute} ml gegeben, erlaubt sind 6 ml. Die zweite Haelfte lief an der "
            + "Tagesmenge vorbei.");
        var rest = Assert.Single(_dosing.GetDuePending(Jetzt.AddDays(1)));
        Assert.Equal(2.0, rest.Ml, 2);

        // Und nochmal: die Grenze ist erreicht, der Rest wartet — er wird nicht verworfen.
        var naechster = Jetzt.AddMinutes(1);
        await Haelfte(b, rest, naechster);

        Assert.Single(_dosing.GetEvents(pumpId: b.Id), e => e.Trigger == DoseTrigger.Partner);
        var wartet = Assert.Single(_dosing.GetDuePending(naechster.AddDays(1)));
        Assert.Equal(rest.Id, wartet.Id);
    }

    /// <summary>B gibt nie mehr als die größte Einzeldosis der Pumpe B in einem Zug.</summary>
    [Fact]
    public async Task ZweiteHaelfte_HaeltDieEinzeldosisEin()
    {
        var b = PumpeB(mlProMinute: 30);
        b.MaxSingleDoseMl = 2;
        _dosing.UpdatePump(b);

        await Haelfte(b, Einplanen(b, 5), Jetzt);

        var lauf = _dosing.GetEvents(pumpId: b.Id).Single(e => e.Trigger == DoseTrigger.Partner);
        Assert.True(lauf.DosedMl <= 2.0 + 1e-9,
            $"Ein Zug von B gab {lauf.DosedMl} ml, die groesste Einzeldosis ist 2 ml.");
        Assert.Equal(3.0, Assert.Single(_dosing.GetDuePending(Jetzt.AddDays(1))).Ml, 2);
    }

    /// <summary>
    /// Ein legitimer A/B-Zyklus wird <b>nicht</b> von der Mischpause blockiert.
    /// </summary>
    /// <remarks>
    /// A liegt per Konstruktion nur die Trennzeit zurück, und die ausstehende
    /// Hälfte ist B selbst. Gälten diese beiden Riegel für B, wartete B jeden
    /// Takt aufs Neue — für immer.
    /// </remarks>
    [Fact]
    public async Task ZweiteHaelfte_WirdNichtVonMischpauseUndEigenerSperreBlockiert()
    {
        var b = PumpeB(mlProMinute: 30);
        var offen = Einplanen(b, 3);
        var kontext = Kontext(b, Jetzt) with
        {
            LastTentDoseUtc = Jetzt.AddMinutes(-5),
            TentHasPendingDose = true,
        };

        await _takt.GibZweiteHaelfteAsync(_dosing, _dienst, offen, b, kontext, Jetzt, CancellationToken.None);

        var lauf = _dosing.GetEvents(pumpId: b.Id).SingleOrDefault(e => e.Trigger == DoseTrigger.Partner);
        Assert.True(lauf is { Outcome: DoseOutcome.Done, DosedMl: 3.0 },
            "B lief fuenf Minuten nach A nicht — die Mischpause oder die Sperre „Haelfte steht aus\" "
            + "haelt B auf. Dann wartet B fuer immer, und A steht allein im Becken.");
        Assert.Empty(_dosing.GetDuePending(Jetzt.AddDays(1)));
    }

    // ------------------------------- 4. Home Assistant weg: B bleibt stehen

    /// <summary>
    /// Nichts gesendet heisst nichts geflossen — dann bleibt B stehen und kommt
    /// wieder, sobald Home Assistant zurück ist.
    /// </summary>
    [Fact]
    public async Task HomeAssistantWeg_ZweiteHaelfteBleibtStehenUndKommtSpaeter()
    {
        var b = EchteB();
        _anlage.Weg = true;

        await Haelfte(b, Einplanen(b, 3), Jetzt);

        var erster = Assert.Single(_dosing.GetDuePending(Jetzt.AddDays(1)));
        Assert.True(erster.Ml == 3 && erster.Fehlversuche == 1,
            $"Nach einem Versuch ohne Home Assistant steht {(erster is null ? "nichts" : $"{erster.Ml} ml, {erster.Fehlversuche} Fehlversuche")} aus. "
            + "Es ist nachweislich nichts geflossen — B muss bleiben.");
        Assert.Equal(0, _anlage.Anzahl("turn_on"));

        // Und nochmal, ohne Neustart: der Zaehler steigt, das Protokoll bleibt ruhig.
        await Haelfte(b, erster, Jetzt.AddMinutes(1));
        var zweiter = Assert.Single(_dosing.GetDuePending(Jetzt.AddDays(1)));
        Assert.Equal(2, zweiter.Fehlversuche);
        Assert.Single(_dosing.GetEvents(pumpId: b.Id), e => e.Trigger == DoseTrigger.Partner);

        // Home Assistant ist zurueck: B laeuft, der Eintrag ist weg.
        _anlage.Weg = false;
        await Haelfte(b, zweiter, Jetzt.AddMinutes(2));

        Assert.Contains(_dosing.GetEvents(pumpId: b.Id),
            e => e.Trigger == DoseTrigger.Partner && e.Outcome == DoseOutcome.Done && e.DosedMl == 3);
        Assert.Equal(1, _anlage.Anzahl("turn_on"));
        Assert.Empty(_dosing.GetDuePending(Jetzt.AddDays(1)));
    }

    /// <summary>
    /// Bleibt Home Assistant weg, gibt der Takt nach einer festen Zahl Versuche auf.
    /// </summary>
    /// <remarks>
    /// Solange B aussteht, dosiert im ganzen Becken niemand. Ein für immer
    /// stehender Eintrag hielte also auch die pH-Pumpe für immer an.
    /// </remarks>
    [Fact]
    public async Task HomeAssistantDauerhaftWeg_GibtNachHoechstensMaxFehlversuchenAuf()
    {
        var b = EchteB();
        _anlage.Weg = true;
        Einplanen(b, 3);

        var takte = 0;
        var jetzt = Jetzt;
        while (takte < PartnerDosing.MaxFehlversuche + 10
               && _dosing.GetDuePending(jetzt).FirstOrDefault() is { } offen)
        {
            await Haelfte(b, offen, jetzt);
            takte++;
            jetzt = jetzt.AddMinutes(1);
        }

        Assert.True(takte >= 2, $"Nur {takte} Takt(e) — der Aufbau sieht die Wiederholung gar nicht.");
        Assert.True(takte == PartnerDosing.MaxFehlversuche,
            $"Der Takt versuchte es {takte}-mal, erwartet waren {PartnerDosing.MaxFehlversuche}.");
        Assert.Empty(_dosing.GetDuePending(jetzt.AddDays(1)));

        var zeilen = _dosing.GetEvents(pumpId: b.Id).Where(e => e.Trigger == DoseTrigger.Partner).ToList();
        Assert.True(zeilen.Count == 2 && zeilen.All(e => e.Outcome == DoseOutcome.Rejected),
            $"{zeilen.Count} Protokollzeilen fuer {takte} Versuche. Erwartet: eine beim ersten Fehlschlag, "
            + "eine beim Aufgeben — dreissig verdraengten sonst die echten Dosen, aus denen Mischpause und Lernen lesen.");
        Assert.Contains("von Hand nachgeben", zeilen[0].Reason);
        Assert.Equal(0, _anlage.Anzahl("turn_on"));
    }

    /// <summary>
    /// Gesendet, aber nicht bestätigt: kein zweiter Versuch und kein Rest.
    /// </summary>
    /// <remarks>
    /// Vielleicht ist B schon geflossen. Doppeltes B ist das schlimmere Ende —
    /// und ins Protokoll kommt die Menge, die dieser Lauf höchstens bringen
    /// konnte, nicht der ganze ausstehende Auftrag.
    /// </remarks>
    [Fact]
    public async Task UnbestaetigtesEinschalten_KeineWiederholungUndKeinRest()
    {
        var b = EchteB(mlProMinute: 4);
        _anlage.AntwortZuSpaet = true;

        await Haelfte(b, Einplanen(b, 5), Jetzt);

        var lauf = _dosing.GetEvents(pumpId: b.Id).Single(e => e.Trigger == DoseTrigger.Partner);
        Assert.Equal(DoseOutcome.Failed, lauf.Outcome);
        Assert.Equal(4.0, DosingService.HoechstensGegeben(lauf), 2);
        Assert.Empty(_dosing.GetDuePending(Jetzt.AddDays(1)));
        Assert.True(_anlage.Anzahl("turn_off") >= 1, "Nach unbestaetigtem Einschalten wurde B nicht ausgeschaltet.");
    }

    /// <summary>Kann B nie laufen, wird es verworfen — mit Zeile im Protokoll.</summary>
    [Fact]
    public async Task UnkalibrierteB_WirdVerworfenUndProtokolliert()
    {
        var b = PumpeB(mlProMinute: 30);
        var offen = Einplanen(b, 3);
        b.MlPerMinute = null;
        _dosing.UpdatePump(b);

        await Haelfte(_dosing.GetPump(b.Id)!, offen, Jetzt);

        Assert.Empty(_dosing.GetDuePending(Jetzt.AddDays(1)));
        var zeile = _dosing.GetEvents(pumpId: b.Id).Single(e => e.Trigger == DoseTrigger.Partner);
        Assert.Equal(DoseOutcome.Rejected, zeile.Outcome);
        Assert.Contains("von Hand nachgeben", zeile.Reason);
        Assert.Empty(_laeufe);
    }

    // ------------------------------------------ Beide Wege, eine Einplanung

    /// <summary>Hand und Automatik planen B über dieselbe Stelle ein.</summary>
    /// <remarks>
    /// Die Automatik ist oben über den echten Weg geprüft. Der Controller hier
    /// über seinen Quelltext — Kommentare zählen nicht.
    /// </remarks>
    [Theory]
    [InlineData("Services/DosingWorker.cs")]
    [InlineData("Api/Controllers/DosingApiController.cs")]
    public void BeideWegeBenutzenDieGemeinsameEinplanung(string datei)
    {
        var quelle = File.ReadAllText(Path.Combine(ProjektWurzel(), "GrowDiary.Web", datei));
        var ohneKommentare = Regex.Replace(
            Regex.Replace(quelle, @"/\*.*?\*/", " ", RegexOptions.Singleline), @"//[^\n]*", string.Empty);

        Assert.True(ohneKommentare.Length > 3000,
            $"Nur {ohneKommentare.Length} Zeichen von {datei} gelesen — die Pruefung sieht die Datei nicht.");
        Assert.Contains("PartnerDosing.Einplanen(", ohneKommentare, StringComparison.Ordinal);
        Assert.False(Regex.IsMatch(ohneKommentare, @"new\s+PendingDose\b") && datei.Contains("Controller"),
            $"{datei} legt selbst eine zweite Haelfte an — das gehoert nach PartnerDosing.Einplanen.");
    }

    /// <summary>
    /// Eine bestehende Installation bekommt die neue Spalte beim Start nachgetragen.
    /// </summary>
    /// <remarks>
    /// Das CREATE TABLE erreicht nur frische Datenbanken. Fehlte der Nachtrag,
    /// bräche bei jedem Bestandsnutzer schon das Lesen der ausstehenden
    /// Hälften ab — und mit ihm der ganze Dosiertakt.
    /// </remarks>
    [Fact]
    public void BestehendeDatenbankOhneSpalte_BekommtSieBeimStart()
    {
        using (var verbindung = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_pfade.DatabasePath};Pooling=False"))
        {
            verbindung.Open();
            using var befehl = verbindung.CreateCommand();
            befehl.CommandText = "ALTER TABLE PendingDoses DROP COLUMN Fehlversuche;";
            befehl.ExecuteNonQuery();
        }

        Assert.ThrowsAny<Exception>(() => _dosing.GetDuePending(Jetzt));

        TestDatabase.Initialize(_pfade);

        var b = PumpeB(mlProMinute: 30);
        var offen = Einplanen(b, 3);
        Assert.Equal(0, offen.Fehlversuche);
        Assert.Single(_dosing.GetDuePending(Jetzt));
    }

    // ------------------------------------------------------------------ Hilfe

    private (DosingPump A, DosingPump B) Paar()
    {
        var b = PumpeB(mlProMinute: 30);
        var a = new DosingPump
        {
            TentId = _zelt, Name = "Grow A", Purpose = DosingPurpose.Nutrient,
            SimulationMode = true, MlPerMinute = 30, AutomationEnabled = true,
            PartnerPumpId = b.Id, PartnerRatio = 1, PartnerDelayMinutes = 5,
        };
        a.Id = _dosing.InsertPump(a);

        // Erfahrung fuer A: drei echte Dosen mit Wert davor und danach, je ml +0,1.
        for (var i = 1; i <= 3; i++)
        {
            _dosing.InsertEvent(new DoseEvent
            {
                PumpId = a.Id, TentId = _zelt, OccurredAtUtc = Jetzt.AddDays(-i),
                Trigger = DoseTrigger.Manual, Outcome = DoseOutcome.Done,
                RequestedMl = 1, DosedMl = 1, SecondsRun = 2, ValueBefore = 1.0, ValueAfter = 1.1,
            });
        }

        return (_dosing.GetPump(a.Id)!, _dosing.GetPump(b.Id)!);
    }

    private DosingPump PumpeB(double mlProMinute)
    {
        var b = new DosingPump
        {
            TentId = _zelt, Name = "Grow B", Purpose = DosingPurpose.Nutrient,
            SimulationMode = true, MlPerMinute = mlProMinute,
        };
        b.Id = _dosing.InsertPump(b);
        return _dosing.GetPump(b.Id)!;
    }

    private DosingPump EchteB(double mlProMinute = 30)
    {
        var b = PumpeB(mlProMinute);
        b.SimulationMode = false;
        b.HaEntityId = Anlage.Entitaet;
        _dosing.UpdatePump(b);
        return _dosing.GetPump(b.Id)!;
    }

    private PendingDose Einplanen(DosingPump b, double ml)
    {
        _dosing.InsertPending(new PendingDose
        {
            PumpId = b.Id, Ml = ml, DueAtUtc = Jetzt.AddMinutes(-1), Reason = "Zweite Hälfte.", CreatedAtUtc = Jetzt.AddMinutes(-6),
        });
        return _dosing.GetPendingForPump(b.Id).Single();
    }

    private void Gelaufen(DosingPump pumpe, DateTime wann, double ml)
        => _dosing.InsertEvent(new DoseEvent
        {
            PumpId = pumpe.Id, TentId = _zelt, OccurredAtUtc = wann,
            Trigger = DoseTrigger.Manual, Outcome = DoseOutcome.Done, RequestedMl = ml, DosedMl = ml, SecondsRun = 10,
        });

    /// <summary>Was der Takt über B wüsste: die heutigen Dosen aus der Datenbank.</summary>
    private DosingContext Kontext(DosingPump b, DateTime jetzt)
        => new(null, null, null, false, _dosing.GetDosesSince(b.Id, jetzt.ToLocalTime().Date.ToUniversalTime()), null);

    private Task Haelfte(DosingPump b, PendingDose offen, DateTime jetzt)
        => _takt.GibZweiteHaelfteAsync(_dosing, _dienst, offen, b, Kontext(b, jetzt), jetzt, CancellationToken.None);

    private static DosingSituation Lage(bool tentHasPending = false)
        => new(new DosingContext(
                Reading: 1.0, ReadingAge: TimeSpan.FromMinutes(1), ProbeCalibratedAtUtc: Jetzt.AddDays(-1),
                ProbeCalibrationOverdue: false, DosesToday: Array.Empty<DoseEvent>(), WaterLevelOk: null,
                TentHasPendingDose: tentHasPending),
            Target: 1.4, TargetFrom: TargetSource.User, ReadingFrom: ReadingSource.Sensor);

    private Task<bool> Automatik(DosingPump a, DosingSituation lage, DateTime? jetzt = null)
        => _takt.DoseIfNeededAsync(_dosing, _dienst, Meldungen(), a, lage, jetzt ?? Jetzt, CancellationToken.None);

    private NotificationService Meldungen()
        => new(new NotificationSettingsRepository(_pfade), _grows,
            new HomeAssistantService(
                new StubHttpClientFactory(new RecordingHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))),
                NullLogger<HomeAssistantService>.Instance),
            NullLogger<NotificationService>.Instance);

    private DosingContextBuilder Bauer()
        => new(_grows, _dosing, new SensorReadingRepository(_pfade), new AlertRuleRepository(_pfade),
            new TargetValueService(new KnowledgeBaseLoader(_pfade, NullLogger<KnowledgeBaseLoader>.Instance)),
            new HydroSetupRepository(_pfade, new TentRepository(_pfade)));

    private static string ProjektWurzel()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "GrowDiary.Web"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Projektwurzel nicht gefunden.");
    }

    /// <summary>Ein Schalter in Home Assistant, der weg sein oder zu spät antworten kann.</summary>
    private sealed class Anlage : IAcFunk
    {
        public const string Entitaet = "switch.duenger_b";

        private string _stand = "off";

        /// <summary>Home Assistant nicht erreichbar: nichts lesbar, nichts kommt an.</summary>
        public bool Weg { get; set; }

        /// <summary>Der Befehl wirkt, die Antwort bleibt aus — der Aufruf meldet false.</summary>
        public bool AntwortZuSpaet { get; set; }

        private readonly List<string> _gesendet = new();

        public int Anzahl(string dienst) => _gesendet.Count(d => d == dienst);

        public Task<HomeAssistantState?> ZustandAsync(HomeAssistantSettings einstellungen, string entityId, CancellationToken ct)
            => Task.FromResult<HomeAssistantState?>(Weg ? null : new HomeAssistantState { EntityId = entityId, State = _stand });

        public Task<bool> SchickenAsync(
            HomeAssistantSettings einstellungen, string domain, string dienst, string entityId,
            IReadOnlyDictionary<string, object> daten, CancellationToken ct)
        {
            _gesendet.Add(dienst);
            if (Weg) return Task.FromResult(false);
            _stand = dienst == "turn_on" ? "on" : "off";
            return Task.FromResult(!(AntwortZuSpaet && dienst == "turn_on"));
        }
    }
}
