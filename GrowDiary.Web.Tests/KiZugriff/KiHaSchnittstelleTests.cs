using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Api.Controllers;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using GrowDiary.Web.Tests.Api;
using GrowDiary.Web.Tests.TestFakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace GrowDiary.Web.Tests.KiZugriff;

/// <summary>
/// Fork AI (A-003 Etappe B, 03.10.2026): Home Assistant über den Fork — an der
/// echten App, mit einem nachgestellten Home Assistant dahinter.
/// </summary>
/// <remarks>
/// <para>Der ganze Weg läuft echt: Sperre vor dem Routing, Schlüsselprüfung,
/// Einstufung nach dem Routing, Stundenfenster, Controller, Dienst. Nur das Netz
/// zu Home Assistant endet in <see cref="HaAttrappe"/>, die jede Anfrage
/// mitschreibt — so lässt sich belegen, dass ein abgewiesener Aufruf Home
/// Assistant nie erreicht hat.</para>
/// <para>Jeder Fall nimmt eine eigene Absenderadresse (172.30.33.4x), damit
/// sich die Fälle nicht über Fehlversuch-Sperren berühren.</para>
/// </remarks>
[Collection(IntegrationsSammlung.Name)]
public sealed class KiHaSchnittstelleTests : IClassFixture<KiHaApp>
{
    private static readonly string[] AlleStufen = ["Dokumentieren", "GrowPlanen", "GeraeteSchalten", "Verwaltung"];

    private readonly KiHaApp _ki;

    public KiHaSchnittstelleTests(KiHaApp ki) => _ki = ki;

    private IntegrationsApp App => _ki.App;

    // ------------------------------------------------------------ ohne Schlüssel

    /// <summary>
    /// Ohne Schlüssel 401 — auch aus dem Add-on-Netz, wo Lesen sonst ohne
    /// Schlüssel geht. Sonst griffe jedes Nachbar-Add-on über Grow OS alle
    /// Zustände von Home Assistant ab, mit dem Token des Forks.
    /// </summary>
    [Fact]
    public async Task OhneSchluessel_401_AuchAusDemAddonNetz()
    {
        await _ki.SchalterAsync(true);
        var vorher = _ki.Ha.Anzahl(a => a.Pfad == "/api/states" || a.Pfad == "/api/template" || a.Pfad.StartsWith("/api/history", StringComparison.Ordinal));

        var nachbar = App.AddonClient("172.30.33.40");
        var oberflaeche = _ki.Oberflaeche();
        foreach (var weg in new[] { "/api/ki-ha/bereiche", "/api/ki-ha/zustaende", "/api/ki-ha/verlauf?entityId=sensor.zelt_temperatur" })
        {
            await KiHaApp.Erwarte(await nachbar.GetAsync(weg), HttpStatusCode.Unauthorized, "ki_schluessel_fehlt");
            await KiHaApp.Erwarte(await oberflaeche.GetAsync(weg), HttpStatusCode.Unauthorized, "ki_schluessel_fehlt");
        }

        var dienst = KiHaApp.Dienst("light", "turn_on", "light.ki_test");
        await KiHaApp.Erwarte(await oberflaeche.PostAsJsonAsync("/api/ki-ha/dienst", dienst), HttpStatusCode.Unauthorized, "ki_schluessel_fehlt");
        // Schreiben aus dem Add-on-Netz ohne Schlüssel endet schon vor dem Routing — wie überall.
        await KiHaApp.Erwarte(await nachbar.PostAsJsonAsync("/api/ki-ha/dienst", dienst), HttpStatusCode.Forbidden, "admin_access_required");

        // Und Home Assistant wurde für keine dieser Anfragen gefragt.
        var nachher = _ki.Ha.Anzahl(a => a.Pfad == "/api/states" || a.Pfad == "/api/template" || a.Pfad.StartsWith("/api/history", StringComparison.Ordinal));
        Assert.Equal(vorher, nachher);
        Assert.Equal(0, _ki.Ha.Anzahl(a => a.Koerper?.Contains("light.ki_test", StringComparison.Ordinal) == true));
    }

    // ------------------------------------------------------------------ lesen

