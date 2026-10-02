using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services.Knowledge;

namespace GrowDiary.Web.Services;

public sealed class DeviationAnalyzerService
{
    // RDWC growplan: hold the pH between these and let it drift; correct only outside the
    // critical bounds. Stage setpoints stay the mixing target.
    // Public because the trend guard has to judge pH by the same rule. Keeping a second
    // copy is how the "chase the pH" mistake would quietly come back in another file.
    public const double PhComfortMin = 5.8;
    public const double PhComfortMax = 6.2;

    /// <summary>
    /// Der pH-Bereich, ab dem gehandelt wird — nicht das Anmischziel.
    /// </summary>
    /// <remarks>
    /// <para><b>Fachlich:</b> der Stufenwert ist das, worauf man ANMISCHT, kein
    /// Schwellenwert zum Hinterherregeln. Der RDWC-Growplan sagt ausdrücklich,
    /// dass der pH innerhalb der Komfortzone von allein wandern darf (ab
    /// Blütewoche 4 sogar soll); eingegriffen wird erst, wenn er sie verlässt.
    /// Wer bei jeder kleinen Drift warnt, rät das Gegenteil des Plans.</para>
    ///
    /// <para><b>Der Anlass (01.09.2026).</b> Diese Formel stand nur in der
    /// Diagnose. Die Live-Kachel maß am Anmischziel — bei pH 5,85 (Profil
    /// 5,90–6,00) schrieb sie „daneben" und zog zehn Punkte vom Score ab,
    /// während das Messprotokoll derselben Messung „im Ziel, Komfortzone
    /// 5,8–6,2" sagte. Zwei Urteile über einen Wert, nebeneinander auf dem
    /// Schirm.</para>
    ///
    /// <para><b>Eigene Grenzen gewinnen.</b> Wer sie einträgt, meint keine
    /// Empfehlung mehr, sondern eine Ansage — dann gilt seine Zahl, auch wenn
    /// sie enger ist als die Komfortzone.</para>
    /// </remarks>
    public static (double Min, double Max) PhHandlungsbereich(
        HydroTargetValues? targets, (double? Min, double? Max)? eigene)
    {
        var unten = Math.Min(targets?.PhMin ?? PhComfortMin, PhComfortMin);
        var oben = Math.Max(targets?.PhMax ?? PhComfortMax, PhComfortMax);

        // Je Grenze, nicht auf einen Schlag: <c>UserTargets.IsUserSet</c> ist
        // schon true, wenn EINE der beiden eingetragen ist. Wer nur eine
        // Obergrenze setzte, verlor damit die ganze Komfortzone — die Diagnose
        // rechnete ab der Profil-Untergrenze 5,9 und meldete „zu niedrig",
        // waehrend die Kachel daneben bei 5,8 blieb und „im Ziel" schrieb.
        if (eigene is { } e)
        {
            if (e.Min is { } min) unten = min;
            if (e.Max is { } max) oben = max;
        }

        return (unten, oben);
    }
    public const double PhCriticalMin = 5.5;
    public const double PhCriticalMax = 6.5;

    // Without CO2 enrichment the growplan caps light here; its higher PPFD targets assume CO2.
    private const double PpfdCeilingWithoutCo2 = 900;

    // SOP-RDWC-CAN-N1, Abschnitt 2.2: unter 6,5 mg/L gilt als erhoehte mikrobiologische
    // Aktivitaet und ist ein Handlungsauslöser. SOP-RDWC-CAN-S1, 2.2: unter 6 mg/L gilt
    // Wurzelfaeule als bestaetigt. Vorher stand hier pauschal 6 bzw. 4.
    /// <remarks>
    /// Oeffentlich, weil dieselbe Zahl an vier Stellen stand: hier, im
    /// Stabilitaets-Analysator, in der Empfehlungs-Maschine und als Satztext im
    /// Protokoll-Beurteiler. Vier Orte fuer eine SOP-Schwelle sind vier
    /// Gelegenheiten, drei davon zu vergessen.
    /// </remarks>
    public const double DoActionThreshold = 6.5;

    /// <inheritdoc cref="DoActionThreshold"/>
    public const double DoInfestationThreshold = 6.0;

