using System.Text.Json;
using System.Text.Json.Nodes;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI: Prüft die CO₂-Automationen, die es in Home Assistant schon gibt, und
/// sichert sie auf Freigabe ab — auch von Hand gebaute.
/// </summary>
/// <remarks>
/// <para><b>Warum das ein eigener Weg ist.</b> <see cref="SteuerungAutomationService"/>
/// fasst nur an, was der Fork selbst angelegt hat, und ersetzt es ganz durch die
/// Vorlage. Eine handgebaute Regelung bekäme die Absicherungen aus forkai.153
/// damit nie. Hier wird nichts ersetzt, sondern nur die schwache Stelle
/// umgeschrieben (<see cref="Co2Absicherung"/>), und erst, wenn der Bediener auf
/// der CO₂-Seite zugestimmt hat.</para>
/// <para><b>Welche Automationen.</b> Die des Katalogs (handgebaute Kennungen),
/// die der Fork angelegt hat (<c>fork_ai_co2_…</c>), und jede, die eine
/// Dosierung schaltet — das findet Home Assistant selbst über
/// <c>search/related</c>. So kommt auch eine „Licht-aus-Sicherung" dazu, die
/// in keinem Katalog steht.</para>
/// <para><b>Auch die Rechenwerte.</b> „CO2 Bedarf" und „CO2 Impuls Bedarf"
/// in einer bekannten älteren Fassung werden über
/// <see cref="SteuerungRechenwertAbsicherung"/> ersetzt — von Hand angepasste
/// nur angezeigt.</para>
/// <para><b>Nicht während des Dosierens.</b> Wer eine laufende Automation neu
/// schreibt, bricht sie ab — mitten im Impuls bliebe das Ventil offen, bis der
/// Wächter es schließt. Läuft eine Dosierung oder steht der Port offen, wird
/// nichts geschrieben.</para>
/// </remarks>
public sealed class SteuerungAbsicherungService
{
    private const string ConfigPfad = "api/config/automation/config";
    private const string EigenePraefix = "fork_ai_co2_";

    private readonly HomeAssistantService _ha;
    private readonly SteuerungAutomationService _automationen;
    private readonly ILogger<SteuerungAbsicherungService> _log;

    public SteuerungAbsicherungService(
        HomeAssistantService ha, SteuerungAutomationService automationen, ILogger<SteuerungAbsicherungService> log)
    {
        _ha = ha;
        _automationen = automationen;
        _log = log;
        Rechenwerte = new SteuerungRechenwertAbsicherung(ha, log);
    }

    /// <summary>Die Rechenwerte (Formeln der Template-Helfer) — eigener Weg über den Einstellungsdialog.</summary>
    public SteuerungRechenwertAbsicherung Rechenwerte { get; init; }

    public sealed record Automation(string EntityId, string Name, IReadOnlyList<Co2Absicherung.Befund> Befunde);

    /// <param name="Erreichbar">Hat Home Assistant geantwortet?</param>
    /// <param name="DosiertGerade">Läuft eine Dosierung oder steht der Port offen? Dann wird nicht geschrieben.</param>
    /// <param name="Hinweis">Was nicht geprüft werden konnte, in Klartext.</param>
    /// <param name="Rechenwerte">Die CO₂-Rechenwerte und ob ihre Formel veraltet ist.</param>
    public sealed record Lage(
        bool Erreichbar, bool DosiertGerade, IReadOnlyList<Automation> Automationen, string? Hinweis,
        IReadOnlyList<SteuerungRechenwertAbsicherung.Rechenwert>? Rechenwerte = null)
    {
        public IReadOnlyList<SteuerungRechenwertAbsicherung.Rechenwert> Rechenwerte { get; init; } = Rechenwerte ?? [];

        public int Behebbar => Automationen.Sum(a => a.Befunde.Count(b => b.Behebbar)) + Rechenwerte.Count(r => r.Behebbar);
    }

    public sealed record Ergebnis(string EntityId, string Name, bool Geschrieben, string? Fehler);

    /// <param name="Abgelehnt">Warum gar nichts geschrieben wurde, oder null.</param>
    public sealed record Bilanz(string? Abgelehnt, IReadOnlyList<Ergebnis> Einzeln, Lage Nachher);

    private sealed record Gelesen(string EntityId, string Name, string ConfigId, JsonObject Config);

