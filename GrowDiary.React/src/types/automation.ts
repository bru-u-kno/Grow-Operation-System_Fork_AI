import type { AutoMeasurementAggregation, AutoMeasurementField, AutoMeasurementRunStatus, AutoMeasurementStatus, AutoMeasurementTriggerKind, HydroSetupLayoutType, HydroSetupStatus, HydroStyle, ReservoirPosition, SelectableHydroStyle } from './shared'
import type { HomeAssistantSettingsDto } from './hardware'
import type { TentStatus, TentType } from './production'

export interface AutoMeasurementConfigDto {
  id: number
  growId: number
  tentId: number | null
  name: string
  status: AutoMeasurementStatus
  triggerKind: AutoMeasurementTriggerKind
  delayMinutes: number | null
  windowMinutes: number
  captureSnapshot: boolean
  createdAtUtc: string
  updatedAtUtc: string
}

export interface CreateAutoMeasurementConfigRequest {
  growId: number
  tentId?: number | null
  name: string
  status: AutoMeasurementStatus
  triggerKind: AutoMeasurementTriggerKind
  delayMinutes?: number | null
  windowMinutes: number
  captureSnapshot?: boolean
}

export interface UpdateAutoMeasurementConfigRequest {
  tentId?: number | null
  name: string
  status: AutoMeasurementStatus
  triggerKind: AutoMeasurementTriggerKind
  delayMinutes?: number | null
  windowMinutes: number
  captureSnapshot?: boolean
}

export interface AutoMeasurementFieldMappingDto {
  id: number
  configId: number
  measurementField: AutoMeasurementField
  metricKey: string
  aggregation: AutoMeasurementAggregation
  isRequired: boolean
  createdAtUtc: string
  updatedAtUtc: string
}

export interface AutoMeasurementFieldMappingUpsertRequest {
  measurementField: AutoMeasurementField
  metricKey: string
  aggregation: AutoMeasurementAggregation
  isRequired: boolean
}

export interface ReplaceAutoMeasurementFieldMappingsRequest {
  mappings: AutoMeasurementFieldMappingUpsertRequest[]
}

export interface AutoMeasurementRunDto {
  id: number
  configId: number
  growId: number
  triggerKind: AutoMeasurementTriggerKind
  scheduledForUtc: string
  measurementId: number | null
  status: AutoMeasurementRunStatus
  errorMessage: string | null
  createdAtUtc: string
  updatedAtUtc: string
}

export interface AutoMeasurementConfigStatusDto {
  configId: number
  growId: number
  name: string
  status: AutoMeasurementStatus
  triggerKind: AutoMeasurementTriggerKind
  delayMinutes: number | null
  windowMinutes: number
  mappingCount: number
  requiredMappingCount: number
  lastRunStatus: AutoMeasurementRunStatus | null
  lastRunScheduledForUtc: string | null
  lastRunMeasurementId: number | null
  lastRunErrorMessage: string | null
  createdRunCount: number
  skippedRunCount: number
  failedRunCount: number
  latestRelevantLightTransitionAtUtc: string | null
  latestRelevantLightTransitionKind: LightTransitionKind | null
}

export interface AutoMeasurementGrowStatusDto {
  growId: number
  configs: AutoMeasurementConfigStatusDto[]
}

export type LightState = 'Unknown' | 'On' | 'Off'
export type LightTransitionKind = 'LightOn' | 'LightOff'
export type LightSource = 'Manual' | 'HomeAssistant'

export interface LightScheduleDto {
  id: number
  tentId: number
  name: string
  isActive: boolean
  lightsOnTime: string
  lightsOffTime: string
  timeZoneId: string | null
  source: LightSource
  createdAtUtc: string
  updatedAtUtc: string
}

export interface CreateLightScheduleRequest {
  tentId: number
  name: string
  isActive: boolean
  lightsOnTime: string
  lightsOffTime: string
  timeZoneId?: string | null
  source: LightSource
}

export interface UpdateLightScheduleRequest {
  name: string
  isActive: boolean
  lightsOnTime: string
  lightsOffTime: string
  timeZoneId?: string | null
  source: LightSource
}

export interface LightTransitionEventDto {
  id: number
  tentId: number
  kind: LightTransitionKind
  occurredAtUtc: string
  source: LightSource
  rawState: string | null
  createdAtUtc: string
}

