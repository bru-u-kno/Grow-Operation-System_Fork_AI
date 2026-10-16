using System.Globalization;
using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Infrastructure;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Services;

public sealed class DeviationRiskEventSyncService
{
    private const string DedupePrefix = "deviation:grow:";

    /// <summary>
    /// Vermerk an einem Risiko, das der Abgleich selbst geschlossen hat, weil die
    /// Abweichung nicht mehr besteht — im Gegensatz zu „Erledigt" von Hand. Die
    /// Oberflaeche zeigt solche Karten als „wieder okay".
    /// </summary>
    public const string AutoResolvedNote = "Deviation aktuell nicht mehr erkannt.";

    private readonly GrowRepository _repository;
    private readonly DeviationAnalyzerService _deviationAnalyzer;
    private readonly TreatmentRecommender _treatmentRecommender;

    public DeviationRiskEventSyncService(
        GrowRepository repository,
        DeviationAnalyzerService deviationAnalyzer,
        TreatmentRecommender treatmentRecommender)
    {
        _repository = repository;
        _deviationAnalyzer = deviationAnalyzer;
        _treatmentRecommender = treatmentRecommender;
    }

    public int SyncActiveGrowDeviations()
    {
        var changed = 0;
        foreach (var grow in _repository.GetActiveGrows())
        {
            changed += SyncGrow(grow);
        }

        return changed;
    }

    // Der Abgleich laeuft aus dem Wachter im Minutentakt UND aus Seitenaufrufen. Das
    // Anlegen einer Karte ist „suchen, dann einfuegen": zwei gleichzeitige Laeufe
    // koennten fuer dieselbe Abweichung zwei offene Karten anlegen.
    private readonly object _sperre = new();

    public int SyncGrow(GrowRun grow)
    {
        lock (_sperre)
        {
            return SyncGrowOhneSperre(grow);
        }
    }