    private sealed record Stand(Lage Lage, IReadOnlyList<Gelesen> Gelesen, Co2Absicherung.Rahmen Rahmen);

    /// <summary>Nur lesen: was ist schwach, und lässt es sich beheben?</summary>
    public async Task<Lage> PruefenAsync(
        IReadOnlyDictionary<string, string> zuordnung, HomeAssistantSettings settings, CancellationToken ct)
        => (await LesenAsync(zuordnung, settings, ct)).Lage;

    /// <summary>Alle behebbaren Befunde beheben — vorher sichern, nachher nachlesen.</summary>
    public async Task<Bilanz> AbsichernAsync(
        IReadOnlyDictionary<string, string> zuordnung, HomeAssistantSettings settings, CancellationToken ct)
    {
        var vorher = await LesenAsync(zuordnung, settings, ct);
        if (!vorher.Lage.Erreichbar)
        {
            return new Bilanz("Home Assistant antwortet nicht — es wurde nichts geändert.", [], vorher.Lage);
        }
        if (vorher.Lage.DosiertGerade)
        {
            return new Bilanz(
                "Gerade läuft eine Dosierung oder das Ventil steht offen. Eine Automation neu zu schreiben bricht sie ab — "
                + "mitten im Impuls bliebe das Ventil offen. Bitte in einer Pause oder bei Licht aus noch einmal.",
                [], vorher.Lage);
        }

        using var client = _ha.CreateClient(settings);
        var einzeln = new List<Ergebnis>();

        foreach (var a in vorher.Gelesen)
        {
            if (!Co2Absicherung.Pruefen(a.Config, vorher.Rahmen).Any(b => b.Behebbar)) continue;

            // Vor JEDEM Schreiben neu nachsehen: die Dosierung hat einen
            // Auslöser alle zwei Minuten und kann mitten im Absichern anlaufen.
            if (await DosiertGeradeAsync(client, zuordnung, vorher.Rahmen.Dosierungen, settings, ct))
            {
                var nachher0 = await LesenAsync(zuordnung, settings, ct);
                return new Bilanz(
                    "Mitten im Absichern hat eine Dosierung begonnen — angehalten, bevor die nächste Automation neu "
                    + "geschrieben wurde. Schon Abgesichertes bleibt; den Rest bitte in einer Pause noch einmal.",
                    einzeln, nachher0.Lage);
            }

            // Ohne Sicherung kein Schreiben: die Seite verspricht ein Zurück.
            if (!await _automationen.SichernAsync(client, a.ConfigId, ct))
            {
                einzeln.Add(new Ergebnis(a.EntityId, a.Name, false,
                    "Der alte Stand ließ sich nicht sichern — deshalb nicht geändert."));
                continue;
            }

            var neu = Co2Absicherung.Absichern(a.Config, vorher.Rahmen);
            var fehler = await SteuerungAutomationService.SchreibenAsync(client, a.ConfigId, neu, ct);

            // Nachlesen, was Home Assistant jetzt wirklich hat — nicht, was geschickt wurde.
            if (fehler is null)
            {
                var gelesen = await ConfigAsync(client, a.ConfigId, ct);
                if (gelesen is null)
                {
                    fehler = "Geschrieben, aber Home Assistant liefert die Automation nicht zurück.";
                }
                else if (Co2Absicherung.Pruefen(gelesen, vorher.Rahmen).Any(b => b.Behebbar))
                {
                    fehler = "Geschrieben, aber Home Assistant zeigt danach noch die alte Fassung.";
                }
            }

            einzeln.Add(new Ergebnis(a.EntityId, a.Name, fehler is null, fehler));
            if (fehler is null) _log.LogInformation("Automation {EntityId} abgesichert.", a.EntityId);
            else _log.LogWarning("Automation {EntityId} nicht abgesichert: {Fehler}", a.EntityId, fehler);
        }

        var veraltet = vorher.Lage.Rechenwerte.Where(r => r.Behebbar).ToList();
        if (veraltet.Count > 0)
        {
            await using var socket = await HomeAssistantSocket.OeffnenAsync(settings, ct);
            foreach (var r in veraltet)
            {
                if (socket is null)
                {
                    einzeln.Add(new Ergebnis(r.EntityId, r.Name, false, "Home Assistant nimmt die WebSocket-Anmeldung nicht an."));
                    continue;
                }

                // Ein Rechenwert lädt beim Ändern neu und steht kurz auf „nicht
                // verfügbar“ — mitten im Zyklus endet die Schleife dann. Sicher,
                // aber unnötig: auch hier erst nachsehen.
                if (await DosiertGeradeAsync(client, zuordnung, vorher.Rahmen.Dosierungen, settings, ct))
                {
                    var nachher0 = await LesenAsync(zuordnung, settings, ct);
                    return new Bilanz(
                        "Mitten im Absichern hat eine Dosierung begonnen — angehalten, bevor der nächste Rechenwert "
                        + "geändert wurde. Schon Abgesichertes bleibt; den Rest bitte in einer Pause noch einmal.",
                        einzeln, nachher0.Lage);
                }

                var e = await Rechenwerte.ErsetzenAsync(client, socket, settings, r.EntityId, zuordnung, ct);
                einzeln.Add(new Ergebnis(e.EntityId, e.Name, e.Geschrieben, e.Fehler));
            }
        }

        var nachher = await LesenAsync(zuordnung, settings, ct);
        return new Bilanz(null, einzeln, nachher.Lage);
    }

