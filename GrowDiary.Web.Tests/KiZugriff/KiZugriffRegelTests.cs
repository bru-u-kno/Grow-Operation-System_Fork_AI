using System.Net;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Infrastructure.KiZugriff;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace GrowDiary.Web.Tests.KiZugriff;

/// <summary>
/// Fork AI (A-003, 03.10.2026): Wer den Schlüsselweg geht, und was Schritt 2 entscheidet.
/// </summary>
public sealed class KiZugriffRegelTests
{
    private const string Schluessel = "Bearer gok_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private static DefaultHttpContext Anfrage(string ip, string? kopf = Schluessel, bool ingress = false)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        context.Connection.LocalIpAddress = IPAddress.Parse("172.30.33.2");
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/grows";
        if (kopf is not null) context.Request.Headers.Authorization = kopf;
        if (ingress) context.Request.Headers[AdminAccessPolicy.IngressPathHeaderName] = "/api/hassio_ingress/x";
        return context;
    }

    // ------------------------------------------------------ Policy

    [Theory]
    [InlineData("172.30.33.5")]        // ein Nachbar-Add-on
    [InlineData("172.30.32.1")]        // der Gateway liegt im Netz
    [InlineData("::ffff:172.30.33.5")] // IPv4 in IPv6
    [InlineData("fd0c:ac1e:2100::7")]  // IPv6 im Add-on-Netz
    [InlineData("127.0.0.1")]          // Loopback
    [InlineData("::1")]
    public void AusDemAddonNetzUndVonLoopbackGehtDerSchluesselweg(string ip)
        => Assert.True(AdminAccessPolicy.IsKiSchluesselWeg(Anfrage(ip)));

    [Theory]
    [InlineData("192.168.1.50")]   // das Heimnetz
    [InlineData("203.0.113.10")]   // irgendwer
    [InlineData("172.30.34.1")]    // knapp neben dem /23
    [InlineData("10.0.0.5")]
    public void VonAusserhalbWirdEinSchluesselNieGeprueft(string ip)
        => Assert.False(AdminAccessPolicy.IsKiSchluesselWeg(Anfrage(ip)));

    [Fact]
    public void OhneGokSchluesselBleibtEsDerWegVonHeute()
    {
        Assert.False(AdminAccessPolicy.IsKiSchluesselWeg(Anfrage("172.30.33.5", kopf: null)));
        Assert.False(AdminAccessPolicy.IsKiSchluesselWeg(Anfrage("172.30.33.5", kopf: "Bearer eyJhbGciOi")));
    }

    [Fact]
    public void EchterIngressGehtVor()
    {
        Assert.False(AdminAccessPolicy.IsKiSchluesselWeg(Anfrage("172.30.32.2", ingress: true)));
        // Ein gefälschter Ingress-Kopf aus dem Netz macht daraus keinen Ingress — der Schlüssel zählt.
        Assert.True(AdminAccessPolicy.IsKiSchluesselWeg(Anfrage("172.30.33.5", ingress: true)));
    }

    [Fact]
    public void DerLesewegFuerNachbarnBleibtUnveraendert()
    {
        var ohne = Anfrage("172.30.33.5", kopf: null);
        ohne.Request.Method = HttpMethods.Get;
        Assert.True(AdminAccessPolicy.IsInternalAddonRead(ohne));
        Assert.True(AdminAccessPolicy.CanAccess(ohne));

        ohne.Request.Method = HttpMethods.Post;
        Assert.False(AdminAccessPolicy.CanAccess(ohne));
    }

    // ------------------------------------------------------ Schritt 2

    private static KiZugriffKontext Kontext(KiStufe stufen) => new(1, "Test", stufen, new KiHoechstwerteDto(10, 20));

    private static Endpoint Endpunkt(params object[] metadaten)
        => new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse("/api/x"), 0, new EndpointMetadataCollection(metadaten), "Test");

    private const KiStufe Alle = KiStufe.Dokumentieren | KiStufe.GrowPlanen | KiStufe.GeraeteSchalten | KiStufe.Verwaltung;

    [Fact]
    public void LesenAusserhalbDerVerwaltungBrauchtKeineStufe()
    {
        var e = KiZugriffSperre.Entscheiden(Kontext(KiStufe.Keine), Endpunkt(), "GET", "/api/grows");
        Assert.True(e.Durch);
    }

    [Fact]
    public void LesenAufDemVerwaltungswegBrauchtVerwaltung()
    {
        Assert.Equal("ki_stufe_fehlt",
            KiZugriffSperre.Entscheiden(Kontext(KiStufe.Dokumentieren), Endpunkt(), "GET", "/api/settings").Code);
        Assert.True(KiZugriffSperre.Entscheiden(Kontext(KiStufe.Verwaltung), Endpunkt(), "GET", "/api/settings").Durch);
        // KeinKiZugriff gilt auch lesend auf einem Verwaltungsweg.
        Assert.Equal("ki_kein_zugriff",
            KiZugriffSperre.Entscheiden(Kontext(Alle), Endpunkt(new KeinKiZugriffAttribute("x")), "GET", "/api/settings/ki-zugriff").Code);
    }

    [Fact]
    public void SchreibenOhneEinstufungIstGesperrt()
    {
        var e = KiZugriffSperre.Entscheiden(Kontext(Alle), Endpunkt(), "POST", "/api/grows");
        Assert.False(e.Durch);
        Assert.Equal(StatusCodes.Status403Forbidden, e.Status);
        Assert.Equal("ki_nicht_eingestuft", e.Code);

        // „Keine" ist keine Einstufung.
        Assert.Equal("ki_nicht_eingestuft",
            KiZugriffSperre.Entscheiden(Kontext(Alle), Endpunkt(new KiStufeAttribute(KiStufe.Keine)), "POST", "/api/grows").Code);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public void JedeSchreibendeMethodeBrauchtDieStufe(string methode)
    {
        var endpunkt = Endpunkt(new KiStufeAttribute(KiStufe.GrowPlanen));
        Assert.Equal("ki_stufe_fehlt", KiZugriffSperre.Entscheiden(Kontext(KiStufe.Dokumentieren), endpunkt, methode, "/api/grows").Code);
        Assert.True(KiZugriffSperre.Entscheiden(Kontext(KiStufe.GrowPlanen), endpunkt, methode, "/api/grows").Durch);
    }

    [Fact]
    public void DasLetzteAttributGewinnt_AlsoDieAktion()
    {
        // Reihenfolge wie in den Endpunkt-Metadaten: Controller zuerst, Aktion danach.
        var aktionOeffnet = Endpunkt(new KeinKiZugriffAttribute("Controller"), new KiStufeAttribute(KiStufe.Dokumentieren));
        Assert.True(KiZugriffSperre.Entscheiden(Kontext(KiStufe.Dokumentieren), aktionOeffnet, "POST", "/api/grows").Durch);

        var aktionSperrt = Endpunkt(new KiStufeAttribute(KiStufe.Dokumentieren), new KeinKiZugriffAttribute("Aktion"));
        Assert.Equal("ki_kein_zugriff", KiZugriffSperre.Entscheiden(Kontext(Alle), aktionSperrt, "POST", "/api/grows").Code);
    }

    [Fact]
    public void SchreibenAufDemVerwaltungswegBrauchtZusaetzlichVerwaltung()
    {
        var endpunkt = Endpunkt(new KiStufeAttribute(KiStufe.Dokumentieren));
        Assert.Equal("ki_stufe_fehlt", KiZugriffSperre.Entscheiden(Kontext(KiStufe.Dokumentieren), endpunkt, "POST", "/api/settings/x").Code);
        Assert.True(KiZugriffSperre.Entscheiden(Kontext(KiStufe.Dokumentieren | KiStufe.Verwaltung), endpunkt, "POST", "/api/settings/x").Durch);
    }

    [Fact]
    public void SchaltenZaehltUndSicherungWirdVerlangt()
    {
        var schalten = KiZugriffSperre.Entscheiden(Kontext(KiStufe.GeraeteSchalten), Endpunkt(new KiStufeAttribute(KiStufe.GeraeteSchalten)), "POST", "/api/dosing/x");
        Assert.True(schalten.SchaltbefehlZaehlen);
        Assert.False(schalten.SicherungVorher);

        // Mehrere Stufen: zählt, sobald das Bit GeraeteSchalten gesetzt ist.
        var licht = KiZugriffSperre.Entscheiden(Kontext(Alle),
            Endpunkt(new KiStufeAttribute(KiStufe.Verwaltung | KiStufe.GeraeteSchalten)), "PUT", "/api/steuerung/licht");
        Assert.True(licht.SchaltbefehlZaehlen);

        // Ein Sicherheitsbefehl zählt nie.
        var stopp = KiZugriffSperre.Entscheiden(Kontext(KiStufe.GeraeteSchalten),
            Endpunkt(new KiStufeAttribute(KiStufe.GeraeteSchalten), new KiOhneHoechstwertAttribute("Stopp")), "POST", "/api/dosing/pumps/1/stop");
        Assert.True(stopp.Durch);
        Assert.False(stopp.SchaltbefehlZaehlen);

        var doku = KiZugriffSperre.Entscheiden(Kontext(KiStufe.Dokumentieren), Endpunkt(new KiStufeAttribute(KiStufe.Dokumentieren)), "POST", "/api/journal");
        Assert.False(doku.SchaltbefehlZaehlen);

        var sichern = KiZugriffSperre.Entscheiden(Kontext(KiStufe.Verwaltung),
            Endpunkt(new KiStufeAttribute(KiStufe.Verwaltung), new KiSicherungVorherAttribute()), "DELETE", "/api/strains/1");
        Assert.True(sichern.SicherungVorher);
    }

    [Fact]
    public void OhneEndpunktAntwortetDieApp404()
        => Assert.True(KiZugriffSperre.Entscheiden(Kontext(KiStufe.Keine), null, "POST", "/api/gibtsnicht").Durch);
}
