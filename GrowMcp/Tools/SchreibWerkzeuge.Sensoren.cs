using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GrowOsAccess;
using ModelContextProtocol.Server;

namespace GrowMcp.Tools;

/// <summary>
/// Sensor-Zuordnung eines Zelts lesen und genau eine Messgröße zuordnen (forkai.247).
/// </summary>
/// <remarks>
/// <para><b>Warum nicht über das ganze Zelt.</b> <c>PUT api/settings/tents/{id}</c> mit <c>sensors</c> legt alle Sensorzeilen neu an
/// (neue Ids; Geräte und Meldungen hängen daran). Deshalb lässt <c>zelt_aufstellraum_setzen</c> die Sensoren weg, und deshalb gibt
/// es in Cultivate Control <c>PUT api/settings/tents/{id}/sensors/{metricType}</c>, das genau eine Zeile ändert.</para>
/// <para><b>Namen nicht von hier.</b> Etikett, Schlüssel und passende Domains liefert Cultivate Control (<c>GET api/messgroessen</c>); dort
/// sind sie gegen die App-Oberfläche geprüft. Hier wird nur toleriert, wie ein Mensch sie sagt.</para>
/// <para><b>Gemessen, nicht behauptet.</b> <c>sensor_zuordnen</c> liest das Zelt vor und nach dem Speichern und vergleicht jede
/// andere Zeile (Id, Messgröße, Entität, aktiv). Erst dann steht in der Antwort, dass sie gleich geblieben sind.</para>
/// </remarks>
public sealed partial class SchreibWerkzeuge
{
    private static readonly string[] KeineEntitaetWoerter = ["", "keine", "keiner", "keinen", "nichts", "lösen", "loesen", "none", "-"];

    /// <summary>Eine Messgröße aus <c>GET api/messgroessen</c>.</summary>
    public sealed record Messgroesse(string MetricType, string Schluessel, string Etikett, string? MeldeEtikett, IReadOnlyList<string> Domains, string? Einheit)
    {
        /// <summary>Alle Namen, unter denen ein Mensch sie nennen könnte.</summary>
        public IEnumerable<string> Namen => new[] { MetricType, Schluessel, Etikett, MeldeEtikett }.OfType<string>();
    }

    /// <summary>Eine Sensorzeile eines Zelts, wie Cultivate Control sie liefert.</summary>
    public readonly record struct SensorZeile(int Id, string MetricType, string Entitaet, bool Aktiv);

    // ------------------------------------------------------------------ sensoren

    [McpServerTool(Name = "sensoren")]
    [Description($"Die Sensor-Zuordnung eines Zelts: je Messgröße (deutsches Etikett wie in der App) die Home-Assistant-Entität, ob aktiv, ihr Wert jetzt — und welche Messgrößen noch frei sind. Ohne zeltId bei nur einem Zelt dieses. Liest nur; Cultivate Control verlangt für die Zelt-Einstellungen trotzdem Stufe „{Stufen.VerwaltungText}\". Vor sensor_zuordnen aufrufen.")]
    [BrauchtForkSchluessel(Stufen.Verwaltung, NurLesen = true)]
    public async Task<string> SensorenAsync(
        [Description("Die Id des Zelts; weglassen, wenn es nur eines gibt")] int? zeltId = null,
        CancellationToken cancellationToken = default)
    {
        if (!reader.HatForkSchluessel) return ForkFehler.SchluesselNoetig;

        var gewaehlt = await ZeltWaehlenAsync(zeltId, cancellationToken);
        if (gewaehlt.Fehler is { } wahlFehler) return wahlFehler;

        var katalog = await KatalogAsync(cancellationToken);
        if (katalog.Fehler is { } katalogFehler) return katalogFehler;

        var zelt = await ZeltLesenAsync(gewaehlt.Id, cancellationToken);
        if (zelt.Fehler is { } zeltFehler) return zeltFehler;

        var text = new StringBuilder();
        var zeilen = zelt.Zeilen;
        var belegt = zeilen.Where(z => z.Aktiv && z.Entitaet.Length > 0).OrderBy(z => Rang(katalog.Liste, z.MetricType)).ToList();
        text.AppendLine($"Zelt {gewaehlt.Id} „{zelt.Name}\" — zugeordnet ({belegt.Count}):");

        string? haHinweis = null;
        foreach (var zeile in belegt)
        {
            var groesse = katalog.Liste.FirstOrDefault(m => m.MetricType == zeile.MetricType);
            var wert = "";
            if (haHinweis is null)
            {
                var stand = await HaZustandAsync(zeile.Entitaet, cancellationToken);
                if (stand.Fehler is { } haFehler) haHinweis = haFehler;
                else wert = stand.Zustand is null ? " — in Home Assistant nicht gefunden" : $" — {WertText(stand.Zustand, stand.Einheit)}";
            }

            var passt = groesse is null || groesse.Domains.Contains(Domain(zeile.Entitaet))
                ? ""
                : $" (passt nicht zur Regel: erwartet {string.Join(" oder ", groesse.Domains)})";
            text.AppendLine($"  {groesse?.Etikett ?? zeile.MetricType}: {zeile.Entitaet}{wert}, aktiv{passt} [Sensor-Id {zeile.Id}]");
        }
        if (haHinweis is not null) text.AppendLine($"  Werte aus Home Assistant: nicht lesbar — {haHinweis}");

        var inaktiv = zeilen.Where(z => !(z.Aktiv && z.Entitaet.Length > 0)).OrderBy(z => Rang(katalog.Liste, z.MetricType)).ToList();
        if (inaktiv.Count > 0)
        {
            text.AppendLine("Inaktiv: " + string.Join("; ", inaktiv.Select(z =>
                $"{Etikett(katalog.Liste, z.MetricType)}{(z.Entitaet.Length > 0 ? $" ({z.Entitaet})" : "")} [Sensor-Id {z.Id}]")) + ".");
        }

        var frei = katalog.Liste.Where(m => !belegt.Any(z => z.MetricType == m.MetricType)).Select(m => m.Etikett).ToList();
        text.AppendLine(frei.Count == 0 ? "Frei: keine." : "Frei: " + string.Join(", ", frei) + ".");
        return text.ToString().TrimEnd();
    }

