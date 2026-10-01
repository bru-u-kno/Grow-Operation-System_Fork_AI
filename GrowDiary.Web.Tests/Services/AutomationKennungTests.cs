using System.Net;
using System.Text.Json.Nodes;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Tests.TestFakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (01.10.2026): Unter welcher Kennung eine Automation in Home Assistant
/// steht — und dass der Fork sie dort auch findet.
/// </summary>
/// <remarks>
/// <para>Home Assistant leitet die Entity-ID aus dem Alias ab: aus der Vorlage
/// „CO2 Dosierung" wird <c>automation.co2_dosierung</c>. Der Katalog kennt nur die
/// handgebaute Kennung aus Brus Anlage (<c>automation.co2_dosierung_rdwc_port_5</c>).
/// Vorher stand deshalb jede angelegte Automation für immer unter „fehlt".</para>
/// <para>Die handgebauten müssen weiter gelten: sie laufen in der echten Anlage.</para>
/// </remarks>
public sealed class AutomationKennungTests
{
    private static readonly HomeAssistantSettings Einstellungen = new()
    {
        Enabled = true, BaseUrl = "http://ha.local:8123", AccessToken = "test",
    };

    private static readonly Dictionary<string, string> Rollen = new(StringComparer.Ordinal)
    {
        ["co2_sensor"] = "sensor.co2",
        ["canopy"] = "sensor.blatt",
        ["rh"] = "sensor.feuchte",
        ["licht"] = "binary_sensor.licht",
        ["port_zustand"] = "binary_sensor.port",
        ["port_schalter"] = "select.port",
    };

    /// <summary>Ein Home Assistant, das die angegebenen Automationen samt Konfiguration kennt.</summary>
    private sealed class NachgebautesHa
    {
        public List<(string EntityId, string? KonfigKennung, string Zustand)> Automationen { get; } = [];
        public Dictionary<string, JsonObject> Configs { get; } = new(StringComparer.Ordinal);
        public RecordingHttpHandler Handler { get; }

