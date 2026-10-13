import { describe, expect, it } from 'vitest'
import {
  abweichungsWort,
  feuchteBand,
  feuchteZoneImBand,
  grenzenFuerPhase,
  kontextPlaketten,
  temperaturBand,
  temperaturZoneImBand,
  zielText,
} from './entfeuchtung-ziel'

/** Brus Grenzwerte vom 12.10.2026 (GET /api/zielwerte). */
const FEUCHTE_REGEL = { min: 45, max: 54, nachtMax: 60, toleranz: 0 }
const TEMP_REGEL = { min: 14, max: 22, nachtMin: 12, nachtMax: 20, toleranz: 4 }

describe('Zielband der Luftfeuchte — zwei Seiten', () => {
  it('Obergrenze aus dem Plan, Untergrenze aus dem Grenzwert (Bru, 13.10.2026: auch zu trocken ist kritisch)', () => {
    expect(feuchteBand(54, FEUCHTE_REGEL, false)).toEqual({ unten: 45, oben: 54 })
  })

  it('der Plan hat Vorrang vor der Obergrenze des Grenzwerts — nur sie ist das Ziel der Entfeuchter (nachts steht dort 60)', () => {
    expect(feuchteBand(54, FEUCHTE_REGEL, false).oben).toBe(54)
    // Ohne Plan-Wert springt der Nachtwert des Grenzwerts ein.
    expect(feuchteBand(null, FEUCHTE_REGEL, false).oben).toBe(60)
  })

  it('ohne Grenzwert bleibt das Band einseitig wie vorher: nur die Obergrenze', () => {
    expect(feuchteBand(54, null, true)).toEqual({ unten: null, oben: 54 })
  })

  it('zu trocken ist „knapp daneben" und weiter weg „deutlich daneben" — symmetrisch zu zu feucht', () => {
    const band = feuchteBand(54, FEUCHTE_REGEL, true)
    expect(feuchteZoneImBand(50, band)).toBe('ziel')
    expect(feuchteZoneImBand(55.7, band)).toBe('knapp')
    expect(feuchteZoneImBand(58, band)).toBe('knapp')
    expect(feuchteZoneImBand(58.1, band)).toBe('kritisch')
    expect(feuchteZoneImBand(43, band)).toBe('knapp')
    expect(feuchteZoneImBand(41, band)).toBe('knapp')
    expect(feuchteZoneImBand(40.9, band)).toBe('kritisch')
  })

  it('eine Untergrenze über der Obergrenze ergibt kein umgedrehtes Band — dann gilt nur die Obergrenze', () => {
    expect(feuchteBand(54, { min: 60, max: 70 }, true)).toEqual({ unten: null, oben: 54 })
    expect(temperaturBand(16, { min: 25, max: 20 }, true)).toEqual({ unten: null, oben: 20 })
  })

  it('ohne Untergrenze zählt „zu niedrig" nicht — der Rechner erfindet keine', () => {
    expect(feuchteZoneImBand(20, { unten: null, oben: 54 })).toBe('ziel')
  })

  it('ohne Messwert oder ohne jede Grenze gibt es keine Aussage', () => {
    expect(feuchteZoneImBand(null, { unten: 45, oben: 54 })).toBeNull()
    expect(feuchteZoneImBand(50, { unten: null, oben: null })).toBeNull()
  })
})

