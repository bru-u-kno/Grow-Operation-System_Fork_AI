using System.Text.Json;
using GrowDiary.Web.Infrastructure;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fragt den Supervisor, wie dieses Add-on heisst.
/// </summary>
/// <remarks>
/// Wofür: Grow OS wird über den Ingress ausgeliefert, und dieser Pfad
/// (<c>/api/hassio_ingress/&lt;token&gt;/</c>) trägt ein Token, das pro Anfrage
/// wechselt — ein Lesezeichen darauf ist am nächsten Tag tot. Stabil ist nur der
/// Panel-Pfad, und dafür braucht es den Slug.
///
/// Raten geht nicht: der Slug ist je nach Installationsweg <c>local_grow_os</c>
/// oder <c>&lt;repo-hash&gt;_grow_os</c>. Der Supervisor weiss es, also wird er
/// gefragt.
///
/// Das Ergebnis wird gemerkt — der Slug ändert sich zur Laufzeit nie, und die
/// Seite, die ihn braucht, wird oft geöffnet.
/// </remarks>
public sealed class SupervisorInfoService
{
    private const string SelfInfoUrl = "http://supervisor/addons/self/info";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SupervisorInfoService> _logger;

    private string? _cachedSlug;

    public SupervisorInfoService(IHttpClientFactory httpClientFactory, ILogger<SupervisorInfoService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>Der Add-on-Slug, oder null wenn Grow OS nicht als Add-on läuft.</summary>
    public async Task<string?> GetAddonSlugAsync(CancellationToken cancellationToken = default)
    {
        // Testdatenmodus: der Entwicklungsrechner hat keinen Supervisor. Ein
        // plausibler Slug, damit sich die Seite lokal ansehen laesst.
        if (DemoData.IsEnabled)
        {
            return "local_grow_os";
        }

        if (_cachedSlug is not null)
        {
            return _cachedSlug;
        }

        var token = HomeAssistantAddon.SupervisorToken;
        if (token is null)
        {
            return null;
        }

        try
        {
            using var client = _httpClientFactory.CreateClient(nameof(SupervisorInfoService));
            client.Timeout = RequestTimeout;
            using var request = new HttpRequestMessage(HttpMethod.Get, SelfInfoUrl);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Supervisor antwortete auf addons/self/info mit {Status}.", (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            _cachedSlug = ReadSlug(await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken));
            return _cachedSlug;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Supervisor nach dem eigenen Slug gefragt — keine brauchbare Antwort.");
            return null;
        }
    }

    /// <summary>
    /// Die Antwort des Supervisors ist immer <c>{ "result": "ok", "data": { … } }</c>.
    /// </summary>
    public static string? ReadSlug(JsonDocument document)
    {
        if (!document.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("slug", out var slug)
            || slug.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = slug.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// Der stabile Pfad zu diesem Add-on in Home Assistant, optional mit einer
    /// Seite der App dahinter — oder null ohne Slug.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Pfad ist <c>/app/&lt;slug&gt;</c>.</b> Das ist das feste
    /// App-Panel von Home Assistant (<c>hassio/__init__.py</c> registriert
    /// <c>"app"</c>), über das auch „Web-UI öffnen" führt. Es gibt es immer.</para>
    ///
    /// <para><b>Nicht <c>/&lt;slug&gt;</c>.</b> Das ist der Seitenleisten-Eintrag
    /// (<c>addon_panel.py</c>, <c>frontend_url_path=&lt;slug&gt;</c>), und den
    /// legt HA nur an, solange am Add-on „In Seitenleiste anzeigen" an ist. Ist
    /// es aus, antwortet HA mit „404: Not Found" — so endete bis forkai.167
    /// jeder Tipp auf eine Push-Meldung (04.10.2026), und der QR-Code auf der
    /// Handy-Seite ebenso. Davor war schon <c>/hassio/ingress/&lt;slug&gt;</c>
    /// falsch: dort zeichnete die Oberfläche eine leere Seite.</para>
    ///
    /// <para>Den Rest hinter dem Slug reicht HA per <c>postMessage</c> an die
    /// App weiter (<c>home-assistant/properties</c>, <c>route.path</c>); dort
    /// öffnet <c>useHaTiefenlink</c> die Seite.</para>
    /// </remarks>
    public static string? PanelPath(string? slug, string? seite = null)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;
        var rest = seite?.Trim('/');
        return string.IsNullOrEmpty(rest) ? $"/app/{slug}" : $"/app/{slug}/{rest}";
    }

    /// <summary>
    /// Der Pfad zu einem Ingress-Panel ohne HA-Kopfleiste
    /// (<see cref="IngressPanelService"/>), optional mit einer Seite der App.
    /// </summary>
    /// <remarks>
    /// <c>hass_ingress</c> nimmt die Seite nur als <c>?index=</c> an, nicht als
    /// Pfad hinter dem Panel — ein Pfad dahinter wählt dort ein Unter-Panel.
    /// </remarks>
    public static string IngressPanelPfad(string panel, string? seite = null)
    {
        var rest = seite?.Trim('/');
        return string.IsNullOrEmpty(rest) ? $"/{panel}" : $"/{panel}?index={Uri.EscapeDataString(rest)}";
    }
}
