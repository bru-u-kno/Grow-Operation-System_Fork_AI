using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net.Http.Json;

namespace GrowDiary.Web.Tests.Api;

/// <summary>
/// Die echte App, im Test gestartet — mit eigenem Datenordner und Demobestand.
/// </summary>
/// <remarks>
/// <para><b>Warum es das braucht (25.08.2026).</b> Bis hierher gab es im ganzen
/// Projekt <b>keinen</b> Integrations-Aufbau: <c>DeutscheZahlenTests</c> merkt
/// das in seiner eigenen Doku an („es gibt keinen Integrations-Aufbau (kein
/// <c>WebApplicationFactory</c>)"). Alle Backend-Tests rufen Controller direkt
/// auf. Das reicht nicht für die Frage, um die es hier geht: <i>kommt ein Wert,
/// den ein Formular schickt, hinterher auch wieder heraus?</i> — denn dazu
/// gehören Model-Binding, Routen und die ganze Kette dazwischen.</para>
///
/// <para><b>Der Anlass.</b> Das Flipdatum wurde still verworfen. Es war eines
/// von 471 Feldern in 36 schreibenden Verträgen, und <b>zwei</b> Testdateien
/// prüften überhaupt je, ob ein Feld die Runde übersteht. Gefunden hat es der
/// Nutzer, nicht die Mappe.</para>
///
/// <para><b>Eigener Datenordner.</b> <c>GROWDIARY_DATA_PATH</c> zeigt auf ein
/// Wegwerf-Verzeichnis — sonst liefe der Test gegen die Datenbank der
/// Entwicklungs-Installation und würde sie verändern. <c>GROW_OS_DEMO=1</c>
/// sät den vollständigen Bestand, damit es überhaupt etwas zu ändern gibt.</para>
/// </remarks>
public sealed class IntegrationsApp : WebApplicationFactory<Program>
{
    private readonly string _datenordner;

    public IntegrationsApp()
    {
        _datenordner = Path.Combine(Path.GetTempPath(), "GrowOsIntegration_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_datenordner);
    }

    /// <summary>Der Ordner mit der Wegwerf-Datenbank.</summary>
    public string Datenordner => _datenordner;

    /// <summary>
    /// Ein Client, der die App so anspricht, wie Home Assistant es tut.
    /// </summary>
    /// <remarks>
    /// <b>Warum der Kopf noetig ist.</b> <see cref="AdminAccessPolicy"/> laesst
    /// nur Loopback- oder Ingress-Anfragen durch; der Testserver ist weder das
    /// eine noch das andere und bekommt sonst 403. Der Kopf
    /// <c>X-Ingress-Path</c> ist genau der, den das HA-Ingress setzt — hier
    /// wird also nicht der Waechter umgangen, sondern der echte Weg
    /// nachgestellt.
    /// </remarks>
    /// <remarks>
    /// <b>Seit 16.09.2026 auch der Absender.</b> Der Kopf zählt nur noch, wenn
    /// die Anfrage von Home Assistant selbst kommt (Fehlerregister F-011).
    /// Der Testserver hat keine Absenderadresse; <see cref="AbsenderHeader"/>
    /// setzt sie auf <c>172.30.32.1</c> — die Adresse, unter der Ingress-Anfragen
    /// auf Brus Installation ankommen.
    /// </remarks>
    public HttpClient IngressClient() => ClientVon("172.30.32.1", mitIngressKopf: true);

    /// <summary>Nur im Test: welche Absenderadresse der Server sehen soll.</summary>
    public const string AbsenderHeader = "X-Test-Absender";

    /// <summary>Ein Client mit frei gewählter Absenderadresse — für die Gegenproben.</summary>
    public HttpClient ClientVon(string absender, bool mitIngressKopf)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(AbsenderHeader, absender);
        if (mitIngressKopf)
        {
            client.DefaultRequestHeaders.Add(AdminAccessPolicy.IngressPathHeaderName, "/api/hassio_ingress/test");
        }
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(dienste => dienste.AddTransient<IStartupFilter, AbsenderFilter>());
    }