describe('Zielband der Temperatur', () => {
  it('nachts gelten die Nachtgrenzen des Grenzwerts (12–20), tags die Tagesgrenzen (14–22)', () => {
    expect(temperaturBand(16, TEMP_REGEL, false)).toEqual({ unten: 12, oben: 20 })
    expect(temperaturBand(18, TEMP_REGEL, true)).toEqual({ unten: 14, oben: 22 })
  })

  it('ohne Nachtwerte gelten nachts die Tageswerte', () => {
    expect(grenzenFuerPhase({ min: 14, max: 22 }, false)).toEqual({ unten: 14, oben: 22 })
  })

  it('ohne Grenzwert: Plan-Luft ± Toleranz, ohne beides kein Band', () => {
    expect(temperaturBand(16, { min: null, max: null, toleranz: 4 }, false)).toEqual({ unten: 12, oben: 20 })
    expect(temperaturBand(null, null, false)).toEqual({ unten: null, oben: null })
  })

  it('ohne Grenzwert und ohne Toleranz gibt es kein Band — nie „16 – 16"', () => {
    expect(temperaturBand(16, null, false)).toEqual({ unten: null, oben: null })
    expect(temperaturBand(16, { min: null, max: null, toleranz: 0 }, false)).toEqual({ unten: null, oben: null })
  })

  it('Brus Zelt: 21 °C nachts liegt über dem Plan-Band (12–20) — knapp daneben, solange die Höchsttemperatur (23) nicht erreicht ist', () => {
    const band = temperaturBand(16, TEMP_REGEL, false)
    expect(temperaturZoneImBand(21, band, 23, 2)).toBe('knapp')
    expect(temperaturZoneImBand(20, band, 23, 2)).toBe('ziel')
    expect(temperaturZoneImBand(23, band, 23, 2)).toBe('knapp')
    expect(temperaturZoneImBand(23.1, band, 23, 2)).toBe('kritisch')
  })

  it('zu kalt: bis zum Rand unter der Untergrenze knapp, weiter weg deutlich daneben', () => {
    const band = temperaturBand(16, TEMP_REGEL, false)
    expect(temperaturZoneImBand(10, band, 23, 2)).toBe('knapp')
    expect(temperaturZoneImBand(9.9, band, 23, 2)).toBe('kritisch')
  })
})

describe('Zielzeile und Plaketten', () => {
  it('zeigt „45 – 54", „≤ 54" oder „≥ 45", ohne Grenze nichts', () => {
    expect(zielText({ unten: 45, oben: 54 })).toBe('45 – 54')
    expect(zielText({ unten: null, oben: 54 })).toBe('≤ 54')
    expect(zielText({ unten: 45, oben: null })).toBe('≥ 45')
    expect(zielText({ unten: null, oben: null })).toBeNull()
  })

  it('Dezimalzahlen deutsch, ganze Zahlen ohne „,0"', () => {
    expect(zielText({ unten: 12.5, oben: 20 })).toBe('12,5 – 20')
  })

  it('Plaketten: Lichtphase und Woche — Fehlendes bleibt weg, kein Platzhalter', () => {
    expect(kontextPlaketten(false, 'Flush')).toEqual(['☾ Nacht', 'Woche Flush'])
    expect(kontextPlaketten(true, null)).toEqual(['☀ Tag'])
    expect(kontextPlaketten(null, null)).toEqual([])
  })
})

describe('Das Urteil in Worten', () => {
  const feuchte = { unten: 45, oben: 54 }
  it('nennt Richtung und Maß: knapp/deutlich, über/unter', () => {
    expect(abweichungsWort('feuchte', 50, feuchte, 'ziel')).toBe('im Ziel')
    expect(abweichungsWort('feuchte', 55.7, feuchte, 'knapp')).toBe('knapp über dem Ziel')
    expect(abweichungsWort('feuchte', 70, feuchte, 'kritisch')).toBe('deutlich über dem Ziel')
    expect(abweichungsWort('feuchte', 43, feuchte, 'knapp')).toBe('knapp unter dem Ziel')
    expect(abweichungsWort('feuchte', 30, feuchte, 'kritisch')).toBe('deutlich unter dem Ziel')
  })

  it('bei der Temperatur heißt es „dem Plan", und über der Höchsttemperatur „zu warm — alle aus"', () => {
    const temp = { unten: 12, oben: 20 }
    expect(abweichungsWort('temperatur', 21, temp, 'knapp')).toBe('knapp über dem Plan')
    expect(abweichungsWort('temperatur', 24, temp, 'kritisch')).toBe('zu warm — alle aus')
    expect(abweichungsWort('temperatur', 8, temp, 'kritisch')).toBe('deutlich unter dem Plan')
  })

  it('ohne Zone oder Messwert „keine Aussage" statt einer erfundenen', () => {
    expect(abweichungsWort('feuchte', null, feuchte, null)).toBe('keine Aussage')
  })
})