    // SOP-RDWC-CAN-N1, Abschnitt 2.1: 0,1–0,4 pH-Punkte pro Tag sind eine normale
    // Schwankung, ab 0,5 innerhalb von 12–24 h ist es ein kritischer Drift mit
    // Sofortmassnahmen. Der reine Absolutwert verraet das nicht — er kann die ganze Zeit
    // im Band bleiben.
    /// <remarks>
    /// Oeffentlich: der Stabilitaets-Analysator prueft dieselbe SOP-Regel ueber
    /// ein anderes Fenster (108 statt 24 Stunden) und hatte die Zahl abgetippt.
    /// Das Fenster darf sich unterscheiden, die Schwelle nicht.
    /// </remarks>
    public const double PhDriftCritical = 0.5;
    private const double PhDriftLight = 0.2;
    private const int PhDriftWindowHours = 24;
    private const double Co2EnrichmentFrom = 800;
    private const int MaxConsecutiveLookback = 10;

    private readonly TargetValueService _targetValues;

    private readonly AlertRuleRepository? _alertRules;

    private readonly KnowledgeBaseLoader? _wissen;

    public DeviationAnalyzerService(
        TargetValueService targetValues,
        AlertRuleRepository? alertRules = null,
        KnowledgeBaseLoader? wissen = null)
    {
        _targetValues = targetValues;
        _alertRules = alertRules;
        _wissen = wissen;
    }

