using System.Net;
using GrowMcp.Services;
using GrowMcp.Tools;
using GrowOsAccess;

namespace GrowMcp;

/// <summary>
/// Dienste und Weg einer Anfrage — an einer Stelle, damit der Test denselben Aufbau fährt.
/// </summary>
/// <remarks>
/// Fork AI (A-004, 03.10.2026): Stand bis dahin direkt in <c>Program.cs</c>. Der
/// Beleg, dass ein Fork-Schlüssel durch den ECHTEN MCP-Weg bis zu Grow OS
/// kommt (<c>DurchreichenTests</c>), braucht dieselbe Tür und dieselben
/// Werkzeuge wie der Betrieb. Ein nachgebauter Aufbau im Test belegte nur sich
/// selbst.
/// </remarks>
public static class Aufbau
{
    /// <summary>Alle Dienste des Grow MCP eintragen.</summary>
    public static void Dienste(IServiceCollection dienste, McpEinstellungen einstellungen)
    {
        dienste.AddSingleton(einstellungen);
        dienste.AddSingleton<TokenSpeicher>();

        // Der Fork-Schlüssel kommt aus der laufenden HTTP-Anfrage. Vor
        // AddGrowOsAccess, das sonst „kein Schlüssel" einträgt.
        dienste.AddHttpContextAccessor();
        dienste.AddSingleton<IForkSchluesselQuelle, AnfrageSchluessel>();

        // Der eigene Slug steht in grow-mcp/config.yaml; aus ihm folgt der Name von
        // Grow OS im internen Netz. Stand hier einmal der Slug eines anderen Add-ons,
        // fand dieses hier ein Grow OS aus dem Store gar nicht — genau so ist es beim
        // ersten Einsatz gescheitert.
        dienste.AddGrowOsAccess(einstellungen.GrowOsAdresse, eigenerSlug: "grow_mcp");

        dienste.AddMcpServer()
            // Zustandslos: dieser Server haelt nichts zwischen zwei Aufrufen fest,
            // also braucht er auch keine Sitzung. Ein Neustart kostet damit nichts.
            // Und: nur so laeuft jedes Werkzeug im Kontext GENAU der HTTP-Anfrage,
            // die es ausgeloest hat — daran haengt AnfrageSchluessel.
            .WithHttpTransport(transport => transport.Stateless = true)
            .WithTools<GrowTools>()
            .WithTools<SchreibWerkzeuge>()
            .WithTools<HaWerkzeuge>();
    }

    /// <summary>Tür und Wege.</summary>
    public static void Wege(WebApplication app)
    {
        app.Use(async (kontext, weiter) =>
        {
            var speicher = kontext.RequestServices.GetRequiredService<TokenSpeicher>();
            var mitgeschickt = Mitgeschickt(kontext.Request);
            var zutritt = Tueren.Pruefen(
                kontext.Connection.LocalPort,
                kontext.Request.Path.Value ?? "/",
                SchluesselPasst(mitgeschickt, speicher.Stimmt),
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
    }

    /// <summary>
    /// Öffnet der mitgeschickte Schlüssel die Tür zur Schnittstelle?
    /// </summary>
    /// <remarks>
    /// <para>Zwei Schlüssel passen. Der MCP-Schlüssel dieses Add-ons (lesen) — er
    /// wird hier geprüft. Und ein Schlüssel aus Grow OS (<c>gok_…</c>) — von dem
    /// prüft dieses Add-on nur die Form, denn gültig ist er nur, wenn Grow OS ihn
    /// kennt. Ein erfundener <c>gok_</c> kommt also durch die Tür, aber an nichts
    /// heran: Grow OS lehnt jede Anfrage damit ab, auch die lesenden, und zählt
    /// den Fehlversuch.</para>
    /// </remarks>
    public static bool SchluesselPasst(string? mitgeschickt, Func<string?, bool> mcpSchluesselStimmt)
        => mcpSchluesselStimmt(mitgeschickt) || ForkSchluessel.HatForm(mitgeschickt);

    /// <summary>Den Schlüssel aus dem Kopf der Anfrage holen.</summary>
    public static string? Mitgeschickt(HttpRequest anfrage)
    {
        var kopf = anfrage.Headers.Authorization.ToString();
        return kopf.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? kopf["Bearer ".Length..].Trim()
            : null;
    }
}
