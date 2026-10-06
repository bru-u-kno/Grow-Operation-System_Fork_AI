import { describe, expect, it } from 'vitest'
import { isNavLeafActive, navGroups } from '../../navigation'
import type { ProbelaufKennzahl, ProbelaufMessreihe } from '../../types'
import {
  erholungText, grenzenAusText, istOffen, kurvenPunkte, proMinuteText, restzeit, startText, statusText, wertText,
} from './probelauf-anzeige'

const k = (groesse: ProbelaufKennzahl['groesse'], proMinute: number, erholung: number | null = null): ProbelaufKennzahl => ({
  groesse, start: 1, spitze: 2, ende: 2, aenderungProMinute: proMinute, erholungMinuten: erholung,
})

describe('Probelauf: Anzeige', () => {
  it('Status steht auf Deutsch, nie als Bezeichner', () => {
    expect(statusText('RueckstellungOffen')).toBe('Zurückstellen offen')
    expect(statusText('Laeuft')).toBe('läuft')
    for (const s of ['Laeuft', 'Nachlauf', 'Fertig', 'Abgebrochen', 'RueckstellungOffen'] as const) {
      expect(statusText(s)).not.toMatch(/ae|oe|ue|Rueck/)
    }
  })

  it('offen heißt: Eingriff, Nachlauf oder unbestätigtes Zurückstellen', () => {
    expect(['Laeuft', 'Nachlauf', 'RueckstellungOffen'].every((s) => istOffen(s as never))).toBe(true)
    expect(istOffen('Fertig')).toBe(false)
    expect(istOffen('Abgebrochen')).toBe(false)
  })

  it('Restzeit als Minuten:Sekunden, nie negativ', () => {
    expect(restzeit(760)).toBe('12:40')
    expect(restzeit(5)).toBe('0:05')
    expect(restzeit(-30)).toBe('0:00')
  })

  it('Änderung je Minute mit Vorzeichen und deutschem Komma', () => {
    expect(proMinuteText(k('Feuchte', 0.29))).toBe('+0,29 %/Min.')
    expect(proMinuteText(k('VPD', -0.008))).toBe('−0,008 kPa/Min.')
    expect(proMinuteText(k('Temperatur', 0))).toBe('0 °C/Min.')
  })

  it('Werte mit Einheit, VPD mit zwei Stellen', () => {
    expect(wertText('Feuchte', 54.2)).toBe('54,2 %')
    expect(wertText('VPD', 1.2)).toBe('1,2 kPa')
    expect(wertText('Temperatur', 25.83)).toBe('25,8 °C')
  })

  it('Erholung: Minuten oder „nicht erreicht"', () => {
    expect(erholungText(k('Feuchte', 0.3, 9))).toBe('9 Min.')
    expect(erholungText(k('Feuchte', 0.3, null))).toBe('nicht erreicht')
  })

  it('Kurvenpunkte in Minuten seit dem Start: Vorlauf negativ, Nachlauf positiv, leere Werte fehlen', () => {
    const start = '2026-10-07T12:00:00Z'
    const reihe: ProbelaufMessreihe = {
      vorlauf: [{ zeitUtc: '2026-10-07T11:50:00Z', feuchte: 50, temp: null, vpd: null }],
      waehrend: [{ zeitUtc: '2026-10-07T12:10:00Z', feuchte: 56, temp: null, vpd: null }],
      nachlauf: [{ zeitUtc: '2026-10-07T12:30:00Z', feuchte: null, temp: null, vpd: null }],
    }

    const p = kurvenPunkte(reihe, 'feuchte', start)

    expect(p.vorlauf).toEqual([{ minute: -10, wert: 50 }])
    expect(p.waehrend).toEqual([{ minute: 10, wert: 56 }])
    expect(p.nachlauf).toEqual([])
    expect(kurvenPunkte(reihe, 'temp', start).waehrend).toEqual([])
  })

  it('Startzeit: heute, gestern, sonst Datum', () => {
    const jetzt = new Date('2026-10-07T12:00:00')
    expect(startText('2026-10-07T08:05:00', jetzt)).toMatch(/^heute \d{2}:\d{2}$/)
    expect(startText('2026-10-06T20:00:00', jetzt)).toMatch(/^gestern \d{2}:\d{2}$/)
    expect(startText('2026-10-01T20:00:00', jetzt)).toMatch(/^01\.10\. \d{2}:\d{2}$/)
  })

  it('Grenzen: Komma-Zahlen werden gelesen, leere Felder bleiben unüberwacht', () => {
    const r = grenzenAusText({ feuchteMax: '62', tempMax: '27,5', vpdMin: '', vpdMax: '' })

    expect(r).toEqual({ grenzen: { feuchteMax: 62, tempMax: 27.5, vpdMin: null, vpdMax: null } })
  })

  it('Grenzen: ein unlesbares Feld ist ein Fehler, kein stilles Weglassen', () => {
    const r = grenzenAusText({ feuchteMax: '62', tempMax: '27,5x', vpdMin: '', vpdMax: '' })

    expect('fehler' in r && r.fehler).toContain('Temperatur max.')
  })

  it('Grenzen: ohne eine einzige Grenze startet kein Lauf', () => {
    const r = grenzenAusText({ feuchteMax: '', tempMax: ' ', vpdMin: '', vpdMax: '' })

    expect('fehler' in r && r.fehler).toContain('Mindestens eine Grenze')
  })

  it('Menü: auf der Probelauf-Seite leuchtet „Probelauf" und nicht zugleich „Steuerung"', () => {
    const blaetter = navGroups.flatMap((g) => g.items)
    const steuerung = blaetter.find((b) => b.to === '/steuerung')!
    const probelauf = blaetter.find((b) => b.to === '/steuerung/probelauf')!

    expect(isNavLeafActive(probelauf, '/steuerung/probelauf')).toBe(true)
    expect(isNavLeafActive(steuerung, '/steuerung/probelauf')).toBe(false)
    // Alle anderen Steuerungsseiten leuchten weiter unter „Steuerung".
    expect(isNavLeafActive(steuerung, '/steuerung/chiller')).toBe(true)
    expect(isNavLeafActive(steuerung, '/steuerung')).toBe(true)
  })
})