    /// <param name="leafTempOffsetC">
    /// The tent's leaf offset. Defaults to the documented RDWC value so a caller without a
    /// tent at hand still gets leaf VPD rather than air VPD — the two are different numbers
    /// and every RDWC recommendation is written for the former.
    /// </param>
    public IReadOnlyList<GrowDeviation> Analyze(
        GrowRun grow,
        IReadOnlyList<Measurement> recentMeasurements,
        double leafTempOffsetC = Tent.DefaultLeafTempOffsetC,
        // Das Sollwert-Profil des Hydro-Systems: die mittlere Stufe der
        // Kette Grow -> System -> Anbaustil. Der Aufrufer reicht sie durch,
        // weil dieser Dienst als Singleton keinen Zugriff auf die
        // Hydro-Ablage hat.
        string? systemProfileId = null)
    {
        /* Hier stand ein frueher Ausstieg auf IrrigationType und
           Profile.IsHydro. Er konnte nie greifen: IrrigationType hat genau
           einen Wert, und GrowthProfile.IsHydro ist "=> true". Er las sich wie
           ein Schutz und war keiner — wer ihn sah, hielt die Diagnose fuer
           anbauart-abhaengig, und sie ist es nicht.

           Kommt eine zweite Anbauart, wird zuerst
           KeinZweigFuerEineAnbauartDieEsNichtGibtTests rot. */

        // Messungen aus der Zukunft fliegen raus, bevor sortiert wird.
        //
        // Der Bestand enthaelt eine Testzeile mit dem Datum 2099-01-01. Weil
        // die Diagnose die juengste Messung nach Zeitstempel nimmt, urteilte
        // sie aus genau dieser Zeile — sechs Wochen lang, ohne dass es
        // jemandem auffiel. Eine Stunde Luft nach vorn, damit eine leicht
        // vorgehende Uhr keine echte Messung verschluckt.
        var spaetestens = DateTime.Now.AddHours(1);

        var sorted = recentMeasurements
            .Where(measurement => measurement.GrowId == 0 || measurement.GrowId == grow.Id)
            .Where(measurement => measurement.TakenAt <= spaetestens)
            .OrderByDescending(measurement => measurement.TakenAt)
            .ThenByDescending(measurement => measurement.Id)
            .Take(MaxConsecutiveLookback)
            .ToList();

        if (sorted.Count == 0)
        {
            return Array.Empty<GrowDeviation>();
        }

        var latest = sorted[0];
        // Der eingetragene Wert des Nutzers gewinnt — dieselbe Regel wie auf den
        // Live-Kacheln. Vorher las die Diagnose nur das Wissen und widersprach
        // damit den Alarmen, die schon immer die Werte des Nutzers nahmen.
        // Die Profil-Kette Grow -> System -> Anbaustil, dieselbe wie auf den
        // Live-Kacheln (GrowDashboardComposer). Vorher fragte die Diagnose
        // nur mit dem Anbaustil und landete damit immer beim Standardprofil:
        // wer eigene Sollwerte eingetragen hatte, bekam sie auf der Kachel zu
        // sehen und in der Diagnose nicht. Gemessen stand fuer denselben Grow
        // EC 0,6-0,8 gegen 0,9-1,1.
        /* Die ganze Kette an EINER Stelle: Profil, Phase, Feedchart-Ziele,
           eigene Grenzen. Hier fehlte Schritt drei — der Schalter „Wochen-Ziele
           auch auf dem Bildschirm" wirkte nur auf der Live-Kachel, und bei
           Athena Blended sagte die Kachel „im Ziel", waehrend das Messprotokoll
           derselben Messung „weit ueber dem Ziel" schrieb. */
        var regeln = grow.TentId is { } tentId ? _alertRules?.GetForTent(tentId) : null;

        /* Die Phase von HEUTE, nicht die Aufschrift der letzten Messung. Bis zum
           02.09.2026 stand hier `latest.Stage` — dieselbe Form wie an vier
           anderen Stellen, nur ohne `?.` und `??`, weshalb die Zaehlung sie im
           ersten Anlauf uebersah.

           Nachgestellt an der laufenden App: Grow 1 blueht seit dem 29.07.,
           eine einzige Messzeile mit Aufschrift „Saemling" reicht — die Kachel
           sagt „EC 1,03 im Ziel (1,00-1,20)", die Diagnose derselben Minute
           „EC 1,03 ausserhalb (0,20-0,40)". Genau der Widerspruch, den der
           Kommentar zwei Zeilen darueber selbst beklagt. */
        var phase = GrowStageResolver.Resolve(grow, DateTime.Today);

        var targets = Zielband.FuerGrow(
            _targetValues, _wissen, grow, phase, systemProfileId, regeln);

        // Fuer die Wassertemperatur getrennt: dort ist das Zielband die Quelle
        // fuer den Arbeitsbereich, und eine eingelegte Alarmregel saehe darin
        // aus wie eine Nachtabsenkung. Sie kommt deshalb als eigene Angabe.
        var ohneNutzer = Zielband.FuerGrow(
            _targetValues, _wissen, grow, phase, systemProfileId, null);

        // Fuer die Phasen-Vergleiche weiter unten: das Profil selbst, ohne
        // Feedchart und ohne eigene Grenzen — dort geht es um „was gilt in
        // Bluete gegen Finish", nicht um das Band dieser einen Messung.
        var profil = SetpointProfileResolver.Resolve(
            grow.SetpointProfileId, systemProfileId, grow.HydroStyle);
        var deviations = new List<GrowDeviation>();

        CheckPh(grow, sorted, targets, UserTargets.For("reservoir-ph", regeln), deviations);
        CheckEc(grow, sorted, targets, deviations);
        CheckPhDriftRate(grow, sorted, deviations);
        CheckOrp(grow, sorted, targets, deviations);
        CheckWaterTemp(grow, sorted, ohneNutzer, Wasserband.RampenBodenC(
            grow,
            _targetValues.GetTargets(profil.ProfileId, GrowStage.Flower),
            _targetValues.GetTargets(profil.ProfileId, GrowStage.Finish)),
            UserTargets.For("reservoir-temp", regeln), deviations);
        CheckDissolvedOxygen(grow, sorted, deviations);
        CheckVpd(grow, sorted, targets, leafTempOffsetC, deviations);
        CheckPpfd(grow, sorted, targets, deviations);
        CheckCo2(grow, sorted, deviations);

        return deviations;
    }