    // ------------------------------------------------------------------ sensor_zuordnen

    [McpServerTool(Name = "sensor_zuordnen")]
    [Description($"Ordnet GENAU EINE Messgröße eines Zelts einer Home-Assistant-Entität zu, hängt sie um oder löst sie (entitaet leer oder „keine\"). Prüft vorher, ob es die Entität in Home Assistant gibt und ob ihre Art passt (etwa sensor für Temperaturen). Alle anderen Zuordnungen bleiben mit ihren Ids stehen — das wird vorher/nachher verglichen. Beim Lösen bleibt die Zeile inaktiv stehen. Erst sensoren aufrufen. {StufeVerwaltung}")]
    [BrauchtForkSchluessel(Stufen.Verwaltung)]
    public async Task<string> SensorZuordnenAsync(
        [Description("Die Id des Zelts (aus sensoren oder raum)")] int zeltId,
        [Description("Die Messgröße wie in der App („Außen Temp.\", „pH\", „Luftfeuchte\") oder ihr Name in Cultivate Control (OutsideTemperature, outside-temperature)")][Beispiel("Außen Temp.")] string messgroesse,
        [Description("Die Entity-Id in Home Assistant, etwa sensor.controller_temperatur. Nur ein ausdrücklich leerer Text (\"\") oder „keine\" löst die Zuordnung.")][Beispiel("sensor.controller_temperatur")] string entitaet,
        CancellationToken cancellationToken = default)
    {
        if (!reader.HatForkSchluessel) return ForkFehler.SchluesselNoetig;

        var katalog = await KatalogAsync(cancellationToken);
        if (katalog.Fehler is { } katalogFehler) return katalogFehler;

        var (groesse, findFehler) = MessgroesseFinden(katalog.Liste, messgroesse);
        if (groesse is null) return "Nichts geändert: " + findFehler;

        var vorher = await ZeltLesenAsync(zeltId, cancellationToken);
        if (vorher.Fehler is { } zeltFehler) return zeltFehler;

        var wunsch = (entitaet ?? string.Empty).Trim();
        var loesen = KeineEntitaetWoerter.Contains(wunsch.ToLowerInvariant());
        var ziel = loesen ? null : wunsch.ToLowerInvariant();
        var alt = AktuelleZeile(vorher.Zeilen, groesse.MetricType);
        var altText = alt is { Aktiv: true } a && a.Entitaet.Length > 0 ? a.Entitaet : null;
        var wo = $"{groesse.Etikett} an Zelt {zeltId} „{vorher.Name}\"";

        (string? Zustand, string? Einheit) stand = (null, null);
        if (ziel is not null)
        {
            if (!EineEntitaetMuster.IsMatch(ziel))
                return $"Nichts geändert: „{wunsch}\" ist nicht genau eine Entity-Id. Erwartet wird etwa sensor.controller_temperatur — eine, ohne Komma.";
            if (!groesse.Domains.Contains(Domain(ziel)))
                return $"Nichts geändert: „{groesse.Etikett}\" erwartet eine Entität der Art {string.Join(" oder ", groesse.Domains)}; {ziel} ist {Domain(ziel)}.";
            if (string.Equals(altText, ziel, StringComparison.Ordinal))
                return $"Nichts geändert: {wo} zeigt schon auf {ziel}.";

            var ha = await HaZustandAsync(ziel, cancellationToken);
            if (ha.Fehler is { } haFehler) return $"Nichts geändert: Ob es {ziel} in Home Assistant gibt, ließ sich nicht prüfen. {haFehler}";
            if (ha.Zustand is null)
            {
                var aehnlich = await AehnlicheAsync(ziel, cancellationToken);
                return $"Nichts geändert: In Home Assistant gibt es keine Entität {ziel}."
                       + (aehnlich.Count > 0 ? $" Ähnlich: {string.Join(", ", aehnlich)}." : " Mit ha_zustaende lässt sich suchen.");
            }
            stand = (ha.Zustand, ha.Einheit);
        }
        else if (altText is null)
        {
            return $"Nichts geändert: {wo} ist schon frei.";
        }

        var gespeichert = await SendenAsync(HttpMethod.Put, $"api/settings/tents/{zeltId}/sensors/{groesse.MetricType}",
            new JsonObject { ["haEntityId"] = ziel }, cancellationToken);
        if (gespeichert.Fehler is { } speichernFehler) return speichernFehler;
        if (!gespeichert.Antwort!.Erfolg) return ForkFehler.Text(gespeichert.Antwort.Status, gespeichert.Antwort.Text, "Sensor zuordnen");

        // Gegenprobe am gespeicherten Stand, nicht an der Antwort.
        var nachher = await ZeltLesenAsync(zeltId, cancellationToken);
        if (nachher.Fehler is not null)
            return $"{wo}: gespeichert, aber der Stand danach ließ sich nicht lesen — der Vergleich der übrigen Zuordnungen fehlt. Bitte mit sensoren nachsehen.";

        var (uebrige, abweichend) = UebrigeVergleichen(vorher.Zeilen, nachher.Zeilen, groesse.MetricType);
        var neu = AktuelleZeile(nachher.Zeilen, groesse.MetricType);

        var satz = new StringBuilder();
        if (ziel is not null)
        {
            var zielStimmt = neu is { Aktiv: true } n && n.Entitaet == ziel;
            if (!zielStimmt) satz.Append($"ACHTUNG: Cultivate Control hat gespeichert, aber {groesse.Etikett} zeigt danach nicht auf {ziel}. Bitte mit sensoren nachsehen. ");
            satz.Append($"{wo}: vorher {altText ?? "keine Zuordnung"}, jetzt {ziel} (gerade {WertText(stand.Zustand!, stand.Einheit)}");
            satz.Append(neu is { } z ? (alt is { } va && va.Id == z.Id ? $"; Sensor-Id {z.Id} wie vorher)." : $"; neue Sensor-Id {z.Id}).") : ").");
        }
        else
        {
            satz.Append($"{wo}: Zuordnung gelöst (vorher {altText}).");
            var eigene = nachher.Zeilen.Where(z => z.MetricType == groesse.MetricType).ToList();
            if (eigene.Any(z => z.Aktiv && z.Entitaet.Length > 0))
                satz.Insert(0, $"ACHTUNG: Cultivate Control hat gespeichert, aber {groesse.Etikett} ist danach noch zugeordnet. Bitte mit sensoren nachsehen. ");
            else if (eigene.Count == 1) satz.Append($" Die Zeile bleibt mit Sensor-Id {eigene[0].Id} inaktiv stehen, so behalten Geräte und Meldungen ihren Verweis.");
            else if (eigene.Count > 1) satz.Append($" Die Zeilen mit den Sensor-Ids {string.Join(", ", eigene.Select(z => z.Id))} bleiben inaktiv stehen, so behalten Geräte und Meldungen ihren Verweis.");
        }

        satz.Append(abweichend.Count == 0
            ? uebrige == 0
                ? " Weitere Zuordnungen hat das Zelt nicht (vorher und nachher verglichen)."
                : $" Die übrigen {uebrige} Zuordnungen sind unverändert (Ids gleich, vorher und nachher verglichen)."
            : $" ACHTUNG: Bei anderen Zuordnungen hat sich etwas verändert: {string.Join("; ", abweichend)}. Bitte sofort mit sensoren und in Cultivate Control prüfen.");
        if (ziel is not null) satz.Append(" Cultivate Control liest den Wert ab dem nächsten Messtakt.");
        return satz.ToString();
    }

