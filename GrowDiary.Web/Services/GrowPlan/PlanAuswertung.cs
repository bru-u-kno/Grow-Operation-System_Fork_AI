using GrowDiary.Web.Models;
using GrowDiary.Web.Services.Knowledge.Schema;

namespace GrowDiary.Web.Services.GrowPlan;

/// <summary>Eine Woche der Auswertung: Startstand, Endstand, gemessen.</summary>
public sealed record PlanAuswertungWoche(
    string Id,
    string Label,
    string Stage,
    int? Week,
    DateTime? Von,
    DateTime? Bis,
    Dictionary<string, double?> Start,
    Dictionary<string, double?> Ende,
    Dictionary<string, double?> Gemessen,
    int Messungen,
    IReadOnlyList<FeedChartItem> DosierungStart,
    IReadOnlyList<FeedChartItem> DosierungEnde,
    /// <summary>Fork AI (02.10.2026): angehängte Woche — die Phase lief länger als der Plan.</summary>
    bool Verlaengert = false);

/// <summary>
/// Fork AI (Grow-Plan, Schritt 6): die Auswertung eines Grows je Woche —
/// was geplant war (Start), was am Ende galt, was gemessen wurde.
/// </summary>
/// <remarks>
/// <para><b>Zeiträume.</b> Alle Beginne aus dem <see cref="Phasenanker"/>:
/// Anzucht-Wochen ab dem Anzuchtbeginn, Vegi-Wochen ab dem bestätigten
/// Vegi-Beginn, Blütewochen ab dem Blütebeginn, Flush ab dem Finish (sonst nach
/// der letzten Blütewoche) bis zum Ende. Jede Woche endet spätestens mit dem
/// Beginn der nächsten Phase. Ohne bestätigten Beginn hat eine Phase keinen
/// Zeitraum — dann steht dort nichts Gemessenes.</para>
/// <para><b>Welche Spalte welche Woche ist</b>, sagt
/// <see cref="Planwochen.Wochen"/> — dieselbe Stelle, an der der Mischplan die
/// laufende Spalte wählt. Ein Tag gehört damit hier zu genau der Woche, die
/// Kachel, Grenzwerte und Übergabe an diesem Tag zeigen.</para>
/// <para><b>Gemessen</b> sind die Mittelwerte der gespeicherten Messungen
/// (Hand und Auto). CO₂ und VPD stehen dort nicht — sie fehlen bewusst.</para>
/// </remarks>
public static class PlanAuswertung
{
    /// <summary>Messgröße → Leser aus einer Messung.</summary>
    public static readonly IReadOnlyDictionary<string, (Func<Measurement, double?> Lesen, string Physik)> Messgroessen =
        new Dictionary<string, (Func<Measurement, double?>, string)>
        {
            ["ec"] = (m => m.ReservoirEc, "ec"),
            ["ph"] = (m => m.ReservoirPh, "ph"),
            ["wasser"] = (m => m.ReservoirWaterTempC, "water-temp"),
            ["rh"] = (m => m.HumidityPercent, "humidity"),
            ["luft"] = (m => m.AirTemperatureC, "air-temp"),
            ["orp"] = (m => m.OrpMv, "orp"),
        };

    public static List<PlanAuswertungWoche> Bauen(
        GrowRun grow,
        GrowPlanInhalt? start,
        GrowPlanInhalt ende,
        IReadOnlyList<Measurement> messungen,
        DateTime heute)
    {
        var spalten = ende.Chart.Columns;
        var zeitraeume = Zeitraeume(grow, spalten, heute);
        return spalten.Select(spalte =>
        {
            // Eine angehängte Woche hat im Startstand keine Spalte: geplant war für
            // sie, was ihre Programmwoche vorsah.
            var programmwoche = ende.Programmwoche(spalte.Id);
            var anfang = start?.Chart.Columns.FirstOrDefault(c => string.Equals(c.Id, programmwoche, StringComparison.OrdinalIgnoreCase));
            var (von, bis) = zeitraeume[spalte.Id];
            var inWoche = von is { } a && bis is { } b
                ? messungen.Where(m => m.TakenAt >= a && m.TakenAt < b).ToList()
                : [];
            // Sondenaussetzer (EC 99999 …) zählen nicht — dieselbe Grenze wie beim Erfassen.
            var gemessen = Messgroessen.ToDictionary(
                g => g.Key,
                g => inWoche.Select(g.Value.Lesen).OfType<double>()
                        .Where(w => MeasurementSanityService.IstPhysikalischMoeglich(g.Value.Physik, w))
                        .ToList() is { Count: > 0 } werte
                    ? Math.Round(werte.Average(), 2)
                    : (double?)null);
            return new PlanAuswertungWoche(
                spalte.Id, spalte.Label, spalte.Stage, spalte.Week, von, bis,
                Werte(anfang), Werte(spalte), gemessen, inWoche.Count,
                anfang?.Items ?? [], spalte.Items, ende.IstVerlaengert(spalte.Id));
        }).ToList();
    }

