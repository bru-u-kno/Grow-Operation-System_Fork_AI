using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GrowDiary.Web.Infrastructure;
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

    /// <summary>Wo die Vorlagen liegen — je Modul ein Ordner.</summary>
    public static string VorlagenWurzel => Path.Combine(AppContext.BaseDirectory, "Vorlagen");

    /// <summary>
    /// Die Herkunftsmarke samt Fassung, etwa <c>Herkunft: fork-ai/co2/dosierung/4</c>.
    /// </summary>
    private static readonly Regex FassungMuster = new(
        @"Herkunft: fork-ai/(?<modul>[a-z0-9_-]+)/(?<vorlage>[a-z0-9_]+)/(?<fassung>\d+)",
        RegexOptions.CultureInvariant);

    private readonly HomeAssistantService _ha;
    private readonly ILogger<SteuerungAutomationService> _log;
    private readonly string _vorlagenWurzel;

    public SteuerungAutomationService(HomeAssistantService ha, ILogger<SteuerungAutomationService> log, AppPaths pfade)
    {
        _ha = ha;
        _log = log;
        _vorlagenWurzel = VorlagenWurzel;
        SicherungsOrdner = SteuerungSicherungsOrdner.Fuer(pfade);
    }

    /// <summary>Wohin der alte Stand vor dem Überschreiben gesichert wird — unter dem Datenpfad.</summary>
    public string SicherungsOrdner { get; }

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

    public sealed record Ergebnis(string Kennung, string Name, Stand Stand, string? Hinweis)
    {
        /// <summary>
        /// Der Name aus der Vorlage (<c>alias</c>), etwa „Water Chiller Wächter".
        /// </summary>
        /// <remarks>
        /// Fork AI (02.10.2026): <see cref="Name"/> ist der Dateiname der Vorlage
        /// und dient dem Abgleich; in der Vorschau stand er roh auf dem Schirm
        /// („waechter", „dosierung").
        /// </remarks>
        public string? Titel { get; init; }
    }

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

        // Schreibt Automationen: die lange Frist. Home Assistant lädt beim
        // Speichern alle Automationen neu, das dauert auf einem kleinen Rechner.
        using var client = _ha.CreateClient(settings, _ha.Dienstfrist);
        var einzeln = new List<Ergebnis>();
        // Kennung → Alias der Vorlage, für die Anzeige (Ergebnis.Titel).
        var titel = new Dictionary<string, string>(StringComparer.Ordinal);

        // Fork AI (01.10.2026): Die handgebauten Automationen stehen unter den
        // Katalog-Kennungen, die vom Fork angelegten unter einer anderen. Ohne
        // diesen Blick legte der Fork neben eine handgebaute Dosierung eine
        // zweite — zwei Schleifen an einem Gasventil.
        var vorhandeneEntitaeten = await _ha.GetEntitiesAsync(settings, ct);

        foreach (var datei in Directory.EnumerateFiles(ordner, "*.json").OrderBy(d => d, StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(datei);
            var kennung = SteuerungBauteile.AutomationsKennung(modul, name);

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
            if (vorlage["alias"] is JsonValue alias && alias.TryGetValue<string>(out var lesbar)) titel[kennung] = lesbar;

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

            if (HandgebauteDaneben(modul, name, vorhandeneEntitaeten) is { } handgebaut)
            {
                einzeln.Add(new Ergebnis(kennung, name, Stand.Fremd,
                    $"Unter {handgebaut} läuft schon eine handgebaute Automation für diese Aufgabe. Eine zweite daneben würde doppelt schalten — es wird keine angelegt."));
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

        return new Bilanz(true, einzeln.Select(e => titel.TryGetValue(e.Kennung, out var t) ? e with { Titel = t } : e).ToList());
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
        if (fertig is null || JsonNode.Parse(fertig) is not JsonObject ergebnis) return null;

        SchaltbefehleAnpassen(ergebnis);
        return ergebnis;
    }

    /// <summary>
    /// Die Domänen, die statt <c>select.select_option</c> mit „On"/„Off" ein
    /// <c>turn_on</c>/<c>turn_off</c> brauchen — dieselben, die die Schalt-Rollen
    /// neben <c>select</c> zulassen (<see cref="SteuerungGeraeteRollen"/>).
    /// </summary>
    private static readonly HashSet<string> EinAusDomaenen = new(StringComparer.Ordinal) { "switch", "input_boolean" };

    /// <summary>
    /// Fork AI (01.10.2026): Schaltbefehle an die Domäne des zugeordneten Geräts anpassen.
    /// </summary>
    /// <remarks>
    /// <para>Die Vorlagen sind für einen AC-Infinity-Port geschrieben: ein
    /// <c>select</c> mit den Optionen „On" und „Off". Die Schalt-Rolle lässt aber
    /// auch <c>switch</c> und <c>input_boolean</c> zu. An die ging bisher ein
    /// <c>select.select_option</c> — Home Assistant lehnt ihn für diese Domäne ab,
    /// das Ventil blieb zu, und die Zwangsschließung des Wächters ging genauso
    /// ins Leere.</para>
    /// <para>Umgeschrieben werden nur Befehle und Zustandsprüfungen, deren Ziel
    /// wirklich ein <c>switch</c> oder <c>input_boolean</c> ist. Ein <c>select</c>
    /// bleibt, wie er ist.</para>
    /// </remarks>
    private static void SchaltbefehleAnpassen(JsonNode? knoten)
    {
        switch (knoten)
        {
            case JsonObject o:
                if (Text(o["action"]) == "select.select_option"
                    && Domaene(Text(o["target"]?["entity_id"])) is { } domaene
                    && EinAusDomaenen.Contains(domaene)
                    && Text(o["data"]?["option"]) is "On" or "Off")
                {
                    o["action"] = $"{domaene}.{(Text(o["data"]?["option"]) == "On" ? "turn_on" : "turn_off")}";
                    o.Remove("data");
                }

                // Zustand: ein select meldet „On"/„Off", ein switch „on"/„off".
                if (Domaene(Text(o["entity_id"])) is { } eigene && EinAusDomaenen.Contains(eigene))
                {
                    foreach (var feld in new[] { "state", "to", "from" })
                    {
                        if (Text(o[feld]) is "On" or "Off") o[feld] = Text(o[feld])!.ToLowerInvariant();
                    }
                }

                foreach (var (_, kind) in o.ToList()) SchaltbefehleAnpassen(kind);
                break;

            case JsonArray a:
                foreach (var kind in a.ToList()) SchaltbefehleAnpassen(kind);
                break;
        }
    }

    private static string? Text(JsonNode? knoten)
        => knoten is JsonValue v && v.TryGetValue<string>(out var text) ? text : null;

    private static string? Domaene(string? entityId)
        => entityId is not null && entityId.Contains('.') ? entityId.Split('.', 2)[0] : null;

    /// <summary>
    /// Die Entity-ID einer handgebauten Automation, die dieselbe Aufgabe hat wie
    /// diese Vorlage — oder null.
    /// </summary>
    /// <remarks>
    /// Handgebaut heißt: sie steht unter der Katalog-Kennung und trägt NICHT die
    /// Konfigurations-Kennung, unter der der Fork anlegt. Beim Kühler leitet Home
    /// Assistant aus dem Alias genau die Katalog-Kennung ab — die eigene
    /// Automation des Fork ist dort also kein Fremdkörper.
    /// </remarks>
    public static string? HandgebauteDaneben(string modul, string vorlage, IEnumerable<HomeAssistantEntity> vorhanden)
    {
        var bauteil = SteuerungBauteile.FuerModul(modul)
            .FirstOrDefault(b => b.Art == BauteilArt.Automation && b.VorlagenDatei == vorlage);
        if (bauteil?.KonfigKennung is not { } kennung) return null;

        return vorhanden.FirstOrDefault(e
                => string.Equals(e.EntityId, bauteil.EntityId, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(e.KonfigKennung, kennung, StringComparison.Ordinal))
            ?.EntityId;
    }

    /// <summary>Die Fassung aus einer Beschreibung mit Herkunftsmarke, oder null.</summary>
    public static int? Fassung(string? beschreibung)
        => beschreibung is not null && FassungMuster.Match(beschreibung) is { Success: true } m
            ? int.Parse(m.Groups["fassung"].Value, System.Globalization.CultureInfo.InvariantCulture)
            : null;

    /// <summary>Die Fassung der mitgelieferten Vorlage, oder null, wenn es sie nicht gibt.</summary>
    public static int? VorlagenFassung(string modul, string vorlage)
    {
        var datei = Path.Combine(VorlagenWurzel, modul, vorlage + ".json");
        if (!File.Exists(datei)) return null;
        try
        {
            return Fassung((JsonNode.Parse(File.ReadAllText(datei)) as JsonObject)?["description"]?.GetValue<string>());
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Die Fassung, in der eine vom Fork angelegte Automation in Home Assistant
    /// steht — null, wenn sie nicht zu lesen ist oder keine Marke trägt.
    /// </summary>
    public static async Task<int?> FassungInHaAsync(HttpClient client, string kennung, CancellationToken ct)
    {
        try
        {
            using var antwort = await client.GetAsync($"{ConfigPfad}/{Uri.EscapeDataString(kennung)}", ct);
            if (!antwort.IsSuccessStatusCode) return null;
            var config = JsonNode.Parse(await antwort.Content.ReadAsStringAsync(ct)) as JsonObject;
            return Fassung(Text(config?["description"]));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
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
    /// <remarks>Auch die Absicherung (<see cref="SteuerungAbsicherungService"/>) sichert hierüber.</remarks>
    /// <returns>True, wenn der alte Stand wirklich auf der Platte liegt.</returns>
    public async Task<bool> SichernAsync(HttpClient client, string kennung, CancellationToken ct)
    {
        try
        {
            var antwort = await client.GetAsync($"{ConfigPfad}/{kennung}", ct);
            if (!antwort.IsSuccessStatusCode) return false;

            Directory.CreateDirectory(SicherungsOrdner);
            var ziel = Path.Combine(SicherungsOrdner, $"{kennung}-{DateTime.UtcNow:yyyyMMddHHmmss}.json");
            await File.WriteAllTextAsync(ziel, await antwort.Content.ReadAsStringAsync(ct), ct);
            _log.LogInformation("Alter Stand von {Kennung} gesichert: {Ziel}", kennung, ziel);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Sicherung von {Kennung} fehlgeschlagen.", kennung);
            return false;
        }
    }

    /// <summary>Schreiben und nachsehen, ob es angekommen ist.</summary>
    /// <remarks>
    /// Bleibt die Antwort auf das Schreiben aus, heißt das nicht „abgelehnt":
    /// Home Assistant lädt beim Speichern alle Automationen neu und kann dabei
    /// länger brauchen als die Frist. Dann wird nachgelesen, ob genau das
    /// Geschickte dasteht.
    /// </remarks>
    public static async Task<string?> SchreibenAsync(
        HttpClient client, string kennung, JsonObject config, CancellationToken ct)
    {
        try
        {
            HttpResponseMessage antwort;
            try
            {
                antwort = await client.PostAsJsonAsync($"{ConfigPfad}/{kennung}", config, ct);
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException && !ct.IsCancellationRequested)
            {
                return await StehtDaAsync(client, kennung, config, ct)
                    ? null
                    : "Home Assistant hat auf das Schreiben nicht rechtzeitig geantwortet, und die Automation steht "
                      + "danach nicht in der geschickten Fassung da. Bitte in Home Assistant nachsehen.";
            }

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

    /// <summary>
    /// Steht die Automation genau so in Home Assistant, wie sie geschickt wurde?
    /// Home Assistant speichert den Rumpf unverändert und setzt nur <c>id</c> davor.
    /// </summary>
    private static async Task<bool> StehtDaAsync(HttpClient client, string kennung, JsonObject config, CancellationToken ct)
    {
        try
        {
            using var antwort = await client.GetAsync($"{ConfigPfad}/{Uri.EscapeDataString(kennung)}", ct);
            if (!antwort.IsSuccessStatusCode) return false;
            if (JsonNode.Parse(await antwort.Content.ReadAsStringAsync(ct)) is not JsonObject gelesen) return false;
            gelesen.Remove("id");
            var geschickt = (JsonObject)config.DeepClone();
            geschickt.Remove("id");
            return JsonNode.DeepEquals(gelesen, geschickt);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return false;
        }
    }
}
