namespace GrowDiary.Web.Models;

public sealed class JournalEntry
{
    public int Id { get; set; }
    public int GrowId { get; set; }
    public int? MeasurementId { get; set; }
    public string? Title { get; set; }
    public string? Body { get; set; }
    public JournalEntryType EntryType { get; set; } = JournalEntryType.Note;
    public ValueOrigin Source { get; set; } = ValueOrigin.Manual;
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    /// <summary>Wann der Eintrag zuletzt geändert wurde; <c>null</c> = nie.</summary>
    public DateTime? UpdatedAtUtc { get; set; }
}
