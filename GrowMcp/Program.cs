using System.Net;
using GrowMcp;
using GrowMcp.Services;
using GrowMcp.Tools;
using GrowOsAccess;

// Grow MCP Fork AI: Grow OS Fork AI als Werkzeugkasten fuer einen beliebigen MCP-Klienten —
// Claude Code, Claude Desktop, was auch immer im eigenen Netz laeuft.
//
// Der Unterschied zur Berater-Mappe, die Grow OS zum Herunterladen anbietet: die
// ist EIN fertiger Stapel Papier mit dem Stand von jetzt. Hier bekommt das Modell
// Griffe und fragt gezielt nach — der einzige Weg zu Verlaufsfragen, denn ein
// Momentwert zeigt keine Bewegung.
//
// Zwei Tueren, mit Absicht getrennt:
//   Port 5078  Ingress. Die Seite, auf der der Schluessel steht. Home Assistant
//              besitzt hier die Anmeldung, nach draussen ist der Port zu. Im
//              internen Add-on-Netz ist er offen — deshalb antwortet die Seite nur
//              dem Ingress-Proxy 172.30.32.2 und Loopback (Tueren.Pruefen).
//   Port 5080  Das WLAN. Nur die MCP-Schnittstelle, nur mit Schluessel. Wer hier
//              anklopft, sieht die Seite mit dem Schluessel NICHT — sonst haette
//              das Absichern keinen Sinn.

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(kestrel =>
{
    // Beide Ports fest im Programm statt ueber ASPNETCORE_URLS: welcher Port was
    // darf, ist hier eine Sicherheitsfrage und keine Umgebungsvariable.
    kestrel.ListenAnyIP(Tueren.IngressPort);
    kestrel.ListenAnyIP(Tueren.NetzPort);
});

var einstellungen = McpEinstellungen.Laden(
    LoggerFactory.Create(bau => bau.AddConsole()).CreateLogger<McpEinstellungen>());

builder.Services.AddSingleton(einstellungen);
builder.Services.AddSingleton<TokenSpeicher>();
// Der eigene Slug steht in grow-mcp/config.yaml; aus ihm folgt der Name von
// Grow OS im internen Netz. Stand hier einmal der Slug eines anderen Add-ons,
// fand dieses hier ein Grow OS aus dem Store gar nicht — genau so ist es beim
// ersten Einsatz gescheitert.
builder.Services.AddGrowOsAccess(einstellungen.GrowOsAdresse, eigenerSlug: "grow_mcp");

builder.Services.AddMcpServer()
    // Zustandslos: dieser Server haelt nichts zwischen zwei Aufrufen fest, also
    // braucht er auch keine Sitzung. Ein Neustart kostet damit nichts.
    .WithHttpTransport(transport => transport.Stateless = true)
    .WithTools<GrowTools>();

var app = builder.Build();

app.Use(async (kontext, weiter) =>
{
    var speicher = kontext.RequestServices.GetRequiredService<TokenSpeicher>();
    var zutritt = Tueren.Pruefen(
        kontext.Connection.LocalPort,
        kontext.Request.Path.Value ?? "/",
        speicher.Stimmt(Mitgeschickt(kontext.Request)),
        kontext.Connection.RemoteIpAddress);

    switch (zutritt)
    {
        case Zutritt.NichtGefunden:
            kontext.Response.StatusCode = (int)HttpStatusCode.NotFound;
            return;

        case Zutritt.SchluesselFehlt:
            kontext.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            kontext.Response.Headers.WWWAuthenticate = "Bearer";
            await kontext.Response.WriteAsync("Kein oder falscher Zugriffsschluessel.");
            return;

        case Zutritt.Verboten:
            // Die Einrichtungsseite zeigt den Schluessel. Ein anderes Add-on im
            // internen Netz soll ihn nicht abholen koennen — siehe Tueren.IngressProxy.
            kontext.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            await kontext.Response.WriteAsync("Nur ueber Home Assistant (Ingress) erreichbar.");
            return;

        case Zutritt.Erlaubt:
            await weiter();
            return;

        default:
            // Ein neuer Fall, den hier niemand behandelt, sperrt — er oeffnet nicht.
            kontext.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            return;
    }
});

app.MapMcp(Tueren.McpPfad);

app.MapGet("/", (HttpRequest anfrage, TokenSpeicher speicher, GrowOsDiscovery suche, CancellationToken ct)
    => Einrichtungsseite.RendernAsync(anfrage, speicher, suche, ct));

app.Run();

/// <summary>Den Schlüssel aus dem Kopf der Anfrage holen.</summary>
static string? Mitgeschickt(HttpRequest anfrage)
{
    var kopf = anfrage.Headers.Authorization.ToString();
    return kopf.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? kopf["Bearer ".Length..].Trim()
        : null;
}
