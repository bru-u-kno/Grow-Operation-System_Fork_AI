using GrowDiary.Web.Models;

namespace GrowDiary.Web.Api.Contracts;

/// <summary>
/// Ein Nachfüllen als ein Vorgang (A-006, Etappe 3): Addback-Eintrag, Messung
/// vorher und nachher, Verbrauch und Tagebuchzeile mit einem Speichern.
/// </summary>
/// <remarks>
/// Messwerte, Buchungen und Tagebuchzeile haben dieselbe Form wie beim
/// Wasserwechsel (<see cref="VorgangMessungRequest"/>, <see cref="VorgangBuchungRequest"/>,
/// <see cref="VorgangTagebuchRequest"/>) — und dieselbe Prüfung.
/// </remarks>
public sealed class AddbackVorgangRequest
{
    /// <summary>Wann nachgefüllt wurde, Ortszeit <c>yyyy-MM-ddTHH:mm</c>. Leer = jetzt.</summary>
    public string? ZeitpunktLokal { get; set; }

    /// <summary><c>Addback</c> = Wasser mit Dünger, <c>TopOff</c> = nur Wasser, <c>Correction</c> = Korrektur.</summary>
    public AddbackLogKind Art { get; set; } = AddbackLogKind.Addback;

    /// <summary>Nachgefüllte Liter — Pflicht und größer 0; nur bei <c>Correction</c> („nur Zusätze, ohne Wasser") darf sie fehlen.</summary>
    public double? Liter { get; set; }

    /// <summary>
    /// Wasserverbrauch seit dem letzten Nachfüllen oder Wasserwechsel in Litern — optional, nur zum
    /// Festhalten (Statistik, Gegenprobe zur nachgefüllten Menge). Die Rechnung braucht ihn nicht.
    /// </summary>
    public double? VerbrauchLiter { get; set; }

    /// <summary>Füllstand nach dem Nachfüllen in Litern. Leer = das Reservoir ist wieder voll (Anlagevolumen).</summary>
    public double? FuellstandDanachLiter { get; set; }

    /// <summary>
    /// Nach so vielen Minuten (1–240) trägt der Fork EC und pH aus den Sensoren als Messung „nachher" ein.
    /// Leer = keine automatische Nachmessung. Wird ignoriert, wenn <see cref="Nachher"/> Werte trägt —
    /// was der Nutzer selbst einträgt, hat Vorrang.
    /// </summary>
    public int? NachmessungMinuten { get; set; }

    /// <summary>Womit nachgefüllt wurde.</summary>
    public WaterSource Wasser { get; set; } = WaterSource.Tap;

    /// <summary>Nur bei Mischung: Anteil Osmose 0–100 %.</summary>
    public double? OsmoseProzent { get; set; }

    /// <summary>EC des Ausgangswassers, mS/cm (Vorschlag oder selbst gemessen).</summary>
    public double? WasserEcMsCm { get; set; }

    /// <summary>EC-Ziel am Tank, mS/cm — wie es der Vorschlag nannte (Plan + Wasser). Optional.</summary>
    public double? EcZiel { get; set; }

    /// <summary>Werte vor dem Nachfüllen — vom Sensor übernommen oder von Hand. Leer = keine Messung „vorher".</summary>
    public VorgangMessungRequest? Vorher { get; set; }

    /// <summary>Werte nach dem Nachfüllen. Leer = keine Messung „nachher".</summary>
    public VorgangMessungRequest? Nachher { get; set; }

    /// <summary>Was als Verbrauch gebucht wird.</summary>
    public List<VorgangBuchungRequest> Buchungen { get; set; } = [];

    /// <summary>Notiz; steht am Addback-Eintrag.</summary>
    public string? Notiz { get; set; }

    /// <summary>Die Tagebuchzeile — oder <c>null</c>, wenn „Ins Tagebuch" aus ist.</summary>
    public VorgangTagebuchRequest? Tagebuch { get; set; }
}

/// <summary>Ein gespeichertes Nachfüllen mit allem, was es angelegt hat.</summary>
public sealed record AddbackVorgangDto(
    int Id,
    int GrowId,
    DateTime ErstelltAmUtc,
    AddbackLogDto? Eintrag,
    MeasurementDto? Vorher,
    MeasurementDto? Nachher,
    IReadOnlyList<VorgangBuchungDto> Buchungen,
    JournalEntryDto? Tagebuch,
    double? OsmoseProzent,
    string? VorherHerkunft,
    DateTime? VorherSensorZeitUtc,
    string? NachmessungStatus = null,
    DateTime? NachmessungFaelligUtc = null,
    string? NachmessungHinweis = null);
