using System.Text.RegularExpressions;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services.Knowledge.Schema;

namespace GrowDiary.Web.Services;

/// <summary>Was eine Zeile im Mischplan ist — bestimmt, wie ihr Vorschlag gerechnet wird.</summary>
public enum MischplanRolle
{
    /// <summary>Die Grunddünger (A, B, PK) — an ihnen hängt der Dünger-EC.</summary>
    Grundduenger,

    /// <summary>CalMag — folgt dem Calcium im Ausgangswasser.</summary>
    CalMag,

    /// <summary>Alles übrige im Plan (Booster, Enzyme, Wurzelmittel).</summary>
    Zusatz,
}

/// <summary>Eine Zeile des Vorschlags.</summary>
/// <param name="Komponente">Name im Plan, z. B. „Aqua Flores A".</param>
/// <param name="MlProLiter">Dosis je Liter, mit der gerechnet wird (bei einer Spanne die Mitte).</param>
/// <param name="MlProLiterText">Wie der Plan sie nennt: „2" oder „1–2".</param>
/// <param name="VorschlagMl">Menge für die angesetzten Liter, ganze ml.</param>
/// <param name="ArtikelId">Der Kosten-Artikel, auf den gebucht wird — oder <c>null</c>, wenn keiner passt.</param>
/// <param name="Hinweis">Warum die Menge so ist (CalMag entfällt …), sonst <c>null</c>.</param>
public sealed record MischplanVorschlagZeile(
    string Komponente,
    MischplanRolle Rolle,
    double MlProLiter,
    string MlProLiterText,
    double VorschlagMl,
    int? ArtikelId,
    string? ArtikelName,
    string? ArtikelEinheit,
    string? Hinweis);

/// <summary>
/// Der Mischplan als Vorschlag für einen Wasserwechsel — auf die Liter und das Wasser gerechnet (A-006).
/// </summary>
/// <param name="WasserEcVorschlag">EC des Ausgangswassers nach Wasserart, mS/cm; <c>null</c>, wenn das Profil keinen Leitwert nennt.</param>
/// <param name="WasserEcQuelle">Woher der Wasser-EC stammt, als Satz für die Oberfläche.</param>
/// <param name="WasserEc">Der Wasser-EC, mit dem gerechnet wird: der eigene Messwert, sonst der Vorschlag.</param>
/// <param name="EcZielDuenger">EC-Ziel des Plans — gilt für Osmose als Basis (EC 0,0).</param>
/// <param name="EcZielGesamt">EC-Ziel am Tank: Dünger + Wasser.</param>
/// <param name="CalMagHinweis">Warum der CalMag-Vorschlag so ist; <c>null</c>, wenn der Plan kein CalMag kennt.</param>
/// <param name="Luecke">Warum es keinen Vorschlag gibt (kein Programm, kein Chart …), sonst <c>null</c>.</param>
public sealed record MischplanVorschlag(
    string? ProgrammName,
    string? SpalteLabel,
    double? AnlageLiter,
    double Liter,
    WaterSource Wasser,
    double OsmoseAnteil,
    double? WasserEcVorschlag,
    string WasserEcQuelle,
    double? WasserEc,
    double? EcZielDuenger,
    double? EcZielGesamt,
    double? PhMin,
    double? PhMax,
    IReadOnlyList<MischplanVorschlagZeile> Zeilen,
    string? CalMagHinweis,
    string? Luecke);

/// <summary>
/// Die Rechnung hinter dem Mischplan-Vorschlag — an genau einer Stelle (A-006).
/// </summary>
/// <remarks>
/// <para><b>Entschieden von Bru (05.10.2026):</b> der Wasser-EC folgt der
/// Wasserart (Leitung = Wasserprofil, Osmose = 0, Mischung anteilig), das
/// EC-Ziel am Tank ist Plan + Wasser, und CalMag folgt dem Calcium — mit
/// Leitungswasser keins, mit Osmose nach Plan, bei der Mischung anteilig.</para>
///
/// <para><b>Statisch und ohne Datenbank</b>, damit jede Regel prüfbar ist
/// (<c>MischplanVorschlagTests</c>). Die Oberfläche rechnet hier nichts nach —
/// sie zeigt, was diese Klasse liefert.</para>
/// </remarks>
public static partial class MischplanVorschlagRechnung
{
    /// <summary>Der Artikel für Leitungswasser — derselbe Name wie im Vorgang.</summary>
    public const string Leitungswasser = WasserwechselVorgangRepository.LeitungswasserArtikel;

