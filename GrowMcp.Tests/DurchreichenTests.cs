using System.Net;
using System.Text;
using System.Text.Json;
using GrowMcp.Services;
using GrowOsAccess;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace GrowMcp.Tests;

/// <summary>
/// Der Fork-Schlüssel kommt durch den ECHTEN MCP-Weg bis zu Grow OS — und der MCP-Schlüssel nie.
/// </summary>
/// <remarks>
/// <para>Fork AI (A-004, 03.10.2026): Ob ein Werkzeug den Schlüssel der Anfrage
/// sieht, hängt nicht an diesem Programm, sondern am MCP-Paket: ruft es das
/// Werkzeug im Kontext der HTTP-Anfrage auf, oder in einem eigenen? Ein Test,
/// der die Werkzeuge direkt aufruft, kann das nicht zeigen. Deshalb läuft hier
/// der ganze Weg: Tür (<see cref="Aufbau.Wege"/>), MCP-Schnittstelle,
/// Werkzeug, <see cref="GrowOsReader"/> — gegen eine Attrappe von Grow OS, die
/// mitschreibt, was ankommt.</para>
/// </remarks>
public sealed class DurchreichenTests : IAsyncLifetime
{
    private readonly ForkAttrappe _fork = ForkAttrappe.MitGrow();
    private WebApplication? _app;
    private HttpClient _klient = null!;
    private string _mcpSchluessel = null!;

    public async Task InitializeAsync()
    {
        var bau = WebApplication.CreateBuilder();
        bau.WebHost.UseTestServer();
        Aufbau.Dienste(bau.Services, new McpEinstellungen { GrowOsAdresse = "http://grow-os.test:5076" });

        // Grow OS ist die Attrappe — für den Leser und für das Anklopfen der Suche.
        bau.Services.AddHttpClient<GrowOsReader>().ConfigurePrimaryHttpMessageHandler(() => _fork);
        bau.Services.AddHttpClient(GrowOsDiscovery.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _fork);

        _app = bau.Build();

        // Der Testserver kennt keine Ports. Die Tür lässt die Schnittstelle nur
        // am Netz-Port durch — also so tun, als käme alles dort an.
        _app.Use((kontext, weiter) =>
        {
            kontext.Connection.LocalPort = Tueren.NetzPort;
            return weiter(kontext);
        });
        Aufbau.Wege(_app);

        await _app.StartAsync();
        _klient = _app.GetTestClient();
        _mcpSchluessel = _app.Services.GetRequiredService<TokenSpeicher>().Token;
    }

    public async Task DisposeAsync()
    {
        if (_app is not null) await _app.DisposeAsync();
    }

