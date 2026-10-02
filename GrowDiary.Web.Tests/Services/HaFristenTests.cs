using System.Net;
using System.Text.Json.Nodes;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Tests.TestFakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Fork AI (02.10.2026): Schreibende Aufrufe an Home Assistant bekommen eine
/// längere Frist, und eine Zeitüberschreitung ist „unbestätigt", nicht
/// „abgelehnt".
/// </summary>
/// <remarks>
/// <para><b>Der Anlass.</b> Alle Aufrufe hatten 4 s. Home Assistants
/// Dienst-Endpunkt wartet selbst bis zu 10 s auf die Integration, die
/// AC-Infinity-Wolke oft länger. Der Aufruf brach ab, das Gerät schaltete
/// trotzdem — und die Seite meldete einen harten Fehler („502, aber es
/// schaltet"), beim Preset blieb der Modus ungeschrieben.</para>
/// </remarks>
public sealed class HaFristenTests
{
    private static readonly HomeAssistantSettings Einstellungen = new()
    {
        BaseUrl = "http://ha.local:8123", AccessToken = "token", Enabled = true,
    };

    /// <summary>Ein Home Assistant, das sich Zeit lässt — oder gar nicht antwortet.</summary>
    private sealed class LangsamesHa : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _antwort;
        public List<(HttpMethod Methode, string Pfad)> Anfragen { get; } = [];

