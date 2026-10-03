using GrowMcp;
using GrowMcp.Services;

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
//
// Zwei Schluessel (A-004, 03.10.2026): der MCP-Schluessel dieses Add-ons liest
// nur. Ein Schluessel aus Grow OS (gok_…) wird bei jeder Anfrage an Grow OS
// durchgereicht; dort entscheiden Stufen und Hoechstwerte, ob eingetragen oder
// geschaltet werden darf. Dienste und Tuer stehen in Aufbau.cs.

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

Aufbau.Dienste(builder.Services, einstellungen);

var app = builder.Build();
Aufbau.Wege(app);
app.Run();