    // ------------------------------------------------------------------ Hilfe Sensoren

    private static readonly Regex EineEntitaetMuster = new(@"^[a-z0-9_]+\.[a-z0-9_]+$", RegexOptions.CultureInvariant);

    private static string Domain(string entityId) => entityId.Split('.', 2)[0];

    private static int Rang(IReadOnlyList<Messgroesse> katalog, string metricType)
    {
        for (var i = 0; i < katalog.Count; i++) if (katalog[i].MetricType == metricType) return i;
        return int.MaxValue;
    }

    private static string Etikett(IReadOnlyList<Messgroesse> katalog, string metricType)
        => katalog.FirstOrDefault(m => m.MetricType == metricType)?.Etikett ?? metricType;

    /// <summary>
    /// Die Zeile, die für eine Messgröße gilt — dieselbe Wahl wie Cultivate Control (<c>SensorMessgroessen.WirksameZeile</c>): die letzte aktive
    /// mit Entität, sonst die älteste.
    /// </summary>
    private static SensorZeile? AktuelleZeile(IReadOnlyList<SensorZeile> zeilen, string metricType)
    {
        var eigene = zeilen.Where(z => z.MetricType == metricType).OrderBy(z => z.Id).ToList();
        return eigene.Where(z => z.Aktiv && z.Entitaet.Length > 0).Cast<SensorZeile?>().LastOrDefault() ?? eigene.Cast<SensorZeile?>().FirstOrDefault();
    }

