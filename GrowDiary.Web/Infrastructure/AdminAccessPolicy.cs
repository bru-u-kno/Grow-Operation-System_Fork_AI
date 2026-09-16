using System.Net;
using Microsoft.AspNetCore.Http;

namespace GrowDiary.Web.Infrastructure;

/// <summary>
/// Gates administrative and product API routes. Grow OS runs as a Home Assistant
/// add-on: the add-on port is ingress-only (never published to the network), so all
/// real traffic arrives either from loopback or through the Home Assistant ingress
/// proxy, which has already authenticated the user. Any other (direct, non-ingress
/// remote) request to a protected route is refused as defense-in-depth.
/// </summary>
public static class AdminAccessPolicy
{
    // Home Assistant's ingress proxy sets this header on every request it forwards.
    // Its presence means Home Assistant has already authenticated the user.
    public const string IngressPathHeaderName = "X-Ingress-Path";

    private static readonly string[] ProtectedPrefixes =
    {
        "/settings",
        "/einstellungen",
        "/api/settings",
        "/api/system/backup",
        "/api/system/release-readiness",
        "/api/system/database-status",
        "/api/system/api-manifest",
        "/api/system/security-status",
        "/api/system/audit-events",
        "/api/system/error-contract",
        "/api/system/migration-status",
        "/api/system/migration-plan",
        "/api/system/upgrade-preflight",
        "/api/exports"
    };

    private static readonly string[] ProtectedProductApiPrefixes =
    {
        "/api/alerts",
        "/api/notifications",
        "/api/auto-measurements",
        "/api/calibration-events",
        "/api/grows",
        "/api/hardware-items",
        "/api/hydro-setups",
        "/api/journal",
        "/api/home-assistant",
        "/api/knowledge",
        // Hier kommen die KAMERABILDER aus dem Zelt heraus
        // (/api/live/tents/{id}/camera) — das Empfindlichste, was diese App
        // ausliefert. Bis zum 02.09.2026 stand der Praefix nicht in dieser
        // Liste: geschuetzt waren nur die drei LEGACY-Kamerawege, und die
        // Oberflaeche nahm laengst diesen hier.
        "/api/live",
        "/api/light-schedules",
        "/api/light-transitions",
        "/api/maintenance-events",
        "/api/measurements",
        "/api/plants",
        "/api/risk-events",
        "/api/setups",
        "/api/sop-instances",
        "/api/strains",
        "/api/tasks"
    };

    public static IReadOnlyList<string> ProtectedRoutePrefixes => ProtectedPrefixes.Concat(ProtectedProductApiPrefixes).ToArray();

    public static IReadOnlyList<string> ProtectedProductApiRoutePrefixes => ProtectedProductApiPrefixes;

    public static bool IsProtectedPath(PathString path)
    {
        if (ProtectedPrefixes.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (ProtectedProductApiPrefixes.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        /* Hier stand ein Waechter fuer drei Legacy-Kamera-Pfade
           (/tents/{id}/camera.jpg, /camera-stream, /latest-snapshot). Die
           Routen sind am 02.09.2026 geloescht — die Oberflaeche nimmt an allen
           Stellen /api/live/tents/{id}/camera. Ein Waechter fuer Pfade, die es
           nicht gibt, schuetzt nichts und liest sich, als gaebe es sie noch.

           Beim Aufraeumen kam heraus, dass "/api/live" GAR NICHT in der Liste
           oben stand: geschuetzt waren nur die drei alten Wege, waehrend der
           neue offen lag. Er steht jetzt dort. */
        return false;
    }

    /// <summary>
    /// Das interne Add-on-Netz von Home Assistant.
    /// </summary>
    /// <remarks>
    /// Der Supervisor legt dafür eine eigene Docker-Brücke an; darin steckt nur,
    /// was der Betreiber selbst als Add-on installiert hat. Von aussen ist das
    /// Netz nicht erreichbar — Grow OS veröffentlicht keinen Port.
    /// Quelle: Home-Assistant-Dokumentation zur Add-on-Kommunikation.
    /// </remarks>
    private static readonly (IPAddress Netz, int Bits)[] AddonNetworks =
    [
        (IPAddress.Parse("172.30.32.0"), 23),
        (IPAddress.Parse("fd0c:ac1e:2100::"), 48),
    ];

    /// <summary>
    /// Access is allowed for loopback requests and for requests proxied through the
    /// Home Assistant ingress (which Home Assistant has already authenticated).
    /// </summary>
    /// <remarks>
    /// Dazu kommt ein dritter Weg: <b>lesende</b> Anfragen aus dem internen
    /// Add-on-Netz. Ohne ihn könnte ein zweites Add-on — etwa Grow MCP —
    /// nichts abrufen, denn es ist weder Loopback noch Ingress. Bewusst nur
    /// lesend: mitlesen kann ein Nachbar-Add-on damit, schalten oder dosieren
    /// nicht. Für einen Schlüssel hat sich der Betreiber ausdrücklich nicht
    /// entschieden; das Netz enthält nur selbst installierte Software.
    /// </remarks>
    public static bool CanAccess(HttpContext context)
        => IsLocalRequest(context) || IsIngressRequest(context) || IsInternalAddonRead(context);

    /// <summary>Eine lesende Anfrage eines anderen Add-ons im internen Netz.</summary>
    /// <summary>
    /// Lesender Zugriff aus dem Add-on-Netz — Verwaltungswege nur aus der Infrastruktur.
    /// </summary>
    /// <remarks>
    /// <para>Produktdaten (Grows, Messungen, Kamera …) darf jedes Nachbar-Add-on
    /// lesen; davon lebt Grow MCP.</para>
    /// <para><b>Verwaltungswege</b> (Backups, Einstellungen, Exporte, Audit —
    /// siehe <see cref="IsAdministrativePath"/>) nur von den
    /// <see cref="InfrastrukturAdressen"/>. Bis 16.09.2026 konnte jedes Add-on
    /// im Netz per GET das komplette Datenbank-Backup herunterladen
    /// (Fehlerregister F-011). Grow MCP ruft keinen dieser Wege auf.</para>
    /// </remarks>
    public static bool IsInternalAddonRead(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method)) return false;
        if (context.Connection.RemoteIpAddress is not { } ip) return false;
        if (!AddonNetworks.Any(bereich => IsInSubnet(ip, bereich.Netz, bereich.Bits))) return false;

        return !IsAdministrativePath(context.Request.Path) || IsInfrastructureAddress(ip);
    }

