using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Woher die Grenzen einer Alarmregel kommen.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (10.09.2026).</b> Die Alarme lasen bis dahin
/// ausschliesslich die von Hand eingetragenen Zahlen. Ein Lauf nach Wochenplan
/// bewegt sein Ziel aber jede Woche: das SKX-Chart nennt in Bluete W3 EC 1,2
/// und in W6 EC 1,6. Eine feste Obergrenze von 1,2 haette ab W4 jede Nacht
/// gemeldet, obwohl planmaessig gefuettert wurde — und wer wochenlang falschen
/// Alarm bekommt, glaubt auch dem echten nicht mehr.</para>
///
/// <para><b>Fest</b> ist und bleibt der Standard: eingetragene Zahlen gelten
/// unveraendert weiter. <b>Plan</b> heisst, dass die Regel dieselbe Kette liest
/// wie der Rest der App (<see cref="Zielband"/>) und ihre Grenzen daraus
/// ableitet — Zielband minus/plus <see cref="TentAlertRule.Toleranz"/>.</para>
/// </remarks>
public enum Grenzwertquelle
{
    /// <summary>Die eingetragenen Zahlen des Nutzers.</summary>
    Fest = 0,

    /// <summary>Zielband der laufenden Phase/Woche, erweitert um die Toleranz.</summary>
    Plan = 1,
}

/// <summary>
/// Rechnet die Grenzen einer Plan-Regel aus dem Zielband aus.
/// </summary>
public static class Planzielgrenzen
{
    /// <summary>Was eingesetzt wird, wenn keine Toleranz am Zettel steht.</summary>
    /// <remarks>
    /// Ohne Toleranz waere das Band bei einem Wochen-pH genau null breit — das
    /// SKX-Chart nennt je Woche EINEN pH-Wert (W3: 6,0), phMin und phMax sind
    /// dann gleich. Jede Messung waere eine Ueberschreitung. Der Standard ist
    /// bewusst grosszuegig; die Zeile darf ihn ueberschreiben.
    /// </remarks>
    public static double StandardToleranz(string metricKey) => metricKey switch
    {
        "reservoir-ph" => 0.2,
        "reservoir-ec" => 0.2,
        "reservoir-temp" => 2,
        "orp" => 50,
        "vpd" => 0.2,
        "co2" => 200,
        "ppfd" => 100,
        _ => 0,
    };

    /// <summary>
    /// Die Messgroessen, fuer die der Plan ueberhaupt etwas hergibt.
    /// </summary>
    /// <remarks>
    /// Luftfeuchte, Sauerstoff und Wasserstand stehen in keinem Sollwertprofil
    /// und in keinem Feed-Chart. Sie duerfen die Quelle „Plan" gar nicht erst
    /// angeboten bekommen — sonst waehlt sie jemand und die Regel schweigt
    /// stumm, ohne dass irgendwo stuende warum.
    /// </remarks>
    public static bool KenntPlanziel(string metricKey) => metricKey switch
    {
        "reservoir-ph" or "reservoir-ec" or "reservoir-temp"
            or "orp" or "vpd" or "co2" or "ppfd" => true,
        _ => false,
    };

    /// <summary>
    /// Die Regel, gegen die wirklich gemessen wird.
    /// </summary>
    /// <remarks>
    /// <para>Gibt bei <see cref="Grenzwertquelle.Fest"/> die Regel unveraendert
    /// zurueck. Bei <see cref="Grenzwertquelle.Plan"/> entsteht eine Kopie mit
    /// den abgeleiteten Grenzen; ohne Zielband (kein aktiver Grow, keine Phase,
    /// Messgroesse ohne Planziel) kommt <c>null</c> — die Regel schweigt dann,
    /// statt gegen leere Grenzen zu melden.</para>
    ///
    /// <para>Das Band kommt aus <see cref="Zielband.FuerMetrik"/> und damit aus
    /// derselben Lesart, die die Live-Kachel zeigt: Handlungsbereich beim pH,
    /// Arbeitsbereich bei der Wassertemperatur. Sonst stuende auf dem Handy eine
    /// vierte Zahl fuer denselben Messwert.</para>
    /// </remarks>
    public static TentAlertRule? Wirksam(
        TentAlertRule regel, HydroTargetValues? band, double? rampenBodenC)
    {
        if (regel.Quelle != Grenzwertquelle.Plan)
        {
            return regel;
        }

        if (band is null || !KenntPlanziel(regel.MetricKey))
        {
            return null;
        }

        var (min, max) = Zielband.FuerMetrik(regel.MetricKey, band, rampenBodenC);
        if (min is null && max is null)
        {
            return null;
        }

        var toleranz = regel.Toleranz is { } eigene && eigene > 0
            ? eigene
            : StandardToleranz(regel.MetricKey);

        return new TentAlertRule
        {
            Id = regel.Id,
            TentId = regel.TentId,
            MetricKey = regel.MetricKey,
            MinValue = min is { } m ? m - toleranz : null,
            MaxValue = max is { } x ? x + toleranz : null,
            NotifyService = regel.NotifyService,
            Enabled = regel.Enabled,
            CooldownMinutes = regel.CooldownMinutes,
            Quelle = regel.Quelle,
            Toleranz = toleranz,
            LastState = regel.LastState,
            LastNotifiedUtc = regel.LastNotifiedUtc,
        };
    }
}
