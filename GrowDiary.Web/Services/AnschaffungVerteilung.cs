using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Wie sich eine verteilte Anschaffung auf die Grows aufteilt — Stand heute.
/// </summary>
/// <param name="AnschaffungId">Die Anschaffung.</param>
/// <param name="VonTag">Erster Tag der Nutzung (Ortszeit), das Kaufdatum.</param>
/// <param name="BisTag">Erster Tag NACH der geplanten Nutzungsdauer (Ortszeit).</param>
/// <param name="AusgemustertTag">Tag der vorzeitigen Ausmusterung, falls einer gilt.</param>
/// <param name="EurProTag">Gesamtpreis geteilt durch die Tage der Nutzungsdauer.</param>
/// <param name="JeGrow">Was jeder Grow bisher trägt — Tagesanteile plus ggf. Rest bei Ausmusterung.</param>
/// <param name="LeerlaufEur">Tagesanteile ohne laufenden Grow; die trägt niemand.</param>
/// <param name="RestwertEur">Bei Ausmusterung noch nicht verteilter Rest (auf Grows oder in den Leerlauf).</param>
/// <param name="OffenEur">Noch nicht verteilt, weil die Tage erst kommen.</param>
public sealed record AnschaffungAnteile(
    int AnschaffungId,
    DateTime VonTag,
    DateTime BisTag,
    DateTime? AusgemustertTag,
    double EurProTag,
    IReadOnlyDictionary<int, double> JeGrow,
    double LeerlaufEur,
    double RestwertEur,
    double OffenEur)
{
    public double VerteiltEur => JeGrow.Values.Sum();
}

/// <summary>
/// forkai.157: Verteilt den Preis einer Anschaffung über ihre Nutzungsdauer
/// auf die Grows, die in dieser Zeit laufen.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (01.10.2026).</b> Eine Anschaffung zählte bisher ganz in
/// EINEM Grow oder in keinem („Lager"). Ein Gerät, das ab heute jahrelang
/// läuft, belastete damit den einen Durchgang mit dem vollen Preis — und alle
/// folgenden mit nichts. Gewünscht war: jeder künftige Grow trägt einen Teil.</para>
///
/// <para><b>Über die Zeit, nicht über die Zahl der Grows.</b> Wie viele Grows
/// noch kommen, weiß niemand. Teilte man durch ihre Zahl, würde jeder neue
/// Grow die Kosten der alten nachträglich senken — die Euro je Gramm vom
/// letzten Jahr stünden nie fest. Deshalb lineare Abschreibung: Preis durch
/// Tage der Nutzungsdauer ergibt einen Tagesanteil, und jeder Tag geht an die
/// Grows, die an ihm laufen. Ein abgeschlossener Grow ändert sich dadurch nie
/// mehr.</para>
///
/// <para><b>Drei Regeln, mit dem Nutzer abgestimmt:</b></para>
/// <list type="bullet">
/// <item>Laufen an einem Tag mehrere Grows, teilen sie sich den Anteil — sonst
/// wäre das Gerät doppelt bezahlt. Mit Zelt zählen nur Grows in diesem Zelt.</item>
/// <item>Läuft an einem Tag kein Grow, verfällt der Anteil als <i>Leerlauf</i>.
/// Das ist ehrlich: die Pause hat das Gerät auch gealtert.</item>
/// <item>Wird das Gerät vorzeitig ausgemustert, fällt der noch nicht verteilte
/// Rest auf die Grows, die an diesem Tag laufen — wie eine außerplanmäßige
/// Abschreibung. Läuft keiner, ist auch der Rest Leerlauf.</item>
/// </list>
///
/// <para>Die Tage sind Ortstage, wie Start- und Enddatum eines Grows. Ein Grow
/// läuft von seinem Start bis zu seinem Ende einschließlich; ein laufender bis
/// heute. Ein geplanter läuft noch nicht. Ein abgeschlossener ohne Enddatum
/// (Altbestand) bekommt nichts, weil niemand weiß, wie lange er lief.</para>
/// </remarks>
public static class AnschaffungVerteilung
{
    /// <summary>Höchste Nutzungsdauer in Monaten: 50 Jahre. Darüber ist es ein Tippfehler.</summary>
    public const int MaxMonate = 600;

