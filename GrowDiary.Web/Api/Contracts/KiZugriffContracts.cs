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
    public List<string> Stufen { get; set; } = [];
}

/// <summary>Antwort auf das Anlegen — der einzige Moment, in dem der Schlüssel im Klartext existiert.</summary>
public sealed record KiSchluesselAngelegtDto(KiSchluesselDto Schluessel, string Klartext);

/// <summary>GET /api/ki-zugriff/ich — was der anfragende Schlüssel darf.</summary>
public sealed record KiZugriffIchDto(
    string SchluesselName,
    IReadOnlyList<string> Stufen,
    string? RueckfrageAbStufe,
    KiHoechstwerteDto Hoechstwerte);
