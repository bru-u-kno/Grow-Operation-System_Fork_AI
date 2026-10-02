using GrowDiary.Web.Services.Knowledge.Schema;

namespace GrowDiary.Web.Models;

/// <summary>
/// Fork AI (Grow-Plan, 16.09.2026): der Plan eines Grows — eine eigene Kopie
/// des gewählten Düngeprogramms.
/// </summary>
/// <remarks>
/// <para><b>Warum eine Kopie.</b> Der Grow trug bisher nur die Programm-Id. Wer
/// das Programm später änderte, änderte damit rückwirkend auch jeden
/// abgeschlossenen Grow, der es benutzt hatte — eine Auswertung „was lief
/// damals" war nicht möglich. Vorbild ist der Zelt- und Anlagen-Schnappschuss
/// des Originals (<see cref="GrowRun.TentSnapshotJson"/>).</para>
/// <para><b>Drei Stände.</b> Der <i>Startstand</i> entsteht beim Anlegen und
/// ändert sich nie. Der <i>Arbeitsstand</i> ist das, was gilt und bearbeitet
/// wird. Beim Abschließen wird er als <i>Endstand</i> eingefroren.</para>
/// </remarks>
public static class GrowPlanStaende
{
    public const string Start = "start";
    public const string Arbeit = "arbeit";
    public const string Ende = "ende";

    /// <summary>
    /// Die frische Programmkopie nach einem Programmwechsel — Vergleichswert
    /// „Start“ für den Arbeitsstand. Der eigentliche Startstand bleibt für die
    /// Auswertung erhalten.
    /// </summary>
    public const string Basis = "basis";
}

/// <summary>Woher ein Wert im Plan stammt.</summary>
public static class GrowPlanHerkunft
{
    /// <summary>Stand so im Programm (Normalfall, wird nicht gespeichert).</summary>
    public const string Programm = "programm";

    /// <summary>Das Programm nannte nichts; beim Anlegen aus dem mitgelieferten Standard gefüllt.</summary>
    public const string Standard = "standard";

    /// <summary>Weder Programm noch Standard kennen den Wert — der Nutzer soll ihn eintragen.</summary>
    public const string Fehlt = "fehlt";

    /// <summary>Vom Nutzer im Plan gesetzt.</summary>
    public const string Eigen = "eigen";
}

/// <summary>Arten von Einträgen im Änderungsbuch.</summary>
public static class GrowPlanArten
{
    public const string Angelegt = "angelegt";
    public const string Wert = "wert";
    public const string Dosierung = "dosierung";
    public const string Programmwechsel = "programmwechsel";
    public const string Eingefroren = "eingefroren";
    public const string Wiedergeoeffnet = "wiedergeoeffnet";
    public const string AlsProgramm = "alsprogramm";
    /// <summary>Fork AI (F-045): Startstand an eine Quelle angeglichen (z. B. Vorlagen-PDF) — keine eigene Änderung.</summary>
    public const string Startkorrektur = "startkorrektur";
    /// <summary>
    /// Fork AI (02.10.2026): eine Woche angehängt, weil die Phase länger läuft als
    /// der Plan — <c>Alt</c> ist die Woche, deren Werte übernommen wurden.
    /// </summary>
    public const string Verlaengert = "verlaengert";
}

/// <summary>Der gespeicherte Inhalt eines Planstands.</summary>
public sealed class GrowPlanInhalt
{
    /// <summary>Id des Programms, aus dem der Plan entstand.</summary>
    public string ProgrammId { get; set; } = string.Empty;

    /// <summary>Name des Programms zum Zeitpunkt des Anlegens.</summary>
    public string ProgrammName { get; set; } = string.Empty;

    /// <summary>
    /// Das eigene Programm, in das „auch ins Programm“ schreibt. Leer, bis zum
    /// ersten Mal übernommen wird — ein mitgeliefertes Programm wird nie geändert.
    /// </summary>
    public string? EigenesProgrammId { get; set; }

    /// <summary>
    /// Fork AI (forkai.130): Standard für alle Wochen dieses Grows — nachts gelten
    /// die Tageswerte (Luft, Luftfeuchte). Aus: Nachtwerte kommen aus dem Plan.
    /// </summary>
    /// <remarks>
    /// Vorgabe aus, damit bestehende Grows sich nicht verändern: dort gilt nachts
    /// heute schon ein eigenes Band.
    /// </remarks>
    public bool NachtWieTag { get; set; }

