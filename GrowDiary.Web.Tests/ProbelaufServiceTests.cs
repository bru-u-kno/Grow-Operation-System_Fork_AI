using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Tests.KiZugriff;
using Microsoft.Data.Sqlite;

namespace GrowDiary.Web.Tests;

/// <summary>
/// Fork AI (A-010, 07.10.2026): Der Probelauf als Ganzes — Start, Grenzen, Zurückstellen, Neustart, Auswertung.
/// </summary>
/// <remarks>
/// Das sind die Prüfungen der Sicherheit: Jede steht für einen Fall, in dem ohne den Schutz ein Gerät im
/// Eingriffszustand bliebe. Die Uhr geht nur weiter, wenn der Test es sagt.
/// </remarks>
public sealed class ProbelaufServiceTests : IDisposable
{
    private const string Regelung = "automation.entfeuchter_regelung";
    private const string Port = "select.rdwc_dehumi_aktiver_modus";

    private readonly string _ordner;
    private readonly AppPaths _pfade;
    private readonly VerstellbareUhr _uhr = new();
    private readonly FakeProbelaufHa _ha = new();
    private readonly FakeChillerRegler _chiller = new();
    private readonly FakeProbelaufMessung _messung = new();
    private readonly FakeProbelaufMeldung _meldung = new();
    private readonly ProbelaufRepository _repo;
    private readonly ProbelaufService _dienst;

