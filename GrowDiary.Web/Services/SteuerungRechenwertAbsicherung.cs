using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI: Liest die Formel der CO₂-Rechenwerte aus Home Assistant und ersetzt
/// eine bekannte ältere Fassung — über denselben Einstellungsdialog, den man in
/// Home Assistant unter <i>Helfer → … → Optionen</i> sieht.
/// </summary>
/// <remarks>
/// <para><b>Der Weg (Home Assistant 2026.9).</b> Entität → Helfer-Eintrag über
/// <c>config/entity_registry/get</c> (WebSocket). Den Dialog öffnet
/// <c>POST api/config/config_entries/options/flow</c> mit dem Eintrag als
/// <c>handler</c>; er springt sofort in den Schritt der Art
/// (<c>binary_sensor</c>/<c>sensor</c>) und belegt jedes Feld mit dem
/// gespeicherten Wert vor. Zum bloßen Lesen wird er mit <c>DELETE</c> wieder
/// geschlossen, ohne dass etwas gespeichert wird.</para>
/// <para><b>Nicht abbrechbar, sobald begonnen.</b> Ein offener Dialog
/// verfällt in Home Assistant nicht von selbst — er bleibt, bis er
/// abgeschlossen oder abgebrochen wird. Und ein Schreiben ohne Nachsehen
/// hinterließe vielleicht einen Rechenwert ohne Wert. Deshalb laufen alle
/// Aufrufe an Home Assistant ohne das Abbruchsignal der Anfrage: verlässt der
/// Bediener die Seite, wird trotzdem zu Ende gelesen, geschlossen und
/// nötigenfalls zurückgeschrieben.</para>
/// <para><b>Was dabei schiefgehen kann.</b> Ein Feld, das beim Absenden fehlt,
/// löscht Home Assistant — deshalb geht jeder vorbelegte Wert unverändert
/// zurück (<see cref="Co2Rechenwerte.Antwort"/>). Und eine Formel, die Home
/// Assistant annimmt, kann trotzdem nichts liefern: nach dem Schreiben wird
/// der Zustand des Rechenwerts angesehen. Bleibt er „nicht verfügbar", wird
/// die alte Formel zurückgeschrieben.</para>
/// </remarks>
public sealed class SteuerungRechenwertAbsicherung
{
    private const string DialogPfad = "api/config/config_entries/options/flow";

    private readonly HomeAssistantService _ha;
    private readonly ILogger _log;
    private readonly string _sicherungsOrdner;

    /// <param name="sicherungsOrdner">
    /// Wohin der alte Stand gesichert wird — derselbe Ordner wie bei den
    /// Automationen (<see cref="SteuerungAutomationService.SicherungsOrdner"/>).
    /// </param>
    public SteuerungRechenwertAbsicherung(HomeAssistantService ha, ILogger log, string sicherungsOrdner)
    {
        _ha = ha;
        _log = log;
        _sicherungsOrdner = sicherungsOrdner;
    }

