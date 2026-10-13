import { describe, expect, it } from 'vitest'
import {
  SCHWELLE_MIN,
  abstandAnwenden,
  arbeitsbereichAusPlan,
  aussenluftNachEinschalten,
  ausAusEin,
  ausWirksam,
  einMinimum,
  einSchwelleSetzen,
  schwellenGetrennt,
  schwellenNachAbstand,
} from './entfeuchtung-schwellen'

const BRU = { feuchteEinTag: 50, feuchteAusTag: 46, feuchteEinNacht: 50, feuchteAusNacht: 46 }

describe('Einschalt-Schwelle der Luftfeuchte', () => {
  it('AUS ist EIN minus Abstand — Nutzer-Werte gehen auf (50 − 4 = 46)', () => {
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

  it('ein geleertes Feld (NaN) macht die Wartezeit nicht kaputt — sonst bliebe sie NaN, auch wenn „Einschalten nach" wieder gefüllt ist', () => {
    expect(aussenluftNachEinschalten(5, Number.NaN)).toBe(5)
    expect(aussenluftNachEinschalten(5, 3)).toBe(5)
  })

  it('mit „getrennt" bleibt das AUS der anderen Tageszeit stehen, auch wenn es nicht zu EIN minus Abstand passt', () => {
    // Nacht: EIN 62, AUS 55 (bewusst weiter weg als der Abstand 4) — eine Änderung am Tag darf es nicht anheben.
    const bestand = { feuchteEinTag: 60, feuchteAusTag: 57, feuchteEinNacht: 62, feuchteAusNacht: 55 }
    const tag = einSchwelleSetzen(bestand, 4, { wert: 61, tageszeit: 'tag' })
    expect(tag.feuchteAusNacht).toBe(55)
    expect(tag.feuchteEinNacht).toBe(62)
    expect(tag.feuchteAusTag).toBe(57)
    const nacht = einSchwelleSetzen(bestand, 4, { wert: 63, tageszeit: 'nacht' })
    expect(nacht.feuchteAusTag).toBe(57)
    expect(nacht.feuchteAusNacht).toBe(59)
  })

  it('das wirksame AUS ist nie näher als der Abstand unter EIN — die Vorgaben des Backends (60/57, Abstand 4) schalten bei 56 aus', () => {
    expect(ausWirksam(60, 57, 4)).toBe(56)
    expect(ausWirksam(50, 46, 4)).toBe(46)
    // Ein weiter entferntes gespeichertes AUS gilt.
    expect(ausWirksam(62, 55, 4)).toBe(55)
  })
})

describe('Arbeitsbereich aus dem Plan (forkai.212) — dieselben Beispiele wie das Backend', () => {
  it('Nutzer-Zelt: Obergrenze 54, Vorlauf 4, Abstand 4 ergibt 50/46', () => {
    expect(arbeitsbereichAusPlan(54, 4, 4)).toEqual({ ein: 50, aus: 46 })
  })

  it('der Abstand verschiebt nur AUS, der Vorlauf nur EIN, der Wochenwechsel beide', () => {
    expect(arbeitsbereichAusPlan(54, 4, 6)).toEqual({ ein: 50, aus: 44 })
    expect(arbeitsbereichAusPlan(54, 2, 4)).toEqual({ ein: 52, aus: 48 })
    expect(arbeitsbereichAusPlan(50, 4, 4)).toEqual({ ein: 46, aus: 42 })
  })

  it('EIN bleibt in der Spanne des Backends (31 + Abstand bis 90) und rundet auf eine Nachkommastelle', () => {
    expect(arbeitsbereichAusPlan(40, 10, 4)).toEqual({ ein: 35, aus: 31 })
    expect(arbeitsbereichAusPlan(95, 0, 4).ein).toBe(90)
    expect(arbeitsbereichAusPlan(54.34, 4, 4)).toEqual({ ein: 50.3, aus: 46.3 })
  })

  it('ein neuer Abstand rechnet bei „plan" EIN mit neu, bei „fest" nur AUS', () => {
    const bestand = { feuchteEinTag: 50, feuchteAusTag: 46, feuchteEinNacht: 50, feuchteAusNacht: 46, vorlaufProzent: 4 }
    // Plan: Obergrenze 54, Abstand 10 → EIN muss mindestens 31 + 10 = 41; 54 − 4 = 50 bleibt, AUS 40 (über der Untergrenze 30).
    expect(schwellenNachAbstand({ ...bestand, arbeitsbereichQuelle: 'plan' }, 10, 54)).toEqual({ feuchteEinTag: 50, feuchteAusTag: 40, feuchteEinNacht: 50, feuchteAusNacht: 40 })
    // Fest: EIN bleibt auch dann, wenn der Plan etwas anderes sagen würde.
    expect(schwellenNachAbstand({ ...bestand, feuchteEinTag: 58, feuchteEinNacht: 58, arbeitsbereichQuelle: 'fest' }, 6, 54).feuchteEinTag).toBe(58)
    // Ohne Plan-Wert fällt „plan" auf das Verhalten von „fest" zurück.
    expect(schwellenNachAbstand({ ...bestand, arbeitsbereichQuelle: 'plan' }, 6, null).feuchteEinTag).toBe(50)
  })
})

describe('arbeitsbereichAusPlan: halbe Zehntel', () => {
  it('rundet wie das Backend (AwayFromZero)', () => {
    expect(arbeitsbereichAusPlan(54.25, 4, 4)).toEqual({ ein: 50.3, aus: 46.3 })
  })
})