    [Fact]
    public async Task Lesen_mit_einem_Dokumentieren_Schluessel_geht()
    {
        Assert.False(DemoData.IsEnabled, "Im Testbetrieb ginge kein Aufruf an Home Assistant — der Fall prüfte nichts.");
        await _ki.SchalterAsync(true);
        var (_, klartext) = await _ki.SchluesselAsync("Nur lesen", "Dokumentieren");
        var ki = App.AddonClient("172.30.33.41", klartext);

        // Bereiche
        var bereiche = await Lesen<List<KiHaBereichDto>>(ki, "/api/ki-ha/bereiche");
        Assert.Equal(["Garten", "Growzelt"], bereiche.Select(b => b.Name).ToList());
        Assert.Contains(bereiche, b => b.Id == "growzelt");

        // Zustände mit Suche ohne Rücksicht auf Groß-/Kleinschreibung, Domain und Bereich.
        var zelt = await Lesen<List<KiHaZustandDto>>(ki, "/api/ki-ha/zustaende?domain=sensor&suche=ZELT");
        var temperatur = Assert.Single(zelt);
        Assert.Equal("sensor.zelt_temperatur", temperatur.EntityId);
        Assert.Equal("Zelt Temperatur", temperatur.Name);
        Assert.Equal("24.6", temperatur.Zustand);
        Assert.Equal("°C", temperatur.Einheit);
        Assert.Equal("Growzelt", temperatur.Bereich);
        Assert.Equal(new DateTime(2026, 10, 3, 8, 15, 0, DateTimeKind.Utc), temperatur.GeaendertAmUtc);

        // Auch der Name wird durchsucht, nicht nur die Entity-ID.
        var perName = await Lesen<List<KiHaZustandDto>>(ki, "/api/ki-ha/zustaende?suche=hauptlicht");
        Assert.Equal("light.zelt", Assert.Single(perName).EntityId);

        var garten = await Lesen<List<KiHaZustandDto>>(ki, "/api/ki-ha/zustaende?bereich=GARTEN");
        Assert.Equal("sensor.aussen_temperatur", Assert.Single(garten).EntityId);

        // Anzahl: Vorgabe 100, höchstens 500. Die Attrappe hat mehr als 100 Entitäten.
        Assert.True(HaAttrappe.AnzahlEntitaeten > KiHomeAssistantApiController.VorgabeAnzahl,
            "Die Attrappe muss mehr Entitäten haben als die Vorgabe, sonst prüft der Fall die Grenze nicht.");
        Assert.Equal(KiHomeAssistantApiController.VorgabeAnzahl, (await Lesen<List<KiHaZustandDto>>(ki, "/api/ki-ha/zustaende")).Count);
        Assert.Equal(HaAttrappe.AnzahlEntitaeten, (await Lesen<List<KiHaZustandDto>>(ki, "/api/ki-ha/zustaende?anzahl=500")).Count);
        await KiHaApp.Erwarte(await ki.GetAsync("/api/ki-ha/zustaende?anzahl=501"), HttpStatusCode.BadRequest);
        await KiHaApp.Erwarte(await ki.GetAsync("/api/ki-ha/zustaende?anzahl=0"), HttpStatusCode.BadRequest);

        // Verlauf über die History-Schnittstelle von Home Assistant.
        var verlauf = await Lesen<KiHaVerlaufDto>(ki, "/api/ki-ha/verlauf?entityId=sensor.zelt_temperatur&stunden=6");
        Assert.Equal("sensor.zelt_temperatur", verlauf.EntityId);
        Assert.Equal(["24.1", "24.6"], verlauf.Punkte.Select(p => p.Zustand).ToList());
        var abruf = _ki.Ha.Letzte(a => a.Pfad.StartsWith("/api/history/period/", StringComparison.Ordinal));
        Assert.Contains("filter_entity_id=sensor.zelt_temperatur", abruf.Abfrage, StringComparison.Ordinal);
        Assert.Contains("minimal_response", abruf.Abfrage, StringComparison.Ordinal);

        await KiHaApp.Erwarte(await ki.GetAsync("/api/ki-ha/verlauf?entityId=sensor.zelt_temperatur&stunden=0"), HttpStatusCode.BadRequest);
        await KiHaApp.Erwarte(await ki.GetAsync("/api/ki-ha/verlauf?entityId=sensor.zelt_temperatur&stunden=169"), HttpStatusCode.BadRequest);
        await KiHaApp.Erwarte(await ki.GetAsync("/api/ki-ha/verlauf?entityId=sensor.a,lock.tuer"), HttpStatusCode.BadRequest);
    }

    // --------------------------------------------------------------- schalten

    [Fact]
    public async Task Licht_einschalten_geht_mit_GeraeteSchalten_und_ohne_nicht()
    {
        await _ki.SchalterAsync(true);
        var (_, schalten) = await _ki.SchluesselAsync("Schalten", "GeraeteSchalten");
        var (_, doku) = await _ki.SchluesselAsync("Doku", "Dokumentieren");

        var antwort = await App.AddonClient("172.30.33.42", schalten)
            .PostAsJsonAsync("/api/ki-ha/dienst", KiHaApp.Dienst("light", "turn_on", "light.ki_licht", new { brightness_pct = 80 }));
        await KiHaApp.Erwarte(antwort, HttpStatusCode.OK);
        var ergebnis = (await antwort.Content.ReadFromJsonAsync<KiHaDienstErgebnisDto>())!;
        Assert.True(ergebnis.Erfolg, ergebnis.Meldung);

        var aufruf = _ki.Ha.Letzte(a => a.Pfad == "/api/services/light/turn_on" && a.Koerper?.Contains("light.ki_licht", StringComparison.Ordinal) == true);
        using (var koerper = JsonDocument.Parse(aufruf.Koerper!))
        {
            Assert.Equal("light.ki_licht", koerper.RootElement.GetProperty("entity_id").GetString());
            Assert.Equal(80, koerper.RootElement.GetProperty("brightness_pct").GetInt32());
        }

        var ohne = await App.AddonClient("172.30.33.42", doku)
            .PostAsJsonAsync("/api/ki-ha/dienst", KiHaApp.Dienst("light", "turn_on", "light.ki_licht_doku"));
        await KiHaApp.Erwarte(ohne, HttpStatusCode.Forbidden, "ki_stufe_fehlt");
        Assert.Equal(0, _ki.Ha.Anzahl(a => a.Koerper?.Contains("light.ki_licht_doku", StringComparison.Ordinal) == true));
    }