    /// <param name="eigenePh">
    /// Die von Hand eingetragenen Grenzen, je Grenze. Das ändert ihre
    /// Bedeutung: der mitgelieferte Wert ist ein Anmischziel, der eingetragene
    /// eine Grenze. <b>Je Grenze</b>, weil man auch nur eine der beiden
    /// eintragen kann.
    /// </param>
    private static void CheckPh(GrowRun grow, List<Measurement> sorted, HydroTargetValues? targets, (double? Min, double? Max)? eigenePh, List<GrowDeviation> result)
    {
        sorted = sorted.Where(measurement => measurement.ReservoirPh.HasValue).ToList();
        if (sorted.Count == 0 || sorted[0].ReservoirPh is not { } actual)
        {
            return;
        }

        // The stage value is what you mix TO — it is not a threshold to chase. The RDWC
        // growplan is explicit: inside the comfort zone the pH may drift on its own (from
        // flower week 4 deliberately so), and you only intervene once it leaves it. Warning
        // on every small drift produced noise and advised the opposite of the plan.
        //
        // Ausser jemand hat die Grenzen selbst eingetragen. Dann ist die Zahl
        // keine Empfehlung mehr, sondern eine Ansage: „darunter/darüber will ich
        // es wissen". Wer bewusst enger fährt als die Komfortzone, bekam vorher
        // nichts zu sehen — die Zone hat seine Grenzen einfach aufgesogen.
        var (actionMin, actionMax) = PhHandlungsbereich(targets, eigenePh);
        var critical = actual < PhCriticalMin || actual > PhCriticalMax;
        var actionable = actual < actionMin || actual > actionMax;
        if (!actionable && !critical)
        {
            return;
        }

        var predicate = new Func<double, bool>(value => value < actionMin || value > actionMax || value < PhCriticalMin || value > PhCriticalMax);
        var participants = Consecutive(sorted, measurement => measurement.ReservoirPh, predicate);
        var tooHigh = actual > actionMax;
        var mixHint = targets is not null ? $" Anmischen auf {targets.PhMin:0.0}-{targets.PhMax:0.0}." : string.Empty;

        result.Add(CreateDeviation(
            grow,
            "hydro.ph",
            DeviationMetric.Ph,
            actual,
            actionMin,
            actionMax,
            "pH",
            critical ? DeviationSeverity.Critical : DeviationSeverity.Warning,
            tooHigh
                ? $"Reservoir-pH {actual:0.00} liegt ueber dem Handlungsbereich {actionMin:0.0}-{actionMax:0.0}."
                : $"Reservoir-pH {actual:0.00} liegt unter dem Handlungsbereich {actionMin:0.0}-{actionMax:0.0}.",
            (tooHigh ? "pH-Down pruefen." : "pH-Up pruefen.") + mixHint,
            tooHigh ? "ph-too-high" : "ph-too-low",
            participants));
    }

    private static void CheckEc(GrowRun grow, List<Measurement> sorted, HydroTargetValues? targets, List<GrowDeviation> result)
    {
        sorted = sorted.Where(measurement => measurement.ReservoirEc.HasValue).ToList();
        if (sorted.Count == 0 || sorted[0].ReservoirEc is not { } actual)
        {
            return;
        }

        var critical = actual < 0 || actual > 3.0;
        var outsideTarget = targets is not null && (actual < targets.EcMin || actual > targets.EcMax);
        var trendParticipants = GetEcTrendParticipants(sorted);

        if (!critical && !outsideTarget && trendParticipants.Count == 0)
        {
            return;
        }

        IReadOnlyList<Measurement> participants;
        string message;
        string? hint;
        if (trendParticipants.Count > 0)
        {
            participants = trendParticipants;
            var diff = sorted[0].ReservoirEc!.Value - sorted[1].ReservoirEc!.Value;
            message = diff > 0
                ? $"Reservoir-EC ist um {diff:+0.00;-0.00} mS/cm gestiegen."
                : $"Reservoir-EC ist um {diff:+0.00;-0.00} mS/cm gefallen.";
            hint = diff > 0 ? "Verdunstung/Addback pruefen." : "Naehrstoffaufnahme/Addback pruefen.";
        }
        else
        {
            var predicate = targets is not null
                ? new Func<double, bool>(value => value < targets.EcMin || value > targets.EcMax)
                : value => value < 0 || value > 3.0;
            participants = Consecutive(sorted, measurement => measurement.ReservoirEc, predicate);
            message = $"Reservoir-EC {actual:0.00} liegt ausserhalb des Zielbereichs.";
            hint = "EC-Ziel und Addback pruefen.";
        }

        result.Add(CreateDeviation(
            grow,
            "hydro.ec",
            DeviationMetric.Ec,
            actual,
            targets?.EcMin,
            targets?.EcMax,
            "mS/cm",
            critical ? DeviationSeverity.Critical : DeviationSeverity.Warning,
            message,
            hint,
            null,
            participants));
    }