export type SensorMetricType =
  | 'AirTemperature'
  | 'Humidity'
  | 'Vpd'
  | 'Co2'
  | 'Ppfd'
  | 'LightStatus'
  | 'ReservoirPh'
  | 'ReservoirEc'
  | 'ReservoirOrp'
  | 'ReservoirDissolvedOxygen'
  | 'ReservoirWaterTemp'
  | 'ReservoirLevel'
  | 'ReservoirLevelCm'
  | 'PumpCirculation'
  | 'PumpAir'
  | 'Chiller'
  | 'UpsBattery'
  | 'UpsStatus'
export type LightControllerType = 'AcInfinityPro69' | 'AcInfinityCloudline' | 'GenericRelay' | 'Manual' | 'Other'
export type HvacControllerType = 'AcInfinityPro69' | 'AcInfinityCloudline' | 'GenericRelay' | 'Manual' | 'Other'

export interface TentSensorDto {
  id: number
  tentId: number
  metricType: SensorMetricType
  haEntityId: string
  displayLabel: string | null
  isActive: boolean
}

export interface TentDto {
  id: number
  name: string
  kind: string
  tentType: TentType
  status: TentStatus
  notes: string | null
  displayOrder: number
  accentColor: string
  widthCm: number | null
  depthCm: number | null
  tentHeightCm: number | null
  lightType: string | null
  lightWatt: number | null
  lightController: LightControllerType | null
  lightControllerEntityId: string | null
  exhaustFanCount: number | null
  exhaustM3h: number | null
  circulationFanCount: number | null
  hvacController: HvacControllerType | null
  hvacControllerEntityId: string | null
  co2Available: boolean
  hasCo2Enrichment: boolean
  cameraEntityId: string | null
  cameras: string[]
  leafTempOffsetC: number
  leafOffsetSyncService: string | null
  leafOffsetSyncPort: number
  activeGrowCount: number
  archivedGrowCount: number
  activeSetupCount: number
  archivedSetupCount: number
  sensors: TentSensorDto[]
}

export interface UpdateTentSensorRequest {
  id: number
  metricType: SensorMetricType
  haEntityId: string | null
  displayLabel: string | null
  isActive: boolean
}

export interface UpdateTentRequest {
  name: string
  status: TentStatus
  kind: string
  tentType: TentType
  notes: string | null
  displayOrder: number
  accentColor: string
  widthCm: number | null
  depthCm: number | null
  tentHeightCm: number | null
  lightType: string | null
  lightWatt: number | null
  lightController: LightControllerType | null
  lightControllerEntityId: string | null
  exhaustFanCount: number | null
  exhaustM3h: number | null
  circulationFanCount: number | null
  hvacController: HvacControllerType | null
  hvacControllerEntityId: string | null
  co2Available: boolean
  hasCo2Enrichment: boolean
  cameraEntityId: string | null
  cameras?: string[]
  leafTempOffsetC?: number
  leafOffsetSyncService?: string | null
  leafOffsetSyncPort?: number
  sensors: UpdateTentSensorRequest[]
}

export interface CreateTentRequest {
  name: string
  kind: string
  tentType: TentType
  status?: TentStatus
  notes: string | null
  displayOrder: number
  accentColor: string
  widthCm: number | null
  depthCm: number | null
  tentHeightCm: number | null
  lightType: string | null
  lightWatt: number | null
  lightController: LightControllerType | null
  lightControllerEntityId: string | null
  exhaustFanCount: number | null
  exhaustM3h: number | null
  circulationFanCount: number | null
  hvacController: HvacControllerType | null
  hvacControllerEntityId: string | null
  co2Available: boolean
  hasCo2Enrichment: boolean
  cameraEntityId: string | null
  cameras?: string[]
  leafTempOffsetC?: number
  leafOffsetSyncService?: string | null
  leafOffsetSyncPort?: number
  sensors: UpdateTentSensorRequest[]
}

export interface HydroSetupDto {
  /** Sollwert-Profil dieses Systems; null heisst „nach Anbaustil". */
  setpointProfileId?: string | null
  id: number
  name: string
  tentId: number | null
  tentName: string | null
  hydroStyle: HydroStyle
  potCount: number | null
  potSizeLiters: number | null
  reservoirLiters: number | null
  levelSensorEmptyRaw: number | null
  levelSensorFullRaw: number | null
  levelSensorFullLiters: number | null
  levelCalibratedAtUtc: string | null
  totalVolumeLiters: number | null
  layoutType: HydroSetupLayoutType
  reservoirPosition: ReservoirPosition
  status: HydroSetupStatus
  hasCirculationPump: boolean
  circulationPumpNotes: string | null
  hasAirPump: boolean
  airPumpNotes: string | null
  /** Foerderleistung der Luftpumpe laut Datenblatt, L/h. */
  airPumpLitersPerHour: number | null
  /** Belueftung aus Pumpe und Volumen eingeschaetzt — gerechnet, nicht gemessen. */
  aeration: { stufe: string; satz: string; literLuftJeMinuteJeLiter: number } | null
  airStoneCount: number | null
  hasChiller: boolean
  hasUvSterilizer: boolean
  notes: string | null
  displayOrder: number
  activeGrowCount: number
  createdAtUtc: string
  updatedAtUtc: string
}