    [Fact]
    public async Task Automation_braucht_zusaetzlich_Verwaltung()
    {
        await _ki.SchalterAsync(true);
        var (_, schalten) = await _ki.SchluesselAsync("Nur schalten", "GeraeteSchalten");
        var (_, beides) = await _ki.SchluesselAsync("Schalten und Verwaltung", "GeraeteSchalten", "Verwaltung");
        var (_, nurVerwaltung) = await _ki.SchluesselAsync("Nur Verwaltung", "Verwaltung");

        var abgewiesen = await App.AddonClient("172.30.33.43", schalten)
            .PostAsJsonAsync("/api/ki-ha/dienst", KiHaApp.Dienst("automation", "turn_off", "automation.ki_nur_schalten"));
        await KiHaApp.Erwarte(abgewiesen, HttpStatusCode.Forbidden, "ki_stufe_fehlt");
        var meldung = (await abgewiesen.Content.ReadFromJsonAsync<ApiError>())!.Message;
        Assert.Contains("Dafür fehlt die Freigabe für Stufe „Verwaltung“", meldung, StringComparison.Ordinal);
        Assert.Equal(0, _ki.Ha.Anzahl(a => a.Koerper?.Contains("automation.ki_nur_schalten", StringComparison.Ordinal) == true));

        // Verwaltung allein genügt nicht: die Aktion selbst verlangt Geräte schalten (Sperre).
        await KiHaApp.Erwarte(await App.AddonClient("172.30.33.43", nurVerwaltung)
                .PostAsJsonAsync("/api/ki-ha/dienst", KiHaApp.Dienst("automation", "turn_off", "automation.ki_nur_verwaltung")),
            HttpStatusCode.Forbidden, "ki_stufe_fehlt");

        await KiHaApp.Erwarte(await App.AddonClient("172.30.33.43", beides)
                .PostAsJsonAsync("/api/ki-ha/dienst", KiHaApp.Dienst("automation", "turn_off", "automation.ki_beides")),
            HttpStatusCode.OK);
        Assert.Equal(1, _ki.Ha.Anzahl(a => a.Pfad == "/api/services/automation/turn_off"
                                           && a.Koerper?.Contains("automation.ki_beides", StringComparison.Ordinal) == true));
    }

    [Theory]
    [InlineData("homeassistant", "restart", null)]
    [InlineData("homeassistant", "turn_on", "homeassistant.irgendwas")]
    [InlineData("hassio", "host_reboot", null)]
    [InlineData("hassio", "addon_stop", null)]
    [InlineData("light", "reload", null)]
    [InlineData("automation", "reload", null)]
    [InlineData("lock", "unlock", "lock.haustuer")]
    public async Task Nie_ist_auch_mit_allen_Stufen_403(string domain, string dienst, string? entityId)
    {
        await _ki.SchalterAsync(true);
        var (_, alles) = await _ki.SchluesselAsync("Alles", AlleStufen);

        var antwort = await App.AddonClient("172.30.33.44", alles)
            .PostAsJsonAsync("/api/ki-ha/dienst", KiHaApp.Dienst(domain, dienst, entityId));
        await KiHaApp.Erwarte(antwort, HttpStatusCode.Forbidden, "ki_kein_zugriff");
        Assert.Equal(0, _ki.Ha.Anzahl(a => a.Pfad == $"/api/services/{domain}/{dienst}"));
    }

    /// <summary>
    /// Prüferbefund 04.10.2026: mit nur „Geräte schalten" kam der Prüfer über diese
    /// Dienste bis Home Assistant. Jetzt sind sie nie erreichbar — auch mit allen
    /// Stufen —, und zwar nicht über einen eigenen Eintrag, sondern weil nur die
    /// Positivliste erlaubt ist (<see cref="KiHaEinstufungTests.Der_Prueferbefund_faellt_ohne_Sonderbehandlung_auf_nie"/>).
    /// </summary>
    [Theory]
    [MemberData(nameof(KiHaEinstufungTests.Prueferbefund), MemberType = typeof(KiHaEinstufungTests))]
    public async Task Prueferbefund_ist_auch_mit_allen_Stufen_403_und_Home_Assistant_hoert_nichts(
        string domain, string dienst, string? entityId, string? datenJson)
    {
        await _ki.SchalterAsync(true);
        var (_, alles) = await _ki.SchluesselAsync("Alles", AlleStufen);
        var daten = datenJson is null ? null : JsonSerializer.Deserialize<Dictionary<string, object>>(datenJson);

        var antwort = await App.AddonClient("172.30.33.47", alles)
            .PostAsJsonAsync("/api/ki-ha/dienst", KiHaApp.Dienst(domain, dienst, entityId, daten));
        await KiHaApp.Erwarte(antwort, HttpStatusCode.Forbidden, "ki_kein_zugriff");
        var meldung = (await antwort.Content.ReadFromJsonAsync<ApiError>())!.Message;
        Assert.Contains("nur bekannte Gerätebereiche erlaubt", meldung, StringComparison.Ordinal);
        Assert.Equal(0, _ki.Ha.Anzahl(a => a.Pfad == $"/api/services/{domain}/{dienst}"));
    }

    /// <summary>Über eine erlaubte Domain kein schädlicher Dienst: nur die Dienste der Liste.</summary>
    [Theory]
    [InlineData("media_player", "play_media", "media_player.zelt", "{\"media_content_id\":\"http://boese.example/x.mp3\",\"media_content_type\":\"music\"}")]
    [InlineData("vacuum", "send_command", "vacuum.robbi", "{\"command\":\"app_goto_target\"}")]
    [InlineData("climate", "set_climate_timer", "climate.zelt", null)]
    public async Task In_einer_erlaubten_Domain_geht_nur_die_Dienstliste(string domain, string dienst, string entityId, string? datenJson)
    {
        await _ki.SchalterAsync(true);
        var (_, alles) = await _ki.SchluesselAsync("Alles", AlleStufen);
        var daten = datenJson is null ? null : JsonSerializer.Deserialize<Dictionary<string, object>>(datenJson);

        var antwort = await App.AddonClient("172.30.33.48", alles)
            .PostAsJsonAsync("/api/ki-ha/dienst", KiHaApp.Dienst(domain, dienst, entityId, daten));
        await KiHaApp.Erwarte(antwort, HttpStatusCode.Forbidden, "ki_kein_zugriff");
        Assert.Equal(0, _ki.Ha.Anzahl(a => a.Pfad == $"/api/services/{domain}/{dienst}"));

        // Gegenprobe in derselben Domain: ein Dienst der Liste geht durch.
        var erlaubt = KiHaEinstufung.Domains[domain].Dienste![0];
        await KiHaApp.Erwarte(await App.AddonClient("172.30.33.48", alles)
                .PostAsJsonAsync("/api/ki-ha/dienst", KiHaApp.Dienst(domain, erlaubt, entityId)),
            HttpStatusCode.OK);
        Assert.True(_ki.Ha.Anzahl(a => a.Pfad == $"/api/services/{domain}/{erlaubt}") >= 1);
    }

