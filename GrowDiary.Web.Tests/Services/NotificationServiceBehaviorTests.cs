using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Tests.TestFakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Behavior tests for the central notification gateway: settings persistence and the
/// gating rules (unconfigured / category off / quiet hours) verified by whether an HTTP
/// call actually reaches the faked Home Assistant.
/// </summary>
public sealed class NotificationServiceBehaviorTests : IDisposable
{
    private readonly string _contentRoot;
    private readonly AppPaths _paths;
    private readonly NotificationSettingsRepository _settingsRepo;
    private readonly GrowRepository _growRepository;
    private readonly string? _savedSupervisorToken;

    public NotificationServiceBehaviorTests()
    {
        _contentRoot = Path.Combine(Path.GetTempPath(), $"grow-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_contentRoot);
        _paths = new AppPaths(_contentRoot);
        TestDatabase.Initialize(_paths);

        _settingsRepo = new NotificationSettingsRepository(_paths);
        _growRepository = new GrowRepository(_paths);

        // The add-on env token would override the stored HA settings — keep tests hermetic.
        _savedSupervisorToken = Environment.GetEnvironmentVariable(HomeAssistantAddon.SupervisorTokenEnvironmentVariable);
        Environment.SetEnvironmentVariable(HomeAssistantAddon.SupervisorTokenEnvironmentVariable, null);

        _growRepository.SaveHomeAssistantSettings(new HomeAssistantSettings
        {
            BaseUrl = "http://ha.local:8123",
            AccessToken = "token",
            Enabled = true,
        });
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(HomeAssistantAddon.SupervisorTokenEnvironmentVariable, _savedSupervisorToken);
        try { Directory.Delete(_contentRoot, recursive: true); } catch { }
    }

    private NotificationService Service(RecordingHttpHandler handler)
        => new(
            _settingsRepo,
            _growRepository,
            new HomeAssistantService(new StubHttpClientFactory(handler), NullLogger<HomeAssistantService>.Instance),
            NullLogger<NotificationService>.Instance);

    private static RecordingHttpHandler OkHandler() => new((_, _) => RecordingHttpHandler.Json("[]"));

    private void SaveSettings(Action<NotificationSettings> mutate)
    {
        var settings = new NotificationSettings { NotifyService = "notify.mobile_app_test" };
        mutate(settings);
        _settingsRepo.SaveNotificationSettings(settings);
    }

    [Fact]
    public void Settings_Roundtrip_PersistsEverything()
    {
        _settingsRepo.SaveNotificationSettings(new NotificationSettings
        {
            NotifyService = "notify.mobile_app_pixel",
            QuietHoursStartHour = 22,
            QuietHoursEndHour = 7,
            Thresholds = false,
            Calibration = true,
            SensorOffline = false,
        });

        var loaded = _settingsRepo.GetNotificationSettings();

        Assert.Equal("notify.mobile_app_pixel", loaded.NotifyService);
        Assert.Equal(22, loaded.QuietHoursStartHour);
        Assert.Equal(7, loaded.QuietHoursEndHour);
        Assert.False(loaded.Thresholds);
        Assert.True(loaded.Calibration);
        Assert.False(loaded.SensorOffline);
    }

    [Fact]
    public void Settings_FreshDatabase_DefaultsToAllCategoriesOn_NoService()
    {
        var loaded = _settingsRepo.GetNotificationSettings();

        Assert.Null(loaded.NotifyService);
        Assert.False(loaded.IsConfigured);
        Assert.True(loaded.Thresholds);
        Assert.True(loaded.Calibration);
        Assert.True(loaded.SensorOffline);
        Assert.Null(loaded.QuietHoursStartHour);
    }

    [Fact]
    public async Task Send_WithoutNotifyService_DoesNotCallHa()
    {
        var handler = OkHandler();

        var sent = await Service(handler).SendAsync(NotificationCategory.Threshold, "t", "m");

        Assert.False(sent);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Send_CategoryDisabled_DoesNotCallHa()
    {
        SaveSettings(s => s.Thresholds = false);
        var handler = OkHandler();

        var sent = await Service(handler).SendAsync(NotificationCategory.Threshold, "t", "m");

        Assert.False(sent);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Send_DuringQuietHours_DoesNotCallHa()
    {
        /* Ein ZWEI Stunden breites Fenster ab der aktuellen Stunde.
           Vorher war es eine Stunde breit — und der Dienst liest die Uhr ein
           zweites Mal (NotificationService.SendAsync ruft
           settings.IsQuietHour(DateTime.Now.Hour)). Springt die Stunde zwischen
           den beiden Ablesungen um, deckt das Fenster "jetzt" nicht mehr, und
           der Fall faellt durch — einmal pro Stunde, ohne dass sich am Code
           etwas geaendert haette. Mit zwei Stunden ueberlebt er den Sprung. */
        var nowHour = DateTime.Now.Hour;
        SaveSettings(s =>
        {
            s.QuietHoursStartHour = nowHour;
            s.QuietHoursEndHour = (nowHour + 2) % 24;
        });
        var handler = OkHandler();

        var sent = await Service(handler).SendAsync(NotificationCategory.Calibration, "t", "m");

        Assert.False(sent);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Send_ConfiguredAndAllowed_PushesThroughHa()
    {
        SaveSettings(_ => { });
        var handler = OkHandler();

        var sent = await Service(handler).SendAsync(NotificationCategory.SensorOffline, "🌱 Cultivate Control", "Sensor liefert keine Werte mehr: pH.");

        Assert.True(sent);
        var request = Assert.Single(handler.Requests);
        Assert.EndsWith("/api/services/notify/mobile_app_test", request.Uri.ToString());
        Assert.Contains("Sensor liefert keine Werte mehr", request.Body);
    }

    [Fact]
    public async Task Send_OtherCategoryDisabled_DoesNotAffectThisOne()
    {
        SaveSettings(s => s.Thresholds = false);
        var handler = OkHandler();

        Assert.True(await Service(handler).SendAsync(NotificationCategory.Calibration, "t", "m"));
        Assert.Single(handler.Requests);
    }
}
