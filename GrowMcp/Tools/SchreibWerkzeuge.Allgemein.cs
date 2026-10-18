using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrowOsAccess;
using ModelContextProtocol.Server;

namespace GrowMcp.Tools;

/// <summary>
/// Fork AI (Paket D, 18.10.2026): <c>fork_ausfuehren</c> — jede Funktion von Grow OS über einen Schlüssel.
/// </summary>
/// <remarks>
/// <para><b>Grundsatz seit 18.10.2026:</b> Über den Grow MCP lässt sich alles im Fork steuern. Gesperrt bleiben nur
/// die Wege, die Grow OS selbst als „nie über einen Schlüssel" führt (Schlüsselverwaltung, KI-Hauptschalter, Sicherung
/// zurückspielen/herunterladen, Home-Assistant-Adresse und -Token).</para>
/// <para><b>Hier werden keine Rechte hinzugefügt.</b> Stufe, Höchstwerte, Sicherung vorher und Protokoll prüft Grow OS
/// bei jeder Anfrage. Dieses Werkzeug verengt nur: Es ruft nichts, was nicht im Funktionsverzeichnis steht, lehnt
/// „nie"-Wege ab, bevor es sie ruft, nimmt nur Pfade unter <c>api/</c> an (kein <c>..</c>, keine Adresse, kein anderer
/// Host) und gibt keine Dateien weiter, weder hinauf noch herunter.</para>
/// <para><b>Vorher/Nachher:</b> Kennt das Verzeichnis einen GET auf denselben Weg, wird vor und nach einer Änderung
/// gelesen und gemeldet, was sich bewegt hat; vor einem Löschen wird gelesen und das Gelöschte genannt.</para>
/// </remarks>
public sealed partial class SchreibWerkzeuge
{
    /// <summary>So viel Text nimmt das Werkzeug von Grow OS an (danach wird abgeschnitten).</summary>
    internal const int AntwortBytesHoechstens = 512 * 1024;

    private const int LeseAntwortHoechstens = 20_000;
    private const int SchreibAntwortHoechstens = 4_000;
    private const int AenderungenHoechstens = 25;

    [McpServerTool(Name = "fork_ausfuehren")]
    [Description("Führt EINE Funktion von Grow OS aus — jede, die im Funktionsverzeichnis steht (erst fork_funktionen fragen: Weg, Stufe, Felder). "
                 + "methode GET/POST/PUT/PATCH/DELETE, pfad relativ wie im Verzeichnis mit eingesetzten Werten (api/plants/7), query als „a=1&b=2\", body als JSON-Text. "
                 + "Ob die Stufe reicht, prüft Grow OS (Stufe je Funktion, siehe zugriff_pruefen); Höchstwerte, Sicherung vorher und Prüfprotokoll gelten wie überall. "
                 + "Bei Änderungen wird vorher und nachher gelesen und gemeldet, was sich geändert hat; beim Löschen steht das Gelöschte in der Antwort. "
                 + "PUT ersetzt bei Grow OS meist das ganze Objekt: fehlende Rumpffelder ergänzt das Werkzeug aus dem gelesenen Stand (ergaenzen). "
                 + "Nie über einen Schlüssel: Schlüsselverwaltung, KI-Hauptschalter, Sicherung zurückspielen/herunterladen, Home-Assistant-Adresse/-Token. Keine Datei-Uploads, keine Bilder (dafür kamera_bild, tagesbilder, foto_ansehen). "
                 + "Für Alltägliches die festen Werkzeuge nehmen (messung_eintragen, journal_eintragen, licht_schalten, pumpe_dosieren …) — sie prüfen mehr. Vor Änderungen die Rückfrage-Regel aus zugriff_pruefen beachten.")]
    [BrauchtForkSchluessel]
    public async Task<string> ForkAusfuehrenAsync(
        [Description("GET, POST, PUT, PATCH oder DELETE")][Beispiel("GET")] string methode,
        [Description("Der Weg wie im Verzeichnis, Platzhalter durch Werte ersetzt, etwa api/kosten/nachfuellungen/41")][Beispiel("api/grows")] string pfad,
        [Description("Abfragewerte, etwa „growId=1&tage=7\" (ohne ?)")] string? query = null,
        [Description("Der JSON-Rumpf als Text, etwa {\"hystereseProzent\":3}. Nur bei POST, PUT, PATCH.")] string? body = null,
        [Description("Nur PUT: Felder, die im Rumpf fehlen, aus dem vorher gelesenen Stand ergänzen (gleiche Feldnamen). Vorgabe ja.")] bool ergaenzen = true,
        CancellationToken cancellationToken = default)
    {
        if (!reader.HatForkSchluessel) return ForkFehler.SchluesselNoetig;

        // 1. Form: Methode, Pfad, Abfrage, Rumpf — bevor Grow OS überhaupt gefragt wird.
        var art = (methode ?? string.Empty).Trim().ToUpperInvariant();
        if (!Funktionsverzeichnis.Methoden.Contains(art)) return $"Nicht ausgeführt: „{methode}\" ist keine Methode. Möglich: GET, POST, PUT, PATCH, DELETE.";

        if (Funktionsverzeichnis.PfadPruefen(pfad, out var pfadFehler) is not { } weg) return $"Nicht ausgeführt: {pfadFehler}";
        if (Funktionsverzeichnis.AbfragePruefen(query, out var abfrageFehler) is not { } abfrage) return $"Nicht ausgeführt: {abfrageFehler}";

        JsonNode? rumpf = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            if (art is "GET" or "DELETE") return $"Nicht ausgeführt: {art} nimmt keinen Rumpf. body weglassen.";
            try
            {
                rumpf = JsonNode.Parse(body);
            }
            catch (JsonException ex)
            {
                return $"Nicht ausgeführt: body ist kein gültiges JSON ({ex.Message}).";
            }
        }

