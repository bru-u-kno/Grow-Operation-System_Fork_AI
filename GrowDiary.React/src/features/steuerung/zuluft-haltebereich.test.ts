import { describe, expect, it } from 'vitest'
import { bandStellen, haeltGerade, haltebereich } from './zuluft-haltebereich'

describe('haltebereich', () => {
  it('rechnet die Aus-Grenze aus Mindest-Differenz und Abstand', () => {
    expect(haltebereich(1, 0.5)).toEqual({ anAb: 1, ausUnter: 0.5 })
  })
  it('rundet, statt 0,7000000000000001 zu zeigen', () => {
    expect(haltebereich(1, 0.3).ausUnter).toBe(0.7)
  })
  it('geht nie unter 0', () => {
    expect(haltebereich(0.4, 1).ausUnter).toBe(0)
  })
  it('Abstand 0 heißt: an und aus an derselben Grenze', () => {
    const hb = haltebereich(1.5, 0)
    expect(hb.ausUnter).toBe(hb.anAb)
  })
})

describe('haeltGerade', () => {
  const hb = haltebereich(1, 0.5)
  // Echte Werte vom 15.10.2026, 16:39: geglättet 0,78, Bedarf an seit 03:45.
  it('hält, wenn der Bedarf an ist und die Differenz unter der Einschaltgrenze liegt', () => {
    expect(haeltGerade(true, 0.78, hb)).toBe(true)
  })
  it('hält nicht, wenn die Differenz über der Einschaltgrenze liegt', () => {
    expect(haeltGerade(true, 1.2, hb)).toBe(false)
    expect(haeltGerade(true, 1, hb)).toBe(false)
  })
  it('hält nicht, wenn der Bedarf aus ist (dann wartet er auf die Einschaltgrenze)', () => {
    expect(haeltGerade(false, 0.84, hb)).toBe(false)
  })
  it('sagt ohne Messwert nichts', () => {
    expect(haeltGerade(true, null, hb)).toBe(false)
    expect(haeltGerade(null, 0.78, hb)).toBe(false)
  })
})

describe('bandStellen', () => {
  const hb = haltebereich(1, 0.5)
  it('legt Marken und Istwert auf eine Skala von 0 bis 2', () => {
    expect(bandStellen(hb, 0.78)).toEqual({ bis: 2, ausPos: 25, anPos: 50, istPos: 39 })
  })
  it('dehnt die Skala, wenn die Differenz größer ist', () => {
    expect(bandStellen(hb, 3.88).bis).toBe(4)
  })
  it('hält den Istwert im Band, auch bei negativer Differenz', () => {
    expect(bandStellen(hb, -0.4).istPos).toBe(0)
  })
  it('zeigt ohne Messwert keinen Punkt', () => {
    expect(bandStellen(hb, null).istPos).toBeNull()
  })
})
