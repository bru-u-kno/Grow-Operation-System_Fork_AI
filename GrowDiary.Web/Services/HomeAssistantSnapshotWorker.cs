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
            var now = DateTime.Now;

            await CaptureReadingsAsync(stoppingToken);

            var today = DateOnly.FromDateTime(now);
            // Ab 02:00, nicht NUR 02:00–02:05: der Takt ist 5 Minuten PLUS
            // Capture-Laufzeit. Ein zaehes HA um 01:59 haette das enge Fenster
            // uebersprungen — und weil die Rohdaten nach 7 Tagen aufgeraeumt
            // werden, waere der Vortag irgendwann unwiederbringlich ohne
            // Tagesstatistik geblieben.
            if (now.Hour >= 2 && _lastAggregationDateLocal != today)
            {
                await AggregateYesterdayAsync(stoppingToken);
                await CleanupOldReadingsAsync();
                _lastAggregationDateLocal = today;
            }

            // Kalibrier-/Wartungs-Erinnerung: einmal täglich am Vormittag (nicht mitten in der Nacht).
            if (now.Hour >= 8 && _lastCalibrationCheckDateLocal != today)
            {
                await RunCalibrationReminderAsync(stoppingToken);
                _lastCalibrationCheckDateLocal = today;
            }

            // Täglicher Digest zur eingestellten Uhrzeit.
            if (_lastDigestDateLocal != today)
            {
                await RunDigestIfDueAsync(now, today, stoppingToken);
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

        var settings = repository.GetEffectiveHomeAssistantSettings();
        // The worker is alive either way — record the round even when HA is unconfigured,
        // so the watchdog does not mistake "nothing to do" for "stalled".
        heartbeat.MarkSnapshotRun(DateTime.UtcNow);
        if (!settings.IsConfigured) return;

        var tents = repository.GetTents();
        foreach (var tent in tents)
        {
            try
            {
                var states      = await haService.GetStatesAsync(settings, tent, cancellationToken);
                var capturedAt  = DateTime.UtcNow;
                heartbeat.MarkHomeAssistantSuccess(capturedAt);

                foreach (var (key, state) in states)
                {
                    if (state.NumericValue is not { } value) continue;
                    sensorRepo.AddReading(new TentSensorReading
                    {
                        TentId        = tent.Id,
                        MetricKey     = key,
                        Value         = value,
                        Unit          = state.UnitOfMeasurement,
                        CapturedAtUtc = capturedAt
                    });
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
                    await notifications.SendAsync(NotificationCategory.SensorOffline, $"🌱 Grow OS · {tent.Name}", $"Sensor liefert keine Werte mehr: {name}.", cancellationToken);
                    break;
                case SensorOfflineTracker.Transition.CameOnline:
                    await notifications.SendAsync(NotificationCategory.SensorOffline, $"🌱 Grow OS · {tent.Name}", $"Sensor liefert wieder Werte: {name}.", cancellationToken);
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

            foreach (var key in metricKeys)
            {
                var readings = sensorRepo.GetReadingsForDay(tent.Id, key, yesterday);
                if (readings.Count < 3) continue;

                var values = readings.Select(r => r.Value).ToList();
                var unit   = readings[0].Unit;
                var stat   = PercentileCalculator.ComputeStats(tent.Id, key, yesterday, values, unit);
                sensorRepo.UpsertDailyStat(stat);
            }
        }

        _logger.LogInformation("Tages-Aggregation abgeschlossen für {Date}", yesterday);
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

