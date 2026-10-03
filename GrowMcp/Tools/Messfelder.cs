using System.Globalization;
using System.Text.Json.Nodes;

namespace GrowMcp.Tools;

/// <summary>Die Werte einer Messung, wie ein Assistent sie diktiert bekommt.</summary>
/// <remarks>Alles optional: eingetragen wird, was gesagt wurde.</remarks>
public sealed record Messwerte
{
    public double? Ph { get; init; }
    public double? Ec { get; init; }
    public double? WassertemperaturC { get; init; }
    public double? OrpMv { get; init; }
    public double? SauerstoffMgL { get; init; }
    public double? FuellstandLiter { get; init; }
    public double? FuellstandCm { get; init; }
    public double? NachfuellLiter { get; init; }
    public double? NachfuellEc { get; init; }
    public double? LufttemperaturC { get; init; }
    public double? LuftfeuchteProzent { get; init; }
    public double? Co2Ppm { get; init; }
    public double? Ppfd { get; init; }
    public double? LuftstromAmBlattMProMin { get; init; }
    public double? HoeheCm { get; init; }
    public double? GiessmengeMl { get; init; }
    public double? AblaufMl { get; init; }
    public double? GiesswasserPh { get; init; }
    public double? GiesswasserEc { get; init; }
    public double? DrainPh { get; init; }
    public double? DrainEc { get; init; }
    public string? Stroemung { get; init; }
    public bool? Wasserwechsel { get; init; }
    public string? Notiz { get; init; }
}

/// <summary>
/// Von den diktierten Werten zu den Feldern, die Grow OS erwartet.
/// </summary>
/// <remarks>
/// <para>Fork AI (A-004, 03.10.2026): Ziel ist <c>MeasurementUpsertRequest</c> in
/// Grow OS (<c>POST api/grows/{id}/measurements</c>, <c>PUT
/// api/measurements/{id}</c>). Die Namen stehen hier EINMAL. Geprüft werden sie
/// nicht gegen eine abgetippte Liste, sondern gegen den echten Typ:
/// <c>MessfelderTests</c> lädt die gebaute <c>GrowDiary.Web.dll</c>, liest den
/// Rumpf mit <c>JsonUnmappedMemberHandling.Disallow</c> in genau diesen Typ
/// zurück und verlangt, dass jede Eigenschaft dort entweder hier belegt ist oder
/// eine Ausnahme mit Grund hat.</para>
///
/// <para>Ein falscher Name wäre hier kein Fehler, sondern Stille: ASP.NET
/// übergeht unbekannte Felder, Grow OS speichert eine Messung ohne den Wert, und
/// der Assistent meldet „eingetragen".</para>
/// </remarks>
public static class Messfelder
{
    /// <summary>Feld in Grow OS → Wert aus dem Diktat.</summary>
    public static IReadOnlyList<(string Feld, Func<Messwerte, object?> Wert)> Tabelle { get; } =
    [
        ("reservoirPh", m => m.Ph),
        ("reservoirEc", m => m.Ec),
        ("reservoirWaterTempC", m => m.WassertemperaturC),
        ("orpMv", m => m.OrpMv),
        ("dissolvedOxygenMgL", m => m.SauerstoffMgL),
        ("reservoirLevelLiters", m => m.FuellstandLiter),
        ("reservoirLevelCm", m => m.FuellstandCm),
        ("topOffLiters", m => m.NachfuellLiter),
        ("addbackEc", m => m.NachfuellEc),
        ("airTemperatureC", m => m.LufttemperaturC),
        ("humidityPercent", m => m.LuftfeuchteProzent),
        ("co2Ppm", m => m.Co2Ppm),
        ("ppfdMol", m => m.Ppfd),
        ("airflowAtLeafMPerMin", m => m.LuftstromAmBlattMProMin),
        ("heightCm", m => m.HoeheCm),
        ("waterAmountMl", m => m.GiessmengeMl),
        ("runoffAmountMl", m => m.AblaufMl),
        ("irrigationPh", m => m.GiesswasserPh),
        ("irrigationEc", m => m.GiesswasserEc),
        ("drainPh", m => m.DrainPh),
        ("drainEc", m => m.DrainEc),
        ("waterFlow", m => Stroemung(m.Stroemung)),
        ("solutionChange", m => m.Wasserwechsel),
        ("notes", m => string.IsNullOrWhiteSpace(m.Notiz) ? null : m.Notiz.Trim()),
    ];

    /// <summary>Die Felder für Zeitpunkt und Phase — sie kommen nicht aus dem Diktat der Werte.</summary>
    public const string FeldZeitpunkt = "takenAtLocal";
    public const string FeldPhase = "stage";

    /// <summary>Hat das Diktat überhaupt etwas zum Eintragen?</summary>
    public static bool Leer(Messwerte werte) => Tabelle.All(eintrag => eintrag.Wert(werte) is null);

