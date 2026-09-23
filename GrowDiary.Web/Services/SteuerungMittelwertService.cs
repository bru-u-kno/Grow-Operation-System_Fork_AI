using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI (forkai.150): Der gleitende Mittelwert, an dem die Klima-Freigabe der
/// CO₂-Steuerung hängt — ein Filter-Helfer in Home Assistant.
/// </summary>
/// <remarks>
/// <para><b>Warum ein Filter-Helfer und kein Template.</b> Einen Mittelwert über
/// die Zeit kann ein Template nicht rechnen, ohne sich den Verlauf selbst zu
/// merken. Der Filter-Helfer „gleitender Mittelwert" tut genau das und ist in
/// Home Assistant unter Helfer sichtbar und einstellbar.</para>
/// <para><b>Warum das Fenster nicht wie ein Einstellwert geschrieben wird.</b>
/// Das Fenster ist keine Zahl in einem <c>input_number</c>, sondern eine Option
/// des Helfers. Geändert wird sie über denselben Optionen-Dialog, den ein Mensch
/// im Browser öffnet — und jede Änderung lädt den Helfer neu, womit sein
/// Mittelwert von vorn beginnt. Deshalb schreibt der Fork das Fenster nur beim
/// Speichern und nur, wenn es sich geändert hat, nie im stündlichen Lauf.</para>
/// <para><b>Die Genauigkeit bleibt, wie sie ist.</b> Der Optionen-Dialog setzt
/// ein fehlendes Feld auf seine Vorgabe (2 Nachkommastellen). Der Dienst liest
/// deshalb den aktuellen Wert aus dem Formular und schickt ihn unverändert
/// zurück.</para>
/// </remarks>
public sealed class SteuerungMittelwertService
{
    private const string OptionenPfad = "api/config/config_entries/options/flow";
    private const string DialogPfad = "api/config/config_entries/flow";
    public const string FilterArt = "time_simple_moving_average";
    public const int Nachkommastellen = 1;

    private readonly HomeAssistantService _ha;
    private readonly HomeAssistantSettingsRepository _haSettings;
    private readonly ILogger<SteuerungMittelwertService> _log;

    public SteuerungMittelwertService(
        HomeAssistantService ha,
        HomeAssistantSettingsRepository haSettings,
        ILogger<SteuerungMittelwertService> log)
    {
        _ha = ha;
        _haSettings = haSettings;
        _log = log;
    }

    /// <summary>Das Mittelungsfenster des Helfers setzen. false, wenn es nicht angekommen ist.</summary>
    public async Task<bool> FensterSetzenAsync(string entityId, int minuten, CancellationToken ct)
    {
        var settings = _haSettings.GetEffectiveHomeAssistantSettings();
        if (!settings.IsConfigured) return false;

        var eintrag = await EintragFuerAsync(settings, entityId, ct);
        if (eintrag is null)
        {
            _log.LogWarning("Mittelwert {EntityId}: kein Helfer-Eintrag in Home Assistant gefunden.", entityId);
            return false;
        }

        using var client = _ha.CreateClient(settings);
        try
        {
            var start = await client.PostAsJsonAsync(OptionenPfad, new { handler = eintrag }, ct);
            if (!start.IsSuccessStatusCode) return Scheitern($"Optionen liessen sich nicht öffnen ({(int)start.StatusCode}).");

            var formular = await start.Content.ReadFromJsonAsync<JsonElement>(ct);
            var dialog = Text(formular, "flow_id");
            if (string.IsNullOrWhiteSpace(dialog)) return Scheitern("Home Assistant hat keine Dialog-Kennung geliefert.");

            // Der Dialog springt ohne eigenen ersten Schritt direkt in das
            // Formular der Filterart. Ist es ein anderes, ist das kein
            // gleitender Mittelwert — dann nichts anfassen.
            if (Text(formular, "step_id") != FilterArt)
            {
                await client.DeleteAsync($"{OptionenPfad}/{dialog}", ct);
                return Scheitern($"Der Helfer ist kein gleitender Mittelwert (Schritt '{Text(formular, "step_id")}').");
            }

            var genauigkeit = VorschlagAusFormular(formular, "precision") ?? Nachkommastellen;
            var antwort = await client.PostAsJsonAsync($"{OptionenPfad}/{dialog}", FensterFelder(minuten, (int)genauigkeit), ct);
            var inhalt = await antwort.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (!antwort.IsSuccessStatusCode || Text(inhalt, "type") != "create_entry")
            {
                var fehler = inhalt.ValueKind == JsonValueKind.Object && inhalt.TryGetProperty("errors", out var e) ? e.ToString() : Text(inhalt, "message");
                return Scheitern($"Das Fenster wurde nicht übernommen: {fehler}");
            }

            _log.LogInformation("Mittelwert {EntityId}: Fenster auf {Minuten} min gesetzt.", entityId, minuten);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return Scheitern(ex.Message);
        }

        bool Scheitern(string grund)
        {
            _log.LogWarning("Mittelwert {EntityId}: {Grund}", entityId, grund);
            return false;
        }
    }

