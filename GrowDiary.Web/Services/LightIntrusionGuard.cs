using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>
/// Licht in der Dunkelphase der Blüte — der teuerste Fehler im ganzen Zyklus.
/// </summary>
/// <remarks>
/// <para>Eine Zeitschaltuhr, die in der Dunkelphase versagt, oder jemand, der
/// zum Nachsehen die Lampe anmacht: die Pflanze liest das als „Sommer" und
/// reagiert mit Rückwuchs in die Vegetation oder mit Zwitterblüten. Beides
/// merkt man erst Wochen später, und dann ist die Ernte hin.</para>
///
/// <para>Cultivate Control sieht die Einschaltflanke in dem Moment, in dem sie passiert —
/// die Aufzeichnung gab es schon, nur hat sie niemand gelesen. Ein Push jetzt
/// kann die Nacht noch retten; ein Blick ins Protokoll übermorgen nicht.</para>
///
/// <para>Ausgenommen sind Autoflower: die blühen unabhängig vom Zyklus, und
/// Licht in ihrer Dunkelphase ist keine Katastrophe.</para>
/// </remarks>
public static class LightIntrusionGuard
{
    /// <summary>
    /// Die Minuten rund um die gelernten Schaltzeiten zählen nicht: die ersten
    /// nach dem Licht-aus und die letzten vor dem Licht-an.
    /// </summary>
    /// <remarks>
    /// Schaltuhren und Sensoren sind nicht taktgenau; ohne diese Schonfrist
    /// meldete jede Sekunde Nachlauf einen Alarm. Vor dem Licht-an gilt das
    /// genauso: die gelernte Zeit ist ein Median, und die einzelnen Flanken
    /// streuen aus dem Poll-Takt um Minuten — am 23.10.2026 kam der Alarm, weil
    /// das Licht 46 Sekunden vor dem Median anging.
    /// </remarks>
    public const int GraceMinutes = 10;

    /// <summary>
    /// Ist dieses Licht-AN ein Einbruch in die Dunkelphase?
    /// </summary>
    /// <param name="cycle">Der gelernte Zyklus — ohne ihn gibt es keine Dunkelphase.</param>
    /// <param name="localTimeOfDay">Uhrzeit im Zelt, als das Licht anging.</param>
    /// <param name="stage">Die Phase des Grows.</param>
    /// <param name="seedType">Autoflower ist ausgenommen.</param>
    public static bool IsIntrusion(
        LearnedCycle? cycle, TimeOnly localTimeOfDay, GrowStage stage, SeedType seedType)
    {
        if (cycle is null) return false;
        if (seedType == SeedType.Autoflower) return false;

        // Nur in der Blüte ist die Dunkelphase heilig. In der Veg kostet eine
        // Stunde Licht mehr nichts.
        if (stage is not (GrowStage.Transition or GrowStage.Flower or GrowStage.Finish)) return false;

        // Ein Zyklus ohne echte Dunkelphase kann auch nicht gestört werden.
        if (cycle.HoursOn >= 23) return false;

        // Erwartet an ist zwischen OnAt und OffAt — dazwischen ist Einschalten
        // normal (etwa nach einem Stromausfall).
        var an = cycle.OnAt;
        var aus = cycle.OffAt;

        // Die Schonfrist hängt an beiden Enden der Lichtphase: kurz nach dem
        // Ausschalten ist ein Nachzucken keine Störung, und kurz vor dem
        // gelernten Einschalten ist ein früher Schalter keine.
        var anMitFrist = an.AddMinutes(-GraceMinutes);
        var ausMitFrist = aus.AddMinutes(GraceMinutes);

        var inLichtphase = anMitFrist < ausMitFrist
            ? localTimeOfDay >= anMitFrist && localTimeOfDay < ausMitFrist
            : localTimeOfDay >= anMitFrist || localTimeOfDay < ausMitFrist;

        return !inLichtphase;
    }

    /// <summary>Was auf dem Handy stehen soll.</summary>
    public static string Message(string tentName, LearnedCycle cycle, TimeOnly localTimeOfDay)
        => $"Im Zelt „{tentName}“ ist um {localTimeOfDay:HH:mm} das Licht angegangen — "
         + $"mitten in der Dunkelphase ({cycle.Label}, aus um {cycle.OffAt:HH:mm}). "
         + "In der Blüte führt das zu Rückwuchs oder Zwittern. Sofort nachsehen.";
}
