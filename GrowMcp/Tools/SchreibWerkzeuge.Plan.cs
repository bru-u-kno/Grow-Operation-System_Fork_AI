using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GrowOsAccess;
using ModelContextProtocol.Server;

namespace GrowMcp.Tools;

/// <summary>
/// Eine Meldegrenze und die Schaltzeiten des Lichtplans ändern (forkai.247, Stufe Grow planen).
/// </summary>
/// <remarks>
/// <para>Beide Wege in Cultivate Control ersetzen beim Speichern das GANZE Gebilde: <c>PUT api/alerts/tents/{id}</c> alle Regeln des Zelts,
/// <c>PUT api/light-schedules/{id}</c> den ganzen Plan. Deshalb wie bei <c>zelt_aufstellraum_setzen</c>: Ist-Stand lesen, genau
/// ein Feld ändern, zurückschicken, danach neu lesen und alles andere per Prüfsumme als gleich belegen.</para>
/// <para><b>Keine eigenen Grenzen.</b> Ob eine Meldegrenze physikalisch möglich ist, ob Unter- und Obergrenze vertauscht sind,
/// ob An- und Aus-Zeit gleich sind — das prüft Cultivate Control beim Speichern selbst (<c>MeasurementSanityService.PhysikGrenze</c>,
/// <c>LightSchedulesApiController.Validate</c>) und antwortet mit einem deutschen Satz. Der kommt unverändert zurück.</para>
/// </remarks>
public sealed partial class SchreibWerkzeuge
{
    /// <summary>Die vier Grenzen einer Regel: Feld in Cultivate Control, Anzeige, und wie man sie sagt.</summary>
    public static IReadOnlyList<(string Feld, string Anzeige, string[] Woerter)> Grenzen { get; } =
    [
        ("minValue", "min.", ["min", "minimum", "unten", "untergrenze", "untere", "unter"]),
        ("maxValue", "max.", ["max", "maximum", "oben", "obergrenze", "obere", "ober"]),
        ("nightMinValue", "nachts min.", ["nachtmin", "nachtsmin", "nachtminimum", "nachtuntergrenze", "nachtsuntergrenze"]),
        ("nightMaxValue", "nachts max.", ["nachtmax", "nachtsmax", "nachtmaximum", "nachtobergrenze", "nachtsobergrenze"]),
    ];

    // ------------------------------------------------------------------ grenzwert_setzen

