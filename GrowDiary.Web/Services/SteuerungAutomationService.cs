using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Fork AI (forkai.72): Legt die Automationen einer Steuerung an.
/// </summary>
/// <remarks>
/// <para><b>Der heikelste Teil des Einrichtens.</b> Am Ende dieser Automationen
/// hängt ein Ventil an einer Gasflasche. Deshalb gelten hier strengere Regeln
/// als beim Anlegen von Helfern.</para>
/// <para><b>Nur was der Fork selbst angelegt hat, fasst er wieder an.</b> Jede
/// erzeugte Automation trägt eine Herkunftsmarke in ihrer Beschreibung. Findet
/// der Dienst eine Automation unter derselben Kennung ohne diese Marke, rührt er
/// sie nicht an und meldet es. Handgebaute Automationen enthalten Dinge, die
/// keine Vorlage kennt — in dieser Anlage etwa die Nachführung des Durchflusses
/// und das Halten des letzten Zustands bei Geräteaussetzern. Ein Generator, der
/// darüberschreibt, nimmt sie weg, ohne dass es jemand merkt.</para>
/// <para><b>Vorher sichern, nachher nachsehen.</b> Der vorhandene Stand wird vor
/// dem Überschreiben weggeschrieben, damit ein Zurück existiert. Und nach dem
/// Schreiben wird geprüft, ob die Automation wirklich geladen ist — sonst steht
/// eine Regelung da, die stumm nichts tut.</para>
/// </remarks>
public sealed class SteuerungAutomationService
{
    /// <summary>Woran der Fork seine eigenen Automationen erkennt.</summary>
    public const string HerkunftsMarke = "Herkunft: fork-ai/";

    private const string ConfigPfad = "api/config/automation/config";

    private readonly HomeAssistantService _ha;
    private readonly ILogger<SteuerungAutomationService> _log;
    private readonly string _vorlagenWurzel;

    public SteuerungAutomationService(HomeAssistantService ha, ILogger<SteuerungAutomationService> log)
    {
        _ha = ha;
        _log = log;
        _vorlagenWurzel = Path.Combine(AppContext.BaseDirectory, "Vorlagen");
    }

    /// <summary>Was mit einer einzelnen Automation geschehen ist.</summary>
    public enum Stand
    {
        /// <summary>Neu angelegt.</summary>
        Angelegt,
        /// <summary>Vorhanden und vom Fork — auf den Stand der Vorlage gebracht.</summary>
        Erneuert,
        /// <summary>Vorhanden, aber von Hand gebaut. Unangetastet.</summary>
        Fremd,
        /// <summary>Nicht angelegt, weil ein gebrauchtes Gerät fehlt.</summary>
        OhneGeraet,
        /// <summary>Versucht und gescheitert.</summary>
        Fehlgeschlagen,
    }

    public sealed record Ergebnis(string Kennung, string Name, Stand Stand, string? Hinweis);

    public sealed record Bilanz(bool Erreichbar, IReadOnlyList<Ergebnis> Einzeln)
    {
        public int Angelegt => Einzeln.Count(e => e.Stand is Stand.Angelegt or Stand.Erneuert);
        public int Fremd => Einzeln.Count(e => e.Stand == Stand.Fremd);
        public int Fehlgeschlagen => Einzeln.Count(e => e.Stand == Stand.Fehlgeschlagen);
    }

