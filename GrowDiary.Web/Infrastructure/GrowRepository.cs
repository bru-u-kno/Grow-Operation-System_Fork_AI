using GrowDiary.Web.Models;
using GrowDiary.Web.Services.Knowledge.Schema;

namespace GrowDiary.Web.Infrastructure;

public sealed class GrowRepository
{
    private readonly TentRepository _tentRepository;
    private readonly HydroSetupRepository _hydroSetupRepository;
    private readonly AddbackRepository _addbackRepository;
    private readonly HardwareRepository _hardwareRepository;
    private readonly SetupRepository _setupRepository;
    private readonly AutoMeasurementRepository _autoMeasurementRepository;
    private readonly LightRepository _lightRepository;
    private readonly SopRepository _sopRepository;
    private readonly PhotoRepository _photoRepository;
    private readonly HomeAssistantSettingsRepository _homeAssistantSettingsRepository;
    private readonly GrowCoreRepository _growCoreRepository;
    private readonly MeasurementRepository _measurementRepository;

    public GrowRepository(AppPaths paths)
        : this(paths, new TentRepository(paths))
    {
    }

    private GrowRepository(AppPaths paths, TentRepository tentRepository)
        : this(paths, tentRepository, new HydroSetupRepository(paths, tentRepository), new AddbackRepository(paths), new HardwareRepository(paths), new SetupRepository(paths), new AutoMeasurementRepository(paths), new LightRepository(paths), new SopRepository(paths), new PhotoRepository(paths), new HomeAssistantSettingsRepository(paths), new GrowCoreRepository(paths), new MeasurementRepository(paths))
    {
    }

    public GrowRepository(AppPaths paths, TentRepository tentRepository, HydroSetupRepository hydroSetupRepository)
        : this(paths, tentRepository, hydroSetupRepository, new AddbackRepository(paths), new HardwareRepository(paths), new SetupRepository(paths), new AutoMeasurementRepository(paths), new LightRepository(paths), new SopRepository(paths), new PhotoRepository(paths), new HomeAssistantSettingsRepository(paths), new GrowCoreRepository(paths), new MeasurementRepository(paths))
    {
    }

    public GrowRepository(AppPaths paths, TentRepository tentRepository, HydroSetupRepository hydroSetupRepository, AddbackRepository addbackRepository)
        : this(paths, tentRepository, hydroSetupRepository, addbackRepository, new HardwareRepository(paths), new SetupRepository(paths), new AutoMeasurementRepository(paths), new LightRepository(paths), new SopRepository(paths), new PhotoRepository(paths), new HomeAssistantSettingsRepository(paths), new GrowCoreRepository(paths), new MeasurementRepository(paths))
    {
    }

    public GrowRepository(AppPaths paths, TentRepository tentRepository, HydroSetupRepository hydroSetupRepository, AddbackRepository addbackRepository, HardwareRepository hardwareRepository)
        : this(paths, tentRepository, hydroSetupRepository, addbackRepository, hardwareRepository, new SetupRepository(paths), new AutoMeasurementRepository(paths), new LightRepository(paths), new SopRepository(paths), new PhotoRepository(paths), new HomeAssistantSettingsRepository(paths), new GrowCoreRepository(paths), new MeasurementRepository(paths))
    {
    }

    public GrowRepository(AppPaths paths, TentRepository tentRepository, HydroSetupRepository hydroSetupRepository, AddbackRepository addbackRepository, HardwareRepository hardwareRepository, SetupRepository setupRepository)
        : this(paths, tentRepository, hydroSetupRepository, addbackRepository, hardwareRepository, setupRepository, new AutoMeasurementRepository(paths), new LightRepository(paths), new SopRepository(paths), new PhotoRepository(paths), new HomeAssistantSettingsRepository(paths), new GrowCoreRepository(paths), new MeasurementRepository(paths))
    {
    }

