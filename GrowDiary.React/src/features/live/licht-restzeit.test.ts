import { describe, expect, it } from 'vitest'
import { dauerInWorten, naechsterZeitpunkt, restzeitText } from './licht-restzeit'

describe('Restzeit bis zum Lichtwechsel', () => {
  const abends = new Date(2026, 8, 13, 21, 11, 0)

  it('nennt die Zeit bis zum Einschalten, wenn das Licht aus ist', () => {
    // 21:11 -> 05:04 am nächsten Morgen sind 7 Std 53 Min.
    expect(restzeitText(abends, false, '05:04', '17:04')).toBe('Wechsel in: 7 h 53 min')
  })

  it('nennt die Zeit bis zum Ausschalten, wenn das Licht an ist', () => {
    const mittags = new Date(2026, 8, 13, 12, 0, 0)
    expect(restzeitText(mittags, true, '05:04', '17:04')).toBe('Wechsel in: 5 h 4 min')
  })

  it('rechnet über Mitternacht', () => {
    const kurzVorMitternacht = new Date(2026, 8, 13, 23, 30, 0)
    const ziel = naechsterZeitpunkt(kurzVorMitternacht, '00:15')
    expect(ziel?.getDate()).toBe(14)
  })

  it('schweigt ohne brauchbare Zeit', () => {
    expect(restzeitText(abends, false, null, '17:04')).toBeNull()
    expect(restzeitText(abends, false, 'Zeitplan', '17:04')).toBeNull()
    expect(naechsterZeitpunkt(abends, '25:70')).toBeNull()
  })

  it('schreibt Dauern lesbar', () => {
    expect(dauerInWorten(48 * 60_000)).toBe('48 min')
    expect(dauerInWorten(3 * 3600_000)).toBe('3 h 0 min')
    expect(dauerInWorten(3 * 3600_000 + 25 * 60_000)).toBe('3 h 25 min')
    expect(dauerInWorten(0)).toBe('gleich')
  })
})
