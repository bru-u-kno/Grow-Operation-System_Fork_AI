namespace GrowDiary.Web.Api.Contracts;

// Fork AI (A-003, 03.10.2026): Verträge für „Zugriff für KI-Assistenten".
// Stufen gehen als Liste von Namen über die Leitung ("Dokumentieren",
// "GrowPlanen", "GeraeteSchalten", "Verwaltung") — wie KiStufe heisst, ohne "Keine".

/// <summary>Die ganze Seite in den Einstellungen.</summary>
public sealed record KiZugriffSeiteDto(
    /// <summary>Hauptschalter. Aus: jeder Schlüssel wird abgewiesen, auch ein gültiger.</summary>
    bool Aktiv,
    /// <summary>Ab welcher Stufe der Assistent vorher nachfragen soll; null = nie.</summary>
    string? RueckfrageAbStufe,
    KiHoechstwerteDto Hoechstwerte,
    IReadOnlyList<KiSchluesselDto> Schluessel);

/// <summary>Grenzen für Befehle über einen Schlüssel — gelten zusätzlich zu den Grenzen der Geräte selbst.</summary>
public sealed record KiHoechstwerteDto(
    /// <summary>Höchstens so viele ml je Dosierbefehl. Die Grenze der Pumpe gilt zusätzlich.</summary>
    double MaxDosisMlJeBefehl,
    /// <summary>Höchstens so viele Schalt- und Dosierbefehle je Stunde über alle Schlüssel.</summary>
    int MaxSchaltbefehleJeStunde);

public sealed record KiSchluesselDto(
    int Id,
    string Name,
    /// <summary>Die ersten Zeichen des Schlüssels, zum Wiedererkennen — nie der ganze.</summary>
    string Praefix,
    IReadOnlyList<string> Stufen,
    DateTime ErstelltAmUtc,
    DateTime? ZuletztGenutztAmUtc,
    DateTime? GesperrtAmUtc);

/// <summary>PUT /api/settings/ki-zugriff</summary>
public sealed class KiZugriffSpeichernRequest
{
    public bool Aktiv { get; set; }
    public string? RueckfrageAbStufe { get; set; }
    public KiHoechstwerteDto? Hoechstwerte { get; set; }
}

/// <summary>POST /api/settings/ki-zugriff/schluessel und PUT …/schluessel/{id}</summary>
public sealed class KiSchluesselRequest
{
    public string? Name { get; set; }
    /// <summary>
    /// Fork AI (A-003, 03.10.2026): null = nicht angegeben (beim Anlegen gilt dann die
    /// Vorbelegung Dokumentieren); eine ausdrücklich leere Liste ist ein Fehler.
    /// </summary>
    public List<string>? Stufen { get; set; }
}

/// <summary>Antwort auf das Anlegen — der einzige Moment, in dem der Schlüssel im Klartext existiert.</summary>
public sealed record KiSchluesselAngelegtDto(KiSchluesselDto Schluessel, string Klartext);

/// <summary>GET /api/ki-zugriff/ich — was der anfragende Schlüssel darf.</summary>
public sealed record KiZugriffIchDto(
    string SchluesselName,
    IReadOnlyList<string> Stufen,
    string? RueckfrageAbStufe,
    KiHoechstwerteDto Hoechstwerte);

/// <summary>
/// Fork AI (A-003, 03.10.2026): Ein Eintrag in „Was die KI zuletzt getan hat" —
/// GET /api/settings/ki-zugriff/protokoll.
/// </summary>
/// <remarks>
/// Aus dem Prüfprotokoll (Quelle <c>ki-zugriff</c>), nur was ein Assistent
/// ausgelöst hat: schreibende Anfragen, Lesen auf Verwaltungswegen,
/// Abweisungen und die Sicherung vor einer Aktion. Lesende Anfragen ausserhalb
/// der Verwaltungswege stehen nie darin — die protokolliert die Sperre nicht.
/// </remarks>
public sealed record KiProtokollEintragDto(
    int Id,
    DateTime ZeitpunktUtc,
    /// <summary>Der Schlüssel; null bei ungültigem Schlüssel, ausgeschaltetem Zugriff und bei Einträgen aus forkai.163.</summary>
    int? SchluesselId,
    /// <summary>Der heutige Name des Schlüssels; bei einem gelöschten der Name aus dem Eintrag, sonst null.</summary>
    string? SchluesselName,
    string? Methode,
    string? Pfad,
    /// <summary>HTTP-Status der Antwort; null, wenn der Eintrag keine Antwort beschreibt (Sicherung vorher, Adresse gesperrt).</summary>
    int? Status,
    /// <summary>Fehlercode der Antwort, etwa <c>ki_stufe_fehlt</c>; null bei Erfolg oder wenn unbekannt.</summary>
    string? Fehlercode,
    /// <summary>Ausgeführt (Status unter 400) — false heisst abgewiesen oder gescheitert.</summary>
    bool Erfolg,
    /// <summary>Die Art des Eintrags (<c>KiProtokollArt</c>) — ein Bezeichner, nicht zum Anzeigen.</summary>
    string Art,
    /// <summary>Der Satz aus dem Prüfprotokoll, deutsch.</summary>
    string Beschreibung);
