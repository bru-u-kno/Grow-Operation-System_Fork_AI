using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services.Knowledge;
using GrowDiary.Web.Services.Knowledge.Schema;

namespace GrowDiary.Web.Services;

public sealed class TreatmentRecommender
{
    private readonly KnowledgeBaseLoader _knowledgeBase;

    public TreatmentRecommender(KnowledgeBaseLoader knowledgeBase)
    {
        _knowledgeBase = knowledgeBase;
    }

    public GrowTreatmentRecommendationDto Recommend(GrowRun grow, IReadOnlyList<GrowDeviation> deviations)
    {
        var symptoms = _knowledgeBase.Symptoms.ToDictionary(symptom => symptom.Id, StringComparer.OrdinalIgnoreCase);
        var treatments = _knowledgeBase.Treatments.ToDictionary(treatment => treatment.Id, StringComparer.OrdinalIgnoreCase);
        var sops = _knowledgeBase.Sops.ToDictionary(sop => sop.Id, StringComparer.OrdinalIgnoreCase);
        var recommendations = new List<TreatmentRecommendationDto>();

        foreach (var deviation in deviations)
        {
            var symptomId = ResolveExistingSymptomId(deviation, symptoms);
            if (symptomId is null)
            {
                recommendations.Add(CreateFallbackRecommendation(deviation));
                continue;
            }

            var symptom = symptoms[symptomId];
            foreach (var treatmentId in symptom.SuggestedTreatmentIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (treatments.TryGetValue(treatmentId, out var treatment))
                {
                    recommendations.Add(CreateTreatmentRecommendation(deviation, symptom, treatment));
                }
            }

            foreach (var sopId in symptom.SuggestedSopIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (sops.TryGetValue(sopId, out var sop))
                {
                    recommendations.Add(CreateSopRecommendation(deviation, symptom, sop));
                }
            }

            if (symptom.SuggestedTreatmentIds.Count == 0 && symptom.SuggestedSopIds.Count == 0)
            {
                recommendations.Add(CreateSymptomOnlyRecommendation(deviation, symptom));
            }
        }

        return new GrowTreatmentRecommendationDto(grow.Id, recommendations);
    }

    private static string? ResolveExistingSymptomId(
        GrowDeviation deviation,
        IReadOnlyDictionary<string, SymptomDefinition> symptoms)
    {
        if (!string.IsNullOrWhiteSpace(deviation.SymptomId) && symptoms.ContainsKey(deviation.SymptomId))
        {
            return deviation.SymptomId;
        }

        var mappedId = MapDeviationToSymptomId(deviation);
        return mappedId is not null && symptoms.ContainsKey(mappedId)
            ? mappedId
            : null;
    }

    private static string? MapDeviationToSymptomId(GrowDeviation deviation)
    {
        var actual = deviation.ActualValue;
        return deviation.Metric switch
        {
            DeviationMetric.Ph when actual.HasValue && deviation.TargetMax.HasValue && actual > deviation.TargetMax => "ph-too-high",
            DeviationMetric.Ph when actual.HasValue && deviation.TargetMin.HasValue && actual < deviation.TargetMin => "ph-too-low",
            DeviationMetric.Ec when IsEcFalling(deviation) => "ec-falling-high-consumption",
            DeviationMetric.Ec when IsEcRising(deviation) => "ec-rising-rejection",
            DeviationMetric.Ec when actual.HasValue && deviation.TargetMax.HasValue && actual > deviation.TargetMax => "ec-rising-rejection",
            DeviationMetric.Ec when actual.HasValue && deviation.TargetMin.HasValue && actual < deviation.TargetMin => "ec-falling-high-consumption",
            DeviationMetric.WaterTemp when actual.HasValue && actual > (deviation.TargetMax ?? 22) => "water-temp-rising-rapid",
            DeviationMetric.DissolvedOxygen => "do-critical",
            DeviationMetric.Orp when actual.HasValue && actual < (deviation.TargetMin ?? 300) => "orp-low-mild",
            DeviationMetric.Ppfd when actual.HasValue && actual > (deviation.TargetMax ?? 0) => "led-bleaching-mild",
            _ => null
        };
    }

    private static bool IsEcFalling(GrowDeviation deviation)
        => ContainsIgnoreCase(deviation.Message, "gefallen") || ContainsIgnoreCase(deviation.Recommendation, "gefallen");

    private static bool IsEcRising(GrowDeviation deviation)
        => ContainsIgnoreCase(deviation.Message, "gestiegen") || ContainsIgnoreCase(deviation.Recommendation, "gestiegen");

    private static bool ContainsIgnoreCase(string? value, string pattern)
        => value?.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;

