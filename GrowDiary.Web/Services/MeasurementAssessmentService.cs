using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services.Knowledge;

namespace GrowDiary.Web.Services;

/// <summary>Wie ein einzelner Messwert zu seinem Ziel steht.</summary>
public enum AssessmentVerdict
{
    /// <summary>Im Zielbereich.</summary>
    InTarget,

    /// <summary>Unter dem Ziel.</summary>
    Below,

    /// <summary>Über dem Ziel.</summary>
    Above,

    /// <summary>Es gibt kein Ziel, gegen das man prüfen könnte — mit Begründung.</summary>
    NoTarget,

    /// <summary>
    /// Den Wert kann es physikalisch nicht geben — 9000 °C Luft, EC 99999.
    /// </summary>
    /// <remarks>
    /// <para>Bis hierher lief das unter <see cref="NoTarget"/> mit, weil beides
    /// „zählt nicht in die Bilanz" bedeutet. In der Anzeige ist es aber das
    /// Gegenteil: „kein Ziel" heißt <i>unauffällig, hier gibt es nichts zu
    /// sehen</i>, „unmöglich" heißt <i>das Messgerät oder die Einheit stimmt
    /// nicht</i>. Beide gleich zu zeichnen hieß, dass im Protokoll eine Zeile
    /// mit EC 99.999 aussah wie jede andere.</para>
    /// <para>Gefunden hat es der Nutzer, nicht ein Test: „unlogische werte wird
    /// keine meldung teilweise gegeben". Die Live-Prüfung im Formular hatte die
    /// Grenzen da schon — das Protokoll nicht.</para>
    /// </remarks>
    Impossible,
}

/// <summary>Das Urteil zu einer Messgröße einer Messung.</summary>
/// <param name="Metric">Kurzname der Größe, z. B. „ph“ oder „ec“.</param>
/// <param name="Label">Deutscher Name für die Anzeige.</param>
/// <param name="Value">Der gemessene Wert.</param>
/// <param name="Unit">Einheit.</param>
/// <param name="TargetMin">Untere Grenze, gegen die geprüft wurde.</param>
/// <param name="TargetMax">Obere Grenze.</param>
/// <param name="Verdict">Das Urteil.</param>
/// <param name="Note">
/// Der Klartext dazu — bei <see cref="AssessmentVerdict.NoTarget"/> steht hier der
/// Grund, warum nicht geprüft wurde. Ein leeres Feld wäre schlimmer als keine
/// Spalte: es sähe aus wie „in Ordnung“.
/// </param>
public sealed record MetricAssessment(
    string Metric,
    string Label,
    double Value,
    string Unit,
    double? TargetMin,
    double? TargetMax,
    AssessmentVerdict Verdict,
    string Note
);

/// <summary>Das Urteil zu einer ganzen Messung.</summary>
public sealed record MeasurementAssessment(
    int MeasurementId,
    DateTime TakenAt,
    GrowStage StoredStage,
    GrowStage? ComputedStage,
    ValueOrigin Source,
    bool Excluded,
    string? ExcludedReason,
    IReadOnlyList<MetricAssessment> Metrics
);

/// <summary>Die Bilanz über alle Messungen eines Grows.</summary>
public sealed record MeasurementAssessmentReport(
    int MeasurementCount,
    int ExcludedCount,
    int CheckedValueCount,
    int InTargetCount,
    int OffTargetCount,
    /// <summary>Werte, die es physikalisch nicht geben kann.</summary>
    int ImpossibleCount,
    string ProfileId,
    string ProfileLabel,
    IReadOnlyList<MeasurementAssessment> Measurements
);

