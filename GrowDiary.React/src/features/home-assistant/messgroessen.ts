import type { HomeAssistantEntity, SensorMetricType } from '../../types'
import { haWert } from '../../utils'
import { entitaetsOptionen } from '../geraete/entitaets-optionen'
import type { Kriterium, Vorbereitet } from '../geraete/entitaets-passung'

/**
 * Fork AI (forkai.45): Die Messgrößen-Tabelle und ihre Helfer — aus
 * `pages/HomeAssistantPage.tsx` herausgelöst.
 *
 * Sie werden von zwei Seiten gebraucht (HA-Seite und Messgrößen-Reiter der
 * Geräteseite). Solange sie neben einer Komponente standen, verbot die
 * Fast-Refresh-Regel den Export und der Lint-Lauf der CI war rot.
 */
type GroupKey = 'tent' | 'reservoir' | 'hardware'

export type EntityDefinition = {
  metricType: SensorMetricType
  label: string
  group: GroupKey
  placeholder: string
  importance: 'core' | 'optional'
  unit?: string
  /** Ein Satz unter der Zeile, wo das Etikett allein missverständlich wäre. */
  hinweis?: string
}

/**
 * Der Satz zu den Außenwerten — einmal, weil er an drei Zeilen steht.
 *
 * „Außen" heißt hier die Luft um das Zelt (wie „Aussen" in der App des
 * AC-Infinity-Controllers), nicht die Luft von draußen: die liest die
 * Zuluft-Steuerung über eigene Entitäten.
 */
const AUSSEN_HINWEIS = 'Luft außerhalb des Zelts, z. B. der Sensor am Controller — nicht die Luft von draußen.'

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
  { metricType: 'OutsideTemperature', label: 'Außen Temp.', group: 'tent', placeholder: 'sensor.controller_temperatur', unit: '°C', importance: 'optional', hinweis: AUSSEN_HINWEIS },
  { metricType: 'OutsideHumidity', label: 'Außen RLF', group: 'tent', placeholder: 'sensor.controller_luftfeuchtigkeit', unit: '%', importance: 'optional', hinweis: AUSSEN_HINWEIS },
  { metricType: 'OutsideVpd', label: 'Außen VPD', group: 'tent', placeholder: 'sensor.controller_vpd', unit: 'kPa', importance: 'optional', hinweis: AUSSEN_HINWEIS },
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

// forkai.236 (A-019): Welche Entitäten zu einer Messgröße passen, sortiert die gemeinsame Rangfolge der Rollen-Auswahl
// (features/geraete/entitaets-passung.ts) — dieselbe Regel, nicht ein zweiter Filter. Hier stehen nur die Domains je
// Messgröße und die Begriffe, die ihr Name nennt; Einheit und Geräteklasse kommen aus `definition.unit`.
// Vorher blendete ein Filter alles andere aus: ein Fremder ohne Geräteklassen sah bei "Luftfeuchte" jeden Sensor.
const suggestionFilters: Partial<Record<SensorMetricType, { domains: string[]; begriffe: string[] }>> = {
  AirTemperature: { domains: ['sensor'], begriffe: ['temperatur', 'zelt'] },
  Humidity: { domains: ['sensor'], begriffe: ['feuchte', 'zelt'] },
  Co2: { domains: ['sensor'], begriffe: ['co2'] },
  ReservoirWaterTemp: { domains: ['sensor'], begriffe: ['wasser', 'temperatur'] },
  Vpd: { domains: ['sensor'], begriffe: ['vpd', 'zelt'] },
  Ppfd: { domains: ['sensor'], begriffe: ['ppfd'] },
  ReservoirPh: { domains: ['sensor'], begriffe: ['ph'] },
  ReservoirEc: { domains: ['sensor'], begriffe: ['ec'] },
  ReservoirLevel: { domains: ['sensor'], begriffe: ['wasser', 'stand'] },
  ReservoirLevelCm: { domains: ['sensor'], begriffe: ['wasser', 'stand'] },
  ReservoirOrp: { domains: ['sensor'], begriffe: ['orp'] },
  ReservoirDissolvedOxygen: { domains: ['sensor'], begriffe: ['sauerstoff'] },
  UpsStatus: { domains: ['sensor', 'binary_sensor'], begriffe: ['usv'] },
  LightStatus: { domains: ['switch', 'light', 'binary_sensor', 'input_boolean'], begriffe: ['licht'] },
  PumpCirculation: { domains: ['switch', 'input_boolean'], begriffe: ['pumpe'] },
  PumpAir: { domains: ['switch', 'input_boolean'], begriffe: ['pumpe'] },
  Chiller: { domains: ['climate', 'switch'], begriffe: ['kuehler'] },
  OutsideTemperature: { domains: ['sensor'], begriffe: ['temperatur', 'aussen'] },
  OutsideHumidity: { domains: ['sensor'], begriffe: ['feuchte', 'aussen'] },
  OutsideVpd: { domains: ['sensor'], begriffe: ['vpd', 'aussen'] },
}

/** Das Kriterium der Rangfolge für eine Messgröße; ohne Eintrag: keine Domain passt, alles steht unter "Alle anderen". */
export function kriteriumFuerMessgroesse(definition: EntityDefinition): Kriterium {
  const filter = suggestionFilters[definition.metricType]
  return {
    domains: filter?.domains ?? [],
    einheit: definition.unit ?? null,
    label: definition.label,
    begriffe: filter?.begriffe ?? [],
  }
}

/** Auswahl-Einträge einer Messgröße: gemeinsame Vorsortierung, Anzeige wie bisher ("Name — Wert"). */
export function messgroessenOptionen(vorbereitet: readonly Vorbereitet[], definition: EntityDefinition) {
  return entitaetsOptionen(vorbereitet, kriteriumFuerMessgroesse(definition), {
    passtGruppe: 'Passt zu dieser Messgröße',
    hinweis: entityOptionLabel,
  })
}

export function entityOptionLabel(entity: HomeAssistantEntity): string {
  const name = entity.friendlyName ?? entity.entityId
  if (entity.state == null || entity.state === '') return name
  return `${name} — ${haWert(entity.state, entity.unitOfMeasurement)}`
}

/**
 * cc.263 (Raum, Stufe 2): Steht ein Zelt im Raum und hat der Raum beide Fühler, kommen „{Raumname} Temp./RLF/VPD“ vom Raum —
 * die drei eigenen Zeilen des Zelts entfallen dann (gespeichert bleiben sie, falls das Zelt den Raum verlässt).
 */
export const RAUMWERTE_METRIKEN: SensorMetricType[] = ['OutsideTemperature', 'OutsideHumidity', 'OutsideVpd']

/**
 * Dampfdruckdefizit in kPa aus Lufttemperatur (°C) und Feuchte (%), Blatt = Luft. Dieselbe Formel wie `RaumLuft.Vpd` im Backend
 * (`RaumLuftTests` und `messgroessen.test.ts` halten die gleichen Zahlen fest); hier nur für die Anzeige der Raumwerte-Zeile.
 */
export function vpdKpa(tempC: number, rhProzent: number): number | null {
  if (!Number.isFinite(tempC) || !Number.isFinite(rhProzent) || rhProzent < 0 || rhProzent > 100 || tempC < -40 || tempC > 80) return null
  const saettigung = 0.6108 * Math.exp((17.27 * tempC) / (tempC + 237.3))
  return Math.round(saettigung * (1 - rhProzent / 100) * 100) / 100
}
