using GrowDiary.Web.Models;

namespace GrowDiary.Web.Api.Contracts;

public sealed record GrowTaskDto(
    int Id,
    int GrowId,
    string? GrowName,
    string Title,
    string? Notes,
    DateTime? DueAtUtc,
    TaskPriority Priority,
    GrowTaskStatus Status,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    string? CreatedByAssistant
);
