using System.ComponentModel;
using System.Text;
using GrowOsAccess;
using ModelContextProtocol.Server;

namespace GrowMcp.Tools;

/// <summary>
/// Fork AI (Paket D, 18.10.2026): <c>fork_funktionen</c> — das Verzeichnis aller Funktionen von Cultivate Control.
/// </summary>
public sealed partial class GrowTools
{
    /// <summary>Bis zu so vielen Treffern steht jede Funktion mit allen Angaben da.</summary>
    public const int AusfuehrlichBis = 12;

    /// <summary>Bis zu so vielen Treffern eine Zeile je Funktion; darüber nur die Bereiche.</summary>
    public const int ZeilenBis = 80;

    private const int AntwortHoechstens = 24_000;

    [McpServerTool(Name = "fork_funktionen")]
    [Description("Das Verzeichnis ALLER Funktionen von Cultivate Control: Weg, Art (lesen/anlegen/ändern/löschen), was ein Schlüssel dafür braucht (frei, Stufen oder nie), Sicherung vorher, Schaltbefehl, Beschreibung und erwartete Angaben (Weg-, Abfrage- und Rumpffelder). Mit suche (Wörter, etwa „entfeuchter hysterese\"), bereich (etwa kosten, steuerung, plants), art oder zugang eingrenzen. Cultivate Control erzeugt das Verzeichnis selbst aus seinem Code. Vor fork_ausfuehren aufrufen, um Weg und Felder zu finden. Für Alltägliches (Messung, Journal, Licht, Dosieren …) gibt es feste Werkzeuge — die zuerst nehmen.")]
    public Task<string> ForkFunktionenAsync(
        [Description("Suchwörter, alle müssen passen (Weg, Beschreibung, Bereich, Rumpffelder), etwa „kosten nachfuellung\"")] string? suche = null,
        [Description("Nur dieser Bereich (der Teil nach /api/), etwa kosten, steuerung, grows, plants, settings")] string? bereich = null,
        [Description("Nur diese Art: lesen, anlegen, ändern oder löschen")] string? art = null,
        [Description("Nur dieser Zugang: frei, stufe, nie")] string? zugang = null,
        CancellationToken cancellationToken = default)
        => SicherAsync(async () =>
        {
            var antwort = await reader.SendenAsync(HttpMethod.Get, Funktionsverzeichnis.Weg, null, cancellationToken);
            if (!antwort.Erfolg) return VerzeichnisFehler(antwort);
            var verzeichnis = Funktionsverzeichnis.Lesen(antwort.Text);
            if (verzeichnis is null) return SchreibWerkzeuge.AndersAlsErwartet;
            return FunktionenBeschreiben(verzeichnis, suche, bereich, art, zugang);
        });

    /// <summary>Warum das Verzeichnis nicht kam — „KI aus" und „Cultivate Control zu alt" sind die häufigen Gründe.</summary>
    internal static string VerzeichnisFehler(ForkAntwort antwort)
    {
        if (antwort.Status == 404 && antwort.Text.Contains("\"ki_aus\"", StringComparison.Ordinal))
        {
            return "Das Funktionsverzeichnis gibt es nur, wenn in Cultivate Control die KI-Funktionen eingeschaltet sind (Einrichtung → KI-Assistent).";
        }
        if (antwort.Status == 404)
        {
            return "Cultivate Control kennt das Funktionsverzeichnis noch nicht — bitte Cultivate Control aktualisieren.";
        }
        return ForkFehler.Text(antwort.Status, antwort.Text, "Funktionsverzeichnis lesen");
    }

