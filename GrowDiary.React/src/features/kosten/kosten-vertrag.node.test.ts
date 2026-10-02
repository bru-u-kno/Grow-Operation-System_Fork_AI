import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'

/**
 * Die Füllstand-Zeile der Kostenseite gegen den Backend-Vertrag.
 *
 * **Der Anlass (01.10.2026).** Der Typ hier hieß `verbraucht`/`quelle`, das
 * Backend schickt `verbrauchtMenge`/`fuellstandQuelle`. TypeScript merkt so
 * etwas nicht — die Antwort ist ungeprüftes JSON. Die Seite schrieb deshalb
 * auch bei gebuchtem Verbrauch (CO₂-Flasche) immer „Geschätzt noch …".
 *
 * Gelesen wird der C#-Record selbst, keine abgetippte Liste.
 */
const csharp = readFileSync(new URL('../../../../GrowDiary.Web/Services/KostenSeiteService.cs', import.meta.url), 'utf8')
const ts = readFileSync(new URL('./kosten-typen.ts', import.meta.url), 'utf8')

function recordFelder(quelle: string, name: string): string[] {
  const start = quelle.indexOf(`record ${name}(`)
  if (start < 0) return []
  const ende = quelle.indexOf(');', start)
  return quelle.slice(start, ende)
    .split('\n')
    .map((z) => z.replace(/\/\/\/.*$/, '').trim())
    .flatMap((z) => [...z.matchAll(/[\w?<>]+\s+([A-Z]\w*)\s*(?:=[^,]*)?(?:,|$)/g)].map((m) => m[1]))
    .map((n) => n[0].toLowerCase() + n.slice(1))
}

function typFelder(quelle: string, name: string): string[] {
  const start = quelle.indexOf(`export type ${name} = {`)
  if (start < 0) return []
  const ende = quelle.indexOf('\n}', start)
  return [...quelle.slice(start, ende).matchAll(/^\s{2}(\w+)\??:/gm)].map((m) => m[1])
}

describe('KostenFuellungAktuell', () => {
  const backend = recordFelder(csharp, 'KostenFuellungAktuell')
  const frontend = typFelder(ts, 'KostenFuellungAktuell')

  it('sieht beide Seiten (Mengenwächter)', () => {
    expect(backend.length).toBeGreaterThanOrEqual(10)
    expect(frontend.length).toBeGreaterThanOrEqual(10)
  })

  it('jedes Feld der Oberfläche schickt das Backend auch', () => {
    expect(frontend.filter((f) => !backend.includes(f))).toEqual([])
  })
})

/**
 * Die Records der Mehr-Grow-Rechnung (02.10.2026): Teilung, Zelt-Zähler, die
 * Durchgänge fürs Archiv. Dieselbe Falle wie oben — ein Feld, das hier anders
 * heißt als im Backend, kommt nie an, und die Seite zeigt still nichts.
 * Geprüft in BEIDE Richtungen: was das Backend neu schickt, muss die Oberfläche
 * auch kennen, sonst ist es ein Feld ohne Anzeige.
 */
describe.each([
  ['KostenStrom', 15],
  ['KostenTeilung', 3],
  ['KostenZelt', 2],
  ['KostenDurchgang', 10],
  ['KostenSeite', 12],
])('%s', (name, mindestens) => {
  const backend = recordFelder(csharp, name)
  const frontend = typFelder(ts, name)

  it('sieht beide Seiten (Mengenwächter)', () => {
    expect(backend.length).toBeGreaterThanOrEqual(mindestens)
    expect(frontend.length).toBeGreaterThanOrEqual(mindestens)
  })

  it('beide Seiten nennen dieselben Felder', () => {
    expect(frontend.filter((f) => !backend.includes(f))).toEqual([])
    expect(backend.filter((f) => !frontend.includes(f))).toEqual([])
  })
})