    [McpServerTool(Name = "grenzwert_setzen")]
    [Description($"Ändert GENAU EINE Meldegrenze eines Zelts — etwa Lufttemp. max. oder pH min. — auf eine feste Zahl. Alle anderen Grenzen und Einstellungen bleiben; das wird nach dem Speichern per Prüfsumme belegt. Ob die Zahl physikalisch möglich ist und nicht über der Obergrenze liegt, prüft Cultivate Control selbst. Folgt die Regel dem Wochenplan, ändert das Werkzeug nichts. Die Grenzen stehen in alarme. {StufeGrowPlanen}")]
    [BrauchtForkSchluessel(Stufen.GrowPlanen)]
    public async Task<string> GrenzwertSetzenAsync(
        [Description("Die Id des Zelts")] int zeltId,
        [Description("Die Messgröße wie in der App („Lufttemp.\", „pH\", „Außen Temp.\") oder ihr Schlüssel (temperature, reservoir-ph)")][Beispiel("Lufttemp.")] string messgroesse,
        [Description("Welche Grenze: min, max, nacht min oder nacht max")][Beispiel("max")] string grenze,
        [Description("Die neue Grenze als Zahl in der Einheit der Messgröße")][Beispiel("28")] double wert,
        CancellationToken cancellationToken = default)
    {
        if (!reader.HatForkSchluessel) return ForkFehler.SchluesselNoetig;

        var welche = Grenzen.FirstOrDefault(g => g.Woerter.Contains(Glatt(grenze)) || string.Equals(g.Feld, grenze?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (welche.Feld is null) return $"Nichts geändert: „{grenze}\" ist keine Grenze. Möglich: min, max, nacht min, nacht max.";
        if (!double.IsFinite(wert)) return "Nichts geändert: die Grenze muss eine Zahl sein.";

        var katalog = await KatalogAsync(cancellationToken);
        if (katalog.Fehler is { } katalogFehler) return katalogFehler;

        var gelesen = await SendenAsync(HttpMethod.Get, $"api/alerts/tents/{zeltId}", null, cancellationToken);
        if (gelesen.Fehler is { } lesefehler) return lesefehler;
        if (gelesen.Antwort!.Status == 404) return $"Nichts geändert: Es gibt kein Zelt mit der Id {zeltId}.";
        if (!gelesen.Antwort.Erfolg) return ForkFehler.Text(gelesen.Antwort.Status, gelesen.Antwort.Text, "Meldegrenzen lesen");

        JsonArray vorher;
        try
        {
            vorher = JsonNode.Parse(gelesen.Antwort.Text)?["rules"] as JsonArray ?? throw new JsonException();
        }
        catch (JsonException)
        {
            return AndersAlsErwartet;
        }

        // Erst die Schlüssel der vorhandenen Regeln (auch solche ohne Messgröße im Katalog, etwa „power"), dann der Katalog.
        var schluessel = vorher.Select(r => r?["metricKey"]?.GetValue<string>()).OfType<string>()
            .FirstOrDefault(k => string.Equals(k, messgroesse?.Trim(), StringComparison.OrdinalIgnoreCase));
        var groesse = katalog.Liste.FirstOrDefault(m => m.Schluessel == schluessel);
        if (schluessel is null)
        {
            var (treffer, findFehler) = MessgroesseFinden(katalog.Liste, messgroesse);
            if (treffer is null) return "Nichts geändert: " + findFehler;
            groesse = treffer;
            schluessel = treffer.Schluessel;
        }

        var name = $"{groesse?.MeldeEtikett ?? groesse?.Etikett ?? schluessel} {welche.Anzeige}";
        var einheit = groesse?.Einheit is { } e ? " " + e : "";
        var index = vorher.Select((r, i) => (r, i)).Where(x => x.r?["metricKey"]?.GetValue<string>() == schluessel).Select(x => (int?)x.i).FirstOrDefault();

        var neu = (JsonArray)vorher.DeepClone();
        double? alt = null;
        if (index is { } i)
        {
            var regel = neu[i]!.AsObject();
            if (string.Equals(regel["quelle"]?.GetValue<string>(), "Plan", StringComparison.OrdinalIgnoreCase))
            {
                return $"Nichts geändert: Die Meldegrenzen für {groesse?.MeldeEtikett ?? groesse?.Etikett ?? schluessel} an Zelt {zeltId} folgen dem Wochenplan und haben keine eigene Zahl. "
                       + "Auf feste Grenzen umstellen geht in Cultivate Control auf der Seite Grenzwerte.";
            }
            alt = regel[welche.Feld]?.GetValue<double>();
            if (alt == wert) return $"Nichts geändert: {name} an Zelt {zeltId} steht schon auf {Zahl(wert)}{einheit}.";
            regel[welche.Feld] = wert;
        }
        else if (welche.Feld is "nightMinValue" or "nightMaxValue")
        {
            // Cultivate Control verwirft beim Speichern jede feste Regel ohne Tag-Grenze (AlertsApiController.SaveForTent) — eine Regel nur
            // mit Nachtgrenze käme nie an (Prüfer forkai.247).
            return $"Nichts geändert: Für {groesse?.MeldeEtikett ?? groesse?.Etikett ?? schluessel} an Zelt {zeltId} gibt es noch keine Meldegrenze. "
                   + "Eine Nachtgrenze gilt nur zusammen mit einer Tag-Grenze — zuerst min oder max setzen.";
        }
        else
        {
            // Noch keine Regel: eine neue mit genau dieser Grenze, an und mit den Vorgaben von Cultivate Control (Abstand 30 min).
            neu.Add(new JsonObject
            {
                ["metricKey"] = schluessel, ["minValue"] = null, ["maxValue"] = null, ["notifyService"] = "",
                ["enabled"] = true, ["cooldownMinutes"] = 30, ["quelle"] = "Fest", ["toleranz"] = null,
                ["nightMinValue"] = null, ["nightMaxValue"] = null, [welche.Feld] = wert,
            });
        }

        var gespeichert = await SendenAsync(HttpMethod.Put, $"api/alerts/tents/{zeltId}", new JsonObject { ["rules"] = neu }, cancellationToken);
        if (gespeichert.Fehler is { } speichernFehler) return speichernFehler;
        if (!gespeichert.Antwort!.Erfolg) return ForkFehler.Text(gespeichert.Antwort.Status, gespeichert.Antwort.Text, "Meldegrenze setzen");

        // Gegenprobe am gespeicherten Stand.
        var danach = await SendenAsync(HttpMethod.Get, $"api/alerts/tents/{zeltId}", null, cancellationToken);
        JsonArray? nachher = null;
        try
        {
            if (danach.Antwort is { Erfolg: true } ok) nachher = JsonNode.Parse(ok.Text)?["rules"] as JsonArray;
        }
        catch (JsonException)
        {
        }
        if (nachher is null) return $"{name} an Zelt {zeltId}: gespeichert, aber der Stand danach ließ sich nicht lesen — die Prüfsumme fehlt. Bitte mit alarme nachsehen.";

        var summeVorher = RegelPruefsumme(vorher, schluessel, welche.Feld, neuAngelegt: index is null);
        var summeNachher = RegelPruefsumme(nachher, schluessel, welche.Feld, neuAngelegt: index is null);
        var jetzt = nachher.FirstOrDefault(r => r?["metricKey"]?.GetValue<string>() == schluessel)?[welche.Feld]?.GetValue<double>();

        if (summeVorher != summeNachher || jetzt != wert)
        {
            var rueck = await SendenAsync(HttpMethod.Put, $"api/alerts/tents/{zeltId}", new JsonObject { ["rules"] = vorher.DeepClone() }, cancellationToken);
            return $"ACHTUNG: Beim Speichern von {name} an Zelt {zeltId} hat sich mehr verändert als diese eine Grenze (oder sie steht nicht auf {Zahl(wert)}). "
                   + (rueck.Antwort is { Erfolg: true }
                       ? "Der vorherige Stand wurde zurückgeschrieben; bitte mit alarme nachsehen."
                       : "Das Zurückschreiben ist NICHT gelungen — bitte sofort in Cultivate Control auf der Seite Grenzwerte nachsehen.");
        }

        return $"{name} an Zelt {zeltId}: vorher {(alt is { } a ? Zahl(a) + einheit : index is null ? "keine Regel" : "keine Grenze")}, jetzt {Zahl(wert)}{einheit}"
               + (index is null ? " (neue Regel, Meldungen an, Abstand 30 Minuten)" : "")
               + $". Alle anderen Grenzen und Einstellungen sind unverändert (Prüfsumme {summeNachher} vorher und nachher gleich, {nachher.Count} Regeln). "
               + "Cultivate Control prüft die neue Grenze gleich beim Speichern gegen den aktuellen Wert, sofern Home Assistant erreichbar ist.";
    }

    /// <summary>Prüfsumme über alle Regeln — ohne das eine geänderte Feld; eine eben angelegte Regel zählt gar nicht mit.</summary>
    public static string RegelPruefsumme(JsonArray regeln, string schluessel, string feld, bool neuAngelegt)
    {
        var ohne = new JsonArray();
        foreach (var regel in regeln.OfType<JsonObject>().OrderBy(r => r["metricKey"]?.GetValue<string>(), StringComparer.Ordinal))
        {
            var eigene = regel["metricKey"]?.GetValue<string>() == schluessel;
            if (eigene && neuAngelegt) continue;
            var kopie = new JsonObject();
            foreach (var (name, wert) in regel.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (eigene && name == feld) continue;
                kopie[name] = wert?.DeepClone();
            }
            ohne.Add(kopie);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ohne.ToJsonString(Ausgabe))))[..8].ToLowerInvariant();
    }

