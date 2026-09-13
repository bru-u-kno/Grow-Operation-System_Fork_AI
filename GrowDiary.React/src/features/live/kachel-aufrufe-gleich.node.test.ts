import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

/**
 * Beide Stellen, die eine Messwert-Kachel bauen, geben ihr dasselbe mit.
 *
 * <b>Der Anlass (13.09.2026).</b> Die Live-Seite rendert ihre Kacheln über
 * `DashboardBands` (Bereiche mit Kacheln), `LiveScreen` hat daneben einen
 * eigenen Aufruf. Beim Nachtband und bei der Licht-Restzeit wurde jeweils nur
 * eine der beiden Stellen nachgezogen — das Ergebnis sah nach einem alten
 * Zwischenspeicher aus: der Server lieferte das Neue, die Oberfläche zeigte es
 * nicht, und gesucht wurde zweimal an der falschen Stelle.
 *
 * Deshalb hier ein Vergleich der Eigenschaften statt einer Liste, die jemand
 * pflegen müsste: was die eine Stelle übergibt, muss die andere auch übergeben.
 */
describe('Kachel-Aufrufe', () => {
  const eigenschaften = (pfad: string): Set<string> => {
    const quelle = readFileSync(new URL(pfad, import.meta.url), 'utf8')
    const anfang = quelle.indexOf('<MetricTile')
    expect(anfang, `${pfad} baut keine MetricTile`).toBeGreaterThan(-1)
    const block = quelle.slice(anfang, quelle.indexOf('/>', anfang))
    return new Set(Array.from(block.matchAll(/^\s*([a-zA-Z]+)=\{/gm), (treffer) => treffer[1]))
  }

  it('geben beide Stellen dieselben Eigenschaften mit', () => {
    const band = eigenschaften('./DashboardBands.tsx')
    const schirm = eigenschaften('./LiveScreen.tsx')

    // Was nur an einer Stelle Sinn ergibt, steht hier mit Grund.
    const nurImBand = new Set(['key'])

    const fehltImBand = [...schirm].filter((name) => !band.has(name) && !nurImBand.has(name))
    const fehltImSchirm = [...band].filter((name) => !schirm.has(name) && !nurImBand.has(name))

    expect(fehltImBand, 'DashboardBands übergibt diese Eigenschaften nicht').toEqual([])
    expect(fehltImSchirm, 'LiveScreen übergibt diese Eigenschaften nicht').toEqual([])
  })
})
