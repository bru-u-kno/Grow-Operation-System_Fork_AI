import { describe, expect, it } from 'vitest'
import { STUFEN, stufeErkennen, stufeFuerHaupt, stufeFuerZusatz } from './entfeuchtung-stufen'
import type { StufenName } from './entfeuchtung-stufen'

/** Brus Werte vom 12.10.2026 — die Stufe „Normal" muss genau sie sein, sonst ändert die Umstellung etwas. */
const BRU = { hystereseProzent: 4, einschaltverzoegerungMin: 2, mindestlaufzeitMin: 20 }
const BRU_ZUSATZ = { mindestlaufzeitMin: 15 }

describe('Stufe „Wie reagieren die Entfeuchter?"', () => {
  it('Brus heutige Werte sind „Normal" — nach der Umstellung ändert sich nichts', () => {
    expect(stufeErkennen(BRU, BRU_ZUSATZ)).toBe('normal')
  })

  it('jede Stufe erkennt sich selbst, wenn man sie anwendet (Rundlauf über alle drei)', () => {
    for (const name of Object.keys(STUFEN) as StufenName[]) {
      const haupt = { ...stufeFuerHaupt(name, { wartezeitAussenluftMin: 5 }) }
      const zusatz = stufeFuerZusatz(name)
      expect(stufeErkennen(haupt, zusatz)).toBe(name)
    }
  })

  it('ein einziger abweichender Wert macht daraus „eigene" — auch der des Zusatzes', () => {
    expect(stufeErkennen({ ...BRU, hystereseProzent: 5 }, BRU_ZUSATZ)).toBe('eigene')
    expect(stufeErkennen({ ...BRU, einschaltverzoegerungMin: 3 }, BRU_ZUSATZ)).toBe('eigene')
    expect(stufeErkennen({ ...BRU, mindestlaufzeitMin: 21 }, BRU_ZUSATZ)).toBe('eigene')
    expect(stufeErkennen(BRU, { mindestlaufzeitMin: 16 })).toBe('eigene')
  })

  it('ohne Zusatz-Entfeuchter zählt dessen Mindestlaufzeit nicht mit', () => {
    expect(stufeErkennen(BRU, null)).toBe('normal')
  })

  it('die drei Stufen unterscheiden sich in jedem Wert, den sie setzen (sonst wäre eine überflüssig)', () => {
    const [ruhig, normal, schnell] = [STUFEN.ruhig, STUFEN.normal, STUFEN.schnell]
    for (const feld of Object.keys(normal) as Array<keyof typeof normal>) {
      expect(new Set([ruhig[feld], normal[feld], schnell[feld]]).size).toBe(3)
    }
  })

  it('der Abstand folgt den früheren Stufen knapp 2 · normal 4 · ruhig 6', () => {
    expect([STUFEN.schnell.abstandProzent, STUFEN.normal.abstandProzent, STUFEN.ruhig.abstandProzent]).toEqual([2, 4, 6])
  })

  it('Ruhig verlängert die Wartezeit auf Außenluft mit — das Backend lässt sie nie kürzer sein als „Einschalten nach"', () => {
    expect(stufeFuerHaupt('ruhig', { wartezeitAussenluftMin: 5 }).wartezeitAussenluftMin).toBe(10)
    // Eine längere Wartezeit bleibt, wie sie ist.
    expect(stufeFuerHaupt('ruhig', { wartezeitAussenluftMin: 25 }).wartezeitAussenluftMin).toBe(25)
    expect(stufeFuerHaupt('schnell', { wartezeitAussenluftMin: 5 }).wartezeitAussenluftMin).toBe(5)
  })

  it('die Stufe rührt die Felder der Zusatz-Hilfsstärke nicht an (Zuschalten nach, Mindestpause)', () => {
    expect(Object.keys(stufeFuerZusatz('ruhig'))).toEqual(['mindestlaufzeitMin'])
  })
})
