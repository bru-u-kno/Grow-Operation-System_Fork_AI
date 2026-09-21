import { describe, expect, it } from 'vitest'
import { deutscheWoche, engesBand, planKurz, planWaereText, planwertText, weichtVomPlanAb, zeilenStand } from './tag-nacht'

const u = (rolle: string, wert: string, zustand = 'folgt dem Plan') => ({ rolle, name: rolle, wert, zustand })

describe('zeilenStand', () => {
  it('liest Planwerte und ob die Zeile dem Plan folgt', () => {
    const s = zeilenStand([u('luft-unten', '21'), u('luft-oben', '27')], ['luft-unten', 'luft-oben'])
    expect(s).toEqual({ folgtPlan: true, planVon: 21, planBis: 27 })
    expect(planwertText(s, '°C')).toBe('Planwert 24 °C ± 3 K')
  })

  it('eine von Hand gesetzte Grenze macht die ganze Zeile zum eigenen Wert', () => {
    const s = zeilenStand([u('luft-nacht-unten', '17', 'von dir gesetzt'), u('luft-nacht-oben', '23')], ['luft-nacht-unten', 'luft-nacht-oben'])
    expect(s.folgtPlan).toBe(false)
    expect(planWaereText(s)).toBe('Plan wäre 17–23')
  })

  it('ohne Übergabe weiß die Zeile nichts über den Plan', () => {
    expect(zeilenStand([], ['x']).folgtPlan).toBeNull()
  })

  it('Feuchte hat nur eine Obergrenze', () => {
    const s = zeilenStand([u('feuchte-oben', '55')], ['feuchte-oben'])
    expect(planwertText(s, '%')).toBe('Planwert: höchstens 55 %')
  })
})

describe('engesBand', () => {
  it('warnt bei 20–21 °C (der Fall vom 21.09.)', () => {
    expect(engesBand('Tag', '20', '21', '°C')).toContain('Trotzdem speichern?')
    expect(engesBand('Tag', '20', '21,5', '°C')).toBeNull()
  })
  it('schweigt bei normalen Bändern und leeren Feldern', () => {
    expect(engesBand('Tag', '21', '27', '°C')).toBeNull()
    expect(engesBand('Tag', '', '27', '°C')).toBeNull()
  })
})

describe('deutscheWoche', () => {
  it('übersetzt Blüte und Vegi', () => {
    expect(deutscheWoche('Flores · Woche 5')).toBe('Blütewoche 5')
    expect(deutscheWoche('Vega · Woche 2')).toBe('Vegiwoche 2')
    expect(deutscheWoche('Root')).toBe('Root')
  })
})

describe('planKurz und weichtVomPlanAb', () => {
  const tag = zeilenStand([u('luft-unten', '21'), u('luft-oben', '27')], ['luft-unten', 'luft-oben'])
  const nacht = zeilenStand([u('luft-nacht-unten', '17'), u('luft-nacht-oben', '23')], ['luft-nacht-unten', 'luft-nacht-oben'])

  it('nennt Tag und Nacht wie im Mockup', () => {
    expect(planKurz(tag, nacht, false, '°C')).toBe('tags 24 °C, nachts 20 °C')
    expect(planKurz(tag, nacht, true, '°C')).toBe('tags und nachts 24 °C')
  })

  it('eine gerade geänderte Zahl gilt sofort als eigener Wert', () => {
    expect(weichtVomPlanAb(tag, '21', '27')).toBe(false)
    expect(weichtVomPlanAb(tag, '20', '27')).toBe(true)
  })
})
