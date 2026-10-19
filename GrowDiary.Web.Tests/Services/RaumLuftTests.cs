using System.Net;
using System.Text;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (cc.263, Raum Stufe 2): „{Raumname} Temp./RLF/VPD" kommen vom Raum. Geprüft wird das Ergebnis — was der Fork aus Home
/// Assistant liest und wie es heißt —, nicht nur die Hilfsfunktionen.
/// </summary>
public sealed class RaumLuftTests : IDisposable
{
    private readonly string _wurzel = Path.Combine(Path.GetTempPath(), "RaumLuft_" + Guid.NewGuid().ToString("N"));
    private readonly SteuerungRepository _steuerung;
    private readonly TentRepository _zelte;
    private readonly RaumService _raum;

    public RaumLuftTests()
    {
        Directory.CreateDirectory(_wurzel);
        var pfade = new AppPaths(_wurzel);
        TestDatabase.InitializeWithDefaultTent(pfade);
        _steuerung = new SteuerungRepository(pfade);
        _zelte = new TentRepository(pfade);
        _raum = new RaumService(_steuerung, _zelte);
    }

    public void Dispose()
    {
        AussenBeschriftung.Quelle = null;
        try { Directory.Delete(_wurzel, recursive: true); } catch (IOException) { }
    }

    [Theory]
    [InlineData(18.7, 65.8, 0.74)]   // Fühler am AC-Infinity-Controller, 19.10.2026: eigener VPD-Wert 0,74 kPa
    [InlineData(25.0, 50.0, 1.58)]
    [InlineData(20.0, 100.0, 0.0)]
    public void Die_Raum_VPD_stimmt_mit_dem_Fuehler_des_Controllers_ueberein(double temp, double rh, double erwartet)
        => Assert.Equal(erwartet, RaumLuft.Vpd(temp, rh));

    [Theory]
    [InlineData(20.0, 120.0)]
    [InlineData(20.0, -1.0)]
    [InlineData(200.0, 50.0)]
    [InlineData(double.NaN, 50.0)]
    public void Unsinnige_Werte_ergeben_keine_VPD(double temp, double rh) => Assert.Null(RaumLuft.Vpd(temp, rh));

    [Fact]
    public void Ohne_Raum_Fuehler_gilt_die_eigene_Zuordnung_des_Zelts_unveraendert()
    {
        var zelt = ZeltMitAussen();
        Assert.Same(zelt.Sensors, RaumLuft.Wirksam(zelt, null));
    }

    [Fact]
    public void Mit_Raum_Fuehlern_ersetzen_sie_die_drei_Aussen_Zeilen_und_alles_andere_bleibt()
    {
        var zelt = ZeltMitAussen();
        var wirksam = RaumLuft.Wirksam(zelt, new RaumFuehler("sensor.raum_t", "sensor.raum_f"));

        Assert.Equal("sensor.raum_t", wirksam.Single(s => s.MetricType == SensorMetricType.OutsideTemperature).HaEntityId);
        Assert.Equal("sensor.raum_f", wirksam.Single(s => s.MetricType == SensorMetricType.OutsideHumidity).HaEntityId);
        Assert.DoesNotContain(wirksam, s => s.MetricType == SensorMetricType.OutsideVpd);
        Assert.Equal("sensor.zelt_t", wirksam.Single(s => s.MetricType == SensorMetricType.AirTemperature).HaEntityId);
        // Die gespeicherte Zuordnung des Zelts bleibt unberührt (Rückweg ohne Verlust).
        Assert.Equal("sensor.alt_aussen_t", zelt.Sensors.Single(s => s.MetricType == SensorMetricType.OutsideTemperature).HaEntityId);
    }

    [Fact]
    public void VpdErgaenzen_rechnet_aus_den_beiden_Raumwerten_und_verwirft_eine_fremde_VPD()
    {
        var staende = new Dictionary<string, HomeAssistantState>
        {
            ["outside-temperature"] = Zustand("sensor.raum_t", "18.7"),
            ["outside-humidity"] = Zustand("sensor.raum_f", "65.8"),
            ["outside-vpd"] = Zustand("sensor.alt_vpd", "9.99"),
        };
        RaumLuft.VpdErgaenzen(staende);
        Assert.Equal(0.74, staende["outside-vpd"].NumericValue);
        Assert.Equal("kPa", staende["outside-vpd"].UnitOfMeasurement);

        staende.Remove("outside-humidity");
        RaumLuft.VpdErgaenzen(staende);
        Assert.DoesNotContain("outside-vpd", staende.Keys);
    }