    public ProbelaufServiceTests()
    {
        _ordner = Path.Combine(Path.GetTempPath(), "Probelauf_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_ordner);
        _pfade = new AppPaths(_ordner);
        TestDatabase.Initialize(_pfade);
        _repo = new ProbelaufRepository(_pfade);

        _ha.Regelungen.Add(Regelung);
        _ha.Zustaende[Regelung] = "on";
        _ha.Geraete["port_schalter"] = Port;
        _ha.Zustaende[Port] = "Auto";
        Zelt();

        _dienst = Neuer();
    }

    private ProbelaufService Neuer() => new(_repo, new ProbelaufEingriff(_ha, _chiller), _messung, _meldung, _uhr);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_ordner, recursive: true); } catch { }
    }

    private void Zelt(double? feuchte = 55, double? temp = 25, double? vpd = 1.2)
        => _messung.Aktuell = new ProbelaufMesswerte(_uhr.Jetzt.UtcDateTime, feuchte, temp, vpd);

    private void Weiter(TimeSpan dauer)
    {
        _uhr.Jetzt += dauer;
        if (_messung.Aktuell is { } m) _messung.Aktuell = m with { ZeitUtc = _uhr.Jetzt.UtcDateTime };
    }

    private async Task<ProbelaufLauf> StartenAsync(int minuten = 10, string modul = "entfeuchter")
    {
        var e = await _dienst.StartenAsync(new ProbelaufStart(modul, minuten, null), default);
        Assert.True(e.Fehler == ProbelaufFehler.Keiner, e.Meldung);
        return e.Lauf!;
    }

    private ProbelaufLauf Lauf(long id) => _repo.Holen(id)!;

    // ------------------------------------------------------------------- Start

    [Fact]
    public async Task Start_HaeltDenLaufFest_BevorEtwasAmZeltAngefasstWird()
    {
        var offeneBeimEingriff = -1;
        ProbelaufLauf? beimEingriff = null;
        _ha.BeiAenderung = () => { offeneBeimEingriff = _repo.Offene().Count; beimEingriff = _repo.Offene().FirstOrDefault(); };

        var lauf = await StartenAsync(10);

        Assert.Equal(1, offeneBeimEingriff);
        // Der Wecker stand da: geplantes und hartes Ende, und der Zustand zum Zurückstellen.
        Assert.Equal(lauf.StartUtc.AddMinutes(10), beimEingriff!.GeplantesEndeUtc);
        Assert.Equal(lauf.StartUtc.AddMinutes(15), beimEingriff.HartesEndeUtc);
        Assert.Contains(Port, beimEingriff.Ausgangszustand);
    }

    [Fact]
    public async Task Start_PausiertDieRegelungUndSchaltetDasGeraetAus()
    {
        await StartenAsync();

        Assert.Equal("off", _ha.Zustaende[Regelung]);
        Assert.Equal("off", _ha.Zustaende[Port]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(61)]
    public async Task Start_UngueltigeDauerWirdAbgelehnt_UndNichtsWirdAngefasst(int minuten)
    {
        var e = await _dienst.StartenAsync(new ProbelaufStart("entfeuchter", minuten, null), default);

        Assert.Equal(ProbelaufFehler.DauerUngueltig, e.Fehler);
        Assert.Empty(_ha.Protokoll);
        Assert.Empty(_repo.Liste());
    }

    [Fact]
    public async Task Start_UnbekanntesModulWirdAbgelehnt()
        => Assert.Equal(ProbelaufFehler.ModulUnbekannt, (await _dienst.StartenAsync(new ProbelaufStart("licht", 10, null), default)).Fehler);

    [Fact]
    public async Task Start_ZweiterLaufWaehrendDesErstenWirdAbgelehnt()
    {
        await StartenAsync();

        var zweiter = await _dienst.StartenAsync(new ProbelaufStart("zuluft", 10, null), default);

        Assert.Equal(ProbelaufFehler.LaeuftSchon, zweiter.Fehler);
        Assert.Single(_repo.Liste());
    }

    [Fact]
    public async Task Start_GrenzeSchonVerletzt_WirdAbgelehnt()
    {
        Zelt(feuchte: 63);

        var e = await _dienst.StartenAsync(new ProbelaufStart("entfeuchter", 10, null), default);

        Assert.Equal(ProbelaufFehler.GrenzeVerletzt, e.Fehler);
        Assert.Empty(_ha.Protokoll);
    }

    [Fact]
    public async Task Start_OhneMessung_WirdAbgelehnt()
    {
        _messung.Aktuell = null;

        Assert.Equal(ProbelaufFehler.KeineMessung, (await _dienst.StartenAsync(new ProbelaufStart("entfeuchter", 10, null), default)).Fehler);
        Assert.Empty(_ha.Protokoll);
    }

    [Fact]
    public async Task Start_EingriffNichtBestaetigt_StelltSofortZurueck_UndMeldetDenFehler()
    {
        _ha.AusschaltHaengt.Add(Port);

        var e = await _dienst.StartenAsync(new ProbelaufStart("entfeuchter", 10, null), default);

        Assert.Equal(ProbelaufFehler.EingriffFehlgeschlagen, e.Fehler);
        Assert.Equal("on", _ha.Zustaende[Regelung]);
        Assert.Equal("Auto", _ha.Zustaende[Port]);
        Assert.Equal(ProbelaufStatus.Nachlauf, Lauf(e.Lauf!.Id).Status);
    }

    // -------------------------------------------------------------- Grenzen

    [Fact]
    public async Task Tick_GrenzverletzungBrichtSofortAb_UndStelltZurueck()
    {
        var lauf = await StartenAsync(20);
        Weiter(TimeSpan.FromMinutes(3));
        Zelt(feuchte: 61.2);

        await _dienst.TickAsync(default);

        var nachher = Lauf(lauf.Id);
        Assert.Equal(ProbelaufStatus.Nachlauf, nachher.Status);
        Assert.Contains("Luftfeuchte", nachher.AbbruchGrund);
        Assert.Equal("Auto", _ha.Zustaende[Port]);
        Assert.Equal("on", _ha.Zustaende[Regelung]);
    }

    [Fact]
    public async Task Tick_FuehlerOhneWert_BrichtErstNachEinerMinuteAb()
    {
        var lauf = await StartenAsync(20);
        Weiter(TimeSpan.FromMinutes(1));
        _messung.Aktuell = null;
        await _dienst.TickAsync(default);
        Assert.Equal(ProbelaufStatus.Laeuft, Lauf(lauf.Id).Status);

        Weiter(TimeSpan.FromSeconds(30));
        await _dienst.TickAsync(default);
        Assert.Equal(ProbelaufStatus.Laeuft, Lauf(lauf.Id).Status);

        Weiter(TimeSpan.FromSeconds(40));
        await _dienst.TickAsync(default);

        var nachher = Lauf(lauf.Id);
        Assert.Equal(ProbelaufStatus.Nachlauf, nachher.Status);
        Assert.Contains("Fühler", nachher.AbbruchGrund);
        Assert.Equal("Auto", _ha.Zustaende[Port]);
    }

    [Fact]
    public async Task Tick_KommtDerFuehlerRechtzeitigWieder_LaeuftDerLaufWeiter()
    {
        var lauf = await StartenAsync(20);
        Weiter(TimeSpan.FromMinutes(1));
        _messung.Aktuell = null;
        await _dienst.TickAsync(default);
        Weiter(TimeSpan.FromSeconds(40));
        Zelt();
        await _dienst.TickAsync(default);

        Assert.Equal(ProbelaufStatus.Laeuft, Lauf(lauf.Id).Status);
        Assert.Null(Lauf(lauf.Id).FuehlerLosSeitUtc);
    }

    [Fact]
    public async Task Tick_WirftDieMessung_KommtDasEndeTrotzdem()
    {
        var lauf = await StartenAsync(10);
        _messung.Wirft = true;
        Weiter(TimeSpan.FromMinutes(10));

        await _dienst.TickAsync(default);

        Assert.Equal(ProbelaufStatus.Nachlauf, Lauf(lauf.Id).Status);
        Assert.Equal("Auto", _ha.Zustaende[Port]);
    }

    // --------------------------------------------------- Ende und Auswertung

    [Fact]
    public async Task Tick_ZumGeplantenEnde_StelltZurueck_NachlaufDanachFertigMitAuswertung()
    {
        var lauf = await StartenAsync(10);
        for (var s = 0; s < 20; s++)
        {
            Weiter(TimeSpan.FromSeconds(30));
            Zelt(feuchte: 55 + s * 0.2);
            await _dienst.TickAsync(default);
        }
        Assert.Equal(ProbelaufStatus.Nachlauf, Lauf(lauf.Id).Status);
        Assert.Equal("Auto", _ha.Zustaende[Port]);

        for (var s = 0; s < 22; s++)
        {
            Weiter(TimeSpan.FromSeconds(30));
            Zelt(feuchte: 59 - s * 0.3);
            await _dienst.TickAsync(default);
        }

        var fertig = Lauf(lauf.Id);
        Assert.Equal(ProbelaufStatus.Fertig, fertig.Status);
        Assert.NotNull(fertig.Auswertung);
        Assert.Contains(fertig.Auswertung!.Kennzahlen, k => k.Groesse == "Feuchte" && k.AenderungProMinute > 0);
        Assert.NotNull(fertig.EndeUtc);
    }

    [Fact]
    public async Task Auswertung_NenntEinenLichtwechselWaehrendDesLaufs()
    {
        var lauf = await StartenAsync(10);
        for (var s = 0; s < 45; s++)
        {
            Weiter(TimeSpan.FromSeconds(30));
            if (s == 10) _messung.TagPhase = false;
            await _dienst.TickAsync(default);
        }

        var fertig = Lauf(lauf.Id);
        Assert.Equal(ProbelaufStatus.Fertig, fertig.Status);
        Assert.Contains(fertig.Auswertung!.Hinweise, h => h.Contains("Lichtphase"));
    }

    [Fact]
    public async Task NachFertig_KannDerNaechsteLaufStarten()
    {
        await StartenAsync(1);
        for (var s = 0; s < 40; s++) { Weiter(TimeSpan.FromSeconds(30)); await _dienst.TickAsync(default); }
        Assert.Empty(_repo.Offene());

        var zweiter = await _dienst.StartenAsync(new ProbelaufStart("zuluft", 10, null), default);

        Assert.Equal(ProbelaufFehler.Keiner, zweiter.Fehler);
    }

    [Fact]
    public async Task Abbrechen_VonHand_StelltZurueckUndNenntDenGrund()
    {
        var lauf = await StartenAsync(20);
        Weiter(TimeSpan.FromMinutes(2));

        await _dienst.AbbrechenAsync(lauf.Id, "Von Hand abgebrochen.", default);

        var nachher = Lauf(lauf.Id);
        Assert.Equal(ProbelaufStatus.Nachlauf, nachher.Status);
        Assert.Equal("Von Hand abgebrochen.", nachher.AbbruchGrund);
        Assert.Equal("Auto", _ha.Zustaende[Port]);
    }

    // ----------------------------------------------------------- Neustart, Fehler

    [Fact]
    public async Task OffeneBeiStart_StelltAlsErstesZurueck_AuchWennDasAddonMitteninStarb()
    {
        var lauf = await StartenAsync(20);
        Weiter(TimeSpan.FromMinutes(4));
        Assert.Equal("off", _ha.Zustaende[Port]);

        // Neustart: eine neue Instanz, nur die Datenbank erinnert sich.
        var nachNeustart = Neuer();
        await nachNeustart.OffeneBeiStartZurueckstellenAsync(default);

        var nachher = Lauf(lauf.Id);
        Assert.Equal("Auto", _ha.Zustaende[Port]);
        Assert.Equal("on", _ha.Zustaende[Regelung]);
        Assert.Contains("neu gestartet", nachher.AbbruchGrund);
        Assert.Equal(ProbelaufStatus.Nachlauf, nachher.Status);
    }

    [Fact]
    public async Task Rueckstellung_Scheitert_BleibtOffen_WiederholtSich_UndMeldetNachDreiVersuchen()
    {
        var lauf = await StartenAsync(10);
        _ha.Haengt.Add(Port);                      // das Gerät antwortet beim Zurückstellen nicht
        Weiter(TimeSpan.FromMinutes(10));
        await _dienst.TickAsync(default);
        Assert.Equal(ProbelaufStatus.RueckstellungOffen, Lauf(lauf.Id).Status);
        Assert.Empty(_meldung.Gesendet);

        for (var i = 0; i < 2; i++) { Weiter(TimeSpan.FromSeconds(31)); await _dienst.TickAsync(default); }
        Assert.Equal(ProbelaufStatus.RueckstellungOffen, Lauf(lauf.Id).Status);
        Assert.Single(_meldung.Gesendet);          // dritter Versuch → Meldung

        _ha.Haengt.Clear();                        // das Gerät antwortet wieder
        Weiter(TimeSpan.FromSeconds(31));
        await _dienst.TickAsync(default);

        Assert.Equal(ProbelaufStatus.Nachlauf, Lauf(lauf.Id).Status);
        Assert.Equal("Auto", _ha.Zustaende[Port]);
    }

    [Fact]
    public async Task Rueckstellung_OffenBlockiertDenNaechstenStart()
    {
        await StartenAsync(10);
        _ha.Haengt.Add(Port);
        Weiter(TimeSpan.FromMinutes(10));
        await _dienst.TickAsync(default);

        var e = await _dienst.StartenAsync(new ProbelaufStart("zuluft", 10, null), default);

        Assert.Equal(ProbelaufFehler.LaeuftSchon, e.Fehler);
    }

    // ------------------------------------------------- Anlagen-Wächter (Kühler)

    private static ProbelaufLauf Lauf(string modul, ProbelaufStatus status) => new() { Modul = modul, Status = status };

    [Fact]
    public void Waechter_SchweigtBeimKuehler_SolangeDerChillerLaufImEingriffIst()
        => Assert.True(ProbelaufService.KuehlerAbsichtlichAus([Lauf("chiller", ProbelaufStatus.Laeuft)]));

    [Theory]
    [InlineData(ProbelaufStatus.Nachlauf)]            // der Kühler ist wieder an — steht er aus, ist das ein Fehler
    [InlineData(ProbelaufStatus.RueckstellungOffen)]  // nicht bestätigt zurückgestellt — genau das soll gemeldet werden
    [InlineData(ProbelaufStatus.Fertig)]
    [InlineData(ProbelaufStatus.Abgebrochen)]
    public void Waechter_MeldetWeiter_AusserImEingriff(ProbelaufStatus status)
        => Assert.False(ProbelaufService.KuehlerAbsichtlichAus([Lauf("chiller", status)]));

    [Fact]
    public void Waechter_EinLaufAnEinemAnderenGeraetEntschuldigtDenKuehlerNicht()
        => Assert.False(ProbelaufService.KuehlerAbsichtlichAus([Lauf("entfeuchter", ProbelaufStatus.Laeuft)]));
}
