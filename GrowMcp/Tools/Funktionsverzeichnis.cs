using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GrowMcp.Tools;

/// <summary>
/// Fork AI (Paket D, 18.10.2026): Das Funktionsverzeichnis von Cultivate Control (<c>GET /api/ki-zugriff/funktionen</c>) auf
/// der Seite des Cultivate Control MCP — lesen, filtern, einen Pfad einer Funktion zuordnen, Antworten vergleichen.
/// </summary>
/// <remarks>
/// <para>Die Einstufung (frei, Stufen, nie) steht im Verzeichnis und kommt dort aus derselben Funktion wie die Sperre
/// von Cultivate Control. Hier wird sie nur gelesen und weitergesagt — entschieden wird bei jeder Anfrage in Cultivate Control.</para>
/// <para>Bewusst <b>ohne</b> Abhängigkeit zu den Verträgen von Cultivate Control: das Verzeichnis kommt als JSON, und ein Feld, das
/// fehlt, bleibt leer statt den Cultivate Control MCP zu brechen.</para>
/// </remarks>
public static class Funktionsverzeichnis
{
    public const string Weg = "api/ki-zugriff/funktionen";

    public static readonly string[] Methoden = ["GET", "POST", "PUT", "PATCH", "DELETE"];

    public sealed record Feld(string Name, string Typ);

    public sealed record Parameter(string Name, string Typ, bool Pflicht);

    public sealed record Funktion(
        string Methode,
        string Pfad,
        string Bereich,
        string Art,
        string Zugang,
        IReadOnlyList<string> Stufen,
        bool Verwaltungsweg,
        string? NieGrund,
        bool Schaltbefehl,
        bool SicherungVorher,
        string Beschreibung,
        IReadOnlyList<Parameter> Routenparameter,
        IReadOnlyList<Parameter> Abfrage,
        string? RumpfTyp,
        IReadOnlyList<Feld> RumpfFelder,
        bool DateiHochladen,
        bool LiefertDatei);

    // ------------------------------------------------------------------ Lesen

    /// <summary>Das Verzeichnis aus der Antwort von Cultivate Control — oder null, wenn es nicht so aussieht.</summary>
    public static IReadOnlyList<Funktion>? Lesen(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("funktionen", out var liste)
                || liste.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var ergebnis = new List<Funktion>();
            foreach (var f in liste.EnumerateArray())
            {
                if (f.ValueKind != JsonValueKind.Object) continue;
                var methode = Text(f, "methode");
                var pfad = Text(f, "pfad");
                if (methode is null || pfad is null) continue;

                f.TryGetProperty("rumpf", out var rumpf);
                ergebnis.Add(new Funktion(
                    methode.ToUpperInvariant(),
                    pfad,
                    Text(f, "bereich") ?? string.Empty,
                    Text(f, "art") ?? string.Empty,
                    Text(f, "zugang") ?? "nicht-eingestuft",
                    Texte(f, "stufen"),
                    Wahr(f, "verwaltungsweg"),
                    Text(f, "nieGrund"),
                    Wahr(f, "schaltbefehl"),
                    Wahr(f, "sicherungVorher"),
                    Text(f, "beschreibung") ?? string.Empty,
                    ParameterListe(f, "routenparameter"),
                    ParameterListe(f, "abfrage"),
                    rumpf.ValueKind == JsonValueKind.Object ? Text(rumpf, "typ") : null,
                    rumpf.ValueKind == JsonValueKind.Object && rumpf.TryGetProperty("felder", out var felder) && felder.ValueKind == JsonValueKind.Array
                        ? felder.EnumerateArray().Where(x => Text(x, "name") is not null).Select(x => new Feld(Text(x, "name")!, Text(x, "typ") ?? "?")).ToList()
                        : [],
                    Wahr(f, "dateiHochladen"),
                    Wahr(f, "liefertDatei")));
            }
            return ergebnis;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var w) && w.ValueKind == JsonValueKind.String ? w.GetString() : null;

    private static bool Wahr(JsonElement e, string name)
        => e.TryGetProperty(name, out var w) && w.ValueKind == JsonValueKind.True;

    private static List<string> Texte(JsonElement e, string name)
        => e.TryGetProperty(name, out var w) && w.ValueKind == JsonValueKind.Array
            ? w.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
            : [];