    [Fact]
    public async Task Ein_Zelt_im_Raum_liest_die_Raumwerte_statt_der_eigenen_Aussen_Zeilen_und_rechnet_die_VPD()
    {
        var zelt = _zelte.CreateTent(new Tent { Name = "Z", RaumId = RaumService.HauptRaumId });
        zelt.Sensors = ZeltMitAussen().Sensors;
        _steuerung.SetGeraet("raum", "raum_temp", "sensor.raum_t");
        _steuerung.SetGeraet("raum", "raum_rh", "sensor.raum_f");
        var abgefragt = new List<string>();
        var dienst = new HomeAssistantService(new Fabrik(a =>
        {
            abgefragt.Add(a);
            return a switch
            {
                "/api/states/sensor.raum_t" => Stand("18.7", "°C"),
                "/api/states/sensor.raum_f" => Stand("65.8", "%"),
                "/api/states/sensor.zelt_t" => Stand("23.0", "°C"),
                _ => null,
            };
        }), NullLogger<HomeAssistantService>.Instance, raum: _raum);

        var staende = await dienst.GetStatesAsync(new HomeAssistantSettings { BaseUrl = "http://ha:8123", AccessToken = "t", Enabled = true }, zelt);

        Assert.Equal(18.7, staende["outside-temperature"].NumericValue);
        Assert.Equal(65.8, staende["outside-humidity"].NumericValue);
        Assert.Equal(0.74, staende["outside-vpd"].NumericValue);
        Assert.Equal(23.0, staende["temperature"].NumericValue);
        Assert.DoesNotContain("/api/states/sensor.alt_aussen_t", abgefragt);
        Assert.DoesNotContain("/api/states/sensor.alt_aussen_vpd", abgefragt);
    }

    [Fact]
    public async Task Ein_Zelt_ohne_Raum_liest_weiter_seine_eigenen_Aussen_Zeilen()
    {
        var zelt = _zelte.CreateTent(new Tent { Name = "Z" });
        zelt.Sensors = ZeltMitAussen().Sensors;
        _steuerung.SetGeraet("raum", "raum_temp", "sensor.raum_t");
        _steuerung.SetGeraet("raum", "raum_rh", "sensor.raum_f");
        var dienst = new HomeAssistantService(new Fabrik(a => a switch
        {
            "/api/states/sensor.alt_aussen_t" => Stand("11.0", "°C"),
            "/api/states/sensor.alt_aussen_vpd" => Stand("0.50", "kPa"),
            _ => null,
        }), NullLogger<HomeAssistantService>.Instance, raum: _raum);

        var staende = await dienst.GetStatesAsync(new HomeAssistantSettings { BaseUrl = "http://ha:8123", AccessToken = "t", Enabled = true }, zelt);

        Assert.Equal(11.0, staende["outside-temperature"].NumericValue);
        Assert.Equal(0.5, staende["outside-vpd"].NumericValue);
    }

    [Fact]
    public void Die_Beschriftung_heisst_nach_dem_Raum_solange_ein_Zelt_drin_steht_und_beide_Fuehler_da_sind()
    {
        AussenBeschriftung.Quelle = _raum.AussenBeschriftungsName;
        Assert.Equal("Außen Temp.", AlertEvaluationService.MetricDisplay("outside-temperature").Label);

        _raum.NameSpeichern("Keller");
        _zelte.CreateTent(new Tent { Name = "Z", RaumId = RaumService.HauptRaumId });
        _steuerung.SetGeraet("raum", "raum_temp", "sensor.raum_t");
        Assert.False(_raum.Stand().AussenWerte); // nur ein Fühler
        _steuerung.SetGeraet("raum", "raum_rh", "sensor.raum_f");

        Assert.True(_raum.Stand().AussenWerte);
        Assert.Equal("Keller Temp.", RaumNamenFrisch(() => AlertEvaluationService.MetricDisplay("outside-temperature").Label));
        Assert.Equal("Keller RLF", AlertEvaluationService.MetricDisplay("outside-humidity").Label);
        Assert.Equal("Keller VPD", AlertEvaluationService.MetricDisplay("outside-vpd").Label);
    }

    // Die Beschriftung merkt sich den Namen kurz; der Test wartet nicht, sondern ändert den Namen (das leert den Merker).
    private string RaumNamenFrisch(Func<string> lesen) { _raum.NameSpeichern("Keller"); return lesen(); }

    private static Tent ZeltMitAussen() => new()
    {
        Id = 7,
        Sensors =
        [
            new TentSensor { TentId = 7, MetricType = SensorMetricType.AirTemperature, HaEntityId = "sensor.zelt_t", IsActive = true },
            new TentSensor { TentId = 7, MetricType = SensorMetricType.OutsideTemperature, HaEntityId = "sensor.alt_aussen_t", IsActive = true },
            new TentSensor { TentId = 7, MetricType = SensorMetricType.OutsideHumidity, HaEntityId = "sensor.alt_aussen_f", IsActive = true },
            new TentSensor { TentId = 7, MetricType = SensorMetricType.OutsideVpd, HaEntityId = "sensor.alt_aussen_vpd", IsActive = true },
        ],
    };

    private static HomeAssistantState Zustand(string entity, string wert) => new() { EntityId = entity, State = wert };

    private static HttpResponseMessage? Stand(string wert, string einheit)
    {
        var json = "{\"entity_id\":\"x\",\"state\":\"" + wert + "\",\"last_updated\":\"2026-10-19T10:00:00Z\",\"last_changed\":\"2026-10-19T10:00:00Z\",\"attributes\":{\"unit_of_measurement\":\"" + einheit + "\"}}";
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private sealed class Fabrik(Func<string, HttpResponseMessage?> antwort) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(antwort)) { BaseAddress = new Uri("http://ha:8123/") };
        private sealed class Handler(Func<string, HttpResponseMessage?> antwort) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
                => Task.FromResult(antwort(request.RequestUri!.AbsolutePath) ?? new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
