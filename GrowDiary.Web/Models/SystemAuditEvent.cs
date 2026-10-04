namespace GrowDiary.Web.Models;

public sealed class SystemAuditEvent
{
    public int Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
    public string Source { get; set; } = "backend";
    public string? RemoteAddress { get; set; }
    public int? RelatedGrowId { get; set; }
    public string? RelatedFileName { get; set; }
    public bool Success { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Fork AI (A-003, 03.10.2026): Bezug auf einen KI-Schlüssel und die Anfrage
    // dahinter — für „Was die KI zuletzt getan hat". Vorher stand das nur im
    // Satz (Summary); nach einem Schlüssel filtern liess sich so nicht. Alle
    // leer bei Einträgen, die keine Anfrage über einen Schlüssel sind, und bei
    // allen Einträgen aus forkai.163.

    /// <summary>Der Schlüssel, über den die Anfrage kam — null, wenn keiner bekannt ist (ungültig, Zugriff aus).</summary>
    public int? KiSchluesselId { get; set; }
    /// <summary>HTTP-Methode der Anfrage, etwa <c>POST</c>.</summary>
    public string? Methode { get; set; }
    /// <summary>Pfad der Anfrage ohne Abfrageteil.</summary>
    public string? Pfad { get; set; }
    /// <summary>Der Status der Antwort, soweit es eine gab.</summary>
    public int? HttpStatus { get; set; }
    /// <summary>Der Fehlercode der Antwort (<c>ki_stufe_fehlt</c> …), soweit bekannt.</summary>
    public string? Fehlercode { get; set; }
    /// <summary>
    /// Fork AI (Prüferbefund 04.10.2026): bei <c>POST /api/ki-ha/dienst</c> der Dienst
    /// und seine Entität, etwa <c>light.turn_on → light.zelt</c> — ohne <c>daten</c>.
    /// </summary>
    public string? HaDienst { get; set; }
}