/// <summary>
/// Beurteilt GESPEICHERTE Messungen gegen die Sollwerte ihrer eigenen Phase.
///
/// <para><b>Wozu.</b> Die App konnte einen Wert bisher nur beim Eintippen
/// beurteilen (LiveCheckPanel) und danach nur noch die jüngste Messung
/// (DeviationAnalyzerService). Wer wissen wollte, ob sein Grow über die Wochen
/// im grünen Bereich lief, bekam eine Tabelle nackter Zahlen. Dieser Dienst
/// urteilt über jede Zeile.</para>
///
/// <para><b>Warum im Backend.</b> Die Profil-Kette, die Wissensbasis und der
/// Phasen-Rechner liegen hier. Im Browser nachgebaut wäre es die zweite
/// Wahrheit — genau der Fehler, den <see cref="UserTargets"/> beschreibt und
/// der zwischen Diagnose und Live-Kachel schon einmal passiert ist.</para>
///
/// <para><b>Was ausdrücklich NICHT gerechnet wird.</b> Keine Note je Messung
/// und keine für den Grow: es gibt keine Quelle dafür, wie pH gegen EC gegen
/// VPD zu gewichten wäre, und eine solche Zahl könnte niemand nachprüfen. Kein
/// Trend und keine Vorhersage: eine belegte Ratenregel gibt es nur für pH
/// (SOP-RDWC-CAN-N1 §2.1), und die wohnt im <see cref="SolutionStabilityAnalyzer"/>.
/// Hier gibt es Urteile je Wert und eine Abzählung, sonst nichts.</para>
/// </summary>
public sealed class MeasurementAssessmentService
{
    /// <summary>
    /// Der Arbeitsbereich für die Wassertemperatur — dieselben Zahlen wie in
    /// <see cref="DeviationAnalyzerService"/>.
    /// </summary>
    /// <remarks>
    /// <b>Bewusst NICHT das Phasenband.</b> Gemessen am echten Profil „Meine
    /// RDWC-Werte“: in der Veg-Phase sind Tag- und Nachtwert beide 20 °C, das
    /// Band ist also null breit. 19,7 und 20,3 wären beide rot — ein Protokoll,
    /// das bei jeder Messung schreit. Das Phasenband ist ein Ziel, kein
    /// Grenzwert; der Arbeitsbereich stammt aus
    /// App_Data/knowledge/guidance/water-temperature-band.json (SOP-RDWC-CAN-N1).
    /// </remarks>
    // Die Zahlen stehen in <see cref="Wasserband"/> — sie galten frueher hier
    // UND im DeviationAnalyzerService, also zweimal.

    private readonly TargetValueService _targetValues;
    private readonly AlertRuleRepository? _alertRules;

    private readonly KnowledgeBaseLoader? _wissen;

    public MeasurementAssessmentService(
        TargetValueService targetValues,
        AlertRuleRepository? alertRules = null,
        KnowledgeBaseLoader? wissen = null)
    {
        _targetValues = targetValues;
        _alertRules = alertRules;
        _wissen = wissen;
    }

