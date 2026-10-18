using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GrowOsAccess;
using ModelContextProtocol.Server;

namespace GrowMcp.Tools;

/// <summary>
/// Fork AI (Paket C1, 18.10.2026): <c>pflanze_aendern</c> und <c>probelauf_abbrechen</c>.
/// </summary>
/// <remarks>
/// <para><b>Pflanze:</b> <c>PUT /api/plants/{id}</c> ersetzt die ganze Pflanze. Das Werkzeug liest sie, ändert nur die
/// genannten Felder, schickt den Rest unverändert zurück und vergleicht danach alle übrigen Felder. Weicht etwas ab,
/// schreibt es den vorherigen Stand zurück (Muster <c>zelt_aufstellraum_setzen</c>).</para>
/// <para><b>Topf und Name:</b> Wie in der Oberfläche (<c>GrowPlantsCard.tsx</c>, <c>pflanzen-namen.ts</c>) zieht ein
/// automatischer Name („Pflanze 3") beim Topfwechsel mit; ein selbst vergebener bleibt. Die Regel steht dort in
/// TypeScript — <c>PaketC1WerkzeugeTests.Die_Namensregel_ist_die_der_Oberflaeche</c> hält diese Kopie gegen sie.</para>
/// </remarks>
public sealed partial class SchreibWerkzeuge
{
    /// <summary>Wie <c>istAutomatischerName</c> in <c>GrowDiary.React/src/features/grow-detail/pflanzen-namen.ts</c>.</summary>
    public const string AutomatischerNameMuster = @"^\s*(Pflanze|Topf)\s*(\d+)?\s*$";

    /// <summary>Die Felder von <c>UpdatePlantInstanceRequest</c> — was ein PUT annimmt.</summary>
    public static readonly string[] PflanzenFelder =
        ["strainId", "setupId", "growId", "siteIndex", "parentPlantId", "label", "plantRole", "plantStatus", "phenoLabel", "startedAt", "endedAt", "notes"];

    /// <summary>Was Grow OS selbst führt oder ableitet — kein Vergleich.</summary>
    private static readonly string[] PflanzenAbgeleitet = ["updatedAtUtc", "createdAtUtc", "strainName"];

    private static readonly (string Wert, string[] Woerter)[] PflanzenStatus =
    [
        ("Planned", ["geplant", "planned"]),
        ("Active", ["aktiv", "active", "läuft", "laeuft"]),
        ("Archived", ["archiviert", "archived", "archiv"]),
        ("Culled", ["raus", "ausgemustert", "verworfen", "entsorgt", "culled", "zwitter"]),
        ("Harvested", ["geerntet", "ernte", "harvested"]),
    ];

    public static bool IstAutomatischerName(string? name)
        => name is null || Regex.IsMatch(name, AutomatischerNameMuster, RegexOptions.IgnoreCase) || name.Trim() == "Pflanze ohne Topf";

    public static string PflanzenName(int? topf) => topf is null ? "Pflanze ohne Topf" : $"Pflanze {topf}";

