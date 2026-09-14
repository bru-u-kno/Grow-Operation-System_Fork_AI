using System.Net;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge;
using GrowDiary.Web.Tests.TestFakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Gemerkt wird erst, wenn es wirklich rausgegangen ist.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (01.09.2026).</b> Dieselbe Form an drei Stellen: die
/// Merkstelle wird geschrieben, <b>bevor</b> die Meldung raus ist — und das
/// Ergebnis des Sendens wird gar nicht angesehen. Im
/// <c>PumpWatchNotifier</c> war es heute schon behoben; der
/// <c>TrendWatchRunner</c> hat die Korrektur nie bekommen.</para>
///
/// <para><b>Was das kostet.</b> Der Urlaubswächter läuft im Minutentakt. Kippt
/// der EC um 23:10 über das Band und steht die Ruhezeit auf 22–07, gibt
/// <c>SendAsync</c> <c>false</c> zurück und sendet nichts — der Befund landet
/// trotzdem in der Merkstelle. Beim nächsten Lauf gilt er als „schon
/// gemeldet", und weil er sich nicht ändert, kommt der Push <b>nie</b>. Der
/// Wächter, der genau für die Abwesenheit gebaut ist, schweigt.</para>
/// </remarks>
public sealed class GemerktWirdErstNachDemSendenTests : IDisposable
{
    private readonly string _wurzel;
    private readonly AppPaths _pfade;
    private readonly GrowRepository _grows;