    /// <summary>Setzt die Absenderadresse ganz vorn in der Kette — vor dem Wächter.</summary>
    private sealed class AbsenderFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> weiter) => app =>
        {
            app.Use(async (context, naechster) =>
            {
                if (context.Request.Headers.TryGetValue(AbsenderHeader, out var wert)
                    && System.Net.IPAddress.TryParse(wert.ToString(), out var adresse))
                {
                    context.Connection.RemoteIpAddress = adresse;
                }
                await naechster();
            });
            weiter(app);
        };
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Ueber den INHALTSPFAD, nicht ueber GROWDIARY_DATA_PATH: die Variable
        // gilt fuer den ganzen Prozess, und AppPaths liest sie in JEDEM
        // Konstruktor. Gesetzt hat sie sechs anderen Testklassen den Datenpfad
        // untergeschoben — acht rote Faelle, die nichts mit ihnen zu tun hatten.
        WissenKopieren(Path.Combine(Projektwurzel(), "GrowDiary.Web", "wwwroot", "knowledge-defaults"),
            _datenordner);

        builder.UseContentRoot(_datenordner);
        builder.UseEnvironment("Development");
        var host = base.CreateHost(builder);

        // Den Bestand SELBST saeen statt ueber GROW_OS_DEMO. Grund:
        // DemoData.IsEnabled ist eine statische Eigenschaft, die genau EINMAL
        // beim Laden des Typs gelesen wird. Hat eine andere Testklasse den Typ
        // vorher angefasst, steht dort fuer immer „aus" — in der vollen Mappe
        // war die App deshalb leer, allein gefahren voll. Ein Testaufbau, der
        // von der Reihenfolge abhaengt, prueft nichts Verlaessliches.
        using var bereich = host.Services.CreateScope();
        var grows = bereich.ServiceProvider.GetRequiredService<GrowRepository>();
        if (Demobestand.IstNoetig(grows))
        {
            Demobestand.Anlegen(bereich.ServiceProvider);
        }

        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;

        try { Directory.Delete(_datenordner, recursive: true); } catch { }
    }

    private static string Projektwurzel()
    {
        var ordner = AppContext.BaseDirectory;
        while (ordner != null)
        {
            if (Directory.Exists(Path.Combine(ordner, "GrowDiary.Web"))) return ordner;
            ordner = Path.GetDirectoryName(ordner);
        }

        throw new InvalidOperationException("Projektwurzel nicht gefunden.");
    }

    /// <summary>Die Wissens-Vorlagen in den Wegwerf-Ordner spiegeln.</summary>
    /// <remarks>
    /// Ohne sie startet die App mit leerer Bibliothek — und der Demobestand
    /// legt dann keine Ablaeufe an.
    /// </remarks>
    private static void WissenKopieren(string quelle, string ziel)
    {
        var nach = Path.Combine(ziel, "wwwroot", "knowledge-defaults");
        foreach (var datei in Directory.EnumerateFiles(quelle, "*.json", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(quelle, datei);
            var pfad = Path.Combine(nach, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(pfad)!);
            File.Copy(datei, pfad, overwrite: true);
        }
    }
}

/// <summary>
/// EINE App fuer alle Integrationsfaelle.
/// </summary>
/// <remarks>
/// <b>Warum nicht je Klasse eine.</b> <c>GROWDIARY_DATA_PATH</c> und
/// <c>GROW_OS_DEMO</c> sind Umgebungsvariablen des ganzen Prozesses. Zwei
/// gleichzeitig startende Faelle ueberschreiben sie sich gegenseitig — die
/// zweite App bekam den Ordner der ersten, und in der vollen Mappe stand
/// dann „Kein einziger Grow". Eine gemeinsame Instanz nimmt die Frage weg.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class IntegrationsSammlung : ICollectionFixture<IntegrationsApp>
{
    public const string Name = "Integration";
}

/// <summary>
/// Trägt das Gerüst überhaupt? Ohne diesen Nachweis prüft alles darüber nichts.
/// </summary>
[Collection(IntegrationsSammlung.Name)]
public sealed class IntegrationsAppTests
{
    private readonly IntegrationsApp _app;

    public IntegrationsAppTests(IntegrationsApp app) => _app = app;

    [Fact]
    public async Task DieAppAntwortetUndHatEinenBestand()
    {
        var client = _app.IngressClient();

        var antwort = await client.GetAsync("/api/grows");
        Assert.True(antwort.IsSuccessStatusCode,
            $"GET /api/grows antwortete mit {(int)antwort.StatusCode}.");

        var grows = await antwort.Content.ReadFromJsonAsync<List<Dictionary<string, object?>>>();
        Assert.NotNull(grows);
        Assert.True(grows!.Count > 0,
            "Kein einziger Grow — ohne Bestand prueft der Rundweg nichts. "
            + "Laeuft der Demobestand (GROW_OS_DEMO=1)?");
    }

    [Fact]
    public void DieAppSchreibtNichtInDieEchteDatenbank()
    {
        // Sonst raeumt ein Testlauf die Entwicklungs-Installation um — genau
        // die Klasse Fehler, die E2E-Rundwege hier schon einmal verursacht haben.
        var pfade = new AppPaths(_app.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath);
        Assert.StartsWith(_app.Datenordner, pfade.DatabasePath, StringComparison.Ordinal);
    }
}