    [McpServerTool(Name = "pflanze_aendern")]
    [Description($"Ändert an einer Pflanze nur die genannten Angaben: status (aktiv, raus/ausgemustert, geerntet, archiviert, geplant), sorte (Name oder Id), topf (Nummer, „kein\"), name oder notiz. Alles andere bleibt — nach dem Speichern per Prüfsumme belegt, bei Abweichung wird zurückgeschrieben. Ein automatischer Name („Pflanze 3\") zieht beim Topfwechsel mit, ein eigener bleibt. Pflanzen-Ids aus pflanzen. {StufeGrowPlanen}")]
    [BrauchtForkSchluessel(Stufen.GrowPlanen)]
    public async Task<string> PflanzeAendernAsync(
        [Description("Die Id der Pflanze (aus pflanzen)")] int pflanzeId,
        [Description("Neuer Status: aktiv, raus (ausgemustert), geerntet, archiviert oder geplant")][Beispiel("raus")] string? status = null,
        [Description("Neue Sorte: Name oder Id")] string? sorte = null,
        [Description("Neuer Topf: Nummer ab 1, oder „kein\"")] string? topf = null,
        [Description("Neuer Name der Pflanze")] string? name = null,
        [Description("Neue Notiz (ersetzt die alte)")] string? notiz = null,
        CancellationToken cancellationToken = default)
    {
        if (!reader.HatForkSchluessel) return ForkFehler.SchluesselNoetig;
        if (status is null && sorte is null && topf is null && name is null && notiz is null)
        {
            return "Nichts geändert: es wurde keine Angabe genannt (status, sorte, topf, name oder notiz).";
        }

        var gelesen = await SendenAsync(HttpMethod.Get, $"api/plants/{pflanzeId}", null, cancellationToken);
        if (gelesen.Fehler is { } lf) return lf;
        if (!gelesen.Antwort!.Erfolg) return ForkFehler.Text(gelesen.Antwort.Status, gelesen.Antwort.Text, "Pflanze lesen");
        JsonObject vorher;
        try
        {
            vorher = JsonNode.Parse(gelesen.Antwort.Text) as JsonObject ?? throw new JsonException();
        }
        catch (JsonException)
        {
            return AndersAlsErwartet;
        }

        // Der Rumpf: genau die Felder des PUT, aus dem gelesenen Stand (fehlende Felder schickt Grow OS als null zurück).
        var rumpf = new JsonObject();
        foreach (var feld in PflanzenFelder) rumpf[feld] = vorher[feld]?.DeepClone();
        var geaendert = new List<string>();
        var alterName = vorher["label"]?.GetValue<string>();

        if (status is not null)
        {
            if (Auswahl(PflanzenStatus, status) is not { } wert) return $"Nichts geändert: „{status}\" ist kein Status. Möglich: aktiv, raus, geerntet, archiviert, geplant.";
            rumpf["plantStatus"] = wert;
            geaendert.Add("plantStatus");
        }

        if (sorte is not null)
        {
            var sorten = await SendenAsync(HttpMethod.Get, "api/strains", null, cancellationToken);
            if (sorten.Fehler is { } sf) return sf;
            if (!sorten.Antwort!.Erfolg) return ForkFehler.Text(sorten.Antwort.Status, sorten.Antwort.Text, "Sorten lesen");
            using var liste = JsonDocument.Parse(sorten.Antwort.Text);
            var (sortenId, fehler) = SorteFinden(liste.RootElement, sorte);
            if (fehler is not null) return fehler;
            rumpf["strainId"] = sortenId;
            geaendert.Add("strainId");
        }

        if (topf is not null)
        {
            int? neu;
            if (KeinRaumWoerter.Contains(topf.Trim().ToLowerInvariant())) neu = null;
            else if (int.TryParse(topf.Trim(), out var nummer) && nummer >= 1) neu = nummer;
            else return $"Nichts geändert: „{topf}\" ist kein Topf. Eine Nummer ab 1 oder „kein\".";
            rumpf["siteIndex"] = neu;
            geaendert.Add("siteIndex");
            if (name is null && IstAutomatischerName(alterName))
            {
                rumpf["label"] = PflanzenName(neu);
                geaendert.Add("label");
            }
        }

        if (name is not null)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Nichts geändert: ein leerer Name geht nicht.";
            rumpf["label"] = name.Trim();
            if (!geaendert.Contains("label")) geaendert.Add("label");
        }

        if (notiz is not null)
        {
            rumpf["notes"] = string.IsNullOrWhiteSpace(notiz) ? null : notiz.Trim();
            geaendert.Add("notes");
        }

        var gespeichert = await SendenAsync(HttpMethod.Put, $"api/plants/{pflanzeId}", rumpf, cancellationToken);
        if (gespeichert.Fehler is { } pf) return pf;
        if (!gespeichert.Antwort!.Erfolg) return ForkFehler.Text(gespeichert.Antwort.Status, gespeichert.Antwort.Text, "Pflanze ändern");

        // Gegenprobe am gespeicherten Stand.
        var danach = await SendenAsync(HttpMethod.Get, $"api/plants/{pflanzeId}", null, cancellationToken);
        JsonObject? nachher = null;
        try
        {
            if (danach.Antwort is { Erfolg: true } ok) nachher = JsonNode.Parse(ok.Text) as JsonObject;
        }
        catch (JsonException)
        {
        }

