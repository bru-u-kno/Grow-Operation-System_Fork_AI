using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrowOsAccess;
using ModelContextProtocol.Server;

namespace GrowMcp.Tools;

/// <summary>
/// Home Assistant lesen und schalten — über Grow OS, mit einem Fork-Schlüssel.
/// </summary>
/// <remarks>
/// <para>Fork AI (A-004, 03.10.2026): Der Grow MCP spricht nie selbst mit Home
/// Assistant. Grow OS hat den Zugang dorthin schon (es schaltet Licht und
/// Pumpen darüber) und bietet ihn Assistenten unter <c>/api/ki-ha/…</c> an —
/// nur mit Schlüssel, und mit den Stufen, die der Betreiber vergeben hat. Ein
/// zweiter Weg zu Home Assistant hier hätte eigene Rechte gebraucht und hätte
/// die Stufen von Grow OS umgangen.</para>
///
/// <para><b>Vertrag</b> (baut Grow OS, Stand 03.10.2026):
/// <c>GET bereiche</c> → <c>[{ id, name }]</c>;
/// <c>GET zustaende?bereich=&amp;domain=&amp;suche=&amp;anzahl=</c> →
/// <c>[{ entityId, name, zustand, einheit, bereich, geaendertAmUtc }]</c>;
/// <c>GET verlauf?entityId=&amp;stunden=</c> → <c>{ entityId, punkte: [{ zeitUtc, zustand }] }</c>;
/// <c>POST dienst</c> mit <c>{ domain, dienst, entityId?, daten? }</c> →
/// <c>{ erfolg, meldung }</c>. Ohne Schlüssel 401 <c>ki_schluessel_fehlt</c>.</para>
///
/// <para><b>Wie Grow OS ihn gebaut hat</b> (Branch <c>ki-ha-schnittstelle</c>,
/// <c>KiHomeAssistantApiController</c>, <c>KiHaEinstufung</c>): <c>anzahl</c>
/// ausserhalb 1–500 und <c>stunden</c> ausserhalb 1–168 geben 400 statt still zu
/// kappen — deshalb begrenzen die Werkzeuge vorher. <c>domain</c> und
/// <c>dienst</c> nur aus <c>[a-z0-9_]</c>, <c>entityId</c> genau eine Entität mit
/// dem Präfix der Domain, in <c>daten</c> keine Zielfelder. Dieselben Regeln
/// stehen hier als Vorprüfung, damit der Assistent den Grund sofort und auf
/// Deutsch bekommt; entscheidend bleibt die Prüfung in Grow OS. Ein Dienst, den
/// Home Assistant nicht bestätigt, kommt als 200 mit <c>erfolg: false</c> — das
/// ist ein Misserfolg und wird so gemeldet. Neu sind 503
/// <c>ha_nicht_eingerichtet</c> und 502 <c>ha_nicht_erreichbar</c>.</para>
/// </remarks>
[McpServerToolType]
public sealed class HaWerkzeuge(GrowOsReader reader)
{
    private const string Basis = "api/ki-ha";

    private static readonly JsonSerializerOptions Ausgabe = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [McpServerTool(Name = "ha_bereiche")]
    [Description("Die Bereiche (Räume) in Home Assistant mit Id und Name — zum Eingrenzen von ha_zustaende. Lesend; braucht einen Schlüssel aus Grow OS, aber keine Stufe.")]
    [BrauchtForkSchluessel]
    public Task<string> BereicheAsync(CancellationToken cancellationToken = default)
        => AnfragenAsync(HttpMethod.Get, $"{Basis}/bereiche", null, "Bereiche lesen", cancellationToken);

    [McpServerTool(Name = "ha_zustaende")]
    [Description("Der aktuelle Zustand von Geräten und Sensoren in Home Assistant: Entity-Id, Name, Zustand, Einheit, Bereich, letzte Änderung. Mit Bereich, Domain (light, switch, sensor …) oder Suchwort eingrenzen. Lesend; braucht einen Schlüssel aus Grow OS, aber keine Stufe.")]
    [BrauchtForkSchluessel]
    public Task<string> ZustaendeAsync(
        [Description("Id eines Bereichs aus ha_bereiche")] string? bereich = null,
        [Description("Domain, etwa light, switch, sensor, climate")] string? domain = null,
        [Description("Suchwort in Name oder Entity-Id")] string? suche = null,
        [Description("Höchstens so viele Einträge, 1 bis 500")] int anzahl = 50,
        CancellationToken cancellationToken = default)
    {
        var teile = new List<string>();
        if (!string.IsNullOrWhiteSpace(bereich)) teile.Add($"bereich={Uri.EscapeDataString(bereich.Trim())}");
        if (!string.IsNullOrWhiteSpace(domain)) teile.Add($"domain={Uri.EscapeDataString(domain.Trim())}");
        if (!string.IsNullOrWhiteSpace(suche)) teile.Add($"suche={Uri.EscapeDataString(suche.Trim())}");
        teile.Add($"anzahl={Math.Clamp(anzahl, 1, 500)}");

        return AnfragenAsync(HttpMethod.Get, $"{Basis}/zustaende?{string.Join('&', teile)}", null, "Zustände lesen", cancellationToken);
    }