    /// <summary>Wie lange nach dem Schreiben auf einen gültigen Zustand gewartet wird.</summary>
    public TimeSpan Wartezeit { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Abstand der Blicke auf den Zustand in dieser Zeit.</summary>
    public TimeSpan Takt { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Wie lange auf eine Antwort am WebSocket gewartet wird. Die HTTP-Aufrufe
    /// haben ihre Zeitgrenze im Client; der WebSocket hat keine — und läuft hier
    /// ohne das Abbruchsignal der Anfrage. Ohne diese Grenze hinge eine
    /// halboffene Verbindung ewig.
    /// </summary>
    public TimeSpan SocketFrist { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Ein Rechenwert, wie die Seite ihn zeigt.</summary>
    /// <param name="Stand"><see cref="Co2Rechenwerte.Stand"/> als Text, oder „NichtLesbar".</param>
    public sealed record Rechenwert(
        string EntityId, string Name, string Stand,
        string? Heute, string? Danach, string? AlteFormel, string? NeueFormel, string? Hinweis)
    {
        public bool Behebbar => Stand == nameof(Co2Rechenwerte.Stand.Veraltet);
    }

    /// <summary>Alle betroffenen Rechenwerte lesen. Ändert nichts.</summary>
    public async Task<IReadOnlyList<Rechenwert>> LesenAsync(
        HttpClient client, HomeAssistantSocket? socket, IReadOnlyDictionary<string, string> zuordnung, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ergebnis = new List<Rechenwert>();
        foreach (var entityId in Co2Rechenwerte.Betroffene)
        {
            ergebnis.Add(await EinenLesenAsync(client, socket, entityId, zuordnung, CancellationToken.None));
        }
        return ergebnis;
    }

    private async Task<Rechenwert> EinenLesenAsync(
        HttpClient client, HomeAssistantSocket? socket, string entityId,
        IReadOnlyDictionary<string, string> zuordnung, CancellationToken ct)
    {
        var name = SteuerungBauteile.Alle.Single(b => b.EntityId == entityId).Name;
        Rechenwert NichtLesbar(string grund) => new(entityId, name, "NichtLesbar", null, null, null, null, grund);

        if (socket is null) return NichtLesbar("Die Formel ließ sich nicht lesen: Home Assistant nimmt die WebSocket-Anmeldung nicht an.");

        var eintrag = await EintragAsync(socket, entityId, ct);
        if (eintrag.Fehler is not null) return NichtLesbar(eintrag.Fehler);

        var formel = await FormelLesenAsync(client, eintrag.Id!, ct);
        if (formel is null) return NichtLesbar("Der Einstellungsdialog von Home Assistant lieferte keine Formel.");

        var (stand, alt) = Co2Rechenwerte.Beurteilen(entityId, formel, zuordnung);
        var neu = Co2Rechenwerte.Aktuelle(entityId, zuordnung);
        return stand switch
        {
            Co2Rechenwerte.Stand.Aktuell => new(entityId, name, stand.ToString(), null, null, null, null, null),
            Co2Rechenwerte.Stand.Veraltet => new(entityId, name, stand.ToString(), alt!.Heute, alt.Danach, formel, neu, null),
            Co2Rechenwerte.Stand.Angepasst => new(entityId, name, stand.ToString(), null, null, formel, neu,
                "Die Formel ist von Hand angepasst — Fork AI ersetzt sie nicht. Zum Vergleich steht die aktuelle Fassung daneben."),
            Co2Rechenwerte.Stand.AndererFuehler => new(entityId, name, stand.ToString(), null, null, formel, neu,
                $"Die Formel rechnet mit {Co2Rechenwerte.FuehlerInFormel(entityId, formel)}, unter „Rollen bearbeiten“ ist "
                + $"{zuordnung.GetValueOrDefault("co2_sensor")} zugeordnet. Fork AI ändert sie nicht — welcher Fühler stimmt, "
                + "weißt du. Stimmt die Zuordnung nicht, danach hier noch einmal nachsehen."),
            _ => new(entityId, name, stand.ToString(), null, null, formel, null,
                "Dafür muss unter „Rollen bearbeiten“ ein CO₂-Sensor zugeordnet sein."),
        };
    }

    public sealed record Ergebnis(string EntityId, string Name, bool Geschrieben, string? Fehler);

    /// <summary>
    /// Einen veralteten Rechenwert ersetzen: sichern, schreiben, nachlesen,
    /// Zustand ansehen — und bei Ausfall zurückschreiben.
    /// </summary>
    public async Task<Ergebnis> ErsetzenAsync(
        HttpClient client, HomeAssistantSocket socket, HomeAssistantSettings settings,
        string entityId, IReadOnlyDictionary<string, string> zuordnung, CancellationToken ct)
    {
        var name = SteuerungBauteile.Alle.Single(b => b.EntityId == entityId).Name;
        Ergebnis Fehler(string grund) => new(entityId, name, false, grund);

        // Ab hier läuft es zu Ende — auch wenn der Bediener die Seite verlässt.
        ct.ThrowIfCancellationRequested();
        ct = CancellationToken.None;

        var eintrag = await EintragAsync(socket, entityId, ct);
        if (eintrag.Fehler is not null) return Fehler(eintrag.Fehler);

        var dialog = await OeffnenAsync(client, eintrag.Id!, ct);
        if (dialog is null) return Fehler("Der Einstellungsdialog ließ sich nicht öffnen.");

        var alt = Co2Rechenwerte.FormelAusDialog(dialog.Value.Schema);
        if (alt is null || Co2Rechenwerte.Beurteilen(entityId, alt, zuordnung).Stand != Co2Rechenwerte.Stand.Veraltet)
        {
            // Zwischen Anzeigen und Drücken hat jemand die Formel geändert.
            await SchliessenAsync(client, dialog.Value.Id, ct);
            return Fehler("Die Formel ist nicht mehr die bekannte alte Fassung — nicht geändert.");
        }

        var vorher = (JsonArray)dialog.Value.Schema.DeepClone();
        if (!await SichernAsync(entityId, eintrag.Id!, vorher, ct))
        {
            await SchliessenAsync(client, dialog.Value.Id, ct);
            return Fehler("Die alte Formel ließ sich nicht sichern — deshalb nicht geändert.");
        }

        var neu = Co2Rechenwerte.Aktuelle(entityId, zuordnung)!;
        var gesendet = await AbsendenAsync(client, dialog.Value.Id, Co2Rechenwerte.Antwort(vorher, neu), ct);
        if (gesendet is not null)
        {
            // Bei einer Ablehnung bleibt der Dialog in Home Assistant offen.
            await SchliessenAsync(client, dialog.Value.Id, ct);
            return Fehler(gesendet);
        }

        // Nachlesen, was Home Assistant jetzt wirklich hat — die Formel UND
        // alle übrigen Werte. Fehlt danach etwa die Einheit, hat die Antwort
        // ein Feld nicht so zurückgeschickt, wie Home Assistant es erwartet.
        string? problem;
        var nachher = await OeffnenAsync(client, eintrag.Id!, ct);
        if (nachher is null)
        {
            problem = "Geschrieben, aber der Einstellungsdialog lässt sich danach nicht mehr öffnen";
        }
        else
        {
            await SchliessenAsync(client, nachher.Value.Id, ct);
            var jetzt = Co2Rechenwerte.FormelAusDialog(nachher.Value.Schema);
            problem = jetzt is null || Co2Rechenwerte.Beurteilen(entityId, jetzt, zuordnung).Stand != Co2Rechenwerte.Stand.Aktuell
                ? "Geschrieben, aber Home Assistant zeigt danach nicht die neue Formel"
                : !JsonNode.DeepEquals(Co2Rechenwerte.Antwort(vorher, neu), Co2Rechenwerte.Antwort(nachher.Value.Schema, neu))
                    ? "Geschrieben, aber danach fehlte oder änderte sich eine andere Einstellung des Helfers"
                    : !await LiefertWertAsync(settings, entityId, ct)
                        ? "Nach dem Ersetzen lieferte der Rechenwert keinen Wert"
                        : null;
        }

        if (problem is null)
        {
            _log.LogInformation("Rechenwert {EntityId} auf die aktuelle Formel gebracht.", entityId);
            return new Ergebnis(entityId, name, true, null);
        }

        // Etwas stimmt nicht: der ganze alte Stand wieder hinein.
        _log.LogWarning("Rechenwert {EntityId}: {Problem} — alter Stand zurück.", entityId, problem);
        var zurueckFehler = await ZurueckschreibenAsync(client, eintrag.Id!, vorher, alt, ct)
            ?? await StandGleichAsync(client, eintrag.Id!, vorher, alt, ct);
        return Fehler(zurueckFehler is null
            ? $"{problem} — der alte Stand wurde zurückgeschrieben."
            : $"{problem}, und das Zurückschreiben scheiterte ({zurueckFehler}). "
              + $"Der alte Stand liegt unter {_sicherungsOrdner}.");
    }

    /// <summary>
    /// Nach dem Zurückschreiben nachlesen: steht wirklich der alte Stand da?
    /// Verliert Home Assistant beim Speichern jedes Mal dasselbe Feld, hilft
    /// auch das Zurückschreiben nicht — dann muss es die Seite sagen.
    /// </summary>
    /// <returns>Null, wenn alles wieder stimmt, sonst was abweicht.</returns>
    private static async Task<string?> StandGleichAsync(
        HttpClient client, string eintrag, JsonArray vorher, string alteFormel, CancellationToken ct)
    {
        if (await OeffnenAsync(client, eintrag, ct) is not { } dialog) return "danach nicht mehr lesbar";
        await SchliessenAsync(client, dialog.Id, ct);
        return JsonNode.DeepEquals(Co2Rechenwerte.Antwort(vorher, alteFormel), Co2Rechenwerte.Antwort(dialog.Schema, alteFormel))
            && Co2Rechenwerte.FormelAusDialog(dialog.Schema) is { } formel && formel == alteFormel
            ? null
            : "danach weicht der Helfer noch vom alten Stand ab";
    }

    /// <summary>Den gesicherten Stand zurück — alle Werte, nicht nur die Formel.</summary>
    private static async Task<string?> ZurueckschreibenAsync(
        HttpClient client, string eintrag, JsonArray vorher, string alteFormel, CancellationToken ct)
    {
        if (await OeffnenAsync(client, eintrag, ct) is not { } dialog) return "Dialog ließ sich nicht öffnen";
        var fehler = await AbsendenAsync(client, dialog.Id, Co2Rechenwerte.Antwort(vorher, alteFormel), ct);
        if (fehler is not null) await SchliessenAsync(client, dialog.Id, ct);
        return fehler;
    }

    // ------------------------------------------------------------ Home Assistant

    private async Task<(string? Id, string? Fehler)> EintragAsync(
        HomeAssistantSocket socket, string entityId, CancellationToken ct)
    {
        using var frist = CancellationTokenSource.CreateLinkedTokenSource(ct);
        frist.CancelAfter(SocketFrist);
        var antwort = await socket.BefehlAsync("config/entity_registry/get",
            new Dictionary<string, object?> { ["entity_id"] = entityId }, frist.Token);
        if (!antwort.Erfolg)
        {
            return (null, frist.IsCancellationRequested
                ? "Home Assistant hat am WebSocket nicht geantwortet — die Formel wurde nicht gelesen."
                : $"{entityId} ist in Home Assistant nicht registriert.");
        }

        var id = antwort.Ergebnis is { ValueKind: JsonValueKind.Object } e
            && e.TryGetProperty("config_entry_id", out var k) && k.ValueKind == JsonValueKind.String
            ? k.GetString() : null;
        return string.IsNullOrWhiteSpace(id)
            ? (null, "Der Rechenwert steht in der YAML-Konfiguration, nicht unter Helfer — Fork AI kann ihn dort nicht ändern.")
            : (id, null);
    }

    private readonly record struct Dialog(string Id, JsonArray Schema);

    private static async Task<Dialog?> OeffnenAsync(HttpClient client, string eintrag, CancellationToken ct)
    {
        try
        {
            using var antwort = await client.PostAsJsonAsync(DialogPfad,
                new Dictionary<string, object> { ["handler"] = eintrag, ["show_advanced_options"] = true }, ct);
            if (!antwort.IsSuccessStatusCode) return null;
            var inhalt = JsonNode.Parse(await antwort.Content.ReadAsStringAsync(ct)) as JsonObject;
            if (inhalt?["type"]?.ToString() != "form") return null;
            if (inhalt["flow_id"]?.ToString() is not { } id || inhalt["data_schema"] is not JsonArray schema) return null;
            return new Dialog(id, schema);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private static async Task SchliessenAsync(HttpClient client, string id, CancellationToken ct)
    {
        try
        {
            using var _ = await client.DeleteAsync($"{DialogPfad}/{Uri.EscapeDataString(id)}", ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Mehr lässt sich nicht tun. Der Dialog bleibt in Home Assistant
            // offen, bis HA neu startet — er speichert aber nichts.
        }
    }

    /// <summary>Öffnen, Formel ablesen, wieder schließen — nichts wird gespeichert.</summary>
    private static async Task<string?> FormelLesenAsync(HttpClient client, string eintrag, CancellationToken ct)
    {
        if (await OeffnenAsync(client, eintrag, ct) is not { } dialog) return null;
        var formel = Co2Rechenwerte.FormelAusDialog(dialog.Schema);
        await SchliessenAsync(client, dialog.Id, ct);
        return formel;
    }

    /// <returns>Null bei Erfolg, sonst der Grund.</returns>
    private static async Task<string?> AbsendenAsync(HttpClient client, string id, JsonObject werte, CancellationToken ct)
    {
        try
        {
            using var antwort = await client.PostAsJsonAsync($"{DialogPfad}/{Uri.EscapeDataString(id)}", werte, ct);
            var inhalt = JsonNode.Parse(await antwort.Content.ReadAsStringAsync(ct)) as JsonObject;
            if (!antwort.IsSuccessStatusCode)
            {
                // Schemafehler kommen als {"errors": {"base": [...]}}
                // (helpers/data_entry_flow.py), andere als {"message": ...}.
                var grund = inhalt?["errors"]?["base"]?[0]?.ToString()
                    ?? inhalt?["errors"]?.ToJsonString()
                    ?? inhalt?["message"]?.ToString()
                    ?? "ohne Begründung";
                return $"Home Assistant hat abgelehnt ({(int)antwort.StatusCode}): {grund}";
            }
            // Bei fehlerhaften Feldern antwortet HA mit 200 und dem Formular noch einmal.
            return inhalt?["type"]?.ToString() == "create_entry"
                ? null
                : $"Home Assistant hat nicht gespeichert: {inhalt?["errors"]?.ToJsonString() ?? inhalt?["type"]?.ToString()}";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return ex.Message;
        }
    }

    /// <summary>Liefert der Rechenwert nach dem Neuladen einen Zustand?</summary>
    private async Task<bool> LiefertWertAsync(HomeAssistantSettings settings, string entityId, CancellationToken ct)
    {
        var bis = DateTime.UtcNow + Wartezeit;
        do
        {
            var zustand = await _ha.GetEntityStateAsync(settings, entityId, ct);
            if (zustand is not null && zustand.State is not ("unavailable" or "unknown" or "")) return true;
            if (Takt > TimeSpan.Zero) await Task.Delay(Takt, ct);
        }
        while (DateTime.UtcNow < bis);
        return false;
    }

    /// <summary>Die alten Werte des Dialogs wegschreiben — neben den Sicherungen der Automationen.</summary>
    private async Task<bool> SichernAsync(string entityId, string eintrag, JsonArray schema, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(_sicherungsOrdner);
            var ziel = Path.Combine(_sicherungsOrdner, $"{entityId}-{DateTime.UtcNow:yyyyMMddHHmmss}.json");
            var inhalt = new JsonObject
            {
                ["entity_id"] = entityId,
                ["config_entry_id"] = eintrag,
                ["werte"] = Co2Rechenwerte.Antwort(schema, Co2Rechenwerte.FormelAusDialog(schema) ?? string.Empty),
            };
            await File.WriteAllTextAsync(ziel, inhalt.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), ct);
            _log.LogInformation("Alte Formel von {EntityId} gesichert: {Ziel}", entityId, ziel);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Sicherung von {EntityId} fehlgeschlagen.", entityId);
            return false;
        }
    }
}
