using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using GrowOsAccess;
using ModelContextProtocol.Server;

namespace GrowMcp.Tools;

/// <summary>
/// Einen falsch diktierten Journal-Eintrag oder eine Messung löschen (forkai.247).
/// </summary>
/// <remarks>
/// <para><b>Erst lesen, dann löschen, dann alles nennen.</b> Gelöscht wird nur, was Cultivate Control vorher unter dieser Id geliefert hat.
/// Die Antwort enthält den vollen Inhalt — Datum, Text bzw. jeden Wert — und die Angaben, mit denen er sich wieder eintragen lässt.
/// Ein Löschen über einen Schlüssel legt Cultivate Control vorher eine Sicherung an (<c>[KiSicherungVorher]</c>); scheitert die, wird nicht
/// gelöscht.</para>
/// </remarks>
public sealed partial class SchreibWerkzeuge
{
    private static readonly CultureInfo Deutsch = CultureInfo.GetCultureInfo("de-DE");

    [McpServerTool(Name = "journal_loeschen")]
    [Description($"Löscht EINEN Journal-Eintrag. Liest ihn vorher und löscht nur, wenn es ihn gibt; die Antwort nennt Datum, Art, Titel und den vollen Text, damit er sich mit journal_eintragen wieder eintragen lässt. Die Eintrags-Id liefert journal. Cultivate Control legt vorher eine Sicherung an. {StufeDokumentieren}")]
    [BrauchtForkSchluessel(Stufen.Dokumentieren)]
    public async Task<string> JournalLoeschenAsync(
        [Description("Die Id des Journal-Eintrags (aus journal)")] int eintragId,
        CancellationToken cancellationToken = default)
    {
        if (!reader.HatForkSchluessel) return ForkFehler.SchluesselNoetig;

        var vorher = await SendenAsync(HttpMethod.Get, $"api/journal/{eintragId}", null, cancellationToken);
        if (vorher.Fehler is { } fehler) return fehler;
        if (vorher.Antwort!.Status == 404) return $"Nichts gelöscht: Einen Journal-Eintrag mit der Id {eintragId} gibt es nicht. Mit journal bekommst du die Einträge eines Grows.";
        if (!vorher.Antwort.Erfolg) return ForkFehler.Text(vorher.Antwort.Status, vorher.Antwort.Text, "Journal-Eintrag lesen");

        string growId, wann, art;
        string? titel, text;
        bool meilenstein;
        try
        {
            using var doc = JsonDocument.Parse(vorher.Antwort.Text);
            var e = doc.RootElement;
            if (e.ValueKind != JsonValueKind.Object || Zahl(e, "id") != eintragId.ToString(CultureInfo.InvariantCulture)) return AndersAlsErwartet;
            growId = Zahl(e, "growId");
            wann = Ortszeit(Text(e, "occurredAtUtc"), utc: true);
            var roh = Text(e, "entryType");
            // Meilensteine (Keimung bestätigt, Flip …) schreibt Cultivate Control beim Bestätigen einer Phase selbst; journal_eintragen kennt sie nicht.
            meilenstein = roh is not null && !JournalArten.Any(a => string.Equals(a.Wert, roh, StringComparison.OrdinalIgnoreCase));
            art = JournalArtText(roh);
            titel = Text(e, "title");
            text = Text(e, "body");
        }
        catch (JsonException)
        {
            return AndersAlsErwartet;
        }

        var geloescht = await SendenAsync(HttpMethod.Delete, $"api/journal/{eintragId}", null, cancellationToken);
        if (geloescht.Fehler is { } loeschFehler) return loeschFehler;
        if (!geloescht.Antwort!.Erfolg) return ForkFehler.Text(geloescht.Antwort.Status, geloescht.Antwort.Text, "Journal-Eintrag löschen");

        var satz = new StringBuilder($"Journal-Eintrag {eintragId} von Grow {growId} vom {wann} gelöscht (Art: {art}).");
        satz.Append(string.IsNullOrWhiteSpace(titel) ? " Ohne Titel." : $" Titel: „{titel}\".");
        satz.Append(string.IsNullOrWhiteSpace(text) ? " Ohne Text." : $" Text war: „{text}\"");
        if (meilenstein)
        {
            satz.Append(Environment.NewLine)
                .Append($"Das war ein Meilenstein ({art}), den Cultivate Control beim Bestätigen einer Phase selbst schreibt. Als Meilenstein lässt er sich nicht wieder eintragen, ")
                .Append("die Phase selbst bleibt davon unberührt. Als Notiz: journal_eintragen mit growId ").Append(growId).Append($", zeitpunkt {wann}, art Notiz")
                .Append(string.IsNullOrWhiteSpace(titel) ? "" : $", titel „{titel}\"")
                .Append(string.IsNullOrWhiteSpace(text) ? "." : ", text wie oben.");
            return satz.ToString();
        }

        satz.Append(Environment.NewLine)
            .Append($"Wieder eintragen: journal_eintragen mit growId {growId}, zeitpunkt {wann}, art {art}")
            .Append(string.IsNullOrWhiteSpace(titel) ? "" : $", titel „{titel}\"")
            .Append(string.IsNullOrWhiteSpace(text) ? "." : ", text wie oben.");
        return satz.ToString();
    }

