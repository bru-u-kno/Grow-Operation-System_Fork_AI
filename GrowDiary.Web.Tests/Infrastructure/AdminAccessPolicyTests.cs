using System.Net;
using GrowDiary.Web.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace GrowDiary.Web.Tests.Infrastructure;

public sealed class AdminAccessPolicyTests
{
    [Theory]
    [InlineData("/api/settings")]
    [InlineData("/api/settings/tents")]
    [InlineData("/api/system/backup")]
    [InlineData("/api/system/backup/grow-os-backup-20260101-120000.zip")]
    [InlineData("/api/system/release-readiness")]
    [InlineData("/api/system/database-status")]
    [InlineData("/api/system/api-manifest")]
    [InlineData("/api/system/security-status")]
    [InlineData("/api/system/audit-events")]
    [InlineData("/api/system/error-contract")]
    [InlineData("/api/system/migration-status")]
    [InlineData("/api/system/migration-plan")]
    [InlineData("/api/system/upgrade-preflight")]
    [InlineData("/api/system/backup/grow-os-backup-20260101-120000.zip/validate")]
    [InlineData("/api/system/backup/grow-os-backup-20260101-120000.zip/restore-plan")]
    [InlineData("/api/exports/grows/1")]
    [InlineData("/api/exports/grows/validate")]
    [InlineData("/api/exports/grows/import-plan")]
    [InlineData("/api/grows")]
    [InlineData("/api/grows/1")]
    [InlineData("/api/grows/1/addback")]
    [InlineData("/api/grows/1/measurements")]
    [InlineData("/api/hydro-setups")]
    [InlineData("/api/hardware-items")]
    [InlineData("/api/measurements/1")]
    [InlineData("/api/tasks/1")]
    [InlineData("/api/journal/1")]
    [InlineData("/api/plants")]
    [InlineData("/api/strains")]
    [InlineData("/api/risk-events")]
    [InlineData("/api/sop-instances")]
    [InlineData("/api/maintenance-events")]
    [InlineData("/api/calibration-events")]
    [InlineData("/api/auto-measurements")]
    [InlineData("/api/light-schedules")]
    [InlineData("/api/light-transitions")]
    [InlineData("/api/knowledge")]
    // Die drei Legacy-Kamerawege standen hier bis zum 02.09.2026. Sie sind
    // geloescht; an ihrer Stelle steht der Weg, den die Oberflaeche wirklich
    // nimmt — und der lag bis dahin UNGESCHUETZT, weil nur die alten in der
    // Liste standen.
    [InlineData("/api/live/tents/1/camera")]
    [InlineData("/api/live/tents/1")]
    public void IsProtectedPath_ProtectsAdminBackupExportProductApiAndLegacyCameraRoutes(string path)
    {
        Assert.True(AdminAccessPolicy.IsProtectedPath(new PathString(path)));
    }

    [Theory]
    [InlineData("/api/system/backend-health")]
    [InlineData("/api/error")]
    [InlineData("/tents")]
    [InlineData("/tents/1")]
    public void IsProtectedPath_DoesNotProtectExplicitlySafeReadOnlyRoutes(string path)
    {
        Assert.False(AdminAccessPolicy.IsProtectedPath(new PathString(path)));
    }


    [Fact]
    public void ProductApiRoutePrefixes_AreListedSeparatelyForSecurityStatus()
    {
        Assert.Contains(AdminAccessPolicy.ProtectedProductApiRoutePrefixes, prefix => prefix == "/api/grows");
        Assert.Contains(AdminAccessPolicy.ProtectedProductApiRoutePrefixes, prefix => prefix == "/api/hydro-setups");
        Assert.Contains(AdminAccessPolicy.ProtectedProductApiRoutePrefixes, prefix => prefix == "/api/hardware-items");
        Assert.Contains(AdminAccessPolicy.ProtectedRoutePrefixes, prefix => prefix == "/api/grows");
    }

    [Fact]
    public void CanAccess_AllowsLoopbackRequests()
    {
        var context = CreateContext(IPAddress.Loopback, IPAddress.Loopback);

        Assert.True(AdminAccessPolicy.CanAccess(context));
    }

    [Fact]
    public void CanAccess_RejectsRemoteNonIngressRequests()
    {
        var context = CreateContext(IPAddress.Parse("203.0.113.10"), IPAddress.Parse("192.168.1.20"));

        Assert.False(AdminAccessPolicy.CanAccess(context));
    }

