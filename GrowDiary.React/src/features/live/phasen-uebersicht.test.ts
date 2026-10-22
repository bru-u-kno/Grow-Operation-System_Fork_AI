import { describe, expect, it } from 'vitest'
import type { Phase } from '../grows/phase-timeline'
import { phasenKopf, phasenZeile, tageText, wochenText } from './phasen-uebersicht'

const phase = (teil: Partial<Phase>): Phase => ({ name: 'Blüte', label: '', short: '', days: 70, state: 'planned', ...teil })

describe('Phasen-Übersicht: Wortlaut', () => {
  it('schreibt Tage und Wochen aus, Einzahl bei 1', () => {
    expect(tageText(1)).toBe('1 Tag')
    expect(tageText(14)).toBe('14 Tage')
    expect(wochenText(7)).toBe('1 Woche')
    expect(wochenText(14)).toBe('2 Wochen')
    expect(wochenText(58)).toBe('8,3 Wochen')
    expect(wochenText(10)).toBe('1,4 Wochen')
  })

  it('zeigt abgeschlossene und kommende Phasen mit Tagen und Wochen', () => {
    expect(phasenZeile(phase({ name: 'Veg', days: 58, state: 'done' })).text).toBe('58 Tage · 8,3 Wochen')
    expect(phasenZeile(phase({ name: 'Trocknen', days: 10, state: 'planned' })).text).toBe('10 Tage · 1,4 Wochen')
  })

  it('nennt bei der laufenden Phase Tag und Woche von Plan', () => {
    const zeile = phasenZeile(phase({ state: 'current', dayInPhase: 61, progress: 61 / 70 }))
    expect(zeile.text).toBe('Tag 61 von 70 · Woche 9 von 10')
    expect(zeile.ueberPlan).toBe(false)
    expect(phasenKopf([phase({ state: 'current', dayInPhase: 61, progress: 61 / 70 })])).toEqual({ text: 'Blüte · Woche 9 von 10', ueberPlan: false })
  })

  it('meldet eine überzogene Phase als über Plan statt „Tag 75 von 70, Woche 11 von 10“', () => {
    const p = phase({ state: 'current', dayInPhase: 75, progress: 1 })
    expect(phasenZeile(p)).toMatchObject({ text: 'Tag 75 von 70 · 5 Tage über Plan', ueberPlan: true })
    expect(phasenKopf([p])).toEqual({ text: 'Blüte · Woche 11 · über Plan', ueberPlan: true })
  })

  it('kommt ohne geplante Dauer und ohne bekannte Dauer aus', () => {
    expect(phasenZeile(phase({ name: 'Anzucht', state: 'current', dayInPhase: 12, days: 12 })).text).toBe('Tag 12 · Woche 2')
    expect(phasenZeile(phase({ name: 'Veg', state: 'done', days: 0 })).text).toBe('Dauer offen')
    expect(phasenKopf([phase({ state: 'planned' })])).toBeNull()
  })
})
