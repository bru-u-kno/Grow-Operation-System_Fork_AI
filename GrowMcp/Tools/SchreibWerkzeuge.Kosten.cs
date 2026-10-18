using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrowOsAccess;
using ModelContextProtocol.Server;

namespace GrowMcp.Tools;

/// <summary>
/// Fork AI (Paket C1, 18.10.2026): Kosten buchen — Verbrauch, Nachfüllung, Anschaffung.
/// </summary>
/// <remarks>
/// <para><b>Artikel per Name:</b> „pH-Minus" findet den Artikel; passen mehrere, kommt die Liste zurück und nichts wird
/// gebucht — geraten wird nie. Ids aus <c>kosten</c> gehen immer.</para>
/// <para><b>Geld:</b> Beträge nie negativ; über <see cref="HoherBetragEur"/> € nennt die Antwort den Betrag ausdrücklich;
/// kein Zeitpunkt in der Zukunft.</para>
/// <para><b>CO₂:</b> Den Verbrauch des Artikels, den die CO₂-Steuerung selbst bucht (<c>Co2Einstellungen.KostenArtikelId</c>),
/// lehnt <c>verbrauch_buchen</c> ab — sonst stünde jedes Gramm zweimal in den Kosten.</para>
/// </remarks>
public sealed partial class SchreibWerkzeuge
{
    /// <summary>Ab diesem Betrag nennt die Antwort ihn ausdrücklich — ein Tippfehler (3800 statt 38) fällt so auf.</summary>
    public const double HoherBetragEur = 500;

    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    // ------------------------------------------------------------------ Verbrauch