    /// <param name="systemProfileId">
    /// Das Sollwert-Profil des Hydro-Systems — die mittlere Stufe der Kette
    /// Grow → System → Anbaustil. Der Aufrufer reicht sie durch, weil dieser
    /// Dienst als Singleton keinen Zugriff auf die Hydro-Ablage hat.
    /// </param>
    /// <param name="leafTempOffsetC">Blattversatz des Zelts, für das VPD.</param>
    public MeasurementAssessmentReport Assess(
        GrowRun grow,
        IReadOnlyList<Measurement> measurements,
        string? systemProfileId = null,
        double leafTempOffsetC = Tent.DefaultLeafTempOffsetC)
    {
        var profil = SetpointProfileResolver.Resolve(grow.SetpointProfileId, systemProfileId, grow.HydroStyle);
        var regeln = grow.TentId is { } tentId ? _alertRules?.GetForTent(tentId) : null;
        var phVomNutzer = UserTargets.For("reservoir-ph", regeln);

        // Die Wassertemperatur bekommt ihre eigene Grenze GETRENNT, nicht als
        // Teil des Zielbands: sonst steht die Untergrenze aus der Alarmregel in
        // WaterTempNightC und sieht dort aus wie eine Nachtabsenkung — das
        // Messprotokoll schrieb dann „nachts fährt deine Absenkung auf 15 °C"
        // über eine Regelung, die es nicht gibt.
        var eigeneTemp = UserTargets.For("reservoir-temp", regeln);

        // Der Wert, auf den die Absenkrampe faehrt. Er zieht die Untergrenze
        // des Arbeitsbereichs mit nach unten — sonst meldet die App ihre
        // eigene Regelung als Abweichung.
        var rampenBoden = Wasserband.RampenBodenC(
            grow,
            _targetValues.GetTargets(profil.ProfileId, GrowStage.Flower),
            _targetValues.GetTargets(profil.ProfileId, GrowStage.Finish));

        // Was außerhalb des Laufs liegt, ist ein Datenfehler und keine Messung.
        // Der Bestand enthält Zeilen mit 2099 und 1800 — beides Handeinträge aus
        // einem Test. Sie fließen nicht in die Bilanz ein, verschwinden aber
        // auch nicht: sie stehen mit Grund da.
        var frueheste = grow.StartDate.AddDays(-1);
        var spaeteste = DateTime.Now.AddHours(1);

        var zeilen = new List<MeasurementAssessment>();
        var geprueft = 0;
        var imZiel = 0;
        var daneben = 0;
        var unmoeglich = 0;
        var ausgeschlossen = 0;

        foreach (var messung in measurements.OrderByDescending(m => m.TakenAt).ThenByDescending(m => m.Id))
        {
            var gerechnet = GrowStageResolver.Resolve(grow, messung.TakenAt.Date);

            if (messung.TakenAt < frueheste || messung.TakenAt > spaeteste)
            {
                ausgeschlossen++;
                zeilen.Add(new MeasurementAssessment(
                    messung.Id, messung.TakenAt, messung.Stage, gerechnet, messung.Source,
                    true,
                    $"Zeitpunkt liegt ausserhalb des Laufs ({messung.TakenAt:dd.MM.yyyy}) — fliesst nicht in die Bilanz ein.",
                    Array.Empty<MetricAssessment>()));
                continue;
            }

            // Dieselbe Kette wie Kachel und Diagnose — inklusive Feedchart.
            // Zweimal: einmal ohne die eigenen Grenzen, weil die
            // Wassertemperatur sie getrennt braucht (siehe oben).
            // Die Wochenspalte des MESSTAGS, nicht die von heute.
            var ohneNutzer = Zielband.FuerGrow(
                _targetValues, _wissen, grow, messung.Stage, systemProfileId, null, messung.TakenAt.Date);
            var mitNutzer = ohneNutzer is null ? null : UserTargets.Overlay(ohneNutzer, regeln);

            var werte = new List<MetricAssessment>();
            PruefePh(messung, mitNutzer, phVomNutzer, werte);
            PruefeEc(messung, mitNutzer, werte);
            PruefeOrp(messung, mitNutzer, werte);
            PruefeWasserTemp(messung, ohneNutzer, rampenBoden, eigeneTemp, werte);
            PruefeVpd(messung, mitNutzer, leafTempOffsetC, werte);
            PruefeSauerstoff(messung, werte);
            PruefeOhneZielband(messung, werte);

            foreach (var wert in werte)
            {
                if (wert.Verdict == AssessmentVerdict.Impossible) { unmoeglich++; continue; }
                if (wert.Verdict == AssessmentVerdict.NoTarget) continue;
                geprueft++;
                if (wert.Verdict == AssessmentVerdict.InTarget) imZiel++;
                else daneben++;
            }

            zeilen.Add(new MeasurementAssessment(
                messung.Id, messung.TakenAt, messung.Stage, gerechnet, messung.Source,
                false, null, werte));
        }

        return new MeasurementAssessmentReport(
            measurements.Count, ausgeschlossen, geprueft, imZiel, daneben, unmoeglich,
            profil.ProfileId, ProfilName(profil.ProfileId), zeilen);
    }

    private static string ProfilName(string profileId)
        => profileId.StartsWith("custom:", StringComparison.Ordinal) ? "eigenes Profil" : profileId;