    [McpServerTool(Name = "ha_verlauf")]
    [Description("Der Verlauf EINER Entity in Home Assistant über die letzten Stunden (1 bis 168): Zeitpunkte (UTC) mit Zustand. Lesend; braucht einen Schlüssel aus Grow OS, aber keine Stufe.")]
    [BrauchtForkSchluessel]
    public Task<string> VerlaufAsync(
        [Description("Genau eine Entity-Id, etwa sensor.zelt_temperatur")][Beispiel("sensor.zelt_temperatur")] string entityId,
        [Description("Wie viele Stunden zurück, 1 bis 168")] int stunden = 24,
        CancellationToken cancellationToken = default)
    {
        var id = entityId.Trim();
        if (!EineEntitaet.IsMatch(id))
        {
            return Task.FromResult($"Nichts gelesen: „{entityId}\" ist nicht genau eine Entity-Id. Erwartet wird etwa sensor.zelt_temperatur — eine, ohne Komma.");
        }

        return AnfragenAsync(HttpMethod.Get,
            $"{Basis}/verlauf?entityId={Uri.EscapeDataString(id)}&stunden={Math.Clamp(stunden, 1, 168)}",
            null, "Verlauf lesen", cancellationToken);
    }

    /// <summary>Genau eine Entität: <c>domain.objekt</c>, beides aus <c>[a-z0-9_]</c>.</summary>
    private static readonly System.Text.RegularExpressions.Regex EineEntitaet = new(@"^[a-z0-9_]+\.[a-z0-9_]+$");

    /// <summary>Name einer Domain oder eines Dienstes — so streng wie Grow OS.</summary>
    private static readonly System.Text.RegularExpressions.Regex Bezeichner = new(@"^[a-z0-9_]+$");

    /// <summary>
    /// Zielfelder, die in <c>daten</c> nichts zu suchen haben.
    /// </summary>
    /// <remarks>
    /// Steht in Grow OS am Vertrag (<c>KiHaDienstRequest.Daten</c>): das Ziel geht
    /// NUR über <c>entityId</c>. Sonst könnte <c>light.turn_on</c> mit
    /// <c>{"area_id": "haus"}</c> jede Lampe im Haus schalten — an der Prüfung
    /// der Entität vorbei.
    /// </remarks>
    public static IReadOnlyList<string> VerboteneDaten { get; } =
        ["entity_id", "device_id", "area_id", "floor_id", "label_id", "target"];

