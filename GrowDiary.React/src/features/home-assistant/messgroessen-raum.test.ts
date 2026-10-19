import { describe, expect, it } from 'vitest'
import { metrikEtikett } from '../live/dashboard-layout'
import { RAUMWERTE_METRIKEN, definitions, vpdKpa } from './messgroessen'

describe('Raumluft-Werte (cc.263)', () => {
  it('rechnet die VPD wie das Backend (RaumLuftTests: gleiche Zahlen)', () => {
    expect(vpdKpa(18.7, 65.8)).toBe(0.74) // Fühler am AC-Infinity-Controller, 19.10.2026: eigener VPD-Wert 0,74
    expect(vpdKpa(25, 50)).toBe(1.58)
    expect(vpdKpa(20, 100)).toBe(0)
  })

  it('liefert bei unsinnigen Werten keine VPD', () => {
    expect(vpdKpa(20, 120)).toBeNull()
    expect(vpdKpa(20, -1)).toBeNull()
    expect(vpdKpa(200, 50)).toBeNull()
    expect(vpdKpa(Number.NaN, 50)).toBeNull()
  })

  it('beschriftet die drei Werte nach dem Raum, sonst bleibt „Außen“', () => {
    expect(metrikEtikett('outside-temperature', 'Außen Temp.', 'Keller')).toBe('Keller Temp.')
    expect(metrikEtikett('outside-humidity', 'Außen RLF', 'Keller')).toBe('Keller RLF')
    expect(metrikEtikett('outside-vpd', 'Außen VPD', 'Keller')).toBe('Keller VPD')
    expect(metrikEtikett('outside-vpd', 'Außen VPD', null)).toBe('Außen VPD')
    expect(metrikEtikett('humidity', 'Luftfeuchte', 'Keller')).toBe('Luftfeuchte')
  })

  it('die ersetzten Messgrößen sind genau die drei Außen-Zeilen der Tabelle', () => {
    const aussen = definitions.filter((d) => d.label.startsWith('Außen')).map((d) => d.metricType)
    expect([...RAUMWERTE_METRIKEN].sort()).toEqual([...aussen].sort())
  })
})