    /// <summary>
    /// Prüferbefund 04.10.2026: das Protokoll sagt, WAS in Home Assistant geschaltet
    /// wurde — Dienst und Entität —, aber nie die Daten. Und eine Abweisung des
    /// Controllers trägt ihren Code.
    /// </summary>
    [Fact]
    public async Task Das_Protokoll_nennt_Dienst_und_Entitaet_aber_keine_Daten()
    {
        await _ki.SchalterAsync(true);
        var (id, schalten) = await _ki.SchluesselAsync("Protokoll HA", "GeraeteSchalten");
        var ki = App.AddonClient("172.30.33.49", schalten);
        const string geheim = "geheim-4711";

        await KiHaApp.Erwarte(await ki.PostAsJsonAsync("/api/ki-ha/dienst",
                KiHaApp.Dienst("light", "turn_on", "light.zelt", new Dictionary<string, object> { ["brightness_pct"] = 4711, ["effect"] = geheim })),
            HttpStatusCode.OK);
        await KiHaApp.Erwarte(await ki.PostAsJsonAsync("/api/ki-ha/dienst",
                KiHaApp.Dienst("zha", "issue_zigbee_cluster_command", null, new Dictionary<string, object> { ["ieee"] = geheim })),
            HttpStatusCode.Forbidden, "ki_kein_zugriff");
        await KiHaApp.Erwarte(await ki.PostAsJsonAsync("/api/ki-ha/dienst",
                KiHaApp.Dienst("automation", "turn_off", "automation.licht_an")),
            HttpStatusCode.Forbidden, "ki_stufe_fehlt");
        await KiHaApp.Erwarte(await ki.PostAsJsonAsync("/api/ki-ha/dienst", KiHaApp.Dienst("light", "turn_on", "switch.x")),
            HttpStatusCode.BadRequest, "validation_failed");

        var antwort = await _ki.Oberflaeche().GetAsync($"/api/settings/ki-zugriff/protokoll?schluesselId={id}");
        await KiHaApp.Erwarte(antwort, HttpStatusCode.OK);
        var roh = await antwort.Content.ReadAsStringAsync();
        var eintraege = (await antwort.Content.ReadFromJsonAsync<List<KiProtokollEintragDto>>())!;

        // Mengenwächter: alle vier Aufrufe stehen da, neueste zuerst.
        Assert.Equal(4, eintraege.Count);
        Assert.Equal(["light.turn_on → switch.x", "automation.turn_off → automation.licht_an", "zha.issue_zigbee_cluster_command", "light.turn_on → light.zelt"],
            eintraege.Select(e => e.HaDienst).ToList());
        Assert.Equal(["validation_failed", "ki_stufe_fehlt", "ki_kein_zugriff", null], eintraege.Select(e => e.Fehlercode).ToList());
        Assert.Equal([400, 403, 403, 200], eintraege.Select(e => e.Status).ToList());
        Assert.Contains("(Home Assistant: light.turn_on → light.zelt)", eintraege[3].Beschreibung, StringComparison.Ordinal);

        // Nie die Daten — weder im Feld noch im Satz noch sonst irgendwo in der Antwort.
        Assert.DoesNotContain(geheim, roh, StringComparison.Ordinal);
        Assert.DoesNotContain("brightness", roh, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Entitaet_muss_zur_Domain_passen_und_Ziele_nur_ueber_entityId()
    {
        await _ki.SchalterAsync(true);
        var (_, schalten) = await _ki.SchluesselAsync("Schalten", "GeraeteSchalten");
        var ki = App.AddonClient("172.30.33.45", schalten);

        await KiHaApp.Erwarte(await ki.PostAsJsonAsync("/api/ki-ha/dienst", KiHaApp.Dienst("light", "turn_on", "switch.x")),
            HttpStatusCode.BadRequest, "validation_failed");
        // Mehrere Entitäten in einer Zeichenkette — Home Assistant nähme sie an.
        await KiHaApp.Erwarte(await ki.PostAsJsonAsync("/api/ki-ha/dienst", KiHaApp.Dienst("light", "turn_on", "light.a,lock.haustuer")),
            HttpStatusCode.BadRequest);
        // Ein Ziel in den Daten überginge die Prüfung oben.
        await KiHaApp.Erwarte(await ki.PostAsJsonAsync("/api/ki-ha/dienst",
                KiHaApp.Dienst("light", "turn_on", "light.zelt", new Dictionary<string, object> { ["entity_id"] = "switch.x" })),
            HttpStatusCode.BadRequest);
        await KiHaApp.Erwarte(await ki.PostAsJsonAsync("/api/ki-ha/dienst",
                KiHaApp.Dienst("light", "turn_off", null, new Dictionary<string, object> { ["area_id"] = "growzelt" })),
            HttpStatusCode.BadRequest);
        // scene.apply setzt über „entities" beliebige Entitäten — auch mit Verwaltung nicht.
        var (_, beides) = await _ki.SchluesselAsync("Schalten und Verwaltung", "GeraeteSchalten", "Verwaltung");
        await KiHaApp.Erwarte(await App.AddonClient("172.30.33.45", beides).PostAsJsonAsync("/api/ki-ha/dienst",
                KiHaApp.Dienst("scene", "apply", null, new Dictionary<string, object> { ["entities"] = new Dictionary<string, string> { ["lock.haustuer"] = "unlocked" } })),
            HttpStatusCode.BadRequest, "validation_failed");
        // Pfad-Teile in der Domain landeten sonst in api/services/{domain}/{dienst}.
        await KiHaApp.Erwarte(await ki.PostAsJsonAsync("/api/ki-ha/dienst", KiHaApp.Dienst("../states", "x", null)),
            HttpStatusCode.BadRequest);

        Assert.Equal(0, _ki.Ha.Anzahl(a => a.Koerper?.Contains("switch.x", StringComparison.Ordinal) == true
                                           || a.Koerper?.Contains("lock.haustuer", StringComparison.Ordinal) == true
                                           || a.Koerper?.Contains("growzelt", StringComparison.Ordinal) == true));
    }

    // ------------------------------------------------------------------ Hilfe

    private static async Task<T> Lesen<T>(HttpClient client, string weg)
    {
        var antwort = await client.GetAsync(weg);
        await KiHaApp.Erwarte(antwort, HttpStatusCode.OK);
        return (await antwort.Content.ReadFromJsonAsync<T>())!;
    }
}

/// <summary>
/// Die Stundengrenze der Sperre zählt den Dienstaufruf mit — in einer eigenen
/// App, weil das Stundenfenster über alle Schlüssel der App gilt und die Fälle
/// der anderen Klasse sonst mitzählten.
/// </summary>
[Collection(IntegrationsSammlung.Name)]
public sealed class KiHaStundengrenzeTests : IClassFixture<KiHaApp>
{
    private readonly KiHaApp _ki;

    public KiHaStundengrenzeTests(KiHaApp ki) => _ki = ki;

    [Fact]
    public async Task Der_Dienstaufruf_zaehlt_ins_Stundenfenster()
    {
        await _ki.SchalterAsync(true, maxSchaltbefehle: 2);
        try
        {
            var (_, schalten) = await _ki.SchluesselAsync("Grenze", "GeraeteSchalten");
            var ki = _ki.App.AddonClient("172.30.33.46", schalten);

            for (var i = 1; i <= 2; i++)
            {
                await KiHaApp.Erwarte(await ki.PostAsJsonAsync("/api/ki-ha/dienst", KiHaApp.Dienst("switch", "turn_on", $"switch.grenze_{i}")),
                    HttpStatusCode.OK);
            }

            await KiHaApp.Erwarte(await ki.PostAsJsonAsync("/api/ki-ha/dienst", KiHaApp.Dienst("switch", "turn_on", "switch.grenze_3")),
                (HttpStatusCode)429, "ki_hoechstwert");
            Assert.Equal(0, _ki.Ha.Anzahl(a => a.Koerper?.Contains("switch.grenze_3", StringComparison.Ordinal) == true));
        }
        finally
        {
            await _ki.SchalterAsync(true);
        }
    }
}

/// <summary>Die Tabelle <see cref="KiHaEinstufung"/>: vollständig, ohne Doppelte, und die Prüfung beisst.</summary>
public sealed class KiHaEinstufungTests
{
    /// <summary>
    /// Prüferbefund 04.10.2026 — wörtlich die 13 Dienste, mit denen der Prüfer über
    /// „Geräte schalten" bis Home Assistant kam (Domain, Dienst, Entität, Daten als JSON).
    /// </summary>
    public static TheoryData<string, string, string?, string?> Prueferbefund => new()
    {
        { "zha", "issue_zigbee_cluster_command", null, """{"ieee":"00:11:22:33:44:55:66:77","endpoint_id":1,"cluster_id":257,"command":1,"command_type":"server"}""" },
        { "zwave_js", "multicast_set_value", null, """{"broadcast":true,"command_class":98,"property":"targetMode","value":0}""" },
        { "telegram_bot", "send_message", null, """{"message":"Hallo"}""" },
        { "assist_satellite", "announce", "assist_satellite.kueche", """{"message":"Hallo"}""" },
        { "cloud", "remote_connect", null, null },
        { "auto_backup", "purge", null, null },
        { "import_statistics", "import_from_json", null, """{"filename":"/config/x.json"}""" },
        { "fritz", "set_guest_wifi_password", null, """{"password":"x"}""" },
        { "device_tracker", "see", null, """{"dev_id":"handy","location_name":"home"}""" },
        { "group", "set", null, """{"object_id":"zelt","entities":"lock.haustuer"}""" },
        { "logbook", "log", null, """{"name":"x","message":"y"}""" },
        { "camera", "snapshot", "camera.zelt", """{"filename":"/config/www/zelt.jpg"}""" },
        { "alert", "turn_off", "alert.wasser", null },
    };

    /// <summary>
    /// Alle 13 ergeben „nie" — und zwar ohne Einzelfall: keine der Domains steht in
    /// der Tabelle. Sie fallen auf die Vorgabe für Unbekanntes.
    /// </summary>
    [Fact]
    public void Der_Prueferbefund_faellt_ohne_Sonderbehandlung_auf_nie()
    {
        var faelle = Prueferbefund.Select(f => ((string)f[0], (string)f[1])).ToList();
        Assert.Equal(13, faelle.Count);

        foreach (var (domain, dienst) in faelle)
        {
            Assert.False(KiHaEinstufung.Domains.ContainsKey(domain),
                $"{domain} steht in der Tabelle — der Fall muss über die Positivliste fallen, nicht über einen eigenen Eintrag.");
            Assert.False(KiHaEinstufung.Dienste.ContainsKey(dienst), $"{dienst} hat einen eigenen Dienst-Eintrag.");
            var regel = KiHaEinstufung.Einstufen(domain, dienst);
            Assert.Equal(KiHaUrteil.Nie, regel.Urteil);
            Assert.Equal(KiHaEinstufung.UnbekanntGrund, regel.Grund);
        }
    }

    /// <summary>Die Positivliste aus dem Auftrag (Prüferbefund 04.10.2026).</summary>
    private static readonly string[] GeraeteLautAuftrag =
    [
        "light", "switch", "fan", "climate", "humidifier", "cover", "valve", "number", "select", "button",
        "water_heater", "vacuum", "media_player",
    ];

    [Fact]
    public void Positivliste_Verwaltung_und_Nie_ueberschneiden_sich_nicht_und_jede_Geraetedomain_hat_Dienste()
    {
        var geraete = KiHaEinstufung.Eintraege.Where(r => r.Urteil == KiHaUrteil.GeraeteSchalten).ToList();
        var verwaltung = KiHaEinstufung.Eintraege.Where(r => r.Urteil == KiHaUrteil.Verwaltung).Select(r => r.Name).ToList();
        var nie = KiHaEinstufung.Eintraege.Where(r => r.Urteil == KiHaUrteil.Nie).Select(r => r.Name).ToList();

        // Mengenwächter: ohne Einträge wäre „keine Überschneidung" geschenkt.
        Assert.True(geraete.Count >= GeraeteLautAuftrag.Length, $"Nur {geraete.Count} Gerätedomains.");
        Assert.True(verwaltung.Count >= VerwaltungLautAuftrag.Length, $"Nur {verwaltung.Count} Verwaltungsdomains.");
        Assert.True(nie.Count >= NieLautAuftrag.Length, $"Nur {nie.Count} Nie-Domains.");

        var geraeteNamen = geraete.Select(r => r.Name).ToList();
        Assert.Empty(geraeteNamen.Intersect(verwaltung, StringComparer.Ordinal));
        Assert.Empty(geraeteNamen.Intersect(nie, StringComparer.Ordinal));
        Assert.Empty(verwaltung.Intersect(nie, StringComparer.Ordinal));

        Assert.Equal(GeraeteLautAuftrag.Order(StringComparer.Ordinal), geraeteNamen.Order(StringComparer.Ordinal));

        // Jede Gerätedomain hat ihre Dienstliste — sonst stünde sie mit leerer Liste da
        // (nichts erlaubt) oder ein Umbau machte daraus wieder „alles erlaubt".
        Assert.All(geraete, r => Assert.True(r.Dienste is { Count: > 0 }, $"{r.Name} hat keine Dienstliste."));
        Assert.All(geraete, r => Assert.Equal(r.Dienste!.Count, r.Dienste.Distinct(StringComparer.Ordinal).Count()));
        // Nur Gerätedomains tragen eine Dienstliste; bei den anderen wäre sie wirkungslos.
        Assert.All(KiHaEinstufung.Eintraege.Where(r => r.Urteil != KiHaUrteil.GeraeteSchalten), r => Assert.Null(r.Dienste));

        // media_player: genau so im Auftrag.
        Assert.Equal(["media_pause", "media_play", "media_stop", "toggle", "turn_off", "turn_on", "volume_mute", "volume_set"],
            KiHaEinstufung.Domains["media_player"].Dienste!.Order(StringComparer.Ordinal));
        // Dienste, die beliebige Befehle, Adressen oder Dateien annehmen, stehen nirgends darin.
        foreach (var gefaehrlich in new[] { "play_media", "send_command", "snapshot", "join", "select_source" })
        {
            Assert.DoesNotContain(geraete, r => r.Dienste!.Contains(gefaehrlich));
        }
    }

    [Fact]
    public void Jeder_Dienst_der_Liste_geht_mit_GeraeteSchalten_jeder_andere_nie()
    {
        var paare = KiHaEinstufung.Eintraege
            .Where(r => r.Urteil == KiHaUrteil.GeraeteSchalten)
            .SelectMany(r => r.Dienste!.Select(d => (r.Name, d)))
            .ToList();
        Assert.True(paare.Count >= 60, $"Nur {paare.Count} erlaubte Dienste — die Zählung sieht ihre Grundmenge nicht.");
        Assert.All(paare, p => Assert.Equal(KiHaUrteil.GeraeteSchalten, KiHaEinstufung.Einstufen(p.Name, p.d).Urteil));

        Assert.Equal(KiHaUrteil.Nie, KiHaEinstufung.Einstufen("media_player", "play_media").Urteil);
        Assert.Equal(KiHaUrteil.Nie, KiHaEinstufung.Einstufen("vacuum", "send_command").Urteil);
        Assert.Equal(KiHaUrteil.Nie, KiHaEinstufung.Einstufen("light", "reload").Urteil);
    }

    /// <summary>So im Auftrag (A-003 Etappe B) — wörtlich, damit eine Lücke auffällt.</summary>
    private static readonly string[] VerwaltungLautAuftrag =
    [
        "automation", "script", "scene", "input_boolean", "input_number", "input_select", "input_text",
        "input_datetime", "input_button", "timer", "counter", "schedule",
    ];

    private static readonly string[] NieLautAuftrag =
    [
        "homeassistant", "hassio", "backup", "recorder", "system_log", "logger", "shell_command", "python_script",
        "pyscript", "rest_command", "notify", "persistent_notification", "tts", "conversation", "lock", "alarm_control_panel",
    ];

    [Fact]
    public void Keine_Domain_steht_doppelt()
    {
        Assert.True(KiHaEinstufung.Eintraege.Count >= 40, "Mengenwächter: die Tabelle ist leer oder fast leer.");
        Assert.Empty(KiHaEinstufung.Doppelte(KiHaEinstufung.Eintraege));
        Assert.Equal(KiHaEinstufung.Eintraege.Count, KiHaEinstufung.Domains.Count);
    }

    [Fact]
    public void Jede_Domain_aus_dem_Auftrag_steht_mit_ihrem_Urteil_und_Grund_darin()
    {
        foreach (var domain in VerwaltungLautAuftrag)
        {
            Assert.True(KiHaEinstufung.Domains.TryGetValue(domain, out var regel) && regel.Urteil == KiHaUrteil.Verwaltung,
                $"{domain} muss Verwaltung verlangen.");
        }
        foreach (var domain in NieLautAuftrag)
        {
            Assert.True(KiHaEinstufung.Domains.TryGetValue(domain, out var regel) && regel.Urteil == KiHaUrteil.Nie,
                $"{domain} muss nie gehen.");
        }
        Assert.Equal(KiHaUrteil.Nie, KiHaEinstufung.Einstufen("automation", "reload").Urteil);
        Assert.Equal(KiHaUrteil.Nie, KiHaEinstufung.Einstufen("light", "reload").Urteil);
        Assert.Equal(KiHaUrteil.GeraeteSchalten, KiHaEinstufung.Einstufen("light", "turn_on").Urteil);
        Assert.Equal(KiHaUrteil.GeraeteSchalten, KiHaEinstufung.Einstufen("climate", "set_temperature").Urteil);
        // Unbekannt ist nie — es gibt keine Vorgabe „Geräte schalten" mehr.
        Assert.Equal(KiHaUrteil.Nie, KiHaEinstufung.Einstufen("irgendeine_integration", "turn_on").Urteil);

        Assert.All(KiHaEinstufung.Eintraege.Concat(KiHaEinstufung.Dienste.Values),
            r => Assert.False(string.IsNullOrWhiteSpace(r.Grund), $"{r.Name} hat keinen Grund."));
    }

    /// <summary>Bissnachweis: eine Domain, die in beiden Teilen der Tabelle steht, wird gefunden.</summary>
    [Fact]
    public void Die_Pruefung_auf_Doppelte_beisst()
    {
        var mitDoppeltem = KiHaEinstufung.Eintraege
            .Append(new KiHaRegel("lock", KiHaUrteil.Verwaltung, "Absichtlich doppelt, für den Bissnachweis."))
            .ToList();

        Assert.Equal(["lock"], KiHaEinstufung.Doppelte(mitDoppeltem));
        Assert.Throws<ArgumentException>(() => mitDoppeltem.ToDictionary(r => r.Name, StringComparer.Ordinal));
    }

    /// <summary>Die Aktion trägt Geräte schalten — und die Sperre zählt sie deshalb ins Stundenfenster.</summary>
    [Fact]
    public void Die_Dienst_Aktion_traegt_GeraeteSchalten()
    {
        var methode = typeof(GrowDiary.Web.Api.Controllers.KiHomeAssistantApiController).GetMethod("Dienst")!;
        var stufe = Assert.Single(methode.GetCustomAttributes(typeof(KiStufeAttribute), inherit: true).Cast<KiStufeAttribute>());
        Assert.Equal(KiStufe.GeraeteSchalten, stufe.Stufe);
        Assert.Null(methode.GetCustomAttributes(typeof(KiOhneHoechstwertAttribute), inherit: true).SingleOrDefault());
    }
}

/// <summary>
/// Die echte App mit nachgestelltem Home Assistant — für die Wege unter <c>/api/ki-ha</c>.
/// </summary>
/// <remarks>
/// <see cref="HomeAssistantService"/> wird mit einer <see cref="StubHttpClientFactory"/>
/// auf <see cref="HaAttrappe"/> neu gebaut; alles andere bleibt die App aus
/// <c>Program.cs</c>. Die Verbindung zu Home Assistant wird wie in der
/// Oberfläche gespeichert, damit <see cref="HomeAssistantSettings.IsConfigured"/> stimmt.
/// </remarks>
public sealed class KiHaApp : IDisposable
{
    public IntegrationsApp App { get; }
    public HaAttrappe Ha { get; } = new();

    public KiHaApp()
    {
        App = new IntegrationsApp
        {
            Umgebung = "Production",
            Zusatzdienste = dienste =>
            {
                dienste.RemoveAll<HomeAssistantService>();
                dienste.AddSingleton(sp => new HomeAssistantService(
                    new StubHttpClientFactory(Ha),
                    sp.GetRequiredService<ILogger<HomeAssistantService>>(),
                    sp.GetService<HydroSetupRepository>()));
            },
        };

        App.Services.GetRequiredService<HomeAssistantSettingsRepository>().SaveHomeAssistantSettings(new HomeAssistantSettings
        {
            BaseUrl = HaAttrappe.Adresse,
            AccessToken = "nur-fuer-den-test",
            Enabled = true,
        });
    }

    public HttpClient Oberflaeche() => App.IngressClient();

    public async Task SchalterAsync(bool aktiv, int maxSchaltbefehle = 1000)
    {
        var antwort = await Oberflaeche().PutAsJsonAsync("/api/settings/ki-zugriff", new
        {
            aktiv,
            hoechstwerte = new { maxDosisMlJeBefehl = 10.0, maxSchaltbefehleJeStunde = maxSchaltbefehle },
        });
        antwort.EnsureSuccessStatusCode();
    }

    public async Task<(int Id, string Klartext)> SchluesselAsync(string name, params string[] stufen)
    {
        var antwort = await Oberflaeche().PostAsJsonAsync("/api/settings/ki-zugriff/schluessel", new { name, stufen });
        Assert.Equal(HttpStatusCode.Created, antwort.StatusCode);
        var angelegt = await antwort.Content.ReadFromJsonAsync<KiSchluesselAngelegtDto>();
        return (angelegt!.Schluessel.Id, angelegt.Klartext);
    }

    public static object Dienst(string domain, string dienst, string? entityId, object? daten = null)
        => new { domain, dienst, entityId, daten };

    public static async Task Erwarte(HttpResponseMessage antwort, HttpStatusCode status, string? code = null)
    {
        var text = await antwort.Content.ReadAsStringAsync();
        Assert.True(antwort.StatusCode == status,
            $"{antwort.RequestMessage?.Method} {antwort.RequestMessage?.RequestUri?.PathAndQuery}: erwartet {(int)status}, kam {(int)antwort.StatusCode} — {text}");
        if (code is not null)
        {
            Assert.True(text.Contains($"\"code\":\"{code}\"", StringComparison.Ordinal),
                $"Erwartet Fehlercode {code}, kam: {text}");
        }
    }

    public void Dispose() => App.Dispose();
}

/// <summary>
/// Ein Home Assistant, der genau die vier Schnittstellen beantwortet, die
/// <c>/api/ki-ha</c> braucht, und jede Anfrage mitschreibt.
/// </summary>
/// <remarks>
/// Threadsicher, weil die Hintergrunddienste der App dieselbe Verbindung nutzen
/// (einzelne Zustände, die hier 404 bekommen) — darum filtern die Fälle ihre
/// Prüfungen nach Pfad und Entität.
/// </remarks>
public sealed class HaAttrappe : HttpMessageHandler
{
    public const string Adresse = "http://ha.test";

    /// <summary>Zwei echte Sensoren, ein Licht, eine Automation und genug Füllung über die Vorgabe-Anzahl hinaus.</summary>
    public const int AnzahlEntitaeten = 4 + Fuellung;
    private const int Fuellung = 126;

    public sealed record Anfrage(string Methode, string Pfad, string Abfrage, string? Koerper);

    private readonly ConcurrentQueue<Anfrage> _anfragen = new();

    public int Anzahl(Func<Anfrage, bool> bedingung) => _anfragen.Count(bedingung);

    public Anfrage Letzte(Func<Anfrage, bool> bedingung)
        => _anfragen.LastOrDefault(bedingung) ?? throw new Xunit.Sdk.XunitException("Home Assistant wurde dafür nicht gefragt.");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var koerper = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var pfad = request.RequestUri!.AbsolutePath;
        _anfragen.Enqueue(new Anfrage(request.Method.Method, pfad, request.RequestUri.Query, koerper));

        if (request.Method == HttpMethod.Get && pfad == "/api/states") return Json(Zustaende());
        if (request.Method == HttpMethod.Post && pfad == "/api/template") return Text(Bereiche());
        if (request.Method == HttpMethod.Get && pfad.StartsWith("/api/history/period/", StringComparison.Ordinal)) return Json(Verlauf());
        if (request.Method == HttpMethod.Post && pfad.StartsWith("/api/services/", StringComparison.Ordinal)) return Json("[]");
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static string Zustaende()
    {
        var liste = new List<object>
        {
            Zustand("sensor.zelt_temperatur", "24.6", "Zelt Temperatur", "°C"),
            Zustand("sensor.aussen_temperatur", "12.0", "Aussen Temperatur", "°C"),
            Zustand("light.zelt", "on", "Hauptlicht", null),
            Zustand("automation.licht_an", "on", "Licht an", null),
        };
        for (var i = 0; i < Fuellung; i++) liste.Add(Zustand($"binary_sensor.fuellung_{i:000}", "off", $"Füllung {i}", null));
        return JsonSerializer.Serialize(liste);
    }

    private static object Zustand(string id, string zustand, string name, string? einheit) => new Dictionary<string, object?>
    {
        ["entity_id"] = id,
        ["state"] = zustand,
        ["last_changed"] = "2026-10-03T08:15:00+00:00",
        ["attributes"] = einheit is null
            ? new Dictionary<string, object?> { ["friendly_name"] = name }
            : new Dictionary<string, object?> { ["friendly_name"] = name, ["unit_of_measurement"] = einheit },
    };

    private static string Bereiche() => JsonSerializer.Serialize(new object[]
    {
        new { id = "growzelt", name = "Growzelt", entitaeten = new[] { "sensor.zelt_temperatur", "light.zelt" } },
        new { id = "garten", name = "Garten", entitaeten = new[] { "sensor.aussen_temperatur" } },
    });

    private static string Verlauf() => """
        [[{"entity_id":"sensor.zelt_temperatur","state":"24.1","last_changed":"2026-10-03T06:00:00+00:00","attributes":{}},
          {"state":"24.6","last_changed":"2026-10-03T08:15:00+00:00"}]]
        """;

    private static HttpResponseMessage Json(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    // /api/template antwortet mit dem gerenderten Text, nicht mit JSON.
    private static HttpResponseMessage Text(string text)
        => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "text/plain") };
}
