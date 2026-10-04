using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrowOsAccess;
using ModelContextProtocol.Server;

namespace GrowMcp.Tools;

/// <summary>
/// Eintragen und Schalten in Grow OS — mit einem Fork-Schlüssel.
/// </summary>
/// <remarks>
/// <para>Fork AI (A-004, 03.10.2026): Bis hierher war der Grow MCP nur lesend,
/// mit Absicht — Grow OS liess aus dem Add-on-Netz nichts anderes zu. Seit
/// forkai.163 kann der Betreiber in Grow OS Schlüssel für KI-Assistenten
/// anlegen (Einrichtung → KI-Assistent → Zugriff & Schlüssel) und dort je Schlüssel
/// Stufen anhaken. Wer den Connector mit so einem Schlüssel einrichtet, bekommt
/// diese Werkzeuge.</para>
///
/// <para><b>Hier wird nichts entschieden.</b> Welche Stufe was darf, welche
/// Höchstwerte gelten, ob der Schlüssel noch besteht — das prüft Grow OS bei
/// jeder Anfrage selbst. Jedes Werkzeug nennt die Stufe nur, damit der
/// Assistent weiss, was ihn erwartet. Die Absage von Grow OS kommt als Satz
/// zurück (<see cref="ForkFehler"/>), nie als Ausnahme.</para>
///
/// <para>Mit dem MCP-Schlüssel des Add-ons fragt keines dieser Werkzeuge Grow OS
/// überhaupt: es antwortet mit dem Hinweis, wo es den richtigen Schlüssel gibt.</para>
/// </remarks>
[McpServerToolType]
public sealed class SchreibWerkzeuge(GrowOsReader reader)
{
    private static readonly JsonSerializerOptions Ausgabe = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private const string StufeDokumentieren = $"Braucht Stufe „{Stufen.DokumentierenText}\".";
    private const string StufeGrowPlanen = $"Braucht Stufe „{Stufen.GrowPlanenText}\".";
    private const string StufeSchalten = $"Braucht Stufe „{Stufen.GeraeteSchaltenText}\".";

    private const string ZeitpunktText = "Ortszeit, etwa 2026-10-03T14:30 oder 03.10.2026 14:30. Weglassen = jetzt.";

    // ------------------------------------------------------------------ Zugriff

    [McpServerTool(Name = "zugriff_pruefen")]
    [Description("Zuerst aufrufen: welche Stufen frei sind, wobei vorher nachgefragt werden soll und welche Höchstwerte gelten. Liefert das in Klartext; ohne Schlüssel aus Grow OS steht dort, wie man ihn bekommt.")]
    [BrauchtForkSchluessel]
    public async Task<string> ZugriffPruefenAsync(CancellationToken cancellationToken = default)
    {
        if (!reader.HatForkSchluessel)
        {
            return "Verbunden mit dem MCP-Schlüssel dieses Add-ons: nur lesen. " + ForkFehler.SchluesselNoetig;
        }

        var antwort = await SendenAsync(HttpMethod.Get, "api/ki-zugriff/ich", null, cancellationToken);
        if (antwort.Fehler is { } fehler) return fehler;
        if (!antwort.Antwort!.Erfolg) return ForkFehler.Text(antwort.Antwort.Status, antwort.Antwort.Text, "Zugriff prüfen");

        try
        {
            using var json = JsonDocument.Parse(antwort.Antwort.Text);
            return ZugriffBeschreiben(json.RootElement);
        }
        catch (JsonException)
        {
            return AndersAlsErwartet;
        }
    }

