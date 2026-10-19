using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace GrowMcp.Tools;

/// <summary>
/// Fork AI (Paket C1, 18.10.2026): <c>steuerung</c> — der Zustand der Regelungen in Klartext.
/// </summary>
/// <remarks>
/// Die Übersicht ist die der Seite „Steuerungen" (<c>GET /api/steuerung</c>), die Zeilen dort rechnet Cultivate Control selbst
/// — hier wird nichts nachgerechnet, nur vorgelesen. Ein Modul liefert Einstellungen, Live-Werte und Abweichungen
/// (<c>GET /api/steuerung/{modul}</c>), dazu ein laufender Probelauf.
/// </remarks>
public sealed partial class GrowTools
{
    /// <summary>Die Module mit eigener Seite — Kennungen wie in <c>SteuerungApiController</c>.</summary>
    public static readonly (string Kennung, string[] Woerter)[] SteuerungsModule =
    [
        ("co2", ["co2", "co₂", "begasung"]),
        ("zuluft", ["zuluft", "luftzufuhr"]),
        ("entfeuchter", ["entfeuchter", "entfeuchtung"]),
        ("entfeuchter-zusatz", ["entfeuchter-zusatz", "zusatz", "zusatz-entfeuchter", "zusatzentfeuchter"]),
        ("chiller", ["chiller", "kuehler", "kühler", "wasserkuehler", "wasserkühler"]),
        ("licht", ["licht", "lampe"]),
    ];

    private const int ModulTextHoechstens = 6_000;

    [McpServerTool(Name = "steuerung")]
    [Description("Der Zustand aller Regelungen von Cultivate Control in Klartext (CO₂, Entfeuchter, Zusatz-Entfeuchter, Wasserkühler, Abluft, Zuluft, Licht) — wie die Seite „Steuerungen\". Mit modul (co2, zuluft, entfeuchter, entfeuchter-zusatz, chiller, licht) kommen Einstellungen, Live-Werte und Abweichungen dieses Moduls dazu. Ein laufender Probelauf steht immer dabei. Für „warum läuft der Entfeuchter gerade nicht?\".")]
    public Task<string> SteuerungAsync(
        [Description("Wahlweise ein Modul: co2, zuluft, entfeuchter, entfeuchter-zusatz, chiller oder licht")] string? modul = null,
        CancellationToken cancellationToken = default)
        => SicherAsync(async () =>
        {
            string? kennung = null;
            if (!string.IsNullOrWhiteSpace(modul))
            {
                kennung = SchreibWerkzeuge.Auswahl(SteuerungsModule.Select(m => (m.Kennung, m.Woerter)).ToList(), modul);
                if (kennung is null)
                {
                    return $"„{modul}\" ist kein Modul. Möglich: {string.Join(", ", SteuerungsModule.Select(m => m.Kennung))}.";
                }
            }

            var uebersicht = await reader.LesenAsync("api/steuerung", cancellationToken);
            var probelaeufe = await reader.LesenAsync("api/steuerung/probelauf", cancellationToken);
            var detail = kennung is null ? null : await reader.LesenAsync($"api/steuerung/{kennung}", cancellationToken);
            return SteuerungBeschreiben(uebersicht, probelaeufe, kennung, detail);
        });

    /// <summary>Übersicht, Probeläufe und wahlweise ein Modul in Klartext — öffentlich für die Prüfung.</summary>
    public static string SteuerungBeschreiben(string uebersicht, string probelaeufe, string? kennung, string? detail)
    {
        using var u = JsonDocument.Parse(uebersicht);
        var text = new StringBuilder();
        var wurzel = u.RootElement;

        if (wurzel.TryGetProperty("haErreichbar", out var ha) && ha.ValueKind == JsonValueKind.False)
        {
            text.AppendLine("ACHTUNG: Home Assistant ist für Cultivate Control gerade nicht erreichbar — die Werte unten können veraltet sein.");
        }

        if (wurzel.TryGetProperty("module", out var module) && module.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in module.EnumerateArray())
            {
                var zustand = m.TryGetProperty("zustand", out var z) && z.ValueKind == JsonValueKind.Object
                    ? $" ({SchreibWerkzeuge.Text(z, "wort")}: {SchreibWerkzeuge.Text(z, "satz")})"
                    : string.Empty;
                text.AppendLine($"{SchreibWerkzeuge.Text(m, "titel")} [{SchreibWerkzeuge.Text(m, "kennung")}]: {StatusWort(SchreibWerkzeuge.Text(m, "status"))}{zustand} · "
                                + $"{SchreibWerkzeuge.Text(m, "kurz")} · jetzt {SchreibWerkzeuge.Text(m, "wert")} · {SchreibWerkzeuge.Text(m, "unterzeile")}");
            }
        }