    [Theory]
    [InlineData("172.30.32.2")]
    [InlineData("::ffff:172.30.32.2")]
    public void CanAccess_AllowsRequestsThroughTheIngressProxy(string ip)
    {
        var context = CreateContext(IPAddress.Parse(ip), IPAddress.Parse("172.30.33.2"));
        context.Request.Method = HttpMethods.Post;
        context.Request.Headers[AdminAccessPolicy.IngressPathHeaderName] = "/api/hassio_ingress/token";

        Assert.True(AdminAccessPolicy.CanAccess(context));
    }

    /// <summary>
    /// Den Ingress-Kopf kann jeder setzen. Bis zum 01.10.2026 genügte er allein:
    /// ein Nachbar-Add-on schrieb mit <c>X-Ingress-Path: /x</c> überall hin.
    /// </summary>
    [Theory]
    [InlineData("172.30.33.7")]    // ein Nachbar-Add-on
    [InlineData("172.30.32.1")]    // der Gateway
    [InlineData("203.0.113.10")]   // irgendwer
    [InlineData("192.168.1.50")]   // das Heimnetz
    [InlineData("fd0c:ac1e:2100::2")]   // IPv6 im Add-on-Netz vergibt Docker frei
    public void CanAccess_RejectsASpoofedIngressHeader(string ip)
    {
        var context = CreateContext(IPAddress.Parse(ip), IPAddress.Parse("172.30.33.2"));
        context.Request.Method = HttpMethods.Post;
        context.Request.Headers[AdminAccessPolicy.IngressPathHeaderName] = "/x";

        Assert.False(AdminAccessPolicy.CanAccess(context));
    }

    /// <summary>Eine Sicherung ist die ganze Datenbank — auch lesend nichts für Nachbarn.</summary>
    [Theory]
    [InlineData("/api/system/backup/grow-os-backup-20260101-120000.zip")]
    [InlineData("/api/system/audit-events")]
    [InlineData("/api/settings")]
    [InlineData("/api/exports/grows/1")]
    [InlineData("/api/home-assistant/settings")]   // zweiter Weg zu /api/settings/home-assistant
    public void CanAccess_RejectsAdminReadsFromTheInternalAddonNetwork(string path)
    {
        var context = CreateContext(IPAddress.Parse("172.30.33.7"), IPAddress.Parse("172.30.33.2"));
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;

        Assert.False(AdminAccessPolicy.CanAccess(context));
    }