export interface CreateHydroSetupRequest {
  /** Sollwert-Profil dieses Systems; null heisst „nach Anbaustil". */
  setpointProfileId?: string | null
  tentId: number | null
  name: string
  hydroStyle: SelectableHydroStyle
  potCount: number | null
  potSizeLiters: number | null
  reservoirLiters: number | null
  layoutType: HydroSetupLayoutType
  reservoirPosition: ReservoirPosition
  hasCirculationPump: boolean
  circulationPumpNotes?: string | null
  hasAirPump: boolean
  airPumpNotes?: string | null
  airPumpLitersPerHour?: number | null
  airStoneCount: number | null
  hasChiller: boolean
  hasUvSterilizer: boolean
  notes?: string | null
  displayOrder: number
}

export interface UpdateHydroSetupRequest extends CreateHydroSetupRequest {
  status: HydroSetupStatus
}

export interface SettingsOverviewDto {
  homeAssistant: HomeAssistantSettingsDto
  tents: TentDto[]
}

export interface MetricPayload {
  key: string
  label: string
  value: string
  unit: string | null
  tone: string
  hint: string | null
  /** Der Wert als Zahl — die Skala der Kachel laesst sich aus "25,4 °C" nicht zurueckrechnen. */
  numericValue: number | null
  /** Zielbereich der aktuellen Phase; null, wo es keinen gibt (Licht, Fuellstand). */
  targetMin: number | null
  targetMax: number | null
  /** Woran das Ziel haengt, wenn es zurueckgerechnet ist — „bei 46 % RLF". */
  targetNote?: string | null
  /** Zurueckgerechnet statt aus dem Wissen: wird gezeigt, zaehlt aber nicht extra im Score. */
  targetDerived?: boolean
  /**
   * Tag- und Nachtband nebeneinander, wo die Messgroesse eins hat (Luft, RLF).
   * targetMin/targetMax bleibt das Band, das GERADE gilt — daran haengen Skala,
   * Status und Score. Diese vier Felder sind allein fuer die Anzeige.
   */
  targetDayMin?: number | null
  targetDayMax?: number | null
  targetNightMin?: number | null
  targetNightMax?: number | null
  /** Welches der beiden Baender gerade gilt: 'day' oder 'night'. */
  targetPhase?: string | null
  /**
   * Fork AI (F-041): Grenzwerte, bei denen gerade gemeldet wird — getrennt vom
   * Ziel. Gelbe Striche auf dem Band, Status „Grenze", wenn überschritten.
   */
  alarmMin?: number | null
  alarmMax?: number | null
  alarmDayMin?: number | null
  alarmDayMax?: number | null
  alarmNightMin?: number | null
  alarmNightMax?: number | null
  /** Kurzer Status in der Ecke, wo es keine Bewertung gibt — „12/12" beim Licht. */
  statusNote?: string | null
  /** Schaltzeiten des Lichts als 'HH:mm'; die Restzeit rechnet die Oberflaeche. */
  lightOnAt?: string | null
  lightOffAt?: string | null
  /** Woher der WERT kommt: 'live' (Sensor) oder 'hand' (erfasste Messung). */
  valueSource?: string | null
  /** Alter der Handmessung in Minuten; null bei Live-Werten. */
  measuredAgeMinutes?: number | null
}

export interface TentLivePayload {
  tentId: number
  stateTone: string
  stateLabel: string
  metrics: MetricPayload[]
  cameraUrl: string | null
  refreshedAtUtc: string
  /** Was der Kühler-Regler gerade tut; null, wenn er für dieses Zelt aus ist. */
  chiller: KuehlerLivePayload | null
}

/** Der Kühler auf der Live-Seite — Lage und Begründung aus derselben Rechnung, die auch schaltet. */
export interface KuehlerLivePayload {
  switchEntityId: string
  sollC: number | null
  istC: number | null
  messwertAlterMinuten: number | null
  tagbetrieb: boolean
  laeuftGerade: boolean | null
  schaltung: 'ein' | 'aus' | 'nichts'
  grund: string
}
