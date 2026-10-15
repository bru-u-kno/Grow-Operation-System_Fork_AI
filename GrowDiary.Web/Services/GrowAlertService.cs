using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

public sealed class GrowAlertService
{
    private readonly GrowRepository _repository;
    private readonly RecommendationEngine _recommendationEngine;
    private readonly DeviationAnalyzerService _deviationAnalyzer;
    private readonly TreatmentRecommender _treatmentRecommender;

    public GrowAlertService(
        GrowRepository repository,
        RecommendationEngine recommendationEngine,
        DeviationAnalyzerService deviationAnalyzer,
        TreatmentRecommender treatmentRecommender)
    {
        _repository = repository;
        _recommendationEngine = recommendationEngine;
        _deviationAnalyzer = deviationAnalyzer;
        _treatmentRecommender = treatmentRecommender;
    }

    public IReadOnlyList<RecommendationCard> BuildAlertsForGrow(GrowRun grow, int? maxCount = null)
    {
        var latest = _repository.GetLatestMeasurement(grow.Id);
        var previous = latest is null ? null : _repository.GetPreviousMeasurement(grow.Id, latest.TakenAt, latest.Id);
        var measurements = _repository.GetMeasurementsForGrow(grow.Id);
        // Beide Belege zaehlen: das Haekchen an einer Messung UND der Eintrag
        // aus dem Formular „Wasserwechsel". Bis zum 31.08.2026 stand hier nur
        // der erste — wer den Wechsel eintrug, raeumte damit keine Mahnung weg.
        var lastSolutionChangeAt = Wasserwechsel.ZuletztOrtszeit(
            measurements, _repository.GetChangeoutsForGrow(grow.Id));
        var legacyAlerts = _recommendationEngine.Evaluate(grow, latest, previous, lastSolutionChangeAt);

        /* Nur noch "gibt es ueberhaupt eine Messung". Die beiden anderen
           Glieder (IrrigationType == ActiveHydro, Profile.IsHydro) waren
           konstant wahr — IrrigationType hat einen Wert, IsHydro ist "=> true".
           Sie lasen sich wie eine Wahl und waren keine. */
        if (latest is not null)
        {
            var deviations = _deviationAnalyzer.Analyze(grow, measurements, _repository.LeafOffsetFor(grow), _repository.SystemProfileIdFor(grow), _repository.GetChangeoutsForGrow(grow.Id).ToList());
            var treatmentRecommendations = _treatmentRecommender.Recommend(grow, deviations).Recommendations;
            var diagnosticAlerts = _recommendationEngine.BuildCardsFromDiagnostics(deviations, treatmentRecommendations);
            var mergedAlerts = MergeDiagnosticAndLegacyAlerts(diagnosticAlerts, legacyAlerts);
            return ApplyMaxCount(mergedAlerts, maxCount);
        }

        return ApplyMaxCount(legacyAlerts, maxCount);
    }

    public static string ResolveStateToneFromDeviations(IEnumerable<GrowDeviation> deviations, bool homeAssistantConfigured)
    {
        if (deviations.Any(deviation => deviation.Severity == DeviationSeverity.Critical))
        {
            return "critical";
        }

        if (deviations.Any(deviation => deviation.Severity == DeviationSeverity.Warning))
        {
            return "attention";
        }

        return homeAssistantConfigured ? "healthy" : "neutral";
    }

