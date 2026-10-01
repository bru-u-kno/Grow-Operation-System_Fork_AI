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
/// Wer die CO₂-Automation in Home Assistant ausschaltet, bekommt sie nicht
/// ungefragt zurück.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (29.09.2026).</b> Der stündliche Abgleich schrieb neben
/// den Sollwerten auch <c>automation.turn_on</c>. Ein Not-Aus in Home Assistant
/// — Ventil klemmt, Arbeit im Zelt, Flaschentausch — hielt so höchstens eine
/// Stunde. Dasselbe beim Speichern der CO₂-Seite: sie kennt den Zustand der
/// Automation nicht, und jedes Speichern eines ppm-Werts schaltete sie mit
/// ein.</para>
/// </remarks>
public sealed class Co2NotAusBleibtTests : IDisposable
{
    // ---------- Der echte Weg: Worker → Dienst → HTTP an Home Assistant ----------
    //
    // Die Listen-Tests unten prüfen nur die Zutaten. Der Prüfer hat am
    // 29.09.2026 gezeigt, dass sie grün bleiben, wenn der Worker wieder den
    // alten Aufruf nimmt — genau der eigentliche Fehler. Diese Tests laufen
    // durch den echten Dienst und zählen, was bei Home Assistant ankommt.

    private readonly string _wurzel;
    private readonly AppPaths _pfade;
    private readonly RecordingHttpHandler _ha;

    /// <summary>Was <c>GET /api/states</c> liefert — ohne Angabe eine leere Liste.</summary>
    private string _zustaende = "[]";