    /// <summary>Die Automationen einer Steuerung anlegen.</summary>
    /// <param name="nurVorschau">True: nichts schreiben, nur sagen, was geschähe.</param>
    public async Task<Bilanz> AnlegenAsync(
        string modul,
        IReadOnlyDictionary<string, string> zuordnung,
        HomeAssistantSettings settings,
        bool nurVorschau,
        CancellationToken ct = default)
    {
        var ordner = Path.Combine(_vorlagenWurzel, modul);
        if (!Directory.Exists(ordner))
        {
            return new Bilanz(true, Array.Empty<Ergebnis>());
        }

        using var client = _ha.CreateClient(settings);
        var einzeln = new List<Ergebnis>();

        foreach (var datei in Directory.EnumerateFiles(ordner, "*.json").OrderBy(d => d, StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(datei);
            var kennung = $"fork_ai_{modul}_{name}";

            JsonObject? vorlage;
            try
            {
                vorlage = JsonNode.Parse(await File.ReadAllTextAsync(datei, ct)) as JsonObject;
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                einzeln.Add(new Ergebnis(kennung, name, Stand.Fehlgeschlagen, ex.Message));
                continue;
            }

            if (vorlage is null)
            {
                einzeln.Add(new Ergebnis(kennung, name, Stand.Fehlgeschlagen, "Die Vorlage ist kein Objekt."));
                continue;
            }

            // Fork AI (Chiller-Ansteuerung): Eine Vorlage kann entfallen, weil ein
            // Gerät da IST — die Steckdosen-Regelung, sobald der Kühler einen
            // eigenen Sollwert-Eingang hat. Das ist kein fehlendes Gerät.
            if (UeberfluessigWegen(vorlage, zuordnung) is { } rolleDa)
            {
                einzeln.Add(new Ergebnis(kennung, name, Stand.OhneGeraet,
                    $"Nicht nötig: die Rolle „{rolleDa}“ ist zugeordnet."));
                continue;
            }

            var fertig = Fuellen(vorlage, zuordnung);
            if (fertig is null)
            {
                einzeln.Add(new Ergebnis(kennung, name, Stand.OhneGeraet,
                    "Eine gebrauchte Rolle ist nicht zugeordnet."));
                continue;
            }

            var (vorhanden, fremd) = await VorhandenAsync(client, kennung, ct);
            if (fremd)
            {
                einzeln.Add(new Ergebnis(kennung, name, Stand.Fremd,
                    "Unter dieser Kennung liegt eine Automation ohne Herkunftsmarke — sie bleibt unangetastet."));
                continue;
            }

            if (nurVorschau)
            {
                einzeln.Add(new Ergebnis(kennung, name,
                    vorhanden ? Stand.Erneuert : Stand.Angelegt, "Vorschau — nichts geschrieben."));
                continue;
            }

            if (vorhanden is not false)
            {
                await SichernAsync(client, kennung, ct);
            }

            var fehler = await SchreibenAsync(client, kennung, fertig, ct);
            einzeln.Add(fehler is null
                ? new Ergebnis(kennung, name, vorhanden ? Stand.Erneuert : Stand.Angelegt, null)
                : new Ergebnis(kennung, name, Stand.Fehlgeschlagen, fehler));

            if (fehler is null)
            {
                _log.LogInformation("Automation {Kennung} geschrieben.", kennung);
            }
            else
            {
                _log.LogWarning("Automation {Kennung} nicht geschrieben: {Fehler}", kennung, fehler);
            }
        }

        return new Bilanz(true, einzeln);
    }

    /// <summary>
    /// Platzhalter ersetzen und die Blöcke entfernen, deren Rolle frei ist.
    /// </summary>
    /// <returns>Null, wenn danach noch ein Platzhalter übrig ist.</returns>
    public static JsonObject? Fuellen(JsonObject vorlage, IReadOnlyDictionary<string, string> zuordnung)
    {
        var belegt = zuordnung
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

        var geputzt = Aussieben(vorlage.DeepClone(), belegt.Keys.ToHashSet(StringComparer.Ordinal));
        if (geputzt is not JsonObject rumpf) return null;

        var text = rumpf.ToJsonString();
        var fertig = SteuerungBauteile.VorlageFuellen(text, belegt);
        return fertig is null ? null : JsonNode.Parse(fertig) as JsonObject;
    }

    /// <summary>
    /// Die Rolle, deretwegen die ganze Vorlage entfällt (<c>"wennNicht"</c> auf
    /// oberster Ebene), oder null.
    /// </summary>
    public static string? UeberfluessigWegen(JsonObject vorlage, IReadOnlyDictionary<string, string> zuordnung)
    {
        if (!vorlage.TryGetPropertyValue("wennNicht", out var knoten)) return null;
        var rolle = knoten?.GetValue<string>();
        return rolle is not null
            && zuordnung.TryGetValue(rolle, out var entity)
            && !string.IsNullOrWhiteSpace(entity)
            ? rolle
            : null;
    }

    /// <summary>
    /// Blöcke mit <c>"wenn": "rolle"</c> entfernen, wenn die Rolle frei ist —
    /// und Blöcke mit <c>"wennNicht": "rolle"</c>, wenn sie belegt ist.
    /// </summary>
    /// <remarks>
    /// Ohne das scheitert die ganze Automation an einem Gerät, das sie nicht
    /// braucht: Die Dosierung prüft, ob die Abluft gedrosselt ist — wer keinen
    /// Abluft-Regler hat, soll trotzdem dosieren.
    /// </remarks>
    private static JsonNode? Aussieben(JsonNode? knoten, IReadOnlySet<string> belegteRollen)
    {
        switch (knoten)
        {
            case JsonObject o:
            {
                if (o.TryGetPropertyValue("wenn", out var wenn)
                    && wenn?.GetValue<string>() is { } rolle
                    && !belegteRollen.Contains(rolle))
                {
                    return null;
                }

                if (o.TryGetPropertyValue("wennNicht", out var wennNicht)
                    && wennNicht?.GetValue<string>() is { } belegt
                    && belegteRollen.Contains(belegt))
                {
                    return null;
                }

                o.Remove("wenn");
                o.Remove("wennNicht");
                foreach (var schluessel in o.Select(p => p.Key).ToList())
                {
                    var kind = Aussieben(o[schluessel], belegteRollen);
                    if (kind is null && o[schluessel] is JsonObject) o.Remove(schluessel);
                    else o[schluessel] = kind;
                }

                return o;
            }

            case JsonArray a:
            {
                var behalten = a.Select(k => Aussieben(k, belegteRollen)).Where(k => k is not null).ToList();
                a.Clear();
                foreach (var k in behalten) a.Add(k);
                return a;
            }

            default:
                return knoten;
        }
    }

    /// <summary>Gibt es die Automation schon, und stammt sie vom Fork?</summary>
    private async Task<(bool Vorhanden, bool Fremd)> VorhandenAsync(
        HttpClient client, string kennung, CancellationToken ct)
    {
        try
        {
            var antwort = await client.GetAsync($"{ConfigPfad}/{kennung}", ct);
            if (!antwort.IsSuccessStatusCode) return (false, false);

            var text = await antwort.Content.ReadAsStringAsync(ct);
            return (true, !text.Contains(HerkunftsMarke, StringComparison.Ordinal));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (false, false);
        }
    }

    /// <summary>Den vorhandenen Stand wegschreiben, damit ein Zurück existiert.</summary>
    private async Task SichernAsync(HttpClient client, string kennung, CancellationToken ct)
    {
        try
        {
            var antwort = await client.GetAsync($"{ConfigPfad}/{kennung}", ct);
            if (!antwort.IsSuccessStatusCode) return;

            var ordner = Path.Combine(AppContext.BaseDirectory, "App_Data", "automations-backup");
            Directory.CreateDirectory(ordner);
            var ziel = Path.Combine(ordner, $"{kennung}-{DateTime.UtcNow:yyyyMMddHHmmss}.json");
            await File.WriteAllTextAsync(ziel, await antwort.Content.ReadAsStringAsync(ct), ct);
            _log.LogInformation("Alter Stand von {Kennung} gesichert: {Ziel}", kennung, ziel);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            _log.LogWarning(ex, "Sicherung von {Kennung} fehlgeschlagen.", kennung);
        }
    }

    /// <summary>Schreiben und nachsehen, ob es angekommen ist.</summary>
    private static async Task<string?> SchreibenAsync(
        HttpClient client, string kennung, JsonObject config, CancellationToken ct)
    {
        try
        {
            var antwort = await client.PostAsJsonAsync($"{ConfigPfad}/{kennung}", config, ct);
            if (!antwort.IsSuccessStatusCode)
            {
                var text = await antwort.Content.ReadAsStringAsync(ct);
                return $"Abgelehnt ({(int)antwort.StatusCode}): {text}";
            }

            // Nachsehen: geschrieben heisst nicht geladen. Eine Automation, die
            // HA nicht annimmt, steht sonst still da und tut nichts.
            var nachher = await client.GetAsync($"{ConfigPfad}/{kennung}", ct);
            return nachher.IsSuccessStatusCode
                ? null
                : "Geschrieben, aber Home Assistant liefert sie nicht zurück.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return ex.Message;
        }
    }
}
