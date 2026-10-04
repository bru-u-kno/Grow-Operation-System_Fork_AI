using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Api.Mapping;

public static class JournalMapping
{
    public static JournalEntryDto ToDto(this JournalEntry entry) => new(
        Id: entry.Id,
        GrowId: entry.GrowId,
        MeasurementId: entry.MeasurementId,
        Title: entry.Title,
        Body: entry.Body,
        EntryType: entry.EntryType,
        Source: entry.Source,
        OccurredAtUtc: entry.OccurredAtUtc,
        CreatedAtUtc: entry.CreatedAtUtc,
        UpdatedAtUtc: entry.UpdatedAtUtc
    );
}
