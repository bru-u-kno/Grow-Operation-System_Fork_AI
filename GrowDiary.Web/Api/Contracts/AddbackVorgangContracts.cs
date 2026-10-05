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

    /// <summary>Nachgefüllte Liter — Pflicht, größer 0.</summary>
    public double? Liter { get; set; }

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
    DateTime? VorherSensorZeitUtc);