    public GrowRepository(AppPaths paths, TentRepository tentRepository, HydroSetupRepository hydroSetupRepository, AddbackRepository addbackRepository, HardwareRepository hardwareRepository, SetupRepository setupRepository, AutoMeasurementRepository autoMeasurementRepository)
        : this(paths, tentRepository, hydroSetupRepository, addbackRepository, hardwareRepository, setupRepository, autoMeasurementRepository, new LightRepository(paths), new SopRepository(paths), new PhotoRepository(paths), new HomeAssistantSettingsRepository(paths), new GrowCoreRepository(paths), new MeasurementRepository(paths))
    {
    }

    public GrowRepository(AppPaths paths, TentRepository tentRepository, HydroSetupRepository hydroSetupRepository, AddbackRepository addbackRepository, HardwareRepository hardwareRepository, SetupRepository setupRepository, AutoMeasurementRepository autoMeasurementRepository, LightRepository lightRepository)
        : this(paths, tentRepository, hydroSetupRepository, addbackRepository, hardwareRepository, setupRepository, autoMeasurementRepository, lightRepository, new SopRepository(paths), new PhotoRepository(paths), new HomeAssistantSettingsRepository(paths), new GrowCoreRepository(paths), new MeasurementRepository(paths))
    {
    }

    public GrowRepository(AppPaths paths, TentRepository tentRepository, HydroSetupRepository hydroSetupRepository, AddbackRepository addbackRepository, HardwareRepository hardwareRepository, SetupRepository setupRepository, AutoMeasurementRepository autoMeasurementRepository, LightRepository lightRepository, SopRepository sopRepository, PhotoRepository photoRepository, HomeAssistantSettingsRepository homeAssistantSettingsRepository)
        : this(paths, tentRepository, hydroSetupRepository, addbackRepository, hardwareRepository, setupRepository, autoMeasurementRepository, lightRepository, sopRepository, photoRepository, homeAssistantSettingsRepository, new GrowCoreRepository(paths), new MeasurementRepository(paths))
    {
    }

    public GrowRepository(AppPaths paths, TentRepository tentRepository, HydroSetupRepository hydroSetupRepository, AddbackRepository addbackRepository, HardwareRepository hardwareRepository, SetupRepository setupRepository, AutoMeasurementRepository autoMeasurementRepository, LightRepository lightRepository, SopRepository sopRepository, PhotoRepository photoRepository, HomeAssistantSettingsRepository homeAssistantSettingsRepository, GrowCoreRepository growCoreRepository, MeasurementRepository measurementRepository)
    {
        _tentRepository = tentRepository;
        _hydroSetupRepository = hydroSetupRepository;
        _addbackRepository = addbackRepository;
        _hardwareRepository = hardwareRepository;
        _setupRepository = setupRepository;
        _autoMeasurementRepository = autoMeasurementRepository;
        _lightRepository = lightRepository;
        _sopRepository = sopRepository;
        _photoRepository = photoRepository;
        _homeAssistantSettingsRepository = homeAssistantSettingsRepository;
        _growCoreRepository = growCoreRepository;
        _measurementRepository = measurementRepository;
    }

    public DashboardStats GetDashboardStats()
        => _growCoreRepository.GetDashboardStats();

    /// <summary>
    /// Die Zelte — jedes mit seinen laufenden Grows.
    /// </summary>
    /// <remarks>
    /// <para><b>Der Anlass (01.09.2026).</b> <c>Tent.ActiveGrows</c> wurde von
    /// genau zwei MVC-Controllern von Hand nachgezogen, vom Repository nie. Auf
    /// jedem anderen Weg war die Liste leer — und sieben Stellen in den
    /// Diensten lesen sie. Der Volumenfaktor der Dosierung blieb dadurch immer
    /// 1, und der Lichteinbruch-Waechter kehrte sofort zurueck, ohne je zu
    /// pruefen.</para>
    ///
    /// <para>Das Feld gehoert dem Zelt, also fuellt es die Stelle, die das Zelt
    /// laedt — genau wie <c>Sensors</c>. Sieben Aufrufer einzeln zu reparieren
    /// hiesse, den Fehler siebenmal nicht zu machen.</para>
    /// </remarks>
    public List<Tent> GetTents(bool includeArchived = false)
    {
        var zelte = _tentRepository.GetTents(includeArchived);
        if (zelte.Count == 0) return zelte;

        var nachZelt = _growCoreRepository.GetActiveGrowsByTent(zelte.Select(z => z.Id).ToList());
        foreach (var zelt in zelte)
        {
            zelt.ActiveGrows = nachZelt.TryGetValue(zelt.Id, out var grows) ? grows : [];
        }

        return zelte;
    }

