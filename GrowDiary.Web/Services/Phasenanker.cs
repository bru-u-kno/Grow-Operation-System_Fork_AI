using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>Die Phasen eines Laufs, wie der Phasenanker sie führt.</summary>
public enum Ankerphase
{
    Anzucht,
    Veg,
    Uebergang,
    Bluete,
    Finish,
    Ende,
}

/// <summary>Was in der Anzucht gerade passiert.</summary>
public enum Anzuchtart
{
    Keimung,
    Saemling,
    Bewurzelung,
}

/// <summary>
/// Eine offene Erinnerung: die Phase ist nach dem bisherigen Richtwert durch,
/// bestätigt hat sie aber noch niemand.
/// </summary>
/// <param name="Art">„vegi-beginn", „bewurzelung" oder „bluete-beginn".</param>
/// <param name="Aktion">Die vorhandene Grow-Aktion (Pfadteil unter <c>/actions/</c>), die sie schließt.</param>
/// <param name="Knopf">Beschriftung des Knopfs.</param>
/// <param name="Text">Der Satz für die Oberfläche.</param>
/// <param name="Ab">Ab diesem Tag wird erinnert — der bisherige Schätzwert.</param>
/// <param name="Tage">Der wievielte Tag (Beginntag = Tag 1) — wie im Zeitstrahl.</param>
public sealed record Phasenerinnerung(
    string Art,
    string Aktion,
    string Knopf,
    string Text,
    DateTime Ab,
    int Tage);

/// <summary>Der Stand eines Laufs an einem Stichtag.</summary>
/// <param name="Stichtag">Der Tag, für den gerechnet wurde.</param>
/// <param name="Phase">Die Phase an diesem Tag.</param>
/// <param name="Anzucht">Nur in der Anzucht: Keimung, Sämling oder Bewurzelung.</param>
/// <param name="Stufe">Dieselbe Auskunft als <see cref="GrowStage"/> — für Sollwerte und Charts.
/// Nach dem Ende die Stufe am Erntetag.</param>
/// <param name="AnzuchtAb">Beginn der Anzucht.</param>
/// <param name="VegAb">Beginn der Vegi — nur bestätigt, nie geschätzt; null, solange offen.</param>
/// <param name="BlueteAb">Beginn der Blüte (Flip bzw. bestätigter Blütebeginn); auch ein geplanter Flip in der Zukunft.</param>
/// <param name="FinishAb">Beginn des Finish — bestätigt, oder aus den Breeder-Wochen.</param>
/// <param name="EndeAm">Erntetag / Ende des Laufs.</param>
/// <param name="PhaseAb">Beginn der laufenden Phase. Bei Übergang und Blüte ist das der Blütebeginn.</param>
/// <param name="TagInPhase">Tag in der laufenden Phase, ab 1.</param>
/// <param name="WocheInPhase">Woche in der laufenden Phase, ab 1, ohne Obergrenze.</param>
/// <param name="Erinnerung">Offene Erinnerung, sonst null.</param>
public sealed record Phasenstand(
    DateTime Stichtag,
    Ankerphase Phase,
    Anzuchtart? Anzucht,
    GrowStage Stufe,
    DateTime AnzuchtAb,
    DateTime? VegAb,
    DateTime? BlueteAb,
    DateTime? FinishAb,
    DateTime? EndeAm,
    DateTime PhaseAb,
    int TagInPhase,
    int WocheInPhase,
    Phasenerinnerung? Erinnerung)
{
    /// <summary>
    /// Woche innerhalb einer Chart-Phase („Clone", „Seedling", „Veg",
    /// „Transition", „Flower", „Finish"), ab 1 und ohne Obergrenze.
    /// </summary>
    /// <remarks>
    /// Gezählt ab dem Beginn dieser Phase bis zum Stichtag — oder bis zum Beginn
    /// der nächsten Phase, wenn die schon vorbei ist. So zeigt die gehaltene
    /// letzte Vegi-Spalte auch in der Blüte noch, wie lang die Vegi war.
    /// Fehlt der Beginn (Phase nie erreicht), ist es Woche 1.
    /// </remarks>
    public int WocheIn(string chartStage)
    {
        var (von, bis) = chartStage.ToLowerInvariant() switch
        {
            "clone" or "seedling" => (AnzuchtAb, VegAb ?? BlueteAb),
            "veg" => (VegAb, BlueteAb),
            "flower" or "transition" => (BlueteAb, FinishAb),
            "finish" => (FinishAb ?? BlueteAb, (DateTime?)null),
            _ => ((DateTime?)null, (DateTime?)null),
        };
        if (von is not { } beginn) return 1;
        var ende = bis is { } b && b < Stichtag ? b : Stichtag;
        if (EndeAm is { } e && e.AddDays(1) < ende) ende = e.AddDays(1);
        return Wochen((ende - beginn).Days);
    }

    internal static int Wochen(int tageSeitBeginn) => Math.Max(1, tageSeitBeginn / 7 + 1);
}

