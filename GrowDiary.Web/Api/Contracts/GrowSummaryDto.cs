using GrowDiary.Web.Models;

namespace GrowDiary.Web.Api.Contracts;

/// <summary>
/// Kompakte Darstellung eines Grow-Runs für Listen und Übersichten.
/// Enthält alles, was man für Cards oder Tabellen braucht.
/// </summary>
public sealed record GrowSummaryDto(
    int Id,
    string Name,
    string? Strain,
    string? Breeder,
    GrowStatus Status,
    HydroStyle HydroStyle,
    GrowEnvironment Environment,
    SeedType SeedType,
    StartMaterial StartMaterial,
    int? PlantCount,
    int? TentId,
    int? SystemId,
    int? SetupId,
    string? TentName,
    string? HydroSetupName,
    DateTime StartDate,
    DateTime? EndDate,
    int? StrainId,
    DateTime? FlipDate,
    /// <summary>Geplante Veg-Dauer in Tagen ab Start — daraus der geplante Flip.</summary>
    int? PlannedVegDays,
    string? SetpointProfileId,
    int? BreederFlowerWeeksMin,
    int? BreederFlowerWeeksMax,
    /// <summary>true = Wochen im Grow bewusst gesetzt; false = folgt der Sorte.</summary>
    bool BreederWeeksEigen,
    /// <summary>Wochen laut Sorte (Anzeige der Abweichung); leer ohne Sorte.</summary>
    int? StrainFlowerWeeksMin,
    int? StrainFlowerWeeksMax,
    /// <summary>Flush vor der Ernte in Tagen: 7 oder 14.</summary>
    int FlushTage,
    /// <summary>Angehängte Blütewochen im Plan, deren Werte noch niemand geprüft hat — für die Aufgaben.</summary>
    IReadOnlyList<PlanWocheKurzDto> PlanUngeprueft,
    DateTime? GerminatedAt,
    DateTime? RootedAt,
    DateTime? VegStartedAt,
    DateTime? FinishStartedAt,
    /// <summary>
    /// Wo der Lauf eingestiegen ist und wie viele Tage er dort schon hinter
    /// sich hatte. Auch die Liste braucht beides: ihr Zeitstrahl rechnet sonst
    /// anders als der auf der Detailseite, und zwei Zahlen zur selben Sache,
    /// die sich widersprechen, sind schlimmer als eine ungenaue.
    /// </summary>
    string EntryPoint,
    int? DaysAlreadyInPhase,
    /// <summary>Die Phase von HEUTE, aus dem Resolver — auch die Listen zeigen sie.</summary>
    string CurrentStage,
    int MeasurementCount,
    string? LatestPhotoPath,
    double? LatestReservoirPh,
    double? LatestReservoirEc,
    DateTime? LatestMeasurementAt,

    /// <summary>
    /// Die Sorten, die wirklich in diesem Grow stehen — aus seinen Pflanzen.
    /// </summary>
    /// <remarks>
    /// Leer heisst „keine Pflanze einzeln erfasst"; dann gilt weiter
    /// <c>Strain</c>. Mehr als eine heisst „gemischt" — ein Grow ist ein
    /// Durchgang mit N Pflanzen und N Sorten, so hat es der Tester am
    /// 31.08.2026 definiert.
    /// </remarks>
    IReadOnlyList<string> PflanzenSorten,

    /// <summary>
    /// Tragen alle erfassten Pflanzen die Hauptsorte des Laufs? Nur dann
    /// gehört <c>Breeder</c> zu dem, was angezeigt wird.
    /// </summary>
    bool NurHauptsorte,

    /// <summary>Phase, Beginne, Woche und offene Erinnerung — aus dem Phasenanker.</summary>
    PhasenankerDto Phasenanker
);

/// <summary>Eine Plan-Woche in Kurzform: Spalten-Id und Name.</summary>
public sealed record PlanWocheKurzDto(string Id, string Label);