    /// <summary>Alle Zeilen außer der einen Messgröße: wie viele, und was sich an ihnen verändert hat.</summary>
    public static (int Anzahl, List<string> Abweichend) UebrigeVergleichen(IReadOnlyList<SensorZeile> vorher, IReadOnlyList<SensorZeile> nachher, string metricType)
    {
        var alt = vorher.Where(z => z.MetricType != metricType).ToList();
        var neu = nachher.Where(z => z.MetricType != metricType).ToList();
        var abweichend = new List<string>();
        foreach (var zeile in alt)
        {
            var gleich = neu.FirstOrDefault(n => n.Id == zeile.Id);
            if (gleich == default) abweichend.Add($"Sensor-Id {zeile.Id} ({zeile.MetricType}) fehlt");
            else if (gleich != zeile) abweichend.Add($"Sensor-Id {zeile.Id} ({zeile.MetricType}) geändert");
        }
        abweichend.AddRange(neu.Where(n => alt.All(a => a.Id != n.Id)).Select(n => $"Sensor-Id {n.Id} ({n.MetricType}) neu"));
        return (alt.Count, abweichend);
    }

    /// <summary>Eine Messgröße so finden, wie ein Mensch sie sagt — oder den Grund, warum nicht.</summary>
    public static (Messgroesse? Treffer, string? Fehler) MessgroesseFinden(IReadOnlyList<Messgroesse> katalog, string? gesagt)
    {
        var wort = Glatt(gesagt);
        var moeglich = "Möglich: " + string.Join(", ", katalog.Select(m => m.Etikett)) + ".";
        if (wort.Length == 0) return (null, "Es wurde keine Messgröße genannt. " + moeglich);

        var genau = katalog.Where(m => m.Namen.Any(n => Glatt(n) == wort)).ToList();
        if (genau.Count == 1) return (genau[0], null);

        // „Lufttemperatur" für „Lufttemp", „Wasserstand" für beide Wasserstände (dann mehrdeutig). Kurze Namen („DO", „pH")
        // nur genau — sonst wäre „Dosierung" der Sauerstoff.
        var aehnlich = katalog.Where(m => m.Namen.Select(Glatt).Any(n =>
            (wort.Length >= 3 && n.StartsWith(wort, StringComparison.Ordinal)) || (n.Length >= 4 && wort.StartsWith(n, StringComparison.Ordinal)))).ToList();
        if (aehnlich.Count == 1) return (aehnlich[0], null);
        if (aehnlich.Count > 1) return (null, $"„{gesagt!.Trim()}\" ist nicht eindeutig: {string.Join(", ", aehnlich.Select(m => m.Etikett))}.");
        return (null, $"Messgröße „{gesagt!.Trim()}\" gibt es nicht. {moeglich}");
    }