/// <summary>
/// Phase und Woche eines Laufs — die EINE Stelle, an der Phasenbeginne
/// entstehen.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass (02.10.2026).</b> „Wann beginnt die Vegi" stand an vier
/// Stellen verschieden: der Resolver schätzte 14 Tage Sämling ab Keimung, der
/// Mischplan zählte die Vegi-Wochen ab dem Startdatum (die Anzucht also mit —
/// nach dem Sämling stand der Plan sofort in Vegi-Woche 3), die
/// Plan-Auswertung nahm Start + 7, der Wochenzähler die Keimung. Ein
/// Samen-Grow mit „Einstieg Blüte" galt dazu als Veg, weil das Formular bei
/// jedem späteren Einstieg ein Keimdatum einträgt.</para>
///
/// <para><b>Die Regeln (Entscheidung des Nutzers).</b> Geschätzt wird kein
/// Phasenbeginn mehr. Die Vegi beginnt mit der Bestätigung — „Vegi beginnt"
/// (<c>VegStartedAt</c>) bzw. beim Steckling „Bewurzelung abgeschlossen"
/// (<c>RootedAt</c>); die Blüte mit dem Flip bzw. bei der Autoflower mit „Blüte
/// beginnt" (beides <c>FlipDate</c>). Wer beim Anlegen einen späteren Einstieg
/// angegeben hat, hat damit bestätigt: die Phase lief seit Start minus der
/// mitgebrachten Tage. Das Keimdatum, das das Formular dabei einträgt, ist
/// KEINE Bestätigung der Vegi. Wo früher automatisch umgeschaltet wurde, steht
/// jetzt eine <see cref="Phasenerinnerung"/> — ab genau dem alten
/// Schätzwert.</para>
///
/// <para><b>Warum eine eigene Klasse und nicht <see cref="GrowStageResolver"/>.</b>
/// Der Resolver beantwortet eine Frage — welche <see cref="GrowStage"/> — und
/// hat über fünfzehn Leser, die genau das brauchen. Der Anker liefert mehr
/// (Beginne, Woche, Erinnerung). Der Resolver bleibt als Kurzform und fragt
/// hier; gerechnet wird nur noch an dieser Stelle. Festgehalten von
/// <c>PhasenankerVollstaendigTests</c>.</para>
/// </remarks>
public static class Phasenanker
{
    /// <summary>
    /// Bisheriger Richtwert für die Anzucht in Tagen. Kein Phasenbeginn mehr —
    /// nur noch, ab wann an die Bestätigung erinnert wird. Typisch sind ein bis
    /// drei Wochen; 14 liegt in der Mitte (Richtwert, kein Befund).
    /// </summary>
    public const int RichtwertAnzuchtTage = 14;

    /// <summary>
    /// Bisheriger Richtwert: eine Autoflower blüht etwa 28 Tage nach der
    /// Keimung. Nur noch Erinnerung, keine Umschaltung (Richtwert, kein Befund).
    /// </summary>
    public const int RichtwertAutoflowerBlueteTage = 28;

    /// <summary>Die ersten Tage nach dem Blütebeginn sind Übergang, noch nicht volle Blüte.</summary>
    public const int UebergangTage = 10;

    /// <summary>Ohne bestätigtes Finish: die letzten zwei Wochen vor der Ernte laut Breeder-Wochen.</summary>
    public const int FinishTageVorErnte = 14;

