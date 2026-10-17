using GrowDiary.Web.Controllers;
using GrowDiary.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Weiterleitungen der Altpfade tragen das Ingress-Präfix (A-017, Neuling-Test, Lücke 7).
/// </summary>
/// <remarks>
/// <para><b>Der Fehler.</b> <c>Redirect("/zelte")</c> schreibt den Pfad unverändert nach <c>Location</c>. Im Add-on
/// liegt die App unter <c>/api/hassio_ingress/&lt;token&gt;</c>; ein Aufruf von <c>/tents</c> landete deshalb auf
/// <c>/zelte</c> ausserhalb der App.</para>
/// <para><b>Zwei Ebenen.</b> Der Helfer mit seinem Muster (kein Präfix, kein Kontext, Präfix mit Schrägstrich am Ende)
/// und der laufende Host — dort entsteht der Fehler in der Kette Kopf → PathBase → Controller → Location.</para>
/// </remarks>
[Collection(IntegrationsSammlung.Name)]
public sealed class IngressWeiterleitungTests
{
    private readonly IntegrationsApp _app;

    public IngressWeiterleitungTests(IntegrationsApp app) => _app = app;

    private static HttpRequest Anfrage(string? basis)
    {
        var kontext = new DefaultHttpContext();
        if (basis is not null) kontext.Request.PathBase = new PathString(basis);
        return kontext.Request;
    }

    [Theory]
    [InlineData(null, "/zelte", "/zelte")]
    [InlineData("", "/zelte", "/zelte")]
    [InlineData("/api/hassio_ingress/Ab3_x-Y9", "/zelte/4", "/api/hassio_ingress/Ab3_x-Y9/zelte/4")]
    [InlineData("/api/hassio_ingress/Ab3_x-Y9/", "/wissen", "/api/hassio_ingress/Ab3_x-Y9/wissen")]
    public void Helfer_stellt_das_Praefix_voran(string? basis, string pfad, string erwartet)
        => Assert.Equal(erwartet, IngressPfad.Weiterleitung(Anfrage(basis), pfad));

    [Fact]
    public void Helfer_ohne_Anfrage_laesst_den_Pfad_unveraendert()
        => Assert.Equal("/einstellungen", IngressPfad.Weiterleitung(null, "/einstellungen"));

    [Fact]
    public void Controller_hinter_Ingress_leiten_mit_Praefix_um()
    {
        var kontext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        kontext.HttpContext.Request.PathBase = new PathString("/api/hassio_ingress/tok");

        var einstellungen = new SettingsController { ControllerContext = kontext };
        Assert.Equal("/api/hassio_ingress/tok/einstellungen", Assert.IsType<RedirectResult>(einstellungen.Index()).Url);

        var wissen = new KnowledgeController { ControllerContext = kontext };
        Assert.Equal("/api/hassio_ingress/tok/wissen", Assert.IsType<RedirectResult>(wissen.Index()).Url);

        var grows = new GrowsController { ControllerContext = kontext };
        Assert.Equal("/api/hassio_ingress/tok/api/exports/grows/7", Assert.IsType<RedirectResult>(grows.Export(7)).Url);
    }

    private HttpClient OhneFolgen(string? ingressKopf)
    {
        var client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (ingressKopf is not null)
            Assert.True(client.DefaultRequestHeaders.TryAddWithoutValidation(AdminAccessPolicy.IngressPathHeaderName, ingressKopf));
        return client;
    }

    [Theory]
    [InlineData("/settings", "/einstellungen")]
    [InlineData("/knowledge", "/wissen")]
    [InlineData("/tents", "/zelte")]
    [InlineData("/tents/3", "/zelte/3")]
    [InlineData("/grows/5/export", "/api/exports/grows/5")]
    public async Task Am_laufenden_Host_traegt_Location_das_Ingress_Praefix(string altpfad, string ziel)
    {
        const string basis = "/api/hassio_ingress/Ab3_x-Y9";

        var antwort = await OhneFolgen(basis).GetAsync(altpfad);

        Assert.Equal(HttpStatusCode.Redirect, antwort.StatusCode);
        Assert.Equal(basis + ziel, antwort.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("/settings", "/einstellungen")]
    [InlineData("/tents/3", "/zelte/3")]
    public async Task Ohne_Ingress_Kopf_bleibt_Location_an_der_Wurzel(string altpfad, string ziel)
    {
        // Gegenprobe: derselbe Weg ohne Präfix darf nicht anders werden (Aufruf an der Wurzel, kein Add-on).
        // Der Kopf trägt einen Wert, den das Muster verwirft: er setzt keinen PathBase (wie ein Aufruf an der Wurzel).
        var antwort = await OhneFolgen("/irgendwo").GetAsync(altpfad);

        Assert.Equal(HttpStatusCode.Redirect, antwort.StatusCode);
        Assert.Equal(ziel, antwort.Headers.Location?.OriginalString);
    }
}
