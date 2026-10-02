using GrowDiary.Web.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Der Kopf <c>X-Ingress-Path</c> darf nicht ungeprüft in <c>&lt;base href&gt;</c> landen.
/// </summary>
/// <remarks>
/// <para><b>Der Befund (Sicherheitsprüfung 02.10.2026).</b> Für den Zugriff zählt
/// der Kopf seit dem 01.10.2026 nur noch vom Ingress-Proxy. Für PathBase und das
/// <c>&lt;base href&gt;</c> der Startseite nahm die App ihn aber weiterhin von
/// jedem — und schrieb ihn ungekodiert ins HTML. Ein Wert mit
/// <c>"&gt;&lt;script&gt;</c> schloss das Attribut und stand als Skript im Kopf
/// der Seite.</para>
///
/// <para><b>Warum am laufenden Host und nicht am Helfer.</b> Der Fehler entsteht
/// in der Kette Kopf → PathBase → Fallback → HTML. Ein Test am Helfer allein
/// sähe nicht, wenn der Fallback ihn umginge.</para>
/// </remarks>
[Collection(IntegrationsSammlung.Name)]
public sealed class IngressPfadInDerStartseiteTests
{
    private readonly IntegrationsApp _app;

    public IngressPfadInDerStartseiteTests(IntegrationsApp app) => _app = app;

    /// <summary>Die Startseite im Wegwerf-Ordner — der Testserver hat sonst keine.</summary>
    private void StartseiteBereitstellen()
    {
        var wurzel = _app.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        Directory.CreateDirectory(wurzel);
        var datei = Path.Combine(wurzel, "index.html");
        if (!File.Exists(datei))
        {
            File.WriteAllText(datei, "<!doctype html><html><head><title>Grow OS</title></head><body></body></html>");
        }
    }

    private async Task<string> StartseiteMitKopf(string kopf)
    {
        StartseiteBereitstellen();
        var client = _app.CreateClient();
        using var anfrage = new HttpRequestMessage(HttpMethod.Get, "/grows");
        Assert.True(
            anfrage.Headers.TryAddWithoutValidation(AdminAccessPolicy.IngressPathHeaderName, kopf),
            "Der Testkopf liess sich nicht setzen — dann prueft der Fall nichts.");
        var antwort = await client.SendAsync(anfrage);
        Assert.True(antwort.IsSuccessStatusCode, $"GET /grows antwortete mit {(int)antwort.StatusCode}.");
        return await antwort.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task EinSkriptImKopfLandetNichtInDerSeite()
    {
        var html = await StartseiteMitKopf("/api/hassio_ingress/abc\"><script>alert(1)</script>");

        Assert.DoesNotContain("<script>alert(1)</script>", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"><script", html, StringComparison.OrdinalIgnoreCase);
        // Ein ungültiger Kopf setzt keinen PathBase: die Seite läuft an der Wurzel.
        Assert.Contains("<base href=\"/\" />", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EinEchterIngressPfadKommtAlsBasisAn()
    {
        // Gegenprobe: der echte Weg darf durch das Muster nicht kaputtgehen.
        var html = await StartseiteMitKopf("/api/hassio_ingress/Ab3_x-Y9");

        Assert.Contains("<base href=\"/api/hassio_ingress/Ab3_x-Y9/\" />", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/hassio_ingress/abc/../../evil")]
    [InlineData("//evil.example/x")]
    [InlineData("/irgendwo")]
    [InlineData("/api/hassio_ingress/")]
    [InlineData("/api/hassio_ingress/a b")]
    public async Task EinFremderPfadWirdNichtZurBasis(string kopf)
    {
        var html = await StartseiteMitKopf(kopf);

        Assert.Contains("<base href=\"/\" />", html, StringComparison.Ordinal);
    }
}