    /// <summary>Ein Zelt — mit seinen laufenden Grows.</summary>
    public Tent? GetTent(int id)
    {
        var zelt = _tentRepository.GetTent(id);
        if (zelt is null) return null;

        zelt.ActiveGrows = _growCoreRepository.GetActiveGrowsForTent(id);
        return zelt;
    }

    public void UpdateTent(Tent tent)
        => _tentRepository.UpdateTent(tent);

    public void DeleteTent(int id)
        => _tentRepository.DeleteTent(id);

    public void DeleteTentWithCleanup(int id)
        => _tentRepository.DeleteTentWithCleanup(id);

    public bool HasTentDependencies(int id)
        => _tentRepository.HasTentDependencies(id);

    public void ArchiveTent(int id)
        => _tentRepository.ArchiveTent(id);

    public Tent CreateTent(string name)
        => _tentRepository.CreateTent(name);

    public Tent CreateTent(Tent tent)
        => _tentRepository.CreateTent(tent);

    public List<TentSensor> GetTentSensors(int tentId)
        => _tentRepository.GetTentSensors(tentId);

    public TentSensor AddTentSensor(TentSensor sensor)
        => _tentRepository.AddTentSensor(sensor);

    public void UpdateTentSensor(TentSensor sensor)
        => _tentRepository.UpdateTentSensor(sensor);

    public void ReplaceTentSensors(int tentId, IReadOnlyCollection<TentSensor> sensors)
        => _tentRepository.ReplaceTentSensors(tentId, sensors);

    public void DeleteTentSensor(int sensorId)
        => _tentRepository.DeleteTentSensor(sensorId);

    public TentSensor? GetTentSensor(int id)
        => _tentRepository.GetTentSensor(id);

    public HardwareItem CreateHardwareItem(HardwareItem item)
        => _hardwareRepository.CreateHardwareItem(item);

    public void UpdateHardwareItem(HardwareItem item)
        => _hardwareRepository.UpdateHardwareItem(item);

    public void DeleteHardwareItem(int id)
        => _hardwareRepository.DeleteHardwareItem(id);

    public HardwareItem? GetHardwareItem(int id)
        => _hardwareRepository.GetHardwareItem(id);

    public List<HardwareItem> GetHardwareItems()
        => _hardwareRepository.GetHardwareItems();

    public List<HardwareItem> GetHardwareItemsByTent(int tentId)
        => _hardwareRepository.GetHardwareItemsByTent(tentId);

    public List<HardwareItem> GetHardwareItemsByHydroSetup(int hydroSetupId)
        => _hardwareRepository.GetHardwareItemsByHydroSetup(hydroSetupId);

    public List<HardwareItem> GetHardwareItemsByStatus(HardwareItemStatus status)
        => _hardwareRepository.GetHardwareItemsByStatus(status);

    public MaintenanceEvent CreateMaintenanceEvent(MaintenanceEvent item)
        => _hardwareRepository.CreateMaintenanceEvent(item);

    public void UpdateMaintenanceEvent(MaintenanceEvent item)
        => _hardwareRepository.UpdateMaintenanceEvent(item);

    public MaintenanceEvent CompleteMaintenanceEvent(MaintenanceEvent item)
        => _hardwareRepository.CompleteMaintenanceEvent(item);

    public MaintenanceEvent? GetMaintenanceEvent(int id)
        => _hardwareRepository.GetMaintenanceEvent(id);

