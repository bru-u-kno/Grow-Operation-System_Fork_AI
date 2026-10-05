using System.Text.Json;
using System.Text.RegularExpressions;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Findet ein Panel der HACS-Integration „Ingress" (<c>lovelylain/hass_ingress</c>),
/// das dieses Add-on ohne die Kopfleiste von Home Assistant zeigt.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (05.10.2026).</b> Seit forkai.168 öffnet ein Tipp auf eine
/// Push-Meldung <c>/app/&lt;slug&gt;/&lt;seite&gt;</c>. Das ist das App-Panel von Home
/// Assistant — mit der weißen HA-Kopfleiste darüber. Bru öffnet Grow OS am Handy
/// aber über ein Ingress-Panel mit <c>ui_mode: normal</c> (FORK.md, „Empfohlen:
/// ohne die Kopfleiste"), und genau diese Leiste wollte er dort loswerden. Die
/// Meldung landete auf der richtigen Seite, aber in der falschen Hülle.</para>
///
/// <para><b>Wie das Panel die Seite bekommt.</b> <c>hass_ingress</c> liest beim
/// Öffnen <c>?index=…</c> aus der Adresse und hängt es an die Ingress-Adresse
/// des Add-ons (<c>www/entrypoint.js</c>, <c>_getTargetUrl</c>). Im Modus
/// <c>hassio</c> trägt jedes Panel ein <c>index</c> (Voreinstellung leer), also
/// greift das immer. Der Link heißt dann <c>/&lt;panel&gt;?index=live/3</c>.</para>
///
/// <para><b>Woran das Panel erkannt wird.</b> <c>get_panels</c> am WebSocket
/// liefert jedes Panel mit seiner Konfiguration. Ein Ingress-Panel ist ein
/// <c>panel_custom</c> mit dem Element <c>ha-panel-ingress</c>; seine
/// Konfiguration nennt im Modus <c>hassio</c> den Add-on-Slug unter
/// <c>addon</c> und die Darstellung unter <c>ui_mode</c>. Nur <c>normal</c> ist
/// ohne Kopfleiste — <c>toolbar</c> zeigt sie wie das App-Panel.</para>
///
/// <para><b>Ohne Panel bleibt alles wie vorher</b> (<see cref="SupervisorInfoService.PanelPath"/>).
/// Die Integration ist freiwillig.</para>
/// </remarks>
public sealed class IngressPanelService
{
    /// <summary>
    /// So lange gilt eine Antwort — auch „kein Panel". Ein Panel entsteht nur mit
    /// einem Eintrag in <c>configuration.yaml</c> und einem Neustart von Home
    /// Assistant; zehn Minuten Verzögerung danach sind hinnehmbar, eine
    /// WebSocket-Anmeldung vor jeder Meldung nicht.
    /// </summary>
    public static readonly TimeSpan Gueltigkeit = TimeSpan.FromMinutes(10);

    /// <summary>
    /// So lange gilt „Home Assistant hat nicht geantwortet" (Neustart, Anmeldung
    /// abgelehnt). Kurz, damit die Meldungen danach nicht zehn Minuten lang mit
    /// Kopfleiste kommen (Prüfer 05.10.2026).
    /// </summary>
    public static readonly TimeSpan GueltigkeitOhneAntwort = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Längste Wartezeit für Anmeldung und Antwort zusammen. Eine Meldung soll
    /// nicht an einem langsamen Home Assistant hängen; ohne Antwort geht sie mit
    /// dem App-Panel raus.
    /// </summary>
    public static readonly TimeSpan Frist = TimeSpan.FromSeconds(5);

    /// <summary>Leitplanke 6: Was in einen Pfad wandert, nur aus einer Zeichenliste.</summary>
    private static readonly Regex PanelName = new("^[a-z0-9_-]+$", RegexOptions.CultureInvariant);

    private readonly ILogger<IngressPanelService> _log;
    private readonly TimeProvider _zeit;
    private readonly Func<HomeAssistantSettings, CancellationToken, Task<JsonElement?>> _panelsAbfragen;
    private readonly SemaphoreSlim _sperre = new(1, 1);
    private (string Slug, string? Panel, DateTimeOffset Bis)? _gemerkt;

    public IngressPanelService(ILogger<IngressPanelService> log)
        : this(log, TimeProvider.System, PanelsAbfragenAsync)
    {
    }

    /// <summary>Für Tests: Uhr und die Abfrage bei Home Assistant von außen.</summary>
    public IngressPanelService(
        ILogger<IngressPanelService> log,
        TimeProvider zeit,
        Func<HomeAssistantSettings, CancellationToken, Task<JsonElement?>> panelsAbfragen)
    {
        _log = log;
        _zeit = zeit;
        _panelsAbfragen = panelsAbfragen;
    }

