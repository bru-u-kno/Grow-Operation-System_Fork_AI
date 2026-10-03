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
        // Derselbe Endpunkt wie /api/settings/home-assistant, nur ein zweiter
        // Weg (SettingsApiController) — sonst läse ein Nachbar-Add-on ihn hier.
        "/api/home-assistant/settings",
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

    /// <summary>
    /// Wege unter <c>/api</c>, die bewusst JEDER erreichen darf — mit Grund.
    /// </summary>
    /// <remarks>
    /// Fork AI (Sicherheitsprüfung 01.10.2026): Bis hierher war die Sperre eine
    /// handgeschriebene Liste geschützter Präfixe. Sie kannte 33 Präfixe, die App
    /// hat über 60 — offen lagen unter anderem <c>/api/dosing</c> (Pumpe
    /// dosieren), <c>/api/steuerung</c> (Licht schalten, Automationen anlegen),
    /// <c>/api/tents</c> und <c>/api/kosten</c>. Jedes Nachbar-Add-on konnte dort
    /// schreiben. Seitdem gilt umgekehrt: alles unter <c>/api</c> ist geschützt,
    /// offen ist nur, was hier mit Grund steht. Eine Liste kann nur an dem
    /// scheitern, was schon draufsteht — und eine vergessene Ausnahme sperrt,
    /// statt zu öffnen.
    /// </remarks>
    private static readonly Dictionary<string, string> OffeneApiWege = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/api/system/backend-health"] =
            "Lebenszeichen mit Zählungen und Baukennung, ohne Inhalte — fragt Grow MCP "
            + "und der Start-Check ab, bevor überhaupt klar ist, wie man hereinkommt.",
        ["/api/error"] =
            "Der Fehlerbehandler (UseExceptionHandler) — er antwortet nur auf eine "
            + "Anfrage, die schon durch die Sperre gegangen ist.",
    };

    /// <summary>Die offenen Wege samt Grund — für Tests und die Sicherheitsübersicht.</summary>
    public static IReadOnlyDictionary<string, string> OpenApiRoutes => OffeneApiWege;

    public static IReadOnlyList<string> ProtectedRoutePrefixes => ProtectedPrefixes.Concat(ProtectedProductApiPrefixes).Append("/api").ToArray();

    public static IReadOnlyList<string> ProtectedProductApiRoutePrefixes => ProtectedProductApiPrefixes;

    public static bool IsProtectedPath(PathString path)
    {
        if (IsAdminPath(path))
        {
            return true;
        }

        if (path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            return !OffeneApiWege.Keys.Any(weg => path.StartsWithSegments(weg, StringComparison.OrdinalIgnoreCase));
        }

        /* Hier stand ein Waechter fuer drei Legacy-Kamera-Pfade
           (/tents/{id}/camera.jpg, /camera-stream, /latest-snapshot). Die
           Routen sind am 02.09.2026 geloescht — die Oberflaeche nimmt an allen
           Stellen /api/live/tents/{id}/camera. */
        return false;
    }

    /// <summary>
    /// Verwaltung, Sicherungen, Exporte, Prüfprotokoll — auch lesend nur über
    /// Ingress oder Loopback, nie für ein Nachbar-Add-on.
    /// </summary>
    /// <remarks>
    /// Eine Sicherung ist die ganze SQLite-Datei; außerhalb des Add-on-Betriebs
    /// steht darin das HA-Token im Klartext, und den Dateinamen verrät das
    /// Prüfprotokoll. Grow MCP braucht keinen dieser Wege.
    /// </remarks>
    public static bool IsAdminPath(PathString path)
        => ProtectedPrefixes.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));

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
    /// nicht; das Netz enthält nur selbst installierte Software.
    ///
    /// Fork AI (A-003, 03.10.2026): Schreiben aus dem Add-on-Netz geht seitdem
    /// nur mit einem <b>Schlüssel</b> (<see cref="IsKiSchluesselWeg"/>) — und
    /// der ist Opt-in: ab Werk ist der Zugriff für KI-Assistenten aus, und jeder
    /// Schlüssel öffnet nur die Stufen, die der Betreiber angehakt hat. Diese
    /// Methode kennt den Schlüssel nicht; ohne ihn bleibt alles wie zuvor.
    /// </remarks>
    public static bool CanAccess(HttpContext context)
        => IsLocalRequest(context) || IsIngressRequest(context) || IsInternalAddonRead(context);

    /// <summary>
    /// Fork AI (A-003, 03.10.2026): Geht diese Anfrage den Schlüsselweg?
    /// </summary>
    /// <remarks>
    /// <para>Nur, wenn sie einen <c>gok_</c>-Schlüssel trägt <b>und</b> aus dem
    /// Add-on-Netz oder von Loopback kommt. Ein Schlüssel von irgendwo sonst
    /// wird nie geprüft — die Anfrage geht den Weg von heute und bekommt 403.
    /// Sonst wäre der Schlüssel eine Tür ins Netz, das Grow OS nie
    /// veröffentlicht hat.</para>
    ///
    /// <para>Echter Ingress geht vor: dort sitzt ein Mensch, den Home Assistant
    /// angemeldet hat, und ein mitgeschickter Schlüssel ändert daran nichts.
    /// Der Ingress-Proxy liegt selbst im Add-on-Netz (172.30.32.2) — ohne diese
    /// Ausnahme bekäme die Oberfläche die Grenzen eines Schlüssels.</para>
    /// </remarks>
    public static bool IsKiSchluesselWeg(HttpContext context)
        => !IsIngressRequest(context)
           && KiZugriff.KiZugriffDienst.SchluesselAusKopf(context.Request) is not null
           && (IsLocalRequest(context) || IstImAddonNetz(context));

    /// <summary>Kommt die Anfrage aus dem internen Add-on-Netz (egal mit welcher Methode)?</summary>
    public static bool IstImAddonNetz(HttpContext context)
        => context.Connection.RemoteIpAddress is { } ip
           && AddonNetworks.Any(bereich => IsInSubnet(ip, bereich.Netz, bereich.Bits));

    /// <summary>Eine lesende Anfrage eines anderen Add-ons im internen Netz.</summary>
    public static bool IsInternalAddonRead(HttpContext context)
        => HttpMethods.IsGet(context.Request.Method)
           && !IsAdminPath(context.Request.Path)
           && context.Connection.RemoteIpAddress is { } ip
           && AddonNetworks.Any(bereich => IsInSubnet(ip, bereich.Netz, bereich.Bits));

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
    /// Die Adressen des Ingress-Proxys im Supervisor.
    /// </summary>
    /// <remarks>
    /// Quelle: Home-Assistant-Entwicklerdoku „Presenting your app → Ingress":
    /// ein Add-on soll Ingress-Anfragen nur von 172.30.32.2 annehmen.
    /// </remarks>
    private static readonly IPAddress[] IngressProxies =
    [
        // Nur IPv4: der Supervisor vergibt fest nur diese Adresse, und Ingress
        // spricht das Add-on über IPv4 an. Eine IPv6 im Add-on-Netz vergibt
        // Docker frei — sie könnte einem anderen Container gehören.
        IPAddress.Parse("172.30.32.2"),
    ];

    /// <summary>True when the request is proxied through the Home Assistant ingress.</summary>
    /// <remarks>
    /// Fork AI (Sicherheitsprüfung 01.10.2026): Vorher genügte der Kopf allein.
    /// Den kann jeder setzen — jedes Add-on im internen Netz bekam mit
    /// <c>X-Ingress-Path: /x</c> vollen Schreibzugriff, auch auf Sicherung
    /// zurückspielen und Einstellungen. Der Kopf zählt jetzt nur, wenn die
    /// Anfrage vom Ingress-Proxy des Supervisors kommt.
    /// </remarks>
    public static bool IsIngressRequest(HttpContext context)
        => context.Request.Headers.ContainsKey(IngressPathHeaderName)
           && context.Connection.RemoteIpAddress is { } ip
           && IngressProxies.Contains(ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip);

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
