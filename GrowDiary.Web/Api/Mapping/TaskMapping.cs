using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Api.Mapping;

public static class TaskMapping
{
    public static GrowTaskDto ToDto(this GrowTask task) => new(
        Id: task.Id,
        GrowId: task.GrowId,
        GrowName: task.GrowName,
        Title: task.Title,
        Notes: task.Notes,
        DueAtUtc: task.DueAtUtc,
        Priority: task.Priority,
        Status: task.Status,
        CreatedAtUtc: task.CreatedAtUtc,
        CompletedAtUtc: task.CompletedAtUtc,
        CreatedByAssistant: task.CreatedByAssistant
    );
}