        public LangsamesHa(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> antwort) => _antwort = antwort;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage anfrage, CancellationToken ct)
        {
            lock (Anfragen) Anfragen.Add((anfrage.Method, anfrage.RequestUri!.AbsolutePath));
            return _antwort(anfrage, ct);
        }
    }

    private static async Task<HttpResponseMessage> Nie(CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct);
        throw new InvalidOperationException("unerreichbar");
    }

    private static HomeAssistantService Ha(HttpMessageHandler handler, TimeSpan lesen, TimeSpan dienst)
        => new(new StubHttpClientFactory(handler), NullLogger<HomeAssistantService>.Instance)
        {
            Lesefrist = lesen,
            Dienstfrist = dienst,
        };

    [Fact]
    public void DieDienstfristLiegtUeberDerVonHomeAssistant()
    {
        // HA wartet selbst bis zu 10 s — darunter käme seine eigene Antwort nie an.
        var ha = new HomeAssistantService(new StubHttpClientFactory(new LangsamesHa((_, ct) => Nie(ct))),
            NullLogger<HomeAssistantService>.Instance);
        Assert.True(ha.Dienstfrist > TimeSpan.FromSeconds(10), $"Dienstfrist {ha.Dienstfrist} — HA wartet selbst bis 10 s.");
        Assert.True(ha.Lesefrist < ha.Dienstfrist, "Lesende Aufrufe sollen schnell bleiben.");
    }

    [Fact]
    public async Task EinLangsamerDienstaufruf_WirdNichtNachDerLesefristAbgebrochen()
    {
        var handler = new LangsamesHa(async (anfrage, ct) =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(400), ct);
            return RecordingHttpHandler.Json("[]");
        });
        var ha = Ha(handler, lesen: TimeSpan.FromMilliseconds(100), dienst: TimeSpan.FromSeconds(5));

        var ok = await ha.CallEntityServiceAsync(Einstellungen, "select", "select_option", "select.licht_modus",
            daten: new Dictionary<string, object> { ["option"] = "On" });

        Assert.True(ok, "Ein Dienstaufruf, der 0,4 s braucht, scheiterte an der Lesefrist von 0,1 s.");
    }

    [Fact]
    public async Task Selbsttest_LesenBleibtBeiDerKurzenFrist()
    {
        // Ohne diesen Fall wäre „alles bekommt die lange Frist" auch grün.
        var handler = new LangsamesHa(async (anfrage, ct) =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(400), ct);
            return RecordingHttpHandler.Json(RecordingHttpHandler.EntityStateJson("sensor.x", "1"));
        });
        var ha = Ha(handler, lesen: TimeSpan.FromMilliseconds(100), dienst: TimeSpan.FromSeconds(5));

        Assert.Null(await ha.GetEntityStateAsync(Einstellungen, "sensor.x"));
    }

    [Fact]
    public async Task Zeitueberschreitung_IstUnbestaetigt_NichtAbgelehnt()
    {
        var ha = Ha(new LangsamesHa((_, ct) => Nie(ct)), lesen: TimeSpan.FromSeconds(1), dienst: TimeSpan.FromMilliseconds(200));

        var antwort = await ha.RufeEntitaetsDienstAsync(Einstellungen, "select", "select_option", "select.licht_modus");

        Assert.Equal(HaDienstAntwort.Unbestaetigt, antwort);
    }

    [Fact]
    public async Task Selbsttest_EineEchteAblehnungBleibtAbgelehnt()
    {
        var ha = Ha(new LangsamesHa((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest))),
            lesen: TimeSpan.FromSeconds(1), dienst: TimeSpan.FromSeconds(1));

        Assert.Equal(HaDienstAntwort.Abgelehnt,
            await ha.RufeEntitaetsDienstAsync(Einstellungen, "select", "select_option", "select.licht_modus"));
    }

    /// <summary>
    /// Der ganze Weg: Home Assistant antwortet auf den Stellbefehl nicht
    /// rechtzeitig, das Gerät hat aber geschaltet. Das ist kein harter Fehler.
    /// </summary>
    [Fact]
    public async Task AcSchreiber_ZeitueberschreitungMitGeschaltetemGeraet_IstBestaetigtUndKeinFehler()
    {
        const string modus = "select.licht_modus";
        var schritt = new AcSchreibschritt(modus, "select", "select_option",
            new Dictionary<string, object> { ["option"] = "On" }, "On");

        // Vorher steht das Gerät auf „Off", nach dem Senden auf „On" — die
        // Antwort auf das Senden kommt aber nie.
        var gesendet = false;
        var handler = new LangsamesHa(async (anfrage, ct) =>
        {
            if (anfrage.Method == HttpMethod.Post)
            {
                gesendet = true;
                return await Nie(ct);
            }
            return RecordingHttpHandler.Json(RecordingHttpHandler.EntityStateJson(modus, gesendet ? "On" : "Off"));
        });
        var ha = Ha(handler, lesen: TimeSpan.FromSeconds(2), dienst: TimeSpan.FromMilliseconds(200));
        var schreiber = new AcSchreiber(new HomeAssistantFunk(ha), NullLogger<AcSchreiber>.Instance);

        var ergebnisse = await schreiber.SchreibenAsync(Einstellungen, [schritt], (_, _) => Task.CompletedTask);

        var e = Assert.Single(ergebnisse);
        Assert.False(e.Uebersprungen, "Selbsttest: das Gerät stand vorher anders — es musste gesendet werden.");
        Assert.True(e.Angenommen, "Eine Zeitüberschreitung wurde als Ablehnung gewertet.");
        Assert.True(e.Bestaetigt, "Das Gerät meldet den Sollwert — der Schritt muss bestätigt sein.");
        Assert.False(AcStellAntwort.SendungAbgelehnt(ergebnisse));
    }

    [Fact]
    public async Task AcSchreiber_ZeitueberschreitungOhneWirkung_IstSchwebezustandUndKeinHarterFehler()
    {
        const string modus = "select.licht_modus";
        var handler = new LangsamesHa((anfrage, ct) => anfrage.Method == HttpMethod.Post
            ? Nie(ct)
            : Task.FromResult(RecordingHttpHandler.Json(RecordingHttpHandler.EntityStateJson(modus, "Off"))));
        var ha = Ha(handler, lesen: TimeSpan.FromSeconds(2), dienst: TimeSpan.FromMilliseconds(100));
        var schreiber = new AcSchreiber(new HomeAssistantFunk(ha), NullLogger<AcSchreiber>.Instance);
        var schritt = new AcSchreibschritt(modus, "select", "select_option",
            new Dictionary<string, object> { ["option"] = "On" }, "On");

        var ergebnisse = await schreiber.SchreibenAsync(Einstellungen, [schritt], (_, _) => Task.CompletedTask,
            takt: new AcTakt(TimeSpan.Zero, TimeSpan.FromSeconds(2), 1));

        var e = Assert.Single(ergebnisse);
        Assert.False(e.Bestaetigt);
        Assert.True(e.Angenommen);
        Assert.False(AcStellAntwort.SendungAbgelehnt(ergebnisse));
    }

    // ---------- Automations-Konfiguration ----------

    private static HttpClient Client(HttpMessageHandler handler, TimeSpan frist)
        => new(handler) { BaseAddress = new Uri("http://ha.local:8123/"), Timeout = frist };

    [Fact]
    public async Task Automation_ZeitueberschreitungBeimSchreiben_LiestNachUndErkenntDieGeschriebeneFassung()
    {
        var config = new JsonObject { ["alias"] = "CO2 Wächter", ["description"] = "Herkunft: fork-ai/co2/waechter/3" };
        var handler = new LangsamesHa((anfrage, ct) => anfrage.Method == HttpMethod.Post
            ? Nie(ct)
            : Task.FromResult(RecordingHttpHandler.Json(
                new JsonObject { ["id"] = "fork_ai_co2_waechter", ["alias"] = "CO2 Wächter", ["description"] = "Herkunft: fork-ai/co2/waechter/3" }
                    .ToJsonString())));

        var fehler = await SteuerungAutomationService.SchreibenAsync(
            Client(handler, TimeSpan.FromMilliseconds(200)), "fork_ai_co2_waechter", config, CancellationToken.None);

        Assert.Null(fehler);
        Assert.Contains(handler.Anfragen, a => a.Methode == HttpMethod.Get);
    }

    [Fact]
    public async Task Automation_ZeitueberschreitungUndAlteFassung_MeldetEsInKlartext()
    {
        var config = new JsonObject { ["alias"] = "CO2 Wächter", ["description"] = "Herkunft: fork-ai/co2/waechter/3" };
        var handler = new LangsamesHa((anfrage, ct) => anfrage.Method == HttpMethod.Post
            ? Nie(ct)
            : Task.FromResult(RecordingHttpHandler.Json(
                new JsonObject { ["id"] = "fork_ai_co2_waechter", ["alias"] = "CO2 Wächter", ["description"] = "Herkunft: fork-ai/co2/waechter/2" }
                    .ToJsonString())));

        var fehler = await SteuerungAutomationService.SchreibenAsync(
            Client(handler, TimeSpan.FromMilliseconds(200)), "fork_ai_co2_waechter", config, CancellationToken.None);

        Assert.NotNull(fehler);
        Assert.Contains("nicht rechtzeitig", fehler);
    }
}
