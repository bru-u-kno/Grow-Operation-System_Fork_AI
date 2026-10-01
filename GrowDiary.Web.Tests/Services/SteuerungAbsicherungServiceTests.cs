using System.Net;
using System.Text.Json.Nodes;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Tests.TestFakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Der Weg um die Absicherung herum: finden, sichern, schreiben, nachlesen — und
/// nichts schreiben, solange dosiert wird. Home Assistant ist hier ein Nachbau,
/// der die echten Automationen der Anlage ausliefert und Geschriebenes behält.
/// </summary>
public sealed class SteuerungAbsicherungServiceTests
{
    private const string Dosierung = "automation.co2_dosierung_rdwc_port_5";
    private const string Waechter = "automation.co2_wachter_rdwc_port_5";
    private const string PortZustand = "binary_sensor.big_port_5_zustand";

    private static readonly Dictionary<string, string> Rollen = new()
    {
        ["co2_sensor"] = "sensor.big_co2_light_sensor_co2",
        ["port_zustand"] = PortZustand,
    };

    // Ein Port, an dem nichts lauscht: der WebSocket scheitert sofort, die
    // Suche nach verwandten Automationen fällt aus — das soll die Seite sagen.
    private static readonly HomeAssistantSettings Einstellungen = new()
    {
        Enabled = true, BaseUrl = "http://127.0.0.1:9", AccessToken = "test",
    };

    private sealed class NachgebautesHa
    {
        public Dictionary<string, JsonObject> Configs { get; } = new()
        {
            ["1788411041559"] = Echt("dosierung"),
            ["1788930088786"] = Echt("waechter"),
        };

        public int DosierungLaeuft { get; set; }
        public string Port { get; set; } = "off";
        public RecordingHttpHandler Handler { get; }

        public NachgebautesHa()
        {
            Handler = new RecordingHttpHandler((anfrage, inhalt) =>
            {
                var pfad = anfrage.RequestUri!.AbsolutePath;
                if (pfad == "/api/states")
                {
                    return RecordingHttpHandler.Json($$$"""
                        [
                          {"entity_id":"{{{Dosierung}}}","state":"on","attributes":{"id":"1788411041559","friendly_name":"CO2 Dosierung (RDWC Port 5)","current":{{{DosierungLaeuft}}}}},
                          {"entity_id":"{{{Waechter}}}","state":"on","attributes":{"id":"1788930088786","friendly_name":"CO2 Wächter (RDWC Port 5)","current":0}},
                          {"entity_id":"automation.etwas_anderes","state":"on","attributes":{"id":"42","friendly_name":"Etwas anderes","current":0}}
                        ]
                        """);
                }
                if (pfad == $"/api/states/{PortZustand}")
                {
                    return RecordingHttpHandler.Json(RecordingHttpHandler.EntityStateJson(PortZustand, Port));
                }
                const string config = "/api/config/automation/config/";
                if (pfad.StartsWith(config, StringComparison.Ordinal))
                {
                    var id = pfad[config.Length..];
                    if (anfrage.Method == HttpMethod.Post)
                    {
                        Configs[id] = (JsonObject)JsonNode.Parse(inhalt!)!;
                        return RecordingHttpHandler.Json("""{"result":"ok"}""");
                    }
                    return Configs.TryGetValue(id, out var c)
                        ? RecordingHttpHandler.Json(c.ToJsonString())
                        : new HttpResponseMessage(HttpStatusCode.NotFound);
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });
        }

        public SteuerungAbsicherungService Dienst()
        {
            var ha = new HomeAssistantService(new StubHttpClientFactory(Handler), NullLogger<HomeAssistantService>.Instance);
            return new SteuerungAbsicherungService(
                ha,
                new SteuerungAutomationService(ha, NullLogger<SteuerungAutomationService>.Instance),
                NullLogger<SteuerungAbsicherungService>.Instance);
        }

        public int Geschrieben => Handler.Requests.Count(r => r.Method == HttpMethod.Post);
    }

    private static JsonObject Echt(string name)
        => (JsonObject)JsonNode.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Services", "Daten", "co2-handgebaut", name + ".json")))!;

    [Fact]
    public async Task Pruefen_FindetDieHandgebautenUndAendertNichts()
    {
        var ha = new NachgebautesHa();
        var lage = await ha.Dienst().PruefenAsync(Rollen, Einstellungen, default);

        Assert.True(lage.Erreichbar);
        Assert.False(lage.DosiertGerade);
        Assert.Equal(
            [Dosierung, Waechter],
            lage.Automationen.Select(a => a.EntityId).Order(StringComparer.Ordinal));
        Assert.Equal(3, lage.Behebbar);
        Assert.NotNull(lage.Hinweis); // die Suche nach verwandten ging nicht — gesagt, nicht verschwiegen
        Assert.Equal(0, ha.Geschrieben);
    }

    [Fact]
    public async Task Absichern_SchreibtNurDieBetroffenen_UndLiestNach()
    {
        var ha = new NachgebautesHa();
        var bilanz = await ha.Dienst().AbsichernAsync(Rollen, Einstellungen, default);

        Assert.Null(bilanz.Abgelehnt);
        Assert.Equal(2, bilanz.Einzeln.Count);
        Assert.All(bilanz.Einzeln, e => Assert.True(e.Geschrieben, e.Fehler));
        Assert.Equal(2, ha.Geschrieben);
        Assert.Equal(0, bilanz.Nachher.Behebbar);

        // Was in „Home Assistant“ steht, ist die abgesicherte Fassung.
        // Als Knoten verglichen: im JSON-Text steht „CO₂“ als \u2082.
        var ablauf = ha.Configs["1788411041559"]["actions"]![0]!["repeat"]!["sequence"]!.AsArray();
        Assert.Contains(ablauf, s => s?["alias"]?.GetValue<string>() == Co2Absicherung.SperreAlias);
        Assert.Contains(Co2Absicherung.NeustartKennung, ha.Configs["1788930088786"].ToJsonString());

        // Ein zweites Absichern schreibt nichts mehr.
        var zweites = await ha.Dienst().AbsichernAsync(Rollen, Einstellungen, default);
        Assert.Empty(zweites.Einzeln);
        Assert.Equal(2, ha.Geschrieben);
    }

    [Fact]
    public async Task Absichern_WaehrendDosiertWird_SchreibtNichts()
    {
        var ha = new NachgebautesHa { DosierungLaeuft = 1 };
        var bilanz = await ha.Dienst().AbsichernAsync(Rollen, Einstellungen, default);

        Assert.NotNull(bilanz.Abgelehnt);
        Assert.Contains("Impuls", bilanz.Abgelehnt);
        Assert.Equal(0, ha.Geschrieben);
    }

    [Fact]
    public async Task Absichern_WennDerPortOffenSteht_SchreibtNichts()
    {
        var ha = new NachgebautesHa { Port = "on" };
        var bilanz = await ha.Dienst().AbsichernAsync(Rollen, Einstellungen, default);

        Assert.NotNull(bilanz.Abgelehnt);
        Assert.Equal(0, ha.Geschrieben);
    }
}
