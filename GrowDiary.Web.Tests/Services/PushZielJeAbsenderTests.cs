using System.Net;
using System.Text.RegularExpressions;
using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge;
using GrowDiary.Web.Tests.TestFakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Wohin der Tipp auf eine Push-Meldung führt — je <b>Absender</b>.
/// </summary>
/// <remarks>
/// <para><see cref="PushZielJeMeldungTests"/> prüft die Tabelle je Meldungsart
/// und dass ein <c>seite:</c> vorgeht. Ob ein Absender sein Ziel überhaupt
/// <i>mitgibt</i>, sieht man dort nicht: fehlt es, greift still der Rückfall
/// der Meldungsart — bei Watchdog und Dosierung „Aufgaben", bei Grenzwert,
/// Risiko und Sensorausfall „Live" ohne Zelt.</para>
/// <para>Hier läuft jeder Absender selbst, durch seinen echten Weg bis zum
/// Push an Home Assistant, und geprüft wird das <c>clickAction</c>, das dort
/// ankommt. Als Add-on (Supervisor-Token gesetzt) geht der Push an
/// <c>http://supervisor/core/api/services/notify/…</c>.</para>
/// </remarks>
public sealed class PushZielJeAbsenderTests : IDisposable
{
    private const string Slug = "d48160c2_grow_os_fork_ai";

    private readonly string _wurzel;
    private readonly AppPaths _pfade;
    private readonly GrowRepository _grows;
    private readonly NotificationSettingsRepository _meldeEinstellungen;
    private readonly Tent _zelt;
    private readonly string? _tokenVorher;

