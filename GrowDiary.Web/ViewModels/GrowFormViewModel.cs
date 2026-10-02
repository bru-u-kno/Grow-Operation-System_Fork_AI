using System.ComponentModel.DataAnnotations;
using GrowDiary.Web.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace GrowDiary.Web.ViewModels;

public sealed class GrowFormViewModel
{
    public int? Id { get; set; }
    public int? TemplateId { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public int? TentId { get; set; }
    public int? SystemId { get; set; }
    public int? SetupId { get; set; }
    public List<SelectListItem> TentOptions { get; set; } = new();

    // Schritt 1 – Genetik
    public string? Strain { get; set; }
    public string? Breeder { get; set; }
    public SeedType SeedType { get; set; } = SeedType.Feminized;
    public StartMaterial StartMaterial { get; set; } = StartMaterial.Seed;
    public GerminationMethod? GerminationMethod { get; set; }
    public string? CloneSource { get; set; }
    public bool CloneIsRooted { get; set; }
    public int? PhenoNumber { get; set; }
    public int? BreederFlowerWeeksMin { get; set; }
    public int? BreederFlowerWeeksMax { get; set; }

    /// <summary>
    /// Geplante Veg-Dauer in Tagen ab Start. Autoflower kennt keinen Flip und
    /// damit auch keine planbare Veg-Phase — dort bleibt das Feld leer.
    /// </summary>
    public int? PlannedVegDays { get; set; }

    /// <summary>Sollwert-Profil dieses Laufs; null heisst „vom System geerbt".</summary>
    public string? SetpointProfileId { get; set; }

    /// <summary>Sorte aus der Bibliothek; leer = nur freier Text.</summary>
    public int? StrainId { get; set; }

    // Schritt 2 – System
    public HydroStyle HydroStyle { get; set; } = HydroStyle.RDWC;
    public int? PlantCount { get; set; }
    public string? ReservoirSize { get; set; }
    public string? ContainerSize { get; set; }
    public PropagationMedium? PropagationMedium { get; set; }
    public string? Light { get; set; }
    public bool HasChiller { get; set; }

    // Schritt 3 – Nährstoffe & Wasser
    public WaterSource WaterSource { get; set; } = WaterSource.RO;
    public string? FeedProgramId { get; set; }

    public bool UseFeedChartTargets { get; set; }
    public string? Nutrients { get; set; }
    public List<string> NutrientSuggestions { get; set; } = new();

    // Schritt 4 – Einstiegspunkt
    [Required]
    public string StartDate { get; set; } = DateTime.Today.ToString("yyyy-MM-dd");
    public GrowEntryPoint EntryPoint { get; set; } = GrowEntryPoint.Germination;
    public int? DaysAlreadyInPhase { get; set; }
    public int? AutoflowerDaysSinceGermination { get; set; }
    public string? FlipDate { get; set; }
    public string? Notes { get; set; }

    // Feste Werte (RDWC/DWC-only App)
    public GrowStatus Status { get; set; } = GrowStatus.Planning;
    public GrowEnvironment Environment { get; set; } = GrowEnvironment.Indoor;
    public MediumType MediumType => MediumType.Hydro;
    public FeedingStyle FeedingStyle => FeedingStyle.None;
    public IrrigationType IrrigationType => IrrigationType.ActiveHydro;

    // Hilfseigenschaften
    public bool IsAutoflower => SeedType == SeedType.Autoflower;

    /// <summary>
    /// Ob das Formular ein Flipdatum kennt — die EINE Wahrheit dazu.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Fehler, aus dem das hier steht.</b> Bis zum 25.08.2026 stand
    /// hier zusätzlich <c>EntryPoint == GrowEntryPoint.Flower</c>. Das Formular
    /// zeigt das Feld aber für <i>jeden</i> Grow, der keine Autoflower ist
    /// (<c>GrowSetupPage.tsx</c>) — und der Normalfall ist genau der andere:
    /// ein Grow beginnt in der Keimung oder Vegetation und wird später
    /// geflippt. Wer das Datum dann eintrug, bekam HTTP 200 und einen
    /// unveränderten Wert zurück. Belegt am laufenden Stand: 2026-08-01
    /// geschickt, 2026-07-20 geblieben, keine Meldung.</para>
    ///
    /// <para>Ein Autoflower hat keinen Flip im Formular — ihr Blütebeginn wird
    /// mit „Blüte beginnt" bestätigt (<c>Phasenanker</c>). Deshalb bleibt das
    /// die einzige Bedingung.</para>
    /// </remarks>
    public bool NeedsFlipDate => !IsAutoflower;
    public bool NeedsDaysInPhase => EntryPoint != GrowEntryPoint.Germination && !IsAutoflower;

    public static GrowFormViewModel FromGrow(GrowRun grow)
    {
        return new GrowFormViewModel
        {
            Id = grow.Id,
            TentId = grow.TentId,
            SystemId = grow.SystemId,
            SetupId = grow.SetupId,
            Name = grow.Name,
            Strain = grow.Strain,
            Breeder = grow.Breeder,
            Status = grow.Status,
            HydroStyle = grow.HydroStyle == HydroStyle.None ? HydroStyle.RDWC : grow.HydroStyle,
            Environment = grow.Environment,
            Light = grow.Light,
            ContainerSize = grow.ContainerSize,
            ReservoirSize = grow.ReservoirSize,
            WaterSource = grow.WaterSource,
            FeedProgramId = grow.FeedProgramId,
            UseFeedChartTargets = grow.UseFeedChartTargets,
            SeedType = grow.SeedType,
            StartMaterial = grow.StartMaterial,
            GerminationMethod = grow.GerminationMethod,
            CloneSource = grow.CloneSource,
            CloneIsRooted = grow.CloneIsRooted,
            PhenoNumber = grow.PhenoNumber,
            BreederFlowerWeeksMin = grow.BreederFlowerWeeksMin,
            BreederFlowerWeeksMax = grow.BreederFlowerWeeksMax,
            PlannedVegDays = grow.PlannedVegDays,
            SetpointProfileId = grow.SetpointProfileId,
            StrainId = grow.StrainId,
            PlantCount = grow.PlantCount,
            PropagationMedium = grow.PropagationMedium,
            HasChiller = grow.HasChiller,
            EntryPoint = grow.EntryPoint,
            DaysAlreadyInPhase = grow.DaysAlreadyInPhase,
            AutoflowerDaysSinceGermination = grow.AutoflowerDaysSinceGermination,
            FlipDate = grow.FlipDate?.ToString("yyyy-MM-dd"),
            Nutrients = grow.Nutrients,
            Notes = grow.Notes,
            StartDate = grow.StartDate.ToString("yyyy-MM-dd")
        };
    }

    public GrowRun ToGrow()
    {
        var run = new GrowRun
        {
            Id = Id ?? 0,
            TentId = TentId,
            SystemId = SystemId,
            SetupId = SetupId,
            Name = Name.Trim(),
            Strain = string.IsNullOrWhiteSpace(Strain) ? null : Strain.Trim(),
            Breeder = string.IsNullOrWhiteSpace(Breeder) ? null : Breeder.Trim(),
            Status = Status,
            MediumType = MediumType.Hydro,
            FeedingStyle = FeedingStyle.None,
            HydroStyle = HydroStyle,
            MediumDetail = HydroStyle.ToString(),
            Environment = Environment,
            Light = string.IsNullOrWhiteSpace(Light) ? null : Light.Trim(),
            ContainerSize = string.IsNullOrWhiteSpace(ContainerSize) ? null : ContainerSize.Trim(),
            ReservoirSize = string.IsNullOrWhiteSpace(ReservoirSize) ? null : ReservoirSize.Trim(),
            IrrigationStyle = null,
            IrrigationType = IrrigationType.ActiveHydro,
            WaterSource = WaterSource,
            FeedProgramId = FeedProgramId,
            UseFeedChartTargets = UseFeedChartTargets,
            SeedType = SeedType,
            StartMaterial = StartMaterial,
            GerminationMethod = StartMaterial == StartMaterial.Seed ? GerminationMethod : null,
            CloneSource = StartMaterial == StartMaterial.Clone ? (string.IsNullOrWhiteSpace(CloneSource) ? null : CloneSource.Trim()) : null,
            CloneIsRooted = StartMaterial == StartMaterial.Clone && CloneIsRooted,
            PhenoNumber = PhenoNumber,
            BreederFlowerWeeksMin = IsAutoflower ? null : BreederFlowerWeeksMin,
            BreederFlowerWeeksMax = IsAutoflower ? null : BreederFlowerWeeksMax,
            PlannedVegDays = IsAutoflower ? null : PlannedVegDays,
            SetpointProfileId = string.IsNullOrWhiteSpace(SetpointProfileId) ? null : SetpointProfileId,
            StrainId = StrainId,
            PlantCount = PlantCount,
            PropagationMedium = PropagationMedium,
            HasChiller = HasChiller,
            EntryPoint = EntryPoint,
            DaysAlreadyInPhase = NeedsDaysInPhase ? DaysAlreadyInPhase : null,
            AutoflowerDaysSinceGermination = IsAutoflower ? AutoflowerDaysSinceGermination : null,
            FlipDate = NeedsFlipDate && !string.IsNullOrWhiteSpace(FlipDate) ? DateTime.Parse(FlipDate) : null,
            Nutrients = string.IsNullOrWhiteSpace(Nutrients) ? null : Nutrients.Trim(),
            Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
            // Ohne Startdatum ergibt der ganze Grow keinen Sinn: Phasen, Tageszählung
            // und Zeitstrahl hängen daran. Kommt keins an — leeres Feld, fremder
            // API-Aufrufer —, ist der Tag des Anlegens Tag 1. Das ist die Regel,
            // die ein Grower ohnehin im Kopf hat, und besser als ein Fehler oder
            // ein Grow ohne Zeitachse.
            StartDate = DateTime.TryParse(StartDate, out var start) ? start : DateTime.Today
        };

        // Steckling bereits bewurzelt: RootedAt auf StartDate setzen
        if (run.StartMaterial == StartMaterial.Clone
            && run.CloneIsRooted
            && run.RootedAt == null)
        {
            run.RootedAt = run.StartDate;
        }

        // Samen bereits in fortgeschrittener Phase: GerminatedAt setzen.
        // Das ist KEINE Bestätigung der Vegi — die steckt im Einstiegspunkt
        // selbst (Phasenanker).
        if (run.StartMaterial == StartMaterial.Seed
            && run.EntryPoint != GrowEntryPoint.Germination
            && run.GerminatedAt == null)
        {
            run.GerminatedAt = run.StartDate;
        }

        return run;
    }
}
