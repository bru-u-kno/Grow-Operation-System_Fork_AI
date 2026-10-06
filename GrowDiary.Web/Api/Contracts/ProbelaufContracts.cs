using GrowDiary.Web.Models;

namespace GrowDiary.Web.Api.Contracts;

/// <summary>Fork AI (A-010): Eine Steuerung, an der ein Probelauf möglich ist — mit dem zentral gepflegten Titel.</summary>
public sealed record ProbelaufModulDto(string Modul, string Titel);

/// <summary>Fork AI (A-010): Rumpf von <c>POST /api/steuerung/probelauf</c>.</summary>
/// <param name="DauerMinuten"><c>null</c> = die Voreinstellung (20 Minuten).</param>
/// <param name="Grenzen"><c>null</c> = die Voreinstellung aus den Pflanzenzielen.</param>
public sealed record ProbelaufStartRequest(string? Modul, int? DauerMinuten, ProbelaufGrenzen? Grenzen);

/// <summary>Fork AI (A-010): Rumpf von <c>POST …/{id}/empfehlung</c> — die Empfehlung der KI zu einem Lauf.</summary>
public sealed record ProbelaufEmpfehlungRequest(string? Text);

/// <summary>Fork AI (A-010): Ein Lauf, wie ihn die Oberfläche braucht.</summary>
/// <param name="Status"><c>Laeuft</c>, <c>Nachlauf</c>, <c>Fertig</c>, <c>Abgebrochen</c> oder <c>RueckstellungOffen</c>.</param>
/// <param name="RestSekunden">Bis zum geplanten Ende des Eingriffs; 0, wenn er vorbei ist.</param>
/// <param name="Messreihe">Nur in der Einzelansicht — die Liste bleibt klein.</param>
public sealed record ProbelaufLaufDto(
    long Id,
    string Modul,
    string ModulTitel,
    string Status,
    DateTime StartUtc,
    DateTime GeplantesEndeUtc,
    DateTime? EingriffEndeUtc,
    DateTime? EndeUtc,
    int RestSekunden,
    string? AbbruchGrund,
    ProbelaufGrenzen Grenzen,
    ProbelaufAuswertung? Auswertung,
    string? Empfehlung,
    ProbelaufMessreihe? Messreihe);