    public void DeleteMaintenanceEvent(int id)
        => _hardwareRepository.DeleteMaintenanceEvent(id);

    public List<MaintenanceEvent> GetMaintenanceEvents()
        => _hardwareRepository.GetMaintenanceEvents();

    public List<MaintenanceEvent> GetMaintenanceEventsByHardwareItem(int hardwareItemId)
        => _hardwareRepository.GetMaintenanceEventsByHardwareItem(hardwareItemId);

    public List<MaintenanceEvent> GetOpenMaintenanceEventsByHardwareItem(int hardwareItemId)
        => _hardwareRepository.GetOpenMaintenanceEventsByHardwareItem(hardwareItemId);

    public List<MaintenanceEvent> GetDueMaintenanceEvents(DateTime nowUtc)
        => _hardwareRepository.GetDueMaintenanceEvents(nowUtc);

    public CalibrationEvent CreateCalibrationEvent(CalibrationEvent item)
        => _hardwareRepository.CreateCalibrationEvent(item);

    public void UpdateCalibrationEvent(CalibrationEvent item)
        => _hardwareRepository.UpdateCalibrationEvent(item);

    public CalibrationEvent CompleteCalibrationEvent(CalibrationEvent item)
        => _hardwareRepository.CompleteCalibrationEvent(item);

    public CalibrationEvent? GetCalibrationEvent(int id)
        => _hardwareRepository.GetCalibrationEvent(id);

    public void DeleteCalibrationEvent(int id)
        => _hardwareRepository.DeleteCalibrationEvent(id);

    public List<CalibrationEvent> GetCalibrationEvents()
        => _hardwareRepository.GetCalibrationEvents();

    public List<CalibrationEvent> GetCalibrationEventsByHardwareItem(int hardwareItemId)
        => _hardwareRepository.GetCalibrationEventsByHardwareItem(hardwareItemId);

    public List<CalibrationEvent> GetOpenCalibrationEventsByHardwareItem(int hardwareItemId)
        => _hardwareRepository.GetOpenCalibrationEventsByHardwareItem(hardwareItemId);

    public List<CalibrationEvent> GetDueCalibrationEvents(DateTime nowUtc)
        => _hardwareRepository.GetDueCalibrationEvents(nowUtc);

    public CalibrationEvent? GetLatestCompletedCalibrationEvent(int hardwareItemId)
        => _hardwareRepository.GetLatestCompletedCalibrationEvent(hardwareItemId);

    public RiskEvent CreateRiskEvent(RiskEvent item)
        => _hardwareRepository.CreateRiskEvent(item);

    public void UpdateRiskEvent(RiskEvent item)
        => _hardwareRepository.UpdateRiskEvent(item);

    public RiskEvent? GetRiskEvent(int id)
        => _hardwareRepository.GetRiskEvent(id);

    public List<RiskEvent> GetRiskEvents()
        => _hardwareRepository.GetRiskEvents();

    public List<RiskEvent> GetOpenRiskEvents()
        => _hardwareRepository.GetOpenRiskEvents();

    public List<RiskEvent> GetRiskEventsByHardwareItem(int hardwareItemId)
        => _hardwareRepository.GetRiskEventsByHardwareItem(hardwareItemId);

    public List<RiskEvent> GetRiskEventsByTent(int tentId)
        => _hardwareRepository.GetRiskEventsByTent(tentId);

    public List<RiskEvent> GetRiskEventsByGrow(int growId)
        => _hardwareRepository.GetRiskEventsByGrow(growId);

    public List<RiskEvent> GetRiskEventsByStatus(RiskEventStatus status)
        => _hardwareRepository.GetRiskEventsByStatus(status);

    public RiskEvent? FindOpenRiskEventByDedupeKey(string dedupeKey)
        => _hardwareRepository.FindOpenRiskEventByDedupeKey(dedupeKey);