    /// <summary>
    /// Die Antwort von <c>GET api/ki-zugriff/ich</c> in Klartext.
    /// </summary>
    /// <remarks>
    /// <para>Die Rückfrage-Regel: seit A-005 (03.10.2026) liefert Grow OS
    /// <c>rueckfrageBei</c> (eine Liste von Stufen, je Schlüssel). Ein Fork aus
    /// forkai.163 liefert noch <c>rueckfrageAbStufe</c> (eine Stufe, ab der
    /// gefragt wird; <c>null</c> = nie). Beides wird gelesen; steht beides da,
    /// gilt die Liste — sie ist die neuere und genauere Angabe.</para>
    /// <para>Öffentlich, damit beide Formen eine eigene Prüfung bekommen.</para>
    /// </remarks>
    public static string ZugriffBeschreiben(JsonElement ich)
    {
        var text = new StringBuilder();
        var name = Text(ich, "schluesselName");
        text.AppendLine(name is null ? "Verbunden mit einem Schlüssel aus Grow OS." : $"Verbunden mit dem Schlüssel „{name}\" aus Grow OS.");

        var frei = Liste(ich, "stufen");
        text.AppendLine(frei.Count == 0
            ? "Freigegeben: keine Stufe — es geht nur lesen."
            : "Freigegeben: " + string.Join(", ", frei.Select(Stufen.Text)) + ".");

        var gesperrt = Stufen.Alle.Where(s => !frei.Contains(s.Name, StringComparer.OrdinalIgnoreCase)).Select(s => s.Text).ToList();
        if (gesperrt.Count > 0)
        {
            text.AppendLine("Nicht freigegeben: " + string.Join(", ", gesperrt) + " — dafür lehnt Grow OS ab.");
        }

        var rueckfrage = RueckfrageBei(ich);
        text.AppendLine(rueckfrage switch
        {
            null => "Rückfrage: Grow OS sagt dazu nichts — im Zweifel vor allem außer Dokumentieren nachfragen.",
            { Count: 0 } => "Rückfrage: nie — der Betreiber will vorher nicht gefragt werden.",
            _ => "Vorher den Betreiber fragen bei: " + string.Join(", ", rueckfrage.Select(Stufen.Text))
                 + ". Erst nach seinem Ja ausführen.",
        });

        if (ich.TryGetProperty("hoechstwerte", out var hoechst) && hoechst.ValueKind == JsonValueKind.Object)
        {
            var teile = new List<string>();
            if (hoechst.TryGetProperty("maxDosisMlJeBefehl", out var ml) && ml.ValueKind == JsonValueKind.Number)
                teile.Add($"höchstens {ml.GetDouble().ToString("0.##", CultureInfo.GetCultureInfo("de-DE"))} ml je Dosierbefehl");
            if (hoechst.TryGetProperty("maxSchaltbefehleJeStunde", out var anzahl) && anzahl.ValueKind == JsonValueKind.Number)
                teile.Add($"höchstens {anzahl.GetInt32()} Schalt- und Dosierbefehle je Stunde (über alle Schlüssel)");
            if (teile.Count > 0) text.AppendLine("Höchstwerte: " + string.Join("; ", teile) + ".");
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>Bei welchen Stufen vorher gefragt werden soll — leer heisst nie, <c>null</c> heisst: Grow OS sagt nichts.</summary>
    public static IReadOnlyList<string>? RueckfrageBei(JsonElement ich)
    {
        if (ich.TryGetProperty("rueckfrageBei", out var liste))
        {
            if (liste.ValueKind == JsonValueKind.Array) return Liste(ich, "rueckfrageBei");
            if (liste.ValueKind == JsonValueKind.Null) return [];
        }

        if (ich.TryGetProperty("rueckfrageAbStufe", out var ab))
        {
            if (ab.ValueKind == JsonValueKind.Null) return [];
            if (ab.ValueKind == JsonValueKind.String && ab.GetString() is { Length: > 0 } stufe)
            {
                var rang = Stufen.Rang(stufe);
                return rang < 0 ? [stufe] : Stufen.Alle.Skip(rang).Select(s => s.Name).ToList();
            }
            return [];
        }

        return null;
    }

    // ------------------------------------------------------------------ Dokumentieren

    [McpServerTool(Name = "messung_eintragen")]
    [Description($"Trägt eine neue Messung in einen Grow ein — das, was der Betreiber diktiert („pH 5,8, EC 1,2, ORP 450, 3 Liter nachgefüllt\"). Nur nennen, was gesagt wurde. Ohne Phase gilt die aktuelle Phase des Grows. {StufeDokumentieren}")]
    [BrauchtForkSchluessel(Stufen.Dokumentieren)]
    public async Task<string> MessungEintragenAsync(
        [Description("Die Id des Grows (aus grows_auflisten)")] int growId,
        [Description($"Zeitpunkt der Messung. {ZeitpunktText}")] string? zeitpunkt = null,
        [Description("Phase, etwa Veg, Blüte, Finish. Weglassen = die aktuelle Phase des Grows.")] string? phase = null,
        [Description("pH im Reservoir")] double? ph = null,
        [Description("EC im Reservoir in mS/cm")] double? ec = null,
        [Description("Wassertemperatur im Reservoir in °C")] double? wassertemperaturC = null,
        [Description("ORP (Redoxpotential) in mV")] double? orpMv = null,
        [Description("Gelöster Sauerstoff in mg/L")] double? sauerstoffMgL = null,
        [Description("Füllstand des Reservoirs in Litern")] double? fuellstandLiter = null,
        [Description("Füllstand des Reservoirs in cm")] double? fuellstandCm = null,
        [Description("Nachgefüllte Menge (Top-Off) in Litern")] double? nachfuellLiter = null,
        [Description("EC der Nachfülllösung (Addback) in mS/cm")] double? nachfuellEc = null,
        [Description("Lufttemperatur in °C")] double? lufttemperaturC = null,
        [Description("Relative Luftfeuchte in %")] double? luftfeuchteProzent = null,
        [Description("CO₂ in ppm")] double? co2Ppm = null,
        [Description("PPFD in µmol/m²/s")] double? ppfd = null,
        [Description("Luftstrom am Blatt in m/min")] double? luftstromAmBlattMProMin = null,
        [Description("Pflanzenhöhe in cm")] double? hoeheCm = null,
        [Description("Gießmenge in ml (Erde/Kokos)")] double? giessmengeMl = null,
        [Description("Ablaufmenge (Runoff) in ml")] double? ablaufMl = null,
        [Description("pH des Gießwassers")] double? giesswasserPh = null,
        [Description("EC des Gießwassers in mS/cm")] double? giesswasserEc = null,
        [Description("pH des Drain")] double? drainPh = null,
        [Description("EC des Drain in mS/cm")] double? drainEc = null,
        [Description("Strömung im System: schwach, mittel oder stark")] string? stroemung = null,
        [Description("true, wenn bei dieser Messung die Lösung komplett gewechselt wurde")] bool? wasserwechsel = null,
        [Description("Freitext, etwa „2 ml pH-Minus, 300 ml Pyrolyt\"")] string? notiz = null,
        CancellationToken cancellationToken = default)
    {
        if (!reader.HatForkSchluessel) return ForkFehler.SchluesselNoetig;

        var werte = new Messwerte
        {
            Ph = ph, Ec = ec, WassertemperaturC = wassertemperaturC, OrpMv = orpMv, SauerstoffMgL = sauerstoffMgL,
            FuellstandLiter = fuellstandLiter, FuellstandCm = fuellstandCm, NachfuellLiter = nachfuellLiter,
            NachfuellEc = nachfuellEc, LufttemperaturC = lufttemperaturC, LuftfeuchteProzent = luftfeuchteProzent,
            Co2Ppm = co2Ppm, Ppfd = ppfd, LuftstromAmBlattMProMin = luftstromAmBlattMProMin, HoeheCm = hoeheCm,
            GiessmengeMl = giessmengeMl, AblaufMl = ablaufMl, GiesswasserPh = giesswasserPh, GiesswasserEc = giesswasserEc,
            DrainPh = drainPh, DrainEc = drainEc, Stroemung = stroemung, Wasserwechsel = wasserwechsel, Notiz = notiz,
        };
        if (Messfelder.Leer(werte)) return "Nichts eingetragen: es wurde kein Wert genannt.";

        var rumpf = new JsonObject();
        if (zeitpunkt is not null)
        {
            if (Messfelder.Ortszeit(zeitpunkt) is not { } zeit) return ZeitpunktUnlesbar(zeitpunkt);
            rumpf[Messfelder.FeldZeitpunkt] = zeit;
        }

        // Ohne Phase die des Grows — NICHT still „Veg". Grow OS setzt im Vertrag
        // Veg als Vorgabe; eine Blüte-Messung, die als Veg gespeichert wird,
        // wird gegen die falschen Sollwerte beurteilt, und niemand sieht es.
        string? stufe;
        if (phase is not null)
        {
            stufe = Messfelder.Phase(phase);
            if (stufe is null) return PhaseUnbekannt(phase);
        }
        else
        {
            var grow = await SendenAsync(HttpMethod.Get, $"api/grows/{growId}", null, cancellationToken);
            if (grow.Fehler is { } fehler) return fehler;
            if (grow.Antwort!.Status == 404) return $"Einen Grow mit der Id {growId} gibt es nicht. Mit grows_auflisten bekommst du die gültigen Ids.";
            if (!grow.Antwort.Erfolg) return ForkFehler.Text(grow.Antwort.Status, grow.Antwort.Text, "Phase des Grows lesen");

            stufe = Messfelder.Phase(TextAus(grow.Antwort.Text, "currentStage"));
            if (stufe is null)
            {
                return $"Die aktuelle Phase von Grow {growId} ließ sich nicht lesen. Bitte die Phase ausdrücklich angeben (etwa Veg oder Blüte).";
            }
        }
        rumpf[Messfelder.FeldPhase] = stufe;
        Messfelder.Einsetzen(rumpf, werte);

        return await SchreibenAsync(HttpMethod.Post, $"api/grows/{growId}/measurements", rumpf, "Messung eintragen",
            json => $"Eingetragen als Messung {Zahl(json, "id")} bei Grow {growId}, Phase {Text(json, "stage") ?? stufe}. "
                    + "Grow OS hat gespeichert:", cancellationToken);
    }

    [McpServerTool(Name = "messung_aendern")]
    [Description($"Ändert eine vorhandene Messung — nur die genannten Werte, alle anderen bleiben, wie sie sind. Die Messungs-Id steht in der Antwort von messung_eintragen oder im lagebericht. Löschen geht hier nicht. {StufeDokumentieren}")]
    [BrauchtForkSchluessel(Stufen.Dokumentieren)]
    public async Task<string> MessungAendernAsync(
        [Description("Die Id der Messung")] int messungId,
        [Description("Neuer Zeitpunkt, Ortszeit, etwa 2026-10-03T14:30 oder 03.10.2026 14:30. Weglassen = bleibt.")] string? zeitpunkt = null,
        [Description("Neue Phase, etwa Veg, Blüte, Finish. Weglassen = bleibt.")] string? phase = null,
        [Description("pH im Reservoir")] double? ph = null,
        [Description("EC im Reservoir in mS/cm")] double? ec = null,
        [Description("Wassertemperatur im Reservoir in °C")] double? wassertemperaturC = null,
        [Description("ORP (Redoxpotential) in mV")] double? orpMv = null,
        [Description("Gelöster Sauerstoff in mg/L")] double? sauerstoffMgL = null,
        [Description("Füllstand des Reservoirs in Litern")] double? fuellstandLiter = null,
        [Description("Füllstand des Reservoirs in cm")] double? fuellstandCm = null,
        [Description("Nachgefüllte Menge (Top-Off) in Litern")] double? nachfuellLiter = null,
        [Description("EC der Nachfülllösung (Addback) in mS/cm")] double? nachfuellEc = null,
        [Description("Lufttemperatur in °C")] double? lufttemperaturC = null,
        [Description("Relative Luftfeuchte in %")] double? luftfeuchteProzent = null,
        [Description("CO₂ in ppm")] double? co2Ppm = null,
        [Description("PPFD in µmol/m²/s")] double? ppfd = null,
        [Description("Luftstrom am Blatt in m/min")] double? luftstromAmBlattMProMin = null,
        [Description("Pflanzenhöhe in cm")] double? hoeheCm = null,
        [Description("Gießmenge in ml (Erde/Kokos)")] double? giessmengeMl = null,
        [Description("Ablaufmenge (Runoff) in ml")] double? ablaufMl = null,
        [Description("pH des Gießwassers")] double? giesswasserPh = null,
        [Description("EC des Gießwassers in mS/cm")] double? giesswasserEc = null,
        [Description("pH des Drain")] double? drainPh = null,
        [Description("EC des Drain in mS/cm")] double? drainEc = null,
        [Description("Strömung im System: schwach, mittel oder stark")] string? stroemung = null,
        [Description("true/false: wurde die Lösung komplett gewechselt")] bool? wasserwechsel = null,
        [Description("Neue Notiz (ersetzt die alte)")] string? notiz = null,
        CancellationToken cancellationToken = default)
    {
        if (!reader.HatForkSchluessel) return ForkFehler.SchluesselNoetig;

        var werte = new Messwerte
        {
            Ph = ph, Ec = ec, WassertemperaturC = wassertemperaturC, OrpMv = orpMv, SauerstoffMgL = sauerstoffMgL,
            FuellstandLiter = fuellstandLiter, FuellstandCm = fuellstandCm, NachfuellLiter = nachfuellLiter,
            NachfuellEc = nachfuellEc, LufttemperaturC = lufttemperaturC, LuftfeuchteProzent = luftfeuchteProzent,
            Co2Ppm = co2Ppm, Ppfd = ppfd, LuftstromAmBlattMProMin = luftstromAmBlattMProMin, HoeheCm = hoeheCm,
            GiessmengeMl = giessmengeMl, AblaufMl = ablaufMl, GiesswasserPh = giesswasserPh, GiesswasserEc = giesswasserEc,
            DrainPh = drainPh, DrainEc = drainEc, Stroemung = stroemung, Wasserwechsel = wasserwechsel, Notiz = notiz,
        };
        if (Messfelder.Leer(werte) && zeitpunkt is null && phase is null) return "Nichts geändert: es wurde kein neuer Wert genannt.";

        string? neueZeit = null;
        if (zeitpunkt is not null && (neueZeit = Messfelder.Ortszeit(zeitpunkt)) is null) return ZeitpunktUnlesbar(zeitpunkt);

        string? neuePhase = null;
        if (phase is not null && (neuePhase = Messfelder.Phase(phase)) is null) return PhaseUnbekannt(phase);

        // Grow OS ersetzt beim Ändern die GANZE Messung (PUT). Wer nur den pH
        // schickt, löscht damit EC, ORP und alles andere. Deshalb zuerst den
        // gespeicherten Stand holen und nur die genannten Werte darüberlegen.
        var vorher = await SendenAsync(HttpMethod.Get, $"api/measurements/{messungId}", null, cancellationToken);
        if (vorher.Fehler is { } fehler) return fehler;
        if (!vorher.Antwort!.Erfolg) return ForkFehler.Text(vorher.Antwort.Status, vorher.Antwort.Text, "Messung lesen");

        JsonObject rumpf;
        try
        {
            rumpf = JsonNode.Parse(vorher.Antwort.Text) as JsonObject ?? throw new JsonException();
        }
        catch (JsonException)
        {
            return AndersAlsErwartet;
        }

        // Was die Antwort mehr hat als die Anfrage braucht: Kennungen und den
        // Zeitpunkt in UTC. Ohne takenAtLocal behält Grow OS den gespeicherten.
        foreach (var feld in new[] { "id", "growId", "takenAt" }) rumpf.Remove(feld);
        if (neueZeit is not null) rumpf[Messfelder.FeldZeitpunkt] = neueZeit;
        if (neuePhase is not null) rumpf[Messfelder.FeldPhase] = neuePhase;
        Messfelder.Einsetzen(rumpf, werte);

        return await SchreibenAsync(HttpMethod.Put, $"api/measurements/{messungId}", rumpf, "Messung ändern",
            _ => $"Messung {messungId} geändert. Grow OS hat gespeichert:", cancellationToken);
    }

    /// <summary>Die Arten eines Journaleintrags, die ein Mensch selbst wählt (<c>JournalEntryType</c>).</summary>
    /// <remarks>
    /// Die Meilensteine (Keimung, Bewurzelung, Veg-Beginn, Flip, Finish) fehlen
    /// mit Absicht: die schreibt Grow OS selbst, wenn sie mit phase_bestaetigen
    /// bestätigt werden — ein Journaleintrag allein verschöbe keine Phase.
    /// <c>MessfelderTests</c> prüft die linke Spalte gegen das Enum in Grow OS.
    /// </remarks>
    public static IReadOnlyList<(string Wert, string[] Woerter)> JournalArten { get; } =
    [
        ("Note", ["notiz", "note"]),
        ("Observation", ["beobachtung", "observation"]),
        ("Action", ["maßnahme", "massnahme", "handlung", "aktion", "action"]),
        ("Problem", ["problem"]),
        ("Solution", ["lösung", "loesung", "solution"]),
        ("Training", ["training", "lst", "topping"]),
        ("Transplant", ["umpflanzen", "umtopfen", "transplant"]),
        ("Feeding", ["düngen", "duengen", "fütterung", "feeding"]),
        ("ReservoirChange", ["wasserwechsel", "reservoirwechsel", "reservoirchange"]),
    ];

    [McpServerTool(Name = "journal_eintragen")]
    [Description($"Schreibt einen Eintrag ins Journal eines Grows — was der Betreiber getan oder beobachtet hat, in seinen Worten. Für Messwerte messung_eintragen nehmen. {StufeDokumentieren}")]
    [BrauchtForkSchluessel(Stufen.Dokumentieren)]
    public async Task<string> JournalEintragenAsync(
        [Description("Die Id des Grows")] int growId,
        [Description("Der Text des Eintrags")] string text,
        [Description("Kurzer Titel; weglassen geht")] string? titel = null,
        [Description("Art: Notiz, Beobachtung, Maßnahme, Problem, Lösung, Training, Umpflanzen, Düngen, Wasserwechsel. Weglassen = Notiz.")] string? art = null,
        [Description(ZeitpunktText)] string? zeitpunkt = null,
        CancellationToken cancellationToken = default)
    {
        if (!reader.HatForkSchluessel) return ForkFehler.SchluesselNoetig;
        if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(titel)) return "Nichts eingetragen: der Eintrag ist leer.";

        var rumpf = new JsonObject { ["body"] = text?.Trim() };
        if (!string.IsNullOrWhiteSpace(titel)) rumpf["title"] = titel.Trim();

        if (art is not null)
        {
            var wert = Auswahl(JournalArten, art);
            if (wert is null)
            {
                return $"„{art}\" kennt das Journal nicht. Möglich: Notiz, Beobachtung, Maßnahme, Problem, Lösung, Training, Umpflanzen, Düngen, Wasserwechsel.";
            }
            rumpf["entryType"] = wert;
        }

        if (zeitpunkt is not null)
        {
            if (Messfelder.Ortszeit(zeitpunkt) is not { } zeit) return ZeitpunktUnlesbar(zeitpunkt);
            rumpf["occurredAtLocal"] = zeit;
        }

        return await SchreibenAsync(HttpMethod.Post, $"api/grows/{growId}/journal", rumpf, "Journal eintragen",
            json => $"Im Journal von Grow {growId} eingetragen (Eintrag {Zahl(json, "id")}):", cancellationToken);
    }

    [McpServerTool(Name = "journal_aendern")]
    [Description($"Ändert einen vorhandenen Journal-Eintrag: Text ersetzen, eine Zeile anhängen, Titel, Art oder Zeitpunkt korrigieren. Nur was genannt wird, ändert sich. Die Eintrags-Id liefert das Werkzeug journal. {StufeDokumentieren}")]
    [BrauchtForkSchluessel(Stufen.Dokumentieren)]
    public async Task<string> JournalAendernAsync(
        [Description("Die Id des Journal-Eintrags")] int eintragId,
        [Description("Neuer Text (ersetzt den alten ganz); leer = Text leeren")] string? text = null,
        [Description("Eine Zeile, die unten an den Text angehängt wird — der alte Text bleibt")] string? anhaengen = null,
        [Description("Neuer Titel; leer = Titel leeren")] string? titel = null,
        [Description("Neue Art: Notiz, Beobachtung, Maßnahme, Problem, Lösung, Training, Umpflanzen, Düngen, Wasserwechsel")] string? art = null,
        [Description("Neuer Zeitpunkt in Ortszeit, etwa 2026-10-03T14:30 oder 03.10.2026 14:30. Weglassen = bleibt.")] string? zeitpunkt = null,
        CancellationToken cancellationToken = default)
    {
        if (!reader.HatForkSchluessel) return ForkFehler.SchluesselNoetig;
        if (text is null && anhaengen is null && titel is null && art is null && zeitpunkt is null)
        {
            return "Nichts geändert: es wurde weder Text, Titel, Art noch Zeitpunkt genannt.";
        }

        if (text is not null && !string.IsNullOrWhiteSpace(anhaengen))
        {
            return "Entweder den Text ersetzen oder eine Zeile anhängen — nicht beides in einem Schritt.";
        }

        // Grow OS lässt beim Ändern jedes Feld, das fehlt (null), wie es ist.
        // Darum geht hier nur mit, was genannt wurde.
        var rumpf = new JsonObject();
        if (text is not null) rumpf["body"] = text.Trim();
        if (titel is not null) rumpf["title"] = titel.Trim();

        if (art is not null)
        {
            var wert = Auswahl(JournalArten, art);
            if (wert is null)
            {
                return $"„{art}\" kennt das Journal nicht. Möglich: Notiz, Beobachtung, Maßnahme, Problem, Lösung, Training, Umpflanzen, Düngen, Wasserwechsel.";
            }
            rumpf["entryType"] = wert;
        }

        if (zeitpunkt is not null)
        {
            if (Messfelder.Ortszeit(zeitpunkt) is not { } zeit) return ZeitpunktUnlesbar(zeitpunkt);
            rumpf["occurredAtLocal"] = zeit;
        }

        if (!string.IsNullOrWhiteSpace(anhaengen))
        {
            // Anhängen braucht den gespeicherten Text — sonst würde die neue
            // Zeile den alten Eintrag ersetzen.
            var vorher = await SendenAsync(HttpMethod.Get, $"api/journal/{eintragId}", null, cancellationToken);
            if (vorher.Fehler is { } fehler) return fehler;
            if (!vorher.Antwort!.Erfolg) return ForkFehler.Text(vorher.Antwort.Status, vorher.Antwort.Text, "Journal-Eintrag lesen");

            string? alterText;
            try
            {
                using var dokument = JsonDocument.Parse(vorher.Antwort.Text);
                alterText = Text(dokument.RootElement, "body");
            }
            catch (JsonException)
            {
                return AndersAlsErwartet;
            }

            rumpf["body"] = string.IsNullOrWhiteSpace(alterText)
                ? anhaengen.Trim()
                : alterText.TrimEnd() + "\n" + anhaengen.Trim();
        }

        return await SchreibenAsync(HttpMethod.Put, $"api/journal/{eintragId}", rumpf, "Journal-Eintrag ändern",
            _ => $"Journal-Eintrag {eintragId} geändert. Grow OS hat gespeichert:", cancellationToken);
    }

    [McpServerTool(Name = "aufgabe_erledigen")]
    [Description($"Hakt eine Aufgabe ab (oder setzt sie auf übersprungen bzw. wieder offen). Die Aufgaben-Id liefert das Werkzeug aufgaben. {StufeDokumentieren}")]
    [BrauchtForkSchluessel(Stufen.Dokumentieren)]
    public async Task<string> AufgabeErledigenAsync(
        [Description("Die Id der Aufgabe")] int aufgabeId,
        [Description("erledigt (Vorgabe), übersprungen oder offen")] string status = "erledigt",
        CancellationToken cancellationToken = default)
    {
        if (!reader.HatForkSchluessel) return ForkFehler.SchluesselNoetig;

        var wert = Auswahl(AufgabenStatus, status);
        if (wert is null) return $"„{status}\" geht nicht. Möglich: erledigt, übersprungen, offen.";

        return await SchreibenAsync(HttpMethod.Patch, $"api/tasks/{aufgabeId}/status", new JsonObject { ["status"] = wert },
            "Aufgabe abhaken", json => $"Aufgabe {aufgabeId} „{Text(json, "title")}\" steht jetzt auf {status}:", cancellationToken);
    }

    /// <summary>Die Zustände einer Aufgabe (<c>GrowTaskStatus</c>) — geprüft gegen Grow OS in <c>MessfelderTests</c>.</summary>
    public static IReadOnlyList<(string Wert, string[] Woerter)> AufgabenStatus { get; } =
    [
        ("Open", ["offen", "open"]),
        ("Done", ["erledigt", "fertig", "done"]),
        ("Skipped", ["übersprungen", "uebersprungen", "skipped"]),
    ];

    [McpServerTool(Name = "wartung_eintragen")]
    [Description($"Hält fest, dass eine anstehende Wartung gemacht ist; Grow OS plant danach den nächsten Termin. Die Wartungs-Id steht im Werkzeug technik unter „wartung\". {StufeDokumentieren}")]
    [BrauchtForkSchluessel(Stufen.Dokumentieren)]
    public async Task<string> WartungEintragenAsync(
        [Description("Die Id der Wartung aus technik")] int wartungId,
        [Description("Was gemacht oder gefunden wurde")] string? notiz = null,
        [Description("true, wenn dabei etwas aufgefallen ist, das noch erledigt werden muss (Teil verschlissen …)")] bool handlungNoetig = false,
        [Description("Wann sie gemacht wurde. Ortszeit oder mit Zeitzone. Weglassen = jetzt.")] string? zeitpunkt = null,
        CancellationToken cancellationToken = default)
    {
        if (!reader.HatForkSchluessel) return ForkFehler.SchluesselNoetig;

        var rumpf = new JsonObject { ["actionNeeded"] = handlungNoetig };
        if (!string.IsNullOrWhiteSpace(notiz)) rumpf["notes"] = notiz.Trim();
        if (zeitpunkt is not null)
        {
            if (Messfelder.Utc(zeitpunkt) is not { } zeit) return ZeitpunktUnlesbar(zeitpunkt);
            rumpf["performedAtUtc"] = zeit;
        }

        return await SchreibenAsync(HttpMethod.Post, $"api/maintenance-events/{wartungId}/complete", rumpf, "Wartung festhalten",
            json => $"Wartung {wartungId} „{Text(json, "title")}\" ist festgehalten:", cancellationToken);
    }

    [McpServerTool(Name = "kalibrierung_eintragen")]
    [Description($"Hält fest, dass eine anstehende Sonden-Kalibrierung gemacht ist, mit Referenz und Werten vorher/nachher; Grow OS plant danach den nächsten Termin. Die Id steht im Werkzeug technik unter „kalibrierung\". {StufeDokumentieren}")]
    [BrauchtForkSchluessel(Stufen.Dokumentieren)]
    public async Task<string> KalibrierungEintragenAsync(
        [Description("Die Id der Kalibrierung aus technik")] int kalibrierungId,
        [Description("Referenzlösung, etwa „pH 7,00\" oder „EC 1,413\"")] string? referenzloesung = null,
        [Description("Sollwert der Referenzlösung")] double? referenzwert = null,
        [Description("Anzeige der Sonde vor dem Abgleich")] double? vorher = null,
        [Description("Anzeige der Sonde nach dem Abgleich")] double? nachher = null,
        [Description("Temperatur der Lösung in °C")] double? temperaturC = null,
        [Description("Notiz")] string? notiz = null,
        [Description("true, wenn die Sonde den Referenzwert nicht mehr annimmt")] bool misslungen = false,
        [Description("Wann kalibriert wurde. Ortszeit oder mit Zeitzone. Weglassen = jetzt.")] string? zeitpunkt = null,
        CancellationToken cancellationToken = default)
    {
        if (!reader.HatForkSchluessel) return ForkFehler.SchluesselNoetig;

        var rumpf = new JsonObject { ["failed"] = misslungen };
        if (!string.IsNullOrWhiteSpace(referenzloesung)) rumpf["referenceSolution"] = referenzloesung.Trim();
        if (referenzwert is { } soll) rumpf["referenceValue"] = soll;
        if (vorher is { } vor) rumpf["beforeValue"] = vor;
        if (nachher is { } nach) rumpf["afterValue"] = nach;
        if (temperaturC is { } temperatur) rumpf["temperatureC"] = temperatur;
        if (!string.IsNullOrWhiteSpace(notiz)) rumpf["notes"] = notiz.Trim();
        if (zeitpunkt is not null)
        {
            if (Messfelder.Utc(zeitpunkt) is not { } zeit) return ZeitpunktUnlesbar(zeitpunkt);
            rumpf["performedAtUtc"] = zeit;
        }

        return await SchreibenAsync(HttpMethod.Post, $"api/calibration-events/{kalibrierungId}/complete", rumpf, "Kalibrierung festhalten",
            json => $"Kalibrierung {kalibrierungId} „{Text(json, "title")}\" ist festgehalten:", cancellationToken);
    }

    // ------------------------------------------------------------------ Grow planen

    /// <summary>Die Bestätigungen im Ablauf eines Grows — die Knöpfe auf der Grow-Seite.</summary>
    /// <remarks>Rechts die Wege in Grow OS (<c>GrowWorkflowApiController</c>, <c>actions/…</c>); <c>ForkWegeTests</c> prüft, dass es sie gibt.</remarks>
    public static IReadOnlyList<(string Schritt, string Weg, string[] Woerter)> PhasenSchritte { get; } =
    [
        ("keimung", "confirm-germination", ["keimung", "gekeimt", "germination"]),
        ("bewurzelung", "confirm-rooting", ["bewurzelung", "bewurzelt", "rooting"]),
        ("veg", "confirm-veg", ["veg", "vegi", "wachstum"]),
        ("bluete", "flip-to-flower", ["bluete", "blüte", "flip", "12/12"]),
        ("finish", "confirm-finish", ["finish", "spülen", "spuelen"]),
    ];

    [McpServerTool(Name = "phase_bestaetigen")]
    [Description($"Bestätigt einen Schritt im Ablauf eines Grows, wie die Knöpfe auf der Grow-Seite: keimung (Samen gekeimt), bewurzelung (Steckling bewurzelt), veg (Vegi beginnt), bluete (Flip auf 12/12 bzw. Blütebeginn bei Autoflower), finish (Trichome reif, Spülen beginnt). Ab dann gelten die Sollwerte der neuen Phase. {StufeGrowPlanen}")]
    [BrauchtForkSchluessel(Stufen.GrowPlanen)]
    public async Task<string> PhaseBestaetigenAsync(
        [Description("Die Id des Grows")] int growId,
        [Description("keimung, bewurzelung, veg, bluete oder finish")][Beispiel("veg")] string schritt,
        CancellationToken cancellationToken = default)
    {
        if (!reader.HatForkSchluessel) return ForkFehler.SchluesselNoetig;

        var wort = schritt.Trim().ToLowerInvariant();
        var treffer = PhasenSchritte.FirstOrDefault(s => s.Woerter.Contains(wort));
        if (treffer.Weg is null) return $"„{schritt}\" ist kein Schritt. Möglich: keimung, bewurzelung, veg, bluete, finish.";

        return await SchreibenAsync(HttpMethod.Post, $"api/grows/{growId}/actions/{treffer.Weg}", null, "Phase bestätigen",
            json =>
            {
                var meldung = Text(json, "message") ?? "Bestätigt.";
                var jetzt = json.TryGetProperty("grow", out var grow) ? Text(grow, "currentStage") : null;
                return jetzt is null ? meldung : $"{meldung} Phase jetzt: {jetzt}.";
            }, cancellationToken, mitRumpf: false);
    }

    // ------------------------------------------------------------------ Geräte schalten

    [McpServerTool(Name = "pumpe_dosieren")]
    [Description($"Lässt eine Dosierpumpe eine Menge in ml fördern. Grow OS prüft vorher seine eigenen Sperren und den Höchstwert je Befehl (zugriff_pruefen) und meldet, ob wirklich dosiert wurde. Pumpen-Id aus dosierungen; was Grow OS selbst empfehlen würde, sagt dosier_vorschlag. {StufeSchalten}")]
    [BrauchtForkSchluessel(Stufen.GeraeteSchalten)]
    public async Task<string> PumpeDosierenAsync(
        [Description("Die Id der Pumpe aus dosierungen")] int pumpeId,
        [Description("Menge in ml")][Beispiel("2")] double ml,
        CancellationToken cancellationToken = default)
    {
        if (!reader.HatForkSchluessel) return ForkFehler.SchluesselNoetig;
        if (ml <= 0) return "Nicht dosiert: die Menge muss größer als 0 ml sein.";

        return await SchreibenAsync(HttpMethod.Post, $"api/dosing/pumps/{pumpeId}/dose", new JsonObject { ["ml"] = ml },
            "Dosieren", DosisBeschreiben, cancellationToken, mitRumpf: false);
    }

    [McpServerTool(Name = "pumpe_stoppen")]
    [Description($"Schaltet eine Dosierpumpe sofort aus. Scheitert nie an einem Höchstwert — Ausschalten geht immer, auch wenn die Pumpe laut Grow OS schon steht. {StufeSchalten}")]
    [BrauchtForkSchluessel(Stufen.GeraeteSchalten)]
    public async Task<string> PumpeStoppenAsync(
        [Description("Die Id der Pumpe aus dosierungen")] int pumpeId,
        CancellationToken cancellationToken = default)
    {
        if (!reader.HatForkSchluessel) return ForkFehler.SchluesselNoetig;

        return await SchreibenAsync(HttpMethod.Post, $"api/dosing/pumps/{pumpeId}/stop", null, "Pumpe stoppen",
            json => Text(json, "reason") ?? "Ausgeschaltet.", cancellationToken, mitRumpf: false);
    }

    [McpServerTool(Name = "licht_schalten")]
    [Description($"Schaltet das Licht über die Steuerung von Grow OS: an, aus, ein Preset (veggie = 18/6, bluete = 12/12) oder eine Dimmstufe 1–10; quittieren räumt eine gemeldete Fehlmeldung weg. Meldet, ob Home Assistant den Befehl angenommen hat. {StufeSchalten}")]
    [BrauchtForkSchluessel(Stufen.GeraeteSchalten)]
    public async Task<string> LichtSchaltenAsync(
        [Description("an, aus, preset, stufe oder quittieren")][Beispiel("an")] string befehl,
        [Description("Nur bei befehl=preset: veggie oder bluete")] string? preset = null,
        [Description("Nur bei befehl=stufe: 1 bis 10")] int? stufe = null,
        CancellationToken cancellationToken = default)
    {
        if (!reader.HatForkSchluessel) return ForkFehler.SchluesselNoetig;

        // Die Wörter, die Grow OS annimmt: SteuerungApiController.LichtBefehl
        // („aus", „an", „preset", „stufe", „quittieren") und
        // LichtSteuerungService.BefehlAsync („veggie", „bluete", Stufe 1–10).
        var art = befehl.Trim().ToLowerInvariant() switch
        {
            "ein" or "einschalten" or "an" => "an",
            "aus" or "ausschalten" => "aus",
            "preset" or "zeitplan" => "preset",
            "stufe" or "dimmen" => "stufe",
            "quittieren" => "quittieren",
            _ => null,
        };
        if (art is null) return $"„{befehl}\" ist kein Lichtbefehl. Möglich: an, aus, preset, stufe, quittieren.";

        var rumpf = new JsonObject { ["art"] = art };
        if (art == "preset")
        {
            var name = preset?.Trim().ToLowerInvariant() switch
            {
                "veggie" or "veg" or "wachstum" or "18/6" => "veggie",
                "bluete" or "blüte" or "12/12" => "bluete",
                _ => null,
            };
            if (name is null) return "Für ein Preset bitte veggie (18/6) oder bluete (12/12) angeben.";
            rumpf["preset"] = name;
        }
        if (art == "stufe")
        {
            if (stufe is not (>= 1 and <= 10)) return "Für die Dimmstufe bitte eine Zahl von 1 bis 10 angeben.";
            rumpf["stufe"] = stufe;
        }

        return await SchreibenAsync(HttpMethod.Post, "api/steuerung/licht/befehl", rumpf, "Licht schalten",
            json => json.TryGetProperty("haAngenommen", out var an) && an.ValueKind == JsonValueKind.False
                ? "Grow OS hat den Befehl geschickt, aber Home Assistant hat ihn NICHT angenommen — das Licht hat sich vermutlich nicht verändert. Stand laut Grow OS:"
                : "Befehl angenommen. Stand laut Grow OS:", cancellationToken);
    }

    // ------------------------------------------------------------------ Hilfe

    internal const string AndersAlsErwartet =
        "Grow OS hat anders geantwortet als erwartet. Vermutlich passen die Fassungen von Grow OS und Grow MCP nicht zusammen — bitte beide aktualisieren.";

    /// <summary>Was ein Dosierbefehl bewirkt hat — „angenommen" ist nicht „dosiert".</summary>
    private static string DosisBeschreiben(JsonElement json)
    {
        var grund = Text(json, "reason");
        if (json.TryGetProperty("dosed", out var dosiert) && dosiert.ValueKind == JsonValueKind.True)
        {
            var ml = json.TryGetProperty("ml", out var menge) && menge.ValueKind == JsonValueKind.Number
                ? menge.GetDouble().ToString("0.##", CultureInfo.GetCultureInfo("de-DE"))
                : "?";
            return $"Dosiert: {ml} ml. {grund}".TrimEnd();
        }
        return $"NICHT dosiert. {grund ?? "Grow OS hat keinen Grund genannt."}";
    }

    /// <summary>Ergebnis einer Anfrage: entweder eine Antwort oder schon der Satz für den Assistenten.</summary>
    private readonly record struct Ergebnis(ForkAntwort? Antwort, string? Fehler);

    /// <summary>Anfragen, ohne dass eine Ausnahme nach oben durchschlägt.</summary>
    private async Task<Ergebnis> SendenAsync(HttpMethod methode, string pfad, JsonObject? rumpf, CancellationToken cancellationToken)
    {
        try
        {
            return new Ergebnis(await reader.SendenAsync(methode, pfad, rumpf?.ToJsonString(Ausgabe), cancellationToken), null);
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
            return new Ergebnis(null, "Grow OS hat nicht rechtzeitig geantwortet. Ob der Befehl ankam, ist offen — vor einem zweiten Versuch mit dem passenden Lese-Werkzeug nachsehen.");
        }
    }

    /// <summary>
    /// Schreiben und die Antwort in einen Satz fassen — oder die Absage.
    /// </summary>
    /// <param name="mitRumpf">Die rohe Antwort von Grow OS anhängen. Bei kurzen Bestätigungen genügt der Satz.</param>
    private async Task<string> SchreibenAsync(
        HttpMethod methode, string pfad, JsonObject? rumpf, string was,
        Func<JsonElement, string> erfolg, CancellationToken cancellationToken, bool mitRumpf = true)
    {
        var ergebnis = await SendenAsync(methode, pfad, rumpf, cancellationToken);
        if (ergebnis.Fehler is { } fehler) return fehler;

        var antwort = ergebnis.Antwort!;
        if (!antwort.Erfolg) return ForkFehler.Text(antwort.Status, antwort.Text, was);

        if (string.IsNullOrWhiteSpace(antwort.Text)) return $"{was}: erledigt.";

        try
        {
            using var json = JsonDocument.Parse(antwort.Text);
            var satz = erfolg(json.RootElement);
            return mitRumpf ? satz + Environment.NewLine + antwort.Text : satz;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            // Gespeichert hat Grow OS trotzdem — der Status war Erfolg.
            return $"{was}: Grow OS meldet Erfolg ({antwort.Status}).";
        }
    }

    private static string ZeitpunktUnlesbar(string zeitpunkt)
        => $"Nichts ausgeführt: den Zeitpunkt „{zeitpunkt}\" kann ich nicht lesen. Bitte so angeben: 2026-10-03T14:30 oder 03.10.2026 14:30.";

    private static string PhaseUnbekannt(string phase)
        => $"Nichts ausgeführt: „{phase}\" ist keine Phase. Möglich: Sämling, Steckling, Veg, Übergang, Blüte, Finish, Trocknen, Aushärten.";

    internal static string? Auswahl(IReadOnlyList<(string Wert, string[] Woerter)> liste, string gesagt)
    {
        var wort = gesagt.Trim().ToLowerInvariant();
        foreach (var (wert, woerter) in liste)
        {
            if (string.Equals(wert, gesagt.Trim(), StringComparison.OrdinalIgnoreCase) || woerter.Contains(wort)) return wert;
        }
        return null;
    }

    private static string? TextAus(string json, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? Text(doc.RootElement, name) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string? Text(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var wert) && wert.ValueKind == JsonValueKind.String
            ? wert.GetString()
            : null;

    private static string Zahl(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var wert) && wert.ValueKind == JsonValueKind.Number
            ? wert.GetRawText()
            : "?";

    private static List<string> Liste(JsonElement element, string name)
        => element.TryGetProperty(name, out var wert) && wert.ValueKind == JsonValueKind.Array
            ? wert.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList()
            : [];
}
