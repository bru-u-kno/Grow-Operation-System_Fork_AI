using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

public enum TrendSeverity
{
    Info,
    Warning,
    Critical
}

/// <summary>Something worth knowing that no single reading would have revealed.</summary>
/// <param name="Code">Stable key, so a finding can be pushed once instead of every round.</param>
/// <param name="GuidanceId">The growplan rule behind it, when there is one.</param>
public sealed record TrendFinding(
    string Code,
    TrendSeverity Severity,
    string Headline,
    string Detail,
    string? GuidanceId = null);

/// <summary>
/// The holiday guard.
///
/// A threshold alert answers "is this value wrong right now". That misses the failures that
/// actually ruin a run while nobody is looking: pH walking from 5.9 to 6.4 over five days,
/// consumption quietly collapsing because the roots are rotting, a water change that never
/// happened. Every reading along the way looks acceptable — only the shape does not.
///
/// Deliberately deterministic. This is the layer people rely on when they are away, so it
/// must not depend on a model, a provider, or a network beyond the house.
/// </summary>
public sealed class TrendWatchService
{
    /// <summary>Days of history to look at. Long enough to see a trend, short enough to stay current.</summary>
    public const int WindowDays = 7;

    /// <summary>Fewer points than this and "a trend" is just noise.</summary>
    public const int MinimumPoints = 4;

    /// <summary>The growplan calls a weekly change mandatory; this is the grace on top.</summary>
    public const int WaterChangeDueDays = 7;
    public const int WaterChangeOverdueDays = 10;

    /// <summary>SOP-N1: HOCl top-up every 2–3 days; 4 is already past the window.</summary>
    public const int OrpTopUpDueDays = 3;
    public const int OrpTopUpOverdueDays = 5;

    private static readonly System.Globalization.CultureInfo De = AppCulture.German;

    /// <summary>
    /// Pure evaluation over one grow's recent measurements, newest first or oldest first —
    /// it sorts for itself. No database, no clock beyond what is passed in.
    /// </summary>
    public static IReadOnlyList<TrendFinding> Evaluate(
        IReadOnlyList<Measurement> measurements,
        HydroTargetValues? targets,
        DateTime now,
        IReadOnlyList<ChangeoutEntry>? wechsel = null,
        double? rampenBodenC = null)
    {
        var window = measurements
            .Where(measurement => measurement.TakenAt >= now.AddDays(-WindowDays))
            .OrderBy(measurement => measurement.TakenAt)
            .ToList();

        var findings = new List<TrendFinding>();

        // Das Band je Messgroesse kommt aus BandFuer — derselben Lesart wie Kachel und
        // Diagnose. Beim pH heisst das: Handlungsbereich, nicht das Anmischziel; ein
        // Wandern innerhalb der Komfortzone ist ausdruecklich erlaubt.
        var (phMin, phMax) = BandFuer("reservoir-ph", targets, rampenBodenC);
        AddDrift(findings, window, "ph", "pH", measurement => measurement.ReservoirPh, 0.25, "0.0#",
            phMin, phMax, "ph-drift-band");
        var (ecMin, ecMax) = BandFuer("reservoir-ec", targets, rampenBodenC);
        AddDrift(findings, window, "ec", "EC", measurement => measurement.ReservoirEc, 0.30, "0.0#",
            ecMin, ecMax, "ec-keep-hungry");
        var (orpMin, orpMax) = BandFuer("orp", targets, rampenBodenC);
        AddDrift(findings, window, "orp", "ORP", measurement => measurement.OrpMv, 60, "0",
            orpMin, orpMax, "orp-rises-with-stage");
        var (wasserMin, wasserMax) = BandFuer("reservoir-temp", targets, rampenBodenC);
        AddDrift(findings, window, "watertemp", "Wassertemperatur", measurement => measurement.ReservoirWaterTempC, 2.0, "0.0",
            wasserMin, wasserMax, null);

        AddWaterChange(findings, measurements, wechsel, now);
        AddConsumption(findings, window);
        AddOrpTopUp(findings, measurements, now);

        return findings;
    }

