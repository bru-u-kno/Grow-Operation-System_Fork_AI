namespace GrowDiary.Web.Infrastructure.KiZugriff;

/// <summary>
/// Fork AI (A-003, 03.10.2026): Die Arten von Prüfprotokoll-Einträgen, die eine
/// Anfrage <b>über einen Schlüssel</b> hinterlässt (<c>SystemAuditEvent.Action</c>,
/// Quelle <see cref="Quelle"/>).
/// </summary>
/// <remarks>
/// <para>Geschrieben werden sie nur von <see cref="KiZugriffSperre"/>. Unter
/// derselben Quelle stehen auch die Handgriffe des Betreibers in der
/// Schlüsselverwaltung (angelegt, gesperrt, Hauptschalter …) — die gehören
/// nicht in „Was die KI zuletzt getan hat", denn das hat der Mensch getan.
/// Deshalb liest die Liste nur die Arten hier.</para>
///
/// <para>Lesende Anfragen über einen Schlüssel kommen nicht ins Protokoll —
/// ausser auf Verwaltungswegen (<see cref="VerwaltungLesend"/>).</para>
/// </remarks>
public static class KiProtokollArt
{
    public const string Quelle = "ki-zugriff";

    /// <summary>Eine schreibende Anfrage, die durch Schritt 1 kam — erledigt, abgewiesen oder gescheitert.</summary>
    public const string Schreibend = "ki-zugriff-schreibend";
    /// <summary>Ein Lesen auf einem Verwaltungsweg (<c>AdminAccessPolicy.IsAdminPath</c>).</summary>
    public const string VerwaltungLesend = "ki-zugriff-verwaltung-lesend";
    /// <summary>Ein Schlüssel kam, der Hauptschalter war aus.</summary>
    public const string ZugriffAus = "ki-zugriff-aus";
    /// <summary>Ungültiger oder gesperrter Schlüssel.</summary>
    public const string SchluesselAbgewiesen = "ki-schluessel-abgewiesen";
    /// <summary>Zu viele ungültige Schlüssel — die Adresse ist ab jetzt gesperrt.</summary>
    public const string AdresseGesperrt = "ki-adresse-gesperrt";
    /// <summary>Vor einer Aktion mit <see cref="KiSicherungVorherAttribute"/> eine Sicherung angelegt.</summary>
    public const string SicherungVorher = "ki-sicherung-vorher";

    /// <summary>Alle Arten, die ein Assistent auslöst.</summary>
    public static IReadOnlyList<string> VomAssistenten { get; } =
        [Schreibend, VerwaltungLesend, ZugriffAus, SchluesselAbgewiesen, AdresseGesperrt, SicherungVorher];
}

/// <summary>
/// Fork AI (A-003, 03.10.2026): Was ein Protokolleintrag über die Anfrage dahinter festhält.
/// </summary>
/// <param name="SchluesselId">Der Schlüssel — null, wenn keiner erkannt wurde.</param>
/// <param name="Status">Der Status der Antwort; null, wenn der Eintrag keine Antwort beschreibt.</param>
/// <param name="Fehlercode">Der Code der Fehlerantwort, soweit bekannt.</param>
/// <param name="HaDienst">
/// Fork AI (Prüferbefund 04.10.2026): Bei <c>POST /api/ki-ha/dienst</c> der gerufene
/// Dienst und seine Entität, etwa <c>light.turn_on → light.zelt</c> — nie die
/// <c>daten</c>. Sonst stand im Protokoll nur, DASS in Home Assistant geschaltet
/// wurde, nicht WAS.
/// </param>
public sealed record KiAnfrage(int? SchluesselId, int? Status, string? Fehlercode, string? HaDienst = null);
