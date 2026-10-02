import { describe, expect, it } from 'vitest'
import { entwurfAbgleichen, stufeAusFeld } from './licht-bedienung'

describe('stufeAusFeld', () => {
  it('liest ganze Zahlen von 1 bis 10', () => {
    expect(stufeAusFeld('1')).toBe(1)
    expect(stufeAusFeld('10')).toBe(10)
    expect(stufeAusFeld(' 7 ')).toBe(7)
  })
  it('alles andere ist keine Stufe', () => {
    expect(stufeAusFeld('')).toBeNull()
    expect(stufeAusFeld('0')).toBeNull()
    expect(stufeAusFeld('11')).toBeNull()
    expect(stufeAusFeld('2.5')).toBeNull()
    expect(stufeAusFeld('abc')).toBeNull()
  })
})

describe('entwurfAbgleichen', () => {
  const alt = { veggieEin: '05:00', veggieAus: '23:00', stufe: 7, verifySekunden: 20 }

  it('behält eine ungespeicherte Änderung, wenn ein Befehl die Stufe ändert', () => {
    const entwurf = { ...alt, veggieEin: '06:00' }
    const neu = { ...alt, stufe: 8 }
    expect(entwurfAbgleichen(entwurf, alt, neu)).toEqual({ ...alt, veggieEin: '06:00', stufe: 8 })
  })

  it('ohne Änderung gilt die Antwort des Backends', () => {
    const neu = { ...alt, stufe: 3, veggieAus: '22:00' }
    expect(entwurfAbgleichen({ ...alt }, alt, neu)).toEqual(neu)
  })

  it('ein geleertes Zahlenfeld (NaN) bleibt leer', () => {
    const entwurf = { ...alt, verifySekunden: Number.NaN }
    expect(entwurfAbgleichen(entwurf, alt, { ...alt, stufe: 2 }).verifySekunden).toBeNaN()
  })
})
