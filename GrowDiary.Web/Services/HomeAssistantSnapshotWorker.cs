using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

public sealed class HomeAssistantSnapshotWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HomeAssistantSnapshotWorker> _logger;
    private readonly AppPaths _paths;

    // Kamera-Snapshot: einmal täglich nach 12:00
    private readonly Dictionary<int, DateOnly> _lastCameraCaptureDateByTent = new();
    private DateOnly? _lastAggregationDateLocal;

    // Sensor-Ausfall (In-Memory, Edge-getriggert) + Kalibrier-Erinnerung (einmal täglich)
    private readonly SensorOfflineTracker _offlineTracker = new();
    private DateOnly? _lastCalibrationCheckDateLocal;
    private DateOnly? _lastDigestDateLocal;

    public HomeAssistantSnapshotWorker(
        IServiceProvider serviceProvider,
        ILogger<HomeAssistantSnapshotWorker> logger,
        AppPaths paths)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _paths = paths;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DurchlaufAsync(DateTime.Now, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>Ein Takt: erfassen, dann die täglichen Arbeiten, wenn sie fällig sind.</summary>
    /// <remarks>
    /// <para><b>Jeder Schritt für sich abgesichert (29.09.2026).</b> Vorher lief
    /// die Schleife ohne <c>try/catch</c>. Eine Ausnahme — eine gesperrte
    /// SQLite-Datei während einer Sicherung reicht — beendete nach der Vorgabe
    /// von .NET (<c>BackgroundServiceExceptionBehavior.StopHost</c>) das ganze
    /// Add-on. Mit ihm fielen Alarme, Wächter und Dosier-Riegel. Jetzt scheitert
    /// ein Schritt allein und wird im nächsten Takt wieder versucht; die übrigen
    /// laufen weiter.</para>
    /// </remarks>
    public async Task DurchlaufAsync(DateTime now, CancellationToken stoppingToken)
    {
        await SicherAsync("Messwerte erfassen", () => CaptureReadingsAsync(stoppingToken), stoppingToken);

        var today = DateOnly.FromDateTime(now);
        // Ab 02:00, nicht NUR 02:00–02:05: der Takt ist 5 Minuten PLUS
        // Capture-Laufzeit. Ein zaehes HA um 01:59 haette das enge Fenster
        // uebersprungen — und weil die Rohdaten nach 7 Tagen aufgeraeumt
        // werden, waere der Vortag irgendwann unwiederbringlich ohne
        // Tagesstatistik geblieben.
        // Erledigt erst nach Erfolg: die Tagesstatistik schreibt per
        // ON CONFLICT … DO UPDATE, ein zweiter Versuch ist harmlos.
        if (now.Hour >= 2 && _lastAggregationDateLocal != today)
        {
            // A-006: Spruenge fuer das Grow-Tagebuch festhalten, BEVOR die
            // Rohwerte geloescht werden — danach ist eine Stufe von 25 Minuten
            // in Min/Median/Max nicht mehr zu sehen. Eigener Schritt: scheitert
            // er, laeuft das Aufraeumen trotzdem (sonst wuechse die Datenbank).
            await SicherAsync("Tagebuch-Spruenge", () => SpruengeMerkenAsync(), stoppingToken);
            var ok = await SicherAsync("Tagesstatistik", async () =>
            {
                await AggregateYesterdayAsync(stoppingToken);
                await CleanupOldReadingsAsync();
            }, stoppingToken);
            if (ok) _lastAggregationDateLocal = today;
        }

        // Kalibrier-/Wartungs-Erinnerung: einmal täglich am Vormittag (nicht mitten in der Nacht).
        // Auch nach einem Fehler erledigt — sonst kämen Erinnerungen, die schon
        // raus sind, alle fünf Minuten erneut.
        if (now.Hour >= 8 && _lastCalibrationCheckDateLocal != today)
        {
            await SicherAsync("Kalibrier-Erinnerung", () => RunCalibrationReminderAsync(stoppingToken), stoppingToken);
            _lastCalibrationCheckDateLocal = today;
        }

        // Täglicher Digest zur eingestellten Uhrzeit.
        if (_lastDigestDateLocal != today)
        {
            await SicherAsync("Tagesbericht", () => RunDigestIfDueAsync(now, today, stoppingToken), stoppingToken);
        }
    }

    /// <summary>Führt einen Schritt aus; eine Ausnahme wird geloggt statt den Worker zu beenden.</summary>
    /// <returns><c>true</c>, wenn der Schritt ohne Ausnahme durchlief.</returns>
    private async Task<bool> SicherAsync(string schritt, Func<Task> arbeit, CancellationToken stoppingToken)
    {
        try
        {
            await arbeit();
            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Snapshot-Takt: {Schritt} fehlgeschlagen — nächster Versuch im nächsten Takt.", schritt);
            return false;
        }
    }

    private async Task CaptureReadingsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var repository  = scope.ServiceProvider.GetRequiredService<GrowRepository>();
        var sensorRepo  = scope.ServiceProvider.GetRequiredService<SensorReadingRepository>();
        var haService   = scope.ServiceProvider.GetRequiredService<HomeAssistantService>();
        var lightStatus = scope.ServiceProvider.GetRequiredService<LightStatusTransitionService>();
        var lightWatch = scope.ServiceProvider.GetRequiredService<LightWatchService>();
        var nachtabsenkung = scope.ServiceProvider.GetRequiredService<NachtabsenkungWriter>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();
        var heartbeat   = scope.ServiceProvider.GetRequiredService<SystemHeartbeat>();
        var strom       = scope.ServiceProvider.GetRequiredService<StromKachel>();

        var settings = repository.GetEffectiveHomeAssistantSettings();
        // The worker is alive either way — record the round even when HA is unconfigured,
        // so the watchdog does not mistake "nothing to do" for "stalled".
        heartbeat.MarkSnapshotRun(DateTime.UtcNow);

        // Die Strom-Kachel kommt in jedes eigene Layout — unabhängig davon, ob Home Assistant gerade antwortet.
        try
        {
            var gelegt = StromKachel.AnbietenFuerAlleZelte(
                repository,
                scope.ServiceProvider.GetRequiredService<DashboardLayoutRepository>(),
                scope.ServiceProvider.GetRequiredService<AppSettingsRepository>());
            if (gelegt > 0) _logger.LogInformation("Strom-Kachel in {Anzahl} eigene Layouts gelegt.", gelegt);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Strom-Kachel konnte nicht ins eigene Layout gelegt werden.");
        }

        if (!settings.IsConfigured) return;

        var tents = repository.GetTents();
        foreach (var tent in tents)
        {
            try
            {
                var states      = await haService.GetStatesAsync(settings, tent, cancellationToken);
                // Die Leistung der Steckdose läuft als Messgröße „power" mit: ohne diese Zeile
                // hätte die Strom-Kachel keinen Verlauf, weil nichts in die Rohwerte käme.
                await strom.ErgaenzenAsync(tent.Id, states, settings, cancellationToken);
                var capturedAt  = DateTime.UtcNow;
                heartbeat.MarkHomeAssistantSuccess(capturedAt);

                foreach (var reading in Rohwerte(tent.Id, states, capturedAt))
                {
                    sensorRepo.AddReading(reading);
                }

                // Kamera-Snapshot täglich nach 12:00 Uhr
                if (states.TryGetValue(TentSensorMetricKeyMap.Resolve(SensorMetricType.LightStatus), out var lightState))
                {
                    var flanke = lightStatus.Process(tent.Id, lightState, capturedAt);

                    // Licht AN mitten in der Dunkelphase der Blüte: das kostet
                    // die Ernte (Rückwuchs oder Zwitter) und faellt sonst erst
                    // Wochen spaeter auf. Die Flanke lag hier schon immer vor —
                    // gelesen hat sie nur nie jemand.
                    if (flanke is not null)
                    {
                        await lightWatch.CheckIntrusionAsync(tent, flanke, cancellationToken);

                        // Die Nachtabsenkung haengt sich an dieselbe Flanke: bei
                        // Licht an der Tagwert, bei Licht aus der Nachtwert.
                        // Zweimal am Tag ein Sollwert — mehr macht Grow OS hier
                        // nicht, geregelt wird in Home Assistant.
                        // Fork AI (forkai.136): stillgelegt, der Grow-Plan führt das Ziel.
                        if (GrowDiary.Web.Infrastructure.ForkAiSchalter.CropSteeringAktiv)
                        {
                            await nachtabsenkung.SchreibenAsync(
                                tent, flanke.Kind == LightTransitionKind.LightOn, DateTime.Now, cancellationToken);
                        }
                    }
                }

                // Grenzwert-Alarme laufen jetzt im dedizierten AlertWatchWorker (jede Minute),
                // damit Minuten-Intervalle greifen — hier nicht mehr doppelt auswerten.

                // Sensor-Ausfall: gemappte Sensoren, die keine Werte mehr liefern, melden.
                await EvaluateSensorOfflineAsync(notifications, tent, states, cancellationToken);

                await TryCaptureCamera(haService, settings, tent, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                heartbeat.MarkHomeAssistantFailure(ex.GetType().Name);
                _logger.LogWarning(ex,
                    "Reading-Capture fehlgeschlagen für Zelt {TentId}", tent.Id);
            }
        }
    }

    /// <summary>Die Rohwerte, die eine Erfassungsrunde schreibt: jeder Zustand mit Zahl, sonst keiner.</summary>
    /// <remarks>
    /// Ein Zustand ohne Zahl (<c>unavailable</c>, <c>unknown</c>, leer — oder ein
    /// Nullbild der Wassersonde, siehe <see cref="WassersondenNullbild"/>) wird
    /// zur Lücke im Verlauf, nicht zu 0.
    /// </remarks>
    public static IReadOnlyList<TentSensorReading> Rohwerte(
        int tentId, IReadOnlyDictionary<string, HomeAssistantState> states, DateTime capturedAtUtc)
        => states
            .Where(paar => paar.Value.NumericValue is not null)
            .Select(paar => new TentSensorReading
            {
                TentId        = tentId,
                MetricKey     = paar.Key,
                Value         = paar.Value.NumericValue!.Value,
                Unit          = paar.Value.UnitOfMeasurement,
                CapturedAtUtc = capturedAtUtc
            })
            .ToList();

    private async Task TryCaptureCamera(
        HomeAssistantService haService,
        HomeAssistantSettings settings,
        Tent tent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tent.CameraEntityId)) return;

        var localNow = DateTime.Now;
        if (localNow.Hour < 12) return;

        var today = DateOnly.FromDateTime(localNow);
        if (_lastCameraCaptureDateByTent.TryGetValue(tent.Id, out var lastCaptureDate) && lastCaptureDate == today)
        {
            return;
        }

        try
        {
            var snapshot = await haService.GetCameraSnapshotAsync(
                settings, tent.CameraEntityId, cancellationToken);
            if (snapshot is not null)
            {
                var dir = Path.Combine(_paths.SnapshotsPath, tent.Id.ToString());
                Directory.CreateDirectory(dir);
                var filePath = Path.Combine(dir, $"{today:yyyy-MM-dd}.jpg");
                await File.WriteAllBytesAsync(filePath, snapshot.Value.Bytes, cancellationToken);
                _lastCameraCaptureDateByTent[tent.Id] = today;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Kamera-Snapshot fehlgeschlagen für Zelt {TentId}", tent.Id);
        }
    }

    private async Task EvaluateSensorOfflineAsync(
        NotificationService notifications,
        Tent tent,
        Dictionary<string, HomeAssistantState> states,
        CancellationToken cancellationToken)
    {
        // No states at all means Home Assistant itself is unreachable, not that every sensor
        // died — skip so an HA outage does not raise an alarm for every mapped sensor.
        if (states.Count == 0)
        {
            return;
        }

        foreach (var sensor in tent.Sensors.Where(s => s.IsActive && !string.IsNullOrWhiteSpace(s.HaEntityId)))
        {
            var key = TentSensorMetricKeyMap.Resolve(sensor.MetricType);
            var offline = !states.TryGetValue(key, out var state)
                || string.IsNullOrWhiteSpace(state.State)
                || state.State.Equals("unavailable", StringComparison.OrdinalIgnoreCase)
                || state.State.Equals("unknown", StringComparison.OrdinalIgnoreCase);

            var name = string.IsNullOrWhiteSpace(sensor.DisplayLabel) ? sensor.HaEntityId : sensor.DisplayLabel;
            var transition = _offlineTracker.Observe($"{tent.Id}:{key}", offline);
            switch (transition)
            {
                case SensorOfflineTracker.Transition.WentOffline:
                    await notifications.SendAsync(NotificationCategory.SensorOffline, $"🌱 Grow OS · {tent.Name}", $"Sensor liefert keine Werte mehr: {name}.", cancellationToken,
                        seite: NotificationService.LiveSeite(tent.Id));
                    break;
                case SensorOfflineTracker.Transition.CameOnline:
                    await notifications.SendAsync(NotificationCategory.SensorOffline, $"🌱 Grow OS · {tent.Name}", $"Sensor liefert wieder Werte: {name}.", cancellationToken,
                        seite: NotificationService.LiveSeite(tent.Id));
                    break;
            }
        }
    }

    private async Task RunDigestIfDueAsync(DateTime now, DateOnly today, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<NotificationSettingsRepository>().GetNotificationSettings();
        if (!settings.DailyDigest)
        {
            return;
        }

        // Fire on the first poll in the digest hour at/after the chosen minute — so a late
        // start (after the window) simply skips today rather than sending a stale digest.
        if (now.Hour != settings.DigestHour || now.Minute < settings.DigestMinute)
        {
            return;
        }

        _lastDigestDateLocal = today; // mark done even on failure — no retry-storm the same day
        try
        {
            await scope.ServiceProvider.GetRequiredService<DigestService>().BuildAndSendAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tagesüberblick fehlgeschlagen.");
        }
    }

    private async Task RunCalibrationReminderAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var reminder = scope.ServiceProvider.GetRequiredService<CalibrationReminderService>();
        try
        {
            await reminder.CheckAndNotifyAsync(DateTime.UtcNow, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kalibrier-Erinnerung fehlgeschlagen.");
        }
    }

    private async Task AggregateYesterdayAsync(CancellationToken cancellationToken)
    {
        var yesterday = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
        using var scope    = _serviceProvider.CreateScope();
        var repository  = scope.ServiceProvider.GetRequiredService<GrowRepository>();
        var sensorRepo  = scope.ServiceProvider.GetRequiredService<SensorReadingRepository>();

        var stromQuelle = KostenSeiteService.StromQuelleLesen(scope.ServiceProvider.GetRequiredService<AppSettingsRepository>());
        var stromEingerichtet = !string.IsNullOrWhiteSpace(stromQuelle.LeistungEntityId);
        var tents = repository.GetTents();
        foreach (var tent in tents)
        {
            var metricKeys = tent.Sensors
                .Where(sensor => sensor.IsActive && !string.IsNullOrWhiteSpace(sensor.HaEntityId))
                .Select(sensor => TentSensorMetricKeyMap.Resolve(sensor.MetricType))
                .Distinct()
                .ToList();

            if (metricKeys.Count == 0)
            {
                metricKeys =
                [
                    "temperature",
                    "humidity",
                    "vpd",
                    "reservoir-ph",
                    "reservoir-ec",
                    "reservoir-temp",
                    "reservoir-level",
                    "reservoir-level-cm",
                    "co2",
                    "ppfd",
                    "orp",
                    "dissolved-oxygen"
                ];
            }

            // Die Strom-Leistung gehört keinem Sensor des Zelts, hat aber Rohwerte — also auch Tageswerte,
            // sonst endet der Verlauf nach sieben Tagen.
            if (stromEingerichtet && !stromQuelle.HatEigenenZaehler(tent.Id) && !metricKeys.Contains(StromKachel.Key)) metricKeys.Add(StromKachel.Key);

            foreach (var key in metricKeys)
            {
                if (Tageswert.Berechnen(sensorRepo, tent.Id, key, yesterday) is not { } stat) continue;
                sensorRepo.UpsertDailyStat(stat);
            }
        }

        _logger.LogInformation("Tages-Aggregation abgeschlossen für {Date}", yesterday);
        await Task.CompletedTask;
    }

    private async Task SpruengeMerkenAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var tagebuch = scope.ServiceProvider.GetRequiredService<GrowDiary.Web.Services.Tagebuch.TagebuchService>();
        var neu = tagebuch.ErkennenFuerAlleZelte(DateTime.UtcNow);
        if (neu > 0) _logger.LogInformation("Tagebuch: {Anzahl} neue Spruenge im Sensorverlauf gemerkt.", neu);
        await Task.CompletedTask;
    }

    private async Task CleanupOldReadingsAsync()
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);
        using var scope   = _serviceProvider.CreateScope();
        var sensorRepo = scope.ServiceProvider.GetRequiredService<SensorReadingRepository>();
        sensorRepo.DeleteOlderThan(cutoff);
        await Task.CompletedTask;
    }
}