    public PushZielJeAbsenderTests()
    {
        _wurzel = Path.Combine(Path.GetTempPath(), "PushZielJeAbsender_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_wurzel);
        // Der Trend-Wächter und die Sollwerte lesen die Wissensbasis.
        KopiereWissen(Path.Combine(ProjektWurzel(), "GrowDiary.Web", "wwwroot", "knowledge-defaults"), _wurzel);

        _pfade = new AppPaths(_wurzel);
        TestDatabase.Initialize(_pfade);
        _grows = new GrowRepository(_pfade);
        _meldeEinstellungen = new NotificationSettingsRepository(_pfade);

        // Zwei Zelte: das geprüfte ist NICHT das erste. Ein Ziel „live/1" aus
        // einer falschen Quelle fiele sonst zufällig richtig aus.
        _grows.CreateTent(new Tent { Name = "Nebenzelt", TentType = TentType.Production });
        _zelt = _grows.CreateTent(new Tent { Name = "Bluetezelt", TentType = TentType.Production });
        Assert.NotEqual(1, _zelt.Id);

        // Mit Supervisor-Token: nur dann kennt Grow OS seinen Slug und setzt ein Ziel.
        _tokenVorher = Environment.GetEnvironmentVariable(HomeAssistantAddon.SupervisorTokenEnvironmentVariable);
        Environment.SetEnvironmentVariable(HomeAssistantAddon.SupervisorTokenEnvironmentVariable, "supervisor-token");

        _grows.SaveHomeAssistantSettings(new HomeAssistantSettings
        {
            BaseUrl = "http://ha.local:8123", AccessToken = "token", Enabled = true,
        });
        _meldeEinstellungen.SaveNotificationSettings(new NotificationSettings
        {
            NotifyService = "notify.mobile_app_test",
            QuietHoursStartHour = null,
            QuietHoursEndHour = null,
            DailyDigest = false,
        });
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(HomeAssistantAddon.SupervisorTokenEnvironmentVariable, _tokenVorher);
        try { Directory.Delete(_wurzel, recursive: true); } catch { /* Windows haelt manchmal fest */ }
    }

    // ------------------------------------------------------------ Watchdog

    [Fact]
    public async Task Watchdog_Warnung_fuehrt_auf_Live()
    {
        SensorZuordnen(_zelt, SensorMetricType.ReservoirPh, "sensor.ph");
        var handler = Handler();

        // Seit einer Stunde gestartet und nie eine Runde gedreht: „Überwachung steht".
        var urteil = await Watchdog(handler, new SystemHeartbeat()).CheckAndNotifyAsync(DateTime.UtcNow.AddHours(1));

        Assert.Equal(WatchdogService.WorkerStalled, urteil.Code);
        Assert.Equal($"/app/{Slug}/live", ClickZiel(EinPush(handler)));
    }

    [Fact]
    public async Task Watchdog_Entwarnung_fuehrt_auf_Live()
    {
        // Kein Sensor zugeordnet: kein Problem. Gemeldet war eins — also Entwarnung.
        var herzschlag = new SystemHeartbeat { NotifiedCode = WatchdogService.WorkerStalled };
        var handler = Handler();

        var urteil = await Watchdog(handler, herzschlag).CheckAndNotifyAsync(DateTime.UtcNow);

        Assert.False(urteil.IsProblem);
        Assert.Null(herzschlag.NotifiedCode);
        Assert.Equal($"/app/{Slug}/live", ClickZiel(EinPush(handler)));
    }

    // ----------------------------------------------------------- Dosierung

    [Fact]
    public async Task Dosierung_fuehrt_auf_Dosierung()
    {
        var jetzt = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
        var dosing = new DosingRepository(_pfade);
        var pumpe = new DosingPump
        {
            TentId = _zelt.Id, Name = "Grow A", Purpose = DosingPurpose.Nutrient,
            SimulationMode = true, MlPerMinute = 30, AutomationEnabled = true,
        };
        pumpe.Id = dosing.InsertPump(pumpe);
        // Erfahrung: drei echte Dosen, je ml +0,1 — sonst rechnet die Automatik keine Menge.
        for (var i = 1; i <= 3; i++)
        {
            dosing.InsertEvent(new DoseEvent
            {
                PumpId = pumpe.Id, TentId = _zelt.Id, OccurredAtUtc = jetzt.AddDays(-i),
                Trigger = DoseTrigger.Manual, Outcome = DoseOutcome.Done,
                RequestedMl = 1, DosedMl = 1, SecondsRun = 2, ValueBefore = 1.0, ValueAfter = 1.1,
            });
        }

        var handler = Handler();
        var funk = new HomeAssistantFunk(HomeAssistant(handler));
        var dienst = new DosingService(_grows, dosing, funk,
            new Ausschalter(funk, NullLogger<Ausschalter>.Instance),
            NullLogger<DosingService>.Instance,
            (_, _) => Task.CompletedTask);
        var takt = new DosingWorker(new ServiceCollection().BuildServiceProvider(), NullLogger<DosingWorker>.Instance);
        var lage = new DosingSituation(
            new DosingContext(
                Reading: 1.0, ReadingAge: TimeSpan.FromMinutes(1), ProbeCalibratedAtUtc: jetzt.AddDays(-1),
                ProbeCalibrationOverdue: false, DosesToday: Array.Empty<DoseEvent>(), WaterLevelOk: null),
            Target: 1.4, TargetFrom: TargetSource.User, ReadingFrom: ReadingSource.Sensor);

        var dosiert = await takt.DoseIfNeededAsync(
            dosing, dienst, Benachrichtigung(handler), dosing.GetPump(pumpe.Id)!, lage, jetzt, CancellationToken.None);

        Assert.True(dosiert, "Die Automatik hat nicht dosiert — dann prüft dieser Fall keine „hat dosiert“-Meldung.");
        var body = EinPush(handler);
        Assert.Contains("hat dosiert", body);
        Assert.Equal($"/app/{Slug}/dosierung", ClickZiel(body));
    }

    // ------------------------------------------------------ Trend-Wächter

    [Fact]
    public async Task Trendbefund_fuehrt_auf_Live_mit_dem_Zelt_des_Grows()
    {
        var grow = GrowMitEcDrift(_zelt);
        var handler = Handler();
        var wissen = Wissen();
        var laeufer = new TrendWatchRunner(
            _grows,
            new TargetValueService(wissen),
            wissen,
            Benachrichtigung(handler),
            new AppSettingsRepository(_pfade),
            NullLogger<TrendWatchRunner>.Instance);

        await laeufer.RunAsync(DateTime.Now);

        var pushes = Pushes(handler);
        Assert.True(pushes.Count > 0,
            "Der Trend-Wächter hat nichts gemeldet — dann prüft dieser Fall nichts. Trägt der Grow wirklich eine Drift?");
        Assert.All(pushes, body => Assert.Equal($"/app/{Slug}/live/{grow.TentId}", ClickZiel(body)));
    }

    // --------------------------------------------------- Licht-Wächter

    [Fact]
    public async Task Lichteinbruch_fuehrt_auf_Live_mit_dem_Zelt()
    {
        SaeeZyklus(_zelt);
        _zelt.ActiveGrows.Add(BlueteGrow(_zelt));
        var handler = Handler();
        var waechter = new LightWatchService(
            new LightCycleReader(new LightRepository(_pfade)),
            Benachrichtigung(handler),
            NullLogger<LightWatchService>.Instance);

        await waechter.CheckIntrusionAsync(_zelt, new LightTransitionEvent
        {
            TentId = _zelt.Id,
            Kind = LightTransitionKind.LightOn,
            OccurredAtUtc = DateTime.UtcNow.Date.AddHours(2),
        }, CancellationToken.None);

        Assert.Equal($"/app/{Slug}/live/{_zelt.Id}", ClickZiel(EinPush(handler)));
    }

    // ------------------------------------------- Snapshot: Sensorausfall

    [Fact]
    public async Task Sensorausfall_fuehrt_auf_Live_mit_dem_Zelt()
    {
        var (takt, zustaende, handler) = SnapshotTakt();

        zustaende["sensor.ph"] = "unavailable";
        await takt.DurchlaufAsync(Nachts(), CancellationToken.None);
        Assert.Empty(Pushes(handler)); // ein Aussetzer ist noch kein Ausfall
        await takt.DurchlaufAsync(Nachts().AddMinutes(5), CancellationToken.None);

        var body = EinPush(handler);
        Assert.Contains("keine Werte mehr", body);
        Assert.Equal($"/app/{Slug}/live/{_zelt.Id}", ClickZiel(body));
    }

    [Fact]
    public async Task Sensor_wieder_da_fuehrt_auf_Live_mit_dem_Zelt()
    {
        var (takt, zustaende, handler) = SnapshotTakt();

        zustaende["sensor.ph"] = "unavailable";
        await takt.DurchlaufAsync(Nachts(), CancellationToken.None);
        await takt.DurchlaufAsync(Nachts().AddMinutes(5), CancellationToken.None);
        zustaende["sensor.ph"] = "6.1";
        await takt.DurchlaufAsync(Nachts().AddMinutes(10), CancellationToken.None);

        var pushes = Pushes(handler);
        Assert.Equal(2, pushes.Count); // Ausfall, dann Wiederkehr
        Assert.Contains("wieder Werte", pushes[1]);
        Assert.Equal($"/app/{Slug}/live/{_zelt.Id}", ClickZiel(pushes[1]));
    }

    // ------------------------------------- Controller: Watchdog-Testmeldung

    [Fact]
    public async Task Watchdog_Testmeldung_fuehrt_auf_Live()
    {
        var handler = Handler();
        var benachrichtigung = Benachrichtigung(handler);
        var controller = new NotificationsApiController(
            _meldeEinstellungen, _grows, HomeAssistant(handler), new HardwareRepository(_pfade), benachrichtigung);

        await controller.WatchdogTest(Watchdog(handler, new SystemHeartbeat()), CancellationToken.None);

        Assert.Equal($"/app/{Slug}/live", ClickZiel(EinPush(handler)));
    }

    // ------------------------------------- Pumpen-/Kühler-Wächter: Rückfall

    /// <summary>
    /// Der Pumpen- und Kühler-Wächter gibt kein eigenes Ziel mit — es gilt die
    /// Seite der Meldungsart (System → Aufgaben, dort steht die Pumpen-Lage).
    /// </summary>
    [Fact]
    public async Task Kuehlerausfall_ohne_eigenes_Ziel_fuehrt_auf_Aufgaben()
    {
        var jetzt = DateTime.UtcNow;
        var seit = jetzt.AddMinutes(-90);
        var zustaende = new Dictionary<string, HomeAssistantState>(StringComparer.OrdinalIgnoreCase)
        {
            ["pump-circulation"] = new() { State = "on", LastChanged = seit, LastUpdated = seit },
            ["pump-air"] = new() { State = "on", LastChanged = seit, LastUpdated = seit },
            ["chiller"] = new() { State = "off", LastChanged = seit, LastUpdated = seit },
        };
        var handler = Handler();
        var waechter = new PumpWatchNotifier(
            new AppSettingsRepository(_pfade),
            Benachrichtigung(handler),
            new SystemHeartbeat(),
            new AnlagenRisikoService(_grows, NullLogger<AnlagenRisikoService>.Instance),
            NullLogger<PumpWatchNotifier>.Instance);

        await waechter.PruefenUndMeldenAsync(_zelt, zustaende, jetzt);

        Assert.Equal($"/app/{Slug}/aufgaben", ClickZiel(EinPush(handler)));
    }

    // ------------------------------------------------------------------ Hilfe

    /// <summary>
    /// Supervisor und HA in einem: <c>addons/self/info</c> nennt den Slug,
    /// <c>…/api/states/&lt;entität&gt;</c> liefert den Zustand aus
    /// <paramref name="zustaende"/>, der Rest (Push) wird angenommen.
    /// </summary>
    private static RecordingHttpHandler Handler(IReadOnlyDictionary<string, string>? zustaende = null)
        => new((anfrage, _) =>
        {
            var pfad = anfrage.RequestUri!.AbsolutePath;
            if (anfrage.RequestUri.Host == "supervisor" && pfad == "/addons/self/info")
            {
                return RecordingHttpHandler.Json($$$"""{"result":"ok","data":{"slug":"{{{Slug}}}"}}""");
            }

            if (pfad.Contains("/api/states/", StringComparison.Ordinal))
            {
                var entitaet = pfad.Split('/').Last();
                return zustaende is not null && zustaende.TryGetValue(entitaet, out var zustand)
                    ? RecordingHttpHandler.Json(RecordingHttpHandler.EntityStateJson(entitaet, zustand))
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return RecordingHttpHandler.Json("[]");
        });

    private static HomeAssistantService HomeAssistant(RecordingHttpHandler handler)
        => new(new StubHttpClientFactory(handler), NullLogger<HomeAssistantService>.Instance);

    private NotificationService Benachrichtigung(RecordingHttpHandler handler)
    {
        var fabrik = new StubHttpClientFactory(handler);
        return new NotificationService(
            _meldeEinstellungen,
            _grows,
            new HomeAssistantService(fabrik, NullLogger<HomeAssistantService>.Instance),
            NullLogger<NotificationService>.Instance,
            new SupervisorInfoService(fabrik, NullLogger<SupervisorInfoService>.Instance));
    }

    private WatchdogService Watchdog(RecordingHttpHandler handler, SystemHeartbeat herzschlag)
        => new(
            _grows,
            new SensorReadingRepository(_pfade),
            herzschlag,
            Benachrichtigung(handler),
            new AnlagenRisikoService(_grows, NullLogger<AnlagenRisikoService>.Instance),
            NullLogger<WatchdogService>.Instance);

    /// <summary>Alle Push-Aufrufe, in Reihenfolge. Mit Token gehen sie an <c>supervisor/core</c>.</summary>
    private static List<string> Pushes(RecordingHttpHandler handler)
        => handler.Requests
            .Where(r => r.Uri.AbsolutePath.Contains("/api/services/notify/", StringComparison.Ordinal))
            .Select(r =>
            {
                Assert.Equal("supervisor", r.Uri.Host);
                return r.Body!;
            })
            .ToList();

    private static string EinPush(RecordingHttpHandler handler) => Assert.Single(Pushes(handler));

    private static readonly Regex CLICK_ACTION = new("\"clickAction\":\"([^\"]*)\"", RegexOptions.CultureInvariant);

    /// <summary>Das <c>clickAction</c> aus dem Push-Body — genau, nicht als Teilstring.</summary>
    private static string ClickZiel(string body)
    {
        var treffer = CLICK_ACTION.Matches(body);
        Assert.True(treffer.Count == 1, $"Im Push steht {treffer.Count}× clickAction statt einmal: {body}");
        return treffer[0].Groups[1].Value;
    }

    private void SensorZuordnen(Tent zelt, SensorMetricType art, string entitaet)
        => _grows.AddTentSensor(new TentSensor
        {
            TentId = zelt.Id, MetricType = art, HaEntityId = entitaet, DisplayLabel = entitaet, IsActive = true,
        });

    /// <summary>01:00 Ortszeit: Tagesstatistik, Erinnerung und Tagesbericht sind nicht fällig.</summary>
    private static DateTime Nachts() => DateTime.Today.AddHours(1);

    /// <summary>
    /// Der Snapshot-Takt mit einem Dienst-Container wie im Betrieb — nur der
    /// Benachrichtigungsdienst kennt den Supervisor.
    /// </summary>
    private (HomeAssistantSnapshotWorker Takt, Dictionary<string, string> Zustaende, RecordingHttpHandler Handler) SnapshotTakt()
    {
        SensorZuordnen(_zelt, SensorMetricType.ReservoirPh, "sensor.ph");
        // Ein zweiter, gesunder Sensor: ohne einen einzigen Zustand hält der
        // Takt Home Assistant für weg und wertet gar keinen Ausfall aus.
        SensorZuordnen(_zelt, SensorMetricType.AirTemperature, "sensor.luft");

        var zustaende = new Dictionary<string, string> { ["sensor.ph"] = "6.0", ["sensor.luft"] = "24.1" };
        var handler = Handler(zustaende);
        var ha = HomeAssistant(handler);
        var wissen = Wissen();
        var tents = new TentRepository(_pfade);

        var dienste = new ServiceCollection()
            .AddSingleton(_pfade)
            .AddSingleton(_grows)
            .AddSingleton(new SensorReadingRepository(_pfade))
            .AddSingleton(ha)
            .AddSingleton(new StromKachel(new AppSettingsRepository(_pfade), ha))
            .AddSingleton(new DashboardLayoutRepository(_pfade))
            .AddSingleton(new AppSettingsRepository(_pfade))
            .AddSingleton(new LightStatusTransitionService(_grows))
            .AddSingleton(new LightWatchService(
                new LightCycleReader(new LightRepository(_pfade)), Benachrichtigung(handler), NullLogger<LightWatchService>.Instance))
            .AddSingleton(new NachtabsenkungWriter(
                _grows, new TargetValueService(wissen), ha, new SetpointProfileRepository(_pfade),
                new HydroSetupRepository(_pfade, tents), new SystemAuditRepository(_pfade),
                NullLogger<NachtabsenkungWriter>.Instance))
            .AddSingleton(Benachrichtigung(handler))
            .AddSingleton(new SystemHeartbeat())
            .AddSingleton(_meldeEinstellungen)
            .BuildServiceProvider();

        return (new HomeAssistantSnapshotWorker(dienste, new FehlerSindRot(), _pfade), zustaende, handler);
    }

    /// <summary>
    /// Ein Takt-Schritt, der scheitert, wird geloggt statt geworfen — hier soll
    /// er den Fall rot machen, sonst prüfte er einen Takt, der nie lief.
    /// </summary>
    private sealed class FehlerSindRot : ILogger<HomeAssistantSnapshotWorker>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
            {
                throw new Xunit.Sdk.XunitException($"Snapshot-Takt meldet: {formatter(state, exception)} {exception}");
            }
        }
    }