    [McpServerTool(Name = "messung_loeschen")]
    [Description($"Löscht EINE Messung. Liest sie vorher und löscht nur, wenn es sie gibt; die Antwort nennt Zeitpunkt, Phase und jeden Wert, damit sie sich mit messung_eintragen wieder eintragen lässt. Die Messungs-Id steht in der Antwort von messung_eintragen oder im lagebericht. Zum Korrigieren einzelner Werte messung_aendern nehmen. Cultivate Control legt vorher eine Sicherung an. {StufeDokumentieren}")]
    [BrauchtForkSchluessel(Stufen.Dokumentieren)]
    public async Task<string> MessungLoeschenAsync(
        [Description("Die Id der Messung")] int messungId,
        CancellationToken cancellationToken = default)
    {
        if (!reader.HatForkSchluessel) return ForkFehler.SchluesselNoetig;

        var vorher = await SendenAsync(HttpMethod.Get, $"api/measurements/{messungId}", null, cancellationToken);
        if (vorher.Fehler is { } fehler) return fehler;
        if (vorher.Antwort!.Status == 404) return $"Nichts gelöscht: Eine Messung mit der Id {messungId} gibt es nicht.";
        if (!vorher.Antwort.Erfolg) return ForkFehler.Text(vorher.Antwort.Status, vorher.Antwort.Text, "Messung lesen");

        string growId, wann;
        string? phase;
        List<(string Anzeige, string Aufruf)> werte;
        try
        {
            using var doc = JsonDocument.Parse(vorher.Antwort.Text);
            var m = doc.RootElement;
            if (m.ValueKind != JsonValueKind.Object || Zahl(m, "id") != messungId.ToString(CultureInfo.InvariantCulture)) return AndersAlsErwartet;
            growId = Zahl(m, "growId");
            // takenAt führt Cultivate Control in Ortszeit (Measurement.TakenAt = DateTime.Now), nicht in UTC.
            wann = Ortszeit(Text(m, "takenAt"), utc: false);
            phase = Text(m, "stage");
            werte = MesswerteLesen(m);
        }
        catch (JsonException)
        {
            return AndersAlsErwartet;
        }

        var geloescht = await SendenAsync(HttpMethod.Delete, $"api/measurements/{messungId}", null, cancellationToken);
        if (geloescht.Fehler is { } loeschFehler) return loeschFehler;
        if (!geloescht.Antwort!.Erfolg) return ForkFehler.Text(geloescht.Antwort.Status, geloescht.Antwort.Text, "Messung löschen");

        var satz = new StringBuilder($"Messung {messungId} von Grow {growId} vom {wann}{(phase is null ? "" : $" (Phase {phase})")} gelöscht.");
        satz.Append(werte.Count == 0 ? " Sie hatte keine Werte." : " Werte waren: " + string.Join("; ", werte.Select(w => w.Anzeige)) + ".");
        satz.Append(Environment.NewLine)
            .Append($"Wieder eintragen: messung_eintragen mit growId {growId}, zeitpunkt {wann}")
            .Append(phase is null ? "" : $", phase {phase}")
            .Append(werte.Count == 0 ? "." : ", " + string.Join(", ", werte.Select(w => w.Aufruf)) + ".");
        return satz.ToString();
    }

