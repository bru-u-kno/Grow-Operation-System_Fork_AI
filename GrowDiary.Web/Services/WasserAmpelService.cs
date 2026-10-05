using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>Ein bewerteter Wert aus dem Wasserbericht.</summary>
/// <param name="Stufe">gut, hinweis oder warnung.</param>
public sealed record AmpelPunkt(
    string Feld,
    string Label,
    string Stufe,
    string Wert,
    string Aussage,
    string Quelle);

/// <summary>Die Ampel über dem ganzen Wasserprofil.</summary>
public sealed record WasserAmpel(
    string Stufe,
    string Zusammenfassung,
    IReadOnlyList<AmpelPunkt> Punkte);

/// <summary>
/// Sagt dem Nutzer, ob sein Leitungswasser taugt — und was es ihm abverlangt.
/// </summary>
/// <remarks>
/// <para>Die Zahlen im Wasserprofil standen bisher nur da. Wer nicht weiss, dass
/// 12 °dH Karbonathärte den pH immer wieder hochzieht, liest sie und ist so klug
/// wie zuvor. Diese Klasse macht aus den Zahlen Sätze.</para>
///
/// <para><b>Jede Schwelle hat eine Quelle, und die Quelle steht im Ergebnis.</b>
/// Erfundene Präzision wäre hier besonders teuer: eine Ampel, die grundlos rot
/// zeigt, kostet den Nutzer Geld für eine Osmoseanlage, die er nicht braucht.
/// Die Grenzen kommen aus dem Gartenbau (Penn State Extension) und aus dem
/// deutschen Gesetz (WRMG) — nicht aus dem Bauch.</para>
///
/// <para><b>Der Hydro-Vorbehalt:</b> die Gartenbau-Werte kennen auch ein „zu
/// weich" — dort liefert das Giesswasser das Calcium mit. Im RDWC mit einem
/// vollständigen Düngerprogramm liefert der Dünger es. Weiches Wasser ist hier
/// deshalb ein Vorteil, kein Mangel; nur ohne CalMag-Gabe bleibt der Hinweis.</para>
/// </remarks>
public sealed class WasserAmpelService
{
    // 1 °dH = 17,848 mg/L CaCO3; 1 mmol/L CaCO3 = 5,6 °dH. Reine Umrechnung.
    private const double MgProDh = 17.848;

    /// <summary>
    /// Bis hierhin (ausschließlich) gilt Wasser als „weich" — WRMG § 9, Härtebereich weich unter 8,4 °dH.
    /// </summary>
    /// <remarks>Öffentlich, weil der Wasserwechsel-Vorschlag (A-006) dieselbe Grenze für CalMag liest.</remarks>
    public const double WeichBisDh = 8.4;

    private const string QuellePsu =
        "Penn State Extension, „Interpreting Irrigation Water Tests\"";
    private const string QuelleWrmg =
        "Wasch- und Reinigungsmittelgesetz (WRMG) § 9 — gesetzliche Härtebereiche";

    private readonly WaterProfileStore _store;

    public WasserAmpelService(WaterProfileStore store)
    {
        _store = store;
    }

    /// <summary>Die Ampel zum gespeicherten Profil, oder null wenn nichts erfasst ist.</summary>
    public WasserAmpel? Aktuell(bool duengerLiefertCalMag = true)
        => _store.Get() is { HasAnyValue: true } profil ? Bewerten(profil, duengerLiefertCalMag) : null;

