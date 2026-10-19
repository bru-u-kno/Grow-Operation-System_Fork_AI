using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

/// <summary>Which column of the SOP's table a signal fell into.</summary>
public enum StabilitySignalVerdict
{
    /// <summary>Not enough data to say anything.</summary>
    Unknown,
    /// <summary>Matches the "normal swing" column — the plant feeding.</summary>
    Normal,
    /// <summary>Matches the "critical drift" column — chemical or microbial instability.</summary>
    Instability
}

/// <param name="Key">Stable identifier, e.g. "ph-rate".</param>
/// <param name="Label">What was looked at.</param>
/// <param name="Observation">What the data actually showed.</param>
public sealed record StabilitySignal(
    string Key,
    string Label,
    StabilitySignalVerdict Verdict,
    string Observation);

/// <param name="VisualChecks">
/// What the table asks for that no sensor provides — the surface of the water. Returned so
/// the user is asked rather than the question being quietly dropped.
/// </param>
public sealed record StabilityAssessment(
    StabilitySignalVerdict Overall,
    string Headline,
    string Detail,
    IReadOnlyList<StabilitySignal> Signals,
    IReadOnlyList<string> VisualChecks)
{
    public int InstabilityCount => Signals.Count(signal => signal.Verdict == StabilitySignalVerdict.Instability);
    public int NormalCount => Signals.Count(signal => signal.Verdict == StabilitySignalVerdict.Normal);
}

/// <summary>
/// SOP-RDWC-CAN-N1 §2.1 as an algorithm.
///
/// The SOP does not diagnose from one value. It lays out a table and asks you to read five
/// signals together — how fast the pH moved, what the EC did meanwhile, the dissolved
/// oxygen, the ORP, and the look of the water — because the same pH movement means "the
/// plant is feeding" in one combination and "biofilm" in another. Cultivate Control checked each of
/// those separately and could therefore never reach the SOP's conclusion.
///
/// The water surface has no sensor, so it comes back as a question instead of being
/// silently dropped from the table.
/// </summary>
public sealed class SolutionStabilityAnalyzer
{
    private const int WindowDays = 4;
    private const int MinimumPoints = 2;

    /// <summary>
    /// Obergrenze für den Abstand zweier Messungen, aus denen eine
    /// Geschwindigkeit gerechnet wird.
    /// </summary>
    /// <remarks>
    /// Das Fenster ist vier Tage breit, die beiden jüngsten Messungen darin
    /// können aber weit auseinanderliegen — oder einen kaputten Zeitstempel
    /// tragen. Ohne diese Grenze stand in der Diagnose „pH bewegte sich um
    /// 0,05 in 634417 h", also 72 Jahre, und daraus wurde eine Änderung je
    /// Tag gerechnet. Eine Zahl, die niemand nachprüfen kann, ist schlechter
    /// als „zu wenig Daten".
    /// </remarks>
    private const double MaxHoursBetweenPoints = WindowDays * 24.0 + 12.0;

    /// <summary>SOP-N1 §2.1: normal is 0,1–0,4 a day, a drift is 0,5 or more in 12–24 h.</summary>
    /// <remarks>
    /// Dieselbe SOP-Schwelle wie in der Diagnose, geprueft ueber ein anderes
    /// Fenster (108 statt 24 Stunden). Das Fenster ist die Entscheidung dieses
    /// Dienstes, die Schwelle nicht.
    /// </remarks>
    private const double PhDriftPerDay = DeviationAnalyzerService.PhDriftCritical;

    /// <summary>SOP-N1 §2.1: healthy stays above 7,5; below 6,5 is microbial activity.</summary>
    private const double DoNormal = 7.5;
    /// <remarks>
    /// Verweist auf die Diagnose statt die Zahl abzutippen — dieselbe
    /// SOP-Schwelle, nur ueber ein anderes Fenster geprueft.
    /// </remarks>
    private const double DoInstability = DeviationAnalyzerService.DoActionThreshold;

    /// <summary>SOP-N1 §2.1: the value should stay above 300 mV and decay slowly.</summary>
    private const double OrpFloor = 300;
    private const double OrpRapidDecayPerDay = 60;

    /// <summary>An EC swing this large between readings is the "stark schwankend" column.</summary>
    private const double EcSwing = 0.3;

