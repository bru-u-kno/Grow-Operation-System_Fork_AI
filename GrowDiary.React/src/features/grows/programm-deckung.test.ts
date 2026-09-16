import { describe, expect, it } from 'vitest'
import { deckung, istEigenesProgramm, wechselNoetig } from './programm-deckung'

const spalte = (klima: boolean) => ({ id: 'w', label: 'W', stage: 'Flower', week: 1, items: [], ecTarget: 1, phMin: 6, phMax: 6, klimaJeWoche: klima })

describe('deckung', () => {
  it('ohne Chart: keine Wochenwerte', () => {
    expect(deckung({ feedChart: null }).stufe).toBe('keine')
  })
  it('nur EC/pH: teilweise', () => {
    const d = deckung({ feedChart: { unit: 'ml', note: null, columns: [spalte(false), spalte(false)] } })
    expect(d).toEqual({ stufe: 'teil', text: '2 Wochen · EC und pH je Woche · Klima aus dem Standard' })
  })
  it('mit Klima: voll', () => {
    expect(deckung({ feedChart: { unit: 'ml', note: null, columns: [spalte(true)] } }).stufe).toBe('voll')
  })
})

describe('wechselNoetig', () => {
  it('nur bei echtem Wechsel mit Plan', () => {
    expect(wechselNoetig('skx', 'athena', true)).toBe(true)
    expect(wechselNoetig('skx', 'skx', true)).toBe(false)
    expect(wechselNoetig('skx', 'athena', false)).toBe(false)
    expect(wechselNoetig(null, 'athena', true)).toBe(false)
    expect(wechselNoetig('skx', null, true)).toBe(false)
  })
  it('erkennt eigene Programme', () => {
    expect(istEigenesProgramm('eigen-mimosa')).toBe(true)
    expect(istEigenesProgramm('skx-canna-aqua')).toBe(false)
  })
})
