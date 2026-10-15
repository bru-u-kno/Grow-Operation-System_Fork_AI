using System.ComponentModel.DataAnnotations;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Api.Contracts;

public sealed record RiskEventDto(
    int Id,
    RiskEventType EventType,
    RiskEventSeverity Severity,
    RiskEventStatus Status,
    RiskEventSource Source,
    string Title,
    string? Description,
    int? HardwareItemId,
    int? TentId,
    int? GrowId,
    int? TentSensorId,
    string? HaEntityId,
    int? SopInstanceId,
    int? GrowTaskId,
    DateTime StartedAtUtc,
    DateTime? LastSeenAtUtc,
    DateTime? ResolvedAtUtc,
    DateTime? AcknowledgedAtUtc,
    string? DedupeKey,
    string? RawValue,
    string? Notes,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    // Wahr, wenn der Abgleich das Risiko selbst geschlossen hat, weil die
    // Abweichung nicht mehr besteht (nicht: von Hand erledigt).
    bool ResolvedAutomatically = false
);

public sealed record RiskEventSopRecommendationDto(
    int RiskEventId,
    string RiskEventType,
    string Severity,
    string SopId,
    string SopName,
    string Reason,
    string Confidence,
    bool AlreadyActive,
    int? ActiveSopInstanceId
);

public sealed class CreateRiskEventRequest
{
    public RiskEventType EventType { get; set; } = RiskEventType.Other;
    public RiskEventSeverity Severity { get; set; } = RiskEventSeverity.Warning;
    public RiskEventStatus Status { get; set; } = RiskEventStatus.Open;
    public RiskEventSource Source { get; set; } = RiskEventSource.Manual;

    [Required]
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }
    public int? HardwareItemId { get; set; }
    public int? TentId { get; set; }
    public int? GrowId { get; set; }
    public int? TentSensorId { get; set; }
    public string? HaEntityId { get; set; }
    public int? SopInstanceId { get; set; }
    public int? GrowTaskId { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? LastSeenAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public DateTime? AcknowledgedAtUtc { get; set; }
    public string? DedupeKey { get; set; }
    public string? RawValue { get; set; }
    public string? Notes { get; set; }
}

public sealed class UpdateRiskEventRequest
{
    public RiskEventType EventType { get; set; } = RiskEventType.Other;
    public RiskEventSeverity Severity { get; set; } = RiskEventSeverity.Warning;
    public RiskEventStatus Status { get; set; } = RiskEventStatus.Open;
    public RiskEventSource Source { get; set; } = RiskEventSource.Manual;

    [Required]
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }
    public int? HardwareItemId { get; set; }
    public int? TentId { get; set; }
    public int? GrowId { get; set; }
    public int? TentSensorId { get; set; }
    public string? HaEntityId { get; set; }
    public int? SopInstanceId { get; set; }
    public int? GrowTaskId { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? LastSeenAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public DateTime? AcknowledgedAtUtc { get; set; }
    public string? DedupeKey { get; set; }
    public string? RawValue { get; set; }
    public string? Notes { get; set; }
}

public sealed class ResolveRiskEventRequest
{
    public DateTime? ResolvedAtUtc { get; set; }
    public string? Notes { get; set; }
}

public sealed class AcknowledgeRiskEventRequest
{
    public DateTime? AcknowledgedAtUtc { get; set; }
    public string? Notes { get; set; }
}

public sealed class StartRiskEventSopRequest
{
    public string SopId { get; set; } = string.Empty;
    public string? Notes { get; set; }
}
