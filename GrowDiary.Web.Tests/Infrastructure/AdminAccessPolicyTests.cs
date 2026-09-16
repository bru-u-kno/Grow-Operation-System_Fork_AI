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

    /// <summary>
    /// Der Ingress-Kopf zählt nur, wenn Home Assistant selbst ihn schickt.
    /// </summary>
    /// <remarks>
    /// Fehlerregister F-011 (16.09.2026): Bis dahin genügte der Kopf allein —
    /// jedes Programm, das den Container erreicht, hätte damit schreiben
    /// dürfen. <c>172.30.32.1</c> ist belegt (Audit-Protokoll einer
    /// Supervised-Installation), <c>172.30.32.2</c> ist der Supervisor.
    /// </remarks>
    [Theory]
    [InlineData("172.30.32.1")]
    [InlineData("172.30.32.2")]
    [InlineData("::ffff:172.30.32.1")]
    public void CanAccess_AllowsIngressWritesFromHomeAssistant(string ip)
    {
        var context = CreateContext(IPAddress.Parse(ip), IPAddress.Parse("172.30.33.6"));
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/system/backup";
        context.Request.Headers[AdminAccessPolicy.IngressPathHeaderName] = "/api/hassio_ingress/token";

        Assert.True(AdminAccessPolicy.CanAccess(context));
    }

    [Theory]
    [InlineData("172.30.33.7")]     // ein Nachbar-Add-on in der Brücke
    [InlineData("172.30.32.3")]     // ein anderer Infrastruktur-Dienst (DNS)
    [InlineData("203.0.113.10")]    // von aussen
    [InlineData("192.168.1.50")]    // das Heimnetz
    public void CanAccess_RejectsAForgedIngressHeader(string ip)
    {
        var context = CreateContext(IPAddress.Parse(ip), IPAddress.Parse("172.30.33.6"));
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/system/backup";
        context.Request.Headers[AdminAccessPolicy.IngressPathHeaderName] = "/api/hassio_ingress/token";

        Assert.False(AdminAccessPolicy.CanAccess(context));
    }

    [Fact]
    public void CanAccess_AllowsTheIngressHeaderFromThisMachine()
    {
        var context = CreateContext(IPAddress.Loopback, IPAddress.Loopback);
        context.Request.Method = HttpMethods.Post;
        context.Request.Headers[AdminAccessPolicy.IngressPathHeaderName] = "/api/hassio_ingress/token";

        Assert.True(AdminAccessPolicy.IsIngressRequest(context));
    }

    /// <summary>
    /// Verwaltungswege lesen nur Home Assistant selbst — nicht jedes Nachbar-Add-on.
    /// </summary>
    [Theory]
    [InlineData("/api/system/backup/grow-os-backup-20260101-120000.zip")]
    [InlineData("/api/system/backup")]
    [InlineData("/api/settings")]
    [InlineData("/api/exports/grows/1")]
    [InlineData("/api/system/audit-events")]
    public void CanAccess_RejectsAdministrativeReadsFromANeighbourAddon(string path)
    {
        var nachbar = CreateContext(IPAddress.Parse("172.30.33.7"), IPAddress.Parse("172.30.33.6"));
        nachbar.Request.Method = HttpMethods.Get;
        nachbar.Request.Path = path;
        Assert.False(AdminAccessPolicy.CanAccess(nachbar));

        var homeAssistant = CreateContext(IPAddress.Parse("172.30.32.1"), IPAddress.Parse("172.30.33.6"));
        homeAssistant.Request.Method = HttpMethods.Get;
        homeAssistant.Request.Path = path;
        Assert.True(AdminAccessPolicy.CanAccess(homeAssistant));
    }

    /// <summary>Produktdaten bleiben für Nachbar-Add-ons lesbar — davon lebt Grow MCP.</summary>
    [Theory]
    [InlineData("/api/grows")]
    [InlineData("/api/grows/1")]
    [InlineData("/api/agent-export/grows/1")]
    [InlineData("/api/tents/1/history")]
    public void CanAccess_KeepsProductReadsOpenForNeighbourAddons(string path)
    {
        var context = CreateContext(IPAddress.Parse("172.30.33.7"), IPAddress.Parse("172.30.33.6"));
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;

        Assert.True(AdminAccessPolicy.CanAccess(context));
    }

    [Fact]
    public void AdministrativePaths_SelbsttestDerGrundmenge()
    {
        // Ohne diesen Test liefe die Unterscheidung bei einer leeren Liste still ins Leere.
        Assert.True(AdminAccessPolicy.IsAdministrativePath("/api/system/backup"));
        Assert.False(AdminAccessPolicy.IsAdministrativePath("/api/grows"));
        Assert.True(AdminAccessPolicy.ProtectedRoutePrefixes.Count
            > AdminAccessPolicy.ProtectedProductApiRoutePrefixes.Count);
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
