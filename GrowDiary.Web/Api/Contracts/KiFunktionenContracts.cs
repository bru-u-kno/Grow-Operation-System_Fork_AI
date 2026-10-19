namespace GrowDiary.Web.Api.Contracts;

// Fork AI (Paket D, 18.10.2026): Das Funktionsverzeichnis für KI-Assistenten —
// GET /api/ki-zugriff/funktionen. Erzeugt zur Laufzeit aus den Routen
// (KiFunktionsverzeichnis), nichts davon ist von Hand gepflegt.

/// <summary>Alle Funktionen von Cultivate Control unter <c>/api</c>, je Methode ein Eintrag.</summary>
public sealed record KiFunktionenDto(
    /// <summary>Wie viele Einträge <see cref="Funktionen"/> hat.</summary>
    int Anzahl,
    /// <summary>Wie die Felder zu lesen sind — in einem Satz für den Assistenten.</summary>
    string Hinweis,
    IReadOnlyList<KiFunktionDto> Funktionen);

/// <summary>Eine Funktion: Methode und Weg, ihre Einstufung für einen Schlüssel und was sie erwartet.</summary>
public sealed record KiFunktionDto(
    /// <summary>GET, POST, PUT, PATCH oder DELETE.</summary>
    string Methode,
    /// <summary>Der Weg mit Platzhaltern ohne Einschränkungen, etwa <c>/api/plants/{id}</c>.</summary>
    string Pfad,
    /// <summary>Der erste Teil nach <c>/api/</c>, etwa <c>kosten</c> — zum Eingrenzen.</summary>
    string Bereich,
    /// <summary>lesen, anlegen, ändern oder löschen (nach der Methode).</summary>
    string Art,
    /// <summary>frei (jeder gültige Schlüssel), stufe (die Stufen in <see cref="Stufen"/>), nie (mit <see cref="NieGrund"/>) oder nicht-eingestuft (gesperrt).</summary>
    string Zugang,
    /// <summary>Alle Stufen, die ein Schlüssel zusammen braucht — Leitungsnamen wie in <c>/api/ki-zugriff/ich</c>.</summary>
    IReadOnlyList<string> Stufen,
    /// <summary>Der Weg ist ein Verwaltungsweg: auch lesend nur mit Verwaltung.</summary>
    bool Verwaltungsweg,
    /// <summary>Warum diese Funktion über einen Schlüssel nie erreichbar ist.</summary>
    string? NieGrund,
    /// <summary>Zählt gegen den Höchstwert der Schaltbefehle je Stunde.</summary>
    bool Schaltbefehl,
    /// <summary>Cultivate Control legt vorher eine Sicherung an.</summary>
    bool SicherungVorher,
    /// <summary>Kurzbeschreibung aus der Code-Doku, sonst Controller und Aktion.</summary>
    string Beschreibung,
    IReadOnlyList<KiParameterDto> Routenparameter,
    IReadOnlyList<KiParameterDto> Abfrage,
    /// <summary>Der erwartete JSON-Rumpf, oder null.</summary>
    KiRumpfDto? Rumpf,
    /// <summary>Erwartet eine hochgeladene Datei (Formular) — über einen Assistenten nicht vorgesehen.</summary>
    bool DateiHochladen,
    /// <summary>Antwortet mit einer Datei oder einem Bild statt JSON.</summary>
    bool LiefertDatei);

/// <summary>Ein Weg- oder Abfrageparameter.</summary>
public sealed record KiParameterDto(string Name, string Typ, bool Pflicht);

/// <summary>Der JSON-Rumpf einer Funktion: Typname und Felder (verschachtelte Objekte im Typtext).</summary>
public sealed record KiRumpfDto(string Typ, IReadOnlyList<KiFeldDto> Felder);

/// <summary>Ein Feld im Rumpf, unter seinem JSON-Namen.</summary>
public sealed record KiFeldDto(string Name, string Typ);