    [McpServerTool(Name = "verbrauch_buchen")]
    [Description($"Bucht eine Gabe aus einem Kosten-Artikel auf einen Grow („20 ml pH-Minus\", „300 ml Purolyt\"), in der Einheit des Artikels. Artikel per Name oder Id (Liste in kosten); passen mehrere Namen, kommt die Auswahl zurück und nichts wird gebucht. Den CO₂-Artikel bucht die CO₂-Steuerung selbst — dafür lehnt das Werkzeug ab. {StufeDokumentieren}")]
    [BrauchtForkSchluessel(Stufen.Dokumentieren)]
    public async Task<string> VerbrauchBuchenAsync(
        [Description("Die Id des Grows, dem die Kosten zugerechnet werden")] int growId,
        [Description("Name oder Id des Artikels, etwa „pH-Minus\" oder 7")][Beispiel("pH")] string artikel,
        [Description("Menge in der Einheit des Artikels (ml, L, g, kg …)")][Beispiel("20")] double menge,
        [Description($"Zeitpunkt der Gabe. {ZeitpunktText}")] string? zeitpunkt = null,
        [Description("Freitext zur Gabe")] string? notiz = null,
        CancellationToken cancellationToken = default)
    {
        if (!reader.HatForkSchluessel) return ForkFehler.SchluesselNoetig;
        if (!double.IsFinite(menge) || menge <= 0) return "Nichts gebucht: die Menge muss größer als 0 sein.";
        if (ZeitpunktPruefen(zeitpunkt, out var utc) is { } zeitFehler) return zeitFehler;

        var seite = await KostenSeiteAsync(growId, cancellationToken);
        if (seite.Fehler is { } f) return f;
        var gefunden = ArtikelFinden(seite.Artikel!.Value, artikel);
        if (gefunden.Fehler is { } af) return af;
        var a = gefunden.Artikel!.Value;
        var artikelId = a.GetProperty("id").GetInt32();
        var name = Text(a, "name") ?? $"Artikel {artikelId}";
        var einheit = Text(a, "einheit") ?? string.Empty;

        // Doppelbuchung verhindern: den Artikel der CO₂-Steuerung bucht Grow OS selbst (Co2SteuerungService).
        var co2 = await SendenAsync(HttpMethod.Get, "api/steuerung/co2", null, cancellationToken);
        if (co2.Fehler is { } cf) return cf;
        if (!co2.Antwort!.Erfolg) return ForkFehler.Text(co2.Antwort.Status, co2.Antwort.Text, "CO₂-Einstellungen lesen");
        if (Co2ArtikelId(co2.Antwort.Text) == artikelId)
        {
            return $"Nichts gebucht: „{name}\" ist der Artikel, dessen Verbrauch die CO₂-Steuerung selbst bucht. Eine Buchung von Hand stünde doppelt in den Kosten. "
                   + "Eine neue Flasche trägt nachfuellung_eintragen ein.";
        }

        var zeile = new JsonObject { ["artikelId"] = artikelId, ["menge"] = menge };
        if (!string.IsNullOrWhiteSpace(notiz)) zeile["notiz"] = notiz.Trim();
        var rumpf = new JsonObject { ["growId"] = growId, ["zeilen"] = new JsonArray(zeile) };
        if (utc is not null) rumpf["zeitpunkt"] = utc;

        return await SchreibenAsync(HttpMethod.Post, "api/kosten/verbrauch", rumpf, "Verbrauch buchen",
            _ =>
            {
                var preis = a.TryGetProperty("preisEur", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : (double?)null;
                var gebinde = a.TryGetProperty("gebinde", out var g) && g.ValueKind == JsonValueKind.Number ? g.GetDouble() : (double?)null;
                var betrag = preis is { } pr && gebinde is > 0 ? pr / gebinde.Value * menge : (double?)null;
                return $"Gebucht: {menge.ToString("0.##", De)} {einheit} {name} auf Grow {growId}"
                       + (betrag is { } b ? $" (rechnerisch {b.ToString("0.00", De)} € nach Artikelpreis)" : string.Empty) + "."
                       + (betrag > HoherBetragEur ? $" ACHTUNG: ungewöhnlich hoch — {betrag.Value.ToString("0.00", De)} €. Stimmt die Menge?" : string.Empty);
            }, cancellationToken, mitRumpf: false);
    }

    // ------------------------------------------------------------------ Nachfüllung

    [McpServerTool(Name = "nachfuellung_eintragen")]
    [Description($"Trägt ein neues Gebinde eines Kosten-Artikels mit Preis ein („neue CO₂-Flasche, 10 kg für 38 €\"). vorherigeLeer sagt ausdrücklich, ob die bisher offene Füllung dieses Artikels jetzt als leer geschlossen wird — das Werkzeug macht das nie still; die Antwort nennt, was mit ihr passiert ist. Artikel per Name oder Id (kosten). {StufeDokumentieren}")]
    [BrauchtForkSchluessel(Stufen.Dokumentieren)]
    public async Task<string> NachfuellungEintragenAsync(
        [Description("Name oder Id des Artikels")][Beispiel("CO2")] string artikel,
        [Description("Menge des neuen Gebindes in der Einheit des Artikels")][Beispiel("10")] double menge,
        [Description("true: die bisher offene Füllung wird jetzt als leer geschlossen. false: sie bleibt offen. Bitte ausdrücklich angeben.")] bool vorherigeLeer,
        [Description("Was das Gebinde gekostet hat, in Euro")] double? kostenEur = null,
        [Description("Die Id des Grows; weglassen = der laufende Grow")] int? growId = null,
        [Description("true: keinem Grow zurechnen (Lager)")] bool ohneGrow = false,
        [Description($"Zeitpunkt. {ZeitpunktText}")] string? zeitpunkt = null,
        [Description("Freitext")] string? notiz = null,
        CancellationToken cancellationToken = default)
    {
        if (!reader.HatForkSchluessel) return ForkFehler.SchluesselNoetig;
        if (!double.IsFinite(menge) || menge <= 0) return "Nichts eingetragen: die Menge muss größer als 0 sein.";
        if (kostenEur is { } k && (!double.IsFinite(k) || k < 0)) return "Nichts eingetragen: der Betrag darf nicht negativ sein.";
        if (ZeitpunktPruefen(zeitpunkt, out var utc) is { } zeitFehler) return zeitFehler;

        var seite = await KostenSeiteAsync(growId, cancellationToken);
        if (seite.Fehler is { } f) return f;
        var gefunden = ArtikelFinden(seite.Artikel!.Value, artikel);
        if (gefunden.Fehler is { } af) return af;
        var a = gefunden.Artikel!.Value;
        var artikelId = a.GetProperty("id").GetInt32();
        var name = Text(a, "name") ?? $"Artikel {artikelId}";
        var einheit = Text(a, "einheit") ?? string.Empty;
        var offenSeit = a.TryGetProperty("aktuell", out var aktuell) && aktuell.ValueKind == JsonValueKind.Object
            ? Datum(aktuell, "zeitpunktUtc")
            : null;

        var rumpf = new JsonObject
        {
            ["artikelId"] = artikelId,
            ["menge"] = menge,
            ["ohneGrow"] = ohneGrow,
            // Ausdrücklich — Grow OS nimmt ohne Angabe „true".
            ["vorherigeLeer"] = vorherigeLeer,
        };
        if (kostenEur is not null) rumpf["kostenEur"] = kostenEur;
        if (growId is not null && !ohneGrow) rumpf["growId"] = growId;
        if (utc is not null) rumpf["zeitpunkt"] = utc;
        if (!string.IsNullOrWhiteSpace(notiz)) rumpf["notiz"] = notiz.Trim();

        return await SchreibenAsync(HttpMethod.Post, "api/kosten/nachfuellungen", rumpf, "Nachfüllung eintragen",
            json =>
            {
                var satz = $"Neue Füllung „{name}\" {menge.ToString("0.##", De)} {einheit}"
                           + (kostenEur is { } b ? $" für {b.ToString("0.00", De)} €" : " ohne Preis")
                           + $" am {Datum(json, "zeitpunktUtc") ?? "heute"} eingetragen"
                           + (json.TryGetProperty("growId", out var g) && g.ValueKind == JsonValueKind.Number ? $" (Grow {g.GetInt32()})." : " (keinem Grow zugerechnet).");
                satz += offenSeit is null
                    ? " Eine offene vorige Füllung gab es nicht."
                    : vorherigeLeer
                        ? $" Die vorige Füllung vom {offenSeit} ist als leer geschlossen."
                        : $" Die vorige Füllung vom {offenSeit} bleibt offen (vorherigeLeer=false).";
                if (kostenEur > HoherBetragEur) satz += $" ACHTUNG: ungewöhnlich hoher Betrag — {kostenEur.Value.ToString("0.00", De)} €. Stimmt das?";
                return satz;
            }, cancellationToken, mitRumpf: false);
    }

    // ------------------------------------------------------------------ Anschaffung

    [McpServerTool(Name = "anschaffung_eintragen")]
    [Description($"Verbucht einen Kauf (Name, Stück, Einzelpreis) auf einen Grow — etwa „neuer Luftbefeuchter, 49 €\". Die Antwort nennt den Gesamtbetrag. {StufeDokumentieren}")]
    [BrauchtForkSchluessel(Stufen.Dokumentieren)]
    public async Task<string> AnschaffungEintragenAsync(
        [Description("Was gekauft wurde")][Beispiel("Luftbefeuchter")] string name,
        [Description("Preis je Stück in Euro")][Beispiel("49")] double einzelpreisEur,
        [Description("Stückzahl, mindestens 1")] int stueck = 1,
        [Description("Die Id des Grows; weglassen = der laufende Grow")] int? growId = null,
        [Description("true: keinem Grow zurechnen (Lager)")] bool ohneGrow = false,
        [Description($"Kaufdatum. {ZeitpunktText}")] string? datum = null,
        [Description("Hersteller")] string? hersteller = null,
        [Description("Produkt/Modell")] string? produkt = null,
        [Description("Freitext")] string? notiz = null,
        CancellationToken cancellationToken = default)
    {
        if (!reader.HatForkSchluessel) return ForkFehler.SchluesselNoetig;
        if (string.IsNullOrWhiteSpace(name)) return "Nichts eingetragen: es fehlt, was gekauft wurde.";
        if (!double.IsFinite(einzelpreisEur) || einzelpreisEur < 0) return "Nichts eingetragen: der Preis darf nicht negativ sein.";
        if (stueck < 1) return "Nichts eingetragen: die Stückzahl muss mindestens 1 sein.";
        if (ZeitpunktPruefen(datum, out var utc) is { } zeitFehler) return zeitFehler;

        var rumpf = new JsonObject { ["name"] = name.Trim(), ["stueck"] = stueck, ["einzelpreisEur"] = einzelpreisEur, ["ohneGrow"] = ohneGrow };
        if (growId is not null && !ohneGrow) rumpf["growId"] = growId;
        if (utc is not null) rumpf["datum"] = utc;
        if (!string.IsNullOrWhiteSpace(hersteller)) rumpf["hersteller"] = hersteller.Trim();
        if (!string.IsNullOrWhiteSpace(produkt)) rumpf["produkt"] = produkt.Trim();
        if (!string.IsNullOrWhiteSpace(notiz)) rumpf["notiz"] = notiz.Trim();

        var gesamt = einzelpreisEur * stueck;
        return await SchreibenAsync(HttpMethod.Post, "api/kosten/anschaffungen", rumpf, "Anschaffung eintragen",
            json =>
            {
                var summe = json.TryGetProperty("gesamtEur", out var g) && g.ValueKind == JsonValueKind.Number ? g.GetDouble() : gesamt;
                return $"Anschaffung „{name.Trim()}\" eingetragen: {stueck} × {einzelpreisEur.ToString("0.00", De)} € = {summe.ToString("0.00", De)} €"
                       + (json.TryGetProperty("growId", out var gid) && gid.ValueKind == JsonValueKind.Number ? $" (Grow {gid.GetInt32()})." : " (keinem Grow zugerechnet).")
                       + (summe > HoherBetragEur ? $" ACHTUNG: ungewöhnlich hoher Betrag — {summe.ToString("0.00", De)} €. Stimmt das?" : string.Empty);
            }, cancellationToken, mitRumpf: false);
    }

    // ------------------------------------------------------------------ Hilfe

    /// <summary>Den Zeitpunkt lesen; null heißt „jetzt". Zukunft (über 5 min) und Unlesbares werden abgelehnt.</summary>
    private static string? ZeitpunktPruefen(string? gesagt, out string? utc)
    {
        utc = null;
        if (string.IsNullOrWhiteSpace(gesagt)) return null;
        if (Messfelder.Utc(gesagt) is not { } wert) return ZeitpunktUnlesbar(gesagt);
        if (DateTime.Parse(wert, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal) > DateTime.UtcNow.AddMinutes(5))
        {
            return $"Nichts eingetragen: „{gesagt}\" liegt in der Zukunft.";
        }
        utc = wert;
        return null;
    }

    private async Task<(JsonElement? Artikel, string? Fehler)> KostenSeiteAsync(int? growId, CancellationToken cancellationToken)
    {
        var antwort = await SendenAsync(HttpMethod.Get, growId is { } g ? $"api/kosten?growId={g}" : "api/kosten", null, cancellationToken);
        if (antwort.Fehler is { } f) return (null, f);
        if (!antwort.Antwort!.Erfolg) return (null, ForkFehler.Text(antwort.Antwort.Status, antwort.Antwort.Text, "Kosten lesen"));
        try
        {
            using var doc = JsonDocument.Parse(antwort.Antwort.Text);
            return doc.RootElement.TryGetProperty("artikel", out var artikel) && artikel.ValueKind == JsonValueKind.Array
                ? (artikel.Clone(), null)
                : (null, AndersAlsErwartet);
        }
        catch (JsonException)
        {
            return (null, AndersAlsErwartet);
        }
    }

    /// <summary>
    /// Einen Artikel per Id oder Name finden: erst genau, dann alle Wörter im Namen. Mehrdeutig heißt: Liste, nicht raten.
    /// </summary>
    public static (JsonElement? Artikel, string? Fehler) ArtikelFinden(JsonElement liste, string gesagt)
    {
        var alle = liste.EnumerateArray().Where(a => a.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number).ToList();
        string Aufzaehlung(IEnumerable<JsonElement> artikel)
            => string.Join("; ", artikel.Select(a => $"{a.GetProperty("id").GetInt32()} {Text(a, "name")} ({Text(a, "einheit")})"));

        var wort = (gesagt ?? string.Empty).Trim();
        if (wort.Length == 0) return (null, "Nichts gebucht: es fehlt der Artikel. Vorhanden: " + Aufzaehlung(alle) + ".");

        if (int.TryParse(wort, NumberStyles.None, CultureInfo.InvariantCulture, out var nummer))
        {
            var perId = alle.Where(a => a.GetProperty("id").GetInt32() == nummer).ToList();
            return perId.Count == 1 ? (perId[0], null) : (null, $"Nichts gebucht: einen Artikel mit der Id {nummer} gibt es nicht. Vorhanden: {Aufzaehlung(alle)}.");
        }

        var gesucht = GrowTools.Vereinfachen(wort);
        var genau = alle.Where(a => GrowTools.Vereinfachen(Text(a, "name") ?? string.Empty) == gesucht).ToList();
        if (genau.Count == 1) return (genau[0], null);

        var woerter = gesucht.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var aehnlich = (genau.Count > 1 ? genau : alle)
            .Where(a => woerter.All(w => GrowTools.Vereinfachen($"{Text(a, "name")} {Text(a, "hersteller")} {Text(a, "produkt")}").Contains(w, StringComparison.Ordinal)))
            .ToList();

        // Inaktive Artikel nur, wenn kein aktiver passt.
        var aktiv = aehnlich.Where(a => !(a.TryGetProperty("aktiv", out var x) && x.ValueKind == JsonValueKind.False)).ToList();
        if (aktiv.Count > 0) aehnlich = aktiv;

        return aehnlich.Count switch
        {
            1 => (aehnlich[0], null),
            0 => (null, $"Nichts gebucht: kein Artikel passt zu „{wort}\". Vorhanden: {Aufzaehlung(alle)}."),
            _ => (null, $"Nichts gebucht: „{wort}\" passt auf mehrere Artikel — bitte genauer oder mit Id: {Aufzaehlung(aehnlich)}."),
        };
    }

    /// <summary>Die Id des Artikels, den die CO₂-Steuerung selbst bucht — aus <c>GET /api/steuerung/co2</c>.</summary>
    public static int? Co2ArtikelId(string co2Seite)
    {
        try
        {
            using var doc = JsonDocument.Parse(co2Seite);
            return doc.RootElement.TryGetProperty("einstellungen", out var e) && e.ValueKind == JsonValueKind.Object
                   && e.TryGetProperty("kostenArtikelId", out var id) && id.ValueKind == JsonValueKind.Number
                ? id.GetInt32()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Datum(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var w) && w.ValueKind == JsonValueKind.String
           && DateTime.TryParse(w.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d)
            ? d.ToLocalTime().ToString("dd.MM.yyyy", De)
            : null;
}