    /// <summary>
    /// Das Band, an dem der Waechter „noch im erlaubten Bereich" misst — eine Messgroesse,
    /// eine Antwort.
    /// </summary>
    /// <param name="metrikSchluessel">Kennung wie auf der Kachel, z. B. <c>reservoir-temp</c>.</param>
    /// <param name="targets">Zielband der Phase/Woche, <b>ohne</b> eigene Grenzen des Nutzers.</param>
    /// <param name="rampenBodenC">Wohin die Nachtabsenkung faehrt (<see cref="Wasserband.RampenBodenC"/>).</param>
    /// <remarks>
    /// <para><b>Der Anlass (Fork AI, 02.10.2026).</b> Hier standen eigene Baender:
    /// beim pH fest 5,8–6,2, bei der Wassertemperatur [Nacht, Tag] des Profils
    /// (z. B. 18–20 °C — in der Veg-Phase ein einziger Punkt). Bei 20,5 °C, vier
    /// Tage langsam steigend, meldete der Waechter „ausserhalb" und schickte eine
    /// Push-Nachricht, waehrend Kachel und Diagnose fuer dieselbe Messung „im
    /// Bereich" sagten. Ein Profil mit pH 5,6–6,0 hatte auf der Kachel den
    /// Handlungsbereich 5,6–6,2, im Waechter 5,8–6,2.</para>
    ///
    /// <para><b>Die Rollen der drei Wassertemperatur-Zahlen:</b></para>
    /// <list type="bullet">
    ///   <item><b>Arbeitsbereich</b> (<see cref="Wasserband.Grenzen"/>, SOP-RDWC-CAN-N1,
    ///   untere Grenze von der Nachtabsenkung mitgezogen) — die GRENZE, ab der
    ///   beanstandet wird. Kachel-Ziel, Messprotokoll und Diagnose lesen sie.</item>
    ///   <item><b>Tag-/Nachtwert</b> des Profils — das ZIEL, auf das der Kuehler
    ///   regelt (Wochenplan-Sync, Nachtabsenkung).</item>
    ///   <item><b>Alarm auf „Plan"</b> (<see cref="Planzielgrenzen.Wirksam"/>) —
    ///   Ziel ± Toleranz je Lichtphase: eine vom Nutzer eingestellte Leine um das
    ///   Ziel, keine fachliche Grenze.</item>
    /// </list>
    /// <para>Der Waechter sagt „noch im erlaubten Bereich" — das ist die Frage nach
    /// der Grenze. Er folgt deshalb dem Arbeitsbereich, ueber
    /// <see cref="Zielband.FuerMetrik"/> und nicht ueber eine Abschrift.</para>
    ///
    /// <para><b>Ohne Profil</b> gibt <see cref="Zielband.FuerMetrik"/> nichts her.
    /// Dann gelten dieselben Formeln mit leerem Zielband — genau das, was die
    /// Diagnose in diesem Fall rechnet: pH-Komfortzone, SOP-Arbeitsbereich, EC und
    /// ORP ohne Band.</para>
    /// </remarks>
    public static (double? Min, double? Max) BandFuer(
        string metrikSchluessel, HydroTargetValues? targets, double? rampenBodenC = null)
    {
        if (targets is not null)
        {
            return Zielband.FuerMetrik(metrikSchluessel, targets, rampenBodenC);
        }

        return metrikSchluessel switch
        {
            "reservoir-ph" => DeviationAnalyzerService.PhHandlungsbereich(null, null),
            "reservoir-temp" => Wasserband.Grenzen(null, rampenBodenC, null),
            _ => (null, null),
        };
    }

    /// <summary>
    /// A value moving the same way day after day. The point is that it can still be inside
    /// its band the whole time — by the time a threshold fires, five days were lost.
    /// </summary>
    private static void AddDrift(
        List<TrendFinding> findings,
        List<Measurement> window,
        string code,
        string label,
        Func<Measurement, double?> read,
        double minimumChange,
        string format,
        double? targetMin,
        double? targetMax,
        string? guidanceId)
    {
        // One value per day, so a day with six measurements doesn't outvote the rest.
        var daily = window
            .Where(measurement => read(measurement) is not null)
            .GroupBy(measurement => measurement.TakenAt.Date)
            .OrderBy(group => group.Key)
            .Select(group => (Day: group.Key, Value: read(group.OrderByDescending(m => m.TakenAt).First())!.Value))
            .ToList();

        if (daily.Count < MinimumPoints)
        {
            return;
        }

        var first = daily[0].Value;
        var last = daily[^1].Value;
        var change = last - first;
        if (Math.Abs(change) < minimumChange)
        {
            return;
        }

        // Every step has to agree with the overall direction; one wobble and it is not a drift.
        var rising = change > 0;
        for (var i = 1; i < daily.Count; i++)
        {
            var step = daily[i].Value - daily[i - 1].Value;
            if (rising ? step < 0 : step > 0)
            {
                return;
            }
        }

        var direction = rising ? "steigt" : "fällt";
        var days = (daily[^1].Day - daily[0].Day).Days + 1;
        var detail =
            $"{label} {direction} seit {days} Tagen durchgehend: " +
            $"{first.ToString(format, De)} → {last.ToString(format, De)}.";

        // Still inside the band is the interesting case — nothing else would have said a word.
        var outsideBand = (targetMin is { } min && last < min) || (targetMax is { } max && last > max);
        if (!outsideBand)
        {
            detail += " Der Wert ist noch im erlaubten Bereich — auffällig ist die Richtung, nicht die Zahl.";
        }

        findings.Add(new TrendFinding(
            $"trend.{code}.drift",
            outsideBand ? TrendSeverity.Warning : TrendSeverity.Info,
            $"{label} driftet seit {days} Tagen",
            detail,
            guidanceId));
    }