    public RiskEvent ResolveRiskEvent(int id, DateTime resolvedAtUtc, string? notes)
        => _hardwareRepository.ResolveRiskEvent(id, resolvedAtUtc, notes);

    public RiskEvent AcknowledgeRiskEvent(int id, DateTime acknowledgedAtUtc, string? notes)
        => _hardwareRepository.AcknowledgeRiskEvent(id, acknowledgedAtUtc, notes);

    public Setup CreateSetup(Setup setup)
        => _setupRepository.CreateSetup(setup);

    public Setup? GetSetup(int id)
        => _setupRepository.GetSetup(id);

    public List<Setup> GetSetups()
        => _setupRepository.GetSetups();

    public List<Setup> GetSetupsForTent(int tentId)
        => _setupRepository.GetSetupsForTent(tentId);

    public void UpdateSetup(Setup setup)
        => _setupRepository.UpdateSetup(setup);

    public Strain CreateStrain(Strain strain)
        => _setupRepository.CreateStrain(strain);

    public void UpdateStrain(Strain strain)
        => _setupRepository.UpdateStrain(strain);

    public Strain? GetStrain(int id)
        => _setupRepository.GetStrain(id);

    public List<Strain> GetStrains()
        => _setupRepository.GetStrains();

    public PlantInstance CreatePlant(PlantInstance plant)
        => _setupRepository.CreatePlant(plant);

    public PlantInstance CreateCloneFromMother(PlantInstance clone, int? motherSetupId, DateTime cutAt)
        => _setupRepository.CreateCloneFromMother(clone, motherSetupId, cutAt);

    public PlantInstance DecideQuarantinePlant(PlantInstance plant, int quarantineSetupId, string quarantineResult)
        => _setupRepository.DecideQuarantinePlant(plant, quarantineSetupId, quarantineResult);

    public void UpdatePlant(PlantInstance plant)
        => _setupRepository.UpdatePlant(plant);

    public void DeletePlant(int id)
        => _setupRepository.DeletePlant(id);

    public void DeleteStrain(int id)
        => _setupRepository.DeleteStrain(id);

    public int CountPlantsWithStrain(int strainId)
        => _setupRepository.CountPlantsWithStrain(strainId);

    public int CountCuringJarsWithStrain(int strainId)
        => _setupRepository.CountCuringJarsWithStrain(strainId);

    public void DeleteSetup(int id)
        => _setupRepository.DeleteSetup(id);

    public int CountPlantsInSetup(int setupId)
        => _setupRepository.CountPlantsInSetup(setupId);

    public int CountHardwareInSetup(int setupId)
        => _setupRepository.CountHardwareInSetup(setupId);

    public int CountGrowsWithSetup(int setupId)
        => _setupRepository.CountGrowsWithSetup(setupId);

    public int CountGrowsWithStrain(int strainId)
        => _setupRepository.CountGrowsWithStrain(strainId);

    public int CountPlantChildren(int parentPlantId)
        => _setupRepository.CountPlantChildren(parentPlantId);

    public PlantInstance? GetPlant(int id)
        => _setupRepository.GetPlant(id);

    public List<PlantInstance> GetPlants()
        => _setupRepository.GetPlants();

    public List<PlantInstance> GetPlantsBySetup(int setupId)
        => _setupRepository.GetPlantsBySetup(setupId);

    public List<PlantInstance> GetPlantsByGrow(int growId)
        => _setupRepository.GetPlantsByGrow(growId);

    public AutoMeasurementConfig CreateAutoMeasurementConfig(AutoMeasurementConfig config)
        => _autoMeasurementRepository.CreateAutoMeasurementConfig(config);

    public void UpdateAutoMeasurementConfig(AutoMeasurementConfig config)
        => _autoMeasurementRepository.UpdateAutoMeasurementConfig(config);

    public AutoMeasurementConfig? GetAutoMeasurementConfig(int id)
        => _autoMeasurementRepository.GetAutoMeasurementConfig(id);

    public void DeleteAutoMeasurementConfig(int id)
        => _autoMeasurementRepository.DeleteAutoMeasurementConfig(id);

