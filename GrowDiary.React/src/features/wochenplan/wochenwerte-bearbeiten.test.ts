import { describe, expect, it } from 'vitest'
import {
  aenderungen,
  aufPlan,
  fehler,
  punkt,
  schluessel,
  startIndex,
  type WochenwertFeld,
  type Wochenwerte,
} from './wochenwerte-bearbeiten'

function feld(name: string, wert: number | null, plan: number | null, extra: Partial<WochenwertFeld> = {}): WochenwertFeld {
  return {
    feld: name,
    bezeichnung: name,
    einheit: '',
    min: 0,
    max: 2000,
    schritt: 0.1,
    wert,
    plan,
    geaendert: wert !== plan,
    ...extra,
  }
}

function werte(): Wochenwerte {
  return {
    growId: 1,
    programmId: 'skx',
    programmName: 'SKX',
    andereGrows: 0,
    spalten: [
      {
        id: 'w3', label: 'Flores 3', stage: 'Flower', woche: 3, istJetzt: false,
        felder: [feld('waterTempNightC', 18, 18), feld('vpdMin', 1.0, 1.0), feld('vpdMax', 1.4, 1.4)],
      },
      {
        id: 'w4', label: 'Flores 4', stage: 'Flower', woche: 4, istJetzt: true,
        felder: [feld('waterTempNightC', 19, 18), feld('vpdMin', 1.2, 1.2), feld('vpdMax', 1.4, 1.4)],
      },
    ],
  }
}

describe('aenderungen', () => {
  it('schickt einen neuen Wert mit Komma als Zahl', () => {
    expect(aenderungen(werte(), { [schluessel('w3', 'waterTempNightC')]: '17,5' }))
      .toEqual([{ spalteId: 'w3', feld: 'waterTempNightC', wert: 17.5 }])
  })

  it('zählt ein nur angetipptes Feld nicht als Änderung', () => {
    expect(aenderungen(werte(), { [schluessel('w4', 'waterTempNightC')]: '19' })).toEqual([])
  })

  it('schickt den Planwert als null, damit die Abweichung gelöscht wird', () => {
    expect(aenderungen(werte(), { [schluessel('w4', 'waterTempNightC')]: '18' }))
      .toEqual([{ spalteId: 'w4', feld: 'waterTempNightC', wert: null }])
  })

  it('versteht ein geleertes Feld als „zurück auf den Plan"', () => {
    expect(aenderungen(werte(), { [schluessel('w4', 'waterTempNightC')]: '  ' }))
      .toEqual([{ spalteId: 'w4', feld: 'waterTempNightC', wert: null }])
  })

  it('lässt Unlesbares weg, statt es als Plan zu speichern', () => {
    expect(aenderungen(werte(), { [schluessel('w3', 'waterTempNightC')]: '17x' })).toEqual([])
  })
})

describe('fehler', () => {
  it('meldet Unlesbares mit Woche und Feld', () => {
    expect(fehler(werte(), { [schluessel('w3', 'waterTempNightC')]: '17x' })[0]).toContain('Flores 3')
  })

  it('meldet ein „von" über dem „bis" — auch wenn nur eins davon geändert wurde', () => {
    const saetze = fehler(werte(), { [schluessel('w4', 'vpdMin')]: '1,6' })
    expect(saetze).toHaveLength(1)
    expect(saetze[0]).toContain('vpdMin')
  })

  it('meldet Werte außerhalb des Bereichs', () => {
    const w = werte()
    w.spalten[0].felder[0] = feld('waterTempNightC', 18, 18, { min: 10, max: 30 })
    expect(fehler(w, { [schluessel('w3', 'waterTempNightC')]: '35' })).toHaveLength(1)
  })

  it('ist leer, wenn alles passt', () => {
    expect(fehler(werte(), { [schluessel('w4', 'vpdMin')]: '1,3' })).toEqual([])
  })
})

describe('punkt und aufPlan', () => {
  it('zeigt gespeicherte Abweichungen und offene Änderungen getrennt', () => {
    const w = werte()
    expect(punkt(w, {}, w.spalten[1])).toEqual({ offen: false, eigen: true })
    expect(punkt(w, { [schluessel('w3', 'vpdMax')]: '1,5' }, w.spalten[0])).toEqual({ offen: true, eigen: true })
  })

  it('setzt eine Gruppe auf den Plan zurück und macht daraus eine Änderung', () => {
    const w = werte()
    const entwurf = aufPlan({}, w.spalten[1], ['waterTempNightC'])
    expect(punkt(w, entwurf, w.spalten[1])).toEqual({ offen: true, eigen: false })
    expect(aenderungen(w, entwurf)).toEqual([{ spalteId: 'w4', feld: 'waterTempNightC', wert: null }])
  })

  it('beginnt bei der laufenden Woche', () => {
    expect(startIndex(werte())).toBe(1)
  })
})
