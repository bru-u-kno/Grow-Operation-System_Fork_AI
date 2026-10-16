using System.ComponentModel.DataAnnotations;
using GrowDiary.Web.Models;

namespace GrowDiary.Web.Api.Contracts;

public sealed class GrowUpsertRequest
{
    public int? TemplateId { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public int? TentId { get; set; }
    public int? SystemId { get; set; }
    public int? SetupId { get; set; }
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
    /// true = die Wochen oben sind bewusst gesetzt und gehen der Sorte vor;
    /// false = das Grow folgt der Sorte (die Wochen oben werden ignoriert);
    /// leer (älterer Client) = abgeleitet: Wochen, die von der Sorte abweichen, gelten als bewusst.
    /// </summary>
    public bool? BreederWeeksEigen { get; set; }

    /// <summary>Flush vor der Ernte in Tagen: 7 (eine Woche) oder 14 (zwei Wochen). Leer = unverändert (beim Anlegen 14).</summary>
    public int? FlushTage { get; set; }

    /// <summary>Geplante Veg-Dauer in Tagen ab Startdatum; leer = kein Plan.</summary>
    public int? PlannedVegDays { get; set; }

    /// <summary>Sollwert-Profil dieses Laufs; null heisst „vom System geerbt".</summary>
    public string? SetpointProfileId { get; set; }

    /// <summary>Sorte aus der Bibliothek; leer = nur freier Text.</summary>
    /// <remarks>
    /// Die <b>Hauptsorte</b> des Laufs — für Laufzeiten, Blütewochen und die
    /// Statistik. Steht in <see cref="Toepfe"/> eine Belegung, gilt sie je Topf
    /// und diese hier ist nur noch der Rückfall für Töpfe ohne eigene Angabe.
    /// </remarks>
    public int? StrainId { get; set; }

    /// <summary>
    /// Welche Sorte in welchem Topf steht — die Belegung des Systems.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Anlass (31.08.2026).</b> Der Tester hat definiert, was ein
    /// Grow ist: „ein Durchgang in einem RDWC/DWC, der N Pflanzen mit N
    /// verschiedenen Sorten/Phenos beinhalten kann. In dem Grow sollten die
    /// ganzen Sorten im RDWC-System stehen wie bei den Töpfen."</para>
    ///
    /// <para><b>Zuweisung, nicht Ersetzung.</b> Beim Anlegen entstehen die
    /// genannten Pflanzen. Beim Bearbeiten bekommt jeder genannte Topf seine
    /// Sorte; ein leerer wird gefüllt, ein nicht genannter bleibt unberührt.
    /// Gelöscht wird hier nie — dafür gibt es die Karte „Pflanzen &amp;
    /// Sorten" mit ihrer Rückfrage.</para>
    ///
    /// <para><c>null</c> heisst „Feld nicht mitgeschickt" und ändert nichts —
    /// fremde Aufrufer und der MCP-Server dürfen dem Grow seine Pflanzen nicht
    /// dadurch nehmen, dass sie ein Feld nicht kennen.</para>
    /// </remarks>
    public List<TopfBelegungRequest>? Toepfe { get; set; }
    public HydroStyle HydroStyle { get; set; } = HydroStyle.RDWC;
    public int? PlantCount { get; set; }
    public string? ReservoirSize { get; set; }
    public string? ContainerSize { get; set; }
    public PropagationMedium? PropagationMedium { get; set; }
    public string? Light { get; set; }
    public bool HasChiller { get; set; }
    public WaterSource WaterSource { get; set; } = WaterSource.RO;

    /// <summary>Duengerprogramm des Laufs (Id aus dem Wissen); null = keins.</summary>
    public string? FeedProgramId { get; set; }

    /// <summary>Feedchart-Ziele als Opt-in; null heisst „Feld nicht mitgeschickt, Bestand behalten".</summary>
    public bool? UseFeedChartTargets { get; set; }
    public string? Nutrients { get; set; }

    [Required]
    public string StartDate { get; set; } = DateTime.Today.ToString("yyyy-MM-dd");

    public GrowEntryPoint EntryPoint { get; set; } = GrowEntryPoint.Germination;
    public int? DaysAlreadyInPhase { get; set; }
    public int? AutoflowerDaysSinceGermination { get; set; }
    public string? FlipDate { get; set; }
    public string? Notes { get; set; }
    public GrowStatus Status { get; set; } = GrowStatus.Planning;
    public GrowEnvironment Environment { get; set; } = GrowEnvironment.Indoor;
}

/// <summary>Ein Topf und die Sorte, die darin steht.</summary>
public sealed class TopfBelegungRequest
{
    /// <summary>Die Topfnummer, ab 1.</summary>
    [Range(1, 512, ErrorMessage = "Die Topfnummer muss mindestens 1 sein.")]
    public int Topf { get; set; }

    /// <summary>Die Sorte aus der Bibliothek; leer = ohne Sorte.</summary>
    public int? StrainId { get; set; }
}
