using System.Text.Json;
using System.Text.Json.Serialization;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Services;
using GrowDiary.Web.Services.Knowledge;
using Microsoft.AspNetCore.DataProtection;

// ERSTE Anweisung: Threads, die davor entstehen, erben die alte Kultur.
// Ohne das formatiert jedes $"{wert:0.0}" mit der Kultur der Umgebung — im
// Container ohne LANG also „6.5" mitten in einem deutschen Satz. Siehe Deutsch.
Deutsch.Setzen();

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Controller-Endpoints fuer JSON-APIs, Kamera-Routen und verbleibende Kompatibilitaets-POSTs
builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
{
    options.SuppressModelStateInvalidFilter = true;
});
builder.Services.AddHttpClient();

var paths = new AppPaths(builder.Environment.ContentRootPath);
Directory.CreateDirectory(paths.DataProtectionKeysPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(paths.DataProtectionKeysPath))
    .SetApplicationName("GrowDiary.Web");
builder.Services.AddSingleton(paths);
builder.Services.AddSingleton<DatabaseInitializer>();
builder.Services.AddSingleton<TentRepository>();
builder.Services.AddSingleton<HydroSetupRepository>();
builder.Services.AddSingleton<AddbackRepository>();
builder.Services.AddSingleton<HardwareRepository>();
builder.Services.AddSingleton<SetupRepository>();
builder.Services.AddSingleton<AutoMeasurementRepository>();
builder.Services.AddSingleton<LightRepository>();
builder.Services.AddSingleton<SopRepository>();
builder.Services.AddSingleton<PhotoRepository>();
builder.Services.AddSingleton<HomeAssistantSettingsRepository>();
builder.Services.AddSingleton<CameraFrameCache>();
builder.Services.AddSingleton<GrowCoreRepository>();
builder.Services.AddSingleton<MeasurementRepository>();
builder.Services.AddSingleton<GrowRepository>();
builder.Services.AddSingleton<TaskRepository>();
builder.Services.AddSingleton<JournalRepository>();
builder.Services.AddSingleton<AuditRepository>();
builder.Services.AddSingleton<SystemAuditRepository>();
builder.Services.AddSingleton<HarvestRepository>();
builder.Services.AddSingleton<CuringRepository>();
builder.Services.AddSingleton<PhenoRepository>();
builder.Services.AddSingleton<DashboardLayoutRepository>();
builder.Services.AddSingleton<KnowledgeBaseLoader>();
builder.Services.AddSingleton<CultivationKnowledgeService>();
builder.Services.AddSingleton<TargetValueService>();
builder.Services.AddSingleton<MeasurementSanityService>();
builder.Services.AddSingleton<RecommendationEngine>();
builder.Services.AddSingleton<GrowAlertService>();
builder.Services.AddSingleton<DeviationAnalyzerService>();
builder.Services.AddSingleton<MeasurementAssessmentService>();
builder.Services.AddSingleton<TreatmentRecommender>();
builder.Services.AddSingleton<DeviationRiskEventSyncService>();
builder.Services.AddSingleton<RiskEventSopRecommender>();
builder.Services.AddSingleton<WeekCounterService>();
builder.Services.AddSingleton<HomeAssistantService>();
builder.Services.AddSingleton<SupervisorInfoService>();
// Der Kalibrierlauf haelt seine Sitzung im Speicher — deshalb Singleton.
builder.Services.AddSingleton<LevelCalibrationService>();
builder.Services.AddSingleton<LightStatusTransitionService>();
// Lesen ist zustandslos und darf Singleton sein; melden braucht den
// Benachrichtigungsdienst und lebt deshalb je Anfrage.
builder.Services.AddSingleton<LightCycleReader>();
builder.Services.AddScoped<LightWatchService>();
builder.Services.AddSingleton<AutoMeasurementValueGuard>();
builder.Services.AddSingleton<PhotoStorageService>();
builder.Services.AddSingleton<GrowDashboardComposer>();
builder.Services.AddScoped<SensorReadingRepository>();
builder.Services.AddScoped<AutoMeasurementExecutionService>();
// Singleton wie die uebrigen zustandslosen Repositories: der Live-Bildschirm und
// die Diagnose sind Singletons und muessen die Grenzwerte des Nutzers lesen
// koennen — ein Scoped-Dienst laesst sich dort nicht hineingeben.
builder.Services.AddSingleton<AlertRuleRepository>();
builder.Services.AddSingleton<SystemHeartbeat>();
builder.Services.AddScoped<WatchdogService>();
builder.Services.AddSingleton<SetpointProfileRepository>();
builder.Services.AddScoped<DosingRepository>();
builder.Services.AddScoped<DosingService>();
builder.Services.AddScoped<DosingContextBuilder>();
builder.Services.AddScoped<AgentContextBuilder>();
builder.Services.AddScoped<AgentPackageBuilder>();
builder.Services.AddScoped<AlertEvaluationService>();
builder.Services.AddSingleton<NotificationSettingsRepository>();
builder.Services.AddSingleton<AppSettingsRepository>();
builder.Services.AddSingleton<WaterProfileStore>();
builder.Services.AddScoped<GrowCostService>();
// Fork AI (forkai.6): Kosten-Seite
builder.Services.AddSingleton<KostenRepository>();
builder.Services.AddScoped<KostenSeiteService>();
// Fork AI (forkai.20): Steuerung — CO₂-Leitstand
builder.Services.AddSingleton<SteuerungRepository>();
builder.Services.AddScoped<SteuerungGeraeteService>();
builder.Services.AddScoped<Co2SteuerungService>();
builder.Services.AddScoped<MischplanService>();
builder.Services.AddScoped<SopDueService>();
builder.Services.AddScoped<WasserwechselStandService>();
builder.Services.AddSingleton<WasserAmpelService>();
builder.Services.AddScoped<AnlagenRisikoService>();
builder.Services.AddScoped<PumpWatchNotifier>();
builder.Services.AddScoped<WartungDueService>();
builder.Services.AddScoped<NachtabsenkungWriter>();
builder.Services.AddScoped<IAcFunk, HomeAssistantFunk>();
builder.Services.AddScoped<AcSchreiber>();
builder.Services.AddSingleton<EinkaufslisteService>();
builder.Services.AddSingleton<BeobachtungsWegweiser>();
builder.Services.AddSingleton<SolutionStabilityAnalyzer>();
builder.Services.AddScoped<TrendWatchRunner>();
builder.Services.AddSingleton<TentSensorHardwareSyncService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<CalibrationReminderService>();
builder.Services.AddScoped<DigestService>();
builder.Services.AddHostedService<HomeAssistantSnapshotWorker>();
builder.Services.AddHostedService<AlertWatchWorker>();
builder.Services.AddHostedService<AutoMeasurementWorker>();
builder.Services.AddHostedService<DosingWorker>();
// Eigener Minutentakt und NICHT an der Lichtflanke: die haengt zweimal am Tag,
// die Wassertemperatur wandert dazwischen.
builder.Services.AddHostedService<KuehlerWorker>();
builder.Services.AddHostedService<ZaehlerstandWorker>(); // Fork AI (forkai.6)
builder.Services.AddHostedService<Co2SyncWorker>(); // Fork AI (forkai.20)

