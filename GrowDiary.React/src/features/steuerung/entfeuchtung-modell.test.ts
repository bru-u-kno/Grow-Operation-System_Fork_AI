import { describe, expect, it } from 'vitest'
import { hauptStatus, mitEntfeuchtung, zusatzStatus, zusatzVorhanden } from './entfeuchtung-modell'
import type { EntfeuchterLive, EntfeuchterZusatzLive, SteuerungModul } from './steuerung-typen'

const modul = (kennung: string, titel: string, status: string, unterzeile = 'bereit'): SteuerungModul =>
  ({ kennung, titel, status, kurz: `kurz ${titel}`, wert: '48 %', unterzeile, hatDetail: true })

describe('Entfeuchtung als ein Eintrag (A-015)', () => {
  it('fasst Hauptentfeuchter und Zusatz an der Stelle des ersten zusammen', () => {
    const liste = mitEntfeuchtung([
      modul('co2', 'CO₂', 'an'), modul('entfeuchter', 'RDWC Dehumi', 'an', 'entfeuchtet'),
      modul('entfeuchter-zusatz', 'Dehumi Tent', 'aus', 'bereit'), modul('chiller', 'Chiller', 'an'),
    ])
    expect(liste.map((m) => m.kennung)).toEqual(['co2', 'entfeuchtung', 'chiller'])
    const e = liste[1]
    expect(e.titel).toBe('Entfeuchtung')
    expect(e.status).toBe('an')
    expect(e.unterzeile).toBe('entfeuchtet · Dehumi Tent: bereit')
  })

  it('der Status warnt, sobald eines warnt', () => {
    const liste = mitEntfeuchtung([modul('entfeuchter', 'A', 'an'), modul('entfeuchter-zusatz', 'B', 'warn')])
    expect(liste[0].status).toBe('warn')
  })

  it('ohne bekannten Zusatz bleibt es bei den Angaben des Hauptentfeuchters', () => {
    const liste = mitEntfeuchtung([modul('entfeuchter', 'RDWC Dehumi', 'aus'), modul('entfeuchter-zusatz', 'Dehumi Tent', 'aus', 'Zustand unbekannt')])
    expect(liste).toHaveLength(1)
    expect(liste[0].kurz).toBe('kurz RDWC Dehumi')
    expect(liste[0].unterzeile).toBe('bereit')
  })

  it('ohne Entfeuchter-Modul bleibt die Liste, wie sie ist', () => {
    const l = [modul('co2', 'CO₂', 'an')]
    expect(mitEntfeuchtung(l)).toBe(l)
  })
})

describe('Zusatz vorhanden? und Statuswort', () => {
  const z = (o: Partial<EntfeuchterZusatzLive>) => ({ haErreichbar: true, zusatzAn: null, zusatzOnline: null, leistungW: null, ...o }) as EntfeuchterZusatzLive
  it('ohne Zustand der Steckdose bei erreichbarem HA ist keiner zugeordnet', () => {
    expect(zusatzVorhanden(z({}))).toBe(false)
    expect(zusatzVorhanden(z({ zusatzAn: false }))).toBe(true)
    expect(zusatzVorhanden(z({ zusatzOnline: false }))).toBe(true)
  })
  it('ohne Antwort von Home Assistant wird nichts versteckt', () => {
    expect(zusatzVorhanden(z({ haErreichbar: false }))).toBe(true)
    expect(zusatzVorhanden(null)).toBe(false)
  })
  it('Statuswörter: nur ein Wort, nie eine Leistung', () => {
    expect(zusatzStatus(z({ zusatzAn: true }), false)).toBe('läuft')
    expect(zusatzStatus(z({ zusatzAn: false }), false)).toBe('aus')
    expect(zusatzStatus(z({ zusatzAn: false }), true)).toBe('wartet')
    expect(zusatzStatus(z({ zusatzOnline: false }), false)).toBe('offline')
    expect(zusatzStatus(z({ zusatzAn: true, automatikAn: false }), false)).toBe('Automatik aus')
    const h = (o: Partial<EntfeuchterLive>) => o as EntfeuchterLive
    expect(hauptStatus(h({ portAn: true }))).toBe('läuft')
    expect(hauptStatus(h({ portAn: false }))).toBe('aus')
    expect(hauptStatus(h({ portAn: true, portOnline: false }))).toBe('offline')
    expect(hauptStatus(h({}))).toBe('unbekannt')
  })
})
