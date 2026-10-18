using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace GrowMcp.Tools;

/// <summary>
/// Fork AI (Paket C1, 18.10.2026): <c>kosten</c> — die Kostenseite eines Grows, stark eingedampft.
/// </summary>
/// <remarks>
/// Eine Wahrheit: alle Zahlen kommen aus <c>GET /api/kosten?growId=</c>, derselben Antwort, aus der die Seite „Kosten"
/// zeichnet. Hier wird nur ausgewählt und gerundet, nichts gerechnet.
/// </remarks>
public sealed partial class GrowTools
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    [McpServerTool(Name = "kosten")]
    [Description("Die Kosten eines Grows kurz: Summe, Strom (kWh, € pro Tag), Artikel, Anschaffungen, pro Tag und Pflanze, Prognose bis zur Ernte; dazu die offenen Gebinde mit Füllstand und wie lange sie reichen, und die Artikel mit Id und Einheit (für verbrauch_buchen und nachfuellung_eintragen). Zahlen wie auf der Seite „Kosten\".")]
    public Task<string> KostenAsync(
        [Description("Die Id des Grows (aus grows_auflisten)")] int growId,
        CancellationToken cancellationToken = default)
        => SicherAsync(async () => KostenBeschreiben(await reader.LesenAsync($"api/kosten?growId={growId}", cancellationToken)));

    /// <summary>Die Antwort von <c>GET /api/kosten</c> in Klartext — öffentlich für die Prüfung.</summary>
    public static string KostenBeschreiben(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var w = doc.RootElement;
        var text = new StringBuilder();

        if (w.TryGetProperty("grow", out var grow) && grow.ValueKind == JsonValueKind.Object)
        {
            text.AppendLine($"Grow {Roh(grow, "id")} „{SchreibWerkzeuge.Text(grow, "name")}\", Tag {Roh(grow, "tag")}, {SchreibWerkzeuge.Text(grow, "phase")}, {Roh(grow, "pflanzen")} Pflanzen.");
        }

        if (w.TryGetProperty("summe", out var s) && s.ValueKind == JsonValueKind.Object)
        {
            text.AppendLine($"Gesamt {Euro(s, "gesamtEur")} = Strom {Euro(s, "stromEur")} + Artikel {Euro(s, "artikelEur")} + Anschaffungen {Euro(s, "anschaffungenEur")}; "
                            + $"pro Tag {Euro(s, "proTagEur")}, pro Pflanze {Euro(s, "proPflanzeEur")}.");
            if (s.TryGetProperty("prognoseErnteEur", out var p) && p.ValueKind == JsonValueKind.Number)
            {
                text.AppendLine($"Prognose bis zur Ernte: {Euro(s, "prognoseErnteEur")} ({SchreibWerkzeuge.Text(s, "prognoseHinweis")}).");
            }
        }

        if (w.TryGetProperty("strom", out var strom) && strom.ValueKind == JsonValueKind.Object)
        {
            text.AppendLine(strom.TryGetProperty("eingerichtet", out var e) && e.ValueKind == JsonValueKind.True
                ? $"Strom: {Zahl(strom, "kwhSeitStart", "0.#")} kWh seit Start, {Zahl(strom, "kwhProTag", "0.#")} kWh/Tag = {Euro(strom, "eurProTag")}/Tag bei {Zahl(strom, "preisCentProKwh", "0.#")} ct/kWh."
                : $"Strom: {SchreibWerkzeuge.Text(strom, "hinweis") ?? "kein Zähler eingerichtet"}.");
        }

        if (w.TryGetProperty("artikel", out var artikel) && artikel.ValueKind == JsonValueKind.Array)
        {
            var offen = new List<string>();
            var liste = new List<string>();
            foreach (var a in artikel.EnumerateArray())
            {
                var einheit = SchreibWerkzeuge.Text(a, "einheit") ?? string.Empty;
                liste.Add($"{Roh(a, "id")} {SchreibWerkzeuge.Text(a, "name")} ({einheit})"
                          + (a.TryGetProperty("aktiv", out var aktiv) && aktiv.ValueKind == JsonValueKind.False ? " — inaktiv" : string.Empty));
                if (!a.TryGetProperty("aktuell", out var g) || g.ValueKind != JsonValueKind.Object) continue;

                var teile = new List<string> { $"{SchreibWerkzeuge.Text(a, "name")}: {Zahl(g, "menge", "0.##")} {einheit} seit {Datum(g, "zeitpunktUtc")}" };
                if (g.TryGetProperty("fuellstandProzent", out var f) && f.ValueKind == JsonValueKind.Number)
                    teile.Add($"Füllstand {Zahl(g, "fuellstandProzent", "0")} % ({SchreibWerkzeuge.Text(g, "fuellstandQuelle")})");
                if (g.TryGetProperty("prognoseLeerAmUtc", out var leer) && leer.ValueKind == JsonValueKind.String)
                    teile.Add($"reicht bis {Datum(g, "prognoseLeerAmUtc")} ({Zahl(g, "prognoseTage", "0")} Tage, {SchreibWerkzeuge.Text(g, "prognoseQuelle")})");
                if (g.TryGetProperty("eurProTag", out var ept) && ept.ValueKind == JsonValueKind.Number)
                    teile.Add($"{Euro(g, "eurProTag")}/Tag");
                offen.Add(string.Join(", ", teile));
            }
            if (offen.Count > 0) text.AppendLine("Offene Gebinde: " + string.Join("; ", offen) + ".");
            if (liste.Count > 0) text.AppendLine("Artikel (Id Name Einheit): " + string.Join("; ", liste) + ".");
        }

        if (w.TryGetProperty("anschaffungen", out var ans) && ans.ValueKind == JsonValueKind.Array && ans.GetArrayLength() > 0)
        {
            text.AppendLine($"Anschaffungen: {ans.GetArrayLength()}, zuletzt " + string.Join("; ", ans.EnumerateArray().Take(5)
                .Select(a => $"{SchreibWerkzeuge.Text(a, "name")} {Euro(a, "gesamtEur")} am {Datum(a, "datumUtc")}")) + ".");
        }

        return text.Length == 0 ? "Grow OS hat keine Kosten geliefert." : text.ToString().TrimEnd();
    }

    private static string Roh(JsonElement e, string name)
        => e.TryGetProperty(name, out var w) && w.ValueKind is JsonValueKind.Number or JsonValueKind.String ? w.ToString() : "?";

    private static string Zahl(JsonElement e, string name, string format)
        => e.TryGetProperty(name, out var w) && w.ValueKind == JsonValueKind.Number ? w.GetDouble().ToString(format, De) : "–";

    private static string Euro(JsonElement e, string name)
        => e.TryGetProperty(name, out var w) && w.ValueKind == JsonValueKind.Number ? w.GetDouble().ToString("0.00", De) + " €" : "–";

    private static string Datum(JsonElement e, string name)
        => e.TryGetProperty(name, out var w) && w.ValueKind == JsonValueKind.String && DateTime.TryParse(w.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d)
            ? d.ToLocalTime().ToString("dd.MM.yyyy", De)
            : "?";
}
