using GrowDiary.Web.Services;

namespace GrowDiary.Web.Api.Contracts;

/// <summary>Eine offene Erinnerung an eine Phasen-Bestätigung — siehe <see cref="Phasenerinnerung"/>.</summary>
public sealed record PhasenerinnerungDto(
    string Art,
    string Aktion,
    string Knopf,
    string Text,
    DateTime Ab,
    int Tage);

/// <summary>
/// Phase, Beginne und Woche eines Laufs — aus dem <see cref="Phasenanker"/>.
/// </summary>
/// <remarks>
/// Die Oberfläche rechnet damit nichts mehr selbst: der Zeitstrahl liest die
/// Beginne von hier, statt aus Keimdatum und 14 Tagen eine eigene Vegi zu
/// schätzen (bis zum 02.10.2026 tat er genau das).
/// </remarks>
public sealed record PhasenankerDto(
    string Phase,
    string? Anzucht,
    string Stufe,
    DateTime AnzuchtAb,
    DateTime? VegAb,
    DateTime? BlueteAb,
    DateTime? FinishAb,
    DateTime? EndeAm,
    int TagInPhase,
    int WocheInPhase,
    PhasenerinnerungDto? Erinnerung)
{
    public static PhasenankerDto Aus(Phasenstand stand) => new(
        stand.Phase.ToString(),
        stand.Anzucht?.ToString(),
        stand.Stufe.ToString(),
        Tag(stand.AnzuchtAb),
        Tag(stand.VegAb),
        Tag(stand.BlueteAb),
        Tag(stand.FinishAb),
        Tag(stand.EndeAm),
        stand.TagInPhase,
        stand.WocheInPhase,
        stand.Erinnerung is { } e ? new PhasenerinnerungDto(e.Art, e.Aktion, e.Knopf, e.Text, Tag(e.Ab), e.Tage) : null);

    /// <summary>
    /// Ein Kalendertag ohne Zeitzone. Die Anker stammen aus zwei Speicherwegen
    /// (VegStartedAt über UTC, FlipDate als Datum) — ohne Angleichen kam einer
    /// mit Zeitzonen-Angabe, der andere ohne. Ein Tag ist ein Tag.
    /// </summary>
    private static DateTime Tag(DateTime wert) => DateTime.SpecifyKind(wert.Date, DateTimeKind.Unspecified);

    private static DateTime? Tag(DateTime? wert) => wert is { } w ? Tag(w) : null;
}