    /// <summary>Ein Werkzeug über die MCP-Schnittstelle aufrufen, wie ein Klient es tut.</summary>
    private async Task<(HttpStatusCode Status, string Text)> AufrufenAsync(string schluessel, string werkzeug, object argumente)
    {
        var rumpf = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new { name = werkzeug, arguments = argumente },
        });

        using var anfrage = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(rumpf, Encoding.UTF8, "application/json"),
        };
        anfrage.Headers.Add("Authorization", $"Bearer {schluessel}");
        anfrage.Headers.Add("Accept", "application/json, text/event-stream");

        using var antwort = await _klient.SendAsync(anfrage);
        var text = await antwort.Content.ReadAsStringAsync();
        return (antwort.StatusCode, Inhalt(text));
    }

    /// <summary>Den Text des Werkzeugs aus der JSON-RPC-Antwort holen — sie kann als SSE kommen.</summary>
    private static string Inhalt(string antwort)
    {
        var json = antwort.Split('\n').FirstOrDefault(z => z.StartsWith("data:", StringComparison.Ordinal)) is { } zeile
            ? zeile["data:".Length..].Trim()
            : antwort;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("result", out var ergebnis)
                && ergebnis.TryGetProperty("content", out var inhalt))
            {
                return string.Join("\n", inhalt.EnumerateArray()
                    .Where(b => b.TryGetProperty("text", out _))
                    .Select(b => b.GetProperty("text").GetString()));
            }
        }
        catch (JsonException)
        {
        }
        return antwort;
    }

    [Fact]
    public async Task Der_Fork_Schluessel_kommt_beim_Eintragen_bei_Grow_OS_an()
    {
        var (status, text) = await AufrufenAsync(Werkzeugkasten.ForkSchluessel, "messung_eintragen",
            new { growId = 1, ph = 5.8, orpMv = 450 });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("Eingetragen als Messung 42", text);

        var post = Assert.Single(_fork.VonWerkzeugen, a => a.Methode == "POST");
        Assert.Equal("api/grows/1/measurements", post.Weg);
        Assert.Equal($"Bearer {Werkzeugkasten.ForkSchluessel}", post.Authorization);

        // Auch das Lesen der Phase davor trug ihn — „bei JEDER Anfrage".
        Assert.All(_fork.VonWerkzeugen, a => Assert.Equal($"Bearer {Werkzeugkasten.ForkSchluessel}", a.Authorization));
    }

    [Fact]
    public async Task Der_MCP_Schluessel_verlaesst_das_Add_on_nie()
    {
        // Lesen geht, und Grow OS sieht dabei KEINEN Schlüssel.
        var (status, _) = await AufrufenAsync(_mcpSchluessel, "grows_auflisten", new { });
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotEmpty(_fork.VonWerkzeugen);
        Assert.All(_fork.Anfragen, a => Assert.Null(a.Authorization));

        // Eintragen geht nicht — und Grow OS wird gar nicht erst gefragt.
        var vorher = _fork.Anfragen.Count;
        var (_, text) = await AufrufenAsync(_mcpSchluessel, "messung_eintragen", new { growId = 1, ph = 5.8 });
        Assert.Contains("Einstellungen → Zugriff für KI-Assistenten", text);
        Assert.Equal(vorher, _fork.Anfragen.Count);
    }

    [Fact]
    public async Task Zwei_Anfragen_nacheinander_tragen_jede_ihren_eigenen_Schluessel()
    {
        // Die Wiederholung unter erschwerten Umständen (CLAUDE.md): erst mit
        // Fork-Schlüssel, dann mit MCP-Schlüssel über denselben Server. Bliebe
        // der Kontext der ersten Anfrage hängen, trüge die zweite ihren Schlüssel.
        await AufrufenAsync(Werkzeugkasten.ForkSchluessel, "grows_auflisten", new { });
        var ersteZahl = _fork.Anfragen.Count;
        Assert.All(_fork.VonWerkzeugen, a => Assert.Equal($"Bearer {Werkzeugkasten.ForkSchluessel}", a.Authorization));

        await AufrufenAsync(_mcpSchluessel, "grows_auflisten", new { });
        var zweite = _fork.Anfragen.Skip(ersteZahl).Where(a => !a.IstAnklopfen).ToList();
        Assert.NotEmpty(zweite);
        Assert.All(zweite, a => Assert.Null(a.Authorization));

        // Und zurück: wieder mit Schlüssel.
        var dritteAb = _fork.Anfragen.Count;
        await AufrufenAsync(Werkzeugkasten.ForkSchluessel, "pumpe_stoppen", new { pumpeId = 3 });
        var dritte = _fork.Anfragen.Skip(dritteAb).Where(a => !a.IstAnklopfen).ToList();
        Assert.Contains(dritte, a => a is { Methode: "POST", Weg: "api/dosing/pumps/3/stop" });
        Assert.All(dritte, a => Assert.Equal($"Bearer {Werkzeugkasten.ForkSchluessel}", a.Authorization));
    }

    [Fact]
    public async Task Ohne_passenden_Schluessel_bleibt_die_Tuer_zu()
    {
        var (status, _) = await AufrufenAsync("falsch", "grows_auflisten", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, status);

        // Ein "gok_" ohne Inhalt hat nicht die Form eines Schlüssels.
        (status, _) = await AufrufenAsync("gok_", "grows_auflisten", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Empty(_fork.Anfragen);
    }
}

/// <summary>Die Quelle des Schlüssels für sich: was aus welchem Kopf herauskommt.</summary>
public sealed class AnfrageSchluesselTests
{
    private static string? Aus(string? kopf)
    {
        var kontext = new DefaultHttpContext();
        if (kopf is not null) kontext.Request.Headers.Authorization = kopf;
        return new AnfrageSchluessel(new HttpContextAccessor { HttpContext = kontext }).Schluessel;
    }

    [Fact]
    public void Ein_Fork_Schluessel_wird_erkannt()
        => Assert.Equal(Werkzeugkasten.ForkSchluessel, Aus($"Bearer {Werkzeugkasten.ForkSchluessel}"));

    [Fact]
    public void Der_MCP_Schluessel_ist_kein_Fork_Schluessel()
        => Assert.Null(Aus($"Bearer {Werkzeugkasten.McpSchluessel}"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("gok_Abc123_-Abc123")]           // ohne „Bearer"
    [InlineData("Basic gok_Abc123_-Abc123")]     // anderes Verfahren
    [InlineData("Bearer gok_kurz")]              // zu kurz für einen echten
    [InlineData("Bearer gok_Abc123 Abc123xyz")]  // Leerzeichen gehören nicht hinein
    public void Alles_andere_ist_kein_Fork_Schluessel(string? kopf)
        => Assert.Null(Aus(kopf));

    [Fact]
    public void Ohne_laufende_Anfrage_gibt_es_keinen_Schluessel()
        => Assert.Null(new AnfrageSchluessel(new HttpContextAccessor()).Schluessel);

    [Fact]
    public void Die_Tuer_laesst_beide_Schluessel_durch_und_sonst_keinen()
    {
        static bool Mcp(string? s) => s == Werkzeugkasten.McpSchluessel;

        Assert.True(Aufbau.SchluesselPasst(Werkzeugkasten.McpSchluessel, Mcp));
        Assert.True(Aufbau.SchluesselPasst(Werkzeugkasten.ForkSchluessel, Mcp));
        Assert.False(Aufbau.SchluesselPasst("falsch", Mcp));
        Assert.False(Aufbau.SchluesselPasst(null, Mcp));
        Assert.False(Aufbau.SchluesselPasst("gok_", Mcp));
    }
}
