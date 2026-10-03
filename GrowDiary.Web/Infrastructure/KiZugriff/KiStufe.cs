namespace GrowDiary.Web.Infrastructure.KiZugriff;

/// <summary>
/// Fork AI (A-003, 03.10.2026): Was ein KI-Assistent mit einem Schlüssel tun darf.
/// </summary>
/// <remarks>
/// <para>Der Betreiber hakt die Stufen je Schlüssel einzeln an — sie bauen
/// nicht aufeinander auf. Wer nur „Geräte schalten" freigibt, darf damit
/// keine Messung anlegen.</para>
///
/// <para>Die Zahlen sind Bits und stehen so in der Datenbank
/// (<c>ForkKiSchluessel.Stufen</c>). Nie umnummerieren.</para>
/// </remarks>
[Flags]
public enum KiStufe
{
    Keine = 0,

    /// <summary>Einträge: Messungen, Journal, Beobachtungen, Aufgaben, Wartung, Kosten, Meldungen quittieren.</summary>
    Dokumentieren = 1,

    /// <summary>Was die App regelt und wonach sie warnt: Phase, Zielwerte, Mischplan, Lichtplan, Pflanzen, Sorten.</summary>
    GrowPlanen = 2,

    /// <summary>Wirkt sofort auf Pflanzen und Wasser: Licht, Pumpen, Dosierpumpen, Geräte.</summary>
    GeraeteSchalten = 4,

    /// <summary>Einstellungen, Sicherungen, Import/Export, Löschen von Stammdaten.</summary>
    Verwaltung = 8,
}

/// <summary>
/// Welche Stufe ein Schlüssel für diese schreibende Aktion braucht.
/// </summary>
/// <remarks>
/// Steht am Controller und gilt für alle seine schreibenden Aktionen; ein
/// Attribut an der Aktion selbst gewinnt. Eine schreibende Aktion ohne
/// Einstufung ist über einen Schlüssel <b>gesperrt</b> — vergessen sperrt,
/// statt zu öffnen. <c>KiStufenVollstaendigTests</c> zählt, dass keine fehlt.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class KiStufeAttribute(KiStufe stufe) : Attribute
{
    public KiStufe Stufe { get; } = stufe;
}

/// <summary>
/// Diese Aktion ist über einen Schlüssel nie erreichbar — egal welche Stufen er hat.
/// </summary>
/// <remarks>
/// Für Wege, die nur ein Mensch in der Oberfläche gehen soll, allen voran die
/// Schlüsselverwaltung selbst: sonst könnte sich ein Schlüssel eigene Stufen
/// geben oder einen zweiten erzeugen.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class KeinKiZugriffAttribute(string grund) : Attribute
{
    public string Grund { get; } = grund;
}

/// <summary>
/// Bevor ein Schlüssel diese Aktion ausführt, legt Grow OS eine Sicherung an.
/// </summary>
/// <remarks>
/// Für alles, was sich nicht von Hand zurückdrehen lässt: Sicherung
/// zurückspielen, Import, Löschen. Gilt nur für Anfragen über einen Schlüssel —
/// in der Oberfläche entscheidet der Mensch selbst.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class KiSicherungVorherAttribute : Attribute
{
}