    /// <summary>Wird diese Anschaffung verteilt statt einmalig gezählt?</summary>
    public static bool IstVerteilt(Anschaffung a) => a.NutzungsdauerMonate is > 0;

    public static AnschaffungAnteile Berechnen(Anschaffung a, IReadOnlyList<GrowRun> grows, DateTime heute)
    {
        if (a.NutzungsdauerMonate is not { } monate || monate <= 0)
        {
            throw new ArgumentException("Nur verteilte Anschaffungen haben Anteile.", nameof(a));
        }

        heute = Tag(heute);
        var von = Tag(a.DatumUtc.ToLocalTime());
        var bis = von.AddMonths(monate);
        var tageGesamt = (bis - von).Days;
        var proTag = a.GesamtEur / tageGesamt;

        // Eine Ausmusterung vor dem Kauf ist eine am Kauftag; eine nach dem
        // Ende der Nutzungsdauer ändert nichts mehr.
        DateTime? aus = a.AusgemustertAmUtc is { } ausUtc ? Tag(ausUtc.ToLocalTime()) : null;
        if (aus is { } ausTag && ausTag < von) aus = von;
        if (aus is { } ausTag2 && ausTag2 >= bis) aus = null;

        var zeitraeume = grows
            .Where(g => a.TentId is null || g.TentId == a.TentId)
            .Select(g => (g.Id, Von: Tag(g.StartDate), Bis: GrowBis(g, heute)))
            .Where(z => z.Bis is not null)
            .Select(z => (z.Id, z.Von, Bis: z.Bis!.Value))
            .ToList();

        var jeGrow = new Dictionary<int, double>();
        double leerlauf = 0;
        double restwert = 0;

        var regulaerBis = aus ?? bis;
        var gerechnetBis = regulaerBis < heute.AddDays(1) ? regulaerBis : heute.AddDays(1);
        for (var tag = von; tag < gerechnetBis; tag = tag.AddDays(1))
        {
            leerlauf += Zuteilen(proTag, tag, zeitraeume, jeGrow);
        }

        if (aus is { } ausgemustert && ausgemustert <= heute)
        {
            restwert = proTag * (bis - ausgemustert).Days;
            leerlauf += Zuteilen(restwert, ausgemustert, zeitraeume, jeGrow);
        }

        var offen = Math.Max(0, a.GesamtEur - jeGrow.Values.Sum() - leerlauf);
        return new AnschaffungAnteile(a.Id, von, bis, aus, proTag, jeGrow, leerlauf, restwert, offen);
    }

    /// <summary>
    /// Ein Kalendertag ohne Zeitzone — wie Start- und Enddatum eines Grows. Mit
    /// Art „Local" schriebe die API „+02:00" dazu, und ein Browser in einer
    /// anderen Zone zeigte den Vortag.
    /// </summary>
    private static DateTime Tag(DateTime wert) => DateTime.SpecifyKind(wert.Date, DateTimeKind.Unspecified);

    /// <summary>Verteilt einen Betrag auf die Grows dieses Tages; gibt zurück, was niemand trägt.</summary>
    private static double Zuteilen(double betrag, DateTime tag, IReadOnlyList<(int Id, DateTime Von, DateTime Bis)> zeitraeume, Dictionary<int, double> jeGrow)
    {
        var laufende = 0;
        foreach (var z in zeitraeume)
        {
            if (z.Von <= tag && tag <= z.Bis) laufende++;
        }
        if (laufende == 0) return betrag;

        var anteil = betrag / laufende;
        foreach (var z in zeitraeume)
        {
            if (z.Von <= tag && tag <= z.Bis) jeGrow[z.Id] = jeGrow.GetValueOrDefault(z.Id) + anteil;
        }
        return 0;
    }

    /// <summary>Letzter Tag, an dem ein Grow lief — null, wenn er keinen Tag beansprucht.</summary>
    private static DateTime? GrowBis(GrowRun g, DateTime heute) => g.Status switch
    {
        GrowStatus.Planning => null,
        _ when g.EndDate is { } ende => Tag(ende),
        GrowStatus.Running => heute,
        _ => null,
    };
}
