import { describe, expect, it } from 'vitest'
import { messZeitpunkt } from './mess-zeitpunkt'

/** Ortszeit, damit der Test in jeder Zeitzone dasselbe prüft. */
const ort = (j: number, m: number, t: number, h: number, min: number) => new Date(j, m - 1, t, h, min).toISOString()
const jetzt = new Date(2026, 9, 2, 14, 0)

describe('messZeitpunkt', () => {
  it('heute nur die Uhrzeit', () => {
    expect(messZeitpunkt(ort(2026, 10, 2, 9, 30), jetzt)).toBe('09:30')
  })
  it('gestern sagt es', () => {
    expect(messZeitpunkt(ort(2026, 10, 1, 21, 5), jetzt)).toBe('gestern 21:05')
  })
  it('vor drei Tagen steht das Datum dabei — sie sieht nicht mehr frisch aus', () => {
    expect(messZeitpunkt(ort(2026, 9, 29, 9, 30), jetzt)).toBe('29.09. 09:30')
  })
  it('aus einem anderen Jahr mit Jahr', () => {
    expect(messZeitpunkt(ort(2025, 12, 30, 8, 0), jetzt)).toBe('30.12.2025 08:00')
  })
  it('Unlesbares wird leer', () => {
    expect(messZeitpunkt('kaputt', jetzt)).toBe('')
  })
})
