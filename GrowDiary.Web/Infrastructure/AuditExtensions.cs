using GrowDiary.Web.Models;

namespace GrowDiary.Web.Infrastructure;

public static class AuditExtensions
{
    public static void LogMeasurementCreated(this AuditRepository repo, int growId, int measurementId, GrowStage stage, DateTime takenAt, ValueOrigin source)
        => repo.Add(new AuditEntry
        {
            GrowId = growId,
            EntityType = "Measurement",
            EntityId = measurementId,
            Action = "Messung gespeichert",
            Summary = $"{stage} am {takenAt:dd.MM.yyyy HH:mm} ({source})."
        });

    public static void LogMeasurementUpdated(this AuditRepository repo, int growId, int measurementId, DateTime takenAt)
        => repo.Add(new AuditEntry
        {
            GrowId = growId,
            EntityType = "Measurement",
            EntityId = measurementId,
            Action = "Messung geändert",
            Summary = $"Messung vom {takenAt:dd.MM.yyyy HH:mm} aktualisiert."
        });

    public static void LogMeasurementDeleted(this AuditRepository repo, int growId, int measurementId, DateTime takenAt)
        => repo.Add(new AuditEntry
        {
            GrowId = growId,
            EntityType = "Measurement",
            EntityId = measurementId,
            Action = "Messung gelöscht",
            Summary = $"Messung vom {takenAt:dd.MM.yyyy HH:mm} entfernt."
        });

    public static void LogTaskCreated(this AuditRepository repo, int growId, int taskId, string title)
        => repo.Add(new AuditEntry
        {
            GrowId = growId,
            EntityType = "Task",
            EntityId = taskId,
            Action = "Aufgabe erstellt",
            Summary = $"Task \"{title}\" angelegt."
        });

    public static void LogTaskStatusChanged(this AuditRepository repo, int growId, int taskId, string title, GrowTaskStatus status)
        => repo.Add(new AuditEntry
        {
            GrowId = growId,
            EntityType = "Task",
            EntityId = taskId,
            Action = status switch
            {
                GrowTaskStatus.Done    => "Aufgabe erledigt",
                GrowTaskStatus.Skipped => "Aufgabe übersprungen",
                _                      => "Aufgabe geöffnet"
            },
            Summary = title
        });

    public static void LogTaskDeleted(this AuditRepository repo, int growId, int taskId, string title)
        => repo.Add(new AuditEntry
        {
            GrowId = growId,
            EntityType = "Task",
            EntityId = taskId,
            Action = "Aufgabe gelöscht",
            Summary = title
        });

    public static void LogJournalCreated(this AuditRepository repo, int growId, int entryId, string? title, JournalEntryType entryType)
        => repo.Add(new AuditEntry
        {
            GrowId = growId,
            EntityType = "JournalEntry",
            EntityId = entryId,
            Action = "Journal aktualisiert",
            Summary = title ?? entryType.ToString()
        });

    public static void LogJournalUpdated(this AuditRepository repo, int growId, int entryId, string? title, JournalEntryType entryType)
        => repo.Add(new AuditEntry
        {
            GrowId = growId,
            EntityType = "JournalEntry",
            EntityId = entryId,
            Action = "Journal geändert",
            Summary = $"'{title ?? entryType.ToString()}' wurde geändert."
        });

    public static void LogPhotosUploaded(this AuditRepository repo, int growId, int measurementId, int count)
        => repo.Add(new AuditEntry
        {
            GrowId = growId,
            EntityType = "Photo",
            EntityId = measurementId,
            Action = "Fotos hochgeladen",
            Summary = $"{count} Foto(s) für Messung #{measurementId} gespeichert."
        });

    public static void LogHarvestCreated(this AuditRepository repo, int growId, string harvestedAt)
        => repo.Add(new AuditEntry
        {
            GrowId = growId,
            EntityType = "Harvest",
            Action = "Ernte dokumentiert",
            Summary = $"Ernte am {harvestedAt} eingetragen."
        });
}