    /// <summary>
    /// EC des Ausgangswassers in mS/cm — und woher er kommt.
    /// </summary>
    /// <remarks>
    /// <para>Leitung: der Leitwert aus dem Bericht (µS/cm ÷ 1000). Osmose: der
    /// eigene Messwert nach der Aufbereitung, wenn einer im Profil steht —
    /// sonst 0, so rechnet auch der Plan (Basis Osmose). Mischung: anteilig.</para>
    /// <para>Die Einheit wechselt hier bewusst: das Profil hält µS/cm (so steht
    /// es im Stadtbericht), der Tank mS/cm (so misst das Handgerät).</para>
    /// </remarks>
    public static (double? Ec, string Quelle) WasserEc(WaterProfile? profil, WaterSource wasser, double osmoseAnteil)
    {
        double? leitung = profil?.ConductivityUsCm is { } us ? Math.Round(us / 1000, 3) : null;
        var osmoseGemessen = profil?.TreatedConductivityUsCm is { } ro ? Math.Round(ro / 1000, 3) : (double?)null;
        var osmose = osmoseGemessen ?? 0;
        var bericht = string.IsNullOrWhiteSpace(profil?.SourceLabel) ? "Wasserprofil" : $"Wasserprofil {profil!.SourceLabel.Trim()}";
        var osmoseText = osmoseGemessen is null ? "Osmosewasser ≈ 0" : "Osmosewasser, dein Messwert nach der Aufbereitung";

        switch (wasser)
        {
            case WaterSource.RO:
                return (osmose, osmoseText);
            case WaterSource.Mixed:
                var prozentOsmose = Math.Round(osmoseAnteil * 100);
                var text = $"{prozentOsmose:0} % Osmose + {100 - prozentOsmose:0} % Leitung";
                return leitung is { } l
                    ? (Math.Round(osmose * osmoseAnteil + l * (1 - osmoseAnteil), 3), text)
                    : (null, $"{text} — im Wasserprofil steht kein Leitwert");
            default:
                return leitung is { } nurLeitung
                    ? (nurLeitung, bericht)
                    : (null, "Im Wasserprofil steht kein Leitwert — miss das Wasser oder trag den Wert unter Wasserprofil ein");
        }
    }

    /// <summary>Welche Rolle eine Komponente im Plan spielt.</summary>
    /// <remarks>
    /// Nach dem Namen, weil das Chart keine Rolle trägt. Erkannt werden die
    /// Schreibweisen aus dem Wissen: „CalMag Agent", „CaMg"; Grunddünger enden auf
    /// A oder B („Aqua Flores A", „Bloom B") oder heißen PK. Was keiner Regel
    /// folgt, ist ein Zusatz und wird nach Plan gerechnet.
    /// </remarks>
    public static MischplanRolle Rolle(string komponente)
    {
        var flach = Regex.Replace(komponente.ToLowerInvariant(), "[^a-z0-9]", "");
        if (flach.Contains("calmag") || flach == "camg" || flach.StartsWith("camg") || flach.Contains("calimagic"))
            return MischplanRolle.CalMag;

        var woerter = Regex.Split(komponente.Trim().ToLowerInvariant(), @"\s+");
        if (woerter.Length > 1 && woerter[^1] is "a" or "b") return MischplanRolle.Grundduenger;
        if (woerter.Contains("pk") || flach.StartsWith("pk")) return MischplanRolle.Grundduenger;
        return MischplanRolle.Zusatz;
    }