    /// <summary>
    /// Der Name des Ingress-Panels ohne Kopfleiste für dieses Add-on, oder null.
    /// </summary>
    public async Task<string?> PanelAsync(HomeAssistantSettings settings, string slug, CancellationToken ct)
    {
        if (Gemerkt(slug) is { } schnell) return schnell.Panel;

        await _sperre.WaitAsync(ct);
        try
        {
            if (Gemerkt(slug) is { } gemerkt) return gemerkt.Panel;

            var (geantwortet, panel) = await AbfragenAsync(settings, slug, ct);
            _gemerkt = (slug, panel, _zeit.GetUtcNow() + (geantwortet ? Gueltigkeit : GueltigkeitOhneAntwort));
            return panel;
        }
        finally
        {
            _sperre.Release();
        }
    }

    private (string Slug, string? Panel, DateTimeOffset Bis)? Gemerkt(string slug) =>
        _gemerkt is { } g && g.Slug == slug && g.Bis > _zeit.GetUtcNow() ? g : null;

    private async Task<(bool Geantwortet, string? Panel)> AbfragenAsync(
        HomeAssistantSettings settings, string slug, CancellationToken ct)
    {
        // Eine Frist für beides — Anmeldung und Antwort.
        using var begrenzt = CancellationTokenSource.CreateLinkedTokenSource(ct);
        begrenzt.CancelAfter(Frist);

        JsonElement? panels;
        try
        {
            panels = await _panelsAbfragen(settings, begrenzt.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            panels = null;
        }

        if (panels is not { } antwort) return (false, null);

        var panel = PanelFuer(antwort, slug);
        if (panel is not null)
        {
            _log.LogInformation("Push-Meldungen öffnen das Ingress-Panel /{Panel} (ohne HA-Kopfleiste).", panel);
        }

        return (true, panel);
    }

    /// <summary>Alle Panels von Home Assistant (<c>get_panels</c>), oder null ohne Antwort.</summary>
    private static async Task<JsonElement?> PanelsAbfragenAsync(HomeAssistantSettings settings, CancellationToken ct)
    {
        await using var socket = await HomeAssistantSocket.OeffnenAsync(settings, ct, Frist);
        if (socket is null) return null;

        var antwort = await socket.BefehlAsync("get_panels", new Dictionary<string, object?>(), ct, Frist);
        return antwort.Erfolg ? antwort.Ergebnis : null;
    }

    /// <summary>
    /// Sucht in der Antwort von <c>get_panels</c> das Ingress-Panel ohne
    /// Kopfleiste, das auf <paramref name="slug"/> zeigt.
    /// </summary>
    /// <remarks>
    /// Mehrere passende Panels: das erste nach Namen, damit der Link nicht von
    /// der Reihenfolge der Antwort abhängt.
    /// </remarks>
    public static string? PanelFuer(JsonElement panels, string slug)
    {
        if (panels.ValueKind != JsonValueKind.Object) return null;

        return panels.EnumerateObject()
            .Select(p => p.Value)
            .Where(p => p.ValueKind == JsonValueKind.Object)
            .Where(p => Text(p, "component_name") == "custom")
            .Select(p => (Name: Text(p, "url_path"), Eigen: Eigenkonfiguration(p)))
            .Where(p => p.Name is not null && PanelName.IsMatch(p.Name))
            .Where(p => p.Eigen is { } e
                && Text(e, "addon") == slug
                && Text(e, "ui_mode") == "normal")
            .Select(p => p.Name!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>
    /// Die Konfiguration, die <c>hass_ingress</c> dem Panel mitgibt — oder null,
    /// wenn es kein Ingress-Panel ist.
    /// </summary>
    /// <remarks>
    /// <c>panel_custom</c> gibt sie unverändert als <c>config</c> weiter und legt
    /// nur <c>_panel_custom</c> mit dem Namen des Elements dazu
    /// (<c>panel_custom/__init__.py</c>, <c>async_register_panel</c>).
    /// </remarks>
    private static JsonElement? Eigenkonfiguration(JsonElement panel)
    {
        if (!panel.TryGetProperty("config", out var config) || config.ValueKind != JsonValueKind.Object) return null;
        if (!config.TryGetProperty("_panel_custom", out var custom) || custom.ValueKind != JsonValueKind.Object) return null;
        return Text(custom, "name") == "ha-panel-ingress" ? config : null;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var wert) && wert.ValueKind == JsonValueKind.String ? wert.GetString() : null;
}
