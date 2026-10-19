using Microsoft.Extensions.Logging;

namespace GrowOsAccess;

/// <summary>Was der Betreiber zur Anbindung eingetragen hat.</summary>
/// <remarks>
/// Jedes Add-on liest seine eigene Konfiguration und legt das Ergebnis hier ab.
/// Die Bibliothek kennt dadurch keine <c>options.json</c> und keinen Slug — sie
/// bekommt nur die eine Angabe, die sie braucht.
/// </remarks>
public sealed class GrowOsOptions
{
    /// <summary>Eine von Hand eingetragene Adresse, sonst leer.</summary>
    public string Adresse { get; init; } = string.Empty;

    /// <summary>
    /// Der eigene Slug ohne Repository-Vorsatz, so wie er in der
    /// <c>config.yaml</c> dieses Add-ons steht, derzeit <c>cultivate_control_mcp</c>.
    /// </summary>
    /// <remarks>
    /// Daraus wird der Name von Cultivate Control abgeleitet. Wer ihn falsch angibt, findet
    /// ein Cultivate Control aus dem Store nicht mehr.
    /// </remarks>
    public string EigenerSlug { get; init; } = string.Empty;
}

/// <summary>Das Ergebnis der Suche: erreichbar unter welcher Adresse.</summary>
/// <param name="Erreichbar">Hat unter <paramref name="Basis"/> jemand geantwortet?</param>
/// <param name="Basis">Die vollständige Adresse, etwa <c>http://a1b2c3d4-grow-os:5076</c>.</param>
/// <param name="Meldung">Ein Satz für den Betreiber — auch im Erfolgsfall.</param>
public sealed record GrowOsVerbindung(bool Erreichbar, string? Basis, string Meldung);

/// <summary>
/// Findet Cultivate Control und merkt sich, wo es steckt.
/// </summary>
/// <remarks>
/// <para>Drei Wege, in dieser Reihenfolge: eine von Hand eingetragene Adresse
/// schlägt alles — wer den Weg kennt, soll nicht an einer fehlenden Auskunft
/// scheitern. Läuft dieses Programm nicht als Add-on, ist Entwicklungsbetrieb
/// gemeint und Cultivate Control liegt auf demselben Rechner. Sonst wird der Name aus dem
/// eigenen abgeleitet und der Reihe nach angeklopft.</para>
///
/// <para>Die gefundene Adresse wird behalten. Ohne das klopfte ein MCP-Server bei
/// jedem einzelnen Werkzeugaufruf erneut an bis zu drei Namen — der erste Aufruf
/// darf suchen, der zwanzigste nicht mehr. Antwortet die gemerkte Adresse nicht
/// mehr, wird sie verworfen und neu gesucht; ein Neustart von Cultivate Control unter
/// anderem Namen heilt sich damit von selbst.</para>
/// </remarks>
public sealed class GrowOsDiscovery
{
    /// <summary>Der Name des Klienten, den diese Suche zum Anklopfen benutzt.</summary>
    public const string HttpClientName = "grow-os-probe";

    private readonly IHttpClientFactory _fabrik;
    private readonly SupervisorClient _supervisor;
    private readonly GrowOsOptions _optionen;
    private readonly ILogger<GrowOsDiscovery> _logger;

    /// <summary>
    /// Die zuletzt funktionierende Adresse.
    /// </summary>
    /// <remarks>
    /// Ohne Sperre: im schlimmsten Fall suchen zwei gleichzeitige Anfragen beide,
    /// und beide schreiben dasselbe Ergebnis. Das kostet einen Anklopfer, nichts
    /// weiter — eine Sperre um einen Netzaufruf wäre der schlechtere Tausch.
    /// </remarks>
    private volatile string? _gemerkt;

    /// <remarks>
    /// Die Klienten-Fabrik statt eines eigenen <see cref="HttpClient"/>: ein
    /// typisierter Klient waere transient, und mit ihm dieser Dienst — die
    /// gemerkte Adresse waere bei jedem Aufruf wieder weg. So bleibt dieser Dienst
    /// ein Singleton.
    /// </remarks>
    public GrowOsDiscovery(
        IHttpClientFactory fabrik, SupervisorClient supervisor, GrowOsOptions optionen, ILogger<GrowOsDiscovery> logger)
    {
        _fabrik = fabrik;
        _supervisor = supervisor;
        _optionen = optionen;
        _logger = logger;
    }

    /// <summary>Antwortet Cultivate Control unter dieser Adresse?</summary>
    /// <remarks>
    /// Gefragt wird nach der Mappen-Übersicht: ein kleiner, lesender Endpunkt, den
    /// es nur in Cultivate Control gibt. Ein beliebiger Webserver auf demselben Port fiele
    /// damit durch, statt sich als Cultivate Control auszugeben.
    /// </remarks>
    public async Task<bool> ErreichbarAsync(string basis, CancellationToken cancellationToken)
    {
        try
        {
            using var klient = _fabrik.CreateClient(HttpClientName);
            using var antwort = await klient.GetAsync($"{basis}/api/agent-export/mappe", cancellationToken);
            return antwort.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cultivate Control unter {Basis} nicht erreichbar.", basis);
            return false;
        }
    }

    /// <summary>Cultivate Control suchen — oder sagen, warum es nicht geht.</summary>
    public async Task<GrowOsVerbindung> FindenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_optionen.Adresse))
        {
            var adresse = _optionen.Adresse.Trim().TrimEnd('/');
            var erreichbar = await ErreichbarAsync(adresse, cancellationToken);
            return new GrowOsVerbindung(
                erreichbar, adresse,
                erreichbar
                    ? $"Cultivate Control laut Einstellungen unter {adresse}."
                    : $"Unter der eingetragenen Adresse {adresse} antwortet Cultivate Control nicht.");
        }

        if (!SupervisorClient.ImAddon)
        {
            var lokal = GrowOsLocator.BaseUrl("localhost");
            return new GrowOsVerbindung(
                await ErreichbarAsync(lokal, cancellationToken), lokal,
                "Entwicklungsbetrieb: Cultivate Control auf localhost.");
        }

        if (_gemerkt is { } bekannt && await ErreichbarAsync(bekannt, cancellationToken))
        {
            return new GrowOsVerbindung(true, bekannt, $"Cultivate Control unter {bekannt}.");
        }

        _gemerkt = null;

        var eigener = await _supervisor.EigenerSlugAsync(cancellationToken);
        foreach (var kandidat in GrowOsLocator.Kandidaten(eigener, _optionen.EigenerSlug))
        {
            var basis = GrowOsLocator.BaseUrl(GrowOsLocator.Hostname(kandidat));
            if (!await ErreichbarAsync(basis, cancellationToken)) continue;

            _gemerkt = basis;
            return new GrowOsVerbindung(true, basis, $"Cultivate Control gefunden unter {basis}.");
        }

        return new GrowOsVerbindung(false, null, GrowOsLocator.NichtGefunden.Meldung);
    }
}
