import type { HomeAssistantEntity, SensorMetricType } from '../../types'
import { haWert } from '../../utils'

/**
 * Fork AI (forkai.45): Die Messgrößen-Tabelle und ihre Helfer — aus
 * `pages/HomeAssistantPage.tsx` herausgelöst.
 *
 * Sie werden von zwei Seiten gebraucht (HA-Seite und Messgrößen-Reiter der
 * Geräteseite). Solange sie neben einer Komponente standen, verbot die
 * Fast-Refresh-Regel den Export und der Lint-Lauf der CI war rot.
 */
type GroupKey = 'tent' | 'reservoir' | 'hardware'

export type EntityDefinition = { metricType: SensorMetricType; label: string; group: GroupKey; placeholder: string; importance: 'core' | 'optional'; unit?: string }

export const groups: Array<{ key: GroupKey; label: string }> = [
  { key: 'tent', label: 'Zelt' },
  { key: 'reservoir', label: 'RDWC/DWC' },
  { key: 'hardware', label: 'Technik' },
]

export const definitions: EntityDefinition[] = [
  { metricType: 'AirTemperature', label: 'Lufttemp', group: 'tent', placeholder: 'sensor.zelt_temperatur', unit: '°C', importance: 'core' },
  { metricType: 'Humidity', label: 'Luftfeuchte', group: 'tent', placeholder: 'sensor.zelt_luftfeuchte', unit: '%', importance: 'core' },
  { metricType: 'Vpd', label: 'VPD', group: 'tent', placeholder: 'sensor.zelt_vpd', unit: 'kPa', importance: 'core' },
  { metricType: 'Ppfd', label: 'PPFD', group: 'tent', placeholder: 'sensor.lampe_ppfd', unit: 'µmol/m²/s', importance: 'optional' },
  { metricType: 'Co2', label: 'CO₂', group: 'tent', placeholder: 'sensor.zelt_co2', unit: 'ppm', importance: 'optional' },
  { metricType: 'LightStatus', label: 'Licht', group: 'tent', placeholder: 'switch.licht', importance: 'optional' },
  { metricType: 'ReservoirPh', label: 'pH', group: 'reservoir', placeholder: 'sensor.rdwc_ph', importance: 'core' },
  { metricType: 'ReservoirEc', label: 'EC', group: 'reservoir', placeholder: 'sensor.rdwc_ec', unit: 'mS/cm', importance: 'core' },
  { metricType: 'ReservoirWaterTemp', label: 'Wassertemp', group: 'reservoir', placeholder: 'sensor.rdwc_wassertemperatur', unit: '°C', importance: 'core' },
  { metricType: 'ReservoirLevel', label: 'Wasserstand (L)', group: 'reservoir', placeholder: 'sensor.rdwc_wasserstand_liter', unit: 'L', importance: 'core' },
  { metricType: 'ReservoirLevelCm', label: 'Wasserstand (cm)', group: 'reservoir', placeholder: 'sensor.rdwc_wasserstand_cm', unit: 'cm', importance: 'optional' },
  { metricType: 'ReservoirOrp', label: 'ORP', group: 'reservoir', placeholder: 'sensor.rdwc_orp', unit: 'mV', importance: 'optional' },
  { metricType: 'ReservoirDissolvedOxygen', label: 'DO', group: 'reservoir', placeholder: 'sensor.rdwc_do', unit: 'mg/L', importance: 'optional' },
  { metricType: 'PumpCirculation', label: 'Umwälzpumpe', group: 'hardware', placeholder: 'switch.rdwc_pumpe', importance: 'optional' },
  { metricType: 'PumpAir', label: 'Luftpumpe', group: 'hardware', placeholder: 'switch.luftpumpe', importance: 'optional' },
  { metricType: 'Chiller', label: 'Chiller', group: 'hardware', placeholder: 'climate.chiller', importance: 'optional' },
  { metricType: 'UpsStatus', label: 'USV', group: 'hardware', placeholder: 'sensor.usv_status', importance: 'optional' },
]

// Per-metric hints for the entity picker: which Home Assistant domains / device
// classes are plausible for each sensor, so the dropdown suggests the right ones
// first. Filters are best-effort — if nothing matches, the full list is offered.
const suggestionFilters: Partial<Record<SensorMetricType, { domains?: string[]; deviceClass?: string }>> = {
  AirTemperature: { domains: ['sensor'], deviceClass: 'temperature' },
  Humidity: { domains: ['sensor'], deviceClass: 'humidity' },
  Co2: { domains: ['sensor'], deviceClass: 'carbon_dioxide' },
  ReservoirWaterTemp: { domains: ['sensor'], deviceClass: 'temperature' },
  Vpd: { domains: ['sensor'] },
  Ppfd: { domains: ['sensor'] },
  ReservoirPh: { domains: ['sensor'] },
  ReservoirEc: { domains: ['sensor'] },
  ReservoirLevel: { domains: ['sensor'] },
  ReservoirLevelCm: { domains: ['sensor'] },
  ReservoirOrp: { domains: ['sensor'] },
  ReservoirDissolvedOxygen: { domains: ['sensor'] },
  UpsStatus: { domains: ['sensor', 'binary_sensor'] },
  LightStatus: { domains: ['switch', 'light', 'binary_sensor', 'input_boolean'] },
  PumpCirculation: { domains: ['switch', 'input_boolean'] },
  PumpAir: { domains: ['switch', 'input_boolean'] },
  Chiller: { domains: ['climate', 'switch'] },
}

export function suggestionsForMetric(entities: HomeAssistantEntity[], metricType: SensorMetricType): HomeAssistantEntity[] {
  const filter = suggestionFilters[metricType]
  if (!filter) return entities
  if (filter.deviceClass) {
    const byClass = entities.filter((entity) => entity.deviceClass === filter.deviceClass)
    if (byClass.length > 0) return byClass
  }
  if (filter.domains) {
    const byDomain = entities.filter((entity) => filter.domains!.includes(entity.domain))
    if (byDomain.length > 0) return byDomain
  }
  return entities
}

export function entityOptionLabel(entity: HomeAssistantEntity): string {
  const name = entity.friendlyName ?? entity.entityId
  if (entity.state == null || entity.state === '') return name
  return `${name} — ${haWert(entity.state, entity.unitOfMeasurement)}`
}