    private static Dictionary<string, double?> Werte(FeedChartColumn? spalte)
        => Wochenwertfelder.Alle.ToDictionary(f => f.Name, f => spalte is null ? null : f.Lesen(spalte));

    /// <summary>Von (einschließlich) und bis (ausschließlich) je Woche.</summary>
    /// <remarks>
    /// <para><b>Angehängte Wochen</b> (Fork AI, 02.10.2026) haben ihren eigenen
    /// Zeitraum wie jede Woche: Vegiwoche 5 läuft von Tag 29 bis 35 der Vegi.
    /// Bis dahin wurde die letzte Vegi-Woche bis zum Flip gestreckt (F-019).</para>
    /// <para><b>Was davon bleibt.</b> Ein eingefrorener Plan aus der Zeit davor
    /// hat die Wochen nicht und bekommt sie nie (<see cref="GrowPlanService.WochenNachziehen"/>).
    /// Damit seine Messungen nicht ins Leere fallen, reicht dort die letzte Woche
    /// einer Phase bis zum Beginn der nächsten — dieselbe Spalte, die der
    /// Mischplan in dieser Zeit gehalten hat. Bei einem nachgezogenen Plan gibt
    /// es diese Lücke nicht, die Regel greift dort nicht.</para>
    /// <para><b>Begrenzt auf die Phase.</b> Keine Woche reicht über den Beginn der
    /// nächsten Phase hinaus; eine Woche, die erst danach begänne (vier
    /// Vegi-Wochen geplant, nach drei geflippt), hat keinen Zeitraum. Vorher lag
    /// sie über den ersten Blütetagen, und ein Tag gehörte zu zwei Wochen.</para>
    /// </remarks>
    public static Dictionary<string, (DateTime? Von, DateTime? Bis)> Zeitraeume(
        GrowRun grow, IReadOnlyList<FeedChartColumn> spalten, DateTime heute)
    {
        var ergebnis = new Dictionary<string, (DateTime?, DateTime?)>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in spalten) ergebnis[s.Id] = (null, null);

        // Alle Beginne aus dem Phasenanker — bis zum 02.10.2026 nahm diese
        // Stelle „Vegi-Beginn, sonst Bewurzelung, sonst Start + 7" und lag
        // damit neben Mischplan und Phase.
        var anker = Phasenanker.Fuer(grow, heute);
        var ende = (grow.EndDate?.Date ?? heute.Date).AddDays(1);

        // Phase, ihr Beginn, der Beginn der nächsten Phase. Die laufende Anzucht
        // reicht bis heute — wie bisher.
        var phasen = new (string Phase, DateTime? Von, DateTime? Naechste)[]
        {
            ("Clone", anker.AnzuchtAb, anker.VegAb ?? anker.BlueteAb ?? ende),
            ("Veg", anker.VegAb, anker.BlueteAb),
            ("Flower", anker.BlueteAb, anker.FinishAb),
            // Kein Programm führt sie, keine Woche wählt sie — wie bisher wie Blütewochen.
            ("Transition", anker.BlueteAb, anker.FinishAb),
        };

        DateTime? letzteBluete = null;
        foreach (var (phase, von, naechste) in phasen)
        {
            if (von is not { } beginn) continue;
            var wochen = Planwochen.Wochen(spalten, phase);
            for (var i = 0; i < wochen.Count; i++)
            {
                var (spalte, woche) = wochen[i];
                var a = beginn.AddDays(7 * (woche - 1));
                var b = beginn.AddDays(7 * woche);
                if (naechste is { } n)
                {
                    if (a >= n) continue;
                    if (b > n || i == wochen.Count - 1) b = n;
                }
                ergebnis[spalte.Id] = (a, b);
                if (phase == "Flower") letzteBluete = letzteBluete is { } l && l > b ? l : b;
            }
        }

        var finishVon = anker.FinishAb ?? letzteBluete;
        foreach (var s in spalten.Where(s => s.Stage.Equals("Finish", StringComparison.OrdinalIgnoreCase)))
        {
            ergebnis[s.Id] = finishVon is { } f && ende > f ? (f, ende) : (null, null);
        }

        return ergebnis;
    }
}