    public List<AutoMeasurementConfig> GetAutoMeasurementConfigs()
        => _autoMeasurementRepository.GetAutoMeasurementConfigs();

    public List<AutoMeasurementConfig> GetAutoMeasurementConfigsByGrow(int growId)
        => _autoMeasurementRepository.GetAutoMeasurementConfigsByGrow(growId);

    public List<AutoMeasurementConfig> GetEnabledAutoMeasurementConfigs()
        => _autoMeasurementRepository.GetEnabledAutoMeasurementConfigs();

    public void ReplaceAutoMeasurementFieldMappings(int configId, IReadOnlyCollection<AutoMeasurementFieldMapping> mappings)
        => _autoMeasurementRepository.ReplaceAutoMeasurementFieldMappings(configId, mappings);

    public List<AutoMeasurementFieldMapping> GetAutoMeasurementFieldMappings(int configId)
        => _autoMeasurementRepository.GetAutoMeasurementFieldMappings(configId);

    public AutoMeasurementRun CreateAutoMeasurementRunIfNotExists(AutoMeasurementRun run)
        => _autoMeasurementRepository.CreateAutoMeasurementRunIfNotExists(run);

    public List<AutoMeasurementRun> GetAutoMeasurementRunsByConfig(int configId)
        => _autoMeasurementRepository.GetAutoMeasurementRunsByConfig(configId);

    public List<AutoMeasurementRun> GetAutoMeasurementRunsByGrow(int growId)
        => _autoMeasurementRepository.GetAutoMeasurementRunsByGrow(growId);

    public AutoMeasurementRun? GetAutoMeasurementRun(int configId, AutoMeasurementTriggerKind triggerKind, DateTime scheduledForUtc)
        => _autoMeasurementRepository.GetAutoMeasurementRun(configId, triggerKind, scheduledForUtc);

    public void UpdateAutoMeasurementRun(AutoMeasurementRun run)
        => _autoMeasurementRepository.UpdateAutoMeasurementRun(run);

    public LightSchedule CreateLightSchedule(LightSchedule schedule)
        => _lightRepository.CreateLightSchedule(schedule);

    public void UpdateLightSchedule(LightSchedule schedule)
        => _lightRepository.UpdateLightSchedule(schedule);

    public LightSchedule? GetLightSchedule(int id)
        => _lightRepository.GetLightSchedule(id);

    public void DeleteLightSchedule(int id)
        => _lightRepository.DeleteLightSchedule(id);

    public List<LightSchedule> GetLightSchedulesByTent(int tentId)
        => _lightRepository.GetLightSchedulesByTent(tentId);

    public LightSchedule? GetActiveLightScheduleForTent(int tentId)
        => _lightRepository.GetActiveLightScheduleForTent(tentId);

    public LightTransitionEvent CreateLightTransitionIfNotDuplicate(LightTransitionEvent transition)
        => _lightRepository.CreateLightTransitionIfNotDuplicate(transition);

    public List<LightTransitionEvent> GetLightTransitionsByTent(int tentId)
        => _lightRepository.GetLightTransitionsByTent(tentId);

    public List<LightTransitionEvent> GetLightTransitionsByTentAndKindSince(int tentId, LightTransitionKind kind, DateTime sinceUtc)
        => _lightRepository.GetLightTransitionsByTentAndKindSince(tentId, kind, sinceUtc);

    public LightTransitionEvent? GetLatestLightTransitionForTent(int tentId)
        => _lightRepository.GetLatestLightTransitionForTent(tentId);

    public LightTransitionEvent? GetLatestLightTransitionForTentAndKind(int tentId, LightTransitionKind kind)
        => _lightRepository.GetLatestLightTransitionForTentAndKind(tentId, kind);

    public AddbackLogEntry CreateAddbackLog(AddbackLogEntry entry)
        => _addbackRepository.CreateAddbackLog(entry);

