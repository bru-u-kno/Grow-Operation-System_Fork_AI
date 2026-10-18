using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>Was der Wächter über eine Pumpe sagt.</summary>
/// <param name="Stufe">ok, warnung oder kritisch.</param>
/// <param name="Herkunft">Woran er es erkannt hat — Zustand, Leistung oder beides.</param>
public sealed record PumpBefund(
    string Schluessel,
    string Name,
    string Stufe,
    string Meldung,
    string Herkunft);

/// <summary>
/// Merkt, wenn eine Pumpe steht — die einzige Störung, die in zwei Tagen alles kostet.
/// </summary>
/// <remarks>
/// <para><b>Warum das hier existiert:</b> `pump-air` war seit jeher als Messgröße
/// eingerichtet und wurde von keinem Dienst gelesen. Schlimmer: eine Alarmregel
/// darauf hätte nie ausgelöst, weil <see cref="AlertEvaluationService"/> einen
/// Zahlenwert verlangt und ein An/Aus-Zustand keinen hat. Die Regel liesse sich
/// anlegen, speichern, anzeigen — und bliebe stumm.</para>
///
/// <para><b>Warum es dringend ist:</b> Fällt die Belüftung aus, wird das
/// Reservoir binnen Stunden anaerob; Pythium macht aus einem gesunden DWC in
/// etwa 48 Stunden einen Totalausfall. Ein paar Stunden Vorsprung sind hier der
/// Unterschied zwischen einem Ärgernis und dem ganzen Lauf.</para>
///
/// <para><b>Zwei Signale, weil eines lügt:</b> Der Zustand aus Home Assistant
/// sagt, ob geschaltet ist. Die Leistungsaufnahme sagt, ob wirklich etwas
/// läuft. Eine Pumpe mit gerissener Membran meldet fröhlich „an" und fördert
/// nichts — das sieht nur, wer auf die Watt schaut.</para>
///
/// <para><b>Nicht von der Begleitungsstufe abhängig.</b> Anders als die
/// fälligen Routinen wird das hier auch dem Experten gesagt. Wer sich
/// Erinnerungen abbestellt, bestellt nicht die Warnung vor dem Totalverlust ab.</para>
/// </remarks>
public sealed class PumpWatchService
{
    /// <summary>Schonfrist, bevor ein Aus als Ausfall zählt — Faustregel gegen Flattern.</summary>
    /// <remarks>
    /// Wer seine Umwälzung im Intervall fährt, stellt sie höher; deshalb steckt
    /// sie in den Einstellungen und nicht im Code fest.
    /// </remarks>
    public const int StandardSchonfristMinuten = 15;

    public const string SchonfristKey = "pump-watch-grace-minutes";

    /// <summary>Unter dieser Leistung läuft nichts mehr, was Wasser oder Luft bewegt.</summary>
    /// <remarks>Faustregel: Messsteckdosen zeigen im Leerlauf typisch unter 1 W.</remarks>
    public const double LeerlaufWatt = 1.0;

    private static readonly (string Zustand, string Leistung, string Name, bool Lebenswichtig)[] Pumpen =
    [
        ("pump-air", "pump-air-power", "Luftpumpe", true),
        ("pump-circulation", "pump-circulation-power", "Umwälzpumpe", false),
    ];