    private static List<Parameter> ParameterListe(JsonElement e, string name)
        => e.TryGetProperty(name, out var w) && w.ValueKind == JsonValueKind.Array
            ? w.EnumerateArray().Where(x => Text(x, "name") is not null)
                .Select(x => new Parameter(Text(x, "name")!, Text(x, "typ") ?? "?", Wahr(x, "pflicht"))).ToList()
            : [];

    // ------------------------------------------------------------------ Zugang in Worten

    /// <summary>Was ein Schlüssel für die Funktion braucht, in Worten.</summary>
    public static string ZugangText(Funktion f) => f.Zugang switch
    {
        "frei" => "frei (jeder gültige Schlüssel)",
        "nie" => "NIE über einen Schlüssel" + (f.NieGrund is { } g ? $" — {g}" : string.Empty),
        "stufe" => "braucht " + string.Join(" + ", f.Stufen.Select(Stufen.Text)),
        _ => "gesperrt (keiner Stufe zugeordnet)",
    };

    /// <summary>Die Zusätze: Schaltbefehl, Sicherung, Datei.</summary>
    public static string Zusaetze(Funktion f)
    {
        var teile = new List<string>();
        if (f.Schaltbefehl) teile.Add("zählt als Schaltbefehl");
        if (f.SicherungVorher) teile.Add("Sicherung vorher");
        if (f.DateiHochladen) teile.Add("Datei-Upload, nicht über den Assistenten");
        if (f.LiefertDatei) teile.Add("liefert eine Datei/ein Bild");
        return teile.Count == 0 ? string.Empty : " · " + string.Join(" · ", teile);
    }

    // ------------------------------------------------------------------ Pfad prüfen und zuordnen

    /// <summary>
    /// Ist das ein erlaubter Pfad? Nur relativ unter <c>api/</c>, kein <c>..</c>, kein Schema, kein anderer Host,
    /// keine kodierten Zeichen, keine Abfrage im Pfad.
    /// </summary>
    /// <returns>Der bereinigte Pfad ohne führenden Schrägstrich — oder null, mit Grund in <paramref name="fehler"/>.</returns>
    public static string? PfadPruefen(string? pfad, out string fehler)
    {
        fehler = string.Empty;
        var roh = (pfad ?? string.Empty).Trim();
        if (roh.Length == 0)
        {
            fehler = "Es fehlt der Pfad, etwa api/grows.";
            return null;
        }

        if (roh.Length > 300)
        {
            fehler = "Der Pfad ist zu lang.";
            return null;
        }

        // Ein führender Schrägstrich ist erlaubt (so steht der Pfad im Verzeichnis) — aber nicht zwei: //host/… ist eine Adresse.
        if (roh.StartsWith('/') && !roh.StartsWith("//", StringComparison.Ordinal)) roh = roh[1..];

        var segmente = roh.Split('/');
        if (segmente.Length < 2 || !string.Equals(segmente[0], "api", StringComparison.OrdinalIgnoreCase))
        {
            fehler = "Nur Wege von Cultivate Control unter api/ sind erlaubt, etwa api/grows — keine Adressen, keine anderen Dienste.";
            return null;
        }

        foreach (var segment in segmente)
        {
            if (segment.Length == 0)
            {
                fehler = "Der Pfad hat einen leeren Abschnitt (//) — so nicht erlaubt.";
                return null;
            }
            if (segment is "." or "..")
            {
                fehler = "„..\" und „.\" sind im Pfad nicht erlaubt.";
                return null;
            }
            foreach (var zeichen in segment)
            {
                if (!(char.IsLetterOrDigit(zeichen) || zeichen is '_' or '-' or '.' or '~'))
                {
                    fehler = $"Das Zeichen „{zeichen}\" ist im Pfad nicht erlaubt (nur Buchstaben, Ziffern, _ - . ~). Abfragewerte gehören in query.";
                    return null;
                }
            }
        }

        // Gegenprobe wie beim echten Aufruf: der Pfad hängt an der Adresse von Cultivate Control und darf sie nicht verlassen.
        var basis = new Uri("http://grow-os.invalid/");
        if (!Uri.TryCreate(basis, roh, out var ziel)
            || ziel.Host != basis.Host || ziel.Port != basis.Port || ziel.Scheme != basis.Scheme
            || !ziel.AbsolutePath.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            fehler = "Der Pfad führt aus Cultivate Control hinaus — nicht erlaubt.";
            return null;
        }

        return roh;
    }

