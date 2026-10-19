using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrowOsAccess;
using ModelContextProtocol.Server;

namespace GrowMcp.Tools;

/// <summary>
/// Fork AI (Paket D, 18.10.2026): <c>fork_ausfuehren</c> — jede Funktion von Cultivate Control über einen Schlüssel.
/// </summary>
/// <remarks>
/// <para><b>Grundsatz seit 18.10.2026:</b> Über den Cultivate Control MCP lässt sich alles im Fork steuern. Gesperrt bleiben nur
/// die Wege, die Cultivate Control selbst als „nie über einen Schlüssel" führt (Schlüsselverwaltung, KI-Hauptschalter, Sicherung
/// zurückspielen/herunterladen, Home-Assistant-Adresse und -Token).</para>
/// <para><b>Hier werden keine Rechte hinzugefügt.</b> Stufe, Höchstwerte, Sicherung vorher und Protokoll prüft Cultivate Control
/// bei jeder Anfrage. Dieses Werkzeug verengt nur: Es ruft nichts, was nicht im Funktionsverzeichnis steht, lehnt
/// „nie"-Wege ab, bevor es sie ruft, nimmt nur Pfade unter <c>api/</c> an (kein <c>..</c>, keine Adresse, kein anderer
/// Host) und gibt keine Dateien weiter, weder hinauf noch herunter.</para>
/// <para><b>Vorher/Nachher:</b> Kennt das Verzeichnis einen GET auf denselben Weg, wird vor und nach einer Änderung
/// gelesen und gemeldet, was sich bewegt hat; vor einem Löschen wird gelesen und das Gelöschte genannt.</para>
/// </remarks>
public sealed partial class SchreibWerkzeuge
{
    /// <summary>So viel Text nimmt das Werkzeug von Cultivate Control an (danach wird abgeschnitten).</summary>
    internal const int AntwortBytesHoechstens = 512 * 1024;

    private const int LeseAntwortHoechstens = 20_000;
    private const int SchreibAntwortHoechstens = 4_000;
    private const int AenderungenHoechstens = 25;

