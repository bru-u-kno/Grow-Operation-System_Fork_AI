import { describe, expect, it } from 'vitest'
import { ruhezeitAusFeldern, stundeAusFeld } from './ruhezeit'

describe('stundeAusFeld', () => {
  it('liest die üblichen Schreibweisen', () => {
    for (const text of ['22', '22:00', '22.00', '22 Uhr', '22:00 Uhr', '22h', ' 22 ']) {
      expect(stundeAusFeld(text), text).toEqual({ stunde: 22 })
    }
    expect(stundeAusFeld('7')).toEqual({ stunde: 7 })
    expect(stundeAusFeld('07:00')).toEqual({ stunde: 7 })
    expect(stundeAusFeld('0')).toEqual({ stunde: 0 })
    expect(stundeAusFeld('24:00')).toEqual({ stunde: 0 })
  })
  it('leer heißt: keine Grenze', () => {
    expect(stundeAusFeld('')).toEqual({ stunde: null })
  })
  it('Unlesbares wird gemeldet, nicht verschluckt', () => {
    expect(stundeAusFeld('abends').fehler).toContain('keine Uhrzeit')
    expect(stundeAusFeld('25').fehler).toContain('0 bis 23')
    expect(stundeAusFeld('22:30').fehler).toContain('vollen Stunden')
  })
})

describe('ruhezeitAusFeldern', () => {
  it('zwei Grenzen', () => {
    expect(ruhezeitAusFeldern('22:00', '7 Uhr')).toEqual({ start: 22, ende: 7 })
  })
  it('beide leer ist erlaubt', () => {
    expect(ruhezeitAusFeldern('', '')).toEqual({ start: null, ende: null })
  })
  it('nur eine Grenze ist keine Ruhezeit — das wird gesagt', () => {
    expect(ruhezeitAusFeldern('22', '').fehler?.bis).toBeTruthy()
    expect(ruhezeitAusFeldern('', '7').fehler?.von).toBeTruthy()
  })
  it('gleiche Stunden sind keine Ruhezeit', () => {
    expect(ruhezeitAusFeldern('22', '22:00').fehler?.bis).toBeTruthy()
  })
})
