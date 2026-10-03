import { ApiRequestError } from '../../api'
import type {
  AutoMeasurementField,
  AutoMeasurementStatus,
  AutoMeasurementTriggerKind,
  GrowDetail,
  GrowDeviationDto,
  PhotoTag,
  ValueOrigin,
} from '../../types'
import { formatNumber, toLocalInputValue } from '../../utils'

export type GrowDetailSection = 'overview' | 'measurements' | 'diagnosis' | 'sops' | 'journal' | 'automation'

export const autoMeasurementFields: AutoMeasurementField[] = [
  'AirTemperatureC',
  'HumidityPercent',
  'ReservoirPh',
  'ReservoirEc',
  'ReservoirWaterTempC',
  'ReservoirLevelLiters',
  'ReservoirLevelCm',
  'DissolvedOxygenMgL',
  'OrpMv',
  'PpfdMol',
  'Co2Ppm',
]

export const defaultMetricKeyByField: Record<AutoMeasurementField, string> = {
  AirTemperatureC: 'temperature',
  HumidityPercent: 'humidity',
  ReservoirPh: 'reservoir-ph',
  ReservoirEc: 'reservoir-ec',
  ReservoirWaterTempC: 'reservoir-temp',
  ReservoirLevelLiters: 'reservoir-level',
  ReservoirLevelCm: 'reservoir-level-cm',
  DissolvedOxygenMgL: 'dissolved-oxygen',
  OrpMv: 'orp',
  PpfdMol: 'ppfd',
  Co2Ppm: 'co2',
}


export const emptyTaskForm = () => ({
  title: '',
  dueAtLocal: '',
  priority: 'Normal',
  notes: '',
})

export type TaskFormState = ReturnType<typeof emptyTaskForm>

export const emptyJournalForm = () => ({
  title: '',
  body: '',
  entryType: 'Observation',
  source: 'Manual',
  occurredAtLocal: toLocalInputValue(),
})

export type JournalFormState = ReturnType<typeof emptyJournalForm>

export const emptyPhotoForm = () => ({
  photoCaption: '',
  photoTag: 'Overview' as PhotoTag,
  useAsReferenceShot: false,
  source: 'Manual' as ValueOrigin,
  files: [] as File[],
})

export type PhotoFormState = ReturnType<typeof emptyPhotoForm>

export const emptyAutoConfigForm = () => ({
  name: '',
  status: 'Enabled' as AutoMeasurementStatus,
  triggerKind: 'Manual' as AutoMeasurementTriggerKind,
  delayMinutes: '',
  windowMinutes: '20',
  captureSnapshot: false,
})

export type AutoConfigFormState = ReturnType<typeof emptyAutoConfigForm>


export function formatDeviationValue(value: number | null, unit: string | null): string {
  if (value == null) return '-'
  return `${formatNumber(value, 2)}${unit ? ` ${unit}` : ''}`
}

export function formatDeviationTarget(deviation: GrowDeviationDto): string | null {
  if (deviation.targetMin == null && deviation.targetMax == null) return null
  if (deviation.targetMin != null && deviation.targetMax != null) {
    return `${formatNumber(deviation.targetMin, 2)}-${formatNumber(deviation.targetMax, 2)}${deviation.unit ? ` ${deviation.unit}` : ''}`
  }
  if (deviation.targetMin != null) {
    return `>= ${formatNumber(deviation.targetMin, 2)}${deviation.unit ? ` ${deviation.unit}` : ''}`
  }
  return `<= ${formatNumber(deviation.targetMax, 2)}${deviation.unit ? ` ${deviation.unit}` : ''}`
}

export function formatGrowStatus(status: GrowDetail['status']) {
  return status === 'Running' ? 'aktiv'
    : status === 'Planning' ? 'geplant'
      : status === 'Completed' ? 'beendet'
        : status === 'Aborted' ? 'abgebrochen'
          : status
}

export function formatGrowHydroMedium(grow: GrowDetail) {
  if (grow.hydroSetupName) return grow.hydroSetupName
  if (grow.hydroStyle !== 'None') return grow.hydroStyle
  return grow.mediumDetail ?? grow.mediumType ?? 'Medium offen'
}

export function isNotFound(caught: unknown) {
  return caught instanceof ApiRequestError && caught.status === 404
}