    /// <remarks>
    /// Die Grenzen kommen aus <see cref="Wasserband"/>, nicht aus Ziffern in
    /// dieser Datei — sie standen frueher auch im MeasurementAssessmentService,
    /// und zwei Kopien derselben Zahl laufen auseinander.
    /// </remarks>
    private static void CheckWaterTemp(
        GrowRun grow, List<Measurement> sorted, HydroTargetValues? targets, double? rampenBoden,
        (double? Min, double? Max)? eigeneTemp, List<GrowDeviation> result)
    {
        sorted = sorted.Where(measurement => measurement.ReservoirWaterTempC.HasValue).ToList();
        if (sorted.Count == 0 || sorted[0].ReservoirWaterTempC is not { } actual)
        {
            return;
        }

        // Die Untergrenze zieht der Nachtwert des Profils mit nach unten: die
        // Absenkung faehrt dort absichtlich hin. Eigene Grenzen gewinnen — auch
        // die OBERE, die hier frueher fest bei ArbeitsbereichMaxC stand.
        var (unten, oben) = Wasserband.Grenzen(targets, rampenBoden, eigeneTemp);
        var kritischUnten = Math.Min(Wasserband.KritischMinC, unten - 3);
        var critical = actual > Wasserband.KritischMaxC || actual < kritischUnten;
        var warning = actual > oben || actual < unten;
        if (!critical && !warning)
        {
            return;
        }

        var participants = Consecutive(sorted, measurement => measurement.ReservoirWaterTempC,
            value => value > oben || value < unten);
        result.Add(CreateDeviation(
            grow,
            "hydro.water-temp",
            DeviationMetric.WaterTemp,
            actual,
            unten,
            oben,
            "C",
            critical ? DeviationSeverity.Critical : DeviationSeverity.Warning,
            $"Reservoir-Wassertemperatur {actual:0.0} C liegt ausserhalb des Arbeitsbereichs.",
            "Wassertemperatur und Kuehlung pruefen.",
            actual > oben ? "water-temp-rising-rapid" : null,
            participants));
    }

    /// <summary>
    /// The rate of change, which the absolute value hides completely.
    ///
    /// SOP-RDWC-CAN-N1 §2.1 separates a normal swing from a real drift by speed, not by
    /// position: 0,1–0,4 points a day is the plant feeding, 0,5 or more within 12–24 h is
    /// chemical or microbial instability and calls for immediate checks. A jump from 5,8 to
    /// 6,3 overnight never leaves the comfort band, so nothing else in this class notices it.
    /// </summary>
    private static void CheckPhDriftRate(GrowRun grow, List<Measurement> sorted, List<GrowDeviation> result)
    {
        sorted = sorted.Where(measurement => measurement.ReservoirPh.HasValue).ToList();
        if (sorted.Count < 2
            || sorted[0].ReservoirPh is not { } latest
            || sorted[1].ReservoirPh is not { } previous)
        {
            return;
        }

        var hours = (sorted[0].TakenAt - sorted[1].TakenAt).TotalHours;
        if (hours <= 0 || hours > PhDriftWindowHours)
        {
            return;
        }

        var delta = latest - previous;
        var magnitude = Math.Abs(delta);
        if (magnitude < PhDriftLight)
        {
            return;
        }

        var direction = delta > 0 ? "gestiegen" : "gefallen";
        var critical = magnitude >= PhDriftCritical;

        result.Add(CreateDeviation(
            grow,
            "hydro.ph-drift",
            DeviationMetric.Ph,
            latest,
            PhComfortMin,
            PhComfortMax,
            "pH",
            critical ? DeviationSeverity.Critical : DeviationSeverity.Info,
            $"pH ist in {hours:0} h um {magnitude:0.00} Punkte {direction}.",
            critical
                ? "Kritischer pH-Drift (SOP-N1): Wurzeln, Wasserprobe, ORP und DO pruefen. "
                  + "Bei Befund NSL ablassen, System mit HOCl spuelen und mit pH 5,8-6,0 / ORP > 400 mV neu aufsetzen."
                : "Leichter Drift (SOP-N1): nur um 0,1-0,2 schrittweise nachregeln und Temperatur, "
                  + "Verdunstung sowie Pflanzenaktivitaet gegenpruefen.",
            critical ? "ph-drift-critical" : null,
            new[] { sorted[0], sorted[1] }));
    }