    /// <summary>Kleinbuchstaben, Umlaute ausgeschrieben, nur Buchstaben und Ziffern: „Außen Temp." → „aussentemp".</summary>
    private static string Glatt(string? text)
    {
        var aus = new StringBuilder();
        foreach (var zeichen in (text ?? string.Empty).ToLowerInvariant())
        {
            switch (zeichen)
            {
                case 'ä': aus.Append("ae"); break;
                case 'ö': aus.Append("oe"); break;
                case 'ü': aus.Append("ue"); break;
                case 'ß': aus.Append("ss"); break;
                case '₂': aus.Append('2'); break;
                default: if (char.IsLetterOrDigit(zeichen)) aus.Append(zeichen); break;
            }
        }
        return aus.ToString();
    }

    private async Task<(IReadOnlyList<Messgroesse> Liste, string? Fehler)> KatalogAsync(CancellationToken cancellationToken)
    {
        var antwort = await SendenAsync(HttpMethod.Get, "api/messgroessen", null, cancellationToken);
        if (antwort.Fehler is { } fehler) return ([], fehler);
        if (antwort.Antwort!.Status == 404) return ([], "Diese Fassung von Cultivate Control kennt die Messgrößen-Liste noch nicht (ab forkai.247). Bitte Cultivate Control aktualisieren.");
        if (!antwort.Antwort.Erfolg) return ([], ForkFehler.Text(antwort.Antwort.Status, antwort.Antwort.Text, "Messgrößen lesen"));
        try
        {
            using var doc = JsonDocument.Parse(antwort.Antwort.Text);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return ([], AndersAlsErwartet);
            var liste = doc.RootElement.EnumerateArray()
                .Where(e => Text(e, "metricType") is not null && Text(e, "etikett") is not null)
                .Select(e => new Messgroesse(
                    Text(e, "metricType")!, Text(e, "metrikSchluessel") ?? Text(e, "metricType")!, Text(e, "etikett")!, Text(e, "meldeEtikett"),
                    Liste(e, "domains"), Text(e, "einheit")))
                .ToList();
            return liste.Count == 0 ? ([], AndersAlsErwartet) : (liste, null);
        }
        catch (JsonException)
        {
            return ([], AndersAlsErwartet);
        }
    }

    private readonly record struct ZeltStand(string Name, IReadOnlyList<SensorZeile> Zeilen, string? Fehler);

    private async Task<ZeltStand> ZeltLesenAsync(int zeltId, CancellationToken cancellationToken)
    {
        var antwort = await SendenAsync(HttpMethod.Get, $"api/settings/tents/{zeltId}", null, cancellationToken);
        if (antwort.Fehler is { } fehler) return new("", [], fehler);
        if (antwort.Antwort!.Status == 404) return new("", [], await ZeltUnbekanntAsync(zeltId, cancellationToken));
        if (!antwort.Antwort.Erfolg) return new("", [], ForkFehler.Text(antwort.Antwort.Status, antwort.Antwort.Text, "Zelt lesen"));
        try
        {
            using var doc = JsonDocument.Parse(antwort.Antwort.Text);
            var wurzel = doc.RootElement;
            if (wurzel.ValueKind != JsonValueKind.Object || !wurzel.TryGetProperty("sensors", out var sensoren) || sensoren.ValueKind != JsonValueKind.Array)
                return new("", [], AndersAlsErwartet);
            var zeilen = sensoren.EnumerateArray()
                .Where(s => s.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && Text(s, "metricType") is not null)
                .Select(s => new SensorZeile(
                    s.GetProperty("id").GetInt32(), Text(s, "metricType")!, Text(s, "haEntityId") ?? "",
                    s.TryGetProperty("isActive", out var aktiv) && aktiv.ValueKind == JsonValueKind.True))
                .ToList();
            return new(Text(wurzel, "name") ?? $"Zelt {zeltId}", zeilen, null);
        }
        catch (JsonException)
        {
            return new("", [], AndersAlsErwartet);
        }
    }

    private readonly record struct ZeltWahl(int Id, string? Fehler);