    /// <summary>Phase, Beginne, Woche und Erinnerung eines Laufs an einem Stichtag.</summary>
    public static Phasenstand Fuer(GrowRun grow, DateTime stichtag)
    {
        var tag = stichtag.Date;
        var a = AnkerVon(grow);

        var phase = PhaseAm(a, tag);
        var stufe = StufeAm(grow, a, a.EndeAm is { } ende && tag > ende ? ende : tag);
        var phaseAb = phase switch
        {
            Ankerphase.Anzucht => a.AnzuchtAb,
            Ankerphase.Veg => a.VegAb!.Value,
            Ankerphase.Uebergang or Ankerphase.Bluete => a.BlueteAb!.Value,
            Ankerphase.Finish => a.FinishAb!.Value,
            _ => a.EndeAm!.Value.AddDays(1),
        };
        var tagInPhase = Math.Max(1, (tag - phaseAb).Days + 1);

        return new Phasenstand(
            tag,
            phase,
            phase == Ankerphase.Anzucht ? AnzuchtartAm(grow, tag) : null,
            stufe,
            a.AnzuchtAb,
            a.VegAb,
            a.BlueteAb,
            a.FinishAb,
            a.EndeAm,
            phaseAb,
            tagInPhase,
            Phasenstand.Wochen(tagInPhase - 1),
            grow.IsArchived ? null : ErinnerungAm(grow, a, phase, tag));
    }

    /// <summary>Die Anker, ohne Stichtag.</summary>
    private sealed record Anker(
        DateTime AnzuchtAb,
        DateTime? VegAb,
        DateTime? BlueteAb,
        DateTime? FinishAb,
        DateTime? EndeAm);

    private static Anker AnkerVon(GrowRun grow)
    {
        var start = grow.StartDate.Date;
        var einstieg = grow.EntryPoint;

        // Mitgebrachte Tage gehören zur Einstiegsphase. Bei der Autoflower
        // fragt das Formular stattdessen die Tage seit der Keimung.
        int Mitgebracht(GrowEntryPoint phase)
        {
            if (einstieg != phase) return 0;
            if (grow.SeedType == SeedType.Autoflower && phase is GrowEntryPoint.Germination or GrowEntryPoint.Seedling)
            {
                return Math.Max(0, grow.AutoflowerDaysSinceGermination ?? 0);
            }
            return Math.Max(0, grow.DaysAlreadyInPhase ?? 0);
        }

        var finishAusEinstieg = einstieg == GrowEntryPoint.Flush ? start.AddDays(-Mitgebracht(GrowEntryPoint.Flush)) : (DateTime?)null;

        var bluete = grow.FlipDate?.Date
            ?? (einstieg == GrowEntryPoint.Flower ? start.AddDays(-Mitgebracht(GrowEntryPoint.Flower)) : (DateTime?)null)
            ?? finishAusEinstieg;

        var veg = grow.VegStartedAt?.Date
            ?? (grow.StartMaterial == StartMaterial.Clone
                ? grow.RootedAt?.Date ?? (grow.CloneIsRooted ? start : (DateTime?)null)
                : null)
            ?? (einstieg == GrowEntryPoint.Veg ? start.AddDays(-Mitgebracht(GrowEntryPoint.Veg)) : (DateTime?)null)
            // Einstieg in Blüte oder Spülen: die Vegi liegt vor dem Lauf und hat
            // hier keine Dauer.
            ?? (einstieg is GrowEntryPoint.Flower or GrowEntryPoint.Flush ? bluete : null);

        var anzucht = einstieg is GrowEntryPoint.Germination or GrowEntryPoint.Seedling
            ? start.AddDays(-Mitgebracht(einstieg))
            : Frueheste(start, veg, bluete);

        DateTime? finish = grow.FinishStartedAt?.Date ?? finishAusEinstieg;
        if (finish is null && bluete is { } flip
            && (grow.BreederFlowerWeeksMax ?? grow.BreederFlowerWeeksMin) is { } wochen && wochen > 0)
        {
            // Wie bisher: die letzten zwei Wochen vor der Breeder-Ernte, aber nie
            // im Übergang.
            var geschaetzt = flip.AddDays(wochen * 7 - FinishTageVorErnte);
            var fruehestens = flip.AddDays(UebergangTage);
            finish = geschaetzt < fruehestens ? fruehestens : geschaetzt;
        }

        return new Anker(anzucht, veg, bluete, finish, grow.EndDate?.Date);
    }

    private static DateTime Frueheste(DateTime start, DateTime? a, DateTime? b)
    {
        var ergebnis = start;
        if (a is { } x && x < ergebnis) ergebnis = x;
        if (b is { } y && y < ergebnis) ergebnis = y;
        return ergebnis;
    }

    private static Ankerphase PhaseAm(Anker a, DateTime tag)
    {
        if (a.EndeAm is { } ende && tag > ende) return Ankerphase.Ende;
        if (a.BlueteAb is { } bluete && tag >= bluete)
        {
            if (a.FinishAb is { } finish && tag >= finish) return Ankerphase.Finish;
            return (tag - bluete).Days < UebergangTage ? Ankerphase.Uebergang : Ankerphase.Bluete;
        }
        if (a.VegAb is { } veg && tag >= veg) return Ankerphase.Veg;
        return Ankerphase.Anzucht;
    }

