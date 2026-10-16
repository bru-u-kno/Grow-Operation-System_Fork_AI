using GrowDiary.Web.Api.Contracts;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Api.Mapping;

public static class GrowMapping
{
    public static GrowSummaryDto ToSummaryDto(this GrowRun grow) => new(
        Id: grow.Id,
        Name: grow.Name,
        Strain: grow.Strain,
        Breeder: grow.Breeder,
        Status: grow.Status,
        HydroStyle: grow.HydroStyle,
        Environment: grow.Environment,
        SeedType: grow.SeedType,
        StartMaterial: grow.StartMaterial,
        PlantCount: grow.PlantCount,
        TentId: grow.TentId,
        SystemId: grow.SystemId,
        SetupId: grow.SetupId,
        TentName: grow.TentName,
        HydroSetupName: grow.HydroSetupName,
        StartDate: grow.StartDate,
        EndDate: grow.EndDate,
        StrainId: grow.StrainId,
        FlipDate: grow.FlipDate,
        PlannedVegDays: grow.PlannedVegDays,
        grow.SetpointProfileId,
        BreederFlowerWeeksMin: grow.BreederFlowerWeeksMin,
        BreederFlowerWeeksMax: grow.BreederFlowerWeeksMax,
        BreederWeeksEigen: grow.BreederWochenEigen,
        StrainFlowerWeeksMin: grow.SortenWochenMin,
        StrainFlowerWeeksMax: grow.SortenWochenMax,
        FlushTage: grow.FlushTage,
        PlanUngeprueft: UngepruefteWochen(grow),
        GerminatedAt: grow.GerminatedAt,
        RootedAt: grow.RootedAt,
        VegStartedAt: grow.VegStartedAt,
        FinishStartedAt: grow.FinishStartedAt,
        EntryPoint: grow.EntryPoint.ToString(),
        DaysAlreadyInPhase: grow.DaysAlreadyInPhase,
        CurrentStage: Services.GrowStageResolver.Resolve(grow, DateTime.Today).ToString(),
        MeasurementCount: grow.MeasurementCount,
        LatestPhotoPath: grow.LatestPhotoPath,
        LatestReservoirPh: grow.LatestReservoirPh ?? grow.LatestMeasurement?.ReservoirPh,
        LatestReservoirEc: grow.LatestReservoirEc ?? grow.LatestMeasurement?.ReservoirEc,
        LatestMeasurementAt: grow.LatestMeasurement?.TakenAt,
        PflanzenSorten: grow.PflanzenSorten,
        NurHauptsorte: grow.NurHauptsorte,
        Phasenanker: PhasenankerDto.Aus(Services.Phasenanker.Fuer(grow, DateTime.Today), grow.FinishStartedAt is not null || grow.EntryPoint == GrowEntryPoint.Flush)
    );

    /// <summary>
    /// Angehängte Blütewochen des Plans, deren übernommene Werte noch niemand
    /// geprüft hat — für die Aufgabenseite. Leer ohne Plan und bei beendeten Grows.
    /// </summary>
    private static IReadOnlyList<PlanWocheKurzDto> UngepruefteWochen(GrowRun grow)
    {
        if (grow.IsArchived || Services.GrowPlan.GrowPlanRegister.Inhalt(grow.Id) is not { } inhalt || inhalt.Ungeprueft.Count == 0)
        {
            return [];
        }

        return inhalt.Chart.Columns
            .Where(spalte => inhalt.Ungeprueft.Contains(spalte.Id))
            .Select(spalte => new PlanWocheKurzDto(spalte.Id, spalte.Label))
            .ToList();
    }

    public static GrowDetailDto ToDetailDto(this GrowRun grow) => new(
        Id: grow.Id,
        SystemId: grow.SystemId,
        SetupId: grow.SetupId,
        Name: grow.Name,
        Strain: grow.Strain,
        Breeder: grow.Breeder,
        Status: grow.Status,
        MediumType: grow.MediumType,
        FeedingStyle: grow.FeedingStyle,
        HydroStyle: grow.HydroStyle,
        IrrigationType: grow.IrrigationType,
        WaterSource: grow.WaterSource,
        FeedProgramId: grow.FeedProgramId,
        UseFeedChartTargets: grow.UseFeedChartTargets,
        Environment: grow.Environment,
        Light: grow.Light,
        ContainerSize: grow.ContainerSize,
        ReservoirSize: grow.ReservoirSize,
        MediumDetail: grow.MediumDetail,
        IrrigationStyle: grow.IrrigationStyle,
        HasChiller: grow.HasChiller,
        SeedType: grow.SeedType,
        StartMaterial: grow.StartMaterial,
        GerminationMethod: grow.GerminationMethod,
        PropagationMedium: grow.PropagationMedium,
        CloneSource: grow.CloneSource,
        CloneIsRooted: grow.CloneIsRooted,
        BreederFlowerWeeksMin: grow.BreederFlowerWeeksMin,
        BreederFlowerWeeksMax: grow.BreederFlowerWeeksMax,
        BreederWeeksEigen: grow.BreederWochenEigen,
        StrainFlowerWeeksMin: grow.SortenWochenMin,
        StrainFlowerWeeksMax: grow.SortenWochenMax,
        FlushTage: grow.FlushTage,
        PlannedVegDays: grow.PlannedVegDays,
        grow.SetpointProfileId,
        StrainId: grow.StrainId,
        PlantCount: grow.PlantCount,
        PhenoNumber: grow.PhenoNumber,
        TentId: grow.TentId,
        TentName: grow.TentName,
        HydroSetupName: grow.HydroSetupName,
        EntryPoint: grow.EntryPoint,
        DaysAlreadyInPhase: grow.DaysAlreadyInPhase,
        AutoflowerDaysSinceGermination: grow.AutoflowerDaysSinceGermination,
        StartDate: grow.StartDate,
        EndDate: grow.EndDate,
        FlipDate: grow.FlipDate,
        GerminatedAt: grow.GerminatedAt,
        RootedAt: grow.RootedAt,
        VegStartedAt: grow.VegStartedAt,
        FinishStartedAt: grow.FinishStartedAt,
        CurrentStage: Services.GrowStageResolver.Resolve(grow, DateTime.Today).ToString(),
        Nutrients: grow.Nutrients,
        Notes: grow.Notes,
        MeasurementCount: grow.MeasurementCount,
        LatestPhotoPath: grow.LatestPhotoPath,
        LatestMeasurement: grow.LatestMeasurement?.ToDto(),
        CreatedAtUtc: grow.CreatedAtUtc,
        UpdatedAtUtc: grow.UpdatedAtUtc,
        PflanzenSorten: grow.PflanzenSorten,
        NurHauptsorte: grow.NurHauptsorte,
        Phasenanker: PhasenankerDto.Aus(Services.Phasenanker.Fuer(grow, DateTime.Today), grow.FinishStartedAt is not null || grow.EntryPoint == GrowEntryPoint.Flush)
    );
}