    /// <summary>
    /// Fork AI (forkai.130): Wochen, die vom Standard abweichen (Spalten-Id → nachts
    /// wie tags ja/nein). Fehlt eine Woche, gilt <see cref="NachtWieTag"/>.
    /// </summary>
    public Dictionary<string, bool> NachtWieTagJeWoche { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gelten in dieser Woche nachts die Tageswerte?</summary>
    public bool NachtWieTagFuer(string spalteId)
        => NachtWieTagJeWoche.TryGetValue(spalteId, out var eigen) ? eigen : NachtWieTag;

    /// <summary>
    /// Die Nachtwerte einer Woche, wie sie gelten: bei „nachts wie tags" die
    /// Tageswerte, sonst die Nachtfelder — Luft ersatzweise Tag − Nachtabsenkung
    /// (nur bis zum Nachtrag), Feuchte ersatzweise wie tags.
    /// </summary>
    public (double? LuftC, double? RhMax) NachtWerte(FeedChartColumn spalte)
    {
        if (NachtWieTagFuer(spalte.Id)) return (spalte.AirTempC, spalte.RhMax);
        var luft = spalte.AirTempNightC
            ?? (spalte.AirTempC is { } tag ? tag - Services.WochenplanSyncService.Nachtabsenkung : null);
        return (luft, spalte.RhMaxNight ?? spalte.RhMax);
    }

    /// <summary>Die Wochen samt Zielen und Dosierung — Schema wie im Programm.</summary>
    public FeedChartDefinition Chart { get; set; } = new();

    /// <summary>
    /// Spalten-Id → Feldname → Herkunft. Steht ein Feld hier nicht, stammt es
    /// aus dem Programm.
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> Herkunft { get; set; } = new();

    /// <summary>
    /// Fork AI (02.10.2026): angehängte Wochen — neue Spalten-Id → Id der Woche,
    /// deren Werte sie beim Anhängen übernommen hat.
    /// </summary>
    /// <remarks>
    /// Läuft eine Phase länger als der Plan, bekommt sie eigene Wochen
    /// (<see cref="Services.GrowPlan.Planwochen.Anhaengen"/>). Eine Woche, die hier
    /// steht, ist „verlängert": sie kommt nicht aus dem Programm und hat im
    /// Startstand keine eigene Spalte.
    /// </remarks>
    public Dictionary<string, string> Verlaengert { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ist diese Woche angehängt (nicht aus dem Programm)?</summary>
    public bool IstVerlaengert(string spalteId) => Verlaengert.ContainsKey(spalteId);

    /// <summary>
    /// Die Programmwoche, für die diese Woche steht: sie selbst, oder bei einer
    /// angehängten Woche die letzte Woche der Phase, die aus dem Programm kam.
    /// </summary>
    /// <remarks>
    /// Danach richten sich Startwert und „zurück auf Plan" einer angehängten
    /// Woche — der Startstand ist die Vorlage und kennt sie nicht. Woche 11
    /// übernimmt von Woche 10, die von 9: gefolgt wird der Kette bis zur
    /// Programmwoche (begrenzt, falls jemand eine Schleife hineinschreibt).
    /// </remarks>
    public string Programmwoche(string spalteId)
    {
        var id = spalteId;
        for (var schritt = 0; schritt < 200 && Verlaengert.TryGetValue(id, out var vorlage); schritt++)
        {
            id = vorlage;
        }
        return id;
    }

    /// <summary>Herkunft eines Felds; <see cref="GrowPlanHerkunft.Programm"/>, wenn nichts vermerkt ist.</summary>
    public string HerkunftVon(string spalteId, string feld)
        => Herkunft.TryGetValue(spalteId, out var felder) && felder.TryGetValue(feld, out var herkunft)
            ? herkunft
            : GrowPlanHerkunft.Programm;

    public void HerkunftSetzen(string spalteId, string feld, string herkunft)
    {
        if (herkunft == GrowPlanHerkunft.Programm)
        {
            if (Herkunft.TryGetValue(spalteId, out var alt))
            {
                alt.Remove(feld);
                if (alt.Count == 0) Herkunft.Remove(spalteId);
            }
            return;
        }

        if (!Herkunft.TryGetValue(spalteId, out var felder))
        {
            felder = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Herkunft[spalteId] = felder;
        }
        felder[feld] = herkunft;
    }
}

/// <summary>Ein gespeicherter Planstand eines Grows.</summary>
public sealed record GrowPlanStand(
    int GrowId,
    string Stand,
    GrowPlanInhalt Inhalt,
    string? Vermerk,
    DateTime AngelegtUtc,
    DateTime GeaendertUtc);

/// <summary>Ein Eintrag im Änderungsbuch — wird nie geändert oder gelöscht.</summary>
public sealed record GrowPlanEintrag(
    long Id,
    int GrowId,
    DateTime ZeitUtc,
    string Art,
    string? SpalteId,
    string? Feld,
    string? Alt,
    string? Neu,
    string? Ziel,
    string? Grund);