    /// <summary>Eine Abfrage „a=1&amp;b=zwei" geprüft und kodiert — oder null mit Grund.</summary>
    public static string? AbfragePruefen(string? abfrage, out string fehler)
    {
        fehler = string.Empty;
        var roh = (abfrage ?? string.Empty).Trim().TrimStart('?');
        if (roh.Length == 0) return string.Empty;

        var teile = new List<string>();
        foreach (var paar in roh.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var gleich = paar.IndexOf('=');
            var name = (gleich < 0 ? paar : paar[..gleich]).Trim();
            var wert = gleich < 0 ? string.Empty : paar[(gleich + 1)..].Trim();
            if (name.Length == 0 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.'))
            {
                fehler = $"„{name}\" ist kein Abfragename (nur Buchstaben, Ziffern, _ - .).";
                return null;
            }
            teile.Add($"{name}={Uri.EscapeDataString(wert)}");
        }
        return string.Join("&", teile);
    }

    /// <summary>
    /// Die Funktion zu Methode und Pfad: Platzhalter passen auf genau einen Abschnitt (bei „Ganzzahl" nur Ziffern);
    /// bei mehreren Treffern gewinnt die mit den meisten festen Abschnitten.
    /// </summary>
    public static Funktion? Zuordnen(IReadOnlyList<Funktion> verzeichnis, string methode, string pfad)
    {
        var abschnitte = pfad.TrimStart('/').Split('/');
        Funktion? beste = null;
        var besteFeste = -1;
        foreach (var f in verzeichnis.Where(f => string.Equals(f.Methode, methode, StringComparison.OrdinalIgnoreCase)))
        {
            var vorlage = f.Pfad.TrimStart('/').Split('/');
            if (vorlage.Length != abschnitte.Length) continue;

            var feste = 0;
            var passt = true;
            for (var i = 0; i < vorlage.Length && passt; i++)
            {
                if (vorlage[i].StartsWith('{') && vorlage[i].EndsWith('}'))
                {
                    var name = vorlage[i][1..^1];
                    var typ = f.Routenparameter.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))?.Typ;
                    if (typ is not null && typ.StartsWith("Ganzzahl", StringComparison.Ordinal) && !abschnitte[i].All(char.IsAsciiDigit)) passt = false;
                }
                else if (string.Equals(vorlage[i], abschnitte[i], StringComparison.OrdinalIgnoreCase))
                {
                    feste++;
                }
                else
                {
                    passt = false;
                }
            }

            if (passt && feste > besteFeste)
            {
                beste = f;
                besteFeste = feste;
            }
        }
        return beste;
    }

    // ------------------------------------------------------------------ Vergleich vorher/nachher

    /// <summary>Alle Blätter eines JSON-Werts mit ihrem Pfad (<c>a.b[0].c</c>).</summary>
    public static Dictionary<string, string> Blaetter(JsonNode? knoten)
    {
        var ergebnis = new Dictionary<string, string>(StringComparer.Ordinal);
        void Gehen(JsonNode? n, string pfad)
        {
            switch (n)
            {
                case JsonObject o:
                    if (o.Count == 0) ergebnis[pfad] = "{}";
                    foreach (var (name, wert) in o) Gehen(wert, pfad.Length == 0 ? name : $"{pfad}.{name}");
                    break;
                case JsonArray a:
                    if (a.Count == 0) ergebnis[pfad] = "[]";
                    // Einträge mit eindeutiger id nach id, nicht nach Stelle: ein neuer Eintrag vorne verschöbe sonst alle
                    // und sähe aus, als wären bestehende umbenannt (Prüfer, 18.10.2026).
                    var ids = a.Select(e => e is JsonObject o && o["id"] is JsonValue v ? v.ToJsonString().Trim('"') : null).ToList();
                    var nachId = ids.All(i => i is not null) && ids.Distinct().Count() == ids.Count;
                    for (var i = 0; i < a.Count; i++) Gehen(a[i], nachId ? $"{pfad}[id={ids[i]}]" : $"{pfad}[{i}]");
                    break;
                case null:
                    ergebnis[pfad] = "null";
                    break;
                default:
                    ergebnis[pfad] = n.ToJsonString();
                    break;
            }
        }
        Gehen(knoten, string.Empty);
        return ergebnis;
    }

    /// <summary>Der tiefste Eintrag einer nach id abgeglichenen Liste, zu dem ein Blatt gehört (<c>journal[id=35]</c>) — oder null.</summary>
    public static string? Eintrag(string pfad)
    {
        // Der tiefste Eintrag: sections[id=a].tiles[id=b].wert gehört zu …tiles[id=b], nicht zum Abschnitt.
        var start = pfad.LastIndexOf("[id=", StringComparison.Ordinal);
        if (start < 0) return null;
        var ende = pfad.IndexOf(']', start);
        return ende < 0 ? null : pfad[..(ende + 1)];
    }

    /// <summary>Ein Unterschied zwischen zwei Ständen: Pfad, alter und neuer Wert (null = fehlte).</summary>
    public sealed record Aenderung(string Pfad, string? Alt, string? Neu);

    /// <summary>
    /// Was sich zwischen <paramref name="vorher"/> und <paramref name="nachher"/> verändert hat — ohne die Werte,
    /// die sich schon zwischen zwei Lesungen davor bewegt haben (Live-Werte, Uhrzeiten).
    /// </summary>
    public static (List<Aenderung> Geaendert, int Unveraendert, int Beweglich) Vergleichen(
        Dictionary<string, string> vorher, Dictionary<string, string>? vorherZweitesMal, Dictionary<string, string> nachher)
    {
        var beweglich = vorherZweitesMal is null
            ? new HashSet<string>()
            : vorher.Keys.Union(vorherZweitesMal.Keys)
                .Where(k => !vorher.TryGetValue(k, out var a) || !vorherZweitesMal.TryGetValue(k, out var b) || a != b)
                .ToHashSet();

        var geaendert = new List<Aenderung>();
        var gleich = 0;
        foreach (var schluessel in vorher.Keys.Union(nachher.Keys).Where(k => !beweglich.Contains(k)).OrderBy(k => k, StringComparer.Ordinal))
        {
            var alt = vorher.GetValueOrDefault(schluessel);
            var neu = nachher.GetValueOrDefault(schluessel);
            if (alt == neu) gleich++;
            else geaendert.Add(new Aenderung(schluessel, alt, neu));
        }
        return (geaendert, gleich, beweglich.Count);
    }

    /// <summary>Ein JSON-Blatt für Menschen: Zahlen mit Komma, Texte in Anführung, null als „leer".</summary>
    public static string Wert(string? json)
    {
        if (json is null) return "(fehlte)";
        if (json == "null") return "leer";
        if (json.StartsWith('"'))
        {
            var text = JsonSerializer.Deserialize<string>(json) ?? string.Empty;
            return "„" + (text.Length > 60 ? text[..59] + "…" : text) + "\"";
        }
        if (double.TryParse(json, NumberStyles.Float, CultureInfo.InvariantCulture, out var zahl))
        {
            return zahl.ToString("0.####", CultureInfo.GetCultureInfo("de-DE"));
        }
        return json switch { "true" => "ja", "false" => "nein", _ => json };
    }

    /// <summary>Einen Text auf eine Länge kürzen, mit Hinweis.</summary>
    public static string Kuerzen(string text, int hoechstens, string hinweis)
        => text.Length <= hoechstens ? text : text[..hoechstens] + Environment.NewLine + $"… gekürzt ({text.Length:N0} Zeichen). {hinweis}";

    /// <summary>Ein Objekt in einer Zeile für „Gelöscht: …" — Name/Bezeichnung zuerst, dann der Rest kompakt.</summary>
    public static string Kurzfassung(JsonNode? knoten, int hoechstens = 600)
    {
        if (knoten is not JsonObject o) return knoten?.ToJsonString() ?? "leer";
        var vorn = new[] { "id", "name", "label", "titel", "title", "artikelName", "bezeichnung", "text", "notiz", "notes" }
            .Where(o.ContainsKey).Select(n => $"{n}: {Wert(o[n]?.ToJsonString() ?? "null")}").ToList();
        var rest = new StringBuilder(string.Join(", ", vorn));
        var json = o.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        if (rest.Length > 0) rest.Append(" — ");
        rest.Append(json);
        var text = rest.ToString();
        return text.Length > hoechstens ? text[..hoechstens] + "…" : text;
    }
}
