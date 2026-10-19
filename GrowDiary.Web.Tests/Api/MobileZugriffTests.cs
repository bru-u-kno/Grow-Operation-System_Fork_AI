using System.Net.Http.Json;
using System.Text.Json;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// <c>GET /api/system/mobile-access</c> sagt, ob es für diese App schon ein Ingress-Panel ohne HA-Titelleiste gibt.
/// </summary>
/// <remarks>
/// Die Oberfläche (Handy → App einrichten) zeigt danach die Hilfe „Ohne HA-Titelleiste“: Anleitung, wenn es kein Panel
/// gibt, einen Hinweis, wenn es eins gibt. Die Antwort kommt von derselben Abfrage, mit der die Push-Meldungen ihren
/// Link wählen (<see cref="IngressPanelService"/>). Der Test ersetzt nur die Abfrage bei Home Assistant, den Rest fährt
/// die echte App — im Demomodus mit dem Slug <c>local_grow_os</c>.
/// </remarks>
public sealed class MobileZugriffTests
{
    private const string DemoSlug = "local_grow_os";

    private static JsonElement Panels(params (string Name, string Addon, string UiMode)[] ingress)
    {
        var eintraege = ingress.Select(p => $$"""
            "{{p.Name}}": { "component_name": "custom", "url_path": "{{p.Name}}",
              "config": { "addon": "{{p.Addon}}", "ui_mode": "{{p.UiMode}}", "_panel_custom": { "name": "ha-panel-ingress" } } }
            """);
        return JsonDocument.Parse("{" + string.Join(",", eintraege.Prepend(
            "\"lovelace\": { \"component_name\": \"lovelace\", \"url_path\": \"lovelace\", \"config\": null }")) + "}").RootElement.Clone();
    }

    private static IngressPanelService Dienst(JsonElement panels, Action? beiAbfrage = null) =>
        new(NullLogger<IngressPanelService>.Instance, TimeProvider.System, (_, _) =>
        {
            beiAbfrage?.Invoke();
            return Task.FromResult<JsonElement?>(panels);
        });

    private static readonly HomeAssistantSettings Ha = new() { BaseUrl = "http://ha.local:8123", AccessToken = "token", Enabled = true };

    [Fact]
    public async Task NenntDasPanelOhneTitelleisteFuerDieseApp()
    {
        var dienst = Dienst(Panels(("growos", DemoSlug, "normal")));

        Assert.Equal("growos", await IngressPanelService.PanelOderNullAsync(dienst, Ha, DemoSlug, CancellationToken.None));
    }

    [Fact]
    public async Task OhnePanelFuerDieseAppKommtNullZurueck()
    {
        // Ein Panel mit Titelleiste (toolbar) und eins für ein anderes Add-on zählen nicht.
        var dienst = Dienst(Panels(("nodered", "a0d7b954_nodered", "normal"), ("growos", DemoSlug, "toolbar")));

        Assert.Null(await IngressPanelService.PanelOderNullAsync(dienst, Ha, DemoSlug, CancellationToken.None));
    }

    [Fact]
    public async Task OhneSlugOderDienstWirdHomeAssistantNichtGefragt()
    {
        var gefragt = 0;
        var dienst = Dienst(Panels(("growos", DemoSlug, "normal")), () => gefragt++);

        Assert.Null(await IngressPanelService.PanelOderNullAsync(dienst, Ha, null, CancellationToken.None));
        Assert.Null(await IngressPanelService.PanelOderNullAsync(dienst, Ha, "  ", CancellationToken.None));
        Assert.Null(await IngressPanelService.PanelOderNullAsync(null, Ha, DemoSlug, CancellationToken.None));
        Assert.Equal(0, gefragt);
    }

    [Fact]
    public async Task DieRouteAntwortetAuchOhneAddonUndSagtKeinPanel()
    {
        // Im Test gibt es keinen Supervisor, also keinen Slug: Die Route antwortet trotzdem, und „kein Panel“ heißt hier
        // „Feld fehlt oder null“ (die App lässt leere Felder weg). Die Oberfläche behandelt beides gleich.
        using var app = new IntegrationsApp();

        var antwort = await app.IngressClient().GetFromJsonAsync<JsonElement>("/api/system/mobile-access");

        Assert.False(antwort.GetProperty("available").GetBoolean());
        if (antwort.TryGetProperty("ingressPanel", out var panel))
        {
            Assert.Equal(JsonValueKind.Null, panel.ValueKind);
        }
    }
}
