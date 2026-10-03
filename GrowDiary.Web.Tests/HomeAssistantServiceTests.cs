using System.Net;
using System.Net.Http.Headers;
using System.Text;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests;

public sealed class HomeAssistantServiceTests
{
    [Fact]
    public async Task GetStatesAsync_UsesTentSensorsAndMapsMetricKeys()
    {
        var service = new HomeAssistantService(
            new FakeHttpClientFactory(request =>
            {
                return request.RequestUri?.AbsolutePath switch
                {
                    "/api/states/sensor.temp" => Json("""
                        {
                          "entity_id": "sensor.temp",
                          "state": "24.5",
                          "last_changed": "2026-05-05T10:00:00Z",
                          "attributes": {
                            "friendly_name": "Tent Temp",
                            "unit_of_measurement": "°C"
                          }
                        }
                        """),
                    "/api/states/sensor.do" => Json("""
                        {
                          "entity_id": "sensor.do",
                          "state": "7.2",
                          "last_changed": "2026-05-05T10:00:00Z",
                          "attributes": {
                            "friendly_name": "DO",
                            "unit_of_measurement": "mg/L"
                          }
                        }
                        """),
                    _ => new HttpResponseMessage(HttpStatusCode.NotFound)
                };
            }),
            NullLogger<HomeAssistantService>.Instance);

        var tent = new Tent
        {
            Id = 7,
            Sensors =
            [
                new TentSensor { TentId = 7, MetricType = SensorMetricType.AirTemperature, HaEntityId = "sensor.temp", IsActive = true },
                new TentSensor { TentId = 7, MetricType = SensorMetricType.ReservoirDissolvedOxygen, HaEntityId = "sensor.do", IsActive = true },
                new TentSensor { TentId = 7, MetricType = SensorMetricType.Humidity, HaEntityId = "sensor.humidity", IsActive = false }
            ]
        };

        var result = await service.GetStatesAsync(CreateSettings(), tent);

        Assert.Equal(2, result.Count);
        Assert.Equal(24.5, result["temperature"].NumericValue);
        Assert.Equal("°C", result["temperature"].UnitOfMeasurement);
        Assert.Equal(7.2, result["dissolved-oxygen"].NumericValue);
        Assert.DoesNotContain("humidity", result.Keys);
    }

    [Fact]
    public async Task GetStatesAsync_ReturnsEmptyWhenHomeAssistantIsNotConfigured()
    {
        var service = new HomeAssistantService(
            new FakeHttpClientFactory(_ => throw new InvalidOperationException("Should not be called")),
            NullLogger<HomeAssistantService>.Instance);

        var result = await service.GetStatesAsync(
            new HomeAssistantSettings(),
            new Tent
            {
                Sensors = [new TentSensor { MetricType = SensorMetricType.AirTemperature, HaEntityId = "sensor.temp", IsActive = true }]
            });

        Assert.Empty(result);
    }

    /// <summary>
    /// Ein deutsch schreibender Vorlagen-Sensor: „5,8" ist 5,8 — nicht 58.
    /// </summary>
    /// <remarks>
    /// Geprüft am Weg, auf dem es passiert ist: der Zustand kommt als JSON von
    /// Home Assistant und wird im <see cref="HomeAssistantService"/> zur
    /// <c>NumericValue</c>, die als Messwert gespeichert wird.
    /// </remarks>
    [Theory]
    [InlineData("5,8", 5.8)]
    [InlineData("5.8", 5.8)]
    [InlineData("1,234", 1.234)]
    [InlineData("nan", null)]
    [InlineData("unavailable", null)]
    public async Task HomeAssistantZustand_WirdNichtZurTausenderzahl(string zustand, double? erwartet)
    {
        var service = new HomeAssistantService(
            new FakeHttpClientFactory(_ => Json(
                $$$"""{"entity_id":"sensor.ph","state":"{{{zustand}}}","attributes":{}}""")),
            NullLogger<HomeAssistantService>.Instance);

        var zelt = new Tent
        {
            Id = 1,
            Sensors = [new TentSensor { TentId = 1, MetricType = SensorMetricType.ReservoirPh, HaEntityId = "sensor.ph", IsActive = true }],
        };
        var staende = await service.GetStatesAsync(CreateSettings(), zelt);

        var stand = Assert.Single(staende).Value;
        Assert.Equal(erwartet, stand.NumericValue);
    }

    private static HomeAssistantSettings CreateSettings() => new()
    {
        BaseUrl = "http://homeassistant.local:8123",
        AccessToken = "token",
        Enabled = true
    };

    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public FakeHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public HttpClient CreateClient(string name)
            => new(new FakeHttpMessageHandler(_responseFactory))
            {
                BaseAddress = new Uri("http://localhost")
            };
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = _responseFactory(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
