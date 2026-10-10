import { describe, expect, it } from 'vitest'
import { BLUELAB_ALARME, BLUELAB_SCHLUESSEL, bluelabLage } from './bluelab-anleitung'
import type { GeraetZeile } from '../steuerung/steuerung-typen'

const zeit = (iso: string) => `um ${iso}`

function z(rolle: string, eingetragen: string, gefunden: boolean): GeraetZeile {
  return {
    rolle, label: rolle, gruppe: 'messen', einheit: null, hinweis: null, pflicht: false, domains: [], vorgabe: '',
    eingetragen, entityId: eingetragen || null, livewert: null, gefunden,
  }
}

const alleDa = [...BLUELAB_ALARME.map((r) => z(r, `number.${r}`, true)), z('skript', 'script.grenze_setzen', true)]
const nichts = [...BLUELAB_ALARME.map((r) => z(r, '', false)), z('skript', '', false)]

describe('bluelabLage', () => {
  it('frisch: vier offene Schritte, nicht fertig, Zahlen genannt', () => {
    const l = bluelabLage(nichts, null, zeit)
    expect(l.schritte.map((s) => s.status)).toEqual(['offen', 'offen', 'offen', 'offen'])
    expect(l.fertig).toBe(false)
    expect(l.schritte[0].text).toContain('Zugeordnet 0 von 6')
  })

  it('eingerichtet und übertragen: alles ok und fertig', () => {
    const l = bluelabLage(alleDa, { eingerichtet: true, zuletztUtc: '2026-10-09T20:43:00Z', fehler: null }, zeit)
    expect(l.schritte.map((s) => s.status)).toEqual(['ok', 'ok', 'ok', 'ok'])
    expect(l.fertig).toBe(true)
    expect(l.schritte[1].text).toBe('script.grenze_setzen gefunden.')
    expect(l.schritte[3].text).toContain('um 2026-10-09T20:43:00Z')
  })

  it('zugeordnet, aber nicht gefunden: warnt statt „fehlt"', () => {
    const zeilen = [...BLUELAB_ALARME.map((r, i) => z(r, `number.${r}`, i < 4)), z('skript', 'script.weg', false)]
    const l = bluelabLage(zeilen, null, zeit)
    expect(l.schritte[0].status).toBe('warn')
    expect(l.schritte[0].text).toContain('6 von 6 zugeordnet, aber nur 4')
    expect(l.schritte[1].status).toBe('warn')
    expect(l.schritte[1].text).toContain('script.weg')
  })

  it('eine Störung der Übertragung wird genannt und verhindert „fertig"', () => {
    const l = bluelabLage(alleDa, { eingerichtet: true, zuletztUtc: '2026-10-09T20:43:00Z', fehler: 'kein Access-Token' }, zeit)
    expect(l.schritte[3]).toMatchObject({ status: 'warn' })
    expect(l.schritte[3].text).toContain('kein Access-Token')
    expect(l.fertig).toBe(false)
  })

  it('alles zugeordnet, aber noch nie geschrieben: nicht fertig, sagt was folgt', () => {
    const l = bluelabLage(alleDa, { eingerichtet: true, zuletztUtc: null, fehler: null }, zeit)
    expect(l.schritte[2].status).toBe('ok')
    expect(l.schritte[3].status).toBe('offen')
    expect(l.schritte[3].text).toContain('alle 5 Minuten')
    expect(l.fertig).toBe(false)
  })

  it('fehlende Rollen-Zeilen (Modul unbekannt) stürzen nicht ab', () => {
    const l = bluelabLage([], null, zeit)
    expect(l.schritte[0].status).toBe('offen')
    expect(l.fertig).toBe(false)
  })
})

describe('Schlüssel', () => {
  it('es sind genau sechs, einer je Alarm-Rolle', () => {
    expect(BLUELAB_SCHLUESSEL).toHaveLength(BLUELAB_ALARME.length)
    for (const r of BLUELAB_ALARME) expect(BLUELAB_SCHLUESSEL).toContain(`setting.${r}_alarm`)
  })
})
