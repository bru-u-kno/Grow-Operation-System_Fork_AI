using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>Ein Stück Verbrauch eines Grows: ein Ortstag (oder ein Teil davon) zwischen zwei Ständen.</summary>
/// <param name="Tag">Der Ortstag, auf den das Stück fällt.</param>
/// <param name="Kwh">Der Anteil DIESES Grows — bei geteiltem Zähler schon geteilt.</param>
/// <param name="GeteiltDurch">Wie viele Grows an diesem Tag am selben Zähler liefen (1 = allein).</param>
/// <param name="MitGrows">Die anderen Grows dieses Tages am selben Zähler.</param>
public sealed record StromStueck(
    DateTime Tag, DateTime VonUtc, DateTime BisUtc, double Kwh, int GeteiltDurch, IReadOnlyList<int> MitGrows)
{
    public double Tage => Math.Max((BisUtc - VonUtc).TotalDays, 0);
}

/// <summary>Was ein Grow vom Verbrauch seines Zählers trägt.</summary>
/// <param name="Zaehler">Die HA-Entität, an der der Grow misst; null ohne Quelle.</param>
/// <param name="Erster">Der Stand, an dem das erste Stück beginnt.</param>
/// <param name="Letzter">Der Stand, an dem das letzte Stück endet.</param>
/// <param name="StaendeImFenster">Stände des Zählers innerhalb der Laufzeit — für „erst ein Stand".</param>
public sealed record StromAnteil(
    int GrowId,
    string? Zaehler,
    IReadOnlyList<StromStueck> Stuecke,
    Zaehlerstand? Erster,
    Zaehlerstand? Letzter,
    IReadOnlyList<Zaehlerstand> StaendeImFenster)
{
    public double Kwh => Stuecke.Sum(s => s.Kwh);
}

/// <summary>
/// Verteilt den Verbrauch jedes Zählers auf die Grows, die an ihm laufen.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (02.10.2026).</b> Bis hierher trug jeder Zählerstand die
/// Id „des" laufenden Grows — des ältesten. Ein zweiter, gleichzeitig laufender
/// Grow bekam deshalb nie einen Stand, zeigte dauerhaft „Noch kein Zählerstand",
/// und der ganze Verbrauch landete beim ersten. Wer zwei Zelte hat, sah auf der
/// Kostenseite eine Zahl, die für keines stimmte.</para>
///
/// <para><b>Das Rechenmodell.</b></para>
/// <list type="number">
/// <item>Ein Stand gehört zu einem <b>Zähler</b> (<see cref="StromQuelle.ZaehlerVonStand"/>),
/// nicht zu einem Grow.</item>
/// <item>Ein Grow misst am Zähler seines Zelts, sonst am gemeinsamen
/// (<see cref="StromQuelle.ZaehlerFuerZelt"/>).</item>
/// <item>Der Verbrauch zwischen zwei Ständen wird nach der Zeit auf die Ortstage
/// gelegt, die er überspannt. Bei Ständen um Mitternacht ist das genau ein Tag;
/// eine Lücke von drei Tagen verteilt sich gleichmäßig auf drei. Das ist die
/// lineare Lesart des Zählers zwischen zwei Ablesungen — mehr weiß niemand.</item>
/// <item>Laufen an einem Tag mehrere Grows am selben Zähler, bekommt jeder
/// denselben Teil. Keine Gewichtung nach Lampen-Watt: die stünde im Zelt, nicht
/// am Zähler, und wäre eine erfundene Genauigkeit.</item>
/// <item>Welcher Grow an welchem Tag läuft, sagen Start, Ende und Status des
/// Grows (<see cref="Laufzeit"/>) — keine zweite Liste.</item>
/// </list>
///
/// <para>Ein Tag ohne laufenden Grow (zwischen zwei Durchgängen) gehört niemandem.
/// Für einen Nutzer mit einem Zelt und einem Grow kommt dieselbe Summe heraus
/// wie vorher: alle Stücke gehören ihm.</para>
/// </remarks>
public static class StromAufteilung
{
    /// <summary>
    /// Die Ortstage, an denen ein Grow läuft — einschließlich beider Enden.
    /// Null, wenn er (noch) nicht läuft.
    /// </summary>
    /// <remarks>
    /// Geplant zählt nicht: ein Grow in Planung zieht keinen Strom, und ihm einen
    /// Anteil zu geben hieße, ihn dem laufenden Grow wegzunehmen. Abgeschlossen
    /// ohne Enddatum gibt es keine Grenze, also auch keinen Anteil — besser kein
    /// Strom als einer, der bis heute weiterläuft und anderen etwas abzieht.
    /// </remarks>
    public static (DateTime Von, DateTime Bis)? Laufzeit(GrowRun grow, DateTime heute)
    {
        if (grow.Status == GrowStatus.Planning) return null;
        var von = grow.StartDate.Date;
        DateTime? bis = grow.EndDate?.Date ?? (grow.Status == GrowStatus.Running ? heute.Date : null);
        if (bis is null || bis.Value < von) return null;
        return (von, bis.Value);
    }

