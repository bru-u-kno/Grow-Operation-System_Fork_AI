using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Tests.TestFakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Full-loop behavior tests for threshold alerts: real SQLite rule persistence, the
/// central notification gateway, and a faked Home Assistant — driven poll by poll the
/// way the snapshot worker drives production.
/// </summary>
public sealed class AlertEvaluationBehaviorTests : IDisposable
{
    private readonly string _contentRoot;
    private readonly AppPaths _paths;
    private readonly Tent _tent;
    private readonly AlertRuleRepository _rules;
    private readonly NotificationSettingsRepository _notificationSettings;
    private readonly GrowRepository _growRepository;
    private readonly string? _savedSupervisorToken;

    public AlertEvaluationBehaviorTests()
    {
        _contentRoot = Path.Combine(Path.GetTempPath(), $"grow-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_contentRoot);
        _paths = new AppPaths(_contentRoot);
        _tent = TestDatabase.InitializeWithDefaultTent(_paths);

        _rules = new AlertRuleRepository(_paths);
        _notificationSettings = new NotificationSettingsRepository(_paths);
        _growRepository = new GrowRepository(_paths);

        _savedSupervisorToken = Environment.GetEnvironmentVariable(HomeAssistantAddon.SupervisorTokenEnvironmentVariable);
        Environment.SetEnvironmentVariable(HomeAssistantAddon.SupervisorTokenEnvironmentVariable, null);

        _growRepository.SaveHomeAssistantSettings(new HomeAssistantSettings { BaseUrl = "http://ha.local:8123", AccessToken = "token", Enabled = true });
        _notificationSettings.SaveNotificationSettings(new NotificationSettings { NotifyService = "notify.mobile_app_test" });

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

    private AlertEvaluationService Service(RecordingHttpHandler handler)
        => new(
            _rules,
            new NotificationService(
                _notificationSettings,
                _growRepository,
                new HomeAssistantService(new StubHttpClientFactory(handler), NullLogger<HomeAssistantService>.Instance),
                NullLogger<NotificationService>.Instance),
            NullLogger<AlertEvaluationService>.Instance);

    private static Dictionary<string, HomeAssistantState> Ph(double value) => new()
    {
        ["reservoir-ph"] = new HomeAssistantState { State = value.ToString(System.Globalization.CultureInfo.InvariantCulture), NumericValue = value },
    };

    private TentAlertRule StoredRule() => Assert.Single(_rules.GetForTent(_tent.Id));

    [Fact]
    public async Task Breach_SendsPush_AndPersistsState()
    {
        var handler = new RecordingHttpHandler((_, _) => RecordingHttpHandler.Json("[]"));
        var service = Service(handler);

        await service.EvaluateAsync(_tent, Ph(5.0));

        var push = Assert.Single(handler.Requests);
        Assert.EndsWith("/api/services/notify/mobile_app_test", push.Uri.ToString());
        Assert.Contains("pH", push.Body);

        var rule = StoredRule();
        Assert.Equal(AlertEvaluationService.Below, rule.LastState);
        Assert.NotNull(rule.LastNotifiedUtc);
    }

    /// <summary>
    /// Der Push ist Oberflächentext: deutsche Zahlen. Bis zum 01.10.2026 stand
    /// dort „pH 5.25 (Grenze 5.5)" — formatiert mit der invarianten Kultur.
    /// </summary>
    [Fact]
    public async Task Breach_PushUsesGermanNumbers()
    {
        var handler = new RecordingHttpHandler((_, _) => RecordingHttpHandler.Json("[]"));
        var service = Service(handler);

        await service.EvaluateAsync(_tent, Ph(5.25));

        var push = Assert.Single(handler.Requests);
        Assert.Contains("5,25", push.Body);
        Assert.Contains("Grenze 5,5", push.Body);
        Assert.DoesNotContain("5.25", push.Body);
    }

    [Fact]
    public async Task ContinuedBreach_AcrossPolls_DoesNotSpam()
    {
        var handler = new RecordingHttpHandler((_, _) => RecordingHttpHandler.Json("[]"));
        var service = Service(handler);

        await service.EvaluateAsync(_tent, Ph(5.0));
        await service.EvaluateAsync(_tent, Ph(5.1));
        await service.EvaluateAsync(_tent, Ph(4.9));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Recovery_SendsRecoveryPush_AndResetsState()
    {
        var handler = new RecordingHttpHandler((_, _) => RecordingHttpHandler.Json("[]"));
        var service = Service(handler);

        await service.EvaluateAsync(_tent, Ph(5.0));
        await service.EvaluateAsync(_tent, Ph(6.0));

        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("wieder im Zielbereich", handler.Requests[1].Body);
        Assert.Equal(AlertEvaluationService.InRange, StoredRule().LastState);
    }

    [Fact]
    public async Task ValueInRange_SendsNothing_AndPersistsInRange()
    {
        var handler = new RecordingHttpHandler((_, _) => RecordingHttpHandler.Json("[]"));

        await Service(handler).EvaluateAsync(_tent, Ph(6.0));

        Assert.Empty(handler.Requests);
        Assert.Equal(AlertEvaluationService.InRange, StoredRule().LastState);
    }

    [Fact]
    public async Task MetricWithoutLiveValue_IsIgnored()
    {
        var handler = new RecordingHttpHandler((_, _) => RecordingHttpHandler.Json("[]"));

        await Service(handler).EvaluateAsync(_tent, new Dictionary<string, HomeAssistantState>());

        Assert.Empty(handler.Requests);
        Assert.Null(StoredRule().LastState);
    }

    [Fact]
    public async Task BreachDuringQuietHours_IsDeferred_NotSwallowed()
    {
        // Regression for a real behavioral gap: a breach that started during quiet hours
        // used to persist its state without ever sending, so the user never got notified.
        // Ein Fenster von DREI Stunden statt einer, mit der aktuellen in der
        // Mitte. Die erste Fassung nahm `nowHour` bis `nowHour + 1`: laeuft der
        // Test um 14:59:59 los und wertet der Dienst um 15:00:00 aus, liegt
        // „jetzt" ausserhalb — der Test faellt dann durch, ohne dass sich etwas
        // am Verhalten geaendert hat. Wie teuer so ein Zufall ist, hat
        // beta.53 gezeigt.
        var nowHour = DateTime.Now.Hour;
        _notificationSettings.SaveNotificationSettings(new NotificationSettings
        {
            NotifyService = "notify.mobile_app_test",
            QuietHoursStartHour = (nowHour + 23) % 24,
            QuietHoursEndHour = (nowHour + 2) % 24,
        });

        var handler = new RecordingHttpHandler((_, _) => RecordingHttpHandler.Json("[]"));
        var service = Service(handler);

        await service.EvaluateAsync(_tent, Ph(5.0));
        Assert.Empty(handler.Requests);
        Assert.Null(StoredRule().LastState); // state NOT burned — breach still pending

        // Quiet hours end (user turns them off / clock moves on) → next poll delivers.
        _notificationSettings.SaveNotificationSettings(new NotificationSettings { NotifyService = "notify.mobile_app_test" });
        await service.EvaluateAsync(_tent, Ph(5.0));

        Assert.Single(handler.Requests);
        Assert.Equal(AlertEvaluationService.Below, StoredRule().LastState);
    }

    [Fact]
    public async Task BreachWhileHaUnreachable_RetriesNextPoll()
    {
        var failing = new RecordingHttpHandler((_, _) => throw new HttpRequestException("down"));
        await Service(failing).EvaluateAsync(_tent, Ph(5.0));
        Assert.Null(StoredRule().LastState); // send failed → state kept for retry

        var working = new RecordingHttpHandler((_, _) => RecordingHttpHandler.Json("[]"));
        await Service(working).EvaluateAsync(_tent, Ph(5.0));

        Assert.Single(working.Requests);
        Assert.Equal(AlertEvaluationService.Below, StoredRule().LastState);
    }

    [Fact]
    public async Task DisabledRule_IsNeverEvaluated()
    {
        _rules.ReplaceForTent(_tent.Id, new[]
        {
            new TentAlertRule { TentId = _tent.Id, MetricKey = "reservoir-ph", MinValue = 5.5, Enabled = false },
        });
        var handler = new RecordingHttpHandler((_, _) => RecordingHttpHandler.Json("[]"));

        await Service(handler).EvaluateAsync(_tent, Ph(4.0));

        Assert.Empty(handler.Requests);
    }
}