    /// <summary>Aus den Karten eines Grows wird die Zustandsampel.</summary>
    /// <remarks>
    /// <para><b>Wer das liest.</b> <c>/api/live/tents/{id}</c> liefert
    /// <c>stateTone</c> und <c>stateLabel</c> — und <b>keine Seite der
    /// Oberfläche liest sie</b> (Stand 02.09.2026). Die Ampel, die der Nutzer
    /// sieht, rechnet der Browser selbst (<c>live-model.ts</c>). Titel und Text
    /// der Karten werden ebenfalls gerechnet und weggeworfen.</para>
    ///
    /// <para>Das ist keine Kleinigkeit: damit hat <c>RecommendationEngine</c>
    /// — 852 Zeilen — heute keine sichtbare Wirkung. Festgehalten von
    /// <c>ZweiAmpelnFuerDasselbeZeltTests</c>.</para>
    ///
    /// <para>Verglichen wird gegen <see cref="Kartenschwere"/> und nicht gegen
    /// rohe Zeichenketten. Bis zum 02.09.2026 standen hier <c>"danger"</c> und
    /// <c>"warning"</c> als Literale — ein Tippfehler oder ein neuer Wert
    /// hätte aus einem kritischen Befund still ein „stabil" gemacht.</para>
    /// </remarks>
    public static string ResolveStateTone(IEnumerable<RecommendationCard> alerts, bool homeAssistantConfigured)
    {
        var schweren = alerts.Select(a => a.Severity).ToList();

        if (schweren.Contains(Kartenschwere.Gefahr))
        {
            return "critical";
        }

        if (schweren.Contains(Kartenschwere.Warnung))
        {
            return "attention";
        }

        return homeAssistantConfigured ? "healthy" : "neutral";
    }

    public static string ResolveStateLabel(string tone)
        => tone switch
        {
            "critical" => "kritisch",
            "attention" => "beobachten",
            "healthy" => "stabil",
            _ => "neutral"
        };

    private static IReadOnlyList<RecommendationCard> ApplyMaxCount(IReadOnlyList<RecommendationCard> alerts, int? maxCount)
        => maxCount is > 0 ? alerts.Take(maxCount.Value).ToList() : alerts;

    private static IReadOnlyList<RecommendationCard> MergeDiagnosticAndLegacyAlerts(
        IReadOnlyList<RecommendationCard> diagnosticAlerts,
        IReadOnlyList<RecommendationCard> legacyAlerts)
    {
        var legacyHasWarning = legacyAlerts.Any(IsWarningOrDanger);
        var merged = diagnosticAlerts
            .Where(alert => !(legacyHasWarning && alert.Severity == "success"))
            .ToList();
        var seenTitles = new HashSet<string>(merged.Select(alert => NormalizeTitle(alert.Title)), StringComparer.OrdinalIgnoreCase);
        var diagnosticProblemKeys = new HashSet<string>(
            merged.Where(IsWarningOrDanger).Select(alert => GetProblemKey(alert.Title)),
            StringComparer.OrdinalIgnoreCase);
        var hasDiagnosticSuccess = merged.Any(alert => alert.Severity == "success");

        foreach (var legacyAlert in legacyAlerts)
        {
            var normalizedTitle = NormalizeTitle(legacyAlert.Title);
            if (!seenTitles.Add(normalizedTitle))
            {
                continue;
            }

            if (legacyAlert.Severity == "success" && hasDiagnosticSuccess)
            {
                continue;
            }

            if (IsWarningOrDanger(legacyAlert) && diagnosticProblemKeys.Contains(GetProblemKey(legacyAlert.Title)))
            {
                continue;
            }

            merged.Add(legacyAlert);
            if (legacyAlert.Severity == "success")
            {
                hasDiagnosticSuccess = true;
            }
        }

        return merged;
    }

    private static bool IsWarningOrDanger(RecommendationCard alert)
        => alert.Severity is "danger" or "warning";

    private static string GetProblemKey(string title)
    {
        var normalized = NormalizeTitle(title);
        if (normalized.Contains("loesungswechsel") || normalized.Contains("wasserwechsel") || normalized.Contains("wechsel"))
        {
            return "solution-change";
        }

        if (normalized.Contains("wassertemperatur") || normalized.Contains("wassertemp") || normalized.Contains("watertemp"))
        {
            return "water-temp";
        }

        if (normalized.Contains("reservoirph") || normalized.Contains("ph"))
        {
            return "ph";
        }

        if (normalized.Contains("reservoirect") || normalized.Contains("ec"))
        {
            return "ec";
        }

        if (normalized.Contains("sauerstoff") || normalized.Contains("dissolvedoxygen") || normalized.Contains("do"))
        {
            return "do";
        }

        if (normalized.Contains("orp"))
        {
            return "orp";
        }

        if (normalized.Contains("ppfd") || normalized.Contains("licht"))
        {
            return "ppfd";
        }

        if (normalized.Contains("co2"))
        {
            return "co2";
        }

        return normalized;
    }

    private static string NormalizeTitle(string title)
        => new(title
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
}