    private KnowledgeBaseLoader Wissen()
    {
        var wissen = new KnowledgeBaseLoader(_pfade, NullLogger<KnowledgeBaseLoader>.Instance);
        wissen.Initialize();
        return wissen;
    }

    /// <summary>Ein Grow, dessen EC über sieben Tage weit aus dem Band läuft.</summary>
    private GrowRun GrowMitEcDrift(Tent zelt)
    {
        var id = _grows.CreateGrow(new GrowRun
        {
            Name = "Drift", TentId = zelt.Id, HydroStyle = HydroStyle.RDWC,
            IrrigationType = IrrigationType.ActiveHydro, MediumType = MediumType.Hydro,
            Status = GrowStatus.Running,
            StartDate = DateTime.Today.AddDays(-70), FlipDate = DateTime.Today.AddDays(-35),
        });

        var messungen = new MeasurementRepository(_pfade);
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

    private GrowRun BlueteGrow(Tent zelt)
    {
        var id = _grows.CreateGrow(new GrowRun
        {
            Name = "Photoperiode", TentId = zelt.Id, HydroStyle = HydroStyle.RDWC,
            Status = GrowStatus.Running, SeedType = SeedType.Feminized,
            StartDate = DateTime.Today.AddDays(-100), FlipDate = DateTime.Today.AddDays(-42),
        });
        return _grows.GetGrow(id)!;
    }

    /// <summary>Vier Tage 12/12 (an 08:00, aus 20:00 Ortszeit) — sonst kennt der Wächter keinen Zyklus.</summary>
    private void SaeeZyklus(Tent zelt)
    {
        var lights = new LightRepository(_pfade);
        var versatz = new LightCycleReader(lights).LocalOffset(zelt.Id);
        var heute = DateTime.UtcNow.Date;

        for (var tag = 4; tag >= 1; tag -= 1)
        {
            var basis = heute.AddDays(-tag);
            foreach (var (stunde, art) in new[] { (8, LightTransitionKind.LightOn), (20, LightTransitionKind.LightOff) })
            {
                lights.CreateLightTransitionIfNotDuplicate(new LightTransitionEvent
                {
                    TentId = zelt.Id, Kind = art, OccurredAtUtc = basis.AddHours(stunde) - versatz,
                });
            }
        }
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
}
