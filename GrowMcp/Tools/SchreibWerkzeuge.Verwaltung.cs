using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrowOsAccess;
using ModelContextProtocol.Server;

namespace GrowMcp.Tools;

/// <summary>
/// Raum und Aufstellraum der Zelte — die ersten Werkzeuge der Stufe Verwaltung (A-027, Schritt 5).
/// </summary>
/// <remarks>
/// <para>Feste Werkzeuge mit eigener Prüfung (Prüfsumme). Seit Paket D (18.10.2026) erreicht <c>fork_ausfuehren</c>
/// jede andere Einstellung; Schlüsselverwaltung und KI-Hauptschalter bleiben ausgeschlossen (<c>[KeinKiZugriff]</c>
/// in Grow OS) und haben auch dort keinen Weg.</para>
/// <para>Wie überall entscheidet Grow OS, ob die Stufe Verwaltung frei ist; die Werkzeuge nennen sie nur.</para>
/// </remarks>
public sealed partial class SchreibWerkzeuge
{
    private const string StufeVerwaltung = $"Braucht Stufe „{Stufen.VerwaltungText}\".";

    private static readonly string[] KeinRaumWoerter = ["keiner", "kein", "keinen", "ohne", "ohne raum", "none"];

    /// <summary>Felder der Zelt-Antwort, die Grow OS selbst zählt und die ein Speichern nicht annimmt.</summary>
    private static readonly string[] ZeltZaehlfelder = ["id", "activeGrowCount", "archivedGrowCount", "activeSetupCount", "archivedSetupCount"];

    /// <summary>
    /// Felder, die beim Speichern eines Zelts <b>weggelassen</b> werden, damit Grow OS sie nicht anfasst:
    /// Sensoren (sonst bekommen alle Zeilen neue Ids und die Verweise darauf werden gelöst) und Kameras
    /// (Grow OS behält ohne Angabe Liste und Einzelfeld).
    /// </summary>
    private static readonly string[] ZeltUnberuehrt = ["sensors", "cameras", "cameraEntityId"];

    // ------------------------------------------------------------------ Raum lesen

    [McpServerTool(Name = "raum")]
    [Description("Der Raum, in dem die Zelte stehen: sein Name (Vorgabe „Aufstellraum\"), seine Id und die Zelte darin. Dazu, wenn der Schlüssel die Stufe Verwaltung hat, alle Zelte der Anlage mit ihrer Zuordnung. Zuerst aufrufen, bevor raum_setzen oder zelt_aufstellraum_setzen etwas ändert.")]
    [BrauchtForkSchluessel]
    public async Task<string> RaumAsync(CancellationToken cancellationToken = default)
    {
        if (!reader.HatForkSchluessel) return ForkFehler.SchluesselNoetig;

        var antwort = await SendenAsync(HttpMethod.Get, "api/steuerung/raum", null, cancellationToken);
        if (antwort.Fehler is { } fehler) return fehler;
        if (!antwort.Antwort!.Erfolg) return ForkFehler.Text(antwort.Antwort.Status, antwort.Antwort.Text, "Raum lesen");

        JsonDocument raum;
        try
        {
            raum = JsonDocument.Parse(antwort.Antwort.Text);
        }
        catch (JsonException)
        {
            return AndersAlsErwartet;
        }

        using (raum)
        {
            // Die Liste aller Zelte liegt hinter der Verwaltung; ohne die Stufe fehlt nur diese Zeile.
            var zelte = await SendenAsync(HttpMethod.Get, "api/settings/tents", null, cancellationToken);
            JsonDocument? alleZelte = null;
            try
            {
                if (zelte.Antwort is { Erfolg: true } liste) alleZelte = JsonDocument.Parse(liste.Text);
            }
            catch (JsonException)
            {
            }

            using (alleZelte)
            {
                return RaumBeschreiben(raum.RootElement, alleZelte?.RootElement);
            }
        }
    }

