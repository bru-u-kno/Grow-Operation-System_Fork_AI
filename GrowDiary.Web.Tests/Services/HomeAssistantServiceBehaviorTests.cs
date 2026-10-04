using System.Net;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Tests.TestFakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Behavior tests for HomeAssistantService against faked HA HTTP responses: entity
/// parsing, the supervisor base-path regression, error handling, the circuit breaker,
/// and the exact notify calls Grow OS sends.
/// </summary>
public sealed class HomeAssistantServiceBehaviorTests
{
    private static readonly HomeAssistantSettings Settings = new()
    {
        BaseUrl = "http://ha.local:8123",
        AccessToken = "test-token",
        Enabled = true,
    };

    private static Tent TentWithSensors(params (SensorMetricType Type, string EntityId)[] sensors) => new()
    {
        Id = 1,
        Name = "Testzelt",
        Sensors = sensors.Select(s => new TentSensor { MetricType = s.Type, HaEntityId = s.EntityId, IsActive = true }).ToList(),
    };

    private static HomeAssistantService Service(RecordingHttpHandler handler)
        => new(new StubHttpClientFactory(handler), NullLogger<HomeAssistantService>.Instance);

    // ---------- GetStatesAsync ----------

    [Fact]
    public async Task GetStates_ParsesEntities_IntoMetricKeyedStates()
    {
        var handler = new RecordingHttpHandler((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/states/sensor.bluelab_ph" => RecordingHttpHandler.Json(
                RecordingHttpHandler.EntityStateJson("sensor.bluelab_ph", "6.12", unit: "pH", friendlyName: "Guardian pH")),
            "/api/states/sensor.bluelab_temp" => RecordingHttpHandler.Json(
                RecordingHttpHandler.EntityStateJson("sensor.bluelab_temp", "20.5", unit: "°C")),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

        var tent = TentWithSensors(
            (SensorMetricType.ReservoirPh, "sensor.bluelab_ph"),
            (SensorMetricType.ReservoirWaterTemp, "sensor.bluelab_temp"));

        var states = await Service(handler).GetStatesAsync(Settings, tent);

        Assert.Equal(2, states.Count);
        Assert.Equal(6.12, states["reservoir-ph"].NumericValue);
        Assert.Equal("pH", states["reservoir-ph"].UnitOfMeasurement);
        Assert.Equal("Guardian pH", states["reservoir-ph"].FriendlyName);
        Assert.Equal(20.5, states["reservoir-temp"].NumericValue);
    }

    [Fact]
    public async Task GetStates_SupervisorBaseUrl_KeepsCorePathSegment()
    {
        // Regression: with BaseAddress http://supervisor/core a leading-slash request path
        // used to drop "/core" and silently hit the wrong endpoint (empty dropdowns, blank
        // live values in the add-on).
        var handler = new RecordingHttpHandler((_, _) => RecordingHttpHandler.Json(
            RecordingHttpHandler.EntityStateJson("sensor.ph", "6.0")));

        var supervisorSettings = new HomeAssistantSettings { BaseUrl = "http://supervisor/core", AccessToken = "t", Enabled = true };
        await Service(handler).GetStatesAsync(supervisorSettings, TentWithSensors((SensorMetricType.ReservoirPh, "sensor.ph")));

        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://supervisor/core/api/states/sensor.ph", request.Uri.ToString());
    }

    [Fact]
    public async Task GetStates_UnavailableEntity_HasNoNumericValue()
    {
        var handler = new RecordingHttpHandler((_, _) => RecordingHttpHandler.Json(
            RecordingHttpHandler.EntityStateJson("sensor.ph", "unavailable")));

        var states = await Service(handler).GetStatesAsync(Settings, TentWithSensors((SensorMetricType.ReservoirPh, "sensor.ph")));

        Assert.Equal("unavailable", states["reservoir-ph"].State);
        Assert.Null(states["reservoir-ph"].NumericValue);
    }

    [Fact]
    public async Task GetStates_NotFoundEntity_IsSkipped_WithoutOpeningCircuit()
    {
        var handler = new RecordingHttpHandler((request, _) => request.RequestUri!.AbsolutePath.EndsWith("sensor.gone")
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : RecordingHttpHandler.Json(RecordingHttpHandler.EntityStateJson("sensor.ph", "6.0")));

        var service = Service(handler);
        var tent = TentWithSensors((SensorMetricType.ReservoirPh, "sensor.ph"), (SensorMetricType.ReservoirEc, "sensor.gone"));

        var states = await service.GetStatesAsync(Settings, tent);
        Assert.Single(states);
        Assert.True(states.ContainsKey("reservoir-ph"));

        // A plain 404 is not a transport failure: the next poll must still reach HA.
        var requestsAfterFirst = handler.Requests.Count;
        await service.GetStatesAsync(Settings, tent);
        Assert.True(handler.Requests.Count > requestsAfterFirst);
    }

    [Fact]
    public async Task GetStates_TransportFailure_OpensCircuit_AndSkipsNextPoll()
    {
        var handler = new RecordingHttpHandler((_, _) => throw new HttpRequestException("connection refused"));
        var service = Service(handler);
        var tent = TentWithSensors((SensorMetricType.ReservoirPh, "sensor.ph"));

        var first = await service.GetStatesAsync(Settings, tent);
        Assert.Empty(first);
        var requestsAfterFirst = handler.Requests.Count;

        // Circuit is open: the immediate next poll must not hit HA at all.
        var second = await service.GetStatesAsync(Settings, tent);
        Assert.Empty(second);
        Assert.Equal(requestsAfterFirst, handler.Requests.Count);
    }

    [Fact]
    public async Task GetStates_InactiveOrUnmappedSensors_AreNotQueried()
    {
        var handler = new RecordingHttpHandler((_, _) => RecordingHttpHandler.Json(
            RecordingHttpHandler.EntityStateJson("sensor.ph", "6.0")));

        var tent = new Tent
        {
            Id = 1,
            Name = "T",
            Sensors = new List<TentSensor>
            {
                new() { MetricType = SensorMetricType.ReservoirPh, HaEntityId = "sensor.ph", IsActive = true },
                new() { MetricType = SensorMetricType.ReservoirEc, HaEntityId = "sensor.ec", IsActive = false },
                new() { MetricType = SensorMetricType.ReservoirOrp, HaEntityId = "  ", IsActive = true },
            },
        };

        await Service(handler).GetStatesAsync(Settings, tent);

        var request = Assert.Single(handler.Requests);
        Assert.EndsWith("/api/states/sensor.ph", request.Uri.ToString());
    }

    // ---------- SendNotificationAsync ----------

    [Fact]
    public async Task SendNotification_PostsToNotifyService_WithTitleAndMessage()
    {
        var handler = new RecordingHttpHandler((_, _) => RecordingHttpHandler.Json("[]"));

        var sent = await Service(handler).SendNotificationAsync(Settings, "notify.mobile_app_pixel", "🌱 Grow OS", "pH über Zielbereich: 6.8.");

        Assert.True(sent);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://ha.local:8123/api/services/notify/mobile_app_pixel", request.Uri.ToString());
        Assert.Contains("\"title\":", request.Body);
        Assert.Contains("Grow OS", request.Body);
        Assert.Contains("\"message\":", request.Body);
        Assert.Contains("Zielbereich: 6.8.", request.Body);
    }

    [Fact]
    public async Task SendNotification_WithoutClickPath_SendsNoDataBlock()
    {
        // Ohne Ziel bleibt das Payload genau wie frueher — sonst braechte ein
        // leeres data-Objekt Companion-Apps unnoetig zum Nachdenken.
        var handler = new RecordingHttpHandler((_, _) => RecordingHttpHandler.Json("[]"));

        await Service(handler).SendNotificationAsync(Settings, "notify.mobile_app_pixel", "t", "m");

        Assert.DoesNotContain("\"data\"", Assert.Single(handler.Requests).Body);
    }

    [Fact]
    public async Task SendNotification_WithClickPath_TellsBothPhonesWhereToGo()
    {
        // Der Tipp auf die Meldung landete bisher auf der HA-Startseite. Mit
        // Ziel kommt der Nutzer dort an, wo die Sache zu erledigen ist:
        // `clickAction` liest Android, `url` liest iOS.
        var handler = new RecordingHttpHandler((_, _) => RecordingHttpHandler.Json("[]"));

        await Service(handler).SendNotificationAsync(
            Settings, "notify.mobile_app_pixel", "t", "m", default, "/app/local_grow_os/aufgaben");

        var body = Assert.Single(handler.Requests).Body;
        Assert.Contains("\"clickAction\":\"/app/local_grow_os/aufgaben\"", body);
        Assert.Contains("\"url\":\"/app/local_grow_os/aufgaben\"", body);
    }

    [Fact]
    public async Task SendNotification_ServiceWithoutDomain_DefaultsToNotify()
    {
        var handler = new RecordingHttpHandler((_, _) => RecordingHttpHandler.Json("[]"));

        await Service(handler).SendNotificationAsync(Settings, "mobile_app_pixel", "t", "m");

        Assert.EndsWith("/api/services/notify/mobile_app_pixel", Assert.Single(handler.Requests).Uri.ToString());
    }

    [Fact]
    public async Task SendNotification_HaError_ReturnsFalse()
    {
        var handler = new RecordingHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        Assert.False(await Service(handler).SendNotificationAsync(Settings, "notify.mobile_app_pixel", "t", "m"));
    }

    [Fact]
    public async Task SendNotification_Unconfigured_DoesNotCallHa()
    {
        var handler = new RecordingHttpHandler((_, _) => RecordingHttpHandler.Json("[]"));

        var sent = await Service(handler).SendNotificationAsync(new HomeAssistantSettings(), "notify.x", "t", "m");

        Assert.False(sent);
        Assert.Empty(handler.Requests);
    }

    // ---------- GetNotifyServicesAsync ----------

    [Fact]
    public async Task GetNotifyServices_ReturnsOnlyNotifyDomain_Sorted()
    {
        const string servicesJson = """
            [
              {"domain":"light","services":{"turn_on":{},"turn_off":{}}},
              {"domain":"notify","services":{"persistent_notification":{},"mobile_app_pixel":{}}}
            ]
            """;
        var handler = new RecordingHttpHandler((_, _) => RecordingHttpHandler.Json(servicesJson));

        var services = await Service(handler).GetNotifyServicesAsync(Settings);

        Assert.Equal(new[] { "notify.mobile_app_pixel", "notify.persistent_notification" }, services);
    }

    [Fact]
    public async Task GetNotifyServices_HaUnreachable_ReturnsEmpty()
    {
        var handler = new RecordingHttpHandler((_, _) => throw new HttpRequestException("down"));

        Assert.Empty(await Service(handler).GetNotifyServicesAsync(Settings));
    }
}
