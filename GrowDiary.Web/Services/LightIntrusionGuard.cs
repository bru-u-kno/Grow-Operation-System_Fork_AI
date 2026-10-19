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
    /// Die ersten Minuten nach dem geplanten Licht-aus zählen nicht.
    /// </summary>
    /// <remarks>
    /// Schaltuhren und Sensoren sind nicht taktgenau; ohne diese Schonfrist
    /// meldete jede Sekunde Nachlauf einen Alarm.
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

        // Die Schonfrist hängt hinten an der Lichtphase: kurz nach dem Ausschalten
        // ist ein Nachzucken keine Störung.
        var ausMitFrist = aus.AddMinutes(GraceMinutes);

        var inLichtphase = an < ausMitFrist
            ? localTimeOfDay >= an && localTimeOfDay < ausMitFrist
            : localTimeOfDay >= an || localTimeOfDay < ausMitFrist;

        return !inLichtphase;
    }

    /// <summary>Was auf dem Handy stehen soll.</summary>
    public static string Message(string tentName, LearnedCycle cycle, TimeOnly localTimeOfDay)
        => $"Im Zelt „{tentName}“ ist um {localTimeOfDay:HH:mm} das Licht angegangen — "
         + $"mitten in der Dunkelphase ({cycle.Label}, aus um {cycle.OffAt:HH:mm}). "
         + "In der Blüte führt das zu Rückwuchs oder Zwittern. Sofort nachsehen.";
}
