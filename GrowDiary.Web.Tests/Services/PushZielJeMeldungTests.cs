using System.Text.RegularExpressions;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Tests.TestFakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Wohin der Tipp auf eine Push-Meldung führt — je Meldungsart.
/// </summary>
/// <remarks>
/// forkai.168 schickte Grenzwert- und Risiko-Meldungen auf „Aufgaben". Dort
/// steht weder eine Grenzwert-Überschreitung noch ein Trend-Befund; Bru bekam
/// fast nur Grenzwert-Meldungen und landete „eigentlich immer bei den Aufgaben"
/// (04.10.2026). Geprüft wird hier der ganze Weg: Alarm → Push an HA →
/// <c>clickAction</c>.
/// </remarks>
public sealed class PushZielJeMeldungTests : IDisposable
{
    private const string Slug = "d48160c2_grow_os_fork_ai";

    private readonly string _contentRoot;
    private readonly AppPaths _paths;
    private readonly Tent _tent;
    private readonly AlertRuleRepository _rules;
    private readonly NotificationSettingsRepository _notificationSettings;
    private readonly GrowRepository _growRepository;
    private readonly string? _savedSupervisorToken;

    public PushZielJeMeldungTests()
    {
        _contentRoot = Path.Combine(Path.GetTempPath(), $"grow-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_contentRoot);
        _paths = new AppPaths(_contentRoot);
        _tent = TestDatabase.InitializeWithDefaultTent(_paths);
        _rules = new AlertRuleRepository(_paths);
        _notificationSettings = new NotificationSettingsRepository(_paths);
        _growRepository = new GrowRepository(_paths);

        // Mit Supervisor-Token: nur dann kennt Grow OS seinen Slug und setzt ein Ziel.
        _savedSupervisorToken = Environment.GetEnvironmentVariable(HomeAssistantAddon.SupervisorTokenEnvironmentVariable);
        Environment.SetEnvironmentVariable(HomeAssistantAddon.SupervisorTokenEnvironmentVariable, "supervisor-token");

        _growRepository.SaveHomeAssistantSettings(new HomeAssistantSettings { BaseUrl = "http://ha.local:8123", AccessToken = "token", Enabled = true });
        _notificationSettings.SaveNotificationSettings(new NotificationSettings { NotifyService = "notify.mobile_app_test", QuietHoursStartHour = null, QuietHoursEndHour = null });
        _rules.ReplaceForTent(_tent.Id, new[]
        {
            new TentAlertRule { TentId = _tent.Id, MetricKey = "reservoir-ph", MinValue = 5.5, MaxValue = 6.5, Enabled = true, CooldownMinutes = 30 },
        });
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(HomeAssistantAddon.SupervisorTokenEnvironmentVariable, _savedSupervisorToken);
        try { Directory.Delete(_contentRoot, recursive: true); } catch { }
    }

    /// <summary>HA und Supervisor in einem: der Supervisor nennt den Slug, HA nimmt den Push an.</summary>
    private static RecordingHttpHandler Handler() => new((request, _) =>
        request.RequestUri!.Host == "supervisor"
            ? RecordingHttpHandler.Json($$$"""{"result":"ok","data":{"slug":"{{{Slug}}}"}}""")
            : RecordingHttpHandler.Json("[]"));

    private NotificationService Benachrichtigung(RecordingHttpHandler handler)
    {
        var fabrik = new StubHttpClientFactory(handler);
        return new NotificationService(
            _notificationSettings,
            _growRepository,
            new HomeAssistantService(fabrik, NullLogger<HomeAssistantService>.Instance),
            NullLogger<NotificationService>.Instance,
            new SupervisorInfoService(fabrik, NullLogger<SupervisorInfoService>.Instance));
    }

    private static string Push(RecordingHttpHandler handler)
        => Assert.Single(handler.Requests, r => r.Uri.AbsolutePath.Contains("/api/services/notify/", StringComparison.Ordinal)).Body!;

    [Fact]
    public async Task Grenzwert_fuehrt_auf_Live_mit_dem_Zelt_der_Meldung()
    {
        var handler = Handler();
        var alarme = new AlertEvaluationService(_rules, Benachrichtigung(handler), NullLogger<AlertEvaluationService>.Instance);

        await alarme.EvaluateAsync(_tent, new Dictionary<string, HomeAssistantState>
        {
            ["reservoir-ph"] = new HomeAssistantState { State = "5", NumericValue = 5.0 },
        });

        var body = Push(handler);
        Assert.Contains($"\"clickAction\":\"/app/{Slug}/live/{_tent.Id}\"", body);
        Assert.Contains($"\"url\":\"/app/{Slug}/live/{_tent.Id}\"", body);
    }

    [Theory]
    [InlineData(NotificationCategory.Threshold, "live")]
    [InlineData(NotificationCategory.Risk, "live")]
    [InlineData(NotificationCategory.System, "aufgaben")]
    [InlineData(NotificationCategory.Calibration, "sensoren")]
    [InlineData(NotificationCategory.Maintenance, "sensoren")]
    [InlineData(NotificationCategory.SensorOffline, "live")]
    public async Task Ohne_genaueres_Ziel_gilt_die_Seite_der_Meldungsart(NotificationCategory art, string seite)
    {
        var handler = Handler();

        Assert.True(await Benachrichtigung(handler).SendAsync(art, "t", "m"));

        Assert.Contains($"\"clickAction\":\"/app/{Slug}/{seite}\"", Push(handler));
    }

    [Fact]
    public async Task Ein_genaueres_Ziel_des_Absenders_geht_vor()
    {
        var handler = Handler();

        await Benachrichtigung(handler).SendAsync(NotificationCategory.System, "t", "m", seite: "dosierung");

        Assert.Contains($"\"clickAction\":\"/app/{Slug}/dosierung\"", Push(handler));
    }

    [Fact]
    public async Task Testmeldung_und_Tagesbericht_nennen_Live_ausdruecklich()
    {
        // Ein leerer Rest („/app/<slug>") kommt in der App als route.path "" an —
        // genau wie bei jeder Größenänderung. Die App bliebe dann auf der Seite,
        // die gerade offen ist.
        Assert.Equal($"/app/{Slug}/live", await Benachrichtigung(Handler()).StartPfadAsync(default));
    }

    /// <summary>
    /// Zählung: jede Meldungsart zeigt auf eine Seite, die es in der App gibt —
    /// gelesen aus den Routen in <c>App.tsx</c>, nicht aus einer Liste hier.
    /// </summary>
    [Fact]
    public void Jedes_Ziel_ist_eine_Route_der_App()
    {
        var app = File.ReadAllText(Path.Combine(ProjektWurzel(), "GrowDiary.React", "src", "App.tsx"));
        var routen = Regex.Matches(app, "<Route path=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToHashSet();
        Assert.True(routen.Count >= 30, $"Nur {routen.Count} Routen gelesen — sieht die Zählung App.tsx überhaupt?");

        var arten = Enum.GetValues<NotificationCategory>();
        Assert.True(arten.Length >= 6);
        foreach (var art in arten)
        {
            Assert.Contains("/" + NotificationService.SeiteFuer(art), routen);
        }

        Assert.Contains("/live/:zeltId", routen);
        Assert.Equal("live/7", NotificationService.LiveSeite(7));
        Assert.Contains("/dosierung", routen);
    }

    private static string ProjektWurzel()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "GrowDiary.Web"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Projektwurzel nicht gefunden.");
    }
}
