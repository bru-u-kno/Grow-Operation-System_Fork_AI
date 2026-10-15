using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Models;
using GrowDiary.Web.Services;

namespace GrowDiary.Web.Api.Mapping;

public static class RiskEventMapping
{
    public static RiskEventDto ToDto(this RiskEvent item) => new(
        Id: item.Id,
        EventType: item.EventType,
        Severity: item.Severity,
        Status: item.Status,
        Source: item.Source,
        Title: item.Title,
        Description: item.Description,
        HardwareItemId: item.HardwareItemId,
        TentId: item.TentId,
        GrowId: item.GrowId,
        TentSensorId: item.TentSensorId,
        HaEntityId: item.HaEntityId,
        SopInstanceId: item.SopInstanceId,
        GrowTaskId: item.GrowTaskId,
        StartedAtUtc: item.StartedAtUtc,
        LastSeenAtUtc: item.LastSeenAtUtc,
        ResolvedAtUtc: item.ResolvedAtUtc,
        AcknowledgedAtUtc: item.AcknowledgedAtUtc,
        DedupeKey: item.DedupeKey,
        RawValue: item.RawValue,
        Notes: item.Notes,
        CreatedAtUtc: item.CreatedAtUtc,
        UpdatedAtUtc: item.UpdatedAtUtc,
        ResolvedAutomatically: item.Status == RiskEventStatus.Resolved
            && item.Source == RiskEventSource.Deviation
            && item.Notes?.Contains(DeviationRiskEventSyncService.AutoResolvedNote, StringComparison.Ordinal) == true
    );

    public static RiskEvent ToModel(this CreateRiskEventRequest request) => new()
    {
        EventType = request.EventType,
        Severity = request.Severity,
        Status = request.Status,
        Source = request.Source,
        Title = request.Title.Trim(),
        Description = NormalizeOptional(request.Description),
        HardwareItemId = request.HardwareItemId,
        TentId = request.TentId,
        GrowId = request.GrowId,
        TentSensorId = request.TentSensorId,
        HaEntityId = NormalizeOptional(request.HaEntityId),
        SopInstanceId = request.SopInstanceId,
        GrowTaskId = request.GrowTaskId,
        StartedAtUtc = request.StartedAtUtc ?? DateTime.UtcNow,
        LastSeenAtUtc = request.LastSeenAtUtc,
        ResolvedAtUtc = request.ResolvedAtUtc,
        AcknowledgedAtUtc = request.AcknowledgedAtUtc,
        DedupeKey = NormalizeOptional(request.DedupeKey),
        RawValue = NormalizeOptional(request.RawValue),
        Notes = NormalizeOptional(request.Notes)
    };

    public static void ApplyTo(this UpdateRiskEventRequest request, RiskEvent item)
    {
        item.EventType = request.EventType;
        item.Severity = request.Severity;
        item.Status = request.Status;
        item.Source = request.Source;
        item.Title = request.Title.Trim();
        item.Description = NormalizeOptional(request.Description);
        item.HardwareItemId = request.HardwareItemId;
        item.TentId = request.TentId;
        item.GrowId = request.GrowId;
        item.TentSensorId = request.TentSensorId;
        item.HaEntityId = NormalizeOptional(request.HaEntityId);
        item.SopInstanceId = request.SopInstanceId;
        item.GrowTaskId = request.GrowTaskId;
        item.StartedAtUtc = request.StartedAtUtc ?? item.StartedAtUtc;
        item.LastSeenAtUtc = request.LastSeenAtUtc;
        item.ResolvedAtUtc = request.ResolvedAtUtc;
        item.AcknowledgedAtUtc = request.AcknowledgedAtUtc;
        item.DedupeKey = NormalizeOptional(request.DedupeKey);
        item.RawValue = NormalizeOptional(request.RawValue);
        item.Notes = NormalizeOptional(request.Notes);
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