    // ------------------------------------------------------------ Lesen

    private async Task<Stand> LesenAsync(
        IReadOnlyDictionary<string, string> zuordnung, HomeAssistantSettings settings, CancellationToken ct)
    {
        zuordnung.TryGetValue("co2_sensor", out var fuehler);
        var leer = new Co2Absicherung.Rahmen(fuehler, new HashSet<string>(StringComparer.Ordinal));

        if (DemoData.IsEnabled || !settings.IsConfigured)
        {
            return new Stand(new Lage(false, false, [], "Ohne Verbindung zu Home Assistant gibt es nichts zu prüfen."), [], leer);
        }

        using var client = _ha.CreateClient(settings);
        var zustaende = await AutomationenAsync(client, ct);
        if (zustaende is null)
        {
            return new Stand(new Lage(false, false, [], null), [], leer);
        }

        var katalog = SteuerungBauteile.FuerModul("co2")
            .Where(b => b.Art == BauteilArt.Automation)
            .Select(b => b.EntityId)
            .ToHashSet(StringComparer.Ordinal);

        var gelesen = new List<Gelesen>();
        foreach (var z in zustaende.Values.Where(z => katalog.Contains(z.EntityId) || z.ConfigId.StartsWith(EigenePraefix, StringComparison.Ordinal)))
        {
            if (await ConfigAsync(client, z.ConfigId, ct) is { } config)
            {
                gelesen.Add(new Gelesen(z.EntityId, z.Name, z.ConfigId, config));
            }
        }

        var dosierungen = gelesen.Where(g => Co2Absicherung.IstDosierung(g.Config)).Select(g => g.EntityId)
            .ToHashSet(StringComparer.Ordinal);

        // Wer schaltet die Dosierung sonst noch? Das weiss nur Home Assistant.
        await using var socket = await HomeAssistantSocket.OeffnenAsync(settings, ct);
        string? hinweis = null;
        if (dosierungen.Count > 0)
        {
            var verwandte = socket is null ? null : await VerwandteAsync(socket, dosierungen, ct);
            if (verwandte is null)
            {
                hinweis = "Automationen, die die Dosierung nur ein- und ausschalten, konnten nicht gesucht werden.";
            }
            else
            {
                foreach (var entity in verwandte.Where(e => gelesen.All(g => g.EntityId != e)))
                {
                    if (zustaende.TryGetValue(entity, out var z) && await ConfigAsync(client, z.ConfigId, ct) is { } config)
                    {
                        gelesen.Add(new Gelesen(z.EntityId, z.Name, z.ConfigId, config));
                    }
                }
            }
        }

        var rahmen = new Co2Absicherung.Rahmen(fuehler, dosierungen, Ports(zuordnung));
        var laeuft = dosierungen.Any(d => zustaende.TryGetValue(d, out var z) && z.Laeuft)
            || await PortOffenAsync(zuordnung, settings, ct);

        var automationen = gelesen
            .Select(g => new Automation(g.EntityId, g.Name, Co2Absicherung.Pruefen(g.Config, rahmen)))
            .OrderByDescending(a => a.Befunde.Count)
            .ThenBy(a => a.Name, StringComparer.CurrentCulture)
            .ToList();

        var rechenwerte = await Rechenwerte.LesenAsync(client, socket, zuordnung, ct);

        return new Stand(new Lage(true, laeuft, automationen, hinweis, rechenwerte), gelesen, rahmen);
    }

