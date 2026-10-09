import { describe, expect, it } from 'vitest'
import {
  automationen, fehlendText, helferUndRechenwerte, reife, summe, wuerdeSchreiben, zaehleFehlend,
  type Aufruf, type AutoBilanz,
} from './bereitstellen-ablauf'
import type { BauteilStand, Bestandsaufnahme } from './steuerung-typen'

function bauteil(art: string, stand = 'Fehlt'): BauteilStand {
  return { entityId: `x.${art}_${Math.random()}`, name: art, art, zweck: art, stand, pflicht: true, ohneDas: null }
}

function bestand(...bauteile: BauteilStand[]): Bestandsaufnahme {
  return {
    haErreichbar: true, eingerichtet: true, da: bauteile.filter((b) => b.stand === 'Da').length,
    fehlt: bauteile.filter((b) => b.stand === 'Fehlt').length, entfaellt: 0, veraltet: 0,
    fehlendeRollen: [], ausgefalleneFunktionen: [], bauteile,
  }
}

/** Ein Aufruf, der alles mitschreibt und je Pfad antwortet. */
function aufzeichnung(antworten: Record<string, unknown>) {
  const calls: string[] = []
  const aufruf: Aufruf = async <T,>(pfad: string, o?: { method?: string }) => {
    calls.push(`${o?.method ?? 'GET'} ${pfad}`)
    const a = antworten[pfad]
    if (a instanceof Error) throw a
    return (a ?? { angelegt: 0, fehlgeschlagen: 0 }) as T
  }
  return { calls, aufruf }
}

describe('zaehleFehlend', () => {
  it('zählt nur, was fehlt — nach Art getrennt', () => {
    const f = zaehleFehlend(bestand(
      bauteil('Zahl'), bauteil('Zahl'), bauteil('Schalter'), bauteil('Zaehler'), bauteil('Zeitpunkt'),
      bauteil('RechenSensor'), bauteil('Mittelwert'), bauteil('Automation'),
      bauteil('Zahl', 'Da'), bauteil('Automation', 'Veraltet'), bauteil('Zahl', 'Entfaellt'),
    ))
    expect(f).toEqual({ helfer: 5, rechenwerte: 2, automationen: 1 })
  })

  it('kennt keinen Bestand ohne abzustürzen', () => {
    expect(zaehleFehlend(null)).toEqual({ helfer: 0, rechenwerte: 0, automationen: 0 })
  })

  it('fasst mehrere Module zusammen und schreibt es lesbar', () => {
    const s = summe([{ helfer: 13, rechenwerte: 3, automationen: 1 }, { helfer: 12, rechenwerte: 4, automationen: 2 }])
    expect(s).toEqual({ helfer: 25, rechenwerte: 7, automationen: 3 })
    expect(fehlendText(s)).toBe('25 Helfer · 7 Rechenwerte · 3 Automationen')
    expect(fehlendText({ helfer: 0, rechenwerte: 1, automationen: 0 })).toBe('1 Rechenwert')
    expect(fehlendText({ helfer: 0, rechenwerte: 0, automationen: 0 })).toBe('')
  })
})

describe('helferUndRechenwerte', () => {
  it('legt je Modul erst Helfer, dann Rechenwerte an — in der übergebenen Reihenfolge', async () => {
    const { calls, aufruf } = aufzeichnung({
      '/api/steuerung/co2/helfer': { angelegt: 20, fehlgeschlagen: 0 },
      '/api/steuerung/co2/rechenwerte': { angelegt: 5, fehlgeschlagen: 0 },
      '/api/steuerung/entfeuchter/helfer': { angelegt: 15, fehlgeschlagen: 1 },
    })
    const r = await helferUndRechenwerte(['co2', 'entfeuchter'], {
      co2: bestand(bauteil('Zahl'), bauteil('RechenSensor')),
      entfeuchter: bestand(bauteil('Zahl')),
    }, aufruf)

    expect(calls).toEqual([
      'POST /api/steuerung/co2/helfer', 'POST /api/steuerung/co2/rechenwerte', 'POST /api/steuerung/entfeuchter/helfer',
    ])
    expect(r).toEqual([
      { modul: 'co2', helfer: 20, rechenwerte: 5, nicht: 0, fehler: null },
      { modul: 'entfeuchter', helfer: 15, rechenwerte: 0, nicht: 1, fehler: null },
    ])
  })

  it('fragt Module nicht an, bei denen nichts fehlt', async () => {
    const { calls, aufruf } = aufzeichnung({})
    await helferUndRechenwerte(['co2'], { co2: bestand(bauteil('Zahl', 'Da'), bauteil('Automation')) }, aufruf)
    expect(calls).toEqual([])
  })

  it('ein Fehler bei einem Modul hält die anderen nicht auf', async () => {
    const { calls, aufruf } = aufzeichnung({
      '/api/steuerung/co2/helfer': new Error('Home Assistant antwortet nicht'),
      '/api/steuerung/zuluft/helfer': { angelegt: 7, fehlgeschlagen: 0 },
    })
    const r = await helferUndRechenwerte(['co2', 'zuluft'], {
      co2: bestand(bauteil('Zahl'), bauteil('RechenSensor')), zuluft: bestand(bauteil('Zahl')),
    }, aufruf)

    expect(r[0].fehler).toBe('Home Assistant antwortet nicht')
    expect(r[0].rechenwerte).toBe(0) // nach dem Fehler beim Helfer wird kein Rechenwert mehr versucht
    expect(r[1]).toMatchObject({ modul: 'zuluft', helfer: 7, fehler: null })
    expect(calls).toContain('POST /api/steuerung/zuluft/helfer')
    expect(calls).not.toContain('POST /api/steuerung/co2/rechenwerte')
  })
})