        var titel = $"Pflanze {pflanzeId} „{alterName}\"" + (vorher["siteIndex"] is JsonValue t ? $" (Topf {t})" : string.Empty);
        if (nachher is null)
        {
            return $"{titel} wurde gespeichert, aber der Stand danach ließ sich nicht lesen — die Prüfsumme fehlt. Bitte mit pflanzen nachsehen.";
        }

        var ausgenommen = geaendert.Concat(PflanzenAbgeleitet).ToHashSet(StringComparer.Ordinal);
        var abweichend = vorher.Select(p => p.Key).Concat(nachher.Select(p => p.Key)).Distinct()
            .Where(n => !ausgenommen.Contains(n) && !JsonNode.DeepEquals(vorher[n], nachher[n]))
            .ToList();
        if (abweichend.Count > 0)
        {
            var zurueck = new JsonObject();
            foreach (var feld in PflanzenFelder) zurueck[feld] = vorher[feld]?.DeepClone();
            var rueck = await SendenAsync(HttpMethod.Put, $"api/plants/{pflanzeId}", zurueck, cancellationToken);
            // Nachlesen, ob der alte Stand wirklich wieder da ist — „200" allein ist kein Beleg.
            var kontrolle = await SendenAsync(HttpMethod.Get, $"api/plants/{pflanzeId}", null, cancellationToken);
            JsonObject? wieder = null;
            try
            {
                if (kontrolle.Antwort is { Erfolg: true } k) wieder = JsonNode.Parse(k.Text) as JsonObject;
            }
            catch (JsonException)
            {
            }
            var wiederGleich = wieder is not null && PflanzenFelder.All(f => JsonNode.DeepEquals(vorher[f], wieder[f]));
            return $"ACHTUNG: Beim Speichern von {titel} haben sich außer {string.Join(", ", geaendert)} weitere Angaben verändert ({string.Join(", ", abweichend)}). "
                   + (rueck.Antwort is { Erfolg: true } && wiederGleich
                       ? "Der vorherige Stand wurde zurückgeschrieben und nachgelesen — er ist wieder da."
                       : "Das Zurückschreiben ist NICHT belegt — bitte sofort in Grow OS nachsehen.");
        }