    public List<AddbackLogEntry> GetAddbackLogsForGrow(int growId)
        => _addbackRepository.GetAddbackLogsForGrow(growId);

    public ChangeoutEntry CreateChangeout(ChangeoutEntry entry)
        => _addbackRepository.CreateChangeout(entry);

    public bool DeleteChangeout(int growId, int id)
        => _addbackRepository.DeleteChangeout(growId, id);

    public bool DeleteAddbackLog(int growId, int id)
        => _addbackRepository.DeleteAddbackLog(growId, id);

    public List<ChangeoutEntry> GetChangeoutsForGrow(int growId)
        => _addbackRepository.GetChangeoutsForGrow(growId);

    public SopInstance StartSopInstance(
        int growId,
        SopDefinition sopDefinition,
        SopStartSource source,
        string? sourceRecommendationKey,
        string? treatmentRecommendationStableKey,
        string? notes,
        IReadOnlyDictionary<string, string>? answers = null,
        IReadOnlyDictionary<string, int>? repeatCounts = null)
        => _sopRepository.StartSopInstance(
            growId, sopDefinition, source, sourceRecommendationKey,
            treatmentRecommendationStableKey, notes, answers, repeatCounts);

    public SopInstance? GetSopInstance(int id)
        => _sopRepository.GetSopInstance(id);

    public IReadOnlyList<int> DeleteSopInstance(int id)
        => _sopRepository.DeleteSopInstance(id);

    public List<SopInstance> GetSopInstancesByGrow(int growId)
        => _sopRepository.GetSopInstancesByGrow(growId);

    public List<SopInstance> GetActiveSopInstancesByGrow(int growId)
        => _sopRepository.GetActiveSopInstancesByGrow(growId);

    public List<SopStepInstance> GetSopStepInstances(int sopInstanceId)
        => _sopRepository.GetSopStepInstances(sopInstanceId);

    public SopStepInstance? GetSopStepInstance(int stepInstanceId)
        => _sopRepository.GetSopStepInstance(stepInstanceId);

    public SopStepInstance UpdateSopStepInstance(
        int stepInstanceId,
        SopStepInstanceStatus status,
        string? notes,
        int? measurementId,
        int? journalEntryId,
        int? photoAssetId)
        => _sopRepository.UpdateSopStepInstance(stepInstanceId, status, notes, measurementId, journalEntryId, photoAssetId);

    public void RecalculateSopInstanceStatus(int sopInstanceId)
        => _sopRepository.RecalculateSopInstanceStatus(sopInstanceId);

    public void UpdateSopStepReminderTaskId(int stepId, int taskId)
        => _sopRepository.UpdateSopStepReminderTaskId(stepId, taskId);

    public TentSensor? GetTentSensorByMetric(int tentId, SensorMetricType metricType)
        => _tentRepository.GetTentSensorByMetric(tentId, metricType);

    public List<GrowSystem> GetSystems(bool includeArchived = true)
        => _hydroSetupRepository.GetSystems(includeArchived);

    public GrowSystem? GetSystem(int id)
        => _hydroSetupRepository.GetSystem(id);

    public List<GrowSystem> GetHydroSetups(bool includeArchived = false)
        => _hydroSetupRepository.GetHydroSetups(includeArchived);

    public GrowSystem? GetHydroSetup(int id)
        => _hydroSetupRepository.GetHydroSetup(id);

    public List<GrowSystem> GetHydroSetupsByTent(int tentId, bool includeArchived = false)
        => _hydroSetupRepository.GetHydroSetupsByTent(tentId, includeArchived);

    public GrowSystem CreateSystem(GrowSystem system)
        => _hydroSetupRepository.CreateSystem(system);

    public GrowSystem CreateHydroSetup(GrowSystem system)
        => _hydroSetupRepository.CreateHydroSetup(system);

    public void UpdateSystem(GrowSystem system)
        => _hydroSetupRepository.UpdateSystem(system);

    public void UpdateHydroSetup(GrowSystem system)
        => _hydroSetupRepository.UpdateHydroSetup(system);