    private int SyncGrowOhneSperre(GrowRun grow)
    {
        var measurements = _repository.GetMeasurementsForGrow(grow.Id);
        var deviations = _deviationAnalyzer
            .Analyze(grow, measurements, _repository.LeafOffsetFor(grow), _repository.SystemProfileIdFor(grow), _repository.GetChangeoutsForGrow(grow.Id).ToList())
            .Where(IsActionable)
            .GroupBy(deviation => deviation.StableKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(deviation => deviation.Severity).First())
            .ToList();
        var recommendations = _treatmentRecommender.Recommend(grow, deviations).Recommendations
            .GroupBy(recommendation => recommendation.DeviationStableKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        // Was der Betreiber abgehakt hat, und wann.
        //
        // Aus dem Feld gemeldet: „Diese WasserTemp-Abweichung bekomme ich nicht
        // weg, egal ob ich Erledigt oder Bestätigt markiere." Der Grund stand
        // hier: die Dedup-Suche kennt nur offene Ereignisse, ein erledigtes fand
        // sie nicht — und legte beim nächsten Durchlauf ein neues an, weil die
        // Abweichung aus derselben unveränderten Messung weiterhin folgte. Der
        // Knopf tat sichtbar nichts.
        //
        // Jetzt gilt: erledigt heisst erledigt, BIS es neue Daten gibt. Kommt
        // eine Messung, die dieselbe Abweichung erneut zeigt, meldet sie sich
        // wieder — sie ist dann ja auch wieder eine Neuigkeit.
        var erledigtAm = _repository.GetRiskEventsByGrow(grow.Id)
            .Where(risk => risk.Source == RiskEventSource.Deviation)
            .Where(risk => risk.Status == RiskEventStatus.Resolved)
            .Where(risk => risk.ResolvedAtUtc is not null && !string.IsNullOrWhiteSpace(risk.DedupeKey))
            .GroupBy(risk => risk.DedupeKey!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                gruppe => gruppe.Key,
                gruppe => gruppe.Max(risk => risk.ResolvedAtUtc!.Value.ToUniversalTime()),
                StringComparer.OrdinalIgnoreCase);

        var changed = 0;
        foreach (var deviation in deviations)
        {
            var zuletztGesehen = (deviation.LastDetectedAtUtc ?? DateTime.UtcNow).ToUniversalTime();
            if (erledigtAm.TryGetValue(DedupeKey(grow.Id, deviation.StableKey), out var abgehakt)
                && abgehakt >= zuletztGesehen)
            {
                continue;
            }

            recommendations.TryGetValue(deviation.StableKey, out var recommendation);
            var beforeCount = _repository.GetRiskEvents().Count;
            _repository.CreateRiskEvent(ToRiskEvent(grow, deviation, recommendation));
            if (_repository.GetRiskEvents().Count != beforeCount)
            {
                changed++;
            }
        }

        changed += ResolveStaleDeviationRisks(grow.Id, deviations.Select(deviation => DedupeKey(grow.Id, deviation.StableKey)).ToHashSet(StringComparer.OrdinalIgnoreCase));
        return changed;
    }

    private int ResolveStaleDeviationRisks(int growId, ISet<string> currentDedupeKeys)
    {
        var stale = _repository.GetRiskEventsByGrow(growId)
            .Where(risk => risk.Source == RiskEventSource.Deviation)
            .Where(risk => risk.Status is RiskEventStatus.Open or RiskEventStatus.Acknowledged)
            .Where(risk => !string.IsNullOrWhiteSpace(risk.DedupeKey))
            .Where(risk => risk.DedupeKey!.StartsWith($"{DedupePrefix}{growId}:", StringComparison.OrdinalIgnoreCase))
            .Where(risk => !currentDedupeKeys.Contains(risk.DedupeKey!))
            .ToList();

        foreach (var risk in stale)
        {
            _repository.ResolveRiskEvent(risk.Id, DateTime.UtcNow, AutoResolvedNote);
        }

        return stale.Count;
    }

    private static bool IsActionable(GrowDeviation deviation)
        => deviation.Severity is DeviationSeverity.Critical or DeviationSeverity.Warning;

    private static RiskEvent ToRiskEvent(GrowRun grow, GrowDeviation deviation, TreatmentRecommendationDto? recommendation)
    {
        var now = DateTime.UtcNow;
        var action = recommendation?.TreatmentName ?? recommendation?.SopTitle ?? deviation.RecommendationHint ?? deviation.Recommendation;
        var description = string.IsNullOrWhiteSpace(action)
            ? deviation.Message
            : $"{deviation.Message} Handlung: {action}";

        return new RiskEvent
        {
            EventType = ToRiskEventType(deviation),
            Severity = deviation.Severity == DeviationSeverity.Critical ? RiskEventSeverity.Critical : RiskEventSeverity.Warning,
            Status = RiskEventStatus.Open,
            Source = RiskEventSource.Deviation,
            Title = $"{ToMetricLabel(deviation.Metric)}: {ToSeverityLabel(deviation.Severity)}",
            Description = description,
            TentId = grow.TentId,
            GrowId = grow.Id,
            StartedAtUtc = deviation.FirstDetectedAtUtc ?? now,
            LastSeenAtUtc = deviation.LastDetectedAtUtc ?? now,
            DedupeKey = DedupeKey(grow.Id, deviation.StableKey),
            RawValue = FormatRawValue(deviation),
            Notes = $"Grow: {grow.Name}"
        };
    }

    private static RiskEventType ToRiskEventType(GrowDeviation deviation)
        => deviation.Metric switch
        {
            DeviationMetric.DissolvedOxygen when deviation.Severity == DeviationSeverity.Critical => RiskEventType.CriticalDo,
            DeviationMetric.Ppfd => RiskEventType.LightMismatch,
            _ => RiskEventType.Other
        };

    private static string DedupeKey(int growId, string stableKey)
        => $"{DedupePrefix}{growId}:{stableKey}";

    private static string FormatRawValue(GrowDeviation deviation)
    {
        var actual = deviation.ActualValue?.ToString("0.##", CultureInfo.InvariantCulture) ?? "-";
        var target = deviation.TargetMin.HasValue && deviation.TargetMax.HasValue
            ? $"{deviation.TargetMin.Value.ToString("0.##", CultureInfo.InvariantCulture)}-{deviation.TargetMax.Value.ToString("0.##", CultureInfo.InvariantCulture)}"
            : deviation.TargetMin.HasValue
                ? $">={deviation.TargetMin.Value.ToString("0.##", CultureInfo.InvariantCulture)}"
                : deviation.TargetMax.HasValue
                    ? $"<={deviation.TargetMax.Value.ToString("0.##", CultureInfo.InvariantCulture)}"
                    : "-";
        return $"actual={actual};target={target};unit={deviation.Unit ?? ""}";
    }

    private static string ToSeverityLabel(DeviationSeverity severity)
        => severity == DeviationSeverity.Critical ? "kritische Abweichung" : "Abweichung prüfen";

    /// <summary>
    /// Der Name einer Messgröße, wie ein Mensch ihn liest.
    /// </summary>
    /// <remarks>
    /// <para>Vorher stand der Enum-Name im Titel. Auf der Aufgabenseite — der
    /// Seite, die man mit dem Telefon im Zelt aufhat — las der Nutzer deshalb
    /// „Ec: Abweichung prüfen". Je nach Messwert wären dort auch „Ph", „Vpd",
    /// „Co2", „WaterTemp" oder „DissolvedOxygen" erschienen.</para>
    ///
    /// <para>Groß- und Kleinschreibung ist hier fachlich, nicht kosmetisch: pH
    /// ist nicht PH, und EC schreibt sich groß. Ein unbekannter Wert wird
    /// durchgereicht statt verschluckt — ein englisches Wort ist besser als
    /// ein leerer Titel.</para>
    /// </remarks>
    public static string ToMetricLabel(DeviationMetric metric) => metric switch
    {
        DeviationMetric.Ph => "pH",
        DeviationMetric.Ec => "EC",
        DeviationMetric.Orp => "ORP",
        DeviationMetric.WaterTemp => "Wassertemperatur",
        DeviationMetric.Vpd => "VPD",
        DeviationMetric.Ppfd => "PPFD",
        DeviationMetric.Co2 => "CO₂",
        DeviationMetric.DissolvedOxygen => "Gelöster Sauerstoff",
        DeviationMetric.GerminationStatus => "Keimung",
        _ => metric.ToString(),
    };
}