var defaultUrls = builder.Configuration["Hosting:DefaultUrls"];
if (!string.IsNullOrWhiteSpace(defaultUrls))
{
    builder.WebHost.UseUrls(defaultUrls);
}

var app = builder.Build();

app.Services.GetRequiredService<DatabaseInitializer>().Initialize();
app.Services.GetRequiredService<KnowledgeBaseLoader>().Initialize();

HaConfigLoader.Apply(
    app.Services.GetRequiredService<AppPaths>(),
    app.Services.GetRequiredService<GrowRepository>());

// Testdatenmodus: einmal 24 Stunden Verlauf nachtragen, damit Kurven und
// Verlaufsseite sofort etwas zeigen. Nur, wenn fuer das Zelt noch nichts da
// ist — sonst waechst die Historie bei jedem Neustart doppelt.
if (DemoData.IsEnabled)
{
    using var demoScope = app.Services.CreateScope();
    var demoLogger = demoScope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        // Der ganze fachliche Bestand — Zelt, Aufbau, drei Grows, sechs Wochen
        // Messungen, Journal, Aufgaben, Geraet, Alarm, Risiko, Aushaerte-Glas
        // und zwei Ernten. Nur in eine Datenbank ganz OHNE Grows: wer schon
        // einen hat, hat eigene Daten, und die sind besser als jede Demo.
        //
        // Bis beta.50 stand hier ein Einzelfall, der nur den fehlenden
        // Archiv-Grow nachtrug. Der ist in Demobestand aufgegangen — sonst
        // haette es zwei Stellen gegeben, die Testdaten anlegen.
        var growsRepo = demoScope.ServiceProvider.GetRequiredService<GrowRepository>();
        if (Demobestand.IstNoetig(growsRepo))
        {
            var was = Demobestand.Anlegen(demoScope.ServiceProvider);
            demoLogger.LogInformation("Testdaten: Demobestand angelegt — {Was}.", was);
        }

        // ERST der Bestand, DANN die Zelte lesen. Andersherum stand hier
        // `GetTents()` vor dem Anlegen: auf einer frischen Datenbank war die
        // Liste leer, die Historie-Schleife lief null Mal, und im
        // 14-Tage-Diagramm stand nichts. Gesehen, weil ich die Kurve
        // angesehen habe statt der Log-Zeile zu glauben.
        var tents = demoScope.ServiceProvider.GetRequiredService<GrowRepository>().GetTents();
        var readings = demoScope.ServiceProvider.GetRequiredService<SensorReadingRepository>();
        var nowUtc = DateTime.UtcNow;
        foreach (var tent in tents)
        {
            var neuester = readings.GetNewestReadingUtc(tent.Id);
            if (neuester is { } vorhanden && nowUtc - vorhanden < TimeSpan.FromHours(2)) continue;

            var anzahl = 0;
            foreach (var reading in DemoData.SeedHistory(tent.Id, nowUtc))
            {
                readings.AddReading(reading);
                anzahl++;
            }

            // Und die Tageswerte fuer alles, was aelter als sieben Tage ist:
            // die Rohablesungen dort raeumt der Aufraeumer ohnehin weg, und
            // ohne Tageswerte zeigt das 14- und 30-Tage-Diagramm genau einen
            // Punkt.
            var tageswerte = 0;
            foreach (var stat in DemoData.SeedDailyStats(tent.Id, DateTime.Today))
            {
                readings.UpsertDailyStat(stat);
                tageswerte++;
            }

            demoLogger.LogInformation(
                "Testdaten: {Count} Messwerte und {Tage} Tageswerte fuer Zelt {TentId} nachgetragen.",
                anzahl, tageswerte, tent.Id);
        }

        // Eine kalibrierte pH-Sonde, sonst bleibt die Automatik gesperrt.
        var hardware = demoScope.ServiceProvider.GetRequiredService<GrowRepository>();
        foreach (var tent in tents)
        {
            var hatSonde = hardware.GetHardwareItemsByTent(tent.Id)
                .Any(item => item.MetricType == GrowDiary.Web.Models.SensorMetricType.ReservoirPh);
            if (hatSonde) continue;

            var (probe, calibration) = DemoData.SeedProbe(tent.Id, nowUtc);
            var angelegt = hardware.CreateHardwareItem(probe);
            calibration.HardwareItemId = angelegt.Id;
            hardware.CreateCalibrationEvent(calibration);
            demoLogger.LogInformation("Testdaten: kalibrierte pH-Sonde fuer Zelt {TentId} angelegt.", tent.Id);
        }

        // Dazu ein paar zurueckliegende Dosen mit Wirkung. Ohne die hat keine
        // Pumpe je etwas gelernt, und der Vorschlag aus Stufe 2 sagt auf dem
        // Entwicklungsrechner immer nur „noch keine Erfahrung".
        var dosing = demoScope.ServiceProvider.GetRequiredService<DosingRepository>();
        foreach (var pump in dosing.GetPumps())
        {
            if (dosing.GetEvents(pumpId: pump.Id, limit: 200)
                .Any(dose => dose.Outcome == GrowDiary.Web.Models.DoseOutcome.Done && !dose.Simulated && dose.ValueAfter is not null))
            {
                continue;
            }

            foreach (var dose in DemoData.SeedDoses(pump.Id, pump.TentId, nowUtc))
            {
                dosing.InsertEvent(dose);
            }
            demoLogger.LogInformation("Testdaten: Dosier-Historie fuer Pumpe {Pump} nachgetragen.", pump.Name);
        }
    }
    catch (Exception ex)
    {
        demoLogger.LogWarning(ex, "Testdaten-Verlauf konnte nicht angelegt werden.");
    }
}