    private static IReadOnlySet<string> Ports(IReadOnlyDictionary<string, string> zuordnung)
        => new[] { "port_schalter", "port_zustand" }
            .Select(r => zuordnung.TryGetValue(r, out var e) ? e : null)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

    private async Task<bool> PortOffenAsync(
        IReadOnlyDictionary<string, string> zuordnung, HomeAssistantSettings settings, CancellationToken ct)
        => zuordnung.TryGetValue("port_zustand", out var port)
           && await _ha.GetEntityStateAsync(settings, port, ct) is { } zustand
           && string.Equals(zustand.State, "on", StringComparison.OrdinalIgnoreCase);

    /// <summary>Läuft eine Dosierung oder steht der Port offen? Antwortet HA nicht, zählt das als ja.</summary>
    private async Task<bool> DosiertGeradeAsync(
        HttpClient client, IReadOnlyDictionary<string, string> zuordnung, IReadOnlySet<string> dosierungen,
        HomeAssistantSettings settings, CancellationToken ct)
    {
        var zustaende = await AutomationenAsync(client, ct);
        if (zustaende is null) return true;
        return dosierungen.Any(d => zustaende.TryGetValue(d, out var z) && z.Laeuft)
            || await PortOffenAsync(zuordnung, settings, ct);
    }

    private sealed record Zustand(string EntityId, string Name, string ConfigId, bool Laeuft);

    /// <summary>Alle Automationen mit ihrer Konfigurations-Kennung. Null, wenn HA nicht antwortet.</summary>
    private static async Task<Dictionary<string, Zustand>?> AutomationenAsync(HttpClient client, CancellationToken ct)
    {
        try
        {
            using var antwort = await client.GetAsync("api/states", ct);
            if (!antwort.IsSuccessStatusCode) return null;

            await using var strom = await antwort.Content.ReadAsStreamAsync(ct);
            using var dokument = await JsonDocument.ParseAsync(strom, cancellationToken: ct);

            var ergebnis = new Dictionary<string, Zustand>(StringComparer.Ordinal);
            foreach (var e in dokument.RootElement.EnumerateArray())
            {
                var entity = e.TryGetProperty("entity_id", out var i) ? i.GetString() : null;
                if (entity is null || !entity.StartsWith("automation.", StringComparison.Ordinal)) continue;
                if (!e.TryGetProperty("attributes", out var attr) || attr.ValueKind != JsonValueKind.Object) continue;

                // Ohne „id" ist sie in YAML geschrieben — dann gibt es keine
                // Konfiguration, die sich über die Oberfläche ändern ließe.
                var id = attr.TryGetProperty("id", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() : null;
                if (string.IsNullOrWhiteSpace(id)) continue;

                var name = attr.TryGetProperty("friendly_name", out var n) && n.ValueKind == JsonValueKind.String
                    ? n.GetString()! : entity;
                var laeuft = attr.TryGetProperty("current", out var c) && c.ValueKind == JsonValueKind.Number && c.GetInt32() > 0;
                ergebnis[entity] = new Zustand(entity, name, id, laeuft);
            }
            return ergebnis;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private static async Task<JsonObject?> ConfigAsync(HttpClient client, string configId, CancellationToken ct)
    {
        try
        {
            using var antwort = await client.GetAsync($"{ConfigPfad}/{Uri.EscapeDataString(configId)}", ct);
            if (!antwort.IsSuccessStatusCode) return null;
            return JsonNode.Parse(await antwort.Content.ReadAsStringAsync(ct)) as JsonObject;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Die Automationen, die eine der Dosierungen erwähnen. Null, wenn die Suche nicht ging.</summary>
    private static async Task<IReadOnlyCollection<string>?> VerwandteAsync(
        HomeAssistantSocket socket, IEnumerable<string> dosierungen, CancellationToken ct)
    {
        var gefunden = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in dosierungen)
        {
            var antwort = await socket.BefehlAsync("search/related",
                new Dictionary<string, object?> { ["item_type"] = "entity", ["item_id"] = d }, ct);
            if (!antwort.Erfolg) return null;

            if (antwort.Ergebnis is { ValueKind: JsonValueKind.Object } e
                && e.TryGetProperty("automation", out var liste) && liste.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in liste.EnumerateArray())
                {
                    if (a.GetString() is { } entity) gefunden.Add(entity);
                }
            }
        }
        return gefunden;
    }
}