    /// <summary>Gefiltert und so knapp, dass die Antwort lesbar bleibt — öffentlich für die Prüfung.</summary>
    public static string FunktionenBeschreiben(IReadOnlyList<Funktionsverzeichnis.Funktion> verzeichnis, string? suche, string? bereich, string? art, string? zugang)
    {
        var woerter = (suche ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Vereinfachen).ToList();
        var artWort = ArtWort(art);

        var treffer = verzeichnis.Where(f =>
                (string.IsNullOrWhiteSpace(bereich) || string.Equals(f.Bereich, bereich.Trim(), StringComparison.OrdinalIgnoreCase))
                && (artWort is null || string.Equals(Vereinfachen(f.Art), artWort, StringComparison.Ordinal))
                && (string.IsNullOrWhiteSpace(zugang) || string.Equals(f.Zugang, zugang.Trim(), StringComparison.OrdinalIgnoreCase))
                && woerter.All(w => Vereinfachen(Suchtext(f)).Contains(w, StringComparison.Ordinal)))
            .ToList();

        var text = new StringBuilder();
        text.AppendLine($"{treffer.Count} von {verzeichnis.Count} Funktionen passen"
                        + (woerter.Count > 0 ? $" zu „{suche!.Trim()}\"" : string.Empty)
                        + (string.IsNullOrWhiteSpace(bereich) ? string.Empty : $" im Bereich {bereich.Trim()}")
                        + (artWort is null ? string.Empty : $" (Art {art!.Trim()})")
                        + (string.IsNullOrWhiteSpace(zugang) ? string.Empty : $" (Zugang {zugang.Trim()})") + ".");

        if (treffer.Count == 0)
        {
            text.AppendLine("Bereiche: " + BereichListe(verzeichnis) + ".");
            text.Append("Mit weniger oder anderen Suchwörtern oder einem Bereich noch einmal versuchen.");
            return text.ToString();
        }

        if (treffer.Count > ZeilenBis)
        {
            text.AppendLine("Zu viele für eine Liste. Bereiche der Treffer: " + BereichListe(treffer) + ".");
            text.Append("Bitte mit suche, bereich, art oder zugang eingrenzen.");
            return text.ToString();
        }

        foreach (var f in treffer)
        {
            text.AppendLine($"{f.Methode} {f.Pfad} — {f.Art} · {Funktionsverzeichnis.ZugangText(f)}{Funktionsverzeichnis.Zusaetze(f)}");
            if (treffer.Count <= AusfuehrlichBis)
            {
                text.AppendLine($"  {f.Beschreibung}");
                if (f.Routenparameter.Count > 0)
                    text.AppendLine("  Weg: " + string.Join(", ", f.Routenparameter.Select(p => $"{p.Name} ({p.Typ})")));
                if (f.Abfrage.Count > 0)
                    text.AppendLine("  Abfrage (query): " + string.Join(", ", f.Abfrage.Select(p => $"{p.Name} ({p.Typ}{(p.Pflicht ? ", Pflicht" : string.Empty)})")));
                if (f.RumpfTyp is not null)
                {
                    var felder = string.Join(", ", f.RumpfFelder.Select(x => $"{x.Name} ({x.Typ})"));
                    text.AppendLine($"  Rumpf (body) {f.RumpfTyp}: " + (felder.Length > 1500 ? felder[..1499] + "…" : felder));
                }
            }
            else
            {
                var kurz = f.Beschreibung.Length > 100 ? f.Beschreibung[..99] + "…" : f.Beschreibung;
                text.AppendLine($"  {kurz}");
            }
        }

        if (treffer.Count > AusfuehrlichBis)
        {
            text.AppendLine($"Mehr als {AusfuehrlichBis} Treffer: Felder und Angaben stehen da, sobald die Suche auf höchstens {AusfuehrlichBis} eingegrenzt ist.");
        }
        text.Append("Ausführen mit fork_ausfuehren (methode, pfad mit eingesetzten Werten, query, body).");

        return Funktionsverzeichnis.Kuerzen(text.ToString(), AntwortHoechstens, "Bitte enger suchen.");
    }

    private static string Suchtext(Funktionsverzeichnis.Funktion f)
        => $"{f.Methode} {f.Pfad} {f.Bereich} {f.Art} {f.Beschreibung} {f.RumpfTyp} {string.Join(' ', f.RumpfFelder.Select(x => x.Name))}";

    private static string BereichListe(IEnumerable<Funktionsverzeichnis.Funktion> funktionen)
        => string.Join(", ", funktionen.GroupBy(f => f.Bereich).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} ({g.Count()})"));

    /// <summary>Kleinbuchstaben, Umlaute ausgeschrieben, ₂ als 2 — „Nachfüllung" findet „nachfuellungen", „CO2" findet „CO₂".</summary>
    internal static string Vereinfachen(string text)
        => text.ToLowerInvariant().Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss").Replace("₂", "2");

    private static string? ArtWort(string? art)
        => string.IsNullOrWhiteSpace(art) ? null : Vereinfachen(art.Trim()) switch
        {
            "lesen" or "get" => "lesen",
            "anlegen" or "post" or "ausfuehren" => "anlegen",
            "aendern" or "put" or "patch" => "aendern",
            "loeschen" or "delete" => "loeschen",
            var anders => anders,
        };
}
