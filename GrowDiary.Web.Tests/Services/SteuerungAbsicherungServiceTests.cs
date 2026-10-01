using System.Net;
using System.Text.Json.Nodes;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Tests.TestFakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
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
    private const string LichtAus = "automation.co2_dosierung_licht_aus_sicherung_rdwc_port_5";

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
            ["1788411591900"] = Echt("licht-aus-sicherung"),
        };

        public int DosierungLaeuft { get; set; }
        public string Port { get; set; } = "off";

        /// <summary>Nimmt Geschriebenes an, behält aber den alten Stand (ein HA, das still verwirft).</summary>
        public bool VerwirftGeschriebenes { get; set; }

        /// <summary>Nach dem ersten Schreiben läuft eine Dosierung an.</summary>
        public bool DosierungStartetNachErstemSchreiben { get; set; }

        /// <summary>Für diese Kennung scheitert das zweite Lesen — das ist das Lesen zum Sichern.</summary>
        public string? SicherungScheitertFuer { get; set; }

        private readonly Dictionary<string, int> _gelesen = new();
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
                          {"entity_id":"{{{LichtAus}}}","state":"on","attributes":{"id":"1788411591900","friendly_name":"CO2 Dosierung Licht-aus Sicherung (RDWC Port 5)","current":0}},
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
                        if (!VerwirftGeschriebenes) Configs[id] = (JsonObject)JsonNode.Parse(inhalt!)!;
                        if (DosierungStartetNachErstemSchreiben) DosierungLaeuft = 1;
                        return RecordingHttpHandler.Json("""{"result":"ok"}""");
                    }
                    _gelesen[id] = _gelesen.GetValueOrDefault(id) + 1;
                    if (id == SicherungScheitertFuer && _gelesen[id] == 2)
                    {
                        return new HttpResponseMessage(HttpStatusCode.InternalServerError);
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

    [Fact]
    public async Task Absichern_WennHomeAssistantDenAltenStandBehaelt_MeldetEsDas()
    {
        var ha = new NachgebautesHa { VerwirftGeschriebenes = true };
        var bilanz = await ha.Dienst().AbsichernAsync(Rollen, Einstellungen, default);

        Assert.NotEmpty(bilanz.Einzeln);
        Assert.All(bilanz.Einzeln, e =>
        {
            Assert.False(e.Geschrieben);
            Assert.Contains("alte Fassung", e.Fehler);
        });
    }

    [Fact]
    public async Task Absichern_WennMittendrinEineDosierungAnlaeuft_HaeltEsAn()
    {
        var ha = new NachgebautesHa { DosierungStartetNachErstemSchreiben = true };
        var bilanz = await ha.Dienst().AbsichernAsync(Rollen, Einstellungen, default);

        Assert.Equal(1, ha.Geschrieben);
        Assert.Contains("Mitten im Absichern", bilanz.Abgelehnt);
    }

    [Fact]
    public async Task Absichern_OhneSicherung_WirdNichtGeschrieben()
    {
        var ha = new NachgebautesHa { SicherungScheitertFuer = "1788930088786" };
        var bilanz = await ha.Dienst().AbsichernAsync(Rollen, Einstellungen, default);

        var waechter = Assert.Single(bilanz.Einzeln, e => e.EntityId == Waechter);
        Assert.False(waechter.Geschrieben);
        Assert.Contains("sichern", waechter.Fehler);
        Assert.DoesNotContain(ha.Handler.Requests, r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath.EndsWith("1788930088786"));
        Assert.True(Assert.Single(bilanz.Einzeln, e => e.EntityId == Dosierung).Geschrieben);
    }

    [Fact]
    public async Task Pruefen_FindetUeberDieSucheAuchDieLichtAusSicherung()
    {
        // Ein echter WebSocket, der wie Home Assistant auf search/related antwortet.
        await using var socket = await NachgebauterSocket.StartenAsync(Dosierung, [LichtAus]);
        var einstellungen = new HomeAssistantSettings { Enabled = true, BaseUrl = socket.Adresse, AccessToken = "test" };

        var ha = new NachgebautesHa();
        var lage = await ha.Dienst().PruefenAsync(Rollen, einstellungen, default);

        Assert.Null(lage.Hinweis);
        var lichtAus = Assert.Single(lage.Automationen, a => a.EntityId == LichtAus);
        Assert.Equal(Co2Absicherung.Art.NotAusWirdZurueckgenommen, Assert.Single(lichtAus.Befunde).Art);
        Assert.Contains(socket.Gefragt, f => f == Dosierung);
    }
}

/// <summary>Ein WebSocket nach Art von Home Assistant: Anmeldung, dann search/related.</summary>
internal sealed class NachgebauterSocket : IAsyncDisposable
{
    private readonly Microsoft.AspNetCore.Builder.WebApplication _app;
    public string Adresse { get; }
    public System.Collections.Concurrent.ConcurrentBag<string> Gefragt { get; } = [];

    private NachgebauterSocket(Microsoft.AspNetCore.Builder.WebApplication app, string adresse)
    {
        _app = app;
        Adresse = adresse;
    }

    public static async Task<NachgebauterSocket> StartenAsync(string dosierung, string[] verwandte)
    {
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        NachgebauterSocket? selbst = null;
        app.UseWebSockets();
        app.Map("/api/websocket", async (Microsoft.AspNetCore.Http.HttpContext http) =>
        {
            using var ws = await http.WebSockets.AcceptWebSocketAsync();
            async Task Senden(object o) => await ws.SendAsync(
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(o), System.Net.WebSockets.WebSocketMessageType.Text, true, default);
            async Task<JsonNode?> Lesen()
            {
                var puffer = new byte[64 * 1024];
                var r = await ws.ReceiveAsync(puffer, default);
                return r.MessageType == System.Net.WebSockets.WebSocketMessageType.Close ? null : JsonNode.Parse(puffer.AsSpan(0, r.Count));
            }

            await Senden(new { type = "auth_required" });
            await Lesen();
            await Senden(new { type = "auth_ok" });
            while (await Lesen() is { } nachricht)
            {
                var id = nachricht["id"]!.GetValue<int>();
                var gefragt = nachricht["item_id"]?.ToString() ?? string.Empty;
                selbst!.Gefragt.Add(gefragt);
                object ergebnis = gefragt == dosierung ? new { automation = verwandte } : new { };
                await Senden(new { id, type = "result", success = true, result = ergebnis });
            }
        });
        await app.StartAsync();
        var adresse = app.Urls.First();
        selbst = new NachgebauterSocket(app, adresse);
        return selbst;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
