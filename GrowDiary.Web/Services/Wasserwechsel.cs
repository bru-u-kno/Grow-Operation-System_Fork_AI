using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Wann zuletzt Wasser gewechselt wurde — die einzige Antwort auf diese Frage.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (31.08.2026).</b> Gemeldet: „der User findet den
/// Wasserwechsel nicht wirklich, das ist sehr umständlich von uns gelöst, weil
/// er hat jetzt einen gemacht und will den eintragen und zurückdatieren."
/// Beim Nachsehen war das Eintragen nicht das Problem — es blieb nur
/// wirkungslos.</para>
///
/// <para><b>Es gab zwei Wahrheiten.</b> Ein Wechsel kann auf zwei Wegen in die
/// Datenbank kommen: als Häkchen <c>SolutionChange</c> an einer Messung, oder
/// als eigener Satz in <c>Changeouts</c> (das Formular auf /addback). Gelesen
/// wurde je nach Dienst nur eine der beiden:</para>
///
/// <list type="table">
///   <listheader><term>Dienst</term><description>las vorher</description></listheader>
///   <item><term>GrowAlertService → RecommendationEngine</term><description>nur die Messung</description></item>
///   <item><term>SopDueService („Wöchentlicher Wasserwechsel")</term><description>nur die Messung</description></item>
///   <item><term>TrendWatchService</term><description>nur die Messung</description></item>
///   <item><term>DosingContextBuilder</term><description>beide — als einziger, mit eigener Kopie der Logik</description></item>
/// </list>
///
/// <para>Wer den Wechsel also über das Formular eintrug, räumte damit keine
/// einzige Mahnung weg: „Wöchentlicher Wasserwechsel: zuletzt vor 11 Tagen"
/// blieb stehen, obwohl der Wechsel erfasst war. Das ist die Regel „eine
/// Wahrheit je Zahl", einmal mehr belegt.</para>
///
/// <para><b>Zwei Zeitzonen, mit Absicht getrennt.</b> <c>Measurement.TakenAt</c>
/// steht in Ortszeit, <c>ChangeoutEntry.PerformedAtUtc</c> in UTC. Wer beide
/// in einen Topf wirft, verschiebt den Wechsel um zwei Stunden — im Sommer
/// genug, um einen Tag zu kippen. Deshalb gibt es hier zwei Methoden statt
/// einer Zahl, die der Aufrufer selbst deuten muss.</para>
/// </remarks>
public static class Wasserwechsel
{
    /// <summary>
    /// Der letzte belegte Wasserwechsel in <b>Ortszeit</b> — für alles, was
    /// gegen <c>DateTime.Today</c> oder gegen <c>Measurement.TakenAt</c> rechnet.
    /// </summary>
    /// <param name="messungen">Messungen des Grows; nur die mit <c>SolutionChange</c> zählen.</param>
    /// <param name="wechsel">Einträge aus dem Formular „Wasserwechsel".</param>
    /// <returns>Der jüngste der beiden Belege, oder <c>null</c>, wenn es keinen gibt.</returns>
    public static DateTime? ZuletztOrtszeit(
        IEnumerable<Measurement>? messungen,
        IEnumerable<ChangeoutEntry>? wechsel)
    {
        var ausMessung = AusMessungen(messungen);
        var ausFormular = AusWechseln(wechsel)?.ToLocalTime();
        return Juengster(ausMessung, ausFormular);
    }

    /// <summary>
    /// Derselbe Zeitpunkt in <b>UTC</b> — für alles, was gegen
    /// <c>DateTime.UtcNow</c> rechnet.
    /// </summary>
    public static DateTime? ZuletztUtc(
        IEnumerable<Measurement>? messungen,
        IEnumerable<ChangeoutEntry>? wechsel)
    {
        var ausMessung = AusMessungen(messungen)?.ToUniversalTime();
        var ausFormular = AusWechseln(wechsel);
        return Juengster(ausMessung, ausFormular);
    }