        if (wurzel.TryGetProperty("nichtEingerichtet", out var fehlt) && fehlt.ValueKind == JsonValueKind.Array && fehlt.GetArrayLength() > 0)
        {
            text.AppendLine("Nicht eingerichtet: " + string.Join(", ", fehlt.EnumerateArray().Select(f => SchreibWerkzeuge.Text(f, "titel"))) + ".");
        }

        using var p = JsonDocument.Parse(probelaeufe);
        var laufend = p.RootElement.ValueKind == JsonValueKind.Array
            ? p.RootElement.EnumerateArray().Where(l => SchreibWerkzeuge.Text(l, "status") is "Laeuft").ToList()
            : [];
        text.AppendLine(laufend.Count == 0
            ? "Probelauf: keiner läuft."
            : "Probelauf läuft: " + string.Join("; ", laufend.Select(l =>
                $"Id {l.GetProperty("id").GetRawText()} am Modul {SchreibWerkzeuge.Text(l, "modulTitel")}, noch "
                + $"{(l.TryGetProperty("restSekunden", out var r) && r.ValueKind == JsonValueKind.Number ? Math.Ceiling(r.GetInt32() / 60.0) : 0)} min"))
              + " — abbrechen mit probelauf_abbrechen.");

        if (kennung is not null && detail is not null)
        {
            text.AppendLine();
            text.AppendLine($"Modul {kennung}:");
            using var d = JsonDocument.Parse(detail);
            foreach (var teil in new[] { "live", "einstellungen", "abweichungen" })
            {
                if (!d.RootElement.TryGetProperty(teil, out var wert)) continue;
                text.AppendLine($"{char.ToUpperInvariant(teil[0])}{teil[1..]}: {Kurz(wert)}");
            }
        }

        return Funktionsverzeichnis.Kuerzen(text.ToString().TrimEnd(), ModulTextHoechstens, "Mit fork_ausfuehren GET api/steuerung/<modul> gibt es alles.");
    }

    private static string StatusWort(string? status) => status switch
    {
        "an" => "an",
        "aus" => "aus",
        "warn" => "WARNUNG",
        _ => status ?? "?",
    };

    /// <summary>Ein JSON-Wert in einer Zeile: Name Wert; verschachtelt in Klammern; Zahlen mit Komma.</summary>
    internal static string Kurz(JsonElement wert, int tiefe = 0)
    {
        switch (wert.ValueKind)
        {
            case JsonValueKind.Object:
                var felder = string.Join("; ", wert.EnumerateObject().Select(p => $"{p.Name} {Kurz(p.Value, tiefe + 1)}"));
                return tiefe == 0 ? felder : $"({felder})";
            case JsonValueKind.Array:
                return wert.GetArrayLength() == 0 ? "keine" : "[" + string.Join(" | ", wert.EnumerateArray().Select(e => Kurz(e, tiefe + 1))) + "]";
            case JsonValueKind.Number:
                return wert.GetDouble().ToString("0.###", CultureInfo.GetCultureInfo("de-DE"));
            case JsonValueKind.True:
                return "ja";
            case JsonValueKind.False:
                return "nein";
            case JsonValueKind.Null:
                return "leer";
            default:
                return wert.GetString() ?? string.Empty;
        }
    }
}
