using System.Net.Http.Json;
using System.Text.Json;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI (forkai.69): Legt die Rechenwerte einer Steuerung an — die
/// Template-Helfer, die Ziel, Bedarf, Klima-Freigabe und Impulslänge rechnen.
/// </summary>
/// <remarks>
/// <para><b>Warum das nicht wie die anderen Helfer geht.</b> Zahlen und Schalter
/// entstehen über einen einzigen Befehl am WebSocket. Template-Helfer nicht: sie
/// entstehen über denselben Einrichtungsdialog, den ein Mensch im Browser
/// durchklickt, und den gibt es nur als REST. Drei Aufrufe — Dialog öffnen, Art
/// wählen, Felder abschicken.</para>
/// <para><b>Was dabei schiefgehen kann.</b> Der Dialog lebt zwischen den
/// Aufrufen im Speicher von Home Assistant. Bricht einer ab, bleibt ein halb
/// offener Dialog zurück; er verfällt von selbst, aber bis dahin steht er in der
/// Oberfläche herum. Deshalb wird nach einem Fehlschlag nicht weitergemacht,
/// sondern abgebrochen und gemeldet.</para>
/// <para><b>Die Reihenfolge ist nicht beliebig.</b> Die Impulslänge liest das
/// Ziel, der Bedarf liest ebenfalls das Ziel. Entsteht das Ziel zuletzt, stehen
/// die anderen kurz auf „nicht verfügbar" — harmlos, aber verwirrend. Der
/// Katalog nennt sie in der richtigen Folge, und diese Folge bleibt erhalten.</para>
/// </remarks>
public sealed class SteuerungRechenwertService
{
    private const string DialogPfad = "api/config/config_entries/flow";

    private readonly HomeAssistantService _ha;
    private readonly ILogger<SteuerungRechenwertService> _log;

    public SteuerungRechenwertService(HomeAssistantService ha, ILogger<SteuerungRechenwertService> log)
    {
        _ha = ha;
        _log = log;
    }

    public sealed record Ergebnis(string EntityId, string Name, bool Angelegt, string? Fehler);

    public sealed record Bilanz(
        bool Erreichbar,
        int Angelegt,
        int Uebersprungen,
        int Fehlgeschlagen,
        IReadOnlyList<string> OhneGeraet,
        IReadOnlyList<Ergebnis> Einzeln);