    /// <summary>Die Antwort von <c>GET api/steuerung/raum</c> (und, wenn vorhanden, der Zeltliste) in Klartext.</summary>
    public static string RaumBeschreiben(JsonElement raum, JsonElement? alleZelte)
    {
        var name = Text(raum, "name") ?? "?";
        var text = new StringBuilder();
        text.AppendLine($"Raum „{name}\" (Id {Text(raum, "id") ?? "?"}, Vorgabe: {Text(raum, "vorgabe") ?? "?"}).");

        var darin = ZeltListe(raum, "zelte");
        text.AppendLine(darin.Count == 0
            ? "Zelte darin: keine."
            : "Zelte darin: " + string.Join("; ", darin.Select(z => $"Zelt {z.Id} „{z.Name}\"")) + ".");

        if (alleZelte is { ValueKind: JsonValueKind.Array } liste)
        {
            var imRaum = darin.Select(z => z.Id).ToHashSet();
            var beschreibung = liste.EnumerateArray()
                .Where(z => z.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number)
                .Select(z =>
                {
                    var id = z.GetProperty("id").GetInt32();
                    return $"Zelt {id} „{Text(z, "name")}\" ({(imRaum.Contains(id) ? "im Raum" : "ohne Raum")})";
                })
                .ToList();
            if (beschreibung.Count > 0) text.AppendLine("Zelte der Anlage: " + string.Join("; ", beschreibung) + ".");
        }
        else
        {
            text.AppendLine("Die Liste aller Zelte steht erst mit der Stufe „Verwaltung\" zur Verfügung.");
        }

        return text.ToString().TrimEnd();
    }

    private static List<(int Id, string Name)> ZeltListe(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var liste) && liste.ValueKind == JsonValueKind.Array
            ? liste.EnumerateArray()
                .Where(z => z.ValueKind == JsonValueKind.Object && z.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number)
                .Select(z => (z.GetProperty("id").GetInt32(), Text(z, "name") ?? "?"))
                .ToList()
            : [];

    // ------------------------------------------------------------------ Raumname

    [McpServerTool(Name = "raum_setzen")]
    [Description($"Ändert den Namen des Raums, in dem die Zelte stehen (etwa „Keller\"). Ein leerer Text = zurück auf die Vorgabe „Aufstellraum\". Der Name erscheint in Zuluft-Zustand und Entfeuchter-Bremse. Ändert sonst nichts. {StufeVerwaltung}")]
    [BrauchtForkSchluessel(Stufen.Verwaltung)]
    public async Task<string> RaumSetzenAsync(
        [Description("Der neue Name, höchstens 60 Zeichen. Nur ein ausdrücklich leerer Text (\"\") setzt auf die Vorgabe zurück.")][Beispiel("Keller")] string name,
        CancellationToken cancellationToken = default)
    {
        if (!reader.HatForkSchluessel) return ForkFehler.SchluesselNoetig;

        var vorher = await RaumLesenAsync(cancellationToken);
        if (vorher.Fehler is { } fehler) return fehler;
        var (alterName, vorgabe, _) = vorher.Raum;

        var neu = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        var rumpf = new JsonObject { ["name"] = neu };

        return await SchreibenAsync(HttpMethod.Put, "api/steuerung/raum", rumpf, "Raumname setzen",
            json =>
            {
                var jetzt = Text(json, "name") ?? neu ?? vorgabe;
                if (neu is null) return $"Raumname von „{alterName}\" auf die Vorgabe „{jetzt}\" zurückgesetzt. Grow OS hat gespeichert:";
                return string.Equals(alterName, jetzt, StringComparison.Ordinal)
                    ? $"Der Raum hieß schon „{jetzt}\"; nichts verändert. Grow OS hat gespeichert:"
                    : $"Raumname von „{alterName}\" auf „{jetzt}\" gesetzt. Grow OS hat gespeichert:";
            }, cancellationToken);
    }

    // ------------------------------------------------------------------ Zelt ↔ Raum