    private static string Zahl(double wert) => wert.ToString("0.###", Deutsch);

    // ------------------------------------------------------------------ lichtplan_setzen

    [McpServerTool(Name = "lichtplan_setzen")]
    [Description($"Ändert die Schaltzeiten (an, aus) des Lichtplans eines Zelts — nur die Zeiten; Name, aktiv, Quelle und Zeitzone bleiben, per Prüfsumme belegt. Die Zeiten sind Uhrzeiten im Zelt (Zeitzone des Plans, sonst die von Home Assistant), etwa 07:00 und 19:00. Cultivate Control schreibt sie NICHT nach Home Assistant und nicht an den Controller: der Plan steuert in Cultivate Control Nachtabsenkung, Lichteinbruch-Wächter, Auto-Messungen und die Nachtgrenzen der Meldungen. Das Licht selbst schaltet licht_schalten. Ohne planId gilt der aktive Plan. Der jetzige Plan steht in licht. {StufeGrowPlanen}")]
    [BrauchtForkSchluessel(Stufen.GrowPlanen)]
    public async Task<string> LichtplanSetzenAsync(
        [Description("Die Id des Zelts")] int zeltId,
        [Description("Licht an, Uhrzeit im Zelt: 07:00, 7:30 oder 7")][Beispiel("07:00")] string an,
        [Description("Licht aus, Uhrzeit im Zelt: 19:00, 19:30 oder 19")][Beispiel("19:00")] string aus,
        [Description("Die Id eines bestimmten Plans; weglassen = der aktive Plan des Zelts")] int? planId = null,
        CancellationToken cancellationToken = default)
    {
        if (!reader.HatForkSchluessel) return ForkFehler.SchluesselNoetig;

        var ein = Uhrzeit(an);
        var ende = Uhrzeit(aus);
        if (ein is null || ende is null)
            return $"Nichts geändert: „{(ein is null ? an : aus)}\" ist keine Uhrzeit. Bitte so angeben: 07:00 oder 19:30.";

        var liste = await SendenAsync(HttpMethod.Get, $"api/light-schedules?tentId={zeltId}", null, cancellationToken);
        if (liste.Fehler is { } listeFehler) return listeFehler;
        if (!liste.Antwort!.Erfolg) return ForkFehler.Text(liste.Antwort.Status, liste.Antwort.Text, "Lichtplan lesen");

        JsonObject? plan;
        try
        {
            var plaene = (JsonNode.Parse(liste.Antwort.Text) as JsonArray ?? throw new JsonException()).OfType<JsonObject>().ToList();
            if (plaene.Count == 0) return $"Nichts geändert: Zelt {zeltId} hat keinen Lichtplan. Anlegen geht in Cultivate Control am Zelt unter Lichtplan.";
            // Dieselbe Wahl wie Cultivate Control (GetActiveLightScheduleForTent): aktiv, zuletzt geändert, dann die höchste Id.
            plan = planId is { } gewuenscht
                ? plaene.FirstOrDefault(p => p["id"]?.GetValue<int>() == gewuenscht)
                : plaene.Where(p => p["isActive"]?.GetValue<bool>() == true)
                    .OrderByDescending(p => p["updatedAtUtc"]?.GetValue<string>(), StringComparer.Ordinal)
                    .ThenByDescending(p => p["id"]?.GetValue<int>())
                    .FirstOrDefault();
            if (plan is null)
            {
                return planId is null
                    ? $"Nichts geändert: Zelt {zeltId} hat keinen aktiven Lichtplan. Pläne: {string.Join("; ", plaene.Select(p => $"{p["id"]} „{p["name"]}\""))} — bitte planId angeben."
                    : $"Nichts geändert: Zelt {zeltId} hat keinen Lichtplan mit der Id {planId}. Pläne: {string.Join("; ", plaene.Select(p => $"{p["id"]} „{p["name"]}\""))}.";
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return AndersAlsErwartet;
        }

        var id = plan["id"]!.GetValue<int>();
        var name = plan["name"]?.GetValue<string>() ?? $"Plan {id}";
        var altAn = plan["lightsOnTime"]?.GetValue<string>();
        var altAus = plan["lightsOffTime"]?.GetValue<string>();
        if (altAn == ein && altAus == ende) return $"Nichts geändert: Der Lichtplan „{name}\" (Id {id}) läuft schon {ein}–{ende}.";

        // Cultivate Control ersetzt beim Speichern den ganzen Plan: alles aus dem Ist-Stand, nur die beiden Zeiten neu.
        var rumpf = new JsonObject
        {
            ["name"] = name,
            ["isActive"] = plan["isActive"]?.DeepClone(),
            ["lightsOnTime"] = ein,
            ["lightsOffTime"] = ende,
            ["timeZoneId"] = plan["timeZoneId"]?.DeepClone(),
            ["source"] = plan["source"]?.DeepClone(),
        };

        var gespeichert = await SendenAsync(HttpMethod.Put, $"api/light-schedules/{id}", rumpf, cancellationToken);
        if (gespeichert.Fehler is { } speichernFehler) return speichernFehler;
        if (!gespeichert.Antwort!.Erfolg) return ForkFehler.Text(gespeichert.Antwort.Status, gespeichert.Antwort.Text, "Lichtplan setzen");

        var danach = await SendenAsync(HttpMethod.Get, $"api/light-schedules/{id}", null, cancellationToken);
        JsonObject? nachher = null;
        try
        {
            if (danach.Antwort is { Erfolg: true } ok) nachher = JsonNode.Parse(ok.Text) as JsonObject;
        }
        catch (JsonException)
        {
        }
        if (nachher is null) return $"Lichtplan „{name}\" (Id {id}): gespeichert, aber der Stand danach ließ sich nicht lesen — die Prüfsumme fehlt. Bitte mit licht nachsehen.";

        var summeVorher = PlanPruefsumme(plan);
        var summeNachher = PlanPruefsumme(nachher);
        if (summeVorher != summeNachher || nachher["lightsOnTime"]?.GetValue<string>() != ein || nachher["lightsOffTime"]?.GetValue<string>() != ende)
        {
            var zurueck = (JsonObject)rumpf.DeepClone();
            zurueck["lightsOnTime"] = altAn;
            zurueck["lightsOffTime"] = altAus;
            var rueck = await SendenAsync(HttpMethod.Put, $"api/light-schedules/{id}", zurueck, cancellationToken);
            return $"ACHTUNG: Beim Speichern des Lichtplans „{name}\" (Id {id}) hat sich mehr verändert als die Zeiten (oder sie stehen nicht auf {ein}–{ende}). "
                   + (rueck.Antwort is { Erfolg: true } ? "Die alten Zeiten wurden zurückgeschrieben; bitte mit licht nachsehen." : "Das Zurückschreiben ist NICHT gelungen — bitte sofort in Cultivate Control nachsehen.");
        }

        var zone = plan["timeZoneId"]?.GetValue<string>() is { Length: > 0 } z ? $"Zeitzone des Plans: {z}" : "der Plan hat keine eigene Zeitzone, es gilt die von Home Assistant";
        return $"Lichtplan „{name}\" (Id {id}) an Zelt {zeltId}: vorher {altAn}–{altAus} ({Lichtstunden(altAn, altAus)}), jetzt {ein}–{ende} ({Lichtstunden(ein, ende)}). "
               + $"Die Zeiten gelten als Uhrzeit im Zelt ({zone}). Name, aktiv, Quelle und Zeitzone sind unverändert (Prüfsumme {summeNachher} vorher und nachher gleich). "
               + "Nach Home Assistant oder an den Controller schreibt Cultivate Control diese Zeiten nicht — was das Licht tatsächlich schaltet, bleibt, wie es ist.";
    }

    /// <summary>Prüfsumme über den Plan ohne die beiden Zeiten und ohne den Änderungszeitpunkt.</summary>
    public static string PlanPruefsumme(JsonObject plan)
    {
        var ohne = new JsonObject();
        foreach (var (name, wert) in plan.Where(p => p.Key is not ("lightsOnTime" or "lightsOffTime" or "updatedAtUtc")).OrderBy(p => p.Key, StringComparer.Ordinal))
            ohne[name] = wert?.DeepClone();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ohne.ToJsonString(Ausgabe))))[..8].ToLowerInvariant();
    }

    private static readonly Regex UhrzeitMuster = new(@"^(?<h>\d{1,2})(?:[:.](?<m>\d{2}))?(?:\s*uhr)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>„7", „7:30", „07.30 Uhr" → „07:00"/„07:30"; sonst <c>null</c>.</summary>
    public static string? Uhrzeit(string? gesagt)
    {
        var treffer = UhrzeitMuster.Match((gesagt ?? string.Empty).Trim());
        if (!treffer.Success) return null;
        var stunde = int.Parse(treffer.Groups["h"].Value, CultureInfo.InvariantCulture);
        var minute = treffer.Groups["m"].Success ? int.Parse(treffer.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
        return stunde is >= 0 and <= 23 && minute is >= 0 and <= 59 ? $"{stunde:00}:{minute:00}" : null;
    }

    private static string Lichtstunden(string? an, string? aus)
    {
        if (!TimeSpan.TryParseExact(an, "hh\\:mm", CultureInfo.InvariantCulture, out var a) || !TimeSpan.TryParseExact(aus, "hh\\:mm", CultureInfo.InvariantCulture, out var b)) return "?";
        var dauer = b - a;
        if (dauer < TimeSpan.Zero) dauer += TimeSpan.FromHours(24);
        return $"{dauer.TotalHours.ToString("0.#", Deutsch)} h Licht";
    }
}