    public GemerktWirdErstNachDemSendenTests()
    {
        _wurzel = Path.Combine(Path.GetTempPath(), "Gemerkt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_wurzel);
        KopiereWissen(Path.Combine(ProjektWurzel(), "GrowDiary.Web", "wwwroot", "knowledge-defaults"), _wurzel);

        _pfade = new AppPaths(_wurzel);
        TestDatabase.Initialize(_pfade);
        _grows = new GrowRepository(_pfade);

        _grows.SaveHomeAssistantSettings(new HomeAssistantSettings
        {
            BaseUrl = "http://ha.local:8123", AccessToken = "token", Enabled = true,
        });
        new NotificationSettingsRepository(_pfade).SaveNotificationSettings(
            new NotificationSettings { NotifyService = "notify.mobile_app_test" });
    }

    public void Dispose()
    {
        try { Directory.Delete(_wurzel, recursive: true); } catch { /* Windows haelt manchmal fest */ }
    }

    /// <summary>
    /// Scheitert der Push, wird beim nächsten Lauf erneut gesendet.
    /// </summary>
    /// <remarks>
    /// Nachgestellt mit einem Home Assistant, der zuerst nicht antwortet
    /// (HTTP 503) und danach wieder — dieselbe Lage, dieselbe Drift.
    /// </remarks>
    [Fact]
    public async Task EinGescheiterterTrendPush_WirdBeimNaechstenLaufWiederholt()
    {
        var grow = GrowMitEcDrift();

        var antwortet = false;
        var handler = new RecordingHttpHandler((_, _) => new HttpResponseMessage(
            antwortet ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable));
        var laeufer = Laeufer(handler);

        var jetzt = DateTime.Now;
        await laeufer.RunAsync(jetzt);
        var beimAusfall = handler.Requests.Count;

        antwortet = true;
        await laeufer.RunAsync(jetzt.AddMinutes(1));

        // Mengenwaechter: ohne einen Versuch im ersten Lauf sagt der Vergleich nichts.
        Assert.True(beimAusfall > 0,
            "Im ersten Lauf ging gar keine Anfrage raus — dann findet der Waechter den Befund "
            + "nicht, und dieser Fall prueft nichts. Traegt der Grow wirklich eine Drift?");
        Assert.True(handler.Requests.Count > beimAusfall,
            "Der erste Push scheiterte (HTTP 503), der zweite haette gehen muessen — es ging "
            + "nichts raus. Der Befund wurde als gemeldet vermerkt, obwohl er nie ankam: "
            + "waehrend der Ruhezeit oder bei einem neu startenden Home Assistant schweigt "
            + "der Urlaubswaechter danach dauerhaft.");
    }

    /// <summary>
    /// Und zweimal derselbe Befund ergibt nicht zwei Meldungen.
    /// </summary>
    /// <remarks>
    /// Die Gegenrichtung: eine Entprellung, die nach der Reparatur gar nicht
    /// mehr greift, wäre eine Push-Nachricht pro Minute — und dann stellt der
    /// Betreiber die Benachrichtigungen ab.
    /// </remarks>
    [Fact]
    public async Task DerselbeBefund_MeldetSichNichtJedeMinuteNeu()
    {
        GrowMitEcDrift();

        var handler = new RecordingHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        var laeufer = Laeufer(handler);

        var jetzt = DateTime.Now;
        await laeufer.RunAsync(jetzt);
        var nachErstem = handler.Requests.Count;

        await laeufer.RunAsync(jetzt.AddMinutes(1));
        await laeufer.RunAsync(jetzt.AddMinutes(2));

        Assert.True(nachErstem > 0,
            "Der erste Lauf hat gar nicht gemeldet — dann sagt der Vergleich unten nichts.");
        Assert.True(handler.Requests.Count == nachErstem,
            $"Nach drei Laeufen mit derselben Lage gingen {handler.Requests.Count} Anfragen "
            + $"raus statt {nachErstem}. Eine Warnung, die sich jede Minute wiederholt, wird "
            + "abgestellt.");
    }

    // ------------------------------------------------------------------ Hilfe

    /// <summary>Ein Grow, dessen EC über Tage aus dem Band läuft.</summary>
    /// <remarks>
    /// Der Trend-Wächter braucht eine Reihe über mehrere Tage; eine einzelne
    /// Messung ergibt keine Drift.
    /// </remarks>
    private GrowRun GrowMitEcDrift()
    {
        var zelt = _grows.CreateTent(new Tent { Name = "Zelt", TentType = TentType.Production });
        var id = _grows.CreateGrow(new GrowRun
        {
            Name = "Drift", TentId = zelt.Id, HydroStyle = HydroStyle.RDWC,
            IrrigationType = IrrigationType.ActiveHydro, MediumType = MediumType.Hydro,
            Status = GrowStatus.Running,
            StartDate = DateTime.Today.AddDays(-70), FlipDate = DateTime.Today.AddDays(-35),
        });

        var messungen = new MeasurementRepository(_pfade);
        // Sieben Tage, EC steigt weit ueber jedes Band.
        for (var tag = 6; tag >= 0; tag -= 1)
        {
            messungen.CreateMeasurement(new Measurement
            {
                GrowId = id,
                TakenAt = DateTime.Now.AddDays(-tag),
                Stage = GrowStage.Flower,
                ReservoirEc = 1.0 + (6 - tag) * 0.35,
                ReservoirPh = 6.0,
            });
        }

        return _grows.GetGrow(id)!;
    }

    private TrendWatchRunner Laeufer(RecordingHttpHandler handler)
    {
        var wissen = new KnowledgeBaseLoader(_pfade, NullLogger<KnowledgeBaseLoader>.Instance);
        wissen.Initialize();

        return new TrendWatchRunner(
            _grows,
            new TargetValueService(wissen),
            wissen,
            new NotificationService(
                new NotificationSettingsRepository(_pfade), _grows,
                new HomeAssistantService(
                    new StubHttpClientFactory(handler), NullLogger<HomeAssistantService>.Instance),
                NullLogger<NotificationService>.Instance),
            new AppSettingsRepository(_pfade),
            NullLogger<TrendWatchRunner>.Instance);
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
            var rel = Path.GetRelativePath(quelle, datei);
            var pfad = Path.Combine(nach, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(pfad)!);
            File.Copy(datei, pfad);
        }
    }
}