    /// <summary>
    /// Wie viel des Plan-CalMags gebraucht wird (0 … 1) — und warum.
    /// </summary>
    /// <remarks>
    /// Leitungswasser bringt sein Calcium mit (Bru: „CalMag folgt dem Calcium").
    /// Das gilt nur, wenn das Profil einen Calcium-Wert nennt und das Wasser
    /// nicht weich ist — die Grenze „weich" ist dieselbe wie in der Wasser-Ampel
    /// (<see cref="WasserAmpelService.WeichBisDh"/>, WRMG § 9). Ohne Calcium-Wert
    /// wird wie bei Osmose gerechnet: lieber CalMag vorschlagen als einen Mangel
    /// still hinnehmen.
    /// </remarks>
    public static (double Faktor, string Hinweis) CalMag(WaterProfile? profil, WaterSource wasser, double osmoseAnteil)
    {
        if (wasser == WaterSource.RO)
            return (1, "Osmosewasser hat kein Calcium und Magnesium — CalMag nach Plan.");

        if (profil?.CalciumMgL is not { } calcium)
            return (1, "Im Wasserprofil steht kein Calcium-Wert — CalMag deshalb wie bei Osmose nach Plan. Trag den Wert unter Wasserprofil nach.");

        if (profil.TotalHardnessDh is { } gh && gh < WasserAmpelService.WeichBisDh)
            return (1, $"Dein Leitungswasser ist weich ({Zahl(gh, "0.#")} °dH) und bringt kaum Calcium mit — CalMag nach Plan.");

        var magnesium = profil.MagnesiumMgL is { } mg ? $" und {Zahl(mg, "0.#")} mg/L Magnesium" : string.Empty;
        if (wasser == WaterSource.Mixed)
        {
            var leitung = Math.Round((1 - osmoseAnteil) * 100);
            return (osmoseAnteil,
                $"{leitung:0} % Leitungswasser bringen Calcium mit ({Zahl(calcium, "0.#")} mg/L im Profil) — CalMag nur für den Osmose-Anteil.");
        }

        return (0, $"Dein Leitungswasser bringt {Zahl(calcium, "0.#")} mg/L Calcium{magnesium} mit — deshalb schlägt der Plan kein CalMag vor.");
    }

    /// <summary>Der Kosten-Artikel zu einer Komponente — oder <c>null</c>.</summary>
    /// <remarks>
    /// Erst genau (Name oder Produkt gleich der Komponente), dann als ganzes Wort
    /// im Namen bzw. Produkt („Canna Aqua Flores A" enthält „Aqua Flores A").
    /// Passen mehrere, gilt keiner — dann wählt der Nutzer, statt dass die App rät.
    /// </remarks>
    public static Verbrauchsartikel? ArtikelFuer(string komponente, IEnumerable<Verbrauchsartikel> artikel)
    {
        var aktive = artikel.Where(a => a.Aktiv).ToList();
        var gesucht = Stammdaten.Normalisieren(komponente);

        var genau = aktive.Where(a =>
            string.Equals(Stammdaten.Normalisieren(a.Name), gesucht, StringComparison.OrdinalIgnoreCase)
            || (a.Produkt is { } p && string.Equals(Stammdaten.Normalisieren(p), gesucht, StringComparison.OrdinalIgnoreCase))).ToList();
        if (genau.Count == 1) return genau[0];
        if (genau.Count > 1) return null;

        var muster = new Regex($@"(^|[^\p{{L}}\p{{N}}]){Regex.Escape(gesucht)}($|[^\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase);
        var enthalten = aktive.Where(a => muster.IsMatch(a.Name) || (a.Produkt is { } p && muster.IsMatch(p))).ToList();
        return enthalten.Count == 1 ? enthalten[0] : null;
    }

    /// <summary>Rechnet den Vorschlag.</summary>
    /// <param name="spalte">Die Chart-Spalte, die heute gilt; <c>null</c> mit <paramref name="luecke"/>.</param>
    /// <param name="osmoseProzent">Nur bei Mischung: Anteil Osmose 0–100.</param>
    /// <param name="wasserEcEigen">Selbst gemessener Wasser-EC; überschreibt den Vorschlag.</param>
    public static MischplanVorschlag Rechnen(
        string? programmName,
        FeedChartColumn? spalte,
        string? luecke,
        double? anlageLiter,
        WaterProfile? profil,
        IReadOnlyList<Verbrauchsartikel> artikel,
        double liter,
        WaterSource wasser,
        double? osmoseProzent,
        double? wasserEcEigen)
    {
        var anteil = wasser switch
        {
            WaterSource.RO => 1.0,
            WaterSource.Mixed => Math.Clamp(osmoseProzent ?? 0, 0, 100) / 100,
            _ => 0.0,
        };

        var (ecVorschlag, ecQuelle) = WasserEc(profil, wasser, anteil);
        var wasserEc = wasserEcEigen ?? ecVorschlag;
        var (calMagFaktor, calMagHinweis) = CalMag(profil, wasser, anteil);

        var zeilen = (spalte?.Items ?? []).Select(item =>
        {
            var rolle = Rolle(item.Component);
            var proLiter = (item.MinMlPerLiter + item.MaxMlPerLiter) / 2;
            var text = item.MinMlPerLiter == item.MaxMlPerLiter
                ? Zahl(item.MinMlPerLiter, "0.##")
                : $"{Zahl(item.MinMlPerLiter, "0.##")}–{Zahl(item.MaxMlPerLiter, "0.##")}";
            var faktor = rolle == MischplanRolle.CalMag ? calMagFaktor : 1;
            var menge = Math.Round(proLiter * liter * faktor, 0, MidpointRounding.AwayFromZero);
            var treffer = ArtikelFuer(item.Component, artikel);
            string? hinweis = rolle == MischplanRolle.CalMag && faktor == 0
                ? "entfällt — Calcium aus dem Leitungswasser"
                : item.MinMlPerLiter != item.MaxMlPerLiter ? $"Plan {text} ml/L — gerechnet mit der Mitte" : null;
            return new MischplanVorschlagZeile(item.Component, rolle, proLiter, text, menge,
                treffer?.Id, treffer?.Name, treffer?.Einheit, hinweis);
        }).ToList();

        var ecZiel = spalte?.EcTarget;
        return new MischplanVorschlag(
            programmName,
            spalte?.Label,
            anlageLiter,
            liter,
            wasser,
            anteil,
            ecVorschlag,
            ecQuelle,
            wasserEc,
            ecZiel,
            ecZiel is { } z && wasserEc is { } w ? Math.Round(z + w, 2) : null,
            spalte?.PhMin,
            spalte?.PhMax,
            zeilen,
            zeilen.Any(z => z.Rolle == MischplanRolle.CalMag) ? calMagHinweis : null,
            luecke);
    }

    private static string Zahl(double wert, string format) => wert.ToString(format, AppCulture.German);
}
