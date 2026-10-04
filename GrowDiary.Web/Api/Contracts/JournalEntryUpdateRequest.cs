using GrowDiary.Web.Models;

namespace GrowDiary.Web.Api.Contracts;

/// <summary>Einen Journaleintrag nachträglich ändern.</summary>
/// <remarks>
/// Jedes Feld ist wahlweise: <c>null</c> heißt „bleibt wie es ist". Ein
/// Assistent, der nur eine vergessene Menge im Text nachträgt, schickt nur
/// <c>Body</c> — und verschiebt dabei nicht still den Zeitpunkt auf „jetzt"
/// oder die Art auf „Notiz". Ein leerer Text (<c>""</c>) leert das Feld.
/// <c>Source</c> fehlt bewusst: woher ein Eintrag stammt, ändert sich nicht,
/// wenn man ihn korrigiert.
/// </remarks>
public sealed class JournalEntryUpdateRequest
{
    public string? Title { get; set; }
    public string? Body { get; set; }
    public JournalEntryType? EntryType { get; set; }
    public string? OccurredAtLocal { get; set; }
}