    private static void AddWaterChange(
        List<TrendFinding> findings, IReadOnlyList<Measurement> measurements,
        IReadOnlyList<ChangeoutEntry>? wechsel, DateTime now)
    {
        // Beide Belege zaehlen — Haekchen an der Messung und Eintrag im
        // Formular. Bis zum 31.08.2026 sah diese Stelle nur den ersten.
        var lastChange = Wasserwechsel.ZuletztOrtszeit(measurements, wechsel);

        // Without a recorded change there is nothing to count from; saying "overdue" to
        // someone who simply never logged one would be noise, not a warning.
        if (lastChange is null)
        {
            return;
        }

        var days = (int)(now.Date - lastChange.Value.Date).TotalDays;
        if (days < WaterChangeDueDays)
        {
            return;
        }

        findings.Add(new TrendFinding(
            "trend.waterchange.overdue",
            days >= WaterChangeOverdueDays ? TrendSeverity.Warning : TrendSeverity.Info,
            $"Wasserwechsel seit {days} Tagen offen",
            $"Der letzte dokumentierte Wechsel war am {lastChange.Value.ToString("dd.MM.", De)}. "
            + "Der Growplan sieht wöchentlich vor.",
            "weekly-water-change"));
    }

    /// <summary>
    /// SOP-N1: the ORP has to be brought back up with HOCl every two to three days. It is
    /// a consumable, not a setting — it decays as it does its job, and the moment it is
    /// forgotten is the moment the reservoir turns anaerobic without a single value moving
    /// out of range that day.
    /// </summary>
    private static void AddOrpTopUp(List<TrendFinding> findings, IReadOnlyList<Measurement> measurements, DateTime now)
    {
        var lastOrp = measurements
            .Where(measurement => measurement.OrpMv is not null)
            .OrderByDescending(measurement => measurement.TakenAt)
            .FirstOrDefault();

        // Never measured means the user isn't tracking ORP at all — nagging about a value
        // they don't collect would be noise, not a reminder.
        if (lastOrp is null)
        {
            return;
        }

        var days = (int)(now.Date - lastOrp.TakenAt.Date).TotalDays;
        if (days < OrpTopUpDueDays)
        {
            return;
        }

        findings.Add(new TrendFinding(
            "trend.orp.topup-due",
            days >= OrpTopUpOverdueDays ? TrendSeverity.Warning : TrendSeverity.Info,
            $"ORP seit {days} Tagen nicht geprüft",
            $"Der letzte ORP-Wert stammt vom {lastOrp.TakenAt.ToString("dd.MM.", De)} "
            + $"({lastOrp.OrpMv:0} mV). Laut SOP wird alle 2–3 Tage per HOCl nachjustiert — "
            + "der Wert baut sich im Betrieb laufend ab.",
            "orp-optimal-band"));
    }

    /// <summary>
    /// Consumption is the plant's own report. A collapse means it stopped drinking — roots
    /// or a blockage; a jump usually means the water went somewhere it shouldn't.
    /// </summary>
    private static void AddConsumption(List<TrendFinding> findings, List<Measurement> window)
    {
        /* Auch die Nulltage.
           Hier stand `is > 0` — ein Tag mit dokumentierten 0 L fiel damit aus
           der Reihe, statt als 0 einzugehen. Der VOLLSTAENDIGE Einbruch war so
           unsichtbar, waehrend ein blosser Rueckgang auf die Haelfte gemeldet
           wurde: aus 4, 4, 4, 4, 0, 0, 0 blieb [4,4,4,4], Verhaeltnis 1, keine
           Meldung. Dabei ist der stille Fall der schlimmere — eine Pflanze, die
           drei Tage gar nichts mehr trinkt, hat ein Wurzelproblem.

           `is not null` statt `is > 0`: ein Tag ohne Eintrag bleibt draussen
           (da wurde nicht gemessen), ein Tag mit eingetragener 0 zaehlt. */
        var daily = window
            .Where(measurement => measurement.TopOffLiters is not null)
            .GroupBy(measurement => measurement.TakenAt.Date)
            .OrderBy(group => group.Key)
            .Select(group => group.Sum(measurement => measurement.TopOffLiters!.Value))
            .ToList();

        if (daily.Count < MinimumPoints)
        {
            return;
        }

        var recent = daily.TakeLast(2).Average();
        var earlier = daily.Take(daily.Count - 2).Average();
        if (earlier <= 0)
        {
            return;
        }

        var ratio = recent / earlier;
        if (ratio <= 0.5)
        {
            findings.Add(new TrendFinding(
                "trend.consumption.drop",
                TrendSeverity.Warning,
                "Verbrauch eingebrochen",
                $"Zuletzt {recent.ToString("0.#", De)} L/Tag gegenüber {earlier.ToString("0.#", De)} L/Tag davor. "
                + "Wenn die Pflanze aufhört zu trinken, sind meist die Wurzeln oder eine Verstopfung die Ursache.",
                "daily-consumption-plausibility"));
        }
        else if (ratio >= 2.0)
        {
            findings.Add(new TrendFinding(
                "trend.consumption.spike",
                TrendSeverity.Warning,
                "Verbrauch stark gestiegen",
                $"Zuletzt {recent.ToString("0.#", De)} L/Tag gegenüber {earlier.ToString("0.#", De)} L/Tag davor. "
                + "Prüfe zuerst auf ein Leck, bevor du es als Wachstum verbuchst.",
                "daily-consumption-plausibility"));
        }
    }
}
