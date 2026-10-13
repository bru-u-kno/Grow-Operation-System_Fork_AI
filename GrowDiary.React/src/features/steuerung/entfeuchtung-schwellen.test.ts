import { describe, expect, it } from 'vitest'
import {
  SCHWELLE_MIN,
  abstandAnwenden,
  aussenluftNachEinschalten,
  ausAusEin,
  einMinimum,
  einSchwelleSetzen,
  schwellenGetrennt,
} from './entfeuchtung-schwellen'

const BRU = { feuchteEinTag: 50, feuchteAusTag: 46, feuchteEinNacht: 50, feuchteAusNacht: 46 }

describe('Einschalt-Schwelle der Luftfeuchte', () => {
  it('AUS ist EIN minus Abstand — Brus Werte gehen auf (50 − 4 = 46)', () => {
    expect(ausAusEin(50, 4)).toBe(46)
  })

  it('rundet auf eine Nachkommastelle, damit nichts wie 46,00000001 gespeichert wird', () => {
    expect(ausAusEin(50.3, 4)).toBe(46.3)
    expect(ausAusEin(0.1 + 0.2 + 49.7, 4)).toBe(46)
  })

  it('AUS fällt nie unter die Untergrenze des Backends (30 %)', () => {
    expect(ausAusEin(33, 6)).toBe(SCHWELLE_MIN)
  })

  it('ohne „getrennt" gilt ein Wert für Tag und Nacht, und beide AUS werden neu gerechnet', () => {
    const neu = einSchwelleSetzen(BRU, 4, { wert: 52, tageszeit: 'beide' })
    expect(neu).toEqual({ feuchteEinTag: 52, feuchteAusTag: 48, feuchteEinNacht: 52, feuchteAusNacht: 48 })
  })

  it('mit „getrennt" ändert sich nur die gewählte Tageszeit — die andere bleibt samt AUS', () => {
    const nacht = einSchwelleSetzen(BRU, 4, { wert: 55, tageszeit: 'nacht' })
    expect(nacht).toEqual({ feuchteEinTag: 50, feuchteAusTag: 46, feuchteEinNacht: 55, feuchteAusNacht: 51 })
    const tag = einSchwelleSetzen(BRU, 4, { wert: 48, tageszeit: 'tag' })
    expect(tag).toEqual({ feuchteEinTag: 48, feuchteAusTag: 44, feuchteEinNacht: 50, feuchteAusNacht: 46 })
  })

  it('ein neuer Abstand verschiebt nur AUS, nie EIN (Stufe „Ruhig": 50 → 44)', () => {
    expect(abstandAnwenden(BRU, 6)).toEqual({ feuchteEinTag: 50, feuchteAusTag: 44, feuchteEinNacht: 50, feuchteAusNacht: 44 })
    expect(abstandAnwenden(BRU, 2)).toEqual({ feuchteEinTag: 50, feuchteAusTag: 48, feuchteEinNacht: 50, feuchteAusNacht: 48 })
  })

  it('getrennt ist es genau dann, wenn Tag und Nacht verschieden stehen', () => {
    expect(schwellenGetrennt(BRU)).toBe(false)
    expect(schwellenGetrennt({ feuchteEinTag: 60, feuchteEinNacht: 62 })).toBe(true)
  })

  it('der kleinste EIN-Wert lässt AUS noch über der Untergrenze: sonst wäre „AUS muss unter EIN liegen" verletzt', () => {
    for (const abstand of [1, 2, 4, 6, 10]) {
      const ein = einMinimum(abstand)
      expect(ausAusEin(ein, abstand)).toBeLessThan(ein)
    }
  })

  it('die Wartezeit auf Außenluft wird nie kürzer als „Einschalten nach"', () => {
    expect(aussenluftNachEinschalten(5, 10)).toBe(10)
    expect(aussenluftNachEinschalten(25, 10)).toBe(25)
  })

  it('ein geleertes Feld (NaN) macht die Wartezeit nicht kaputt — sie hat kein eigenes Feld zum Korrigieren', () => {
    expect(aussenluftNachEinschalten(5, Number.NaN)).toBe(5)
    expect(aussenluftNachEinschalten(5, 3)).toBe(5)
  })
})
