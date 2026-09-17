import { describe, expect, it } from 'vitest'
import { anker, waehlePlan, wochenIndex, wochenKurz, type PlanWoche, type WochenPlan } from './wochen-zeile'

const woche = (teil: Partial<PlanWoche>): PlanWoche => ({
  id: 'flower-w4', label: 'Flores · Woche 4', stage: 'Flower', istJetzt: true, wirdGehalten: false,
  ec: '1,4', ph: '5,9', wasser: '20 / 18 °C', vpd: null, rh: 'max 60 %', luft: null, co2: null, ppfd: null, dosierung: null,
  ...teil,
})
const plan = (teil: Partial<WochenPlan>): WochenPlan => ({
  growId: 1, growName: '2026-01', sorte: null, programmName: 'SKX', wochenZieleAktiv: true, jetztLabel: null, wochen: [], ...teil,
})

describe('Wochenzeile', () => {
  it('nimmt den Plan des Grows aus den Reitern, sonst den ersten', () => {
    const a = plan({ growId: 1 })
    const b = plan({ growId: 2 })
    expect(waehlePlan([a, b], 2)).toBe(b)
    expect(waehlePlan([a, b], 9)).toBe(a)
    expect(waehlePlan([a, b], null)).toBe(a)
    expect(waehlePlan([], 1)).toBeNull()
  })

  it('zeigt nur gesetzte Anker, in fester Reihenfolge', () => {
    expect(anker(plan({ flip: '23.08.2026', erntefenster: '18.10.–25.10.', vegiStart: null }))).toEqual([
      { name: 'Flip', wert: '23.08.2026' },
      { name: 'Ernte', wert: '18.10.–25.10.' },
    ])
    expect(anker(plan({}))).toEqual([])
  })

  it('kürzt eine Woche auf EC, Wasser und Feuchte', () => {
    expect(wochenKurz(woche({}))).toBe('EC 1,4 · 20 / 18 °C · max 60 %')
    expect(wochenKurz(woche({ ec: null, rh: null }))).toBe('20 / 18 °C')
  })

  it('findet die Woche aus der Adresszeile', () => {
    const spalten = [{ id: 'veg-w1' }, { id: 'flower-w4' }]
    expect(wochenIndex(spalten, 'flower-w4')).toBe(1)
    expect(wochenIndex(spalten, 'gibt-es-nicht')).toBeNull()
    expect(wochenIndex(spalten, null)).toBeNull()
  })
})
