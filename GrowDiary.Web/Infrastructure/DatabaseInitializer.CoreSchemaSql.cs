namespace GrowDiary.Web.Infrastructure;

public sealed partial class DatabaseInitializer
{
    private const string CoreSchemaSql = """
            CREATE TABLE IF NOT EXISTS Grows (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TentId INTEGER NULL,
                SetupId INTEGER NULL,
                Name TEXT NOT NULL,
                Strain TEXT NULL,
                Breeder TEXT NULL,
                Status TEXT NOT NULL,
                MediumType TEXT NOT NULL,
                FeedingStyle TEXT NOT NULL,
                HydroStyle TEXT NOT NULL,
                MediumDetail TEXT NULL,
                Environment TEXT NOT NULL,
                Light TEXT NULL,
                ContainerSize TEXT NULL,
                ReservoirSize TEXT NULL,
                IrrigationStyle TEXT NULL,
                Nutrients TEXT NULL,
                Notes TEXT NULL,
                StartDate TEXT NOT NULL,
                EndDate TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                TentSnapshotJson TEXT NULL,
                HydroSetupSnapshotJson TEXT NULL,
                SnapshotsCapturedAtUtc TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS Measurements (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                GrowId INTEGER NOT NULL,
                TakenAt TEXT NOT NULL,
                Stage TEXT NOT NULL,
                Source TEXT NOT NULL DEFAULT 'Manual',
                Notes TEXT NULL,
                AirTemperatureC REAL NULL,
                HumidityPercent REAL NULL,
                HeightCm REAL NULL,
                WaterAmountMl REAL NULL,
                RunoffAmountMl REAL NULL,
                IrrigationPh REAL NULL,
                IrrigationEc REAL NULL,
                DrainPh REAL NULL,
                DrainEc REAL NULL,
                ReservoirPh REAL NULL,
                ReservoirEc REAL NULL,
                ReservoirWaterTempC REAL NULL,
                ReservoirLevelCm REAL NULL,
                ReservoirLevelLiters REAL NULL,
                DissolvedOxygenMgL REAL NULL,
                AirflowAtLeafMPerMin REAL NULL,
                WaterFlow TEXT NULL,
                OrpMv REAL NULL,
                TopOffLiters REAL NULL,
                AddbackEc REAL NULL,
                SolutionChange INTEGER NOT NULL DEFAULT 0,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (GrowId) REFERENCES Grows (Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS Photos (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                GrowId INTEGER NOT NULL,
                MeasurementId INTEGER NULL,
                RelativePath TEXT NOT NULL,
                Caption TEXT NULL,
                Tag TEXT NOT NULL DEFAULT 'Overview',
                Source TEXT NOT NULL DEFAULT 'Manual',
                IsReferenceShot INTEGER NOT NULL DEFAULT 0,
                SymptomId TEXT NULL,
                TakenAtUtc TEXT NOT NULL,
                FOREIGN KEY (GrowId) REFERENCES Grows (Id) ON DELETE CASCADE,
                FOREIGN KEY (MeasurementId) REFERENCES Measurements (Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS Tents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Kind TEXT NOT NULL DEFAULT 'Grow Tent',
                TentType TEXT NOT NULL DEFAULT 'MultiPurpose',
                Status TEXT NOT NULL DEFAULT 'Active',
                Notes TEXT NULL,
                DisplayOrder INTEGER NOT NULL DEFAULT 99,
                AccentColor TEXT NOT NULL DEFAULT '#69b578',
                WidthCm INTEGER NULL,
                DepthCm INTEGER NULL,
                TentHeightCm INTEGER NULL,
                LightType TEXT NULL,
                LightWatt INTEGER NULL,
                LightController TEXT NULL,
                LightControllerEntityId TEXT NULL,
                ExhaustFanCount INTEGER NULL,
                ExhaustM3h INTEGER NULL,
                CirculationFanCount INTEGER NULL,
                HvacController TEXT NULL,
                HvacControllerEntityId TEXT NULL,
                Co2Available INTEGER NOT NULL DEFAULT 0,
                CameraEntityId TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Setups (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TentId INTEGER NOT NULL,
                Name TEXT NOT NULL,
                SetupType TEXT NOT NULL,
                Status TEXT NOT NULL,
                Notes TEXT NULL,
                CloneCounterTotal INTEGER NULL,
                LastCloneCutAt TEXT NULL,
                MotherHealthStatus TEXT NULL,
                QuarantineStartedAt TEXT NULL,
                QuarantinePlannedEndAt TEXT NULL,
                QuarantineResult TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (TentId) REFERENCES Tents (Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS Strains (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Breeder TEXT NULL,
                Dominance TEXT NOT NULL DEFAULT 'Unknown',
                FlowerWeeksMin INTEGER NULL,
                FlowerWeeksMax INTEGER NULL,
                Notes TEXT NULL,
                NutrientDemandFactor REAL NULL,
                StretchFactor REAL NULL,
                VpdPreferenceShift REAL NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS PhenoEvaluations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PlantInstanceId INTEGER NOT NULL UNIQUE,
                VigorScore INTEGER NULL,
                InternodeSpacing TEXT NOT NULL DEFAULT 'Unknown',
                BranchingScore INTEGER NULL,
                LeafToBudScore INTEGER NULL,
                HeightAtFlipCm REAL NULL,
                TrainingMethods TEXT NULL,
                TrainingResponseScore INTEGER NULL,
                StressToleranceScore INTEGER NULL,
                PestResistanceScore INTEGER NULL,
                FloweringDays INTEGER NULL,
                HeightAtHarvestCm REAL NULL,
                WetYieldG REAL NULL,
                DryYieldG REAL NULL,
                BudDensityScore INTEGER NULL,
                ResinScore INTEGER NULL,
                TrimEaseScore INTEGER NULL,
                AromaScore INTEGER NULL,
                AromaNotes TEXT NULL,
                FlavorScore INTEGER NULL,
                EffectScore INTEGER NULL,
                EffectNotes TEXT NULL,
                ThcPercent REAL NULL,
                CbdPercent REAL NULL,
                TerpeneNotes TEXT NULL,
                ManualOverallScore REAL NULL,
                IsKeeper INTEGER NOT NULL DEFAULT 0,
                ConfirmedInSecondRun INTEGER NOT NULL DEFAULT 0,
                Notes TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (PlantInstanceId) REFERENCES PlantInstances (Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS PlantInstances (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StrainId INTEGER NULL,
                SetupId INTEGER NULL,
                GrowId INTEGER NULL,
                SiteIndex INTEGER NULL,
                ParentPlantId INTEGER NULL,
                Label TEXT NOT NULL,
                PlantRole TEXT NOT NULL,
                PlantStatus TEXT NOT NULL,
                PhenoLabel TEXT NULL,
                StartedAt TEXT NULL,
                EndedAt TEXT NULL,
                Notes TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (StrainId) REFERENCES Strains (Id) ON DELETE SET NULL,
                FOREIGN KEY (SetupId) REFERENCES Setups (Id) ON DELETE SET NULL,
                FOREIGN KEY (GrowId) REFERENCES Grows (Id) ON DELETE SET NULL,
                FOREIGN KEY (ParentPlantId) REFERENCES PlantInstances (Id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS AppSettings (
                Key TEXT PRIMARY KEY,
                Value TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS AppliedSchemaMigrations (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                RequiredForSchemaVersion TEXT NOT NULL,
                AppliedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS TentSensorSnapshots (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TentId INTEGER NOT NULL,
                MetricKey TEXT NOT NULL,
                Value REAL NOT NULL,
                Unit TEXT NULL,
                CapturedAtUtc TEXT NOT NULL,
                FOREIGN KEY (TentId) REFERENCES Tents (Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS TentSensors (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TentId INTEGER NOT NULL,
                MetricType TEXT NOT NULL,
                HaEntityId TEXT NOT NULL,
                DisplayLabel TEXT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (TentId) REFERENCES Tents(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_TentSensors_TentId
                ON TentSensors(TentId);

            CREATE TABLE IF NOT EXISTS JournalEntries (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                GrowId INTEGER NOT NULL,
                MeasurementId INTEGER NULL,
                Title TEXT NULL,
                Body TEXT NULL,
                EntryType TEXT NOT NULL,
                Source TEXT NOT NULL DEFAULT 'Manual',
                OccurredAtUtc TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (GrowId) REFERENCES Grows (Id) ON DELETE CASCADE,
                FOREIGN KEY (MeasurementId) REFERENCES Measurements (Id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS GrowTasks (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                GrowId INTEGER NOT NULL,
                Title TEXT NOT NULL,
                Notes TEXT NULL,
                DueAtUtc TEXT NULL,
                Priority TEXT NOT NULL DEFAULT 'Normal',
                Status TEXT NOT NULL DEFAULT 'Open',
                CreatedAtUtc TEXT NOT NULL,
                CompletedAtUtc TEXT NULL,
                FOREIGN KEY (GrowId) REFERENCES Grows (Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS SopInstances (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                GrowId INTEGER NOT NULL,
                SopId TEXT NOT NULL,
                SopName TEXT NOT NULL,
                SopType TEXT NOT NULL,
                Status TEXT NOT NULL,
                Source TEXT NOT NULL,
                SourceRecommendationKey TEXT NULL,
                TreatmentRecommendationStableKey TEXT NULL,
                StartedAtUtc TEXT NOT NULL,
                CompletedAtUtc TEXT NULL,
                CancelledAtUtc TEXT NULL,
                Notes TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (GrowId) REFERENCES Grows (Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS SopStepInstances (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SopInstanceId INTEGER NOT NULL,
                StepId TEXT NOT NULL,
                "Order" INTEGER NOT NULL,
                Title TEXT NOT NULL,
                Description TEXT NULL,
                StepType TEXT NOT NULL,
                Status TEXT NOT NULL,
                WaitMinutes INTEGER NULL,
                SubSopId TEXT NULL,
                ExpectedInputsJson TEXT NULL,
                PhotoRequired INTEGER NOT NULL DEFAULT 0,
                PhotoRecommended INTEGER NOT NULL DEFAULT 0,
                StartedAtUtc TEXT NULL,
                CompletedAtUtc TEXT NULL,
                SkippedAtUtc TEXT NULL,
                Notes TEXT NULL,
                MeasurementId INTEGER NULL,
                JournalEntryId INTEGER NULL,
                PhotoAssetId INTEGER NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (SopInstanceId) REFERENCES SopInstances (Id) ON DELETE CASCADE,
                FOREIGN KEY (MeasurementId) REFERENCES Measurements (Id) ON DELETE SET NULL,
                FOREIGN KEY (JournalEntryId) REFERENCES JournalEntries (Id) ON DELETE SET NULL,
                FOREIGN KEY (PhotoAssetId) REFERENCES Photos (Id) ON DELETE SET NULL
            );

            CREATE INDEX IF NOT EXISTS IX_SopInstances_GrowId_Status
                ON SopInstances(GrowId, Status);

            CREATE INDEX IF NOT EXISTS IX_SopInstances_GrowId_SopId_Status
                ON SopInstances(GrowId, SopId, Status);

            CREATE INDEX IF NOT EXISTS IX_SopStepInstances_SopInstanceId
                ON SopStepInstances(SopInstanceId);

            CREATE INDEX IF NOT EXISTS IX_SopStepInstances_SopInstanceId_Order
                ON SopStepInstances(SopInstanceId, "Order");

            CREATE TABLE IF NOT EXISTS AuditEntries (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                GrowId INTEGER NOT NULL,
                EntityType TEXT NOT NULL,
                EntityId INTEGER NULL,
                Action TEXT NOT NULL,
                Summary TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (GrowId) REFERENCES Grows (Id) ON DELETE CASCADE
            );



            CREATE TABLE IF NOT EXISTS SystemAuditEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                EventType TEXT NOT NULL,
                Action TEXT NOT NULL,
                Summary TEXT NOT NULL,
                Severity TEXT NOT NULL,
                Source TEXT NOT NULL,
                RemoteAddress TEXT NULL,
                RelatedGrowId INTEGER NULL,
                RelatedFileName TEXT NULL,
                Success INTEGER NOT NULL DEFAULT 1,
                CreatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (RelatedGrowId) REFERENCES Grows (Id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS HarvestEntries (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                GrowId INTEGER NOT NULL,
                HarvestedAt TEXT NOT NULL,
                WetWeightG REAL NULL,
                DryWeightG REAL NULL,
                DryDays INTEGER NULL,
                YieldNotes TEXT NULL,
                Rating REAL NULL,
                FlavorNotes TEXT NULL,
                EffectNotes TEXT NULL,
                NugStructure TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (GrowId) REFERENCES Grows (Id) ON DELETE CASCADE
            );

            -- Das Aushaerten im Glas. Bis beta.42 endete die Begleitung beim
            -- Trockengewicht — ausgerechnet vor dem Schritt, der ueber die
            -- Qualitaet von Monaten Arbeit entscheidet.
            -- Ein Glas, nicht ein Grow: mehrere Sorten haerten getrennt aus,
            -- und auch eine Sorte fuellt oft mehrere Glaeser.
            CREATE TABLE IF NOT EXISTS CuringJars (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                GrowId INTEGER NOT NULL,
                Label TEXT NOT NULL,
                StrainId INTEGER NULL,
                FilledAtUtc TEXT NOT NULL,
                WeightG REAL NULL,
                HasHumidityPack INTEGER NOT NULL DEFAULT 0,
                FinishedAtUtc TEXT NULL,
                Notes TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (GrowId) REFERENCES Grows (Id) ON DELETE CASCADE,
                FOREIGN KEY (StrainId) REFERENCES Strains (Id) ON DELETE SET NULL
            );

            -- Ablesung und Lueften sind zwei Dinge: wer lueftet ohne abzulesen,
            -- hat den Rhythmus gehalten und nichts gelernt. Beide Felder sind
            -- deshalb einzeln erlaubt.
            CREATE TABLE IF NOT EXISTS CuringReadings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                JarId INTEGER NOT NULL,
                ReadAtUtc TEXT NOT NULL,
                HumidityPercent REAL NULL,
                BurpedMinutes INTEGER NULL,
                Note TEXT NULL,
                Source TEXT NOT NULL DEFAULT 'Manual',
                CreatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (JarId) REFERENCES CuringJars (Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS TentSensorReadings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TentId INTEGER NOT NULL,
                MetricKey TEXT NOT NULL,
                Value REAL NOT NULL,
                Unit TEXT,
                CapturedAtUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_TentSensorReadings_TentMetric
                ON TentSensorReadings(TentId, MetricKey, CapturedAtUtc);

            CREATE TABLE IF NOT EXISTS TentSensorDailyStats (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TentId INTEGER NOT NULL,
                MetricKey TEXT NOT NULL,
                Date TEXT NOT NULL,
                Min REAL NOT NULL,
                Max REAL NOT NULL,
                Median REAL NOT NULL,
                P5 REAL NOT NULL,
                P95 REAL NOT NULL,
                Avg REAL NOT NULL,
                Count INTEGER NOT NULL,
                Unit TEXT
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_TentSensorDailyStats_Unique
                ON TentSensorDailyStats(TentId, MetricKey, Date);

            CREATE TABLE IF NOT EXISTS LightSchedules (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TentId INTEGER NOT NULL,
                Name TEXT NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                LightsOnTime TEXT NOT NULL,
                LightsOffTime TEXT NOT NULL,
                TimeZoneId TEXT NULL,
                Source TEXT NOT NULL DEFAULT 'Manual',
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (TentId) REFERENCES Tents (Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS LightTransitionEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TentId INTEGER NOT NULL,
                Kind TEXT NOT NULL,
                OccurredAtUtc TEXT NOT NULL,
                Source TEXT NOT NULL DEFAULT 'HomeAssistant',
                RawState TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (TentId) REFERENCES Tents (Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS AutoMeasurementConfigs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                GrowId INTEGER NOT NULL,
                TentId INTEGER NULL,
                Name TEXT NOT NULL,
                Status TEXT NOT NULL DEFAULT 'Enabled',
                TriggerKind TEXT NOT NULL DEFAULT 'Manual',
                DelayMinutes INTEGER NULL,
                WindowMinutes INTEGER NOT NULL DEFAULT 20,
                CaptureSnapshot INTEGER NOT NULL DEFAULT 0,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (GrowId) REFERENCES Grows (Id) ON DELETE CASCADE,
                FOREIGN KEY (TentId) REFERENCES Tents (Id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS AutoMeasurementFieldMappings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ConfigId INTEGER NOT NULL,
                MeasurementField TEXT NOT NULL,
                MetricKey TEXT NOT NULL,
                Aggregation TEXT NOT NULL DEFAULT 'Latest',
                IsRequired INTEGER NOT NULL DEFAULT 0,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (ConfigId) REFERENCES AutoMeasurementConfigs (Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS AutoMeasurementRuns (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ConfigId INTEGER NOT NULL,
                GrowId INTEGER NOT NULL,
                TriggerKind TEXT NOT NULL,
                ScheduledForUtc TEXT NOT NULL,
                MeasurementId INTEGER NULL,
                Status TEXT NOT NULL DEFAULT 'Pending',
                ErrorMessage TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (ConfigId) REFERENCES AutoMeasurementConfigs (Id) ON DELETE CASCADE,
                FOREIGN KEY (GrowId) REFERENCES Grows (Id) ON DELETE CASCADE,
                FOREIGN KEY (MeasurementId) REFERENCES Measurements (Id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS HardwareItems (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Category TEXT NOT NULL,
                Status TEXT NOT NULL,
                Criticality TEXT NOT NULL,
                TentId INTEGER NULL,
                SetupId INTEGER NULL,
                HydroSetupId INTEGER NULL,
                GrowId INTEGER NULL,
                WearTemplateId TEXT NULL,
                TentSensorId INTEGER NULL,
                HaEntityId TEXT NULL,
                Manufacturer TEXT NULL,
                Model TEXT NULL,
                SerialNumber TEXT NULL,
                InstalledAtUtc TEXT NULL,
                RetiredAtUtc TEXT NULL,
                ExpectedLifespanDays INTEGER NULL,
                InspectionIntervalDays INTEGER NULL,
                CalibrationIntervalDays INTEGER NULL,
                Notes TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS MaintenanceEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                HardwareItemId INTEGER NOT NULL,
                EventType TEXT NOT NULL,
                Status TEXT NOT NULL,
                Result TEXT NOT NULL,
                Title TEXT NOT NULL,
                Description TEXT NULL,
                DueAtUtc TEXT NULL,
                PerformedAtUtc TEXT NULL,
                NextDueAtUtc TEXT NULL,
                GrowTaskId INTEGER NULL,
                SopInstanceId INTEGER NULL,
                Notes TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS CalibrationEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                HardwareItemId INTEGER NOT NULL,
                CalibrationType TEXT NOT NULL,
                Status TEXT NOT NULL,
                Result TEXT NOT NULL,
                Title TEXT NOT NULL,
                ReferenceSolution TEXT NULL,
                ReferenceValue REAL NULL,
                BeforeValue REAL NULL,
                AfterValue REAL NULL,
                PointsJson TEXT NULL,
                TemperatureC REAL NULL,
                DueAtUtc TEXT NULL,
                PerformedAtUtc TEXT NULL,
                NextDueAtUtc TEXT NULL,
                GrowTaskId INTEGER NULL,
                Notes TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS RiskEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                EventType TEXT NOT NULL,
                Severity TEXT NOT NULL,
                Status TEXT NOT NULL,
                Source TEXT NOT NULL,
                Title TEXT NOT NULL,
                Description TEXT NULL,
                HardwareItemId INTEGER NULL,
                TentId INTEGER NULL,
                GrowId INTEGER NULL,
                TentSensorId INTEGER NULL,
                HaEntityId TEXT NULL,
                SopInstanceId INTEGER NULL,
                GrowTaskId INTEGER NULL,
                StartedAtUtc TEXT NOT NULL,
                LastSeenAtUtc TEXT NULL,
                ResolvedAtUtc TEXT NULL,
                AcknowledgedAtUtc TEXT NULL,
                DedupeKey TEXT NULL,
                RawValue TEXT NULL,
                Notes TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS AddbackLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                GrowId INTEGER NOT NULL,
                HydroSetupId INTEGER NULL,
                Kind TEXT NOT NULL DEFAULT 'Addback',
                PerformedAtUtc TEXT NOT NULL,
                ReservoirLiters REAL NULL,
                EcBefore REAL NULL,
                EcTarget REAL NULL,
                EcStock REAL NULL,
                EcAfter REAL NULL,
                PhBefore REAL NULL,
                PhAfter REAL NULL,
                LitersAdded REAL NULL,
                NewReservoirVolumeLiters REAL NULL,
                UsedHydroSetupVolume INTEGER NOT NULL DEFAULT 0,
                Notes TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (GrowId) REFERENCES Grows (Id) ON DELETE CASCADE,
                FOREIGN KEY (HydroSetupId) REFERENCES GrowSystems (Id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS ChangeoutEntries (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                GrowId INTEGER NOT NULL,
                HydroSetupId INTEGER NULL,
                Kind TEXT NOT NULL DEFAULT 'Partial',
                PerformedAtUtc TEXT NOT NULL,
                VolumeChangedLiters REAL NULL,
                PercentChanged REAL NULL,
                EcBefore REAL NULL,
                EcAfter REAL NULL,
                PhBefore REAL NULL,
                PhAfter REAL NULL,
                Notes TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (GrowId) REFERENCES Grows (Id) ON DELETE CASCADE,
                FOREIGN KEY (HydroSetupId) REFERENCES GrowSystems (Id) ON DELETE SET NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Measurements_GrowId_TakenAt ON Measurements(GrowId, TakenAt DESC);
            CREATE INDEX IF NOT EXISTS IX_AddbackLogs_GrowId_PerformedAt ON AddbackLogs(GrowId, PerformedAtUtc DESC);
            CREATE INDEX IF NOT EXISTS IX_AddbackLogs_HydroSetupId ON AddbackLogs(HydroSetupId);
            CREATE INDEX IF NOT EXISTS IX_ChangeoutEntries_GrowId_PerformedAt ON ChangeoutEntries(GrowId, PerformedAtUtc DESC);
            CREATE INDEX IF NOT EXISTS IX_ChangeoutEntries_HydroSetupId ON ChangeoutEntries(HydroSetupId);
            CREATE INDEX IF NOT EXISTS IX_Photos_GrowId_TakenAt ON Photos(GrowId, TakenAtUtc DESC);
            CREATE INDEX IF NOT EXISTS IX_Setups_TentId_Status ON Setups(TentId, Status);
            CREATE INDEX IF NOT EXISTS IX_PlantInstances_SetupId ON PlantInstances(SetupId);
            CREATE INDEX IF NOT EXISTS IX_PlantInstances_GrowId ON PlantInstances(GrowId);
            CREATE INDEX IF NOT EXISTS IX_PlantInstances_ParentPlantId ON PlantInstances(ParentPlantId);
            CREATE INDEX IF NOT EXISTS IX_PlantInstances_StrainId ON PlantInstances(StrainId);
            CREATE INDEX IF NOT EXISTS IX_TentSensorSnapshots_TentId_MetricKey_CapturedAtUtc ON TentSensorSnapshots(TentId, MetricKey, CapturedAtUtc DESC);
            CREATE INDEX IF NOT EXISTS IX_JournalEntries_GrowId_OccurredAtUtc ON JournalEntries(GrowId, OccurredAtUtc DESC);
            CREATE INDEX IF NOT EXISTS IX_GrowTasks_GrowId_Status_DueAtUtc ON GrowTasks(GrowId, Status, DueAtUtc);
            CREATE INDEX IF NOT EXISTS IX_CuringJars_GrowId_FinishedAtUtc ON CuringJars(GrowId, FinishedAtUtc);
            CREATE INDEX IF NOT EXISTS IX_CuringReadings_JarId_ReadAtUtc ON CuringReadings(JarId, ReadAtUtc DESC);
            CREATE INDEX IF NOT EXISTS IX_AuditEntries_GrowId_CreatedAtUtc ON AuditEntries(GrowId, CreatedAtUtc DESC);
            CREATE INDEX IF NOT EXISTS IX_SystemAuditEvents_CreatedAtUtc ON SystemAuditEvents(CreatedAtUtc DESC);
            CREATE INDEX IF NOT EXISTS IX_SystemAuditEvents_EventType_CreatedAtUtc ON SystemAuditEvents(EventType, CreatedAtUtc DESC);
            CREATE INDEX IF NOT EXISTS IX_SystemAuditEvents_RelatedGrowId_CreatedAtUtc ON SystemAuditEvents(RelatedGrowId, CreatedAtUtc DESC);
            CREATE INDEX IF NOT EXISTS IX_AutoMeasurementConfigs_GrowId ON AutoMeasurementConfigs(GrowId);
            CREATE INDEX IF NOT EXISTS IX_AutoMeasurementFieldMappings_ConfigId ON AutoMeasurementFieldMappings(ConfigId);
            CREATE UNIQUE INDEX IF NOT EXISTS IX_AutoMeasurementRuns_ConfigTriggerSchedule ON AutoMeasurementRuns(ConfigId, TriggerKind, ScheduledForUtc);
            CREATE INDEX IF NOT EXISTS IX_AutoMeasurementRuns_GrowId ON AutoMeasurementRuns(GrowId);
            CREATE INDEX IF NOT EXISTS IX_LightSchedules_TentId ON LightSchedules(TentId);
            CREATE INDEX IF NOT EXISTS IX_LightSchedules_TentActive ON LightSchedules(TentId, IsActive);
            CREATE INDEX IF NOT EXISTS IX_LightTransitionEvents_TentKindOccurred ON LightTransitionEvents(TentId, Kind, OccurredAtUtc);
            CREATE INDEX IF NOT EXISTS IX_HardwareItems_TentId ON HardwareItems(TentId);
            CREATE INDEX IF NOT EXISTS IX_HardwareItems_SetupId ON HardwareItems(SetupId);
            CREATE INDEX IF NOT EXISTS IX_HardwareItems_GrowId ON HardwareItems(GrowId);
            CREATE INDEX IF NOT EXISTS IX_HardwareItems_WearTemplateId ON HardwareItems(WearTemplateId);
            CREATE INDEX IF NOT EXISTS IX_HardwareItems_Status ON HardwareItems(Status);
            CREATE INDEX IF NOT EXISTS IX_HardwareItems_TentSensorId ON HardwareItems(TentSensorId);
            CREATE INDEX IF NOT EXISTS IX_MaintenanceEvents_HardwareItemId ON MaintenanceEvents(HardwareItemId);
            CREATE INDEX IF NOT EXISTS IX_MaintenanceEvents_Status ON MaintenanceEvents(Status);
            CREATE INDEX IF NOT EXISTS IX_MaintenanceEvents_DueAtUtc ON MaintenanceEvents(DueAtUtc);
            CREATE INDEX IF NOT EXISTS IX_MaintenanceEvents_NextDueAtUtc ON MaintenanceEvents(NextDueAtUtc);
            CREATE INDEX IF NOT EXISTS IX_MaintenanceEvents_GrowTaskId ON MaintenanceEvents(GrowTaskId);
            CREATE INDEX IF NOT EXISTS IX_MaintenanceEvents_SopInstanceId ON MaintenanceEvents(SopInstanceId);
            CREATE INDEX IF NOT EXISTS IX_CalibrationEvents_HardwareItemId ON CalibrationEvents(HardwareItemId);
            CREATE INDEX IF NOT EXISTS IX_CalibrationEvents_Status ON CalibrationEvents(Status);
            CREATE INDEX IF NOT EXISTS IX_CalibrationEvents_DueAtUtc ON CalibrationEvents(DueAtUtc);
            CREATE INDEX IF NOT EXISTS IX_CalibrationEvents_NextDueAtUtc ON CalibrationEvents(NextDueAtUtc);
            CREATE INDEX IF NOT EXISTS IX_CalibrationEvents_GrowTaskId ON CalibrationEvents(GrowTaskId);
            CREATE INDEX IF NOT EXISTS IX_CalibrationEvents_CalibrationType ON CalibrationEvents(CalibrationType);
            CREATE INDEX IF NOT EXISTS IX_RiskEvents_Status ON RiskEvents(Status);
            CREATE INDEX IF NOT EXISTS IX_RiskEvents_Severity ON RiskEvents(Severity);
            CREATE INDEX IF NOT EXISTS IX_RiskEvents_EventType ON RiskEvents(EventType);
            CREATE INDEX IF NOT EXISTS IX_RiskEvents_HardwareItemId ON RiskEvents(HardwareItemId);
            CREATE INDEX IF NOT EXISTS IX_RiskEvents_TentId ON RiskEvents(TentId);
            CREATE INDEX IF NOT EXISTS IX_RiskEvents_GrowId ON RiskEvents(GrowId);
            CREATE INDEX IF NOT EXISTS IX_RiskEvents_TentSensorId ON RiskEvents(TentSensorId);
            CREATE INDEX IF NOT EXISTS IX_RiskEvents_DedupeKey_Status ON RiskEvents(DedupeKey, Status);
            CREATE INDEX IF NOT EXISTS IX_RiskEvents_StartedAtUtc ON RiskEvents(StartedAtUtc);

            CREATE TABLE IF NOT EXISTS TentAlertRules (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TentId INTEGER NOT NULL,
                MetricKey TEXT NOT NULL,
                MinValue REAL NULL,
                MaxValue REAL NULL,
                NotifyService TEXT NOT NULL,
                Enabled INTEGER NOT NULL DEFAULT 1,
                CooldownMinutes INTEGER NOT NULL DEFAULT 30,
                LastState TEXT NULL,
                LastNotifiedUtc TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (TentId) REFERENCES Tents(Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS IX_TentAlertRules_TentId ON TentAlertRules(TentId);

            CREATE TABLE IF NOT EXISTS DosingPumps (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TentId INTEGER NOT NULL,
                Name TEXT NOT NULL,
                Purpose TEXT NOT NULL DEFAULT 'Custom',
                Agent TEXT NULL,
                ConcentrationPercent REAL NULL,
                HaEntityId TEXT NOT NULL,
                MlPerMinute REAL NULL,
                CalibratedAtUtc TEXT NULL,
                TubeChangedAtUtc TEXT NULL,
                CalibrationIntervalDays INTEGER NULL,
                TubeIntervalDays INTEGER NULL,
                MaxSingleDoseMl REAL NOT NULL DEFAULT 5,
                MinIntervalMinutes INTEGER NOT NULL DEFAULT 18,
                MaxDosesPerDay INTEGER NOT NULL DEFAULT 6,
                MaxMlPerDay REAL NOT NULL DEFAULT 25,
                MaxReadingAgeMinutes INTEGER NOT NULL DEFAULT 10,
                AutomationEnabled INTEGER NOT NULL DEFAULT 0,
                HasHomeAssistantAutoOff INTEGER NOT NULL DEFAULT 0,
                SimulationMode INTEGER NOT NULL DEFAULT 0,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                FOREIGN KEY (TentId) REFERENCES Tents(Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS IX_DosingPumps_TentId ON DosingPumps(TentId);

            -- Die zweite Haelfte eines Zweikomponenten-Duengers wartet hier, bis
            -- die Trennzeit um ist. Bewusst in der Datenbank und nicht im
            -- Speicher: ein Neustart zwischen A und B wuerde B sonst wortlos
            -- verschlucken, und im Becken staende A ohne B.
            CREATE TABLE IF NOT EXISTS PendingDoses (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PumpId INTEGER NOT NULL,
                Ml REAL NOT NULL,
                DueAtUtc TEXT NOT NULL,
                SourceDoseEventId INTEGER NULL,
                Reason TEXT NULL,
                CreatedAtUtc TEXT NOT NULL,
                Fehlversuche INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (PumpId) REFERENCES DosingPumps(Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS IX_PendingDoses_DueAtUtc ON PendingDoses(DueAtUtc);

            CREATE TABLE IF NOT EXISTS DoseEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PumpId INTEGER NOT NULL,
                TentId INTEGER NOT NULL,
                GrowId INTEGER NULL,
                OccurredAtUtc TEXT NOT NULL,
                Trigger TEXT NOT NULL,
                Outcome TEXT NOT NULL,
                RequestedMl REAL NOT NULL DEFAULT 0,
                DosedMl REAL NOT NULL DEFAULT 0,
                SecondsRun REAL NOT NULL DEFAULT 0,
                ValueBefore REAL NULL,
                ValueAfter REAL NULL,
                TargetValue REAL NULL,
                Reason TEXT NULL,
                Simulated INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (PumpId) REFERENCES DosingPumps(Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS IX_DoseEvents_PumpId_OccurredAtUtc ON DoseEvents(PumpId, OccurredAtUtc);
            CREATE INDEX IF NOT EXISTS IX_DoseEvents_TentId_OccurredAtUtc ON DoseEvents(TentId, OccurredAtUtc);

            CREATE TABLE IF NOT EXISTS SetpointProfiles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                BaseProfileId TEXT NOT NULL DEFAULT 'rdwc-default',
                OverridesJson TEXT NOT NULL DEFAULT '{}',
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
        """;

    private const string GrowIndexSql = """
            CREATE INDEX IF NOT EXISTS IX_Grows_TentId_Status ON Grows(TentId, Status);
            CREATE INDEX IF NOT EXISTS IX_Grows_SetupId ON Grows(SetupId);
        """;
}