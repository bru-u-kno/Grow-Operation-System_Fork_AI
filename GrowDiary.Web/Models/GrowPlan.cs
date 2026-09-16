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

    /// <summary>Die Wochen samt Zielen und Dosierung — Schema wie im Programm.</summary>
    public FeedChartDefinition Chart { get; set; } = new();

    /// <summary>
    /// Spalten-Id → Feldname → Herkunft. Steht ein Feld hier nicht, stammt es
    /// aus dem Programm.
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> Herkunft { get; set; } = new();

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
