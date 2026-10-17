namespace GrowDiary.Web.Models;

public sealed class GrowTask
{
    public int Id { get; set; }
    public int GrowId { get; set; }
    public string? GrowName { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime? DueAtUtc { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;
    public GrowTaskStatus Status { get; set; } = GrowTaskStatus.Open;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>Name des KI-Schlüssels, über den die Aufgabe angelegt wurde; <c>null</c> = von Hand in der Oberfläche.</summary>
    public string? CreatedByAssistant { get; set; }
}