    /// <summary>
    /// Feld der Messung in Cultivate Control → Parameter von <c>messung_eintragen</c>, Anzeige und Einheit.
    /// </summary>
    /// <remarks>
    /// Die Feldnamen sind die aus <see cref="Messfelder.Tabelle"/> (Anfrage und Antwort heißen in Cultivate Control gleich).
    /// <c>MessfelderTests</c> verlangt, dass jedes Feld der Tabelle hier genau einmal steht und dass der Parameter bei
    /// <c>messung_eintragen</c> wirklich in dieses Feld schreibt.
    /// </remarks>
    public static IReadOnlyList<(string Feld, string Parameter, string Anzeige, string? Einheit)> MessfelderAnzeige { get; } =
    [
        ("reservoirPh", "ph", "pH", null),
        ("reservoirEc", "ec", "EC", "mS/cm"),
        ("reservoirWaterTempC", "wassertemperaturC", "Wassertemp.", "°C"),
        ("orpMv", "orpMv", "ORP", "mV"),
        ("dissolvedOxygenMgL", "sauerstoffMgL", "Sauerstoff", "mg/L"),
        ("reservoirLevelLiters", "fuellstandLiter", "Füllstand", "L"),
        ("reservoirLevelCm", "fuellstandCm", "Füllstand", "cm"),
        ("topOffLiters", "nachfuellLiter", "Nachgefüllt", "L"),
        ("addbackEc", "nachfuellEc", "EC der Nachfülllösung", "mS/cm"),
        ("airTemperatureC", "lufttemperaturC", "Lufttemp.", "°C"),
        ("humidityPercent", "luftfeuchteProzent", "Luftfeuchte", "%"),
        ("co2Ppm", "co2Ppm", "CO₂", "ppm"),
        ("ppfdMol", "ppfd", "PPFD", "µmol/m²/s"),
        ("airflowAtLeafMPerMin", "luftstromAmBlattMProMin", "Luftstrom am Blatt", "m/min"),
        ("heightCm", "hoeheCm", "Höhe", "cm"),
        ("waterAmountMl", "giessmengeMl", "Gießmenge", "ml"),
        ("runoffAmountMl", "ablaufMl", "Ablauf", "ml"),
        ("irrigationPh", "giesswasserPh", "pH Gießwasser", null),
        ("irrigationEc", "giesswasserEc", "EC Gießwasser", "mS/cm"),
        ("drainPh", "drainPh", "pH Drain", null),
        ("drainEc", "drainEc", "EC Drain", "mS/cm"),
        ("waterFlow", "stroemung", "Strömung", null),
        ("solutionChange", "wasserwechsel", "Wasserwechsel", null),
        ("notes", "notiz", "Notiz", null),
    ];

    /// <summary>Die belegten Werte einer Messung: Anzeige („EC 1,2 mS/cm") und Aufruf („ec 1.2").</summary>
    private static List<(string Anzeige, string Aufruf)> MesswerteLesen(JsonElement messung)
    {
        var werte = new List<(string, string)>();
        foreach (var (feld, parameter, anzeige, einheit) in MessfelderAnzeige)
        {
            if (!messung.TryGetProperty(feld, out var wert)) continue;
            switch (wert.ValueKind)
            {
                case JsonValueKind.Number:
                    var zahl = wert.GetDouble();
                    werte.Add(($"{anzeige} {zahl.ToString("0.###", Deutsch)}{(einheit is null ? "" : " " + einheit)}",
                        $"{parameter} {zahl.ToString("0.###", CultureInfo.InvariantCulture)}"));
                    break;
                case JsonValueKind.True:
                    werte.Add(($"{anzeige}: ja", $"{parameter} true"));
                    break;
                case JsonValueKind.String when !string.IsNullOrWhiteSpace(wert.GetString()):
                    werte.Add(($"{anzeige} „{wert.GetString()}\"", $"{parameter} „{wert.GetString()}\""));
                    break;
            }
        }
        return werte;
    }

    /// <summary>Ein Zeitpunkt aus Cultivate Control als „18.10.2026 14:30" in Ortszeit — der Form, die die Eintrags-Werkzeuge lesen.</summary>
    private static string Ortszeit(string? zeitpunkt, bool utc)
    {
        if (zeitpunkt is null) return "?";
        var stil = utc ? DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal : DateTimeStyles.AssumeLocal;
        if (!DateTime.TryParse(zeitpunkt, CultureInfo.InvariantCulture, stil, out var zeit)) return zeitpunkt;
        return (utc ? zeit.ToLocalTime() : zeit).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>Die Art eines Journal-Eintrags auf Deutsch (erstes Wort aus <see cref="JournalArten"/>); Meilensteine bleiben roh.</summary>
    private static string JournalArtText(string? art)
    {
        var treffer = JournalArten.FirstOrDefault(a => string.Equals(a.Wert, art, StringComparison.OrdinalIgnoreCase));
        if (treffer.Woerter is null) return art ?? "Notiz";
        var wort = treffer.Woerter[0];
        return char.ToUpper(wort[0], Deutsch) + wort[1..];
    }
}
