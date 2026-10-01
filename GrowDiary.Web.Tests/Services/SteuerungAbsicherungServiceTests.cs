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

        /// <summary>Die Rechenwerte: Helfer-Eintrag → gespeicherte Optionen (echte Werte vom 30.09.2026).</summary>
        public Dictionary<string, JsonObject> Optionen { get; } = EchteOptionen();

        /// <summary>Entität → Helfer-Eintrag, wie config/entity_registry/get ihn nennt.</summary>
        public static Dictionary<string, string> Eintraege => EchteRechenwerte().ToDictionary(
            p => p.Key, p => p.Value!["config_entry_id"]!.ToString());

        /// <summary>Nach dem Speichern liefert der Rechenwert keinen Zustand mehr.</summary>
        public bool RechenwertFaelltAus { get; set; }

        /// <summary>Das Verhalten des Dialogs (ablehnen, Feld verlieren, Haken für Abbrüche).</summary>
        public OptionsDialog.Verhalten Dialog { get; } = new();

        /// <summary>Wird beim Abfragen des Zustands eines Rechenwerts aufgerufen.</summary>
        public Action? BeimZustand { get; set; }

        private readonly Dictionary<string, string> _dialoge = new();
        public int DialogeOffen => _dialoge.Count;
        public List<JsonObject> Abgeschickt { get; } = [];

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
                if (pfad.StartsWith("/api/states/", StringComparison.Ordinal) && Eintraege.ContainsKey(pfad["/api/states/".Length..]))
                {
                    var entity = pfad["/api/states/".Length..];
                    BeimZustand?.Invoke();
                    var wert = RechenwertFaelltAus ? "unavailable" : entity.StartsWith("binary_sensor", StringComparison.Ordinal) ? "on" : "9";
                    return RecordingHttpHandler.Json(RecordingHttpHandler.EntityStateJson(entity, wert));
                }
                if (pfad.StartsWith(OptionsDialog.Pfad, StringComparison.Ordinal))
                {
                    return OptionsDialog.Beantworten(anfrage, inhalt, Optionen, _dialoge, Abgeschickt, Dialog);
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
                NullLogger<SteuerungAbsicherungService>.Instance)
            {
                // Im Nachbau steht der Zustand sofort fest — kein Warten.
                Rechenwerte = new SteuerungRechenwertAbsicherung(ha, NullLogger.Instance)
                {
                    Wartezeit = TimeSpan.Zero, Takt = TimeSpan.Zero,
                },
            };
        }

        public int Geschrieben => Handler.Requests.Count(r => r.Method == HttpMethod.Post);
    }

    private static JsonObject Echt(string name)
        => (JsonObject)JsonNode.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Services", "Daten", "co2-handgebaut", name + ".json")))!;

    private static Dictionary<string, JsonNode?> EchteRechenwerte()
        => Echt("rechenwerte").Where(p => !p.Key.StartsWith('_')).ToDictionary(p => p.Key, p => p.Value);

    private static Dictionary<string, JsonObject> EchteOptionen()
        => EchteRechenwerte().ToDictionary(
            p => p.Value!["config_entry_id"]!.ToString(),
            p => (JsonObject)p.Value!["optionen"]!.DeepClone());

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

    // ------------------------------------------------------------ Rechenwerte

    private const string BedarfEintrag = "01M228M2H8E9NXMDH433K4F5TT";
    private const string ImpulsEintrag = "01M228KX5ZYT085T0Q16R72H29";

    [Fact]
    public async Task Rechenwerte_WerdenGelesen_UndJederDialogWirdWiederGeschlossen()
    {
        await using var socket = await NachgebauterSocket.StartenAsync(Dosierung, [LichtAus], NachgebautesHa.Eintraege);
        var einstellungen = new HomeAssistantSettings { Enabled = true, BaseUrl = socket.Adresse, AccessToken = "test" };
        var ha = new NachgebautesHa();

        var lage = await ha.Dienst().PruefenAsync(Rollen, einstellungen, default);

        Assert.Equal(2, lage.Rechenwerte.Count);
        Assert.All(lage.Rechenwerte, r => Assert.Equal("Veraltet", r.Stand));
        Assert.Equal("bleibt bis zu 5 Minuten „an“, bei 0 ppm unbegrenzt",
            lage.Rechenwerte.Single(r => r.EntityId == "binary_sensor.co2_bedarf").Heute);
        Assert.Equal(4 + 2, lage.Behebbar);

        Assert.Equal(0, ha.DialogeOffen);
        Assert.Empty(ha.Abgeschickt);
    }

    [Fact]
    public async Task Rechenwerte_Absichern_ErsetztDieFormel_UndBehaeltEinheitUndKlasse()
    {
        await using var socket = await NachgebauterSocket.StartenAsync(Dosierung, [LichtAus], NachgebautesHa.Eintraege);
        var einstellungen = new HomeAssistantSettings { Enabled = true, BaseUrl = socket.Adresse, AccessToken = "test" };
        var ha = new NachgebautesHa();

        var bilanz = await ha.Dienst().AbsichernAsync(Rollen, einstellungen, default);

        Assert.Null(bilanz.Abgelehnt);
        Assert.All(bilanz.Einzeln, e => Assert.True(e.Geschrieben, $"{e.EntityId}: {e.Fehler}"));
        Assert.Equal(0, bilanz.Nachher.Behebbar);
        Assert.All(bilanz.Nachher.Rechenwerte, r => Assert.Equal("Aktuell", r.Stand));

        var impuls = ha.Optionen[ImpulsEintrag];
        Assert.Equal(Co2Rechenwerte.Aktuelle("sensor.co2_impuls_bedarf", Rollen), impuls["state"]!.ToString());
        Assert.Equal("s", impuls["unit_of_measurement"]!.ToString());
        Assert.Equal("measurement", impuls["state_class"]!.ToString());
        Assert.Equal("CO2 Impuls Bedarf", impuls["name"]!.ToString());
        Assert.Equal(Co2Rechenwerte.Aktuelle("binary_sensor.co2_bedarf", Rollen), ha.Optionen[BedarfEintrag]["state"]!.ToString());

        Assert.Equal(2, ha.Abgeschickt.Count);
        Assert.Equal(0, ha.DialogeOffen);
    }

    [Fact]
    public async Task Rechenwerte_LiefertDerNeueNichts_KommtDieAlteFormelZurueck()
    {
        await using var socket = await NachgebauterSocket.StartenAsync(Dosierung, [LichtAus], NachgebautesHa.Eintraege);
        var einstellungen = new HomeAssistantSettings { Enabled = true, BaseUrl = socket.Adresse, AccessToken = "test" };
        var ha = new NachgebautesHa { RechenwertFaelltAus = true };
        var alt = ha.Optionen[BedarfEintrag]["state"]!.ToString();

        var bilanz = await ha.Dienst().AbsichernAsync(Rollen, einstellungen, default);

        var bedarf = Assert.Single(bilanz.Einzeln, e => e.EntityId == "binary_sensor.co2_bedarf");
        Assert.False(bedarf.Geschrieben);
        Assert.Contains("zurückgeschrieben", bedarf.Fehler);
        Assert.Equal(alt, ha.Optionen[BedarfEintrag]["state"]!.ToString());
        Assert.Equal("Veraltet", bilanz.Nachher.Rechenwerte.Single(r => r.EntityId == "binary_sensor.co2_bedarf").Stand);
    }

    [Fact]
    public async Task Rechenwerte_EineAngepassteFormel_WirdNurAngezeigt()
    {
        await using var socket = await NachgebauterSocket.StartenAsync(Dosierung, [LichtAus], NachgebautesHa.Eintraege);
        var einstellungen = new HomeAssistantSettings { Enabled = true, BaseUrl = socket.Adresse, AccessToken = "test" };
        var ha = new NachgebautesHa();
        var angepasst = ha.Optionen[BedarfEintrag]["state"]!.ToString().Replace("float(100)", "float(80)");
        ha.Optionen[BedarfEintrag]["state"] = angepasst;

        var lage = await ha.Dienst().PruefenAsync(Rollen, einstellungen, default);
        var bedarf = lage.Rechenwerte.Single(r => r.EntityId == "binary_sensor.co2_bedarf");
        Assert.Equal("Angepasst", bedarf.Stand);
        Assert.False(bedarf.Behebbar);
        Assert.Equal(angepasst, bedarf.AlteFormel);
        Assert.NotNull(bedarf.NeueFormel);

        await ha.Dienst().AbsichernAsync(Rollen, einstellungen, default);
        Assert.Equal(angepasst, ha.Optionen[BedarfEintrag]["state"]!.ToString());
        Assert.DoesNotContain(ha.Abgeschickt, a => a["state"]!.ToString().Contains("co2_bedarf", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rechenwerte_LehntHomeAssistantAb_WirdDerDialogGeschlossen_UndDerGrundGenannt()
    {
        await using var socket = await NachgebauterSocket.StartenAsync(Dosierung, [LichtAus], NachgebautesHa.Eintraege);
        var einstellungen = new HomeAssistantSettings { Enabled = true, BaseUrl = socket.Adresse, AccessToken = "test" };
        var ha = new NachgebautesHa();
        ha.Dialog.LehntAb = true;
        var alt = ha.Optionen[BedarfEintrag]["state"]!.ToString();

        var bilanz = await ha.Dienst().AbsichernAsync(Rollen, einstellungen, default);

        var bedarf = Assert.Single(bilanz.Einzeln, e => e.EntityId == "binary_sensor.co2_bedarf");
        Assert.False(bedarf.Geschrieben);
        Assert.Contains("expected str", bedarf.Fehler);
        Assert.Equal(0, ha.DialogeOffen);
        Assert.Equal(alt, ha.Optionen[BedarfEintrag]["state"]!.ToString());
    }

    [Fact]
    public async Task Rechenwerte_GehtBeimSchreibenEinWertVerloren_KommtDerGanzeAlteStandZurueck()
    {
        await using var socket = await NachgebauterSocket.StartenAsync(Dosierung, [LichtAus], NachgebautesHa.Eintraege);
        var einstellungen = new HomeAssistantSettings { Enabled = true, BaseUrl = socket.Adresse, AccessToken = "test" };
        var ha = new NachgebautesHa();
        // Der Bedarf wird zuerst geschrieben — damit trifft es den Impuls-Bedarf mit Einheit.
        ha.Dialog.BeimOeffnen = () => { if (ha.Abgeschickt.Count == 1) ha.Dialog.VerliertEinheit = true; };
        var alt = ha.Optionen[ImpulsEintrag]["state"]!.ToString();

        var bilanz = await ha.Dienst().AbsichernAsync(Rollen, einstellungen, default);

        var impuls = Assert.Single(bilanz.Einzeln, e => e.EntityId == "sensor.co2_impuls_bedarf");
        Assert.False(impuls.Geschrieben);
        Assert.Contains("andere Einstellung", impuls.Fehler);
        Assert.Equal(alt, ha.Optionen[ImpulsEintrag]["state"]!.ToString());
        Assert.Equal("s", ha.Optionen[ImpulsEintrag]["unit_of_measurement"]!.ToString());
        Assert.Equal(0, ha.DialogeOffen);
    }

    [Fact]
    public async Task Rechenwerte_VerlaesstDerBedienerDieSeiteBeimLesen_BleibtKeinDialogOffen()
    {
        await using var socket = await NachgebauterSocket.StartenAsync(Dosierung, [LichtAus], NachgebautesHa.Eintraege);
        var einstellungen = new HomeAssistantSettings { Enabled = true, BaseUrl = socket.Adresse, AccessToken = "test" };
        var ha = new NachgebautesHa();
        using var abbruch = new CancellationTokenSource();
        ha.Dialog.BeimOeffnen = abbruch.Cancel;

        await ha.Dienst().PruefenAsync(Rollen, einstellungen, abbruch.Token);

        Assert.True(abbruch.IsCancellationRequested, "Der Abbruch kam nie — der Test prüft nichts.");
        Assert.Equal(0, ha.DialogeOffen);
    }

    [Fact]
    public async Task Rechenwerte_AbbruchWaehrendDesNachsehens_SchreibtTrotzdemZurueck()
    {
        await using var socket = await NachgebauterSocket.StartenAsync(Dosierung, [LichtAus], NachgebautesHa.Eintraege);
        var einstellungen = new HomeAssistantSettings { Enabled = true, BaseUrl = socket.Adresse, AccessToken = "test" };
        var ha = new NachgebautesHa { RechenwertFaelltAus = true };
        using var abbruch = new CancellationTokenSource();
        ha.BeimZustand = abbruch.Cancel;
        var alt = ha.Optionen[BedarfEintrag]["state"]!.ToString();

        try
        {
            await ha.Dienst().AbsichernAsync(Rollen, einstellungen, abbruch.Token);
        }
        catch (OperationCanceledException)
        {
            // Der Abbruch darf die Anfrage beenden — aber erst nach dem Zurückschreiben.
        }

        Assert.True(abbruch.IsCancellationRequested, "Der Abbruch kam nie — der Test prüft nichts.");
        Assert.Equal(alt, ha.Optionen[BedarfEintrag]["state"]!.ToString());
        Assert.Equal(0, ha.DialogeOffen);
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

    public static async Task<NachgebauterSocket> StartenAsync(
        string dosierung, string[] verwandte, IReadOnlyDictionary<string, string>? eintraege = null)
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
                if (nachricht["type"]?.ToString() == "config/entity_registry/get")
                {
                    var entity = nachricht["entity_id"]!.ToString();
                    if (eintraege is not null && eintraege.TryGetValue(entity, out var eintrag))
                    {
                        await Senden(new { id, type = "result", success = true, result = new { entity_id = entity, config_entry_id = eintrag, platform = "template" } });
                    }
                    else
                    {
                        await Senden(new { id, type = "result", success = false, error = new { code = "not_found", message = "Entity not found" } });
                    }
                    continue;
                }
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

/// <summary>
/// Der Einstellungsdialog eines Template-Helfers, nachgebaut nach dem Quelltext
/// von Home Assistant 2026.9.4 (template/config_flow.py, schema_config_entry_flow.py):
/// Felder je Art, Vorbelegung aus den gespeicherten Optionen, Abschnitt
/// „additional_options“ als „expandable“, fremde Felder werden abgelehnt, und
/// fehlende optionale Felder beim Absenden gelöscht.
/// </summary>
internal static class OptionsDialog
{
    public const string Pfad = "/api/config/config_entries/options/flow";

    private static readonly Dictionary<string, (string Name, bool Pflicht)[]> Felder = new()
    {
        ["binary_sensor"] = [("state", true), ("device_class", false), ("device_id", false)],
        ["sensor"] = [("state", true), ("unit_of_measurement", false), ("device_class", false), ("state_class", false), ("device_id", false)],
    };

    /// <summary>Stellschrauben für Fehlerfälle.</summary>
    public sealed class Verhalten
    {
        /// <summary>Jedes Absenden wird abgelehnt (wie bei einem Schemafehler).</summary>
        public bool LehntAb { get; set; }

        /// <summary>Beim ersten Speichern geht die Einheit verloren.</summary>
        public bool VerliertEinheit { get; set; }

        /// <summary>Wird beim Öffnen eines Dialogs aufgerufen.</summary>
        public Action? BeimOeffnen { get; set; }
    }

    private static HttpResponseMessage Fehler400(string grund)
        => RecordingHttpHandler.Json(
            new JsonObject { ["errors"] = new JsonObject { ["base"] = new JsonArray(grund) } }.ToJsonString(),
            HttpStatusCode.BadRequest);

    public static HttpResponseMessage Beantworten(
        HttpRequestMessage anfrage, string? inhalt, Dictionary<string, JsonObject> optionen,
        Dictionary<string, string> dialoge, List<JsonObject> abgeschickt, Verhalten verhalten)
    {
        var pfad = anfrage.RequestUri!.AbsolutePath;
        if (pfad == Pfad && anfrage.Method == HttpMethod.Post)
        {
            var eintrag = JsonNode.Parse(inhalt!)!["handler"]!.ToString();
            if (!optionen.ContainsKey(eintrag)) return new HttpResponseMessage(HttpStatusCode.NotFound);
            var id = Guid.NewGuid().ToString("N");
            dialoge[id] = eintrag;
            verhalten.BeimOeffnen?.Invoke();
            return Formular(id, optionen[eintrag]);
        }

        var flow = pfad[(Pfad.Length + 1)..];
        if (!dialoge.TryGetValue(flow, out var e)) return new HttpResponseMessage(HttpStatusCode.NotFound);

        if (anfrage.Method == HttpMethod.Delete)
        {
            dialoge.Remove(flow);
            return RecordingHttpHandler.Json("""{"message":"Flow aborted"}""");
        }

        var eingabe = (JsonObject)JsonNode.Parse(inhalt!)!;
        abgeschickt.Add((JsonObject)eingabe.DeepClone());
        var o = optionen[e];
        var art = o["template_type"]!.ToString();
        var erlaubt = Felder[art].Select(f => f.Name).Append("additional_options").ToHashSet();

        // Ein Schemafehler: 400 mit {"errors": {"base": [...]}} (helpers/data_entry_flow.py),
        // und der Dialog bleibt offen (data_entry_flow.py entfernt ihn nur bei Abschluss oder Abbruch).
        if (eingabe.Select(p => p.Key).FirstOrDefault(k => !erlaubt.Contains(k)) is { } fremd)
        {
            return Fehler400($"extra keys not allowed @ data['{fremd}']");
        }
        if (verhalten.LehntAb) return Fehler400("expected str for dictionary value @ data['state']");

        // values.update(user_input); fehlende optionale Schlüssel werden gelöscht.
        foreach (var p in eingabe) o[p.Key] = p.Value?.DeepClone();
        foreach (var k in erlaubt.Where(k => k != "state" && !eingabe.ContainsKey(k))) o.Remove(k);
        if (verhalten.VerliertEinheit)
        {
            o.Remove("unit_of_measurement");
            verhalten.VerliertEinheit = false;
        }
        dialoge.Remove(flow);
        return RecordingHttpHandler.Json("""{"type":"create_entry","version":1}""");
    }

    private static HttpResponseMessage Formular(string id, JsonObject o)
    {
        var art = o["template_type"]!.ToString();
        var schema = new JsonArray();
        foreach (var (name, pflicht) in Felder[art])
        {
            var feld = new JsonObject { ["name"] = name, ["required"] = pflicht, ["selector"] = new JsonObject() };
            if (o[name] is { } wert) feld["description"] = new JsonObject { ["suggested_value"] = wert.DeepClone() };
            schema.Add(feld);
        }
        var verf = new JsonObject { ["name"] = "availability", ["required"] = false, ["selector"] = new JsonObject() };
        if (o["additional_options"]?["availability"] is { } a) verf["description"] = new JsonObject { ["suggested_value"] = a.DeepClone() };
        schema.Add(new JsonObject
        {
            ["name"] = "additional_options", ["type"] = "expandable", ["required"] = false, ["expanded"] = false,
            ["schema"] = new JsonArray(verf),
        });
        return RecordingHttpHandler.Json(new JsonObject
        {
            ["type"] = "form", ["flow_id"] = id, ["handler"] = "x", ["step_id"] = art,
            ["data_schema"] = schema, ["errors"] = null, ["last_step"] = true, ["preview"] = "template",
        }.ToJsonString());
    }
}