        public NachgebautesHa()
        {
            Handler = new RecordingHttpHandler((anfrage, inhalt) =>
            {
                var pfad = anfrage.RequestUri!.AbsolutePath;
                if (pfad == "/api/states")
                {
                    var liste = new JsonArray(new JsonObject
                    {
                        ["entity_id"] = "sensor.co2", ["state"] = "800", ["attributes"] = new JsonObject(),
                    });
                    foreach (var (entity, kennung, zustand) in Automationen)
                    {
                        var attribute = new JsonObject { ["friendly_name"] = entity };
                        if (kennung is not null) attribute["id"] = kennung;
                        liste.Add(new JsonObject { ["entity_id"] = entity, ["state"] = zustand, ["attributes"] = attribute });
                    }
                    return RecordingHttpHandler.Json(liste.ToJsonString());
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

        public HomeAssistantService Ha() => new(new StubHttpClientFactory(Handler), NullLogger<HomeAssistantService>.Instance);

        public Task<SteuerungBestandService.Bestandsaufnahme> BestandAsync()
            => new SteuerungBestandService(Ha(), NullLogger<SteuerungBestandService>.Instance)
                .AufnehmenAsync("co2", Rollen.Keys.ToList(), Einstellungen);

        public Task<SteuerungAutomationService.Bilanz> AnlegenAsync()
            => new SteuerungAutomationService(Ha(), NullLogger<SteuerungAutomationService>.Instance)
                .AnlegenAsync("co2", Rollen, Einstellungen, nurVorschau: false);

        public List<string> Geschrieben => Handler.Requests
            .Where(r => r.Method == HttpMethod.Post)
            .Select(r => r.Uri.AbsolutePath)
            .ToList();
    }

    private static SteuerungBestandService.BauteilStand Zeile(SteuerungBestandService.Bestandsaufnahme bestand, string name)
        => bestand.Bauteile.Single(b => b.Name == name);

    // ------------------------------------------------------------ Bestand

    [Fact]
    public async Task Bestand_FindetDieVomForkAngelegtenAutomationen()
    {
        var ha = new NachgebautesHa();
        // So legt Home Assistant sie an: Entity-ID aus dem Alias, id aus der Konfiguration.
        ha.Automationen.Add(("automation.co2_dosierung", "fork_ai_co2_dosierung", "on"));
        ha.Automationen.Add(("automation.co2_wachter", "fork_ai_co2_waechter", "on"));

        var bestand = await ha.BestandAsync();

        Assert.True(bestand.HaErreichbar);
        Assert.Equal(SteuerungBestandService.Stand.Da, Zeile(bestand, "CO2 Dosierung").Stand);
        Assert.Equal("automation.co2_dosierung", Zeile(bestand, "CO2 Dosierung").EntityId);
        Assert.Equal(SteuerungBestandService.Stand.Da, Zeile(bestand, "CO2 Wächter").Stand);
    }

    [Fact]
    public async Task Bestand_DieHandgebautenGeltenWeiter()
    {
        var ha = new NachgebautesHa();
        // Die echten Kennungen aus Brus Anlage (Konfigurations-ids wie in Daten/co2-handgebaut).
        ha.Automationen.Add(("automation.co2_dosierung_rdwc_port_5", "1788411041559", "on"));
        ha.Automationen.Add(("automation.co2_wachter_rdwc_port_5", "1788930088786", "on"));

        var bestand = await ha.BestandAsync();

        Assert.Equal(SteuerungBestandService.Stand.Da, Zeile(bestand, "CO2 Dosierung").Stand);
        Assert.Equal("automation.co2_dosierung_rdwc_port_5", Zeile(bestand, "CO2 Dosierung").EntityId);
        Assert.Equal(SteuerungBestandService.Stand.Da, Zeile(bestand, "CO2 Wächter").Stand);
    }

    [Fact]
    public async Task Bestand_OhneAutomationFehltSieWeiterhin()
    {
        // Selbsttest: die Suche nach der Konfigurations-Kennung macht nicht alles grün.
        var ha = new NachgebautesHa();
        ha.Automationen.Add(("automation.etwas_anderes", "fork_ai_co2_etwas", "on"));

        var bestand = await ha.BestandAsync();

        Assert.Equal(SteuerungBestandService.Stand.Fehlt, Zeile(bestand, "CO2 Dosierung").Stand);
        Assert.Equal(SteuerungBestandService.Stand.Fehlt, Zeile(bestand, "CO2 Wächter").Stand);
    }

    [Fact]
    public async Task Bestand_EineAeltereFassungWirdAngeboten()
    {
        // Ohne das verschwände der Weg zur reparierten Vorlage: die Seite zeigt das
        // Anlegen der Automationen nur, solange etwas fehlt oder veraltet ist.
        var ha = new NachgebautesHa();
        ha.Automationen.Add(("automation.co2_dosierung", "fork_ai_co2_dosierung", "on"));
        ha.Automationen.Add(("automation.co2_wachter", "fork_ai_co2_waechter", "on"));
        ha.Configs["fork_ai_co2_dosierung"] = new JsonObject
        {
            ["alias"] = "CO2 Dosierung",
            ["description"] = "… Angelegt von Grow OS Fork AI - Herkunft: fork-ai/co2/dosierung/3. …",
        };
        ha.Configs["fork_ai_co2_waechter"] = new JsonObject
        {
            ["alias"] = "CO2 Wächter",
            ["description"] = $"Herkunft: fork-ai/co2/waechter/{SteuerungAutomationService.VorlagenFassung("co2", "waechter")}.",
        };

        var bestand = await ha.BestandAsync();

        Assert.Equal(SteuerungBestandService.Stand.Veraltet, Zeile(bestand, "CO2 Dosierung").Stand);
        Assert.Equal(SteuerungBestandService.Stand.Da, Zeile(bestand, "CO2 Wächter").Stand);
        Assert.Equal(1, bestand.Veraltet);
    }

    // ------------------------------------------------------------ Anlegen

    [Fact]
    public async Task Anlegen_LegtNebenEineHandgebauteDosierungKeineZweite()
    {
        var ha = new NachgebautesHa();
        ha.Automationen.Add(("automation.co2_dosierung_rdwc_port_5", "1788411041559", "on"));

        var bilanz = await ha.AnlegenAsync();

        var dosierung = bilanz.Einzeln.Single(e => e.Name == "dosierung");
        Assert.Equal(SteuerungAutomationService.Stand.Fremd, dosierung.Stand);
        Assert.Contains("automation.co2_dosierung_rdwc_port_5", dosierung.Hinweis);
        Assert.DoesNotContain(ha.Geschrieben, p => p.EndsWith("/fork_ai_co2_dosierung", StringComparison.Ordinal));

        // Selbsttest: der Wächter fehlt wirklich und wird angelegt — es wird also geschrieben.
        Assert.Equal(SteuerungAutomationService.Stand.Angelegt, bilanz.Einzeln.Single(e => e.Name == "waechter").Stand);
        Assert.Contains(ha.Geschrieben, p => p.EndsWith("/fork_ai_co2_waechter", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Anlegen_ErneuertDieEigeneAutomation()
    {
        var ha = new NachgebautesHa();
        ha.Automationen.Add(("automation.co2_dosierung", "fork_ai_co2_dosierung", "on"));
        ha.Configs["fork_ai_co2_dosierung"] = new JsonObject
        {
            ["alias"] = "CO2 Dosierung",
            ["description"] = "Herkunft: fork-ai/co2/dosierung/3.",
        };

        var bilanz = await ha.AnlegenAsync();

        Assert.Equal(SteuerungAutomationService.Stand.Erneuert, bilanz.Einzeln.Single(e => e.Name == "dosierung").Stand);
        Assert.Contains("fork-ai/co2/dosierung/4", ha.Configs["fork_ai_co2_dosierung"]["description"]!.ToString());
    }

    // ------------------------------------------------------- reine Auflösung

    [Fact]
    public void AutomationFinden_BeideWegeUndNurWasEsGibt()
    {
        var dosierung = SteuerungBauteile.FuerModul("co2").Single(b => b.VorlagenDatei == "dosierung");
        HomeAssistantEntity Auto(string id, string? kennung) => new() { EntityId = id, KonfigKennung = kennung, Domain = "automation" };

        Assert.Equal(["automation.co2_dosierung"],
            SteuerungBauteile.AutomationFinden(dosierung, [Auto("automation.co2_dosierung", "fork_ai_co2_dosierung")]));
        Assert.Equal([Co2SteuerungService.Entitaeten.Automatik],
            SteuerungBauteile.AutomationFinden(dosierung, [Auto(Co2SteuerungService.Entitaeten.Automatik, "1788411041559")]));
        Assert.Empty(SteuerungBauteile.AutomationFinden(dosierung, [Auto("automation.co2_dosierung", "1234")]));
    }
}