    public void ArchiveHydroSetup(int id)
        => _hydroSetupRepository.ArchiveHydroSetup(id);

    public void DeleteSystem(int id)
        => _hydroSetupRepository.DeleteSystem(id);

    public List<GrowRun> GetActiveGrows(string? search = null)
        => _growCoreRepository.GetActiveGrows(search);

    public List<GrowRun> GetArchivedGrows(string? search = null)
        => _growCoreRepository.GetArchivedGrows(search);

    public List<GrowRun> GetActiveGrowsForTent(int tentId)
        => _growCoreRepository.GetActiveGrowsForTent(tentId);

    public List<GrowRun> GetArchivedGrowsForTent(int tentId)
        => _growCoreRepository.GetArchivedGrowsForTent(tentId);

    public List<GrowRun> GetAllGrows()
        => _growCoreRepository.GetAllGrows();

    public GrowRun? GetGrow(int id)
        => _growCoreRepository.GetGrow(id);

    public Tent? GetTentForGrow(int growId)
        => _growCoreRepository.GetTentForGrow(growId);

    public int CreateGrow(GrowRun grow)
        => _growCoreRepository.CreateGrow(grow);

    public void UpdateGrow(GrowRun grow)
        => _growCoreRepository.UpdateGrow(grow);

    public void DeleteGrow(int id)
        => _growCoreRepository.DeleteGrow(id);

    public List<Measurement> GetMeasurementsForGrow(int growId)
        => _measurementRepository.GetMeasurementsForGrow(growId);

    public List<Measurement> GetMeasurementsForTent(int tentId, int limit = 200)
        => _measurementRepository.GetMeasurementsForTent(tentId, limit);

    public Measurement? GetMeasurement(int id)
        => _measurementRepository.GetMeasurement(id);

    public Measurement? GetLatestMeasurement(int growId)
        => _measurementRepository.GetLatestMeasurement(growId);

    public Measurement? GetPreviousMeasurement(int growId, DateTime beforeTakenAt, int currentMeasurementId = 0)
        => _measurementRepository.GetPreviousMeasurement(growId, beforeTakenAt, currentMeasurementId);

    public int CreateMeasurement(Measurement measurement)
        => _measurementRepository.CreateMeasurement(measurement);

    public void UpdateMeasurement(Measurement measurement)
        => _measurementRepository.UpdateMeasurement(measurement);

    public void DeleteMeasurement(int id)
        => _measurementRepository.DeleteMeasurement(id);

    public List<PhotoAsset> GetPhotosForGrow(int growId)
        => _photoRepository.GetPhotosForGrow(growId);

    public List<PhotoAsset> GetPhotosForMeasurement(int measurementId)
        => _photoRepository.GetPhotosForMeasurement(measurementId);

    public void AddPhoto(PhotoAsset photo)
        => _photoRepository.AddPhoto(photo);


    public List<PhotoAsset> GetRecentPhotos(int limit = 18)
        => _photoRepository.GetRecentPhotos(limit);

    public HomeAssistantSettings GetHomeAssistantSettings()
        => _homeAssistantSettingsRepository.GetHomeAssistantSettings();

    public HomeAssistantSettings GetEffectiveHomeAssistantSettings()
        => _homeAssistantSettingsRepository.GetEffectiveHomeAssistantSettings();

    public void SaveHomeAssistantSettings(HomeAssistantSettings settings)
        => _homeAssistantSettingsRepository.SaveHomeAssistantSettings(settings);

    public void AddTentSensorSnapshot(TentSensorSnapshot snapshot, TimeSpan? dedupeWindow = null)
        => _tentRepository.AddTentSensorSnapshot(snapshot, dedupeWindow);

    public List<TentSensorSnapshot> GetTentSensorSnapshots(int tentId, IEnumerable<string>? metricKeys = null, int limitPerMetric = 48)
        => _tentRepository.GetTentSensorSnapshots(tentId, metricKeys, limitPerMetric);

}
