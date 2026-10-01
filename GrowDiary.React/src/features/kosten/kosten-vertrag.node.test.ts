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