    /// <summary>Ohne Angabe: bei genau einem Zelt dieses, sonst die Liste.</summary>
    private async Task<ZeltWahl> ZeltWaehlenAsync(int? zeltId, CancellationToken cancellationToken)
    {
        if (zeltId is { } id) return new(id, null);

        var antwort = await SendenAsync(HttpMethod.Get, "api/settings/tents", null, cancellationToken);
        if (antwort.Fehler is { } fehler) return new(0, fehler);
        if (!antwort.Antwort!.Erfolg) return new(0, ForkFehler.Text(antwort.Antwort.Status, antwort.Antwort.Text, "Zelte lesen"));
        try
        {
            using var doc = JsonDocument.Parse(antwort.Antwort.Text);
            var zelte = ZeltListe(JsonSerializer.SerializeToElement(new { zelte = doc.RootElement }), "zelte");
            return zelte.Count switch
            {
                0 => new(0, "In Cultivate Control ist noch kein Zelt angelegt."),
                1 => new(zelte[0].Id, null),
                _ => new(0, "Es gibt mehrere Zelte — bitte zeltId angeben: " + string.Join("; ", zelte.Select(z => $"Zelt {z.Id} „{z.Name}\"")) + "."),
            };
        }
        catch (JsonException)
        {
            return new(0, AndersAlsErwartet);
        }
    }

    private readonly record struct HaStand(string? Zustand, string? Einheit, string? Fehler);

    /// <summary>Zustand und Einheit genau dieser Entität über den Home-Assistant-Zugang von Cultivate Control; <c>Zustand null</c> = gibt es nicht.</summary>
    private async Task<HaStand> HaZustandAsync(string entityId, CancellationToken cancellationToken)
    {
        var antwort = await SendenAsync(HttpMethod.Get, $"api/ki-ha/zustaende?suche={Uri.EscapeDataString(entityId)}&anzahl=500", null, cancellationToken);
        if (antwort.Fehler is { } fehler) return new(null, null, fehler);
        if (!antwort.Antwort!.Erfolg) return new(null, null, ForkFehler.Text(antwort.Antwort.Status, antwort.Antwort.Text, "Home Assistant lesen"));
        try
        {
            using var doc = JsonDocument.Parse(antwort.Antwort.Text);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return new(null, null, AndersAlsErwartet);
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (string.Equals(Text(e, "entityId"), entityId, StringComparison.Ordinal))
                    return new(Text(e, "zustand") ?? "", Text(e, "einheit"), null);
            }
            return new(null, null, null);
        }
        catch (JsonException)
        {
            return new(null, null, AndersAlsErwartet);
        }
    }

    /// <summary>Bis zu drei Entitäten derselben Domain mit ähnlicher Id — für den Tippfehler.</summary>
    private async Task<List<string>> AehnlicheAsync(string entityId, CancellationToken cancellationToken)
    {
        var antwort = await SendenAsync(HttpMethod.Get, $"api/ki-ha/zustaende?domain={Uri.EscapeDataString(Domain(entityId))}&anzahl=500", null, cancellationToken);
        if (antwort.Antwort is not { Erfolg: true } ok) return [];
        try
        {
            using var doc = JsonDocument.Parse(ok.Text);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
            // Ein Tippfehler sind ein, zwei Zeichen; mehr wäre eine andere Entität (humidty → vpd liegt bei 7).
            var grenze = Math.Max(2, entityId.Length / 8);
            return doc.RootElement.EnumerateArray()
                .Select(e => Text(e, "entityId"))
                .OfType<string>()
                .Select(id => (Id: id, Abstand: Abstand(id, entityId)))
                .Where(x => x.Abstand <= grenze)
                .OrderBy(x => x.Abstand).ThenBy(x => x.Id, StringComparer.Ordinal)
                .Take(3)
                .Select(x => x.Id)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Levenshtein-Abstand.</summary>
    public static int Abstand(string a, string b)
    {
        var vorher = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var jetzt = new int[b.Length + 1];
            jetzt[0] = i;
            for (var j = 1; j <= b.Length; j++)
                jetzt[j] = Math.Min(Math.Min(jetzt[j - 1] + 1, vorher[j] + 1), vorher[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            vorher = jetzt;
        }
        return vorher[b.Length];
    }

    /// <summary>„24.4" + „°C" → „24,4 °C"; Text („on") bleibt Text.</summary>
    public static string WertText(string zustand, string? einheit)
    {
        var wert = double.TryParse(zustand, NumberStyles.Float, CultureInfo.InvariantCulture, out var zahl)
            ? zahl.ToString("0.###", CultureInfo.GetCultureInfo("de-DE"))
            : zustand;
        return string.IsNullOrWhiteSpace(einheit) ? wert : $"{wert} {einheit}";
    }
}