    /// <summary>
    /// Bewertet, was im Profil steht — und schweigt über alles andere.
    /// </summary>
    /// <remarks>Statisch und ohne Datenbank, damit die Schwellen prüfbar sind.</remarks>
    public static WasserAmpel Bewerten(WaterProfile profil, bool duengerLiefertCalMag = true)
    {
        var punkte = new List<AmpelPunkt>();

        // Karbonathärte = Alkalinität = der pH-Puffer. Der wichtigste Wert von
        // allen: nicht der pH des Wassers entscheidet, sondern wie hartnaeckig
        // es zu seinem pH zurueckkehrt.
        if (profil.CarbonateHardnessDh is { } kh)
        {
            var mgL = kh * MgProDh;
            var (stufe, satz) = mgL switch
            {
                < 30 => ("hinweis",
                    "Kaum Puffer — der pH kippt nach jeder Gabe schnell in beide Richtungen. Häufiger nachmessen."),
                <= 100 => ("gut",
                    "Guter Puffer: der pH lässt sich einstellen und bleibt stehen."),
                <= 150 => ("hinweis",
                    "Erhöhter Puffer — der pH zieht zwischen den Messungen nach oben. pH-Down einplanen."),
                _ => ("warnung",
                    "Starker Puffer: der pH klettert immer wieder hoch, egal wie oft du ihn stellst. Hier hilft nur teilweise Osmose oder Säurevorbehandlung."),
            };

            punkte.Add(new AmpelPunkt(
                "carbonateHardnessDh", "Karbonathärte (Puffer)", stufe,
                $"{Zahl(kh)} °dH ≈ {Zahl(mgL, "0")} mg/L CaCO₃", satz,
                $"{QuellePsu}: ideal 30–100 mg/L CaCO₃, Problem über 150 mg/L"));
        }

        // Gesamthärte: gesetzliche Einordnung plus die Hydro-Lesart.
        if (profil.TotalHardnessDh is { } gh)
        {
            var bereich = gh < WeichBisDh ? "weich" : gh <= 14 ? "mittel" : "hart";
            var (stufe, satz) = gh switch
            {
                < WeichBisDh when duengerLiefertCalMag => ("gut",
                    "Weiches Wasser — im RDWC der Idealfall: du bestimmst Calcium und Magnesium allein über den Dünger."),
                < WeichBisDh => ("hinweis",
                    "Weiches Wasser bringt kaum Calcium und Magnesium mit — das muss dein Dünger liefern (CalMag)."),
                <= 14 => ("hinweis",
                    "Mittlere Härte: das Wasser bringt Calcium und Magnesium schon mit. Beim CalMag entsprechend zurückhaltender dosieren."),
                _ => ("warnung",
                    "Hartes Wasser: viel Calcium und Magnesium sind schon drin, bevor du düngst. Kalkablagerungen und schwer stellbarer pH sind die Folge."),
            };

            punkte.Add(new AmpelPunkt(
                "totalHardnessDh", "Gesamthärte", stufe,
                $"{Zahl(gh)} °dH (Härtebereich {bereich})", satz,
                $"{QuelleWrmg}; Ablagerungsgrenze aus {QuellePsu} (über 150 mg/L CaCO₃)"));
        }

        // Wer selbst aufbereitet, mischt nicht mit dem Wasser aus dem Bericht,
        // sondern mit dem dahinter. Dann zaehlt DAS als Startpunkt — der
        // Bericht bleibt als Herkunft benannt, aber beurteilt wird, was
        // wirklich ins Reservoir laeuft.
        if (profil.TreatedConductivityUsCm is { } behandeltUs)
        {
            var behandeltMs = behandeltUs / 1000.0;
            punkte.Add(new AmpelPunkt(
                "treatedConductivityUsCm", "Leitfähigkeit nach deiner Aufbereitung",
                behandeltMs < 0.5 ? "gut" : "hinweis",
                $"{Zahl(behandeltUs, "0")} µS/cm = {Zahl(behandeltMs, "0.00")} mS/cm",
                behandeltMs < 0.5
                    ? "Damit setzt du an — der volle EC-Spielraum bleibt für den Dünger."
                    : "Nach der Aufbereitung noch spürbar belastet — Filter oder Membran prüfen.",
                "Dein eigener Messwert nach der Aufbereitung; er zählt vor dem Stadtbericht."));
        }

        // Leitfähigkeit: was das Wasser schon mitbringt, fehlt dir spaeter im
        // Duenger-Budget. Der Gartenbau misst in mS/cm, der Bericht in µS/cm.
        if (profil.ConductivityUsCm is { } us)
        {
            var ms = us / 1000.0;
            var (stufe, satz) = ms switch
            {
                < 0.5 => ("gut",
                    "Sauberes Ausgangswasser — der volle EC-Spielraum bleibt für den Dünger."),
                < 1.0 => ("hinweis",
                    "Spürbar vorbelastet: dieser Anteil geht von deinem EC-Ziel ab, bevor der erste Dünger drin ist."),
                _ => ("warnung",
                    "Hoch belastetes Wasser. Der Gartenbau setzt bei empfindlichen Kulturen 1,0 mS/cm als Grenze — darüber bleibt für den Dünger kaum Raum."),
            };

            punkte.Add(new AmpelPunkt(
                "conductivityUsCm", "Leitfähigkeit (Start-EC)", stufe,
                $"{Zahl(us, "0")} µS/cm = {Zahl(ms, "0.00")} mS/cm", satz,
                $"{QuellePsu}: unter 1,0 mS/cm bei empfindlichen Kulturen, über 3 mS/cm schwerwiegend"));
        }

        if (profil.Ph is { } ph)
        {
            var gut = ph is >= 5.0 and <= 7.0;
            punkte.Add(new AmpelPunkt(
                "ph", "pH des Leitungswassers", gut ? "gut" : "hinweis",
                Zahl(ph),
                gut
                    ? "Unauffällig. Für dein Reservoir zählt ohnehin die Karbonathärte mehr als dieser Wert."
                    : "Außerhalb des üblichen Bereichs — wichtiger bleibt trotzdem die Karbonathärte, die den pH zurückzieht.",
                $"{QuellePsu}: 5,0–7,0 unbedenklich"));
        }

        if (profil.SodiumMgL is { } na)
        {
            var warn = na > 50;
            punkte.Add(new AmpelPunkt(
                "sodiumMgL", "Natrium", warn ? "warnung" : "gut",
                $"{Zahl(na)} mg/L",
                warn
                    ? "Zu viel Natrium. Es reichert sich im Kreislauf an, blockiert die Kalium-Aufnahme und geht nur durch Wasserwechsel wieder raus."
                    : "Unbedenklich.",
                $"{QuellePsu}: über 50 mg/L möglicherweise toxisch"));
        }

        if (profil.ChlorideMgL is { } cl)
        {
            var (stufe, satz) = cl switch
            {
                <= 30 => ("gut", "Unbedenklich."),
                <= 100 => ("hinweis", "Erhöht — empfindliche Sorten können mit Blattrandnekrosen reagieren."),
                _ => ("warnung", "Zu viel Chlorid; im geschlossenen Kreislauf reichert es sich zusätzlich an."),
            };

            punkte.Add(new AmpelPunkt(
                "chlorideMgL", "Chlorid", stufe, $"{Zahl(cl)} mg/L", satz,
                $"{QuellePsu}: empfindliche Pflanzen ab 30 mg/L, die meisten vertragen bis 100 mg/L"));
        }

        // Calcium, Magnesium und Nitrat bekommen bewusst KEINE Ampel: im
        // Gartenbau sind ihre Untergrenzen sinnvoll, weil dort das Giesswasser
        // die Quelle ist. Hier ist der Duenger die Quelle. Was bleibt, ist die
        // Rechengroesse — und die ist eine Tatsache, kein Urteil.
        foreach (var (feld, label, wert) in new (string, string, double?)[]
        {
            ("calciumMgL", "Calcium", profil.CalciumMgL),
            ("magnesiumMgL", "Magnesium", profil.MagnesiumMgL),
            ("nitrateMgL", "Nitrat", profil.NitrateMgL),
        })
        {
            if (wert is not { } v) continue;
            punkte.Add(new AmpelPunkt(
                feld, label, "gut", $"{Zahl(v)} mg/L",
                "Bringt dein Wasser schon mit — rechne es bei der Düngung mit, statt es obendrauf zu geben.",
                "Kein Grenzwert: im Hydro-Kreislauf liefert der Dünger diesen Nährstoff, nicht das Wasser."));
        }

        var gesamt = punkte.Any(p => p.Stufe == "warnung") ? "warnung"
            : punkte.Any(p => p.Stufe == "hinweis") ? "hinweis"
            : "gut";

        return new WasserAmpel(gesamt, Zusammenfassen(gesamt, punkte), punkte);
    }

    private static string Zusammenfassen(string gesamt, List<AmpelPunkt> punkte)
    {
        if (punkte.Count == 0) return "Noch keine Werte erfasst.";

        var betroffen = punkte.Where(p => p.Stufe == gesamt).Select(p => p.Label).ToList();
        return gesamt switch
        {
            "gut" => "Dein Leitungswasser ist gut geeignet — nichts davon muss dich beschäftigen.",
            "hinweis" => $"Brauchbares Wasser mit einer Eigenheit: {string.Join(", ", betroffen)}.",
            _ => $"Achtung bei diesem Wasser: {string.Join(", ", betroffen)}.",
        };
    }

    private static string Zahl(double wert, string format = "0.#")
        => wert.ToString(format, System.Globalization.CultureInfo.GetCultureInfo("de-DE"));
}
