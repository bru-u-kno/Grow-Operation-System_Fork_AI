import type { CalibrationEventStatus, CalibrationEventType, CalibrationResult, HardwareItemCriticality, HardwareItemStatus, MaintenanceEventStatus, MaintenanceEventType, MaintenanceResult, RiskEventSeverity, RiskEventSource, RiskEventStatus, RiskEventType } from './shared'

export interface HomeAssistantSettingsDto {
  baseUrl: string | null
  accessToken: string | null
  enabled: boolean
  // True when Grow OS runs as a Home Assistant add-on: the connection is managed
  // automatically, so the manual URL/token fields are hidden.
  isManagedByAddon?: boolean
}

export interface HomeAssistantEntity {
  entityId: string
  friendlyName: string | null
  state: string | null
  unitOfMeasurement: string | null
  deviceClass: string | null
  domain: string
}

// Device class: fixed HA-mapped sensor, handheld meter (calibrated, never mapped),
// or equipment like pumps/chillers (maintenance only).
export type HardwareDeviceKind = 'FixedSensor' | 'HandheldMeter' | 'Equipment'

export interface HardwareItemDto {
  id: number
  name: string
  category: string
  status: HardwareItemStatus
  criticality: HardwareItemCriticality
  tentId: number | null
  setupId: number | null
  hydroSetupId: number | null
  growId: number | null
  wearTemplateId: string | null
  tentSensorId: number | null
  haEntityId: string | null
  metricType?: string | null
  deviceKind?: HardwareDeviceKind | null
  manufacturer: string | null
  model: string | null
  serialNumber: string | null
  installedAtUtc: string | null
  retiredAtUtc: string | null
  expectedLifespanDays: number | null
  inspectionIntervalDays: number | null
  calibrationIntervalDays: number | null
  notes: string | null
  createdAtUtc: string
  updatedAtUtc: string
}

export interface CreateHardwareItemRequest {
  name?: string | null
  category?: string | null
  status: HardwareItemStatus
  criticality: HardwareItemCriticality
  tentId?: number | null
  setupId?: number | null
  hydroSetupId?: number | null
  growId?: number | null
  wearTemplateId?: string | null
  tentSensorId?: number | null
  haEntityId?: string | null
  deviceKind?: HardwareDeviceKind | null
  manufacturer?: string | null
  model?: string | null
  serialNumber?: string | null
  installedAtUtc?: string | null
  retiredAtUtc?: string | null
  expectedLifespanDays?: number | null
  inspectionIntervalDays?: number | null
  calibrationIntervalDays?: number | null
  notes?: string | null
}

export interface UpdateHardwareItemRequest extends CreateHardwareItemRequest {
  name: string
  category: string
}

export interface MaintenanceEventDto {
  id: number
  hardwareItemId: number
  eventType: MaintenanceEventType
  status: MaintenanceEventStatus
  result: MaintenanceResult
  title: string
  description: string | null
  dueAtUtc: string | null
  performedAtUtc: string | null
  nextDueAtUtc: string | null
  growTaskId: number | null
  sopInstanceId: number | null
  notes: string | null
  createdAtUtc: string
  updatedAtUtc: string
}

export interface CreateMaintenanceEventRequest {
  hardwareItemId: number
  eventType: MaintenanceEventType
  status: MaintenanceEventStatus
  result: MaintenanceResult
  title: string
  description?: string | null
  dueAtUtc?: string | null
  performedAtUtc?: string | null
  nextDueAtUtc?: string | null
  growTaskId?: number | null
  sopInstanceId?: number | null
  notes?: string | null
}

export type UpdateMaintenanceEventRequest = CreateMaintenanceEventRequest

export interface CalibrationEventDto {
  id: number
  hardwareItemId: number
  calibrationType: CalibrationEventType
  status: CalibrationEventStatus
  result: CalibrationResult
  title: string
  referenceSolution: string | null
  referenceValue: number | null
  beforeValue: number | null
  afterValue: number | null
  temperatureC: number | null
  dueAtUtc: string | null
  performedAtUtc: string | null
  nextDueAtUtc: string | null
  growTaskId: number | null
  notes: string | null
  createdAtUtc: string
  updatedAtUtc: string
}

export interface CreateCalibrationEventRequest {
  hardwareItemId: number
  calibrationType: CalibrationEventType
  status: CalibrationEventStatus
  result: CalibrationResult
  title: string
  referenceSolution?: string | null
  referenceValue?: number | null
  beforeValue?: number | null
  afterValue?: number | null
  temperatureC?: number | null
  dueAtUtc?: string | null
  performedAtUtc?: string | null
  nextDueAtUtc?: string | null
  growTaskId?: number | null
  notes?: string | null
}

export type UpdateCalibrationEventRequest = CreateCalibrationEventRequest

export interface RiskEventDto {
  id: number
  eventType: RiskEventType
  severity: RiskEventSeverity
  status: RiskEventStatus
  source: RiskEventSource
  title: string
  description: string | null
  hardwareItemId: number | null
  tentId: number | null
  growId: number | null
  tentSensorId: number | null
  haEntityId: string | null
  sopInstanceId: number | null
  growTaskId: number | null
  startedAtUtc: string
  lastSeenAtUtc: string | null
  resolvedAtUtc: string | null
  acknowledgedAtUtc: string | null
  dedupeKey: string | null
  rawValue: string | null
  notes: string | null
  createdAtUtc: string
  updatedAtUtc: string
  /** Vom Abgleich geschlossen, weil die Abweichung nicht mehr besteht (nicht von Hand erledigt). */
  resolvedAutomatically?: boolean
  /** Zeit der neuesten Messung des Grows — nur an offenen Abweichungs-Karten. */
  latestMeasurementAtUtc?: string | null
  /** Üblicher Abstand zwischen automatischen Messungen in Minuten; fehlt, wenn es zu wenige gibt. */
  measurementIntervalMinutes?: number | null
  /** Die neueste Messung ist älter als das 1,5-Fache des üblichen Abstands. */
  measurementStale?: boolean
  /** Wann die nächste automatische Messung zu erwarten ist; fehlt, wenn unbekannt oder schon fällig. */
  nextMeasurementExpectedAtUtc?: string | null
}

export interface CreateRiskEventRequest {
  eventType: RiskEventType
  severity: RiskEventSeverity
  status: RiskEventStatus
  source: RiskEventSource
  title: string
  description?: string | null
  hardwareItemId?: number | null
  tentId?: number | null
  growId?: number | null
  tentSensorId?: number | null
  haEntityId?: string | null
  sopInstanceId?: number | null
  growTaskId?: number | null
  startedAtUtc?: string | null
  lastSeenAtUtc?: string | null
  resolvedAtUtc?: string | null
  acknowledgedAtUtc?: string | null
  dedupeKey?: string | null
  rawValue?: string | null
  notes?: string | null
}

export type UpdateRiskEventRequest = CreateRiskEventRequest

export interface ResolveRiskEventRequest {
  resolvedAtUtc?: string | null
  notes?: string | null
}

export interface AcknowledgeRiskEventRequest {
  acknowledgedAtUtc?: string | null
  notes?: string | null
}

export interface RiskEventSopRecommendationDto {
  riskEventId: number
  riskEventType: string
  severity: string
  sopId: string
  sopName: string
  reason: string
  confidence: string
  alreadyActive: boolean
  activeSopInstanceId: number | null
}

export interface StartRiskEventSopRequest {
  sopId: string
  notes?: string | null
}