// Der Totmann: Ist Grow OS mitten in einer Dosis abgestuerzt, laeuft die Pumpe
// seither weiter — niemand ist da, der sie stoppt. Der erste Handgriff nach dem
// Hochfahren ist deshalb, jede eingerichtete Pumpe einmal auszuwerfen. Kostet
// nichts, wenn ohnehin alles aus war.
_ = Task.Run(async () =>
{
    using var scope = app.Services.CreateScope();
    var dosing = scope.ServiceProvider.GetRequiredService<DosingService>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        await dosing.TurnAllOffAsync();
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Pumpen-Auswurf beim Start fehlgeschlagen.");
    }
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/api/error");
}

app.Use(async (context, next) =>
{
    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    context.Response.Headers.TryAdd("X-Frame-Options", "SAMEORIGIN");
    context.Response.Headers.TryAdd("Referrer-Policy", "no-referrer");

    await next();
});

// When running as a Home Assistant add-on, requests arrive through the ingress
// proxy under a dynamic base path (e.g. /api/hassio_ingress/<token>). Home
// Assistant already strips that prefix, so we only need to record it as PathBase
// so any server-generated URLs point back through the ingress.
app.Use(async (context, next) =>
{
    if (context.Request.Headers.TryGetValue(AdminAccessPolicy.IngressPathHeaderName, out var ingressPath))
    {
        var value = ingressPath.ToString().TrimEnd('/');
        // PathString requires a leading slash; guard against a malformed header.
        if (value.StartsWith('/'))
        {
            context.Request.PathBase = new PathString(value);
        }
    }

    await next();
});