    /// <summary>
    /// Der letzte Wechsel für die <b>Lösung selbst</b> (UTC) — auch einer, der die
    /// Erinnerung nicht neu startet.
    /// </summary>
    /// <remarks>
    /// Für die Dosierung (A-006, Befund des Prüfers): nach jedem Wechsel puffert
    /// frisches Wasser anders, das Lernfenster muss dort schneiden — gleich, ob
    /// der Bediener den Wechsel für die Erinnerung zählen lässt. Die Erinnerung
    /// selbst (Stand, Mahnung, Trend) liest <see cref="ZuletztUtc"/> bzw.
    /// <see cref="ZuletztOrtszeit"/>.
    /// </remarks>
    public static DateTime? ZuletztFuerDieLoesungUtc(
        IEnumerable<Measurement>? messungen,
        IEnumerable<ChangeoutEntry>? wechsel)
    {
        var ausMessung = AusMessungen(messungen)?.ToUniversalTime();
        var ausFormular = wechsel?.Select(w => (DateTime?)w.PerformedAtUtc).DefaultIfEmpty(null).Max();
        return Juengster(ausMessung, ausFormular);
    }

    /// <summary>
    /// Ob zwischen zwei Messungen Wasser gewechselt wurde — für alle Trendvergleiche
    /// („Wert ist um X gestiegen/gefallen"), die sonst den Wechsel selbst als
    /// Abweichung melden.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Anlass (11.10.2026).</b> Vollwechsel 11:07, Messung davor
    /// EC 1,30, Messung danach EC 0,61: Das Risiko „EC: Abweichung prüfen — um
    /// −0,69 gefallen" blieb stehen, obwohl der Wechsel als Vorgang erfasst war
    /// und der Wert im Zielband lag. Die Analyse kannte den Wechsel nicht.</para>
    ///
    /// <para><b>Gezählt wird jeder Wechsel</b>, auch einer, der die Erinnerung
    /// nicht neu startet — es geht um die Lösung selbst, wie bei
    /// <see cref="ZuletztFuerDieLoesungUtc"/>. Das Fenster ist <c>(von, bis]</c>:
    /// Ein Wechsel an der Messung „nachher" (dort setzt der Vorgang
    /// <c>SolutionChange</c>) trennt das Paar; ein Wechsel an der Messung
    /// „vorher" nicht mehr.</para>
    /// </remarks>
    /// <param name="von">Zeitpunkt der älteren Messung (Ortszeit, wie <c>Measurement.TakenAt</c>).</param>
    /// <param name="bis">Zeitpunkt der jüngeren Messung (Ortszeit).</param>
    public static bool LiegtDazwischen(
        DateTime von,
        DateTime bis,
        IEnumerable<Measurement>? messungen,
        IEnumerable<ChangeoutEntry>? wechsel)
    {
        var vonUtc = von.ToUniversalTime();
        var bisUtc = bis.ToUniversalTime();

        var ausMessung = messungen is not null && messungen.Any(m =>
            m.SolutionChange
            && m.TakenAt.ToUniversalTime() > vonUtc
            && m.TakenAt.ToUniversalTime() <= bisUtc);
        var ausFormular = wechsel is not null && wechsel.Any(w =>
            w.PerformedAtUtc > vonUtc && w.PerformedAtUtc <= bisUtc);
        return ausMessung || ausFormular;
    }

    private static DateTime? AusMessungen(IEnumerable<Measurement>? messungen)
        => messungen is null
            ? null
            : messungen.Where(m => m.SolutionChange)
                .Select(m => (DateTime?)m.TakenAt)
                .DefaultIfEmpty(null)
                .Max();

    /// <remarks>
    /// Seit A-006 (05.10.2026) zählt ein Wechsel nur, wenn er die Erinnerung
    /// neu startet (<see cref="ChangeoutEntry.ErinnerungNeuStarten"/>). Altdaten
    /// tragen dort <c>true</c> und zählen wie bisher. Der Vorgang setzt dann auch
    /// <c>SolutionChange</c> an seiner Messung „nachher" nicht — sonst käme der
    /// abgeschaltete Wechsel über die Messung wieder herein.
    /// </remarks>
    private static DateTime? AusWechseln(IEnumerable<ChangeoutEntry>? wechsel)
        => wechsel is null
            ? null
            : wechsel.Where(w => w.ErinnerungNeuStarten)
                .Select(w => (DateTime?)w.PerformedAtUtc)
                .DefaultIfEmpty(null)
                .Max();

    private static DateTime? Juengster(DateTime? a, DateTime? b)
        => (a, b) switch
        {
            (null, null) => null,
            (null, { } nurB) => nurB,
            ({ } nurA, null) => nurA,
            var (beideA, beideB) => beideA > beideB ? beideA : beideB,
        };
}