    /// <summary>Gehört der Pfad zu den Verwaltungswegen (nicht zu den Produkt-APIs)?</summary>
    public static bool IsAdministrativePath(PathString path)
        => ProtectedPrefixes.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Die Adressen, von denen Home Assistant selbst kommt.
    /// </summary>
    /// <remarks>
    /// <para><c>172.30.32.1</c> ist das Tor der Supervisor-Brücke. Home Assistant
    /// Core läuft im Host-Netz und reicht Ingress-Anfragen von dort weiter —
    /// belegt am 16.09.2026 im Audit-Protokoll einer Supervised-Installation:
    /// jede Ingress-Anfrage (auch vom Handy) kam als <c>172.30.32.1</c> an.</para>
    /// <para><c>172.30.32.2</c> ist der Supervisor; über ihn laufen
    /// Ingress-Anfragen laut Home-Assistant-Doku auf Home Assistant OS.</para>
    /// <para><b>Grenze:</b> Add-ons mit Host-Netz (z. B. Node-RED) erscheinen
    /// ebenfalls als <c>172.30.32.1</c> und sind davon nicht zu unterscheiden —
    /// sie haben aber ohnehin Zugriff auf den Host. Add-ons in der Brücke
    /// (<c>172.30.33.x</c>) sind ausgeschlossen. Ein gemeinsamer Schlüssel
    /// wurde bewusst nicht eingeführt.</para>
    /// </remarks>
    private static readonly IPAddress[] InfrastrukturAdressen =
    [
        IPAddress.Parse("172.30.32.1"),
        IPAddress.Parse("172.30.32.2"),
        IPAddress.Parse("fd0c:ac1e:2100::1"),
        IPAddress.Parse("fd0c:ac1e:2100::2"),
    ];

    /// <summary>Ist die Adresse Home Assistant selbst (Brücken-Tor oder Supervisor)?</summary>
    public static bool IsInfrastructureAddress(IPAddress adresse)
    {
        if (adresse.IsIPv4MappedToIPv6) adresse = adresse.MapToIPv4();
        return InfrastrukturAdressen.Any(a => a.Equals(adresse));
    }

    /// <summary>Liegt die Adresse im angegebenen Netz?</summary>
    private static bool IsInSubnet(IPAddress adresse, IPAddress netz, int bits)
    {
        // Eine per IPv4-mapped-IPv6 hereinkommende Adresse (::ffff:172.30.33.2)
        // sonst nie erkannt worden — Kestrel liefert die je nach Aufbau.
        if (adresse.IsIPv4MappedToIPv6) adresse = adresse.MapToIPv4();
        if (adresse.AddressFamily != netz.AddressFamily) return false;

        var links = adresse.GetAddressBytes();
        var rechts = netz.GetAddressBytes();
        if (links.Length != rechts.Length) return false;

        for (var i = 0; i < links.Length && bits > 0; i++, bits -= 8)
        {
            var maske = bits >= 8 ? (byte)0xFF : (byte)(0xFF << (8 - bits));
            if ((links[i] & maske) != (rechts[i] & maske)) return false;
        }

        return true;
    }

    /// <summary>
    /// Kam die Anfrage wirklich über den Home-Assistant-Ingress?
    /// </summary>
    /// <remarks>
    /// Der Kopf <see cref="IngressPathHeaderName"/> allein beweist nichts — jedes
    /// Programm, das den Container erreicht, kann ihn setzen. Bis 16.09.2026
    /// genügte er trotzdem (Fehlerregister F-011): ein Nachbar-Add-on hätte
    /// damit Einstellungen ändern oder ein Backup zurückspielen können. Gezählt
    /// wird er deshalb nur, wenn die Anfrage von Home Assistant selbst kommt
    /// (<see cref="InfrastrukturAdressen"/>) oder von dieser Maschine.
    /// </remarks>
    public static bool IsIngressRequest(HttpContext context)
        => context.Request.Headers.ContainsKey(IngressPathHeaderName)
           && context.Connection.RemoteIpAddress is { } ip
           && (IsInfrastructureAddress(ip) || IsLocalRequest(context));

    public static bool IsLocalRequest(HttpContext context)
    {
        var remoteIp = context.Connection.RemoteIpAddress;
        var localIp = context.Connection.LocalIpAddress;
        if (remoteIp is null)
        {
            return false;
        }

        return IPAddress.IsLoopback(remoteIp)
               || (localIp is not null && remoteIp.Equals(localIp));
    }
}
