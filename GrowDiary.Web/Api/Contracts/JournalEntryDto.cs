using GrowDiary.Web.Models;

namespace GrowDiary.Web.Api.Contracts;

public sealed record JournalEntryDto(
    int Id,
    int GrowId,
    int? MeasurementId,
    string? Title,
    string? Body,
    JournalEntryType EntryType,
    ValueOrigin Source,
    DateTime OccurredAtUtc,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc = null
);
