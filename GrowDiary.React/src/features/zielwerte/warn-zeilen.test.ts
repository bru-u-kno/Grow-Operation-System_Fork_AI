import { describe, expect, it } from 'vitest'
import { seitText, warnZeile } from './warn-zeilen'

describe('Warnung je Wert (F-040)', () => {
  it('nennt Richtung und überschrittene Grenze', () => {
    const z = warnZeile({ key: 'vpd', name: 'VPD', ist: '0,98', einheit: 'kPa', lage: 'darunter', alarmVon: 1, alarmBis: 1.6, istZahl: 0.98 })
    expect(z.richtung).toBe('zu niedrig')
    expect(z.grenze).toBe('unter 1 kPa')
    const h = warnZeile({ key: 'humidity', name: 'Luftfeuchte', ist: '64', einheit: '%', lage: 'darüber', alarmVon: 45, alarmBis: 55, istZahl: 64 })
    expect(h.richtung).toBe('zu hoch')
    expect(h.grenze).toBe('über 55 %')
  })

  it('schreibt „seit" als Uhrzeit, gestern oder Datum', () => {
    const jetzt = new Date(2026, 8, 23, 9, 30)
    expect(seitText(new Date(2026, 8, 23, 9, 2).toISOString(), jetzt)).toBe('09:02')
    expect(seitText(new Date(2026, 8, 22, 18, 10).toISOString(), jetzt)).toBe('gestern 18:10')
    expect(seitText(new Date(2026, 8, 20, 7, 5).toISOString(), jetzt)).toBe('20.09. 07:05')
    expect(seitText(null, jetzt)).toBeNull()
  })
})
