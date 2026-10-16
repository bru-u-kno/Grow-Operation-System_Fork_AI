namespace GrowDiary.Web.Models;

public sealed class GrowRun
{
    public int Id { get; set; }
    public int? TentId { get; set; }

    /// <summary>
    /// Eigenes Sollwert-Profil für diesen Lauf; null heisst „vom System geerbt".
    /// </summary>
    /// <remarks>
    /// Sollwerte beschreiben, wie man DIESE Pflanze fährt — eine Sorte, die mehr
    /// verträgt, ein Versuch. Zwei Läufe im selben Becken dürfen abweichen.
    /// </remarks>
    public string? SetpointProfileId { get; set; }
    public int? SystemId { get; set; }
    public int? SetupId { get; set; }
    public string? TentName { get; set; }
    public string? HydroSetupName { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// Verweis in die Sorten-Bibliothek. <see cref="Strain"/> und
    /// <see cref="Breeder"/> bleiben als Text daneben stehen: sie halten fest,
    /// was zum Zeitpunkt des Laufs galt, auch wenn die Sorte spaeter umbenannt
    /// oder geloescht wird.
    /// </summary>
    public int? StrainId { get; set; }

    /// <summary>
    /// Die Sorten, die wirklich in diesem Grow stehen — aus seinen Pflanzen.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Anlass (31.08.2026).</b> Der Tester hat definiert, was ein
    /// Grow ist: ein Durchgang mit N Pflanzen und N Sorten. <see cref="Strain"/>
    /// trägt aber nur EINE — und fünf Ansichten gaben sie als die Sorte des
    /// Grows aus: Grow-Liste, Zelt-Detail, Messformular, Addback-Kopf und
    /// Addback-Übersicht. Bei zwei Sorten im selben Becken war das schlicht
    /// falsch.</para>
    ///
    /// <para>Gelesen wird sie aus <c>PlantInstances</c> — dort steht die Sorte
    /// je Topf, und das ist die Wahrheit. Leer heisst „keine Pflanze einzeln
    /// erfasst"; dann gilt weiter <see cref="Strain"/>.</para>
    /// </remarks>
    public IReadOnlyList<string> PflanzenSorten { get; set; } = [];

    /// <summary>
    /// Tragen <b>alle</b> erfassten Pflanzen die Hauptsorte des Laufs?
    /// </summary>
    /// <remarks>
    /// <para>Nur dann gehört <see cref="Breeder"/> zu dem, was angezeigt wird.
    /// Die Oberfläche hat das zuerst über den NAMEN geraten — und lag bei
    /// „Northern Lights" gegen „Northern Lights Auto" falsch: richtige Sorte,
    /// Züchter der anderen. Gefunden vom Prüfer, der sich zwei solche Sorten
    /// selbst angelegt hat.</para>
    ///
    /// <para><c>false</c> auch, wenn gar keine Pflanze erfasst ist — dann ist
    /// die Frage gegenstandslos, und <see cref="PflanzenSorten"/> ist leer.</para>
    /// </remarks>
    public bool NurHauptsorte { get; set; }

    public string? Strain { get; set; }
    public string? Breeder { get; set; }
    public GrowStatus Status { get; set; } = GrowStatus.Planning;
    public MediumType MediumType { get; set; } = MediumType.Hydro;
    public FeedingStyle FeedingStyle { get; set; } = FeedingStyle.None;
    public HydroStyle HydroStyle { get; set; } = HydroStyle.None;
    public GrowEnvironment Environment { get; set; } = GrowEnvironment.Indoor;
    public string? Light { get; set; }
    public string? ContainerSize { get; set; }
    public string? ReservoirSize { get; set; }
    public string? MediumDetail { get; set; }
    public string? IrrigationStyle { get; set; }
    public IrrigationType IrrigationType { get; set; } = IrrigationType.ActiveHydro;
    public WaterSource WaterSource { get; set; } = WaterSource.Tap;

    /// <summary>
    /// Das Duengerprogramm dieses Laufs — die Id eines nutrient-programs aus dem
    /// Wissen (etwa <c>athena</c>), null heisst „keins gewaehlt".
    /// </summary>
    /// <remarks>
    /// Am Grow, nicht global: ein Vergleichslauf darf ein anderes Programm
    /// fahren. Traegt das Programm ein Wochen-Chart, kann der Mischplan beim
    /// Ansetzen konkrete Milliliter nennen.
    /// </remarks>
    public string? FeedProgramId { get; set; }

    /// <summary>Ob die Wochen-Ziele des Feedcharts als Sollwerte gelten.</summary>
    /// <remarks>
    /// Bewusst ein eigener Schalter und nicht die blosse Folge der
    /// Programmwahl: das Chart ist ein Vorschlag des Herstellers, kein Befehl.
    /// Wer sein Programm nur zum Mischen nutzt und bei den Zielen des
    /// Phasenprofils bleiben will, soll das duerfen.
    /// </remarks>
    public bool UseFeedChartTargets { get; set; }

    /// <summary>Ob die Nachttemperatur je Blütewoche abgesenkt wird.</summary>
    /// <remarks>
    /// Aus, solange niemand es einschaltet. Das hier greift in die Kuehlung ein —
    /// das darf nie eine Nebenwirkung einer anderen Einstellung sein.
    /// </remarks>
    public bool NightRampEnabled { get; set; }

    /// <summary>Wo die Rampe stehen bleibt; ohne Angabe der Finish-Nachtwert des Profils.</summary>
    public double? NightRampFloorC { get; set; }
    public SeedType SeedType { get; set; } = SeedType.Feminized;
    public StartMaterial StartMaterial { get; set; } = StartMaterial.Seed;
    public GerminationMethod? GerminationMethod { get; set; }
    public string? CloneSource { get; set; }
    public bool CloneIsRooted { get; set; }
    /// <summary>
    /// Wie lange vegetativ gewachsen werden soll, in Tagen ab Startdatum.
    /// Die Absicht, nicht die Beobachtung: daraus ergibt sich der geplante
    /// Flip-Termin, solange <see cref="FlipDate"/> noch leer ist.
    /// </summary>
    public int? PlannedVegDays { get; set; }
    /// <summary>
    /// Wirksame Züchterwochen. Gehört das Grow einer Sorte und hat keinen
    /// eigenen Wert (<see cref="BreederWochenEigen"/>), stehen hier die Wochen
    /// der Sorte — beim Lesen aus der Datenbank eingesetzt, nie kopiert.
    /// Alle Rechner (Finish-Beginn, Plan-Woche, Kosten) lesen diesen Wert.
    /// </summary>
    public int? BreederFlowerWeeksMin { get; set; }
    public int? BreederFlowerWeeksMax { get; set; }
    /// <summary>
    /// Der Wert im Grow ist bewusst gesetzt und geht der Sorte vor. Ohne Sorte
    /// ist er es immer. Sonst folgt das Grow der Sorte, auch wenn sie sich ändert.
    /// </summary>
    public bool BreederWochenEigen { get; set; }

    /// <summary>Vorgabe und Obergrenze des Flush: zwei Wochen vor der Ernte.</summary>
    public const int FlushTageStandard = 14;

    /// <summary>Kürzester wählbarer Flush: eine Woche.</summary>
    public const int FlushTageKurz = 7;

    /// <summary>
    /// Wie lange das Grow vor der Ernte flusht, in Tagen: 7 (eine Woche) oder 14
    /// (zwei Wochen, die Vorgabe). Der Finish-Beginn ist Ernte laut Züchter
    /// (Maximum) minus diese Tage, solange er nicht bestätigt ist.
    /// </summary>
    public int FlushTage { get; set; } = FlushTageStandard;

    /// <summary>Jeder andere Wert als 7 gilt als die Vorgabe 14 — nie eine erfundene Dauer.</summary>
    public static int FlushTageGueltig(int? tage) => tage == FlushTageKurz ? FlushTageKurz : FlushTageStandard;
    /// <summary>Wochen laut Sorte (nur Anzeige; leer ohne Sorte oder ohne Angabe).</summary>
    public int? SortenWochenMin { get; set; }
    public int? SortenWochenMax { get; set; }
    public int? PlantCount { get; set; }
    public int? PhenoNumber { get; set; }
    public PropagationMedium? PropagationMedium { get; set; }
    public bool HasChiller { get; set; }
    public GrowEntryPoint EntryPoint { get; set; } = GrowEntryPoint.Germination;
    public int? DaysAlreadyInPhase { get; set; }
    public int? AutoflowerDaysSinceGermination { get; set; }
    public DateTime? FlipDate { get; set; }
    public DateTime? GerminatedAt { get; set; }
    public DateTime? RootedAt { get; set; }

    /// <summary>
    /// Wann der Saemling zur Veg wurde — beobachtet, nicht gerechnet.
    /// </summary>
    /// <remarks>
    /// Der Uebergang haengt nicht am Kalender, sondern am Aussehen: echte
    /// gezackte Blaetter statt der zwei runden Keimblaetter, dickerer Stengel,
    /// regelmaessig neue Blattpaare, Seitentriebe an den Knoten, spuerbar mehr
    /// Wasserverbrauch. Typisch ein bis drei Wochen nach der Keimung — aber eben
    /// typisch, nicht sicher.
    ///
    /// Solange hier nichts steht, schaetzt <see cref="Services.GrowStageResolver"/>
    /// ueber die Tage. Steht etwas, gewinnt es: wer hingesehen hat, weiss es
    /// besser als jede Rechnung. Dieselbe Regel wie beim Flip.
    /// </remarks>
    public DateTime? VegStartedAt { get; set; }

    /// <summary>
    /// Wann das Finish (Spuelen) begann — beobachtet, nicht gerechnet.
    /// </summary>
    /// <remarks>
    /// Real entscheidet der Blick auf die Trichome, nicht die Breeder-Wochen:
    /// milchig mit ersten bernsteinfarbenen heisst bald ernten, also spuelen.
    /// Ohne Eintrag schaetzt der Resolver weiter ueber die Breeder-Angabe.
    /// </remarks>
    public DateTime? FinishStartedAt { get; set; }
    public string? Nutrients { get; set; }
    public string? Notes { get; set; }
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime? EndDate { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Immutable JSON snapshot of the tent at grow creation time.
    /// Used for export/comparison stability when the live tent changes later.
    /// </summary>
    public string? TentSnapshotJson { get; set; }

    /// <summary>
    /// Immutable JSON snapshot of the DWC/RDWC HydroSetup at grow creation time.
    /// Used for export/comparison stability when the live HydroSetup changes later.
    /// </summary>
    public string? HydroSetupSnapshotJson { get; set; }

    public DateTime? SnapshotsCapturedAtUtc { get; set; }

    public int MeasurementCount { get; set; }
    public string? LatestPhotoPath { get; set; }
    public Measurement? LatestMeasurement { get; set; }
    // Latest NON-NULL reservoir values across all measurements, so a partial
    // auto-measurement (e.g. only temp/humidity) does not blank pH/EC in summaries.
    public double? LatestReservoirPh { get; set; }
    public double? LatestReservoirEc { get; set; }

    public GrowthProfile Profile => new(HydroStyle);

    public bool IsArchived => Status is GrowStatus.Completed or GrowStatus.Aborted;
}