    public Co2NotAusBleibtTests()
    {
        _wurzel = Path.Combine(Path.GetTempPath(), "Co2NotAus_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_wurzel);
        KopiereWissen(Path.Combine(ProjektWurzel(), "GrowDiary.Web", "wwwroot", "knowledge-defaults"), _wurzel);
        _pfade = new AppPaths(_wurzel);
        TestDatabase.InitializeWithDefaultTent(_pfade);
        new HomeAssistantSettingsRepository(_pfade).SaveHomeAssistantSettings(new HomeAssistantSettings
        {
            BaseUrl = "http://ha.local:8123", AccessToken = "token", Enabled = true,
        });
        _ha = new RecordingHttpHandler((anfrage, _) => anfrage.Method == HttpMethod.Get
            ? RecordingHttpHandler.Json(anfrage.RequestUri!.AbsolutePath.EndsWith("/api/states") ? _zustaende : "{}",
                anfrage.RequestUri!.AbsolutePath.EndsWith("/api/states") ? HttpStatusCode.OK : HttpStatusCode.NotFound)
            : RecordingHttpHandler.Json("[]"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_wurzel, recursive: true); } catch { /* Windows haelt manchmal fest */ }
    }

    private Co2SteuerungService Dienst()
    {
        var ha = new HomeAssistantService(new StubHttpClientFactory(_ha), NullLogger<HomeAssistantService>.Instance);
        var haSettings = new HomeAssistantSettingsRepository(_pfade);
        var steuerung = new SteuerungRepository(_pfade);
        var grows = new GrowRepository(_pfade);
        var wissen = new KnowledgeBaseLoader(_pfade, NullLogger<KnowledgeBaseLoader>.Instance);
        wissen.Initialize();
        return new Co2SteuerungService(
            steuerung, new KostenRepository(_pfade), new JournalRepository(_pfade), grows,
            new HydroSetupRepository(_pfade, new TentRepository(_pfade)), new TargetValueService(wissen), wissen,
            ha, haSettings, new SteuerungGeraeteService(steuerung),
            new WochenplanSyncService(grows, wissen, steuerung, new AlertRuleRepository(_pfade), ha, haSettings,
                NullLogger<WochenplanSyncService>.Instance),
            new SteuerungMittelwertService(ha, haSettings, NullLogger<SteuerungMittelwertService>.Instance),
            NullLogger<Co2SteuerungService>.Instance);
    }

    private int AutomationsAufrufe()
        => _ha.Requests.Count(r => r.Method == HttpMethod.Post
            && r.Uri.AbsolutePath.Contains("/api/services/automation/", StringComparison.Ordinal));

    private int SchalterAufrufe()
        => _ha.Requests.Count(r => r.Method == HttpMethod.Post
            && r.Uri.AbsolutePath.Contains("/api/services/input_boolean/", StringComparison.Ordinal));

    [Fact]
    public async Task Worker_StundenlaufSchaltetDieAutomationNicht()
    {
        new SteuerungRepository(_pfade).SetEinstellungen(Co2SteuerungService.Modul, Einstellungen(automatik: true));
        var dienste = new ServiceCollection()
            .AddScoped(_ => Dienst())
            .AddScoped(_ => new SteuerungRepository(_pfade))
            .BuildServiceProvider();
        var worker = new Co2SyncWorker(dienste, NullLogger<Co2SyncWorker>.Instance);

        await worker.EinmalAsync(CancellationToken.None);

        // Selbsttest: der Stundenlauf lief wirklich und schrieb die Schalter-Helfer.
        Assert.True(SchalterAufrufe() >= 2, "Der Stundenlauf hat nichts geschrieben — der Test sieht ihn nicht.");
        Assert.Equal(0, AutomationsAufrufe());
    }

    [Fact]
    public async Task SpeichernOhneUmlegen_SchaltetDieAutomationNicht()
    {
        var dienst = Dienst();
        new SteuerungRepository(_pfade).SetEinstellungen(Co2SteuerungService.Modul, Einstellungen(automatik: true));

        var neu = Einstellungen(automatik: true);
        await dienst.SpeichernAsync(neu, CancellationToken.None);

        Assert.True(SchalterAufrufe() >= 2, "Speichern hat nichts geschrieben — der Test sieht es nicht.");
        Assert.Equal(0, AutomationsAufrufe());
    }

    [Fact]
    public async Task SpeichernMitUmlegen_SchaltetDieAutomation()
    {
        var dienst = Dienst();
        new SteuerungRepository(_pfade).SetEinstellungen(Co2SteuerungService.Modul, Einstellungen(automatik: true));

        await dienst.SpeichernAsync(Einstellungen(automatik: false), CancellationToken.None);

        Assert.Equal(1, AutomationsAufrufe());
    }

    // ---------- Fork AI (01.10.2026): die Automation dort schalten, wo sie steht ----------

    private static string Automation(string entity, string kennung, string zustand = "on")
        => $$$"""{"entity_id":"{{{entity}}}","state":"{{{zustand}}}","attributes":{"id":"{{{kennung}}}","friendly_name":"{{{entity}}}"}}""";

    private const string Fuehler = """{"entity_id":"sensor.co2","state":"800","attributes":{}}""";

    private List<string> GeschalteteAutomationen()
        => _ha.Requests
            .Where(r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath.Contains("/api/services/automation/", StringComparison.Ordinal))
            .Select(r => System.Text.Json.JsonDocument.Parse(r.Body!).RootElement.GetProperty("entity_id").GetString()!)
            .ToList();

    [Fact]
    public async Task SpeichernMitUmlegen_SchaltetDieVomForkAngelegteDosierung()
    {
        // Home Assistant leitet die Entity-ID aus dem Alias der Vorlage ab.
        _zustaende = $"[{Fuehler},{Automation("automation.co2_dosierung", "fork_ai_co2_dosierung")}]";
        var dienst = Dienst();
        new SteuerungRepository(_pfade).SetEinstellungen(Co2SteuerungService.Modul, Einstellungen(automatik: true));

        var (_, _, erreicht) = await dienst.SpeichernAsync(Einstellungen(automatik: false), CancellationToken.None);

        Assert.Equal(["automation.co2_dosierung"], GeschalteteAutomationen());
        Assert.True(erreicht);
    }

    [Fact]
    public async Task SpeichernMitUmlegen_DieHandgebauteDosierungBleibtErreichbar()
    {
        _zustaende = $"[{Fuehler},{Automation(Co2SteuerungService.Entitaeten.Automatik, "1788411041559")}]";
        var dienst = Dienst();
        new SteuerungRepository(_pfade).SetEinstellungen(Co2SteuerungService.Modul, Einstellungen(automatik: true));

        await dienst.SpeichernAsync(Einstellungen(automatik: false), CancellationToken.None);

        Assert.Equal([Co2SteuerungService.Entitaeten.Automatik], GeschalteteAutomationen());
    }

    [Fact]
    public async Task SpeichernMitUmlegen_OhneDosierungMeldetEsKeinenErfolg()
    {
        // Vorher: turn_off an eine Entität, die es nicht gibt — Home Assistant
        // antwortet mit 200, die Seite meldete „geschrieben".
        _zustaende = $"[{Fuehler}]";
        var dienst = Dienst();
        new SteuerungRepository(_pfade).SetEinstellungen(Co2SteuerungService.Modul, Einstellungen(automatik: true));

        var (gespeichert, _, erreicht) = await dienst.SpeichernAsync(Einstellungen(automatik: false), CancellationToken.None);

        Assert.NotNull(gespeichert);
        Assert.Empty(GeschalteteAutomationen());
        Assert.False(erreicht);
        Assert.True(SchalterAufrufe() >= 2, "Speichern hat nichts geschrieben — der Test sieht es nicht.");
    }

    [Fact]
    public async Task Livebild_ZeigtDenZustandDerVomForkAngelegtenDosierung()
    {
        _zustaende = $"[{Fuehler},{Automation("automation.co2_dosierung", "fork_ai_co2_dosierung", "off")}]";

        var live = await Dienst().LiveAsync(CancellationToken.None);

        Assert.True(live.HaErreichbar);
        Assert.False(live.AutomatikAn);
    }

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

    // ---------- Die Zutaten ----------

    private static Co2Einstellungen Einstellungen(bool automatik = true) => new() { AutomatikAktiv = automatik };

    [Fact]
    public void StuendlicherAbgleich_FasstDieAutomationNichtAn()
    {
        var liste = Co2SteuerungService.Schalterliste(Einstellungen(), mitAutomatik: false);

        Assert.DoesNotContain(liste, s => s.Domain == "automation");
        Assert.DoesNotContain(liste, s => s.Entity == Co2SteuerungService.Entitaeten.Automatik);
    }

    [Fact]
    public void Selbsttest_MitAutomatikSchaltetDieListeSieWirklich()
    {
        var an = Co2SteuerungService.Schalterliste(Einstellungen(automatik: true), mitAutomatik: true);
        var aus = Co2SteuerungService.Schalterliste(Einstellungen(automatik: false), mitAutomatik: true);

        Assert.Contains(("automation", "turn_on", Co2SteuerungService.Entitaeten.Automatik), an);
        Assert.Contains(("automation", "turn_off", Co2SteuerungService.Entitaeten.Automatik), aus);
    }

    [Fact]
    public void StuendlicherAbgleich_SchreibtDieUebrigenSchalterWeiter()
    {
        var liste = Co2SteuerungService.Schalterliste(Einstellungen(), mitAutomatik: false);

        Assert.Contains(liste, s => s.Entity == Co2SteuerungService.Entitaeten.Autokalibrierung);
        Assert.Contains(liste, s => s.Entity == Co2SteuerungService.Entitaeten.AbluftDrosseln);
    }

    [Theory]
    [InlineData(true, true, false)]   // ppm geändert, Schalter nicht → Automation bleibt, wie sie in HA ist
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]   // Schalter umgelegt → schalten
    [InlineData(true, false, true)]
    public void Speichern_SchaltetDieAutomationNurWennDerSchalterUmgelegtWurde(bool vorher, bool neu, bool schalten)
    {
        Assert.Equal(schalten, Co2SteuerungService.AutomatikSchalten(Einstellungen(vorher), Einstellungen(neu)));
    }

    [Fact]
    public void ErstesSpeichern_SchaltetDieAutomation()
    {
        Assert.True(Co2SteuerungService.AutomatikSchalten(null, Einstellungen()));
    }
}