app.Use(async (context, next) =>
{
    if (AdminAccessPolicy.IsProtectedPath(context.Request.Path))
    {
        var isLocal = AdminAccessPolicy.IsLocalRequest(context);
        var canAccess = AdminAccessPolicy.CanAccess(context);
        if (!isLocal)
        {
            TryLogAdminAccess(context, canAccess);
        }

        if (!canAccess)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json; charset=utf-8";
            await JsonSerializer.SerializeAsync(
                context.Response.Body,
                ApiErrorFactory.Forbidden(
                    "admin_access_required",
                    "Dieser Bereich ist nur lokal oder ueber Home Assistant (Ingress) erreichbar.",
                    context.TraceIdentifier),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return;
        }
    }

    await next();
});

static void TryLogAdminAccess(HttpContext context, bool allowed)
{
    try
    {
        var audit = context.RequestServices.GetService<SystemAuditRepository>();
        audit?.Add(new GrowDiary.Web.Models.SystemAuditEvent
        {
            EventType = "security",
            Action = allowed ? "remote-admin-access-allowed" : "remote-admin-access-blocked",
            Summary = $"{context.Request.Method} {context.Request.Path}",
            Severity = allowed ? "warning" : "critical",
            Source = "admin-access-middleware",
            RemoteAddress = context.Connection.RemoteIpAddress?.ToString(),
            Success = allowed
        });
    }
    catch
    {
        // Audit logging must never block request handling.
    }
}

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var name = ctx.File.Name;
        // index.html must always revalidate, otherwise clients keep loading a stale
        // shell that points at old asset hashes (hashed /assets/* files stay immutably
        // cacheable).
        if (name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        }
    }
});

