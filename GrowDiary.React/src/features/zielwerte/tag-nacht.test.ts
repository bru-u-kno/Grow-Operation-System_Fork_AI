import { describe, expect, it } from 'vitest'
import {
  deutscheWoche, engesBand, planKurz, planWaereText, planwertText, standMitAbweichung, weichtVomPlanAb,
  zeilenBeiAbweichung, zeilenStand,
} from './tag-nacht'

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

describe('zeilenBeiAbweichung (forkai.154: Abweichung rechnet live mit)', () => {
  // Bru, 30.09.2026, Blütewoche 6: Plan 23 °C tags, 19 °C nachts, übergeben mit ± 3 K.
  const tag = zeilenStand([u('luft-unten', '20'), u('luft-oben', '26')], ['luft-unten', 'luft-oben'])
  const nacht = zeilenStand([u('luft-nacht-unten', '16'), u('luft-nacht-oben', '22')], ['luft-nacht-unten', 'luft-nacht-oben'])
  const start = { min: '20', max: '26', nachtMin: '16', nachtMax: '22', toleranz: '' }

  it('± 4 K: Tag und Nacht folgen dem Plan und wandern sofort mit', () => {
    expect(zeilenBeiAbweichung(start, tag, nacht, '4')).toEqual({
      toleranz: '4', min: '19', max: '27', nachtMin: '15', nachtMax: '23',
    })
  })

  it('ein zweites Mal hintereinander: von 4 auf 4,5 — gemessen an der 4, nicht an den alten 3', () => {
    const nachVier = { ...start, ...zeilenBeiAbweichung(start, tag, nacht, '4') }
    expect(zeilenBeiAbweichung(nachVier, tag, nacht, '4,5')).toEqual({
      toleranz: '4,5', min: '18,5', max: '27,5', nachtMin: '14,5', nachtMax: '23,5',
    })
  })

  it('eine Zeile mit eigenen Zahlen bleibt stehen, die andere wandert', () => {
    const eigenerTag = { ...start, min: '21' }
    expect(zeilenBeiAbweichung(eigenerTag, tag, nacht, '4')).toEqual({ toleranz: '4', nachtMin: '15', nachtMax: '23' })
  })

  it('ungültige oder halbe Eingaben ändern nur das Feld selbst', () => {
    expect(zeilenBeiAbweichung(start, tag, nacht, '')).toEqual({ toleranz: '' })
    expect(zeilenBeiAbweichung(start, tag, nacht, '0')).toEqual({ toleranz: '0' })
    expect(zeilenBeiAbweichung(start, tag, nacht, '16')).toEqual({ toleranz: '16' })
  })

  it('der Planwert-Hinweis nennt die neue Abweichung, und die Zeile gilt weiter als „folgt dem Plan“', () => {
    const mitVier = standMitAbweichung(tag, 4)
    expect(planwertText(mitVier!, '°C')).toBe('Planwert 23 °C ± 4 K')
    expect(weichtVomPlanAb(mitVier, '19', '27')).toBe(false)
  })
})