    public StabilityAssessment Assess(IReadOnlyList<Measurement> measurements, DateTime now)
    {
        var window = measurements
            .Where(measurement => measurement.TakenAt >= now.AddDays(-WindowDays))
            .OrderByDescending(measurement => measurement.TakenAt)
            .ToList();

        var signals = new List<StabilitySignal>
        {
            AssessPhRate(window),
            AssessEc(window),
            AssessDissolvedOxygen(window),
            AssessOrp(window),
        };

        var instability = signals.Count(signal => signal.Verdict == StabilitySignalVerdict.Instability);
        var normal = signals.Count(signal => signal.Verdict == StabilitySignalVerdict.Normal);

        var visual = new List<string>
        {
            "Wasseroberfläche: klar ohne Blasen und Schleimhaut, oder trüb mit Schaum und schleimigen Partikeln?",
            "Geruch: frische Bohnensprossen (gesund), faulig (anaerob) oder Chlor (zu stark oxidiert)?",
        };

        // Two matching signals is where the table stops being a coincidence: a single low
        // reading has many harmless explanations, two pointing the same way rarely do.
        if (instability >= 2)
        {
            return new StabilityAssessment(
                StabilitySignalVerdict.Instability,
                "Muster deutet auf chemische oder mikrobiologische Instabilität",
                $"{instability} von {signals.Count} auswertbaren Merkmalen fallen in die Drift-Spalte der SOP. "
                + "Das ist nicht die normale Nährstoffaufnahme. Wurzeln, Wasserprobe und Filter prüfen, "
                + "bevor am pH nachgeregelt wird — sonst behandelt man das Symptom.",
                signals,
                visual);
        }

        if (instability == 1)
        {
            var flagged = signals.First(signal => signal.Verdict == StabilitySignalVerdict.Instability);
            return new StabilityAssessment(
                StabilitySignalVerdict.Unknown,
                "Ein Merkmal fällt aus dem Rahmen",
                $"Auffällig ist nur: {flagged.Observation} Die übrigen Merkmale sprechen für normalen Betrieb. "
                + "Einzeln lässt sich daraus noch keine Instabilität ableiten — beobachten und die "
                + "Sichtprüfung unten ergänzen.",
                signals,
                visual);
        }

        if (normal == 0)
        {
            return new StabilityAssessment(
                StabilitySignalVerdict.Unknown,
                "Zu wenig Daten für das Muster",
                "Für die Unterscheidung nach SOP-N1 §2.1 braucht es mindestens zwei Messungen der letzten "
                + "vier Tage mit pH, EC, Sauerstoff und ORP.",
                signals,
                visual);
        }

        return new StabilityAssessment(
            StabilitySignalVerdict.Normal,
            "Muster spricht für normale Nährstoffaufnahme",
            $"{normal} von {signals.Count} auswertbaren Merkmalen liegen in der Normal-Spalte der SOP. "
            + "Eine leichte pH-Absenkung bei stabiler EC ist genau das, was eine fressende Pflanze macht.",
            signals,
            visual);
    }

    private static StabilitySignal AssessPhRate(List<Measurement> window)
    {
        var points = window.Where(m => m.ReservoirPh.HasValue).ToList();
        if (points.Count < MinimumPoints)
        {
            return new StabilitySignal("ph-rate", "pH-Geschwindigkeit", StabilitySignalVerdict.Unknown, "Zu wenige pH-Messungen.");
        }

        var hours = (points[0].TakenAt - points[1].TakenAt).TotalHours;
        if (hours <= 0 || hours > MaxHoursBetweenPoints)
        {
            return new StabilitySignal("ph-rate", "pH-Geschwindigkeit", StabilitySignalVerdict.Unknown, "Zu wenig verwertbare pH-Messungen: die beiden jüngsten liegen zu weit auseinander.");
        }

        var delta = points[0].ReservoirPh!.Value - points[1].ReservoirPh!.Value;
        var perDay = Math.Abs(delta) / hours * 24.0;
        var text = $"pH bewegte sich um {Math.Abs(delta):0.00} in {hours:0} h (rund {perDay:0.00}/Tag).";

        return perDay >= PhDriftPerDay
            ? new StabilitySignal("ph-rate", "pH-Geschwindigkeit", StabilitySignalVerdict.Instability, text)
            : new StabilitySignal("ph-rate", "pH-Geschwindigkeit", StabilitySignalVerdict.Normal, text);
    }

