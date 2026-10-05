using System.Text.Json;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Services;

/// <summary>
/// Push-Meldungen öffnen das Ingress-Panel ohne HA-Kopfleiste, wenn es eins gibt.
/// </summary>
/// <remarks>
/// Anlass (05.10.2026): Bru öffnet Grow OS am Handy über ein Panel der
/// HACS-Integration „Ingress" ohne die weiße HA-Kopfleiste. Der Tipp auf eine
/// Meldung öffnete aber <c>/app/&lt;slug&gt;/…</c> — mit Leiste. Die Antworten von
/// <c>get_panels</c> hier sind der Form nachgebaut, die <c>panel_custom</c> und
/// <c>hass_ingress</c> erzeugen: die Panel-Konfiguration steht direkt unter
/// <c>config</c>, daneben <c>_panel_custom</c> mit dem Element.
/// </remarks>
public sealed class IngressPanelServiceTests
{
    private const string Slug = "d48160c2_grow_os_fork_ai";

    private static readonly HomeAssistantSettings Ha = new() { BaseUrl = "http://ha.local:8123", AccessToken = "token", Enabled = true };

    /// <summary>Ein Panel, wie <c>hass_ingress</c> es im Modus <c>hassio</c> anlegt.</summary>
    private static string IngressPanel(string name, string addon = Slug, string uiMode = "normal", string element = "ha-panel-ingress") => $$"""
        "{{name}}": {
          "component_name": "custom",
          "icon": "mdi:sprout",
          "title": "Grow OS",
          "url_path": "{{name}}",
          "require_admin": false,
          "config": {
            "token": "abc",
            "index": "",
            "addon": "{{addon}}",
            "ui_mode": "{{uiMode}}",
            "_panel_custom": { "name": "{{element}}", "embed_iframe": false, "trust_external": false, "js_url": "/files/ingress/entrypoint.js" }
          }
        }
        """;

    /// <summary>Panels, die jede Anlage hat und die nie gemeint sind.</summary>
    private const string Grundpanels = """
        "lovelace": { "component_name": "lovelace", "url_path": "lovelace", "config": { "mode": "storage" } },
        "app": { "component_name": "app", "url_path": "app", "config": null },
        "map": { "component_name": "lovelace", "url_path": "map", "config": { "mode": "storage" } }
        """;

    private static JsonElement Panels(params string[] eintraege) =>
        JsonDocument.Parse("{" + string.Join(",", eintraege.Prepend(Grundpanels)) + "}").RootElement.Clone();

    [Fact]
    public void Erkennt_das_Ingress_Panel_ohne_Kopfleiste_fuer_dieses_Addon()
    {
        Assert.Equal("growos", IngressPanelService.PanelFuer(Panels(IngressPanel("growos")), Slug));
    }

    [Fact]
    public void Ohne_Ingress_Panel_gibt_es_keins()
    {
        Assert.Null(IngressPanelService.PanelFuer(Panels(), Slug));
    }

    [Fact]
    public void Ein_Panel_mit_Kopfleiste_ist_nicht_gemeint()
    {
        // ui_mode toolbar zeigt die HA-Leiste — dann ist das App-Panel genauso gut.
        Assert.Null(IngressPanelService.PanelFuer(Panels(IngressPanel("growos", uiMode: "toolbar")), Slug));
    }

    [Fact]
    public void Ein_Panel_fuer_ein_anderes_Addon_ist_nicht_gemeint()
    {
        // Das Original-Grow-OS läuft bei Bru daneben.
        Assert.Null(IngressPanelService.PanelFuer(Panels(IngressPanel("growos", addon: "9a313175_grow_os")), Slug));
    }

    [Fact]
    public void Ein_anderes_eigenes_Panel_ist_nicht_gemeint()
    {
        Assert.Null(IngressPanelService.PanelFuer(Panels(IngressPanel("growos", element: "ha-panel-iframe-plus")), Slug));
    }

