using GrowDiary.Web.Api.Contracts;
using Microsoft.AspNetCore.Http;

namespace GrowDiary.Web.Infrastructure.KiZugriff;

/// <summary>
/// Fork AI (A-003): Diese Anfrage kam über einen geprüften Schlüssel.
/// </summary>
/// <remarks>
/// Die Sperre vor dem Routing legt ihn in <see cref="HttpContext.Items"/>, sobald
/// der Schlüssel gültig ist. Wer ihn findet, weiss: kein Mensch in der
/// Oberfläche, sondern ein Assistent — und welche Grenzen für ihn gelten.
/// Die Höchstwerte sind ein Abzug zum Zeitpunkt der Prüfung, damit ein
/// Controller sie nicht selbst aus den Einstellungen lesen muss.
/// </remarks>
/// <param name="Rueckfrage">
/// Fork AI (A-005, 03.10.2026): Bei diesen Stufen soll der Assistent vorher
/// fragen — eine Bitte, durchsetzen kann Grow OS nur <paramref name="Stufen"/>.
/// </param>
public sealed record KiZugriffKontext(
    int SchluesselId,
    string SchluesselName,
    KiStufe Stufen,
    KiHoechstwerteDto Hoechstwerte,
    KiStufe Rueckfrage = KiStufe.Keine)
{
    public const string ItemKey = "GrowOs.KiZugriff";

    /// <summary>Der Kontext dieser Anfrage, oder null, wenn sie nicht über einen Schlüssel kam.</summary>
    public static KiZugriffKontext? Aus(HttpContext? context)
        => context?.Items.TryGetValue(ItemKey, out var wert) == true ? wert as KiZugriffKontext : null;

    public bool Darf(KiStufe stufe) => stufe != KiStufe.Keine && (Stufen & stufe) == stufe;
}
