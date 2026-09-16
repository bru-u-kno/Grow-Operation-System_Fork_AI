import { alsText } from '../wochenplan/wochenwerte-bearbeiten'
import type { BuchEintrag, Zutat } from './plan-reiter'

/**
 * Fork AI (Grow-Plan, Schritt 6b): Rechenseite der Auswertung auf der
 * Grow-Seite — was geplant war, was am Ende galt, was gemessen wurde.
 */

export type Werte = Record<string, number | null>

export type AuswertungWoche = {
  id: string
  label: string
  stage: string
  week: number | null
  von: string | null
  bis: string | null
  start: Werte
  ende: Werte
  gemessen: Werte
  messungen: number
  dosierungStart: Zutat[]
  dosierungEnde: Zutat[]
}

export type Auswertung = {
  growId: number
  growName: string
  programmName: string
  startProgrammName: string | null
  eingefroren: boolean
  eingefrorenUtc: string | null
  startVermerk: string | null
  wochen: AuswertungWoche[]
  buch: BuchEintrag[]
}

type Groesse = {
  key: string
  label: string
  /** Der Planwert als Text. */
  plan: (w: Werte) => string
  /** Das Band, gegen das gemessen wird (inkl. Toleranz) — oder null. */
  band: (w: Werte) => [number | null, number | null] | null
  gemessen: string
}

const spanne = (a: number | null, b: number | null) =>
  a == null && b == null ? '–' : a != null && b != null && Math.abs(a - b) > 1e-9 ? `${alsText(a)}–${alsText(b)}` : alsText(a ?? b)

export const GROESSEN: readonly Groesse[] = [
  {
    key: 'ec', label: 'EC', gemessen: 'ec',
    plan: (w) => alsText(w.ecTarget) || '–',
    band: (w) => w.ecMin != null && w.ecMax != null ? [w.ecMin - 0.2, w.ecMax + 0.2]
      : w.ecTarget != null ? [w.ecTarget - 0.2, w.ecTarget + 0.2] : null,
  },
  {
    key: 'ph', label: 'pH', gemessen: 'ph',
    plan: (w) => spanne(w.phMin, w.phMax),
    band: (w) => w.phMin != null && w.phMax != null ? [w.phMin - 0.2, w.phMax + 0.2] : null,
  },
  {
    key: 'wasser', label: 'Wasser', gemessen: 'wasser',
    plan: (w) => w.waterTempDayC == null ? '–' : w.waterTempNightC != null && w.waterTempNightC !== w.waterTempDayC
      ? `${alsText(w.waterTempDayC)}/${alsText(w.waterTempNightC)}` : alsText(w.waterTempDayC),
    band: (w) => w.waterTempDayC == null ? null
      : [Math.min(w.waterTempDayC, w.waterTempNightC ?? w.waterTempDayC) - 2, Math.max(w.waterTempDayC, w.waterTempNightC ?? w.waterTempDayC) + 2],
  },
  {
    key: 'rh', label: 'Feuchte', gemessen: 'rh',
    plan: (w) => w.rhMax == null ? '–' : `≤ ${alsText(w.rhMax)}`,
    band: (w) => w.rhMax == null ? null : [null, w.rhMax],
  },
  {
    key: 'luft', label: 'Luft', gemessen: 'luft',
    plan: (w) => alsText(w.airTempC) || '–',
    band: (w) => w.airTempC == null ? null : [w.airTempC - 3, w.airTempC + 3],
  },
  {
    key: 'orp', label: 'ORP', gemessen: 'orp',
    plan: (w) => spanne(w.orpMin, w.orpMax),
    band: (w) => w.orpMin != null && w.orpMax != null ? [w.orpMin, w.orpMax] : null,
  },
]

export type Zeile = {
  id: string
  label: string
  start: string
  ende: string
  geaendert: boolean
  gemessen: string
  abweichung: boolean
}

/** Die Tabellenzeilen für eine Messgröße. */
export function zeilen(auswertung: Auswertung, key: string): Zeile[] {
  const g = GROESSEN.find((x) => x.key === key)
  if (!g) return []
  return auswertung.wochen.map((w) => {
    const start = g.plan(w.start)
    const ende = g.plan(w.ende)
    const wert = w.gemessen[g.gemessen] ?? null
    const band = g.band(w.ende)
    const abweichung = wert != null && band != null
      && ((band[0] != null && wert < band[0] - 1e-9) || (band[1] != null && wert > band[1] + 1e-9))
    return {
      id: w.id,
      label: w.label,
      start,
      ende,
      geaendert: start !== ende,
      gemessen: wert == null ? '–' : alsText(wert),
      abweichung,
    }
  })
}

/** Die Dosierung einer Woche als kurzer Text. */
export function dosierungText(items: readonly Zutat[]): string {
  return items.length === 0 ? '–' : items.map((i) => `${i.component} ${alsText(i.minMlPerLiter)}`).join(' · ')
}

/** Dosierungszeilen: Start und Ende je Woche. */
export function dosierungZeilen(auswertung: Auswertung): Zeile[] {
  return auswertung.wochen.map((w) => {
    const start = dosierungText(w.dosierungStart)
    const ende = dosierungText(w.dosierungEnde)
    return { id: w.id, label: w.label, start, ende, geaendert: start !== ende, gemessen: '', abweichung: false }
  })
}

/** Klartext der Planfelder für Zeitleiste und Buch. */
export const FELD_NAMEN: Record<string, string> = {
  ecTarget: 'EC', ecMin: 'EC von', ecMax: 'EC bis', phMin: 'pH von', phMax: 'pH bis',
  orpMin: 'ORP von', orpMax: 'ORP bis', waterTempDayC: 'Wasser Tag', waterTempNightC: 'Wasser Nacht',
  rhMax: 'RH max', airTempC: 'Luft', vpdMin: 'VPD von', vpdMax: 'VPD bis',
  co2Min: 'CO₂ von', co2Max: 'CO₂ bis', ppfdMin: 'PPFD von', ppfdMax: 'PPFD bis',
}

export const feldName = (feld: string) => FELD_NAMEN[feld] ?? feld

/** Einträge für die Zeitleiste — ohne das bloße Anlegen. */
export function zeitleiste(auswertung: Auswertung): BuchEintrag[] {
  return auswertung.buch.filter((e) => e.art !== 'angelegt')
}