describe('automationen', () => {
  const bilanz: AutoBilanz = {
    angelegt: 2, fremd: 1, fehlgeschlagen: 0,
    einzeln: [
      { kennung: 'a', name: 'a', titel: 'A', stand: 'Angelegt', hinweis: null },
      { kennung: 'b', name: 'b', titel: 'B', stand: 'Erneuert', hinweis: null },
      { kennung: 'c', name: 'c', titel: 'C', stand: 'Fremd', hinweis: 'handgebaut' },
    ],
  }

  it('die Vorschau schreibt nichts: sie hängt vorschau=true an', async () => {
    const { calls, aufruf } = aufzeichnung({ '/api/steuerung/co2/automationen?vorschau=true': bilanz })
    const r = await automationen(['co2'], true, aufruf)
    expect(calls).toEqual(['POST /api/steuerung/co2/automationen?vorschau=true'])
    expect(wuerdeSchreiben(r)).toBe(2) // „Fremd" zählt nicht — die bleibt unangetastet
  })

  it('das Anlegen läuft ohne vorschau und je Modul einzeln', async () => {
    const { calls, aufruf } = aufzeichnung({})
    await automationen(['co2', 'entfeuchter'], false, aufruf)
    expect(calls).toEqual(['POST /api/steuerung/co2/automationen', 'POST /api/steuerung/entfeuchter/automationen'])
  })

  it('ein Fehler wird gemeldet, die übrigen Module laufen weiter', async () => {
    const { aufruf } = aufzeichnung({ '/api/steuerung/co2/automationen': new Error('abgelehnt') })
    const r = await automationen(['co2', 'chiller'], false, aufruf)
    expect(r[0]).toMatchObject({ modul: 'co2', bilanz: null, fehler: 'abgelehnt' })
    expect(r[1].fehler).toBeNull()
  })
})

describe('reife', () => {
  const voll = { pflichtZugeordnet: 6, pflichtGesamt: 6 }
  it('fehlende Pflichtgeräte gehen vor allem anderen', () => {
    expect(reife({ pflichtZugeordnet: 1, pflichtGesamt: 2, module: ['chiller'] }, { chiller: bestand() })).toBe('unvollstaendig')
  })
  it('die Lampe braucht nur die Zuordnung', () => {
    expect(reife({ pflichtZugeordnet: 2, pflichtGesamt: 2, module: [] }, {})).toBe('ohne-bausteine')
  })
  it('ohne Antwort von Home Assistant lässt sich nicht sagen, was fehlt', () => {
    expect(reife({ ...voll, module: ['co2'] }, { co2: null })).toBe('ha-stumm')
    expect(reife({ ...voll, module: ['co2'] }, { co2: { ...bestand(), haErreichbar: false } })).toBe('ha-stumm')
  })
  it('bereit, wenn nichts fehlt — anlegen, sobald bei einem Modul etwas fehlt', () => {
    expect(reife({ ...voll, module: ['entfeuchter', 'entfeuchter-zusatz'] },
      { entfeuchter: bestand(bauteil('Zahl', 'Da')), 'entfeuchter-zusatz': bestand(bauteil('Zahl', 'Da')) })).toBe('bereit')
    expect(reife({ ...voll, module: ['entfeuchter', 'entfeuchter-zusatz'] },
      { entfeuchter: bestand(bauteil('Zahl', 'Da')), 'entfeuchter-zusatz': bestand(bauteil('Zahl')) })).toBe('anlegen')
  })
})