    [McpServerTool(Name = "ha_dienst")]
    [Description($"Ruft einen Dienst in Home Assistant auf, etwa domain light, dienst turn_on, entityId light.zelt, daten {{\"brightness_pct\": 60}}. Braucht Stufe „{Stufen.GeraeteSchaltenText}\". Automationen, Skripte, Szenen und Helfer (input_*, timer, counter, schedule) brauchen zusätzlich Stufe „{Stufen.VerwaltungText}\". Manche Domains gehen über einen Schlüssel nie, egal mit welcher Stufe — etwa homeassistant, hassio, backup, recorder, shell_command, notify, lock, alarm_control_panel, update, mqtt und downloader, dazu jeder reload-Dienst; Grow OS lehnt sie mit Begründung ab. Die Entität geht nur über entityId (genau eine, Präfix = Domain); entity_id, device_id, area_id, floor_id, label_id und target sind in daten verboten. erfolg=false heißt: NICHT ausgeführt.")]
    [BrauchtForkSchluessel(Stufen.GeraeteSchalten)]
    public async Task<string> DienstAsync(
        [Description("Domain des Dienstes, etwa light, switch, climate")][Beispiel("light")] string domain,
        [Description("Der Dienst, etwa turn_on, turn_off, toggle")][Beispiel("turn_on")] string dienst,
        [Description("Die Entity-Id, auf die der Dienst wirkt")] string? entityId = null,
        [Description("Zusätzliche Daten als JSON-Objekt, etwa {\"brightness_pct\": 60}")][Beispiel("{\"brightness_pct\": 60}")] string? daten = null,
        CancellationToken cancellationToken = default)
    {
        if (!reader.HatForkSchluessel) return ForkFehler.SchluesselNoetig;

        domain = domain.Trim();
        dienst = dienst.Trim();
        if (!Bezeichner.IsMatch(domain) || !Bezeichner.IsMatch(dienst))
        {
            return "Nichts ausgeführt: domain und dienst bestehen nur aus Kleinbuchstaben, Ziffern und _ — etwa light und turn_on.";
        }

        var rumpf = new JsonObject { ["domain"] = domain, ["dienst"] = dienst };
        if (!string.IsNullOrWhiteSpace(entityId))
        {
            var id = entityId.Trim();
            if (!EineEntitaet.IsMatch(id) || !id.StartsWith(domain + ".", StringComparison.Ordinal))
            {
                return $"Nichts ausgeführt: entityId muss genau eine Entität der Domain {domain} sein, etwa {domain}.zelt — nicht „{entityId}\".";
            }
            rumpf["entityId"] = id;
        }

        if (!string.IsNullOrWhiteSpace(daten))
        {
            JsonObject? objekt;
            try
            {
                objekt = JsonNode.Parse(daten) as JsonObject;
            }
            catch (JsonException)
            {
                objekt = null;
            }
            if (objekt is null) return "Nichts ausgeführt: daten muss ein JSON-Objekt sein, etwa {\"brightness_pct\": 60}.";

            var verboten = objekt.Select(feld => feld.Key).Where(VerboteneDaten.Contains).ToList();
            if (verboten.Count > 0)
            {
                return $"Nichts ausgeführt: {string.Join(", ", verboten)} gehört nicht in daten. Das Ziel geht nur über entityId, genau eine Entität.";
            }
            rumpf["daten"] = objekt;
        }

        var text = await AnfragenAsync(HttpMethod.Post, $"{Basis}/dienst", rumpf, "Home-Assistant-Dienst", cancellationToken);

        // „angenommen" ist nicht „ausgeführt": Grow OS meldet erfolg=false mit
        // Begründung, wenn Home Assistant den Aufruf abweist. Das gehört nach
        // vorn, sonst liest der Assistent nur die erste Zeile.
        try
        {
            using var json = JsonDocument.Parse(text);
            if (json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("erfolg", out var erfolg))
            {
                var meldung = SchreibWerkzeuge.Text(json.RootElement, "meldung");
                return erfolg.ValueKind == JsonValueKind.True
                    ? $"Ausgeführt: {domain}.{dienst}{(entityId is null ? "" : $" für {entityId}")}. {meldung}".TrimEnd()
                    : $"NICHT ausgeführt: {meldung ?? "Home Assistant hat den Aufruf abgewiesen."}";
            }
        }
        catch (JsonException)
        {
            // Schon ein Satz (Absage) — so weitergeben.
        }
        return text;
    }

    /// <summary>Eine Anfrage an den Home-Assistant-Zugang von Grow OS — Antwort roh oder als Satz.</summary>
    private async Task<string> AnfragenAsync(HttpMethod methode, string pfad, JsonObject? rumpf, string was, CancellationToken cancellationToken)
    {
        if (!reader.HatForkSchluessel) return ForkFehler.SchluesselNoetig;

        ForkAntwort antwort;
        try
        {
            antwort = await reader.SendenAsync(methode, pfad, rumpf?.ToJsonString(Ausgabe), cancellationToken);
        }
        catch (GrowOsException ex)
        {
            return ex.Message;
        }
        catch (HttpRequestException)
        {
            return "Grow OS ist gerade nicht erreichbar. Es wurde nichts ausgeführt.";
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return "Grow OS hat nicht rechtzeitig geantwortet. Ob der Aufruf ankam, ist offen — vor einem zweiten Versuch mit ha_zustaende nachsehen.";
        }

        if (antwort.Erfolg) return antwort.Text;

        // Gibt es den Weg in Grow OS noch gar nicht, ist das eine Frage der
        // Fassung, nicht der Rechte.
        if (antwort.Status == 404 && antwort.Text.Contains("endpoint_not_found", StringComparison.Ordinal))
        {
            return "Diese Fassung von Grow OS kennt den Zugang zu Home Assistant für Assistenten noch nicht. Bitte Grow OS Fork AI aktualisieren.";
        }

        return ForkFehler.Text(antwort.Status, antwort.Text, was);
    }
}