    private static StabilitySignal AssessEc(List<Measurement> window)
    {
        var points = window.Where(m => m.ReservoirEc.HasValue).ToList();
        if (points.Count < MinimumPoints)
        {
            return new StabilitySignal("ec", "EC-Verhalten", StabilitySignalVerdict.Unknown, "Zu wenige EC-Messungen.");
        }

        var latest = points[0].ReservoirEc!.Value;
        var previous = points[1].ReservoirEc!.Value;
        var delta = latest - previous;

        // The table: stable or slightly falling is normal; rising or swinging is not.
        var swings = points.Zip(points.Skip(1), (a, b) => Math.Abs(a.ReservoirEc!.Value - b.ReservoirEc!.Value))
            .Any(change => change >= EcSwing);

        var text = $"EC {(delta >= 0 ? "stieg" : "fiel")} um {Math.Abs(delta):0.00} auf {latest:0.00} mS/cm.";

        if (swings)
        {
            return new StabilitySignal("ec", "EC-Verhalten", StabilitySignalVerdict.Instability,
                text + " Der Verlauf schwankt stark.");
        }

        return delta > 0.05
            ? new StabilitySignal("ec", "EC-Verhalten", StabilitySignalVerdict.Instability,
                text + " Steigende EC bei pH-Bewegung passt zur Drift-Spalte.")
            : new StabilitySignal("ec", "EC-Verhalten", StabilitySignalVerdict.Normal,
                text + " Stabil oder leicht fallend — das Bild einer fressenden Pflanze.");
    }

    private static StabilitySignal AssessDissolvedOxygen(List<Measurement> window)
    {
        var latest = window.FirstOrDefault(m => m.DissolvedOxygenMgL.HasValue)?.DissolvedOxygenMgL;
        if (latest is not { } value)
        {
            return new StabilitySignal("do", "Sauerstoff", StabilitySignalVerdict.Unknown, "Kein DO-Wert erfasst.");
        }

        var text = $"Sauerstoff liegt bei {value:0.0} mg/L.";
        if (value < DoInstability)
        {
            return new StabilitySignal("do", "Sauerstoff", StabilitySignalVerdict.Instability,
                text + " Unter 6,5 mg/L spricht die SOP von erhöhter mikrobiologischer Aktivität.");
        }

        return value >= DoNormal
            ? new StabilitySignal("do", "Sauerstoff", StabilitySignalVerdict.Normal, text + " Über 7,5 mg/L ist der Normalbereich.")
            : new StabilitySignal("do", "Sauerstoff", StabilitySignalVerdict.Unknown, text + " Zwischen 6,5 und 7,5 mg/L — Graubereich.");
    }

    private static StabilitySignal AssessOrp(List<Measurement> window)
    {
        var points = window.Where(m => m.OrpMv.HasValue).ToList();
        if (points.Count == 0)
        {
            return new StabilitySignal("orp", "ORP-Verhalten", StabilitySignalVerdict.Unknown, "Kein ORP-Wert erfasst.");
        }

        var latest = points[0].OrpMv!.Value;
        if (latest < OrpFloor)
        {
            return new StabilitySignal("orp", "ORP-Verhalten", StabilitySignalVerdict.Instability,
                $"ORP liegt bei {latest:0} mV, also unter 300 — anaerob mit Keimrisiko.");
        }

        if (points.Count < MinimumPoints)
        {
            return new StabilitySignal("orp", "ORP-Verhalten", StabilitySignalVerdict.Normal,
                $"ORP liegt bei {latest:0} mV. Für die Abbaugeschwindigkeit fehlt eine zweite Messung.");
        }

        var hours = (points[0].TakenAt - points[1].TakenAt).TotalHours;
        var drop = points[1].OrpMv!.Value - latest;
        var perDay = hours > 0 ? drop / hours * 24.0 : 0;

        // "Rapider Abbau" is the table's phrase — the value falling unusually fast between
        // top-ups, which is what happens when something in the water is consuming it.
        return perDay >= OrpRapidDecayPerDay
            ? new StabilitySignal("orp", "ORP-Verhalten", StabilitySignalVerdict.Instability,
                $"ORP fiel um rund {perDay:0} mV/Tag auf {latest:0} mV — ungewöhnlich rascher Abbau.")
            : new StabilitySignal("orp", "ORP-Verhalten", StabilitySignalVerdict.Normal,
                $"ORP liegt bei {latest:0} mV und baut langsam ab.");
    }
}