    [Theory]
    [InlineData("../states")]
    [InlineData("grow os")]
    [InlineData("growos?x=1")]
    [InlineData("Growos")]
    public void Ein_Panelname_mit_fremden_Zeichen_wird_nicht_benutzt(string name)
    {
        // Leitplanke 6: Was in einen Pfad wandert, nur aus einer Zeichenliste.
        Assert.Null(IngressPanelService.PanelFuer(Panels(IngressPanel(name)), Slug));
    }

    [Fact]
    public void Bei_mehreren_passenden_Panels_entscheidet_der_Name_nicht_die_Reihenfolge()
    {
        Assert.Equal("grow_a", IngressPanelService.PanelFuer(Panels(IngressPanel("grow_b"), IngressPanel("grow_a")), Slug));
        Assert.Equal("grow_a", IngressPanelService.PanelFuer(Panels(IngressPanel("grow_a"), IngressPanel("grow_b")), Slug));
    }

    [Theory]
    [InlineData(null, "/growos")]
    [InlineData("", "/growos")]
    [InlineData("live", "/growos?index=live")]
    [InlineData("live/3", "/growos?index=live%2F3")]
    [InlineData("/aufgaben/", "/growos?index=aufgaben")]
    public void Die_Seite_geht_als_index_an_das_Panel(string? seite, string erwartet)
    {
        Assert.Equal(erwartet, SupervisorInfoService.IngressPanelPfad("growos", seite));
    }

    [Fact]
    public async Task Die_Antwort_wird_zehn_Minuten_gemerkt_auch_ohne_Panel()
    {
        var uhr = new StellbareUhr();
        var abfragen = 0;
        JsonElement? antwort = Panels();
        var dienst = new IngressPanelService(NullLogger<IngressPanelService>.Instance, uhr, (_, _) =>
        {
            abfragen++;
            return Task.FromResult(antwort);
        });

        Assert.Null(await dienst.PanelAsync(Ha, Slug, default));
        antwort = Panels(IngressPanel("growos"));
        uhr.Weiter(IngressPanelService.Gueltigkeit - TimeSpan.FromSeconds(1));
        Assert.Null(await dienst.PanelAsync(Ha, Slug, default));
        Assert.Equal(1, abfragen);

        uhr.Weiter(TimeSpan.FromSeconds(2));
        Assert.Equal("growos", await dienst.PanelAsync(Ha, Slug, default));
        Assert.Equal(2, abfragen);
    }

    [Fact]
    public async Task Ohne_Antwort_von_Home_Assistant_wird_nur_kurz_gemerkt()
    {
        // HA startet neu: die Meldungen sollen danach nicht zehn Minuten mit Kopfleiste kommen.
        var uhr = new StellbareUhr();
        JsonElement? antwort = null;
        var dienst = new IngressPanelService(NullLogger<IngressPanelService>.Instance, uhr,
            (_, _) => Task.FromResult(antwort));

        Assert.Null(await dienst.PanelAsync(Ha, Slug, default));
        antwort = Panels(IngressPanel("growos"));
        uhr.Weiter(IngressPanelService.GueltigkeitOhneAntwort + TimeSpan.FromSeconds(1));
        Assert.Equal("growos", await dienst.PanelAsync(Ha, Slug, default));
    }

    [Fact]
    public async Task Ein_haengendes_Home_Assistant_haelt_die_Meldung_nur_bis_zur_Frist_auf()
    {
        var dienst = new IngressPanelService(NullLogger<IngressPanelService>.Instance, new StellbareUhr(),
            async (_, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return null;
            });

        var dauer = System.Diagnostics.Stopwatch.StartNew();
        Assert.Null(await dienst.PanelAsync(Ha, Slug, default));
        Assert.True(dauer.Elapsed < IngressPanelService.Frist + TimeSpan.FromSeconds(3), $"{dauer.Elapsed} gewartet");
    }

    private sealed class StellbareUhr : TimeProvider
    {
        private DateTimeOffset _jetzt = new(2026, 10, 5, 2, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _jetzt;
        public void Weiter(TimeSpan dauer) => _jetzt += dauer;
    }
}
