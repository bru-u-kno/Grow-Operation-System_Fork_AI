using System.Net;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge;
using GrowDiary.Web.Tests.TestFakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (02.10.2026): Der CO₂-Tageslauf und die Wege, auf denen er Home
/// Assistant erreicht — durch den echten Dienst, mit nachgebautem HTTP.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Ein kurzes „unavailable" des Licht-Sensors schließt den Tag nicht ab.</item>
/// <item>Ein Lichtzyklus über Mitternacht bucht seine Impulse einmal, nicht doppelt.</item>
/// <item>Ein gescheitertes Mittelungsfenster wird beim nächsten Speichern erneut versucht.</item>
/// <item>Ein gescheiterter Sollwertlauf wartet keine volle Stunde.</item>
/// </list>
/// </remarks>
public sealed class Co2TageslaufTests : IDisposable
{
    private const string Licht = "binary_sensor.licht";

    private readonly string _wurzel;
    private readonly AppPaths _pfade;
    private readonly RecordingHttpHandler _ha;
    private readonly SteuerungRepository _steuerung;

    private string _lichtZustand = "on";
    private int _impulse;
    private bool _schreibenScheitert;

    public Co2TageslaufTests()
    {
        _wurzel = Path.Combine(Path.GetTempPath(), "Co2Tageslauf_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_wurzel);
        KopiereWissen(Path.Combine(ProjektWurzel(), "GrowDiary.Web", "wwwroot", "knowledge-defaults"), _wurzel);
        _pfade = new AppPaths(_wurzel);
        TestDatabase.InitializeWithDefaultTent(_pfade);
        // Port 9 (discard) ist zu: der WebSocket scheitert sofort, das HTTP läuft über den Nachbau.
        new HomeAssistantSettingsRepository(_pfade).SaveHomeAssistantSettings(new HomeAssistantSettings
        {
            BaseUrl = "http://127.0.0.1:9", AccessToken = "token", Enabled = true,
        });
        _steuerung = new SteuerungRepository(_pfade);
        _steuerung.SetGeraet(Co2SteuerungService.Modul, "licht", Licht);

        _ha = new RecordingHttpHandler((anfrage, _) =>
        {
            if (anfrage.Method == HttpMethod.Get && anfrage.RequestUri!.AbsolutePath.EndsWith("/api/states", StringComparison.Ordinal))
            {
                return RecordingHttpHandler.Json($$$"""
                    [{"entity_id":"{{{Licht}}}","state":"{{{_lichtZustand}}}","attributes":{}},
                     {"entity_id":"{{{Co2SteuerungService.Entitaeten.Impulse}}}","state":"{{{_impulse}}}","attributes":{}}]
                    """);
            }
            if (anfrage.Method == HttpMethod.Get) return new HttpResponseMessage(HttpStatusCode.NotFound);
            return _schreibenScheitert
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : RecordingHttpHandler.Json("[]");
        });
    }

    public void Dispose()
    {
        try { Directory.Delete(_wurzel, recursive: true); } catch { /* Windows haelt manchmal fest */ }
    }

    private Co2SteuerungService Dienst()
    {
        var ha = new HomeAssistantService(new StubHttpClientFactory(_ha), NullLogger<HomeAssistantService>.Instance);
        var haSettings = new HomeAssistantSettingsRepository(_pfade);
        var grows = new GrowRepository(_pfade);
        var wissen = new KnowledgeBaseLoader(_pfade, NullLogger<KnowledgeBaseLoader>.Instance);
        wissen.Initialize();
        return new Co2SteuerungService(
            _steuerung, new KostenRepository(_pfade), new JournalRepository(_pfade), grows,
            new HydroSetupRepository(_pfade, new TentRepository(_pfade)), new TargetValueService(wissen), wissen,
            ha, haSettings, new SteuerungGeraeteService(_steuerung),
            new WochenplanSyncService(grows, wissen, _steuerung, new AlertRuleRepository(_pfade), ha, haSettings,
                NullLogger<WochenplanSyncService>.Instance),
            new SteuerungMittelwertService(ha, haSettings, NullLogger<SteuerungMittelwertService>.Instance),
            NullLogger<Co2SteuerungService>.Instance);
    }

    private static string Datum(int tageZurueck)
        => DateTime.Now.AddDays(-tageZurueck).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private Co2Tag OffenerTag(int tageZurueck, int impulse)
    {
        var tag = new Co2Tag { Datum = Datum(tageZurueck), Impulse = impulse };
        tag.Id = _steuerung.CreateCo2Tag(tag);
        return tag;
    }

    // ---------- Licht unbekannt ----------

    [Theory]
    [InlineData("unavailable")]
    [InlineData("unknown")]
    public async Task LichtSensorKurzWeg_SchliesstDenTagNichtAb(string zustand)
    {
        OffenerTag(0, impulse: 10);
        _lichtZustand = zustand;
        _impulse = 10;

        await Dienst().TaktAsync(CancellationToken.None);

        var tag = _steuerung.GetCo2Tag(Datum(0))!;
        Assert.False(tag.Abgeschlossen, $"Ein „{zustand}\" des Licht-Sensors hat den CO₂-Tag abgeschlossen.");
    }

    [Fact]
    public async Task Selbsttest_LichtAusSchliesstDenTagAb()
    {
        OffenerTag(0, impulse: 10);
        _lichtZustand = "off";

        await Dienst().TaktAsync(CancellationToken.None);

        Assert.True(_steuerung.GetCo2Tag(Datum(0))!.Abgeschlossen);
    }

    // ---------- Lichtzyklus über Mitternacht ----------

    [Fact]
    public async Task LichtUeberMitternacht_BuchtDieImpulseNichtDoppelt()
    {
        // Licht seit gestern 18:00 an, 30 Impulse bis Mitternacht gebucht. Der
        // Zähler wird erst beim nächsten Licht-an zurückgesetzt und steht jetzt bei 45.
        OffenerTag(1, impulse: 30);
        _lichtZustand = "on";
        _impulse = 45;

        await Dienst().TaktAsync(CancellationToken.None);

        Assert.Null(_steuerung.GetCo2Tag(Datum(0)));
        var gestern = _steuerung.GetCo2Tag(Datum(1))!;
        Assert.Equal(45, gestern.Impulse);
        Assert.False(gestern.Abgeschlossen);
        Assert.Equal(45, _steuerung.GetCo2Tage(10).Sum(t => t.Impulse));
    }

    [Fact]
    public async Task ZaehlerZurueckgesetzt_SchliesstDenAltenZyklusUndBeginntEinenNeuen()
    {
        // Das Add-on hat das Licht-aus verschlafen; beim Licht-an hat Home
        // Assistant den Zähler zurückgesetzt. Der alte Tag gehört nicht mehr dazu.
        OffenerTag(1, impulse: 30);
        _lichtZustand = "on";
        _impulse = 3;

        await Dienst().TaktAsync(CancellationToken.None);

        var gestern = _steuerung.GetCo2Tag(Datum(1))!;
        Assert.True(gestern.Abgeschlossen);
        Assert.Equal(30, gestern.Impulse);
        Assert.Equal(3, _steuerung.GetCo2Tag(Datum(0))!.Impulse);
    }

    [Theory]
    [InlineData(1, 30, 45, true)]
    [InlineData(1, 30, 30, true)]
    [InlineData(1, 30, 3, false)]   // zurückgesetzt
    [InlineData(2, 30, 45, false)]  // ein Zyklus ist nie länger als ein Tag
    public void ZyklusFortsetzen_Faelle(int tageZurueck, int gebucht, int zaehler, bool erwartet)
    {
        var offen = new Co2Tag { Datum = Datum(tageZurueck), Impulse = gebucht };
        Assert.Equal(erwartet, Co2SteuerungService.ZyklusFortsetzen(offen, Datum(0), zaehler));
    }

    // ---------- Mittelungsfenster ----------

    [Fact]
    public async Task GescheitertesMittelungsfenster_WirdBeimNaechstenSpeichernErneutVersucht()
    {
        _steuerung.SetEinstellungen(Co2SteuerungService.Modul, new Co2Einstellungen { RhMittelMinuten = 5 });
        var dienst = Dienst();

        // Der WebSocket ist nicht erreichbar — das Fenster lässt sich nicht setzen.
        var (_, _, erstes) = await dienst.SpeichernAsync(new Co2Einstellungen { RhMittelMinuten = 10 }, CancellationToken.None);
        Assert.False(erstes);
        Assert.Equal(5, _steuerung.GetEinstellungen<Co2Einstellungen>(Co2SteuerungService.Modul)!.RhMittelMinuten);

        // Dasselbe noch einmal: es wird wieder versucht (und scheitert wieder) —
        // vorher galt der Wert als gesetzt, und das Speichern meldete Erfolg.
        var (_, _, zweites) = await dienst.SpeichernAsync(new Co2Einstellungen { RhMittelMinuten = 10 }, CancellationToken.None);
        Assert.False(zweites, "Das zweite Speichern hat das Fenster nicht erneut versucht.");
    }

    [Fact]
    public async Task Selbsttest_OhneFensteraenderungMeldetDasSpeichernErfolg()
    {
        _steuerung.SetEinstellungen(Co2SteuerungService.Modul, new Co2Einstellungen { RhMittelMinuten = 5 });

        var (_, _, erreicht) = await Dienst().SpeichernAsync(new Co2Einstellungen { RhMittelMinuten = 5 }, CancellationToken.None);

        Assert.True(erreicht, "Ohne Fensteränderung muss das Speichern gelingen — sonst prüft der Fall oben nichts.");
    }

    // ---------- Sollwertlauf des Workers ----------

    private int Sollwertaufrufe()
        => _ha.Requests.Count(r => r.Method == HttpMethod.Post
            && r.Uri.AbsolutePath.Contains("/api/services/input_number/", StringComparison.Ordinal));

    private (Co2SyncWorker Worker, Action<TimeSpan> Vorstellen) Worker()
    {
        var jetzt = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
        var dienste = new ServiceCollection()
            .AddScoped(_ => Dienst())
            .AddScoped(_ => new SteuerungRepository(_pfade))
            .BuildServiceProvider();
        var worker = new Co2SyncWorker(dienste, NullLogger<Co2SyncWorker>.Instance) { JetztUtc = () => jetzt };
        return (worker, dauer => jetzt += dauer);
    }

    [Fact]
    public async Task GescheiterterSollwertlauf_WirdNachZehnMinutenWiederholt()
    {
        _steuerung.SetEinstellungen(Co2SteuerungService.Modul, new Co2Einstellungen());
        var (worker, vorstellen) = Worker();

        _schreibenScheitert = true;
        await worker.EinmalAsync(CancellationToken.None);
        var nachErstem = Sollwertaufrufe();
        Assert.True(nachErstem > 0, "Selbsttest: der erste Lauf hat nichts geschrieben.");

        _schreibenScheitert = false;
        vorstellen(TimeSpan.FromMinutes(11));
        await worker.EinmalAsync(CancellationToken.None);

        Assert.True(Sollwertaufrufe() > nachErstem, "Nach einem Fehlschlag wartete der Sollwertlauf eine volle Stunde.");
    }

    [Fact]
    public async Task Selbsttest_NachGelungenemLaufWartetErEineStunde()
    {
        _steuerung.SetEinstellungen(Co2SteuerungService.Modul, new Co2Einstellungen());
        var (worker, vorstellen) = Worker();

        await worker.EinmalAsync(CancellationToken.None);
        var nachErstem = Sollwertaufrufe();
        Assert.True(nachErstem > 0);

        vorstellen(TimeSpan.FromMinutes(11));
        await worker.EinmalAsync(CancellationToken.None);
        Assert.Equal(nachErstem, Sollwertaufrufe());

        vorstellen(TimeSpan.FromMinutes(50));
        await worker.EinmalAsync(CancellationToken.None);
        Assert.True(Sollwertaufrufe() > nachErstem);
    }

    // ---------- Hilfen ----------

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

    private static void KopiereWissen(string quelle, string ziel)
    {
        var nach = Path.Combine(ziel, "wwwroot", "knowledge-defaults");
        foreach (var datei in Directory.EnumerateFiles(quelle, "*.json", SearchOption.AllDirectories))
        {
            var pfad = Path.Combine(nach, Path.GetRelativePath(quelle, datei));
            Directory.CreateDirectory(Path.GetDirectoryName(pfad)!);
            File.Copy(datei, pfad);
        }
    }
}