    /// <summary>Die fehlenden Rechenwerte anlegen.</summary>
    /// <param name="zuordnung">Rollen-Schlüssel auf Entitäts-Id.</param>
    public async Task<Bilanz> AnlegenAsync(
        string modul,
        IReadOnlyDictionary<string, string> zuordnung,
        HomeAssistantSettings settings,
        CancellationToken ct = default)
    {
        var belegt = zuordnung
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .Select(p => p.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var vorhanden = (await _ha.GetEntitiesAsync(settings, ct))
            .Select(e => e.EntityId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (vorhanden.Count == 0)
        {
            return new Bilanz(false, 0, 0, 0, Array.Empty<string>(), Array.Empty<Ergebnis>());
        }

        var rechenwerte = SteuerungBauteile.Anwendbar(modul, belegt)
            .Where(b => b.Art is BauteilArt.RechenSensor or BauteilArt.RechenSchalter)
            .ToList();

        var uebersprungen = rechenwerte.Count(b => vorhanden.Contains(b.EntityId));
        var offen = rechenwerte.Where(b => !vorhanden.Contains(b.EntityId)).ToList();

        var ohneGeraet = new List<string>();
        var einzeln = new List<Ergebnis>();
        using var client = _ha.CreateClient(settings);

        foreach (var b in offen)
        {
            var vorschrift = b.Vorlage is null ? null : SteuerungBauteile.VorlageFuellen(b.Vorlage, zuordnung);
            if (vorschrift is null)
            {
                // Ein Rechenwert mit stehendem Platzhalter wird nicht ungueltig -
                // er rechnet stumm mit einem Ausweichwert weiter. Lieber nicht.
                ohneGeraet.Add(b.Name);
                continue;
            }

            var (erfolg, fehler) = await AnlegenAsync(client, b, vorschrift, ct);
            einzeln.Add(new Ergebnis(b.EntityId, b.Name, erfolg, fehler));

            if (erfolg)
            {
                _log.LogInformation("Rechenwert angelegt: {EntityId}", b.EntityId);
            }
            else
            {
                _log.LogWarning("Rechenwert {EntityId} nicht angelegt: {Fehler}", b.EntityId, fehler);
                break; // Ein halb offener Dialog bleibt sonst stehen.
            }
        }

        return new Bilanz(
            true,
            einzeln.Count(e => e.Angelegt),
            uebersprungen,
            einzeln.Count(e => !e.Angelegt),
            ohneGeraet,
            einzeln);
    }

    /// <summary>Den dreistufigen Dialog für ein Bauteil durchspielen.</summary>
    private async Task<(bool Erfolg, string? Fehler)> AnlegenAsync(
        HttpClient client, Bauteil b, string vorschrift, CancellationToken ct)
    {
        try
        {
            // 1. Dialog oeffnen. Home Assistant antwortet mit einem Menue der Arten.
            var start = await client.PostAsJsonAsync(
                DialogPfad, new { handler = "template", show_advanced_options = false }, ct);
            if (!start.IsSuccessStatusCode)
            {
                return (false, $"Dialog liess sich nicht oeffnen ({(int)start.StatusCode}).");
            }

            var eroeffnet = await start.Content.ReadFromJsonAsync<JsonElement>(ct);
            var dialog = Text(eroeffnet, "flow_id");
            if (string.IsNullOrWhiteSpace(dialog))
            {
                return (false, "Home Assistant hat keine Dialog-Kennung geliefert.");
            }

            // 2. Art waehlen.
            var schritt = b.Art == BauteilArt.RechenSchalter ? "binary_sensor" : "sensor";
            var gewaehlt = await client.PostAsJsonAsync(
                $"{DialogPfad}/{dialog}", new { next_step_id = schritt }, ct);
            if (!gewaehlt.IsSuccessStatusCode)
            {
                return (false, $"Art '{schritt}' wurde nicht angenommen ({(int)gewaehlt.StatusCode}).");
            }

            // 3. Felder abschicken.
            var abgeschickt = await client.PostAsJsonAsync($"{DialogPfad}/{dialog}", Felder(b, vorschrift), ct);
            var antwort = await abgeschickt.Content.ReadFromJsonAsync<JsonElement>(ct);

            if (!abgeschickt.IsSuccessStatusCode)
            {
                return (false, Text(antwort, "message") ?? $"Abgelehnt ({(int)abgeschickt.StatusCode}).");
            }

            // Bei fehlerhaften Feldern antwortet HA mit 200 und einem erneuten
            // Formular statt mit einem Eintrag - das ist kein Erfolg.
            var typ = Text(antwort, "type");
            if (typ != "create_entry")
            {
                var fehler = antwort.TryGetProperty("errors", out var e) ? e.ToString() : typ;
                return (false, $"Der Dialog hat nichts angelegt: {fehler}");
            }

            return (true, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Die Felder des letzten Dialogschritts.</summary>
    public static IReadOnlyDictionary<string, object?> Felder(Bauteil b, string vorschrift)
    {
        var felder = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = b.Name,
            ["state"] = vorschrift,
        };

        if (b.Art == BauteilArt.RechenSensor)
        {
            if (!string.IsNullOrWhiteSpace(b.Einheit)) felder["unit_of_measurement"] = b.Einheit;
            if (!string.IsNullOrWhiteSpace(b.Zustandsklasse)) felder["state_class"] = b.Zustandsklasse;
        }

        return felder;
    }

    /// <summary>Der Dialogschritt zur Art.</summary>
    public static string Schritt(BauteilArt art) => art switch
    {
        BauteilArt.RechenSchalter => "binary_sensor",
        BauteilArt.RechenSensor => "sensor",
        _ => throw new ArgumentOutOfRangeException(nameof(art), art, "Das ist kein Rechenwert."),
    };

    private static string? Text(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var wert)
           && wert.ValueKind == JsonValueKind.String
            ? wert.GetString()
            : null;
}
