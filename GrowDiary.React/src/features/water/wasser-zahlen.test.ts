import { describe, expect, it } from 'vitest'
import { zahlZuText } from './wasser-zahlen'
import { zahlOderNull } from '../../zahlenfeld'

/**
 * Der Fehler, den diese Tests festnageln: 1234 µS/cm wurde als „1.234"
 * angezeigt und beim Speichern als 1,234 gelesen — ein Tausendstel.
 * Hartes Wasser gibt es wirklich; der Rundweg muss verlustfrei sein.
 *
 * Gelesen wird mit der einen Leseregel der App (`zahlOderNull`); ihre eigenen
 * Fälle stehen in `src/zahlen-verlust.node.test.ts`.
 */
describe('wasser-zahlen', () => {
  it('überlebt den Rundweg für jeden realistischen Berichtswert', () => {
    for (const wert of [0, 5.6, 7.2, 12, 276, 999, 1000, 1234, 1234.5, 2500]) {
      expect(zahlOderNull(zahlZuText(wert))).toBe(wert)
    }
  })

  it('schreibt große Werte ohne Tausenderpunkt', () => {
    expect(zahlZuText(1234)).toBe('1234')
    expect(zahlZuText(5.6)).toBe('5,6')
    expect(zahlZuText(null)).toBe('')
  })
})