    [McpServerTool(Name = "fork_ausfuehren")]
    [Description("Führt EINE Funktion von Cultivate Control aus — jede, die im Funktionsverzeichnis steht (erst fork_funktionen fragen: Weg, Stufe, Felder). "
                 + "methode GET/POST/PUT/PATCH/DELETE, pfad relativ wie im Verzeichnis mit eingesetzten Werten (api/plants/7), query als „a=1&b=2\", body als JSON-Text. "
                 + "Ob die Stufe reicht, prüft Cultivate Control (Stufe je Funktion, siehe zugriff_pruefen); Höchstwerte, Sicherung vorher und Prüfprotokoll gelten wie überall. "
                 + "Bei Änderungen wird vorher und nachher gelesen und gemeldet, was sich geändert hat; beim Löschen steht das Gelöschte in der Antwort. "
                 + "PUT ersetzt bei Cultivate Control meist das ganze Objekt: fehlende Rumpffelder ergänzt das Werkzeug aus dem gelesenen Stand (ergaenzen). "
                 + "Was Cultivate Control als „nie über einen Schlüssel\" führt (fork_funktionen mit zugang=nie), lehnt das Werkzeug ab, ohne es zu rufen. Keine Datei-Uploads, keine Bilder (dafür kamera_bild, tagesbilder, foto_ansehen). "
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

        // 1. Form: Methode, Pfad, Abfrage, Rumpf — bevor Cultivate Control überhaupt gefragt wird.
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
            return $"Nicht ausgeführt: {art} /{weg} steht nicht im Funktionsverzeichnis von Cultivate Control."
                   + (andere.Count > 0 ? $" Auf diesem Weg gibt es: {string.Join(", ", andere)}." : " Mit fork_funktionen suchen.");
        }

        var name = $"{art} /{weg}";
        if (funktion.Zugang == "nie")
        {
            return $"Nicht ausgeführt: {funktion.Methode} {funktion.Pfad} ist über einen KI-Assistenten nie erreichbar — {(funktion.NieGrund ?? "so in Cultivate Control festgelegt").TrimEnd('.')}. Das bleibt dem Menschen in der Oberfläche von Cultivate Control.";
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

        // Ein Feld, das Cultivate Control nicht kennt, ignoriert es still und nimmt seine Vorgabe — ein Tippfehler („preisEur" statt
        // „einzelpreisEur") würde so 0 € speichern. Deshalb vorher ablehnen (an der Demo am 18.10.2026 so passiert).
        if (rumpf is JsonObject gesendeteFelder && funktion.RumpfFelder.Count > 0)
        {
            var unbekannt = gesendeteFelder.Select(p => p.Key)
                .Where(k => !funktion.RumpfFelder.Any(f => string.Equals(f.Name, k, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (unbekannt.Count > 0)
            {
                return $"Nicht ausgeführt: {string.Join(", ", unbekannt)} {(unbekannt.Count == 1 ? "ist kein Feld" : "sind keine Felder")} von {funktion.RumpfTyp}. "
                       + "Cultivate Control würde das still übergehen. Felder: " + string.Join(", ", funktion.RumpfFelder.Select(f => $"{f.Name} ({f.Typ})")) + ".";
            }
        }

        var ziel = abfrage.Length == 0 ? weg : $"{weg}?{abfrage}";

        // 3. Lesen: einfach ausführen und die Antwort weitergeben.
        if (art == "GET")
        {
            var gelesen = await BegrenztAsync(HttpMethod.Get, ziel, null, cancellationToken);
            if (gelesen.Fehler is { } gf) return gf;
            var a = gelesen.Antwort!;
            if (!a.IstText) return $"Cultivate Control antwortet auf {name} mit einer Datei ({a.MedienTyp}) — die gebe ich nicht weiter. Bilder: kamera_bild, tagesbilder, foto_ansehen.";
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
        var angefragt = rumpf is JsonObject gesendet
            ? gesendet.ToDictionary(p => p.Key, p => p.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        var ergaenzt = new List<string>();
        var alsLeer = new List<string>();
        var nichtErgaenzt = new List<string>();
        if (art == "PUT" && ergaenzen && funktion.RumpfFelder.Count > 0)
        {
            if (rumpf is not null and not JsonObject) return "Nicht ausgeführt: Zum Ergänzen muss body ein JSON-Objekt sein (oder ergaenzen=false).";
            var teil = rumpf as JsonObject ?? new JsonObject();
            var fehlen = funktion.RumpfFelder.Where(f => !teil.Any(p => string.Equals(p.Key, f.Name, StringComparison.OrdinalIgnoreCase))).ToList();
            if (fehlen.Count > 0)
            {
                // PUT ersetzt bei Cultivate Control meist das Ganze: ohne gelesenen Stand würde ein halber Rumpf den Rest zurücksetzen.
                if (leseWeg is null)
                {
                    return $"Nicht ausgeführt: PUT ersetzt bei Cultivate Control meist das ganze Objekt, und auf diesem Weg gibt es keinen Lese-Weg, aus dem ich die fehlenden Felder ergänzen könnte ({string.Join(", ", fehlen.Select(f => f.Name))}). "
                           + "Entweder alle Felder schicken oder ergaenzen=false — dann setzt Cultivate Control die fehlenden auf seine Vorgabe.";
                }
                if (vorher is null)
                {
                    return $"Nicht ausgeführt: Den Stand vor der Änderung konnte ich nicht lesen ({funktion.Pfad}); ohne ihn würde dieser PUT die fehlenden Felder zurücksetzen. Bitte später noch einmal oder alle Felder selbst schicken.";
                }
                if (vorher is not JsonObject stand)
                {
                    return $"Nicht ausgeführt: Der gelesene Stand ({funktion.Pfad}) ist eine Liste, kein Objekt — daraus lässt sich der Rumpf nicht ergänzen. Bitte alle Felder selbst schicken.";
                }

                // Listen behandelt Cultivate Control je Weg verschieden: fehlt sie, bleibt sie mal unverändert (Sensoren eines Zelts),
                // mal wird sie leer (Kacheln des Dashboards — Prüfer 18.10.2026). Nicht belegt → nicht raten, sondern abbrechen.
                var listen = fehlen.Where(f => f.Typ.StartsWith("Liste", StringComparison.Ordinal) || f.Typ.StartsWith("Zuordnung", StringComparison.Ordinal)).ToList();
                if (listen.Count > 0)
                {
                    return $"Nicht ausgeführt: Im Rumpf fehlt {(listen.Count == 1 ? "die Liste" : "die Listen")} {string.Join(", ", listen.Select(f => f.Name))}. "
                           + "Was Cultivate Control mit einer fehlenden Liste tut, ist je Weg verschieden (manchmal wird sie leer). Bitte die Liste aus dem gelesenen Stand mitschicken "
                           + "— oder ergaenzen=false, wenn sicher ist, dass Cultivate Control sie ohne Angabe unverändert lässt.";
                }

                // Die Quelle ist EIN Objekt — der gelesene Stand selbst oder ein Teil davon (etwa „einstellungen"), je
                // nachdem, wo die meisten Rumpffelder stehen. Nie Feld für Feld irgendwo: dasselbe Feld steht oft auch unter „live".
                var (quelle, quellName, treffer) = ErgaenzQuelle(stand, funktion.RumpfFelder);
                if (treffer * 2 < funktion.RumpfFelder.Count)
                {
                    return $"Nicht ausgeführt: Den gelesenen Stand kann ich dem Rumpf {funktion.RumpfTyp} nicht sicher zuordnen (nur {treffer} von {funktion.RumpfFelder.Count} Feldern gefunden). "
                           + "Bitte alle Felder selbst schicken oder ergaenzen=false.";
                }

                foreach (var feld in fehlen)
                {
                    var wert = quelle.FirstOrDefault(p => string.Equals(p.Key, feld.Name, StringComparison.OrdinalIgnoreCase));
                    if (wert.Key is not null)
                    {
                        teil[feld.Name] = wert.Value?.DeepClone();
                        ergaenzt.Add(feld.Name);
                    }
                    else if (feld.Typ.EndsWith('?') || feld.Typ == "Text")
                    {
                        // Cultivate Control lässt leere Felder in der Antwort weg — fehlt es in der Quelle, war es leer.
                        teil[feld.Name] = null;
                        alsLeer.Add(feld.Name);
                    }
                    else
                    {
                        // Darf nicht leer sein und steht nicht im Stand (etwa ein Schalter wie „Schlauch jetzt gewechselt"):
                        // weglassen, Cultivate Control nimmt seine Vorgabe — der Vergleich danach zeigt, ob sich dadurch etwas bewegt.
                        nichtErgaenzt.Add(feld.Name);
                    }
                }
                rumpf = teil;
            }
        }

        var ergebnis = await BegrenztAsync(new HttpMethod(art), ziel, rumpf?.ToJsonString(Ausgabe), cancellationToken);
        if (ergebnis.Fehler is { } ef) return ef;
        var antwort = ergebnis.Antwort!;
        if (!antwort.Erfolg) return ForkFehler.Text(antwort.Status, antwort.Text, name);

        var text = new StringBuilder();
        text.Append($"Ausgeführt: {name} → {antwort.Status}.");
        if (funktion.SicherungVorher) text.Append(" Cultivate Control hat vorher eine Sicherung angelegt.");
        if (funktion.Schaltbefehl) text.Append(" Zählt als Schaltbefehl.");
        text.AppendLine();
        if (ergaenzt.Count > 0) text.AppendLine($"Aus dem gelesenen Stand ergänzt ({ergaenzt.Count}): {string.Join(", ", ergaenzt)}.");
        if (alsLeer.Count > 0) text.AppendLine($"Im gelesenen Stand leer, deshalb leer mitgeschickt: {string.Join(", ", alsLeer)}.");
        if (nichtErgaenzt.Count > 0) text.AppendLine($"Nicht im gelesenen Stand und nicht leer erlaubt, deshalb weggelassen (Cultivate Control nimmt seine Vorgabe): {string.Join(", ", nichtErgaenzt)}.");

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
        else
        {
            text.AppendLine("Den Stand vorher konnte ich nicht lesen — kein Vorher/Nachher-Vergleich. Bitte mit GET nachsehen.");
        }

        if (!antwort.IstText)
        {
            text.Append($"Cultivate Control hat mit einer Datei geantwortet ({antwort.MedienTyp}) — nicht weitergegeben.");
        }
        else if (!string.IsNullOrWhiteSpace(antwort.Text))
        {
            text.AppendLine("Antwort von Cultivate Control:");
            text.Append(Funktionsverzeichnis.Kuerzen(antwort.Text, SchreibAntwortHoechstens, "Den ganzen Stand mit GET lesen."));
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>Die Unterschiede in Worten — was gewollt war (Felder aus dem Rumpf) und was sonst noch.</summary>
    private static string Aenderungen(JsonNode vorher, Dictionary<string, string>? vorherZweitesMal, JsonNode nachher, Dictionary<string, JsonNode?> gesendet)
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

            // Ganze Einträge einer Liste (nach id abgeglichen): neu oder entfernt in einer Zeile statt Feld für Feld.
            // Neu oder entfernt ist ein Eintrag nur, wenn sein eigenes id-Blatt kommt oder geht (Prüfer 18.10.2026: sonst
            // galt ein Abschnitt als „entfernt", aus dem nur zwei Kacheln fehlten).
            var neu = geaendert.Where(a => a.Alt is null && a.Neu is not null && Funktionsverzeichnis.Eintrag(a.Pfad) is { } e && a.Pfad == e + ".id")
                .Select(a => Funktionsverzeichnis.Eintrag(a.Pfad)!).ToList();
            var weg = geaendert.Where(a => a.Neu is null && a.Alt is not null && Funktionsverzeichnis.Eintrag(a.Pfad) is { } e && a.Pfad == e + ".id")
                .Select(a => Funktionsverzeichnis.Eintrag(a.Pfad)!).ToList();
            bool Unter(string pfad, List<string> eintraege) => eintraege.Any(e => pfad == e || pfad.StartsWith(e + ".", StringComparison.Ordinal) || pfad.StartsWith(e + "[", StringComparison.Ordinal));
            geaendert = geaendert.Where(a => !Unter(a.Pfad, neu) && !Unter(a.Pfad, weg)).ToList();
            if (neu.Count > 0) text.AppendLine($"Neu ({neu.Count}): {string.Join("; ", neu.Take(AenderungenHoechstens))}.");
            if (weg.Count > 0) text.AppendLine($"Entfernt ({weg.Count}): {string.Join("; ", weg.Take(AenderungenHoechstens))}.");

            // Gewollt heißt: das Feld stand im Rumpf UND hat jetzt den gesendeten Wert (bei einfachen Werten).
            bool Gewollt(Funktionsverzeichnis.Aenderung a)
                => gesendet.TryGetValue(Letzter(a.Pfad), out var wert)
                   && (wert is JsonObject or JsonArray
                       || (wert?.ToJsonString() ?? "null") == (a.Neu ?? "(fehlt)")
                       // Cultivate Control lässt leere Felder in der Antwort weg: ein gesendetes null kommt als „fehlt" zurück.
                       || (wert is null && (a.Neu is null || a.Neu == "null")));
            var gewollt = geaendert.Where(Gewollt).ToList();
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

    /// <summary>
    /// Das Objekt im gelesenen Stand, aus dem ergänzt wird: der Stand selbst oder einer seiner Teile — das mit den
    /// meisten Rumpffeldern. Bei Gleichstand gewinnt der Stand selbst.
    /// </summary>
    private static (JsonObject Quelle, string Name, int Treffer) ErgaenzQuelle(JsonObject stand, IReadOnlyList<Funktionsverzeichnis.Feld> felder)
    {
        int Zaehlen(JsonObject o) => felder.Count(f => o.Any(p => string.Equals(p.Key, f.Name, StringComparison.OrdinalIgnoreCase)));
        var beste = (Quelle: stand, Name: string.Empty, Treffer: Zaehlen(stand));
        foreach (var (name, wert) in stand)
        {
            if (wert is JsonObject teil && Zaehlen(teil) is var n && n > beste.Treffer) beste = (teil, name, n);
        }
        return beste;
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
            return new Ergebnis(null, "Cultivate Control ist gerade nicht erreichbar. Es wurde nichts ausgeführt.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new Ergebnis(null, "Cultivate Control hat nicht rechtzeitig geantwortet. Ob der Befehl ankam, ist offen — vor einem zweiten Versuch mit GET nachsehen.");
        }
    }
}