        var aenderungen = geaendert.Select(f => $"{FeldName(f)} {Anzeige(vorher, f)} → {Anzeige(nachher, f)}");
        var uebrige = vorher.Select(p => p.Key).Count(n => !ausgenommen.Contains(n));
        return $"{titel}: {string.Join("; ", aenderungen)}. Alle anderen {uebrige} Angaben unverändert (Prüfsumme {PflanzenPruefsumme(nachher, ausgenommen)} vorher und nachher gleich).";
    }

    private static string FeldName(string feld) => feld switch
    {
        "plantStatus" => "Status",
        "strainId" => "Sorte",
        "siteIndex" => "Topf",
        "label" => "Name",
        "notes" => "Notiz",
        _ => feld,
    };

    private static string Anzeige(JsonObject pflanze, string feld)
    {
        if (feld == "strainId" && pflanze["strainName"] is JsonValue sorte) return $"„{sorte}\"";
        return pflanze[feld] switch
        {
            null => "leer",
            JsonValue v when feld == "plantStatus" => StatusWort(v.ToString()),
            JsonValue v when v.TryGetValue<string>(out var s) => $"„{s}\"",
            var v => v.ToJsonString(),
        };
    }

    private static string StatusWort(string status) => status switch
    {
        "Planned" => "Geplant",
        "Active" => "Aktiv",
        "Archived" => "Archiviert",
        "Culled" => "Ausgemustert",
        "Harvested" => "Geerntet",
        _ => status,
    };

    private static string PflanzenPruefsumme(JsonObject pflanze, HashSet<string> ohne)
    {
        var rest = new JsonObject();
        foreach (var (n, w) in pflanze.Where(p => !ohne.Contains(p.Key)).OrderBy(p => p.Key, StringComparer.Ordinal)) rest[n] = w?.DeepClone();
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rest.ToJsonString(Ausgabe))))[..8].ToLowerInvariant();
    }

    /// <summary>Eine Sorte per Id oder Name — mehrdeutig heißt Liste, nicht raten.</summary>
    public static (int? Id, string? Fehler) SorteFinden(JsonElement liste, string gesagt)
    {
        var alle = liste.ValueKind == JsonValueKind.Array
            ? liste.EnumerateArray().Where(s => s.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number).ToList()
            : [];
        string Aufzaehlung(IEnumerable<JsonElement> s) => string.Join("; ", s.Select(x => $"{x.GetProperty("id").GetInt32()} {Text(x, "name")}"));
        var wort = gesagt.Trim();
        if (int.TryParse(wort, out var nummer))
        {
            return alle.Any(s => s.GetProperty("id").GetInt32() == nummer) ? (nummer, null) : (null, $"Nichts geändert: eine Sorte mit der Id {nummer} gibt es nicht. Vorhanden: {Aufzaehlung(alle)}.");
        }

        var gesucht = GrowTools.Vereinfachen(wort);
        var genau = alle.Where(s => GrowTools.Vereinfachen(Text(s, "name") ?? string.Empty) == gesucht).ToList();
        if (genau.Count == 1) return (genau[0].GetProperty("id").GetInt32(), null);
        var aehnlich = alle.Where(s => GrowTools.Vereinfachen(Text(s, "name") ?? string.Empty).Contains(gesucht, StringComparison.Ordinal)).ToList();
        return aehnlich.Count switch
        {
            1 => (aehnlich[0].GetProperty("id").GetInt32(), null),
            0 => (null, $"Nichts geändert: keine Sorte passt zu „{wort}\". Vorhanden: {Aufzaehlung(alle)}."),
            _ => (null, $"Nichts geändert: „{wort}\" passt auf mehrere Sorten — bitte genauer oder mit Id: {Aufzaehlung(aehnlich)}."),
        };
    }

    // ------------------------------------------------------------------ Probelauf

    [McpServerTool(Name = "probelauf_abbrechen")]
    [Description($"Bricht einen laufenden Probelauf ab und stellt die Regelung zurück — ein Not-Aus wie pumpe_stoppen, er scheitert nie am Höchstwert der Schaltbefehle. Ohne Id wird der eine laufende Probelauf abgebrochen; laufen mehrere, kommt die Liste. Welche laufen, zeigt steuerung. {StufeSchalten}")]
    [BrauchtForkSchluessel(Stufen.GeraeteSchalten)]
    public async Task<string> ProbelaufAbbrechenAsync(
        [Description("Die Id des Probelaufs; weglassen = der eine, der gerade läuft")] long? probelaufId = null,
        CancellationToken cancellationToken = default)
    {
        if (!reader.HatForkSchluessel) return ForkFehler.SchluesselNoetig;

        var id = probelaufId;
        if (id is null)
        {
            var liste = await SendenAsync(HttpMethod.Get, "api/steuerung/probelauf", null, cancellationToken);
            if (liste.Fehler is { } lf) return lf;
            if (!liste.Antwort!.Erfolg) return ForkFehler.Text(liste.Antwort.Status, liste.Antwort.Text, "Probeläufe lesen");
            List<(long Id, string Titel)> laufend;
            try
            {
                using var doc = JsonDocument.Parse(liste.Antwort.Text);
                laufend = doc.RootElement.EnumerateArray()
                    .Where(l => Text(l, "status") == "Laeuft")
                    .Select(l => (l.GetProperty("id").GetInt64(), Text(l, "modulTitel") ?? Text(l, "modul") ?? "?"))
                    .ToList();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
            {
                return AndersAlsErwartet;
            }

            if (laufend.Count == 0) return "Es läuft kein Probelauf — nichts abzubrechen.";
            if (laufend.Count > 1) return "Es laufen mehrere Probeläufe — bitte mit Id: " + string.Join("; ", laufend.Select(l => $"{l.Id} ({l.Titel})")) + ".";
            id = laufend[0].Id;
        }

        return await SchreibenAsync(HttpMethod.Post, $"api/steuerung/probelauf/{id}/abbrechen", null, "Probelauf abbrechen",
            json => $"Probelauf {id} ({Text(json, "modulTitel")}) abgebrochen, Status jetzt: {Text(json, "status")}."
                    + (Text(json, "abbruchGrund") is { } grund ? $" Grund: {grund}" : string.Empty)
                    + " Die Regelung ist zurückgestellt; der Nachlauf wird noch aufgezeichnet.", cancellationToken, mitRumpf: false);
    }
}
