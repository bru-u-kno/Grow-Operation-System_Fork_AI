import { describe, expect, it } from 'vitest'
import {
  finishVerschiebungTage, gleicheWochen, istEigen, wirkungAufGrows, wochenText, wochenZahl,
  type GrowFuerWirkung,
} from './zuechterwochen'

describe('wochenText', () => {
  it('zeigt Spanne, Einzelwert und Leere', () => {
    expect(wochenText({ min: 9, max: 10 })).toBe('9–10')
    expect(wochenText({ min: 9, max: 9 })).toBe('9')
    expect(wochenText({ min: null, max: 10 })).toBe('10')
    expect(wochenText({ min: null, max: null })).toBe('–')
  })
})

describe('finishVerschiebungTage', () => {
  it('rechnet wie der Server: Maximum, sonst Minimum, mal sieben', () => {
    // Mimosa EVO, 16.10.2026: Grow 8–9, Sorte 9–10 → Finish eine Woche früher.
    expect(finishVerschiebungTage({ min: 8, max: 9 }, { min: 9, max: 10 })).toBe(-7)
    expect(finishVerschiebungTage({ min: 9, max: null }, { min: 8, max: 8 })).toBe(7)
    expect(finishVerschiebungTage({ min: null, max: null }, { min: 9, max: 10 })).toBeNull()
  })
})

describe('istEigen', () => {
  it('gehört ohne Sorte immer dem Grow', () => {
    expect(istEigen(null, false, false)).toBe(true)
    expect(istEigen(3, false, false)).toBe(true) // Sorte nicht auffindbar
  })
  it('folgt der Sorte, solange nichts ausdrücklich gesetzt ist', () => {
    expect(istEigen(3, false, true)).toBe(false)
    expect(istEigen(3, undefined, true)).toBe(false)
    expect(istEigen(3, null, true)).toBe(false)
    expect(istEigen(3, true, true)).toBe(true)
  })
})

describe('wochenZahl / gleicheWochen', () => {
  it('liest Zahlen, verwirft Unsinn', () => {
    expect(wochenZahl('9')).toBe(9)
    expect(wochenZahl('')).toBeNull()
    expect(wochenZahl('abc')).toBeNull()
    expect(wochenZahl('0')).toBeNull()
  })
  it('vergleicht null gleich undefined', () => {
    expect(gleicheWochen({ min: 9, max: 10 }, { min: 9, max: 10 })).toBe(true)
    expect(gleicheWochen({ min: 9, max: 10 }, { min: 8, max: 10 })).toBe(false)
  })
})

function grow(teil: Partial<GrowFuerWirkung>): GrowFuerWirkung {
  return {
    id: 1, name: '2026-01', status: 'Running', seedType: 'Feminized', strainId: 1,
    breederWeeksEigen: false, breederFlowerWeeksMin: 9, breederFlowerWeeksMax: 10,
    phasenanker: { finishAb: '2026-10-11T00:00:00', finishBestaetigt: false }, ...teil,
  }
}

describe('wirkungAufGrows', () => {
  const alt = { min: 8, max: 9 }
  const neu = { min: 9, max: 10 }

  it('nennt folgende Grows mit altem und neuem Finish-Beginn', () => {
    const wirkung = wirkungAufGrows([grow({})], 1, alt, neu)
    expect(wirkung.folgt).toHaveLength(1)
    expect(wirkung.folgt[0].tage).toBe(7)
    expect(wirkung.folgt[0].von?.getDate()).toBe(11)
    expect(wirkung.folgt[0].nach?.getDate()).toBe(18)
    expect(wirkung.bleibt).toHaveLength(0)
  })
  it('führt Grows mit eigenem Wert als „bleibt"', () => {
    const wirkung = wirkungAufGrows([grow({ breederWeeksEigen: true, breederFlowerWeeksMin: 8, breederFlowerWeeksMax: 9 })], 1, alt, neu)
    expect(wirkung.folgt).toHaveLength(0)
    expect(wirkung.bleibt).toEqual([{ name: '2026-01', wochen: { min: 8, max: 9 } }])
  })
  it('lässt ein bestätigtes Finish unberührt', () => {
    const wirkung = wirkungAufGrows([grow({ phasenanker: { finishAb: '2026-10-11T00:00:00', finishBestaetigt: true } })], 1, alt, neu)
    expect(wirkung.folgt[0].bestaetigt).toBe(true)
    expect(wirkung.folgt[0].nach).toBeNull()
  })
  it('überspringt fremde Sorten, beendete Grows und Autoflower', () => {
    const wirkung = wirkungAufGrows([
      grow({ strainId: 2 }), grow({ status: 'Completed' }), grow({ seedType: 'Autoflower' }),
    ], 1, alt, neu)
    expect(wirkung.folgt).toHaveLength(0)
    expect(wirkung.bleibt).toHaveLength(0)
  })
  it('behauptet bei sehr kurzen Wochenzahlen keine Verschiebung (Mindestabstand des Servers)', () => {
    const wirkung = wirkungAufGrows([grow({})], 1, { min: 2, max: 3 }, { min: 9, max: 10 })
    expect(wirkung.folgt[0].tage).toBeNull()
    expect(wirkung.folgt[0].nach).toBeNull()
  })
  it('sagt nichts, wenn sich die Wochen nicht ändern', () => {
    expect(wirkungAufGrows([grow({})], 1, neu, neu)).toEqual({ folgt: [], bleibt: [] })
  })
})
