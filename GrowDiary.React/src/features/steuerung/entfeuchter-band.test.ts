import { describe, expect, it } from 'vitest'
import { bandBerechnen, hystereseStufe, tempMax } from './entfeuchter-band'

describe('bandBerechnen', () => {
  it('zeigt nur den Ausschnitt um die Werte (Bru, 21.09.2026: 52/53,2/55 lagen auf 40–70 % zu dicht)', () => {
    const band = bandBerechnen({ aus: 46.1, ein: 52, deckel: 55, ist: 53.2 })!
    expect(band.von).toBe(42)
    expect(band.bis).toBe(59)
    // EIN, Ist und Deckel liegen jetzt deutlich auseinander.
    const ein = band.marken.find((m) => m.art === 'ein')!.pos
    const deckel = band.marken.find((m) => m.art === 'deckel')!.pos
    expect(deckel - ein).toBeGreaterThan(15)
    expect(band.ist).toBeGreaterThan(ein)
    expect(band.ist).toBeLessThan(deckel)
  })

  it('nimmt einen Istwert außerhalb der Schwellen mit auf', () => {
    const band = bandBerechnen({ aus: 46, ein: 52, deckel: 55, ist: 63 })!
    expect(band.bis).toBe(67)
    expect(band.ist).toBeLessThan(100)
  })

  it('liefert ohne jeden Wert kein Band statt einer leeren Skala', () => {
    expect(bandBerechnen({ aus: null, ein: null, deckel: null, ist: null })).toBeNull()
  })

  it('lässt fehlende Marken weg, statt sie auf 0 zu setzen', () => {
    const band = bandBerechnen({ aus: null, ein: 52, deckel: null, ist: 50 })!
    expect(band.marken.map((m) => m.art)).toEqual(['ein'])
    expect(band.zoneAb).toBeNull()
  })
})

describe('hystereseStufe', () => {
  it('erkennt die drei Stufen und sonst „eigener Wert“', () => {
    expect(hystereseStufe(4)).toBe(4)
    expect(hystereseStufe(2)).toBe(2)
    expect(hystereseStufe(3.5)).toBeNull()
  })
})

describe('tempMax', () => {
  it('rechnet Plan + Abstand wie der Server (W5: 24 + 5 = 29)', () => {
    expect(tempMax('plan', 5, 27, 24)).toBe(29)
  })
  it('nimmt ohne Plan den festen Wert', () => {
    expect(tempMax('plan', 5, 28, null)).toBe(28)
  })
  it('ignoriert bei „fest“ den Plan', () => {
    expect(tempMax('fest', 5, 27.5, 24)).toBe(27.5)
  })
})