    /// <summary>
    /// VPD against its stage target.
    ///
    /// The setpoints have carried a VPD band per stage all along and nothing ever read it:
    /// the value was calculated, charted and put on tiles, but never compared to anything.
    /// It is also the one figure the workshop material spends a whole chapter on, so leaving
    /// it unevaluated meant the app knew the target and stayed silent about missing it.
    ///
    /// Computed from air temperature and humidity with the tent's leaf offset — leaf VPD,
    /// which is what the RDWC recommendations are written for.
    /// </summary>
    private static void CheckVpd(
        GrowRun grow,
        List<Measurement> sorted,
        HydroTargetValues? targets,
        double leafTempOffsetC,
        List<GrowDeviation> result)
    {
        if (targets is null)
        {
            return;
        }

        var withVpd = sorted
            .Select(measurement => (measurement, vpd: VpdCalculator.Calculate(
                measurement.AirTemperatureC, measurement.HumidityPercent, leafTempOffsetC)))
            .Where(entry => entry.vpd is not null)
            .ToList();

        if (withVpd.Count == 0 || withVpd[0].vpd is not { } actual)
        {
            return;
        }

        var below = actual < targets.VpdMin;
        var above = actual > targets.VpdMax;
        if (!below && !above)
        {
            return;
        }

        // Far outside is a different conversation from slightly outside: 0,3 kPa off is
        // worth a look, twice that is stressing the plant either way.
        var distance = below ? targets.VpdMin - actual : actual - targets.VpdMax;
        var severity = distance >= 0.4 ? DeviationSeverity.Warning : DeviationSeverity.Info;

        var hint = below
            // The workshop is explicit that RDWC transpires far more than soil and wants the
            // upper end — so a low VPD is holding the plant back, not protecting it.
            ? "RDWC transpiriert 2-2,5x so stark wie Erde und vertraegt eher das obere Ende des Bands. "
              + "Zu niedriges VPD bremst Naehrstofftransport. Luftstrom auf Blattniveau pruefen "
              + "(RDWC 90-120 m/min) und die Blattstellung ansehen."
            : "Zu hohes VPD treibt die Transpiration ueber das, was die Wurzeln liefern koennen. "
              + "Vor dem Nachregeln von Temperatur und Feuchte den Luftstrom pruefen — er "
              + "verschiebt das VPD am Blatt.";

        result.Add(CreateDeviation(
            grow,
            "hydro.vpd",
            DeviationMetric.Vpd,
            Math.Round(actual, 2),
            targets.VpdMin,
            targets.VpdMax,
            "kPa",
            severity,
            $"Blatt-VPD {actual:0.00} kPa liegt {(below ? "unter" : "ueber")} dem Zielbereich "
            + $"{targets.VpdMin:0.0}-{targets.VpdMax:0.0}.",
            hint,
            null,
            new[] { withVpd[0].measurement }));
    }

    private static void CheckDissolvedOxygen(GrowRun grow, List<Measurement> sorted, List<GrowDeviation> result)
    {
        sorted = sorted.Where(measurement => measurement.DissolvedOxygenMgL.HasValue).ToList();
        if (sorted.Count == 0 || sorted[0].DissolvedOxygenMgL is not { } actual || actual >= DoActionThreshold)
        {
            return;
        }

        var participants = Consecutive(sorted, measurement => measurement.DissolvedOxygenMgL, value => value < DoActionThreshold);
        var critical = actual < DoInfestationThreshold;
        result.Add(CreateDeviation(
            grow,
            "hydro.do",
            DeviationMetric.DissolvedOxygen,
            actual,
            DoActionThreshold,
            null,
            "mg/L",
            critical ? DeviationSeverity.Critical : DeviationSeverity.Warning,
            $"Geloester Sauerstoff liegt bei {actual:0.0} mg/L.",
            critical
                ? "Unter 6 mg/L gilt Wurzelfaeule als bestaetigt (SOP-S1). Wurzeln pruefen, Belueftung und Wassertemperatur sofort kontrollieren."
                : "Unter 6,5 mg/L deutet auf erhoehte mikrobiologische Aktivitaet (SOP-N1). Belueftung, Umwaelzung und Wassertemperatur pruefen.",
            "do-critical",
            participants));
    }

