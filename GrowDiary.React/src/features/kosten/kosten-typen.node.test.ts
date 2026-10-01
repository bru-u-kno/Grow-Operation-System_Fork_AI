import { describe, expect, it } from 'vitest'
import { dauerText, plusMonate } from './kosten-typen'

/**
 * forkai.157: Die Vorschau einer verteilten Anschaffung rechnet im Browser
 * dieselbe Tageszahl wie AnschaffungVerteilung im Backend. Dort gilt
 * `DateTime.AddMonths` — am Monatsende gekappt. `Date.setMonth` läuft dagegen
 * über: aus dem 31.01. plus einem Monat wird der 03.03., drei Tage zu viel, und
 * die Vorschau nannte einen anderen Betrag je Tag als die Tabelle danach.
 */
describe('plusMonate', () => {
  const tag = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`

  it.each([
    ['2026-01-31', 1, '2026-02-28'],
    ['2028-01-31', 1, '2028-02-29'], // Schaltjahr
    ['2028-02-29', 12, '2029-02-28'],
    ['2026-03-31', 1, '2026-04-30'],
    ['2026-01-01', 36, '2029-01-01'],
    ['2026-10-15', 3, '2027-01-15'], // über den Jahreswechsel
  ])('%s + %i Monate = %s (wie .NET AddMonths)', (von, monate, erwartet) => {
    expect(tag(plusMonate(new Date(`${von}T12:00:00`), monate))).toBe(erwartet)
  })

  it('verändert das übergebene Datum nicht', () => {
    const von = new Date('2026-01-31T12:00:00')
    plusMonate(von, 1)
    expect(tag(von)).toBe('2026-01-31')
  })
})

describe('dauerText', () => {
  it.each([[12, '1 Jahr'], [36, '3 Jahre'], [1, '1 Monat'], [18, '18 Monate']])('%i Monate → %s', (monate, text) => {
    expect(dauerText(monate)).toBe(text)
  })
})
