namespace GrowDiary.Web.Models;

/// <summary>Fork AI (A-010, Etappe 2): Ein Minutenpunkt des Zelts mit der Lichtphase — die Grundlage des Zielabgleichs.</summary>
/// <param name="Tag"><c>true</c> bei Licht an, <c>false</c> bei Licht aus, <c>null</c> unbekannt.</param>
public sealed record ZeltMinute(DateTime ZeitUtc, double? Feuchte, double? Temp, double? Vpd, bool? Tag);

/// <summary>Fork AI (A-010): Die Ziele des Plans für eine Lichtphase. <c>null</c> = kein Ziel.</summary>
public sealed record ZielBand(double? FeuchteMax, double? TempMax, double? VpdMin, double? VpdMax);

public sealed record Zielbaender(ZielBand Tag, ZielBand Nacht);

/// <summary>Fork AI (A-010): Kann das System das Ziel halten?</summary>
public enum ZielUrteil
{
    /// <summary>Zu wenig Messzeit, um etwas zu sagen.</summary>
    Unbekannt = 0,
    Erreichbar = 1,
    Knapp = 2,
    Luecke = 3,
}

/// <summary>Fork AI (A-010): Ein Wert in einer Lichtphase: das Ziel, der Anteil der Zeit im Ziel, das Urteil.</summary>
public sealed record ZielPhase(string? ZielText, double? AnteilProzent, ZielUrteil Urteil, int Minuten);

public sealed record ZielZeile(string Groesse, ZielPhase Tag, ZielPhase Nacht);

/// <summary>Fork AI (A-010): Was ein Gerät in einer Lichtphase bewirkt, wenn es aus ist — gemittelt aus den Läufen.</summary>
public sealed record WirkungWert(string Groesse, double ProMinute, double? ErholungMinuten);

public sealed record WirkungZeile(string Modul, string Titel, bool Tag, int Laeufe, IReadOnlyList<WirkungWert> Werte);

public sealed record AbdeckungZeile(string Modul, string Titel, int LaeufeTag, int LaeufeNacht, bool TagMoeglich, bool NachtMoeglich);

public sealed record NaechsterLauf(string Modul, string Titel, bool Tag, int DauerMinuten, string Begruendung);

public sealed record Kenntnisstand(
    DateTime StandUtc,
    int TageBetrachtet,
    IReadOnlyList<ZielZeile> Zielabgleich,
    IReadOnlyList<WirkungZeile> Wirkung,
    IReadOnlyList<AbdeckungZeile> Abdeckung,
    NaechsterLauf? Naechster,
    IReadOnlyList<string> Hinweise);