    /// <summary>
    /// pH — nach derselben Regel wie die Diagnose, nicht nach dem Phasenwert.
    /// </summary>
    /// <remarks>
    /// Der Phasenwert ist das ANMISCHZIEL, keine Schwelle: im RDWC darf der pH
    /// innerhalb der Komfortzone wandern, ab Blütewoche 4 sogar absichtlich.
    /// Gegen den Phasenwert geprüft wären im echten Bestand alle acht Messungen
    /// rot (5,95 bis 6,06) — und das Protokoll widerspräche der Diagnoseseite.
    ///
    /// Hat der Nutzer die Grenze aber selbst eingetragen, ist sie eine Ansage
    /// und gilt allein. Wer bewusst enger fährt, bekam sonst nichts zu sehen.
    /// </remarks>
    /// <remarks>
    /// Die Formel steht in <see cref="DeviationAnalyzerService.PhHandlungsbereich"/>
    /// und nur dort. Sie stand am 01.09.2026 <b>dreimal</b> — hier, in der
    /// Diagnose und auf der Live-Kachel — und war hier abgetippt.
    /// </remarks>
    private static void PruefePh(
        Measurement m, HydroTargetValues? ziele, (double? Min, double? Max)? vomNutzer,
        List<MetricAssessment> raus)
    {
        if (m.ReservoirPh is not { } wert) return;

        var (min, max) = DeviationAnalyzerService.PhHandlungsbereich(ziele, vomNutzer);

        // Das Anmischziel dazu, solange es nicht ohnehin die genannte Grenze ist.
        var anmischen = ziele is not null
                        && (Math.Abs(ziele.PhMin - min) > 0.001 || Math.Abs(ziele.PhMax - max) > 0.001)
            ? $" Anmischziel {ziele.PhMin:0.0}–{ziele.PhMax:0.0}."
            : string.Empty;
        var woher = vomNutzer is { } e && (e.Min is not null || e.Max is not null)
            ? "dein Grenzwert"
            : "Komfortzone";

        raus.Add(Urteil("ph", "pH", wert, string.Empty, min, max,
            $"{woher} {min:0.0}–{max:0.0}.{anmischen}"));
    }

    private static void PruefeEc(Measurement m, HydroTargetValues? ziele, List<MetricAssessment> raus)
    {
        if (m.ReservoirEc is not { } wert) return;
        if (ziele is null)
        {
            raus.Add(new MetricAssessment("ec", "EC", wert, "mS/cm", null, null,
                AssessmentVerdict.NoTarget, "Für diese Phase gibt es keine Sollwerte im Profil."));
            return;
        }

        raus.Add(Urteil("ec", "EC", wert, "mS/cm", ziele.EcMin, ziele.EcMax,
            $"Ziel {ziele.EcMin:0.00}–{ziele.EcMax:0.00} mS/cm."));
    }

    private static void PruefeOrp(Measurement m, HydroTargetValues? ziele, List<MetricAssessment> raus)
    {
        if (m.OrpMv is not { } wert) return;
        if (ziele is null)
        {
            raus.Add(new MetricAssessment("orp", "ORP", wert, "mV", null, null,
                AssessmentVerdict.NoTarget, "Für diese Phase gibt es keine Sollwerte im Profil."));
            return;
        }

        raus.Add(Urteil("orp", "ORP", wert, "mV", ziele.OrpMin, ziele.OrpMax,
            $"Ziel {ziele.OrpMin:0}–{ziele.OrpMax:0} mV."));
    }