    /// <summary>
    /// Zählung über die Grundmenge: JEDE Route unter /api ist geschützt, außer
    /// den ausdrücklich offenen Wegen mit Grund.
    /// </summary>
    /// <remarks>
    /// Bis zum 01.10.2026 prüfte diese Datei nur eine handgeschriebene Liste —
    /// und die Sperre selbst war auch eine. Offen lagen dadurch unter anderem
    /// <c>POST /api/dosing/pumps/{id}/dose</c> und <c>/api/steuerung/licht/befehl</c>.
    /// </remarks>
    [Fact]
    public void EveryApiRoute_IsProtected_UnlessExplicitlyOpen()
    {
        var routen = typeof(AdminAccessPolicy).Assembly.GetTypes()
            .Where(t => typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t =>
            {
                var basis = t.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.RouteAttribute), true)
                    .Cast<Microsoft.AspNetCore.Mvc.RouteAttribute>().Select(r => r.Template).FirstOrDefault() ?? "";
                return t.GetMethods()
                    .SelectMany(m => m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute), true)
                        .Cast<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>())
                    .Select(a => a.Template is null ? basis
                        : a.Template.StartsWith("~/") ? a.Template[2..]
                        : a.Template.StartsWith('/') ? a.Template[1..]
                        : string.IsNullOrEmpty(basis) ? a.Template : basis + "/" + a.Template);
            })
            .Select(r => "/" + r.Trim('/'))
            .Where(r => r.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToList();

        // Mengenwächter: ohne ihn wäre eine leere Grundmenge grün.
        Assert.True(routen.Count >= 200, $"Nur {routen.Count} API-Routen gefunden — sieht die Zählung ihre Grundmenge?");
        Assert.Contains("/api/dosing/pumps/{id:int}/dose", routen);

        var offen = routen
            .Where(r => !AdminAccessPolicy.IsProtectedPath(new PathString(r)))
            .Where(r => !AdminAccessPolicy.OpenApiRoutes.Keys.Any(w => r.StartsWith(w, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        Assert.True(offen.Count == 0, "Ungeschützt: " + string.Join(", ", offen));
    }

    /// <summary>
    /// Die offene Liste ist seit dem Umbau die EINZIGE Stelle, an der etwas
    /// aufgehen kann — die Zählung oben nimmt ihre Einträge aus und sähe eine
    /// zu weite Ausnahme (etwa "/api/dosing") nicht. Deshalb steht ihr Inhalt
    /// hier fest; wer etwas öffnet, muss es auch hier begründen.
    /// </summary>
    [Fact]
    public void OpenApiRoutes_AreExactlyTheKnownSafeOnes()
    {
        Assert.Equal(
            new[] { "/api/error", "/api/system/backend-health" },
            AdminAccessPolicy.OpenApiRoutes.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.All(AdminAccessPolicy.OpenApiRoutes.Values, grund => Assert.True(grund.Length > 20));
    }

    [Theory]
    [InlineData("/api/dosing/pumps/1/dose")]
    [InlineData("/api/steuerung/licht/befehl")]
    [InlineData("/api/steuerung/chiller/automationen")]
    [InlineData("/api/tents/1")]
    [InlineData("/api/kosten")]
    [InlineData("/api/wochenplan/werte/1")]
    public void IsProtectedPath_ProtectsTheFormerlyOpenWritePaths(string path)
    {
        Assert.True(AdminAccessPolicy.IsProtectedPath(new PathString(path)));
    }

    /// <summary>
    /// Ein Nachbar-Add-on darf lesen — und nur lesen.
    /// </summary>
    /// <remarks>
    /// Ohne diesen Weg käme Grow MCP als zweites Add-on nicht an die Daten: es
    /// ist weder Loopback noch Ingress. Die Beschränkung auf GET ist die
    /// eigentliche Zusage — mitlesen ja, schalten oder dosieren nie.
    /// </remarks>
    [Theory]
    [InlineData("172.30.32.1")]   // der Supervisor-Gateway
    [InlineData("172.30.33.7")]   // ein Add-on-Container
    [InlineData("172.30.32.0")]
    [InlineData("172.30.33.255")]
    public void CanAccess_AllowsReadingFromTheInternalAddonNetwork(string ip)
    {
        var context = CreateContext(IPAddress.Parse(ip), IPAddress.Parse("172.30.33.2"));
        context.Request.Method = HttpMethods.Get;

        Assert.True(AdminAccessPolicy.CanAccess(context));
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public void CanAccess_RejectsWritesFromTheInternalAddonNetwork(string methode)
    {
        var context = CreateContext(IPAddress.Parse("172.30.33.7"), IPAddress.Parse("172.30.33.2"));
        context.Request.Method = methode;

        Assert.False(AdminAccessPolicy.CanAccess(context));
    }

    [Theory]
    [InlineData("172.30.34.1")]     // knapp ausserhalb des /23
    [InlineData("172.30.31.255")]   // knapp davor
    [InlineData("192.168.1.50")]    // das Heimnetz
    [InlineData("10.0.0.5")]
    public void CanAccess_RejectsReadsFromOutsideThatNetwork(string ip)
    {
        var context = CreateContext(IPAddress.Parse(ip), IPAddress.Parse("192.168.1.20"));
        context.Request.Method = HttpMethods.Get;

        Assert.False(AdminAccessPolicy.CanAccess(context));
    }

    [Fact]
    public void CanAccess_RecognisesTheNetworkThroughAnIPv4MappedAddress()
    {
        // Je nach Aufbau reicht Kestrel die Adresse als ::ffff:172.30.33.7
        // durch. Ohne Entpacken waere das Nachbar-Add-on genau dort ausgesperrt,
        // wo es laufen soll — und der Fehler waere schwer zu finden.
        var context = CreateContext(IPAddress.Parse("::ffff:172.30.33.7"), IPAddress.Parse("172.30.33.2"));
        context.Request.Method = HttpMethods.Get;

        Assert.True(AdminAccessPolicy.CanAccess(context));
    }

    private static DefaultHttpContext CreateContext(IPAddress remoteIp, IPAddress localIp)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteIp;
        context.Connection.LocalIpAddress = localIp;
        return context;
    }
}
