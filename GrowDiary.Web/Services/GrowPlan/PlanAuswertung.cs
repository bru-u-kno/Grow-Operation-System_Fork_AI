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
    IReadOnlyList<FeedChartItem> DosierungEnde);

/// <summary>
/// Fork AI (Grow-Plan, Schritt 6): die Auswertung eines Grows je Woche —
/// was geplant war (Start), was am Ende galt, was gemessen wurde.
/// </summary>
/// <remarks>
/// <para><b>Zeiträume.</b> Alle Beginne aus dem <see cref="Phasenanker"/>:
/// Anzucht bis zum bestätigten Vegi-Beginn, Vegi-Wochen ab dort, Blütewochen ab
/// dem Blütebeginn, Flush nach der letzten Blütewoche bis zum Ende. Ohne
/// bestätigten Beginn hat eine Phase keinen Zeitraum — dann steht dort nichts
/// Gemessenes.</para>
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
            var anfang = start?.Chart.Columns.FirstOrDefault(c => string.Equals(c.Id, spalte.Id, StringComparison.OrdinalIgnoreCase));
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
                anfang?.Items ?? [], spalte.Items);
        }).ToList();
    }

    private static Dictionary<string, double?> Werte(FeedChartColumn? spalte)
        => Wochenwertfelder.Alle.ToDictionary(f => f.Name, f => spalte is null ? null : f.Lesen(spalte));

    /// <summary>Von (einschließlich) und bis (ausschließlich) je Woche.</summary>
    public static Dictionary<string, (DateTime? Von, DateTime? Bis)> Zeitraeume(
        GrowRun grow, IReadOnlyList<FeedChartColumn> spalten, DateTime heute)
    {
        var ergebnis = new Dictionary<string, (DateTime?, DateTime?)>(StringComparer.OrdinalIgnoreCase);
        // Alle Beginne aus dem Phasenanker — bis zum 02.10.2026 nahm diese
        // Stelle „Vegi-Beginn, sonst Bewurzelung, sonst Start + 7" und lag
        // damit neben Mischplan und Phase.
        var anker = Phasenanker.Fuer(grow, heute);
        var ende = (grow.EndDate?.Date ?? heute.Date).AddDays(1);
        var anzuchtBis = anker.VegAb ?? anker.BlueteAb ?? ende;
        var vegiBeginn = anker.VegAb;
        var bluetenBeginn = anker.BlueteAb;
        // F-019: die letzte Vegi-Woche gilt bis zum Blütebeginn — wie im Mischplan wird sie gehalten.
        var letzteVegiWoche = spalten
            .Where(c => c.Stage.Equals("Veg", StringComparison.OrdinalIgnoreCase) && c.Week is not null)
            .Select(c => c.Week!.Value)
            .DefaultIfEmpty(0)
            .Max();
        DateTime? letzteBluete = null;

        foreach (var s in spalten)
        {
            (DateTime?, DateTime?) zeitraum = s.Stage.ToLowerInvariant() switch
            {
                // F-019: begann die Vegi am Starttag (bewurzelter Steckling), gab es keine Anzucht.
                "clone" or "seedling" => anzuchtBis > anker.AnzuchtAb ? (anker.AnzuchtAb, anzuchtBis) : (null, null),
                "veg" when s.Week is { } w && vegiBeginn is { } vegi => (vegi.AddDays(7 * (w - 1)),
                    w == letzteVegiWoche && bluetenBeginn is { } flipVegi && flipVegi > vegi.AddDays(7 * w)
                        ? flipVegi
                        : vegi.AddDays(7 * w)),
                "flower" or "transition" when s.Week is { } w && bluetenBeginn is { } flip
                    => (flip.AddDays(7 * (w - 1)), flip.AddDays(7 * w)),
                _ => (null, null),
            };
            if (s.Stage.Equals("Flower", StringComparison.OrdinalIgnoreCase) && zeitraum.Item2 is { } bis)
            {
                letzteBluete = letzteBluete is { } l && l > bis ? l : bis;
            }
            ergebnis[s.Id] = zeitraum;
        }

        foreach (var s in spalten.Where(s => s.Stage.Equals("Finish", StringComparison.OrdinalIgnoreCase)))
        {
            ergebnis[s.Id] = letzteBluete is { } l && ende > l ? (l, ende) : (null, null);
        }

        return ergebnis;
    }
}