// Grow photos / uploads live under the persistent data root (outside wwwroot) so they
// survive updates and are captured by Home Assistant backups. Serve them at /uploads.
Directory.CreateDirectory(paths.UploadRootPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(paths.UploadRootPath),
    RequestPath = "/uploads"
});

app.UseRouting();

// API-Attribute-Routes, Kamera-Routen und Export-Endpoints
app.MapControllers();

// SPA-Fallback fuer alle non-API-Routen — index.html immer frisch (kein Stale-Shell).
// Injects a <base href> so the app's relative asset/API URLs resolve correctly both
// at the site root ("/") and behind the Home Assistant ingress (the request PathBase).
app.MapFallback(async context =>
{
    // „Fuer alle non-API-Routen" stand hier immer im Kommentar — geprueft hat
    // es niemand. Ein unbekannter /api/-Pfad bekam die Startseite mit Status
    // 200: ein Tippfehler im Client sah damit nach Erfolg aus, und wer JSON
    // erwartete, bekam HTML und eine kryptische Parse-Meldung statt eines
    // klaren 404. (Gefunden beim Aufraeumen: drei DELETEs meldeten 200, und
    // geloescht war trotzdem nichts.)
    if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "application/problem+json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(ApiErrorFactory.Create(
            "endpoint_not_found",
            $"Es gibt keinen Endpunkt {context.Request.Method} {context.Request.Path}.",
            StatusCodes.Status404NotFound,
            traceId: context.TraceIdentifier));
        return;
    }

    context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
    context.Response.ContentType = "text/html; charset=utf-8";

    var html = await File.ReadAllTextAsync(Path.Combine(app.Environment.WebRootPath, "index.html"));
    var pathBase = context.Request.PathBase.HasValue ? context.Request.PathBase.Value! : string.Empty;
    var baseHref = string.IsNullOrEmpty(pathBase) ? "/" : pathBase + "/";
    await context.Response.WriteAsync(InjectBaseHref(html, baseHref));
});

static string InjectBaseHref(string html, string baseHref)
{
    var tag = $"<base href=\"{baseHref}\" />";
    var headIndex = html.IndexOf("<head>", StringComparison.OrdinalIgnoreCase);
    if (headIndex < 0)
    {
        return html;
    }

    var insertAt = headIndex + "<head>".Length;
    return string.Concat(html.AsSpan(0, insertAt), tag, html.AsSpan(insertAt));
}

// Hat die Kultur gegriffen? Im Invariant-Modus (kein ICU im Basis-Image oder
// DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1) liefert new CultureInfo("de-DE")
// stillschweigend die invariante Kultur — dann stuende im ganzen Haus wieder
// „6.5" statt „6,5", ohne dass irgendwo etwas schiefginge. Deshalb einmal
// nachsehen und laut werden.
if (!Deutsch.IstWirksam)
{
    app.Services.GetRequiredService<ILogger<Program>>().LogError(
        "Die deutsche Kultur ist NICHT wirksam: 6,5 wird als \"{Probe}\" geschrieben. "
        + "Vermutlich laeuft .NET im Invariant-Modus (fehlendes ICU im Basis-Image oder "
        + "DOTNET_SYSTEM_GLOBALIZATION_INVARIANT). Alle Zahlen in Nutzertexten bekommen "
        + "dadurch einen englischen Dezimalpunkt.", 6.5.ToString("0.0"));
}

app.Run();

/// <summary>
/// Nur damit der Test die App starten kann.
/// </summary>
/// <remarks>
/// Top-Level-Anweisungen erzeugen eine <c>internal</c> Programmklasse;
/// <c>WebApplicationFactory&lt;Program&gt;</c> braucht sie öffentlich. Ohne
/// diese Zeile gäbe es im ganzen Projekt keinen Weg, die App im Test zu
/// starten — und genau der fehlte, als das Flipdatum still verworfen wurde.
/// Sie fügt der laufenden App nichts hinzu.
/// </remarks>
public partial class Program { }