    [McpServerTool(Name = "zelt_aufstellraum_setzen")]
    [Description($"Ordnet ein Zelt dem Raum zu (es steht dann im Raum und zieht seine Luft von dort) oder löst die Zuordnung. Ändert NUR diese eine Angabe des Zelts — alles andere (Sensoren, Kameras, Geräte, Maße) bleibt; das wird nach dem Speichern per Prüfsumme belegt, und weicht etwas ab, wird zurückgesetzt. Zeltliste und Raumname stehen in raum. {StufeVerwaltung}")]
    [BrauchtForkSchluessel(Stufen.Verwaltung)]
    public async Task<string> ZeltAufstellraumSetzenAsync(
        [Description("Die Id des Zelts (aus raum)")] int zeltId,
        [Description("Der Name des Raums (wie in raum angezeigt) oder seine Id; „keiner\" löst die Zuordnung.")][Beispiel("Aufstellraum")] string raum,
        CancellationToken cancellationToken = default)
    {
        if (!reader.HatForkSchluessel) return ForkFehler.SchluesselNoetig;

        var gelesen = await RaumLesenAsync(cancellationToken);
        if (gelesen.Fehler is { } raumFehler) return raumFehler;
        var (raumName, _, raumId) = gelesen.Raum;

        string? ziel;
        var wunsch = (raum ?? string.Empty).Trim();
        if (KeinRaumWoerter.Contains(wunsch.ToLowerInvariant()))
        {
            ziel = null;
        }
        else if (string.Equals(wunsch, raumName, StringComparison.OrdinalIgnoreCase) || string.Equals(wunsch, raumId, StringComparison.OrdinalIgnoreCase))
        {
            ziel = raumId;
        }
        else
        {
            return $"Nichts geändert: „{wunsch}\" ist nicht der Raum. Der Raum heißt „{raumName}\" (Id {raumId}) — oder „keiner\".";
        }

        var zelt = await SendenAsync(HttpMethod.Get, $"api/settings/tents/{zeltId}", null, cancellationToken);
        if (zelt.Fehler is { } zeltFehler) return zeltFehler;
        if (zelt.Antwort!.Status == 404) return await ZeltUnbekanntAsync(zeltId, cancellationToken);
        if (!zelt.Antwort.Erfolg) return ForkFehler.Text(zelt.Antwort.Status, zelt.Antwort.Text, "Zelt lesen");

        JsonObject vorher;
        try
        {
            vorher = JsonNode.Parse(zelt.Antwort.Text) as JsonObject ?? throw new JsonException();
        }
        catch (JsonException)
        {
            return AndersAlsErwartet;
        }

        var zeltName = Text(JsonSerializer.SerializeToElement(vorher), "name") ?? $"Zelt {zeltId}";
        var alt = vorher["raumId"]?.GetValue<string>();
        var wohin = ziel is null ? "keinem Raum" : $"dem Raum „{raumName}\"";
        if (string.Equals(alt, ziel, StringComparison.Ordinal))
        {
            return $"Nichts geändert: Zelt {zeltId} „{zeltName}\" ist schon {(ziel is null ? "keinem Raum zugeordnet" : $"im Raum „{raumName}\"")}.";
        }

        // Grow OS ersetzt beim Speichern das GANZE Zelt (PUT). Deshalb den Ist-Stand schicken, nur mit anderer raumId.
        // Sensoren und Kameras weglassen: ohne Angabe fasst Grow OS sie nicht an (mit Angabe bekämen die Sensoren neue Ids).
        var rumpf = (JsonObject)vorher.DeepClone();
        foreach (var feld in ZeltZaehlfelder.Concat(ZeltUnberuehrt)) rumpf.Remove(feld);
        rumpf["raumId"] = ziel;

        var gespeichert = await SendenAsync(HttpMethod.Put, $"api/settings/tents/{zeltId}", rumpf, cancellationToken);
        if (gespeichert.Fehler is { } speichernFehler) return speichernFehler;
        if (!gespeichert.Antwort!.Erfolg) return ForkFehler.Text(gespeichert.Antwort.Status, gespeichert.Antwort.Text, "Zelt-Raum setzen");

        // Gegenprobe am gespeicherten Stand: alles außer raumId muss gleich sein.
        var danach = await SendenAsync(HttpMethod.Get, $"api/settings/tents/{zeltId}", null, cancellationToken);
        JsonObject? nachher = null;
        try
        {
            if (danach.Antwort is { Erfolg: true } ok) nachher = JsonNode.Parse(ok.Text) as JsonObject;
        }
        catch (JsonException)
        {
        }

        if (nachher is null)
        {
            return $"Zelt {zeltId} „{zeltName}\" wurde gespeichert, aber der Stand danach ließ sich nicht lesen — die Prüfsumme fehlt. Bitte mit raum und in Grow OS nachsehen.";
        }

        var abweichend = AbweichendeFelder(vorher, nachher);
        if (abweichend.Count > 0)
        {
            // Nicht stehen lassen: den vorherigen Stand zurückschreiben.
            var zurueck = (JsonObject)vorher.DeepClone();
            foreach (var feld in ZeltZaehlfelder.Concat(ZeltUnberuehrt)) zurueck.Remove(feld);
            var rueck = await SendenAsync(HttpMethod.Put, $"api/settings/tents/{zeltId}", zurueck, cancellationToken);
            var zurueckgesetzt = rueck.Antwort is { Erfolg: true };
            // Sensoren und Kameras schreibt das Zurückschreiben nicht (es lässt sie wie das erste Speichern weg).
            var nichtZurueck = abweichend.Intersect(ZeltUnberuehrt).ToList();
            return $"ACHTUNG: Beim Speichern von Zelt {zeltId} „{zeltName}\" haben sich außer der Raumzuordnung weitere Angaben verändert ({string.Join(", ", abweichend)}). "
                   + (nichtZurueck.Count > 0
                       ? $"Davon lassen sich {string.Join(", ", nichtZurueck)} NICHT automatisch zurückschreiben — bitte sofort in Grow OS prüfen und von Hand wiederherstellen."
                       : zurueckgesetzt
                       ? "Der vorherige Stand wurde zurückgeschrieben; bitte mit raum und in Grow OS nachsehen, ob alles wieder stimmt (Sensoren-Zuordnung prüfen)."
                       : "Das Zurückschreiben des vorherigen Stands ist NICHT gelungen — bitte sofort in Grow OS nachsehen.");
        }

        var summe = Pruefsumme(nachher);
        return $"Zelt {zeltId} „{zeltName}\" ist jetzt {(ziel is null ? "keinem Raum zugeordnet" : $"im Raum „{raumName}\"")} (vorher: {(alt is null ? "ohne Raum" : "im Raum")}). "
               + $"Alle anderen Zelt-Einstellungen sind unverändert (Prüfsumme {summe} vorher und nachher gleich). Grow OS hat gespeichert:"
               + Environment.NewLine
               + new JsonObject { ["id"] = zeltId, ["name"] = zeltName, ["raumId"] = nachher["raumId"]?.DeepClone() }.ToJsonString(Ausgabe);
    }

    private async Task<string> ZeltUnbekanntAsync(int zeltId, CancellationToken cancellationToken)
    {
        var liste = await SendenAsync(HttpMethod.Get, "api/settings/tents", null, cancellationToken);
        var ids = new List<int>();
        try
        {
            if (liste.Antwort is { Erfolg: true } ok)
            {
                using var doc = JsonDocument.Parse(ok.Text);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    ids = doc.RootElement.EnumerateArray()
                        .Where(z => z.ValueKind == JsonValueKind.Object && z.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number)
                        .Select(z => z.GetProperty("id").GetInt32())
                        .ToList();
                }
            }
        }
        catch (JsonException)
        {
        }

        return $"Nichts geändert: Es gibt kein Zelt mit der Id {zeltId}."
               + (ids.Count > 0 ? $" Möglich: {string.Join(", ", ids)}." : " Mit raum bekommst du die Zelte.");
    }

    /// <summary>Die Felder (außer <c>raumId</c>), in denen sich zwei Stände eines Zelts unterscheiden.</summary>
    public static List<string> AbweichendeFelder(JsonObject vorher, JsonObject nachher)
    {
        var namen = vorher.Select(p => p.Key).Concat(nachher.Select(p => p.Key)).Distinct(StringComparer.Ordinal);
        return namen
            .Where(n => !string.Equals(n, "raumId", StringComparison.Ordinal) && !ZeltZaehlfelder.Contains(n))
            .Where(n => !JsonNode.DeepEquals(vorher[n], nachher[n]))
            .ToList();
    }

    /// <summary>Kurze Prüfsumme über alle Felder außer <c>raumId</c>, nach Namen geordnet.</summary>
    public static string Pruefsumme(JsonObject zelt)
    {
        var ohne = new JsonObject();
        foreach (var feld in zelt.Where(p => p.Key != "raumId" && !ZeltZaehlfelder.Contains(p.Key)).OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            ohne[feld.Key] = feld.Value?.DeepClone();
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ohne.ToJsonString(Ausgabe))))[..8].ToLowerInvariant();
    }

    private readonly record struct RaumStand(string Name, string Vorgabe, string Id);

    private async Task<(RaumStand Raum, string? Fehler)> RaumLesenAsync(CancellationToken cancellationToken)
    {
        var antwort = await SendenAsync(HttpMethod.Get, "api/steuerung/raum", null, cancellationToken);
        if (antwort.Fehler is { } fehler) return (default, fehler);
        if (!antwort.Antwort!.Erfolg) return (default, ForkFehler.Text(antwort.Antwort.Status, antwort.Antwort.Text, "Raum lesen"));

        try
        {
            using var json = JsonDocument.Parse(antwort.Antwort.Text);
            var name = Text(json.RootElement, "name");
            var id = Text(json.RootElement, "id");
            if (name is null || id is null) return (default, AndersAlsErwartet);
            return (new RaumStand(name, Text(json.RootElement, "vorgabe") ?? name, id), null);
        }
        catch (JsonException)
        {
            return (default, AndersAlsErwartet);
        }
    }
}