    /// <summary>
    /// ORP gegen das Zielband des Grows — nicht gegen eine eigene Tabelle.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Anlass (01.09.2026).</b> Für ORP gab es <b>vier</b> Bänder
    /// nebeneinander: das Profil je Phase (Live-Kachel und Messprotokoll),
    /// fest verdrahtete 300/500 hier, 250/650 als kritische Grenzen und die
    /// Werte aus dem Wissen. Bei ORP 470 mV in der Blüte sagte die Kachel
    /// „daneben, Ziel 400–450" und zog zehn Punkte vom Score ab — die Diagnose
    /// fand nichts, weil 470 zwischen 300 und 500 liegt.</para>
    ///
    /// <para>Jetzt gilt dasselbe Band wie überall, genau wie bei
    /// <c>CheckEc</c>. Die harten 250/650 bleiben als <b>kritische</b>
    /// Grenzen: dort ist nicht mehr die Phase die Frage, sondern ob im Becken
    /// noch Sauerstoff ist.</para>
    /// </remarks>
    private static void CheckOrp(GrowRun grow, List<Measurement> sorted, HydroTargetValues? targets, List<GrowDeviation> result)
    {
        sorted = sorted.Where(measurement => measurement.OrpMv.HasValue).ToList();
        if (sorted.Count == 0 || sorted[0].OrpMv is not { } actual)
        {
            return;
        }

        // Ohne Profil bleiben die alten Werte — besser als gar kein Urteil.
        var min = targets?.OrpMin ?? 300;
        var max = targets?.OrpMax ?? 500;

        var critical = actual < 250 || actual > 650;
        var warning = actual < min || actual > max;
        if (!critical && !warning)
        {
            return;
        }

        var participants = Consecutive(sorted, measurement => measurement.OrpMv, value => value < min || value > max);
        result.Add(CreateDeviation(
            grow,
            "hydro.orp",
            DeviationMetric.Orp,
            actual,
            min,
            max,
            "mV",
            critical ? DeviationSeverity.Critical : DeviationSeverity.Warning,
            $"ORP {actual:0} mV liegt ausserhalb des Arbeitsbereichs ({min:0}–{max:0} mV).",
            "Wasserhygiene und Sensor plausibilisieren.",
            actual < min ? "orp-low-mild" : null,
            participants));
    }

    private static void CheckPpfd(GrowRun grow, List<Measurement> sorted, HydroTargetValues? targets, List<GrowDeviation> result)
    {
        sorted = sorted.Where(measurement => measurement.PpfdMol.HasValue).ToList();
        if (sorted.Count == 0 || sorted[0].PpfdMol is not { } actual)
        {
            return;
        }

        var critical = actual > 1500;
        var overTarget = targets is not null && actual > targets.PpfdMax * 1.2;

        // The plan's PPFD targets assume CO2 enrichment. If a reading shows there is none,
        // the ceiling is far lower — more light then means stress, not growth. That case
        // gets its own message because the advice is different and more concrete.
        var measuredCo2 = sorted[0].Co2Ppm;
        var withoutCo2 = measuredCo2 is { } co2 && co2 < Co2EnrichmentFrom;
        var overCeiling = withoutCo2 && actual > PpfdCeilingWithoutCo2;

        if (!critical && !overTarget && !overCeiling)
        {
            return;
        }

        if (overCeiling && !critical)
        {
            var ceilingParticipants = Consecutive(sorted, measurement => measurement.PpfdMol, value => value > PpfdCeilingWithoutCo2);
            result.Add(CreateDeviation(
                grow,
                "hydro.ppfd-no-co2",
                DeviationMetric.Ppfd,
                actual,
                targets?.PpfdMin,
                PpfdCeilingWithoutCo2,
                "umol/m2/s",
                DeviationSeverity.Warning,
                $"PPFD {actual:0} bei nur {measuredCo2:0} ppm CO2 — ohne CO2-Anreicherung sind 800-900 die Obergrenze.",
                "Licht in 50er-Schritten senken oder CO2 anheben. Mindestabstand Lampe-Spitzen 30 cm.",
                "led-bleaching-mild",
                ceilingParticipants));
            return;
        }

        var participants = Consecutive(sorted, measurement => measurement.PpfdMol, value => value > 1500 || (targets is not null && value > targets.PpfdMax * 1.2));
        result.Add(CreateDeviation(
            grow,
            "hydro.ppfd",
            DeviationMetric.Ppfd,
            actual,
            targets?.PpfdMin,
            targets?.PpfdMax,
            "umol/m2/s",
            critical ? DeviationSeverity.Critical : DeviationSeverity.Warning,
            $"PPFD {actual:0} liegt deutlich ueber dem Zielbereich.",
            "Lichtintensitaet oder Abstand pruefen.",
            "led-bleaching-mild",
            participants));
    }

