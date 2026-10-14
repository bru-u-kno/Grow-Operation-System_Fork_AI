using System.Text.Json;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>Der erlaubte Bereich eines Zahl-Helfers in Home Assistant — so, wie er dort eingestellt ist.</summary>
public sealed record HelferZahlBereich(string Id, string Name, double Min, double Max, double? Schritt, string? Modus, string? Einheit, string? Symbol);

/// <summary>
/// Fork AI (A-022): Liest den erlaubten Bereich von Zahl-Helfern und weitet ihn, wenn der Fork einen Wert schreiben will, der
/// außerhalb liegt.
/// </summary>
/// <remarks>
/// <para><b>Warum.</b> Ein von Hand angelegter Helfer trägt die Grenzen, die sein Besitzer damals gewählt hat. Im Fall des Nutzers
/// stand „Feuchte EIN Nacht" auf 50 bis 85. Der Fork wollte 48 schreiben, Home Assistant lehnte ab, die Seite meldete nur „nicht alle
/// Helfer übernommen" — und nachts blieb EIN bei 50, also genau über der Plan-Grenze. Die Grenze des Helfers war nie eine Absicht,
/// nur ein Zufall der Anlage.</para>
/// <para><b>Nur weiten, nie verengen.</b> Der Bereich wird auf den des Katalogs erweitert (<see cref="SteuerungBauteile.Alle"/>),
/// oder, wenn der Katalog nichts nennt, gerade so weit, dass der Wert hineinpasst. Name, Schritt, Modus und Einheit bleiben, wie sie
/// sind; der Wert selbst bleibt unberührt.</para>
/// </remarks>
public sealed class HelferBereichService
{
    private readonly ILogger<HelferBereichService> _log;

    public HelferBereichService(ILogger<HelferBereichService> log) => _log = log;

    /// <summary>
    /// Der Bereich, auf den ein Helfer geweitet werden müsste, damit <paramref name="wert"/> hineinpasst; <c>null</c>, wenn er schon passt.
    /// </summary>
    /// <param name="katalogMin">Untergrenze laut Katalog; <c>null</c>, wenn der Katalog keine nennt.</param>
    /// <param name="katalogMax">Obergrenze laut Katalog; <c>null</c>, wenn der Katalog keine nennt.</param>
    public static (double Min, double Max)? Weiten(double min, double max, double wert, double? katalogMin = null, double? katalogMax = null)
    {
        if (!double.IsFinite(wert) || (wert >= min && wert <= max)) return null;
        var neuMin = Math.Min(min, Math.Min(katalogMin ?? wert, wert));
        var neuMax = Math.Max(max, Math.Max(katalogMax ?? wert, wert));
        return (neuMin, neuMax);
    }

    /// <summary>Die Zahl-Helfer aus der Antwort von <c>input_number/list</c>, nach Entitäts-Kennung (<c>input_number.…</c>).</summary>
    public static IReadOnlyDictionary<string, HelferZahlBereich> Lesen(JsonElement liste)
    {
        var ergebnis = new Dictionary<string, HelferZahlBereich>(StringComparer.OrdinalIgnoreCase);
        if (liste.ValueKind != JsonValueKind.Array) return ergebnis;

        foreach (var h in liste.EnumerateArray())
        {
            if (Text(h, "id") is not { Length: > 0 } id) continue;
            if (!h.TryGetProperty("min", out var min) || !min.TryGetDouble(out var mi)) continue;
            if (!h.TryGetProperty("max", out var max) || !max.TryGetDouble(out var ma)) continue;
            double? schritt = h.TryGetProperty("step", out var st) && st.TryGetDouble(out var sd) ? sd : null;
            ergebnis["input_number." + id] = new HelferZahlBereich(id, Text(h, "name") ?? id, mi, ma, schritt, Text(h, "mode"),
                Text(h, "unit_of_measurement"), Text(h, "icon"));
        }

        return ergebnis;
    }

    /// <summary>Alle Zahl-Helfer mit ihrem Bereich; <c>null</c>, wenn Home Assistant am WebSocket nicht antwortet.</summary>
    public async Task<IReadOnlyDictionary<string, HelferZahlBereich>?> LesenAsync(HomeAssistantSettings settings, CancellationToken ct)
    {
        await using var socket = await HomeAssistantSocket.OeffnenAsync(settings, ct);
        if (socket is null) return null;
        var antwort = await socket.BefehlAsync("input_number/list", new Dictionary<string, object?>(), ct);
        if (!antwort.Erfolg || antwort.Ergebnis is not { } e)
        {
            _log.LogWarning("Helfer-Bereiche lesen: {Fehler}", antwort.Fehler);
            return null;
        }

        return Lesen(e);
    }

    /// <summary>
    /// Sorgt dafür, dass jeder Helfer den Wert annimmt, den der Fork schreiben will. Gibt die Entitäten zurück, deren Bereich
    /// geweitet wurde (leer: nichts war nötig oder nichts ließ sich weiten).
    /// </summary>
    public async Task<IReadOnlyList<string>> SichernAsync(
        HomeAssistantSettings settings, IReadOnlyCollection<(string EntityId, double Wert)> soll, CancellationToken ct)
    {
        var gewollt = soll.Where(s => s.EntityId.StartsWith("input_number.", StringComparison.OrdinalIgnoreCase)).ToList();
        if (gewollt.Count == 0) return Array.Empty<string>();

        var vorhanden = await LesenAsync(settings, ct);
        if (vorhanden is null) return Array.Empty<string>();

        var weiten = new List<(HelferZahlBereich Helfer, (double Min, double Max) Neu, string EntityId)>();
        foreach (var (entityId, wert) in gewollt)
        {
            if (!vorhanden.TryGetValue(entityId, out var h)) continue;
            var katalog = SteuerungBauteile.Alle.FirstOrDefault(b => string.Equals(b.EntityId, entityId, StringComparison.OrdinalIgnoreCase));
            if (Weiten(h.Min, h.Max, wert, katalog?.Min, katalog?.Max) is { } neu) weiten.Add((h, neu, entityId));
        }

        if (weiten.Count == 0) return Array.Empty<string>();

        await using var socket = await HomeAssistantSocket.OeffnenAsync(settings, ct);
        if (socket is null) return Array.Empty<string>();

        var geweitet = new List<string>();
        foreach (var (h, neu, entityId) in weiten)
        {
            var felder = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["input_number_id"] = h.Id,
                ["name"] = h.Name,
                ["min"] = neu.Min,
                ["max"] = neu.Max,
            };
            if (h.Schritt is { } schritt) felder["step"] = schritt;
            if (h.Modus is { } modus) felder["mode"] = modus;
            if (!string.IsNullOrWhiteSpace(h.Einheit)) felder["unit_of_measurement"] = h.Einheit;
            if (!string.IsNullOrWhiteSpace(h.Symbol)) felder["icon"] = h.Symbol;

            var antwort = await socket.BefehlAsync("input_number/update", felder, ct);
            if (antwort.Erfolg)
            {
                geweitet.Add(entityId);
                _log.LogInformation("Helfer {EntityId}: Bereich {AltMin}–{AltMax} auf {NeuMin}–{NeuMax} geweitet.",
                    entityId, h.Min, h.Max, neu.Min, neu.Max);
            }
            else
            {
                _log.LogWarning("Helfer {EntityId}: Bereich nicht geweitet: {Fehler}", entityId, antwort.Fehler);
            }
        }

        return geweitet;
    }

    private static string? Text(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