    private static Anzuchtart AnzuchtartAm(GrowRun grow, DateTime tag)
    {
        if (grow.StartMaterial == StartMaterial.Clone) return Anzuchtart.Bewurzelung;
        if (grow.EntryPoint == GrowEntryPoint.Seedling) return Anzuchtart.Saemling;
        return grow.GerminatedAt is { } gekeimt && tag >= gekeimt.Date ? Anzuchtart.Saemling : Anzuchtart.Keimung;
    }

    private static GrowStage StufeAm(GrowRun grow, Anker a, DateTime tag)
        => PhaseAm(a, tag) switch
        {
            Ankerphase.Anzucht => AnzuchtartAm(grow, tag) == Anzuchtart.Bewurzelung ? GrowStage.Clone : GrowStage.Seedling,
            Ankerphase.Veg => GrowStage.Veg,
            Ankerphase.Uebergang => GrowStage.Transition,
            Ankerphase.Bluete => GrowStage.Flower,
            _ => GrowStage.Finish,
        };

    /// <summary>
    /// Keimung einer Autoflower, um mitgebrachte Tage vorverlegt — Grundlage
    /// beider Autoflower-Richtwerte.
    /// </summary>
    private static DateTime AutoflowerKeimung(GrowRun grow)
        => (grow.GerminatedAt?.Date ?? grow.StartDate.Date).AddDays(-(grow.AutoflowerDaysSinceGermination ?? 0));

    /// <summary>
    /// Ab wann an „Vegi beginnt" erinnert wird — genau dort, wo früher
    /// automatisch umgeschaltet wurde.
    /// </summary>
    private static DateTime RichtwertVegi(GrowRun grow)
    {
        if (grow.SeedType == SeedType.Autoflower) return AutoflowerKeimung(grow).AddDays(RichtwertAnzuchtTage);
        var basis = grow.GerminatedAt?.Date ?? grow.StartDate.Date;
        var mitgebracht = grow.EntryPoint is GrowEntryPoint.Germination or GrowEntryPoint.Seedling
            ? Math.Max(0, grow.DaysAlreadyInPhase ?? 0)
            : 0;
        return basis.AddDays(RichtwertAnzuchtTage - mitgebracht);
    }

    /// <remarks>
    /// Der Text zählt wie der Zeitstrahl: der Beginntag ist Tag 1. Die erste
    /// Fassung schrieb „seit 16 Tagen" direkt unter einen Balken „Anzucht 17" —
    /// zwei Zahlen für denselben Tag, am laufenden Stand gesehen.
    /// </remarks>
    private static Phasenerinnerung? ErinnerungAm(GrowRun grow, Anker a, Ankerphase phase, DateTime tag)
    {
        if (phase == Ankerphase.Anzucht)
        {
            var tagNr = Math.Max(1, (tag - a.AnzuchtAb).Days + 1);
            if (grow.StartMaterial == StartMaterial.Clone)
            {
                var ab = a.AnzuchtAb.AddDays(RichtwertAnzuchtTage);
                return tag < ab ? null : new Phasenerinnerung(
                    "bewurzelung", "confirm-rooting", "Bewurzelung abgeschlossen",
                    $"Bewurzelung Tag {tagNr} — schon abgeschlossen?", ab, tagNr);
            }

            var vegi = RichtwertVegi(grow);
            return tag < vegi ? null : new Phasenerinnerung(
                "vegi-beginn", "confirm-veg", "Vegi beginnt",
                $"Anzucht Tag {tagNr} — Vegi-Beginn bestätigen?", vegi, tagNr);
        }

        if (phase == Ankerphase.Veg && grow.SeedType == SeedType.Autoflower && a.BlueteAb is null)
        {
            var keimung = AutoflowerKeimung(grow);
            var ab = keimung.AddDays(RichtwertAutoflowerBlueteTage);
            var tagSeitKeimung = Math.Max(1, (tag - keimung).Days + 1);
            return tag < ab ? null : new Phasenerinnerung(
                "bluete-beginn", "flip-to-flower", "Blüte beginnt",
                $"Autoflower Tag {tagSeitKeimung} nach der Keimung — Blütebeginn bestätigen?", ab, tagSeitKeimung);
        }

        return null;
    }

}