        // 2. Verzeichnis: nur was dort steht, und nichts, was dort „nie" heißt.
        var verzeichnisAntwort = await SendenAsync(HttpMethod.Get, Funktionsverzeichnis.Weg, null, cancellationToken);
        if (verzeichnisAntwort.Fehler is { } vf) return vf;
        if (!verzeichnisAntwort.Antwort!.Erfolg) return "Nicht ausgeführt: ohne Funktionsverzeichnis führe ich nichts aus. " + GrowTools.VerzeichnisFehler(verzeichnisAntwort.Antwort);
        var verzeichnis = Funktionsverzeichnis.Lesen(verzeichnisAntwort.Antwort.Text);
        if (verzeichnis is null) return "Nicht ausgeführt: " + AndersAlsErwartet;

        var funktion = Funktionsverzeichnis.Zuordnen(verzeichnis, art, weg);
        if (funktion is null)
        {
            var andere = verzeichnis.Where(f => Funktionsverzeichnis.Zuordnen([f], f.Methode, weg) is not null).Select(f => f.Methode).Distinct().ToList();
            return $"Nicht ausgeführt: {art} /{weg} steht nicht im Funktionsverzeichnis von Grow OS."
                   + (andere.Count > 0 ? $" Auf diesem Weg gibt es: {string.Join(", ", andere)}." : " Mit fork_funktionen suchen.");
        }

        var name = $"{art} /{weg}";
        if (funktion.Zugang == "nie")
        {
            return $"Nicht ausgeführt: {funktion.Methode} {funktion.Pfad} ist über einen KI-Assistenten nie erreichbar — {(funktion.NieGrund ?? "so in Grow OS festgelegt").TrimEnd('.')}. Das bleibt dem Menschen in der Oberfläche von Grow OS.";
        }
        if (funktion.Zugang == "nicht-eingestuft" && art != "GET")
        {
            return $"Nicht ausgeführt: {funktion.Methode} {funktion.Pfad} ist keiner Stufe zugeordnet und deshalb über einen Schlüssel gesperrt.";
        }
        if (funktion.DateiHochladen)
        {
            return $"Nicht ausgeführt: {funktion.Methode} {funktion.Pfad} erwartet eine hochgeladene Datei — das geht über den Assistenten nicht. Ein Kamerabild als Foto speichert kamera_foto_speichern.";
        }
        if (funktion.LiefertDatei)
        {
            return $"Nicht ausgeführt: {funktion.Methode} {funktion.Pfad} liefert eine Datei oder ein Bild, kein JSON. Bilder: kamera_bild (Livebild), tagesbilder, foto_ansehen.";
        }

