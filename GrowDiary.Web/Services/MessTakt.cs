using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Wie frisch die Prüfung hinter einer Abweichungs-Karte ist: wann die neueste
/// Messung des Grows war, in welchem Abstand sonst gemessen wird, wann die
/// nächste zu erwarten ist und ob die Prüfung überfällig ist.
/// </summary>
/// <param name="NeuesteMessungUtc">Zeit der neuesten Messung des Grows (von Hand oder automatisch).</param>
/// <param name="UeblicherAbstand">Mittlerer Abstand der letzten automatischen Messungen; <c>null</c>, wenn es zu wenige gibt.</param>
/// <param name="NaechsteErwartetUtc">Neueste Messung plus üblicher Abstand — nur, solange das noch in der Zukunft liegt.</param>
/// <param name="Veraltet">Die neueste Messung ist älter als 1,5 × der übliche Abstand.</param>
public sealed record MessStand(
    DateTime NeuesteMessungUtc,
    TimeSpan? UeblicherAbstand,
    DateTime? NaechsteErwartetUtc,
    bool Veraltet);

/// <summary>
/// Der Takt, in dem ein Grow gemessen wird — aus den echten Abständen, nicht aus
/// einer festen Zahl.
/// </summary>
/// <remarks>
/// <para><b>Warum (16.10.2026).</b> Die Diagnose liest nur die Messungen, nicht die
/// 5-Minuten-Werte der Sensoren. Bei einem Grow, der zweimal am Tag automatisch
/// misst (30 Minuten nach Licht an und aus), ist eine „Besteht noch"-Karte bis zu
/// zwölf Stunden alt, und fällt ein Sensor aus, bleibt sie ohne jeden Hinweis
/// stehen. Wer einmal am Tag oder in anderem Rhythmus misst, braucht eine andere
/// Grenze — deshalb folgt sie dem eigenen Takt.</para>
///
/// <para><b>Nur automatische Messungen geben den Takt.</b> Eine Messung von Hand
/// kommt, wann der Nutzer Zeit hat, und würde den Abstand verzerren. Als
/// „neueste Messung" zählt trotzdem jede, denn die Diagnose liest auch jede.</para>
///
/// <para>Ohne genug automatische Messungen (<see cref="MindestMessungen"/>) gibt es
/// keinen Takt und damit keine Aussage „überfällig" — nur den Zeitpunkt der
/// neuesten Messung. Lieber nichts behaupten als einen Takt raten.</para>
/// </remarks>
public static class MessTakt
{
    /// <summary>Mindestens so viele automatische Messungen, damit ein Takt gilt (also mindestens drei Abstände).</summary>
    public const int MindestMessungen = 4;

    /// <summary>Wie viele der jüngsten automatischen Messungen den Takt bestimmen.</summary>
    public const int Rueckblick = 8;

    /// <summary>Überfällig ist eine Prüfung ab dem Eineinhalbfachen des üblichen Abstands.</summary>
    public const double Toleranz = 1.5;

    public static MessStand? Bestimmen(IReadOnlyList<Measurement> messungen, DateTime jetztUtc)
    {
        // Messungen aus der Zukunft raus, wie in der Diagnose (eine Stunde Luft).
        var spaetestens = jetztUtc.AddHours(1);
        var gueltig = messungen
            .Select(messung => (Messung: messung, Zeit: ZuUtc(messung.TakenAt)))
            .Where(eintrag => eintrag.Zeit <= spaetestens)
            .ToList();
        if (gueltig.Count == 0)
        {
            return null;
        }

        var neueste = gueltig.Max(eintrag => eintrag.Zeit);

        var automatisch = gueltig
            .Where(eintrag => eintrag.Messung.Source == ValueOrigin.HomeAssistant)
            .Select(eintrag => eintrag.Zeit)
            .Distinct()
            .OrderByDescending(zeit => zeit)
            .Take(Rueckblick)
            .ToList();

        TimeSpan? abstand = null;
        if (automatisch.Count >= MindestMessungen)
        {
            // Der Median, nicht der Mittelwert: ein einzelner Ausfall von zwei
            // Tagen soll den üblichen Abstand nicht auf einen halben Tag hochziehen.
            var abstaende = automatisch
                .Zip(automatisch.Skip(1), (juenger, aelter) => juenger - aelter)
                .OrderBy(dauer => dauer)
                .ToList();
            var mitte = abstaende.Count / 2;
            abstand = abstaende.Count % 2 == 1
                ? abstaende[mitte]
                : TimeSpan.FromTicks((abstaende[mitte - 1].Ticks + abstaende[mitte].Ticks) / 2);
        }

        // Die nächste AUTOMATISCHE Messung folgt dem Rhythmus der automatischen,
        // nicht der letzten von Hand — sonst verschöbe jede Handmessung die Erwartung.
        var erwartet = abstand is { } a && automatisch[0] + a > jetztUtc ? automatisch[0] + a : (DateTime?)null;
        var veraltet = abstand is { } b && jetztUtc - neueste > TimeSpan.FromTicks((long)(b.Ticks * Toleranz));
        return new MessStand(neueste, abstand, erwartet, veraltet);
    }

    private static DateTime ZuUtc(DateTime zeit)
        => zeit.Kind == DateTimeKind.Utc ? zeit : zeit.ToUniversalTime();
}