    /// <summary>
    /// Die Wassertemperatur gegen ihren Arbeitsbereich.
    /// </summary>
    /// <remarks>
    /// Die Untergrenze kommt aus <see cref="Wasserband"/> und haengt am
    /// Nachtwert des Profils: seit die Nachtabsenkung nicht mehr nur geplant,
    /// sondern gefahren wird, meldete die App sonst ihre eigene Regelung als
    /// Abweichung.
    /// </remarks>
    private static void PruefeWasserTemp(
        Measurement m, HydroTargetValues? ziele, double? rampenBoden,
        (double? Min, double? Max)? eigene, List<MetricAssessment> raus)
    {
        if (m.ReservoirWaterTempC is not { } wert) return;
        var (unten, oben) = Wasserband.Grenzen(ziele, rampenBoden, eigene);
        raus.Add(Urteil("water-temp", "Wassertemperatur", wert, "°C",
            unten, oben,
            Wasserband.Begruendung(ziele, rampenBoden, eigene)));
    }

    /// <summary>
    /// VPD aus dem Luft-/Feuchte-Paar DERSELBEN Zeile.
    /// </summary>
    /// <remarks>
    /// Ein Urteil statt zweier abgeleiteter Bänder für Luft und Feuchte: die
    /// Live-Kacheln rechnen aus dem VPD-Ziel und dem jeweils anderen aktuellen
    /// Wert ein Band zurück, und das ergibt rückwirkend keinen Sinn.
    ///
    /// <b>Der Vorbehalt gehört an die Anzeige.</b> Live lässt die App das
    /// VPD-Ziel weg, wenn das Licht aus ist. Rückwirkend geht das nicht: der
    /// Lichtplan hat keine Historie, nur ein Kennzeichen für „aktiv“. Hier wird
    /// trotzdem geurteilt — die Alternative wäre, ein Drittel des Inhalts
    /// wegzuwerfen — und die Spalte sagt dazu, dass es das Tag-Ziel ist.
    /// </remarks>
    private static void PruefeVpd(Measurement m, HydroTargetValues? ziele, double blattversatz, List<MetricAssessment> raus)
    {
        if (VpdCalculator.Calculate(m.AirTemperatureC, m.HumidityPercent, blattversatz) is not { } wert) return;
        if (ziele is null)
        {
            raus.Add(new MetricAssessment("vpd", "VPD", wert, "kPa", null, null,
                AssessmentVerdict.NoTarget, "Für diese Phase gibt es keine Sollwerte im Profil."));
            return;
        }

        raus.Add(Urteil("vpd", "VPD", wert, "kPa", ziele.VpdMin, ziele.VpdMax,
            $"Tag-Ziel {ziele.VpdMin:0.00}–{ziele.VpdMax:0.00} kPa. Ob das Licht an war, ist rückwirkend nicht belegbar."));
    }

    /// <summary>
    /// Gelöster Sauerstoff — es gibt kein Profilfeld dafür, nur eine SOP-Schwelle.
    /// </summary>
    /// <remarks>
    /// Deshalb steht hier ausdrücklich der Grund und keine leere Zelle: „nicht
    /// gemessen“ und „nicht beurteilbar“ dürfen nie wie „in Ordnung“ aussehen.
    /// </remarks>
    private static void PruefeSauerstoff(Measurement m, List<MetricAssessment> raus)
    {
        if (m.DissolvedOxygenMgL is not { } wert) return;
        raus.Add(new MetricAssessment("do", "Gelöster Sauerstoff", wert, "mg/l", null, null,
            AssessmentVerdict.NoTarget,
            $"Kein Sollwert im Profil — SOP-Schwelle {DeviationAnalyzerService.DoActionThreshold:0.0} mg/l (SOP-RDWC-CAN-N1 §2.2)."));
    }