        // Ein Feld, das Grow OS nicht kennt, ignoriert es still und nimmt seine Vorgabe — ein Tippfehler („preisEur" statt
        // „einzelpreisEur") würde so 0 € speichern. Deshalb vorher ablehnen (an der Demo am 18.10.2026 so passiert).
        if (rumpf is JsonObject gesendeteFelder && funktion.RumpfFelder.Count > 0)
        {
            var unbekannt = gesendeteFelder.Select(p => p.Key)
                .Where(k => !funktion.RumpfFelder.Any(f => string.Equals(f.Name, k, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (unbekannt.Count > 0)
            {
                return $"Nicht ausgeführt: {string.Join(", ", unbekannt)} {(unbekannt.Count == 1 ? "ist kein Feld" : "sind keine Felder")} von {funktion.RumpfTyp}. "
                       + "Grow OS würde das still übergehen. Felder: " + string.Join(", ", funktion.RumpfFelder.Select(f => $"{f.Name} ({f.Typ})")) + ".";
            }
        }

        var ziel = abfrage.Length == 0 ? weg : $"{weg}?{abfrage}";

        // 3. Lesen: einfach ausführen und die Antwort weitergeben.
        if (art == "GET")
        {
            var gelesen = await BegrenztAsync(HttpMethod.Get, ziel, null, cancellationToken);
            if (gelesen.Fehler is { } gf) return gf;
            var a = gelesen.Antwort!;
            if (!a.IstText) return $"Grow OS antwortet auf {name} mit einer Datei ({a.MedienTyp}) — die gebe ich nicht weiter. Bilder: kamera_bild, tagesbilder, foto_ansehen.";
            if (!a.Erfolg) return ForkFehler.Text(a.Status, a.Text, name);
            var lesetext = a.Gekuerzt ? a.Text + Environment.NewLine + "… (Antwort größer als erlaubt, abgeschnitten)" : a.Text;
            return Funktionsverzeichnis.Kuerzen(lesetext, LeseAntwortHoechstens, "Mit query eingrenzen oder ein festes Werkzeug nehmen.");
        }

        // 4. Ändern/Löschen: vorher lesen (zweimal — was sich schon dabei bewegt, ist ein Live-Wert und kein Ergebnis).
        var leseWeg = verzeichnis.FirstOrDefault(f => f.Methode == "GET" && f.Pfad == funktion.Pfad && f.Zugang != "nie" && !f.LiefertDatei);
        JsonNode? vorher = null;
        Dictionary<string, string>? vorherZweitesMal = null;
        if (art == "DELETE" && leseWeg is null)
        {
            vorher = await GeloeschtesVorherAsync(verzeichnis, funktion, weg, cancellationToken);
        }
        else if (leseWeg is not null)
        {
            vorher = await JsonLesenAsync(weg, cancellationToken);
            if (vorher is not null && art != "DELETE")
            {
                var zweites = await JsonLesenAsync(weg, cancellationToken);
                if (zweites is not null) vorherZweitesMal = Funktionsverzeichnis.Blaetter(zweites);
            }
        }

        // Was der Assistent ändern wollte — vor dem Ergänzen gemerkt, sonst sähe jede Nebenwirkung gewollt aus.
        var angefragt = rumpf is JsonObject gesendet ? gesendet.Select(p => p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
        var ergaenzt = new List<string>();
        var fehlend = new List<string>();
        if (art == "PUT" && ergaenzen && rumpf is JsonObject teil && vorher is JsonObject stand && funktion.RumpfFelder.Count > 0)
        {
            foreach (var feld in funktion.RumpfFelder)
            {
                if (teil.ContainsKey(feld.Name)) continue;
                if (Finden(stand, feld.Name) is { } gefunden)
                {
                    teil[feld.Name] = gefunden.Wert?.DeepClone();
                    ergaenzt.Add(feld.Name);
                }
                else
                {
                    fehlend.Add(feld.Name);
                }
            }
        }

        var ergebnis = await BegrenztAsync(new HttpMethod(art), ziel, rumpf?.ToJsonString(Ausgabe), cancellationToken);
        if (ergebnis.Fehler is { } ef) return ef;
        var antwort = ergebnis.Antwort!;
        if (!antwort.Erfolg) return ForkFehler.Text(antwort.Status, antwort.Text, name);

        var text = new StringBuilder();
        text.Append($"Ausgeführt: {name} → {antwort.Status}.");
        if (funktion.SicherungVorher) text.Append(" Grow OS hat vorher eine Sicherung angelegt.");
        if (funktion.Schaltbefehl) text.Append(" Zählt als Schaltbefehl.");
        text.AppendLine();
        if (ergaenzt.Count > 0) text.AppendLine($"Aus dem gelesenen Stand ergänzt ({ergaenzt.Count}): {string.Join(", ", ergaenzt)}.");
        if (fehlend.Count > 0) text.AppendLine($"Nicht im gelesenen Stand (dort leer oder nicht geführt), also nicht mitgeschickt — Grow OS nimmt dafür seine Vorgabe; ob sich dadurch etwas geändert hat, zeigt der Vergleich: {string.Join(", ", fehlend)}.");

        if (art == "DELETE")
        {
            text.AppendLine(vorher is not null
                ? "Gelöscht: " + Funktionsverzeichnis.Kurzfassung(vorher)
                : "Was gelöscht wurde, ließ sich vorher nicht lesen (kein Lese-Weg, der diesen Eintrag zeigt).");
        }
        else if (vorher is not null)
        {
            var nachher = await JsonLesenAsync(weg, cancellationToken);
            if (nachher is null)
            {
                text.AppendLine("Den Stand danach konnte ich nicht lesen — bitte mit GET nachsehen.");
            }
            else
            {
                text.AppendLine(Aenderungen(vorher, vorherZweitesMal, nachher, angefragt));
            }
        }
        else if (leseWeg is null)
        {
            text.AppendLine("Kein Lese-Weg auf diesem Pfad — Vorher/Nachher ließ sich nicht vergleichen.");
        }

        if (!antwort.IstText)
        {
            text.Append($"Grow OS hat mit einer Datei geantwortet ({antwort.MedienTyp}) — nicht weitergegeben.");
        }
        else if (!string.IsNullOrWhiteSpace(antwort.Text))
        {
            text.AppendLine("Antwort von Grow OS:");
            text.Append(Funktionsverzeichnis.Kuerzen(antwort.Text, SchreibAntwortHoechstens, "Den ganzen Stand mit GET lesen."));
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>Die Unterschiede in Worten — was gewollt war (Felder aus dem Rumpf) und was sonst noch.</summary>
    private static string Aenderungen(JsonNode vorher, Dictionary<string, string>? vorherZweitesMal, JsonNode nachher, HashSet<string> gesendet)
    {
        var (geaendert, gleich, beweglich) = Funktionsverzeichnis.Vergleichen(
            Funktionsverzeichnis.Blaetter(vorher), vorherZweitesMal, Funktionsverzeichnis.Blaetter(nachher));

        var text = new StringBuilder();
        if (geaendert.Count == 0)
        {
            text.Append($"Am gelesenen Stand hat sich nichts verändert ({gleich} Werte gleich).");
        }
        else
        {
            static string Letzter(string pfad) => pfad[(pfad.LastIndexOf('.') + 1)..].Split('[')[0];
            var gewollt = geaendert.Where(a => gesendet.Contains(Letzter(a.Pfad))).ToList();
            var sonst = geaendert.Except(gewollt).ToList();

            string Zeile(Funktionsverzeichnis.Aenderung a) => $"{a.Pfad}: {Funktionsverzeichnis.Wert(a.Alt)} → {Funktionsverzeichnis.Wert(a.Neu)}";
            if (gewollt.Count > 0)
            {
                text.AppendLine($"Geändert ({gewollt.Count}): " + string.Join("; ", gewollt.Take(AenderungenHoechstens).Select(Zeile))
                                + (gewollt.Count > AenderungenHoechstens ? $"; und {gewollt.Count - AenderungenHoechstens} weitere" : string.Empty) + ".");
            }
            if (sonst.Count > 0)
            {
                text.AppendLine((gesendet.Count > 0 ? $"Außerdem verändert, ohne dass es im Rumpf stand ({sonst.Count}) — bitte prüfen: " : $"Verändert ({sonst.Count}): ")
                                + string.Join("; ", sonst.Take(AenderungenHoechstens).Select(Zeile))
                                + (sonst.Count > AenderungenHoechstens ? $"; und {sonst.Count - AenderungenHoechstens} weitere" : string.Empty) + ".");
            }
            text.Append($"{gleich} Werte unverändert.");
        }
        if (beweglich > 0) text.Append($" {beweglich} Werte bewegten sich schon vorher (Live-Werte, Zeiten) und sind nicht verglichen.");
        return text.ToString();
    }

    /// <summary>
    /// Den Eintrag, der gleich gelöscht wird, über einen Lese-Weg darüber finden: <c>api/kosten/anschaffungen/41</c>
    /// steht in <c>GET api/kosten</c> unter <c>anschaffungen</c> mit <c>id</c> 41.
    /// </summary>
    /// <remarks>Nur, wenn der letzte Abschnitt ein Platzhalter ist; gesucht wird in der direkten Liste darüber oder in
    /// einer Liste, die wie der vorletzte Abschnitt heißt — nie irgendein Objekt mit derselben Id.</remarks>
    private async Task<JsonNode?> GeloeschtesVorherAsync(IReadOnlyList<Funktionsverzeichnis.Funktion> verzeichnis, Funktionsverzeichnis.Funktion funktion, string weg, CancellationToken cancellationToken)
    {
        var vorlage = funktion.Pfad.TrimStart('/').Split('/');
        var abschnitte = weg.Split('/');
        if (vorlage.Length != abschnitte.Length || vorlage.Length < 3 || !vorlage[^1].StartsWith('{')) return null;

        var id = abschnitte[^1];
        var liste = abschnitte[^2];
        for (var k = abschnitte.Length - 1; k >= 2; k--)
        {
            var obenVorlage = "/" + string.Join('/', vorlage[..k]);
            if (!verzeichnis.Any(f => f.Methode == "GET" && f.Pfad == obenVorlage && f.Zugang != "nie" && !f.LiefertDatei)) continue;

            var oben = await JsonLesenAsync(string.Join('/', abschnitte[..k]), cancellationToken);
            if (oben is null) continue;
            var treffer = k == abschnitte.Length - 1 && oben is JsonArray direkt ? MitId(direkt, id) : InListe(oben, liste, id);
            if (treffer is not null) return treffer;
        }
        return null;
    }

    private static JsonNode? MitId(JsonArray liste, string id)
        => liste.FirstOrDefault(e => e is JsonObject o && o["id"] is JsonValue v && string.Equals(v.ToJsonString().Trim('"'), id, StringComparison.Ordinal));

    private static JsonNode? InListe(JsonNode? knoten, string name, string id)
    {
        switch (knoten)
        {
            case JsonObject o:
                foreach (var (schluessel, wert) in o)
                {
                    if (wert is JsonArray a && string.Equals(schluessel, name, StringComparison.OrdinalIgnoreCase) && MitId(a, id) is { } gefunden) return gefunden;
                    if (InListe(wert, name, id) is { } tiefer) return tiefer;
                }
                return null;
            case JsonArray liste:
                foreach (var e in liste)
                {
                    if (InListe(e, name, id) is { } tiefer) return tiefer;
                }
                return null;
            default:
                return null;
        }
    }

    /// <summary>Ein Feld im gelesenen Stand: oben, sonst eine Ebene tiefer — nur wenn es dort genau einmal vorkommt.</summary>
    private static (string Pfad, JsonNode? Wert)? Finden(JsonObject stand, string name)
    {
        foreach (var (schluessel, wert) in stand)
        {
            if (string.Equals(schluessel, name, StringComparison.OrdinalIgnoreCase)) return (schluessel, wert);
        }

        var tiefer = stand
            .Where(p => p.Value is JsonObject)
            .SelectMany(p => ((JsonObject)p.Value!).Where(q => string.Equals(q.Key, name, StringComparison.OrdinalIgnoreCase)).Select(q => ($"{p.Key}.{q.Key}", q.Value)))
            .ToList();
        return tiefer.Count == 1 ? tiefer[0] : null;
    }

    private async Task<JsonNode?> JsonLesenAsync(string weg, CancellationToken cancellationToken)
    {
        var gelesen = await BegrenztAsync(HttpMethod.Get, weg, null, cancellationToken);
        if (gelesen.Antwort is not { Erfolg: true, IstText: true, Gekuerzt: false } a) return null;
        try
        {
            return JsonNode.Parse(a.Text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Wie <see cref="SendenAsync"/>, aber mit Inhaltstyp und Grenze.</summary>
    private async Task<Ergebnis> BegrenztAsync(HttpMethod methode, string pfad, string? json, CancellationToken cancellationToken)
    {
        try
        {
            return new Ergebnis(await reader.SendenBegrenztAsync(methode, pfad, json, AntwortBytesHoechstens, cancellationToken), null);
        }
        catch (GrowOsException ex)
        {
            return new Ergebnis(null, ex.Message);
        }
        catch (HttpRequestException)
        {
            return new Ergebnis(null, "Grow OS ist gerade nicht erreichbar. Es wurde nichts ausgeführt.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new Ergebnis(null, "Grow OS hat nicht rechtzeitig geantwortet. Ob der Befehl ankam, ist offen — vor einem zweiten Versuch mit GET nachsehen.");
        }
    }
}