    /// <summary>
    /// Den Helfer neu anlegen — für eine frische Anlage über „Fehlende anlegen".
    /// Der Name bestimmt die Entitäts-Id; er kommt deshalb aus dem Katalog.
    /// </summary>
    public static async Task<(bool Erfolg, string? Fehler)> AnlegenAsync(
        HttpClient client, string name, string quelle, int minuten, CancellationToken ct)
    {
        try
        {
            var start = await client.PostAsJsonAsync(DialogPfad, new { handler = "filter", show_advanced_options = false }, ct);
            if (!start.IsSuccessStatusCode) return (false, $"Dialog liess sich nicht öffnen ({(int)start.StatusCode}).");
            var dialog = Text(await start.Content.ReadFromJsonAsync<JsonElement>(ct), "flow_id");
            if (string.IsNullOrWhiteSpace(dialog)) return (false, "Home Assistant hat keine Dialog-Kennung geliefert.");

            var art = await client.PostAsJsonAsync($"{DialogPfad}/{dialog}",
                new Dictionary<string, object?> { ["name"] = name, ["entity_id"] = quelle, ["filter"] = FilterArt }, ct);
            var artAntwort = await art.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (!art.IsSuccessStatusCode || Text(artAntwort, "step_id") != FilterArt)
            {
                return (false, $"Filterart wurde nicht angenommen: {Text(artAntwort, "message") ?? Text(artAntwort, "step_id")}");
            }

            var fertig = await client.PostAsJsonAsync($"{DialogPfad}/{dialog}", FensterFelder(minuten, Nachkommastellen), ct);
            var antwort = await fertig.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (!fertig.IsSuccessStatusCode || Text(antwort, "type") != "create_entry")
            {
                var fehler = antwort.ValueKind == JsonValueKind.Object && antwort.TryGetProperty("errors", out var e) ? e.ToString() : Text(antwort, "message");
                return (false, $"Der Dialog hat nichts angelegt: {fehler}");
            }

            return (true, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Die Felder des Formulars „gleitender Mittelwert".</summary>
    public static IReadOnlyDictionary<string, object?> FensterFelder(int minuten, int genauigkeit)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["window_size"] = new Dictionary<string, int> { ["hours"] = 0, ["minutes"] = minuten, ["seconds"] = 0 },
            ["type"] = "last",
            ["precision"] = genauigkeit,
        };

    /// <summary>
    /// Der vorgeschlagene Wert eines Feldes im Formular — Home Assistant trägt
    /// dort die aktuellen Optionen ein (<c>description.suggested_value</c>).
    /// </summary>
    public static double? VorschlagAusFormular(JsonElement formular, string feld)
    {
        if (formular.ValueKind != JsonValueKind.Object || !formular.TryGetProperty("data_schema", out var schema) || schema.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var eintrag in schema.EnumerateArray())
        {
            if (Text(eintrag, "name") != feld) continue;
            if (eintrag.TryGetProperty("description", out var beschreibung)
                && beschreibung.ValueKind == JsonValueKind.Object
                && beschreibung.TryGetProperty("suggested_value", out var wert)
                && wert.ValueKind == JsonValueKind.Number)
            {
                return wert.GetDouble();
            }

            if (eintrag.TryGetProperty("default", out var vorgabe) && vorgabe.ValueKind == JsonValueKind.Number)
            {
                return vorgabe.GetDouble();
            }
        }

        return null;
    }

    /// <summary>Welcher Helfer-Eintrag hinter der Entität steht — über das Entitätsregister.</summary>
    private static async Task<string?> EintragFuerAsync(HomeAssistantSettings settings, string entityId, CancellationToken ct)
    {
        await using var socket = await HomeAssistantSocket.OeffnenAsync(settings, ct);
        if (socket is null) return null;

        var antwort = await socket.BefehlAsync("config/entity_registry/get",
            new Dictionary<string, object?> { ["entity_id"] = entityId }, ct);
        return antwort.Erfolg && antwort.Ergebnis is { } e ? Text(e, "config_entry_id") : null;
    }

    private static string? Text(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var wert)
           && wert.ValueKind == JsonValueKind.String
            ? wert.GetString()
            : null;

    /// <summary>Eine Zahl so schreiben, wie Home Assistant sie liest.</summary>
    public static string Zahl(double wert) => wert.ToString(CultureInfo.InvariantCulture);
}