    private static void CheckCo2(GrowRun grow, List<Measurement> sorted, List<GrowDeviation> result)
    {
        sorted = sorted.Where(measurement => measurement.Co2Ppm.HasValue).ToList();
        if (sorted.Count == 0 || sorted[0].Co2Ppm is not { } actual || actual <= 1600)
        {
            return;
        }

        var participants = Consecutive(sorted, measurement => measurement.Co2Ppm, value => value > 1600);
        result.Add(CreateDeviation(
            grow,
            "hydro.co2",
            DeviationMetric.Co2,
            actual,
            null,
            1600,
            "ppm",
            actual > 2500 ? DeviationSeverity.Critical : DeviationSeverity.Warning,
            $"CO2 {actual:0} ppm liegt ueber dem Arbeitsbereich.",
            "CO2-Zufuhr und Lueftung pruefen.",
            null,
            participants));
    }

    private static IReadOnlyList<Measurement> Consecutive(
        List<Measurement> sorted,
        Func<Measurement, double?> getValue,
        Func<double, bool> matches)
    {
        var result = new List<Measurement>();
        foreach (var measurement in sorted)
        {
            var value = getValue(measurement);
            if (!value.HasValue || !matches(value.Value))
            {
                break;
            }

            result.Add(measurement);
        }

        return result;
    }

    private static IReadOnlyList<Measurement> GetEcTrendParticipants(List<Measurement> sorted)
    {
        if (sorted.Count < 2 || !sorted[1].ReservoirEc.HasValue || !sorted[0].ReservoirEc.HasValue)
        {
            return Array.Empty<Measurement>();
        }

        var diff = sorted[0].ReservoirEc.GetValueOrDefault() - sorted[1].ReservoirEc.GetValueOrDefault();
        return Math.Abs(diff) > 0.2
            ? new[] { sorted[0], sorted[1] }
            : Array.Empty<Measurement>();
    }

    private static GrowDeviation CreateDeviation(
        GrowRun grow,
        string stableKey,
        DeviationMetric metric,
        double? actual,
        double? targetMin,
        double? targetMax,
        string? unit,
        DeviationSeverity severity,
        string message,
        string? recommendationHint,
        string? symptomId,
        IReadOnlyList<Measurement> sourceMeasurements)
    {
        var sourceIds = sourceMeasurements
            .Where(measurement => measurement.Id > 0)
            .Select(measurement => measurement.Id)
            .ToList();
        var firstDetected = sourceMeasurements.Count > 0
            ? sourceMeasurements.Min(measurement => measurement.TakenAt).ToUniversalTime()
            : (DateTime?)null;
        var lastDetected = sourceMeasurements.Count > 0
            ? sourceMeasurements.Max(measurement => measurement.TakenAt).ToUniversalTime()
            : (DateTime?)null;

        return new GrowDeviation
        {
            GrowId = grow.Id,
            GrowName = grow.Name,
            StableKey = stableKey,
            Metric = metric,
            ActualValue = actual,
            TargetMin = targetMin,
            TargetMax = targetMax,
            Unit = unit,
            Severity = severity,
            Message = message,
            RecommendationHint = recommendationHint,
            SymptomId = symptomId,
            SourceMeasurementIds = sourceIds,
            Recommendation = message,
            ConsecutiveCount = Math.Max(1, sourceMeasurements.Count),
            FirstDetectedAtUtc = firstDetected,
            LastDetectedAtUtc = lastDetected,
            Source = ResolveSource(sourceMeasurements)
        };
    }

    private static DeviationSource ResolveSource(IReadOnlyList<Measurement> measurements)
    {
        if (measurements.Count == 0)
        {
            return DeviationSource.Unknown;
        }

        if (measurements.All(measurement => measurement.Source == ValueOrigin.HomeAssistant))
        {
            return DeviationSource.HomeAssistant;
        }

        if (measurements.All(measurement => measurement.Source == ValueOrigin.Manual))
        {
            return DeviationSource.Manual;
        }

        if (measurements.All(measurement => measurement.Source is ValueOrigin.Manual or ValueOrigin.HomeAssistant))
        {
            return DeviationSource.Mixed;
        }

        return DeviationSource.Unknown;
    }
}
