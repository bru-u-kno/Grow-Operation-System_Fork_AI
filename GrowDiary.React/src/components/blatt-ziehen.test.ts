import { describe, expect, it } from 'vitest'
import { schleierDeckung, zugEntscheidung, zugWeg } from './blatt-ziehen'

describe('zugWeg', () => {
  it('gibt nach unten eins zu eins nach', () => {
    expect(zugWeg(120)).toBe(120)
  })

  it('gibt nach oben nur zäh nach', () => {
    expect(zugWeg(-40)).toBe(-10)
  })
})

describe('zugEntscheidung', () => {
  it('schliesst ab der Schwelle, auch langsam gezogen', () => {
    expect(zugEntscheidung(90, 4000)).toBe('schliessen')
  })

  it('federt zurück, wenn zu kurz und ohne Schwung gezogen wurde', () => {
    expect(zugEntscheidung(60, 4000)).toBe('zurueck')
  })

  it('schliesst beim schnellen Wisch schon vor der Schwelle', () => {
    expect(zugEntscheidung(40, 60)).toBe('schliessen')
  })

  it('lässt einen Tipper ohne Weg nicht als Wisch durchgehen', () => {
    expect(zugEntscheidung(2, 1)).toBe('zurueck')
    expect(zugEntscheidung(0, 0)).toBe('zurueck')
  })
})

describe('schleierDeckung', () => {
  it('bleibt am Anfang voll', () => {
    expect(schleierDeckung(0)).toBe(1)
    expect(schleierDeckung(-50)).toBe(1)
  })

  it('wird beim Ziehen heller, aber nie durchsichtig', () => {
    expect(schleierDeckung(150)).toBeCloseTo(0.7, 5)
    expect(schleierDeckung(9000)).toBeCloseTo(0.4, 5)
  })
})