    /// <summary>Beginn eines Ortstags in UTC — mit Sommerzeit, darum nicht einfach ±24 h.</summary>
    public static DateTime TagesbeginnUtc(DateTime ortsdatum)
        => DateTime.SpecifyKind(ortsdatum.Date, DateTimeKind.Local).ToUniversalTime();

    /// <summary>kWh zwischen zwei Ständen — ein Sprung nach unten gilt als Zähler-Reset (Neustart bei null).</summary>
    public static double Differenz(Zaehlerstand vorher, Zaehlerstand nachher)
    {
        var delta = nachher.Kwh - vorher.Kwh;
        return delta >= 0 ? delta : nachher.Kwh;
    }

    /// <summary>Den Verbrauch aller Zähler auf alle Grows verteilen.</summary>
    /// <returns>Je Grow sein Anteil — auch für Grows ohne Stück (leere Liste).</returns>
    public static IReadOnlyDictionary<int, StromAnteil> Aufteilen(
        IReadOnlyList<GrowRun> grows, StromQuelle quelle, IReadOnlyList<Zaehlerstand> staende, DateTime jetztUtc)
    {
        var heute = jetztUtc.ToLocalTime().Date;
        var vergleich = StringComparer.OrdinalIgnoreCase;

        var laufzeiten = grows
            .Select(g => (Grow: g, Zaehler: quelle.ZaehlerFuerZelt(g.TentId), Laufzeit: Laufzeit(g, heute)))
            .ToList();

        var stuecke = grows.ToDictionary(g => g.Id, _ => new List<StromStueck>());
        var erster = new Dictionary<int, Zaehlerstand>();
        var letzter = new Dictionary<int, Zaehlerstand>();

        var reihen = staende
            .Select(s => (Stand: s, Zaehler: quelle.ZaehlerVonStand(s)))
            .Where(x => x.Zaehler is not null)
            .GroupBy(x => x.Zaehler!, vergleich)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Stand).OrderBy(s => s.ZeitpunktUtc).ThenBy(s => s.Id).ToList(), vergleich);

        foreach (var (zaehler, reihe) in reihen)
        {
            var amZaehler = laufzeiten
                .Where(l => l.Laufzeit is not null && l.Zaehler is not null && vergleich.Equals(l.Zaehler, zaehler))
                .ToList();
            if (amZaehler.Count == 0) continue;

            for (var i = 1; i < reihe.Count; i++)
            {
                var a = reihe[i - 1];
                var b = reihe[i];
                var kwh = Differenz(a, b);

                foreach (var (tag, vonUtc, bisUtc, anteil) in NachTagen(a.ZeitpunktUtc, b.ZeitpunktUtc))
                {
                    var aktiv = amZaehler
                        .Where(l => tag >= l.Laufzeit!.Value.Von && tag <= l.Laufzeit.Value.Bis)
                        .Select(l => l.Grow.Id)
                        .ToList();
                    if (aktiv.Count == 0) continue;

                    foreach (var id in aktiv)
                    {
                        stuecke[id].Add(new StromStueck(
                            tag, vonUtc, bisUtc, kwh * anteil / aktiv.Count, aktiv.Count,
                            aktiv.Where(x => x != id).ToList()));
                        erster.TryAdd(id, a);
                        letzter[id] = b;
                    }
                }
            }
        }

        return laufzeiten.ToDictionary(
            l => l.Grow.Id,
            l =>
            {
                IReadOnlyList<Zaehlerstand> imFenster = [];
                if (l.Laufzeit is { } lz && l.Zaehler is { } z && reihen.TryGetValue(z, out var reihe))
                {
                    var von = TagesbeginnUtc(lz.Von);
                    var bis = TagesbeginnUtc(lz.Bis.AddDays(1));
                    imFenster = reihe.Where(s => s.ZeitpunktUtc >= von && s.ZeitpunktUtc <= bis).ToList();
                }

                return new StromAnteil(
                    l.Grow.Id, l.Zaehler, stuecke[l.Grow.Id],
                    erster.GetValueOrDefault(l.Grow.Id), letzter.GetValueOrDefault(l.Grow.Id), imFenster);
            });
    }

    /// <summary>
    /// Zerlegt die Spanne zwischen zwei Ständen in Ortstage, jeder mit seinem
    /// Zeitanteil. Zwei Stände im selben Augenblick ergeben ein Stück mit dem
    /// ganzen Betrag (meist 0 kWh).
    /// </summary>
    public static IEnumerable<(DateTime Tag, DateTime VonUtc, DateTime BisUtc, double Anteil)> NachTagen(DateTime vonUtc, DateTime bisUtc)
    {
        var dauer = (bisUtc - vonUtc).TotalSeconds;
        if (dauer <= 0)
        {
            yield return (bisUtc.ToLocalTime().Date, vonUtc, bisUtc, 1);
            yield break;
        }

        var tag = vonUtc.ToLocalTime().Date;
        var beginn = vonUtc;
        while (beginn < bisUtc)
        {
            var tagesende = TagesbeginnUtc(tag.AddDays(1));
            var ende = tagesende < bisUtc ? tagesende : bisUtc;
            if (ende > beginn)
            {
                yield return (tag, beginn, ende, (ende - beginn).TotalSeconds / dauer);
            }

            beginn = ende;
            tag = tag.AddDays(1);
        }
    }
}