    /// <summary>Die belegten Werte in einen Rumpf schreiben; nicht genannte bleiben, wie sie sind.</summary>
    public static void Einsetzen(JsonObject rumpf, Messwerte werte)
    {
        foreach (var (feld, wert) in Tabelle)
        {
            switch (wert(werte))
            {
                case null: break;
                case double zahl: rumpf[feld] = zahl; break;
                case bool janein: rumpf[feld] = janein; break;
                case string text: rumpf[feld] = text; break;
                default: throw new InvalidOperationException($"Unerwarteter Typ für {feld}.");
            }
        }
    }

    /// <summary>
    /// Die Phasen von Grow OS (<c>GrowStage</c>) mit den Wörtern, mit denen man sie sagt.
    /// </summary>
    /// <remarks>
    /// Links der Wert für Grow OS — <c>MessfelderTests</c> verlangt, dass die
    /// Liste genau den Namen des Enums in Grow OS entspricht.
    /// </remarks>
    public static IReadOnlyList<(string Wert, string[] Woerter)> Phasen { get; } =
    [
        ("Seedling", ["sämling", "saemling", "keimling", "seedling"]),
        ("Clone", ["steckling", "clone"]),
        ("Veg", ["veg", "vegi", "wachstum", "vegetativ", "wachstumsphase"]),
        ("Transition", ["übergang", "uebergang", "stretch", "transition"]),
        ("Flower", ["blüte", "bluete", "flower", "flowering"]),
        ("Finish", ["finish", "spülen", "spuelen", "reife"]),
        ("Dry", ["trocknen", "trocknung", "dry"]),
        ("Cure", ["aushärten", "aushaerten", "curing", "cure"]),
    ];

    /// <summary>Ein gesagtes Wort in die Phase von Grow OS übersetzen, oder <c>null</c>.</summary>
    public static string? Phase(string? gesagt)
    {
        if (string.IsNullOrWhiteSpace(gesagt)) return null;
        var wort = gesagt.Trim().ToLowerInvariant();
        foreach (var (wert, woerter) in Phasen)
        {
            if (string.Equals(wert, gesagt.Trim(), StringComparison.OrdinalIgnoreCase) || woerter.Contains(wort)) return wert;
        }
        return null;
    }

    /// <summary>Die Strömung im System (<c>WaterFlowLevel</c>): drei Stufen, keine Liter.</summary>
    public static IReadOnlyList<(string Wert, string[] Woerter)> Stroemungen { get; } =
    [
        ("Weak", ["schwach", "weak"]),
        ("Moderate", ["mittel", "moderat", "moderate"]),
        ("Strong", ["stark", "strong"]),
    ];

    /// <summary>Die gesagte Strömung als Wert für Grow OS. Unbekanntes geht unverändert durch — Grow OS lehnt es dann mit Feldfehler ab.</summary>
    public static string? Stroemung(string? gesagt)
    {
        if (string.IsNullOrWhiteSpace(gesagt)) return null;
        var wort = gesagt.Trim().ToLowerInvariant();
        foreach (var (wert, woerter) in Stroemungen)
        {
            if (woerter.Contains(wort)) return wert;
        }
        return gesagt.Trim();
    }

    /// <summary>Das Format, in dem Grow OS die Ortszeit einer Messung oder eines Eintrags liest.</summary>
    /// <remarks>Steht in Grow OS als <c>MeasurementsApiController.ZeitFormat</c>.</remarks>
    public const string OrtszeitFormat = "yyyy-MM-ddTHH:mm";

    private static readonly string[] Lesbare =
    [
        "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss",
        "dd.MM.yyyy HH:mm", "d.M.yyyy HH:mm", "dd.MM.yyyy H:mm", "d.M.yyyy H:mm",
    ];

    /// <summary>Einen gesagten Zeitpunkt (Ortszeit) in das Format von Grow OS bringen.</summary>
    /// <returns><c>null</c>, wenn er sich nicht lesen lässt.</returns>
    public static string? Ortszeit(string gesagt)
        => DateTime.TryParseExact(gesagt.Trim(), Lesbare, CultureInfo.InvariantCulture, DateTimeStyles.None, out var zeit)
            ? zeit.ToString(OrtszeitFormat, CultureInfo.InvariantCulture)
            : null;

    /// <summary>
    /// Einen gesagten Zeitpunkt als UTC — für Wartung und Kalibrierung (<c>PerformedAtUtc</c>).
    /// </summary>
    /// <remarks>
    /// Mit Zeitzone („…+02:00", „…Z") wird sie genommen. Ohne gilt die Ortszeit
    /// dieses Add-ons; der Supervisor gibt jedem Add-on die Zeitzone von Home
    /// Assistant mit (<c>TZ</c>).
    /// </remarks>
    public static string? Utc(string gesagt)
    {
        var text = gesagt.Trim();
        if (text.EndsWith('Z') || System.Text.RegularExpressions.Regex.IsMatch(text, @"[+-]\d{2}:\d{2}$"))
        {
            return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var mitZone)
                ? mitZone.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
                : null;
        }

        return DateTime.TryParseExact(text, Lesbare, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var ort)
            ? ort.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
            : null;
    }
}