    private static TreatmentRecommendationDto CreateTreatmentRecommendation(
        GrowDeviation deviation,
        SymptomDefinition symptom,
        TreatmentDefinition treatment)
    {
        var safetyNotes = new List<string>();
        safetyNotes.AddRange(treatment.Restrictions);

        if (treatment.PhaseFilter is not null)
        {
            safetyNotes.Add("PhaseFilter vorhanden; aktuelle Grow-Phase ist für D2 nicht eindeutig prüfbar.");
        }

        if (treatment.HardwareRequirements.Count > 0)
        {
            safetyNotes.Add($"Hardware beachten: {string.Join(", ", treatment.HardwareRequirements)}");
        }

        if (treatment.Conflicts.Count > 0)
        {
            safetyNotes.AddRange(treatment.Conflicts.Select(conflict =>
                $"Konflikt mit {conflict.With}: {conflict.Reason}"));
        }

        var conflictIds = treatment.Conflicts
            .Select(conflict => conflict.With)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new TreatmentRecommendationDto(
            StableKey: $"{deviation.StableKey}:treatment:{treatment.Id}",
            DeviationStableKey: deviation.StableKey,
            Metric: deviation.Metric,
            Severity: deviation.Severity,
            SymptomId: symptom.Id,
            TreatmentId: treatment.Id,
            TreatmentName: treatment.Name,
            SopId: null,
            SopTitle: null,
            Confidence: ResolveConfidence(deviation),
            // Der Nutzer liest diesen Satz. „Deviation" und „Knowledge-Symptom"
            // sind unsere Woerter fuer unsere Bauteile, nicht seine.
            Reason: $"Die Abweichung „{deviation.Message}\" passt zum Krankheitsbild „{symptom.Name}\".",
            SafetyNotes: safetyNotes,
            SourceDocumentIds: ToSourceDocumentIds(treatment.Sources),
            Conflicts: treatment.Conflicts.Select(conflict => conflict.Reason).Where(reason => !string.IsNullOrWhiteSpace(reason)).ToList(),
            ConflictTreatmentIds: conflictIds,
            PhaseAllowed: treatment.PhaseFilter is null ? true : null,
            HardwareRequirements: treatment.HardwareRequirements);
    }

    private static TreatmentRecommendationDto CreateSopRecommendation(
        GrowDeviation deviation,
        SymptomDefinition symptom,
        SopDefinition sop)
        => new(
            StableKey: $"{deviation.StableKey}:sop:{sop.Id}",
            DeviationStableKey: deviation.StableKey,
            Metric: deviation.Metric,
            Severity: deviation.Severity,
            SymptomId: symptom.Id,
            TreatmentId: null,
            TreatmentName: null,
            SopId: sop.Id,
            SopTitle: sop.Name,
            Confidence: ResolveConfidence(deviation),
            Reason: $"Die Abweichung „{deviation.Message}\" passt zum Krankheitsbild „{symptom.Name}\".",
            SafetyNotes: sop.RequiredMaterials.Count > 0
                ? new[] { $"Material beachten: {string.Join(", ", sop.RequiredMaterials)}" }
                : Array.Empty<string>(),
            SourceDocumentIds: ToSourceDocumentIds(sop.Sources),
            Conflicts: Array.Empty<string>(),
            ConflictTreatmentIds: Array.Empty<string>(),
            PhaseAllowed: null,
            HardwareRequirements: sop.RequiredMaterials);

    private static TreatmentRecommendationDto CreateSymptomOnlyRecommendation(GrowDeviation deviation, SymptomDefinition symptom)
        => new(
            StableKey: $"{deviation.StableKey}:symptom:{symptom.Id}",
            DeviationStableKey: deviation.StableKey,
            Metric: deviation.Metric,
            Severity: deviation.Severity,
            SymptomId: symptom.Id,
            TreatmentId: null,
            TreatmentName: null,
            SopId: null,
            SopTitle: null,
            Confidence: ResolveConfidence(deviation),
            Reason: $"Das Krankheitsbild „{symptom.Name}\" passt, aber dafür ist weder eine Behandlung noch ein Ablauf hinterlegt.",
            SafetyNotes: symptom.DiagnosticChecks,
            SourceDocumentIds: Array.Empty<string>(),
            Conflicts: Array.Empty<string>(),
            ConflictTreatmentIds: Array.Empty<string>(),
            PhaseAllowed: null,
            HardwareRequirements: Array.Empty<string>());

    private static TreatmentRecommendationDto CreateFallbackRecommendation(GrowDeviation deviation)
        => new(
            StableKey: $"{deviation.StableKey}:diagnostic",
            DeviationStableKey: deviation.StableKey,
            Metric: deviation.Metric,
            Severity: deviation.Severity,
            SymptomId: null,
            TreatmentId: null,
            TreatmentName: null,
            SopId: null,
            SopTitle: null,
            Confidence: ResolveConfidence(deviation),
            Reason: "Zu dieser Abweichung kennt Cultivate Control kein Krankheitsbild — hier hilft nur der Blick auf die Pflanze.",
            SafetyNotes: Array.Empty<string>(),
            SourceDocumentIds: Array.Empty<string>(),
            Conflicts: Array.Empty<string>(),
            ConflictTreatmentIds: Array.Empty<string>(),
            PhaseAllowed: null,
            HardwareRequirements: Array.Empty<string>());

    private static TreatmentRecommendationConfidence ResolveConfidence(GrowDeviation deviation)
        => deviation.Severity == DeviationSeverity.Critical || deviation.ConsecutiveCount >= 3
            ? TreatmentRecommendationConfidence.High
            : deviation.Severity == DeviationSeverity.Warning
                ? TreatmentRecommendationConfidence.Medium
                : TreatmentRecommendationConfidence.Low;

    private static IReadOnlyList<string> ToSourceDocumentIds(IReadOnlyList<KnowledgeSource> sources)
        => sources
            .Select(source => source.Url ?? source.Reference ?? source.Title)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