    /// <summary>
    /// Beurteilt beide Pumpen aus den Live-Zuständen eines Zelts.
    /// </summary>
    /// <remarks>
    /// Statisch und ohne Datenbank: an dieser Entscheidung hängt eine
    /// Push-Nachricht, die den Betreiber nachts aus dem Bett holt. Sie muss
    /// prüfbar sein, ohne dass ein Home Assistant läuft.
    /// </remarks>
    public static IReadOnlyList<PumpBefund> Beurteilen(
        IReadOnlyDictionary<string, HomeAssistantState> zustaende,
        DateTime nowUtc,
        int schonfristMinuten = StandardSchonfristMinuten)
    {
        var befunde = new List<PumpBefund>();

        foreach (var (zustandKey, leistungKey, name, lebenswichtig) in Pumpen)
        {
            zustaende.TryGetValue(zustandKey, out var zustand);
            zustaende.TryGetValue(leistungKey, out var leistung);

            // Nichts eingerichtet, nichts zu sagen. Ein „unbekannt = Gefahr"
            // waere hier das Gegenteil von hilfreich: die meisten haben fuer
            // ihre Pumpen gar keinen Sensor.
            if (zustand is null && leistung?.NumericValue is null) continue;

            var aus = zustand is not null && IstAus(zustand.State);
            var watt = leistung?.NumericValue;
            var stromlos = watt is { } w && w < LeerlaufWatt;

            if (aus)
            {
                var seit = zustand!.LastChanged is { } geaendert
                    ? (int)Math.Max(0, (nowUtc - geaendert.ToUniversalTime()).TotalMinutes)
                    : schonfristMinuten;

                if (seit < GiltFuer(lebenswichtig, schonfristMinuten)) continue;

                befunde.Add(new PumpBefund(
                    zustandKey, name,
                    lebenswichtig ? "kritisch" : "warnung",
                    $"{name} ist seit {seit} Minuten aus." + Folge(lebenswichtig),
                    $"Zustand aus Home Assistant; Schonfrist {schonfristMinuten} Minuten (Faustregel, in den Einstellungen änderbar)."));
                continue;
            }

            // Der teuerste Fall: sie meldet „an" und zieht nichts.
            if (stromlos && (zustand is null || !aus))
            {
                befunde.Add(new PumpBefund(
                    zustandKey, name, "kritisch",
                    $"{name} meldet „an“, zieht aber nur {watt!.Value.ToString("0.0", AppCulture.German)}\u00a0W — sie läuft nicht."
                        + Folge(lebenswichtig),
                    $"Leistungsaufnahme unter {LeerlaufWatt.ToString("0.#", AppCulture.German)}\u00a0W (Faustregel für Leerlauf)."));
                continue;
            }

            befunde.Add(new PumpBefund(zustandKey, name, "ok", $"{name} läuft.",
                (zustand, watt) switch
                {
                    (not null, not null) => "Zustand und Leistungsaufnahme stimmen überein.",
                    (not null, null) => "Zustand aus Home Assistant.",
                    _ => "Leistungsaufnahme der Steckdose.",
                }));
        }

        return befunde;
    }

    /// <summary>
    /// Die Schonfrist, die für <i>diese</i> Pumpe gilt.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Anlass (01.09.2026).</b> Bis hierher galt <b>eine</b>
    /// Schonfrist für <b>beide</b> Pumpen. Einstellbar ist sie aber nur wegen
    /// der Umwälzung — „Wer seine Umwälzung im Intervall fährt, stellt sie
    /// höher" —, und <see cref="Folge"/> rät unter jeder Umwälz-Warnung
    /// wörtlich dazu.</para>
    ///
    /// <para>Wer dem folgte und 240 Minuten eintrug, hatte damit den Alarm für
    /// die <b>Luftpumpe</b> um vier Stunden verzögert — den Alarm, über den
    /// dieselbe Klasse schreibt, dass ohne Belüftung das Reservoir binnen
    /// Stunden sauerstoffarm wird. Ausfall um 23 Uhr, Meldung um 3 Uhr. Der
    /// Setter lässt bis 720 Minuten zu; im schlimmsten Fall knapp zwölf
    /// Stunden.</para>
    ///
    /// <para><b>Deshalb nur nach unten.</b> Eine eigene Schonfrist darf den
    /// lebenswichtigen Alarm schärfer stellen, nie stumpfer: wer 5 Minuten
    /// einträgt, will früher gewarnt werden, und das bekommt er auch.</para>
    /// </remarks>
    private static int GiltFuer(bool lebenswichtig, int schonfristMinuten)
        => lebenswichtig
            ? Math.Min(schonfristMinuten, StandardSchonfristMinuten)
            : schonfristMinuten;

    /// <summary>Der Satz, der aus einer Meldung eine Entscheidungshilfe macht.</summary>
    private static string Folge(bool lebenswichtig)
        => lebenswichtig
            ? " Ohne Belüftung wird das Reservoir binnen Stunden sauerstoffarm; Wurzelfäule kann einen Lauf in rund zwei Tagen erledigen. Solange keine Pumpe läuft: Deckel öffnen und von Hand umwälzen."
            : " Ohne Umwälzung stehen Nährstoffe und Temperatur in den Eimern auseinander. Wenn das Absicht ist (Intervall-Betrieb), stell die Schonfrist höher — die Luftpumpe bleibt davon unberührt.";

    /// <summary>Meldet dieser Zustand „aus"?</summary>
    /// <remarks>
    /// Oeffentlich, weil der Anlagen-Waechter (Kuehler, USV) dieselbe Frage
    /// stellt. Zwei Fassungen davon waeren zwei Wahrheiten ueber denselben
    /// Zustandstext.
    /// </remarks>
    public static bool IstAus(string state)
        => state.Equals("off", StringComparison.OrdinalIgnoreCase)
        || state.Equals("unavailable", StringComparison.OrdinalIgnoreCase)
        || state.Equals("closed", StringComparison.OrdinalIgnoreCase)
        || state == "0";
}