    /// <summary>
    /// Ein Wert gegen sein Ziel — es sei denn, er ist physikalisch unmöglich.
    /// </summary>
    /// <remarks>
    /// <b>Warum die Plausibilität zuerst kommt.</b> Ein erster Bau dieses
    /// Dienstes zählte EC 99999 und Wassertemperatur 5000 °C als ganz normale
    /// Abweichungen in die Bilanz — beides Testeinträge, die vor der Sperre
    /// hereingekommen waren. Damit stand über dem Protokoll eine Zahl, die zwei
    /// Ausreißer mitzählte und dadurch schlechter aussah, als der Grow lief.
    ///
    /// „Unmöglich“ ist kein Urteil über den Anbau, sondern über die Messung.
    /// Deshalb <see cref="AssessmentVerdict.NoTarget"/> mit ausgeschriebenem
    /// Grund und nicht „daneben“: der Wert wird nicht bewertet, er wird
    /// angezweifelt.
    /// </remarks>
    /// <summary>
    /// Größen, für die es kein Zielband gibt — aber eine physikalische Grenze.
    /// </summary>
    /// <remarks>
    /// <b>Warum sie überhaupt auftauchen.</b> Lufttemperatur, Feuchte, CO₂ und
    /// PPFD standen im Protokoll ganz ohne Aussage da — als wäre nichts geprüft
    /// worden. Genau so ist die −500 ppm wochenlang durchgekommen.
    ///
    /// <b>Warum sie kein Zielband bekommen.</b> Luft und Feuchte haben keins:
    /// die Live-Kacheln rechnen es aus dem VPD-Ziel und dem jeweils anderen
    /// aktuellen Wert zurück, und das ergibt rückwirkend keinen Sinn — das
    /// Urteil trägt das VPD. CO₂ hat ohne Anreicherung absichtlich kein Ziel,
    /// und PPFD hängt daran, ob das Licht an war; der Lichtplan hat keine
    /// Historie.
    ///
    /// Ein Wert, der die physikalische Grenze verlässt, wird trotzdem gemeldet.
    /// Die Bilanz-Zahlen ändern sich dadurch nicht: NoTarget zählt nicht mit.
    /// </remarks>
    private static void PruefeOhneZielband(Measurement m, List<MetricAssessment> raus)
    {
        Ansehen("air-temp", "Lufttemperatur", m.AirTemperatureC, "°C", "Kein eigenes Zielband — das Urteil trägt das VPD.");
        Ansehen("humidity", "Luftfeuchte", m.HumidityPercent, "%", "Kein eigenes Zielband — das Urteil trägt das VPD.");
        Ansehen("co2", "CO₂", m.Co2Ppm, "ppm", "Ohne CO₂-Anreicherung gibt es kein Ziel; Umgebungsluft liegt bei rund 420 ppm.");
        Ansehen("ppfd", "PPFD", m.PpfdMol, "µmol/m²/s", "Ob das Licht an war, ist rückwirkend nicht belegbar — der Lichtplan wird nicht historisiert.");

        void Ansehen(string metrik, string label, double? wert, string einheit, string grund)
        {
            if (wert is not { } v) return;
            var moeglich = MeasurementSanityService.IstPhysikalischMoeglich(metrik, v);
            var notiz = moeglich
                ? grund
                : $"Physikalisch nicht möglich ({MeasurementSanityService.PhysikalischeGrenzen[metrik].Min:0}–{MeasurementSanityService.PhysikalischeGrenzen[metrik].Max:0} {einheit}) — fliesst nicht in die Bilanz ein.";
            raus.Add(new MetricAssessment(metrik, label, v, einheit, null, null,
                moeglich ? AssessmentVerdict.NoTarget : AssessmentVerdict.Impossible, notiz));
        }
    }

    private static MetricAssessment Urteil(string metric, string label, double wert, string einheit, double min, double max, string notiz)
    {
        if (!MeasurementSanityService.IstPhysikalischMoeglich(metric, wert))
        {
            var grenzen = MeasurementSanityService.PhysikalischeGrenzen[metric];
            return new MetricAssessment(metric, label, wert, einheit, null, null, AssessmentVerdict.Impossible,
                $"Physikalisch nicht möglich ({grenzen.Min:0}–{grenzen.Max:0}{(einheit.Length > 0 ? " " + einheit : string.Empty)}) — fliesst nicht in die Bilanz ein.");
        }

        var urteil = wert < min ? AssessmentVerdict.Below
            : wert > max ? AssessmentVerdict.Above
            : AssessmentVerdict.InTarget;
        return new MetricAssessment(metric, label, wert, einheit, min, max, urteil, notiz);
    }
}
