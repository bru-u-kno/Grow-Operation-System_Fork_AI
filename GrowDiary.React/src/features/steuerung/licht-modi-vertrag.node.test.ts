import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { LICHT_MODI } from './steuerung-typen'

/**
 * Die Licht-Modi gegen die eine Wahrheit im Backend (`AcModi.cs`).
 *
 * **Der Anlass (02.10.2026).** Die Modi eines AC-Infinity-Geräts standen an drei
 * Stellen — `LichtSteuerungService.Modi`, `LICHT_MODI` hier und eine
 * Übersetzungstabelle in `deutsche-woerter.ts` —, und die Steuerungs-Übersicht
 * zeigte trotzdem „Modus Auto", weil das Backend die Übersetzung nicht kannte.
 *
 * Jetzt steht die Tabelle in `AcModi.cs`, und die Oberfläche bekommt den
 * deutschen Namen mitgeschickt (`modusName`). Übrig bleiben in der Oberfläche
 * drei KENNUNGEN, gegen die die Licht-Seite vergleicht. Die hält diese Datei
 * gegen die C#-Datei — gelesen wird die Datei selbst, keine abgetippte Liste.
 */
const WEB = new URL('../../../../GrowDiary.Web/', import.meta.url)
const acModi = readFileSync(new URL('Services/AcModi.cs', WEB), 'utf8')

/** `public const string Zeitplan = "Schedule";` → { Zeitplan: 'Schedule' } — Kommentarzeilen zählen nicht. */
function konstanten(quelle: string): Record<string, string> {
  const raus: Record<string, string> = {}
  for (const zeile of quelle.split('\n')) {
    if (/^\s*\/\//.test(zeile)) continue
    const treffer = /^\s*public const string (\w+) = "([^"]*)";/.exec(zeile)
    if (treffer) raus[treffer[1]] = treffer[2]
  }
  return raus
}

/** Wie die C#-Konstante zum Schlüssel in LICHT_MODI heisst. */
const ZUORDNUNG: Record<keyof typeof LICHT_MODI, string> = { aus: 'Aus', an: 'An', zeitplan: 'Zeitplan' }

describe('LICHT_MODI gegen AcModi.cs', () => {
  const backend = konstanten(acModi)

  it('sieht die Konstanten des Backends (Mengenwächter)', () => {
    // Die Anlage bietet fünfzehn Modi an. Fände die Suche keine, wäre jeder
    // Vergleich darunter leer und grün.
    expect(Object.keys(backend).length).toBeGreaterThanOrEqual(15)
  })

  it('jede Kennung der Oberfläche ist dieselbe wie im Backend', () => {
    const schluessel = Object.keys(LICHT_MODI) as Array<keyof typeof LICHT_MODI>
    expect(schluessel.length).toBeGreaterThanOrEqual(3)
    for (const k of schluessel) {
      expect(LICHT_MODI[k], `LICHT_MODI.${k} weicht von AcModi.${ZUORDNUNG[k]} ab — `
        + 'der Vergleich auf der Licht-Seite träfe dann nie.').toBe(backend[ZUORDNUNG[k]])
    }
  })

  it('das Backend schickt den deutschen Namen mit, den die Oberfläche anzeigt', () => {
    // Ohne diese Felder stünde auf der Licht-Seite und im AC-Test nichts —
    // die Oberfläche übersetzt seit dem 02.10.2026 nicht mehr selbst.
    const licht = readFileSync(new URL('Services/LichtSteuerungService.cs', WEB), 'utf8')
    const acTest = readFileSync(new URL('Services/AcTest.cs', WEB), 'utf8')
    expect(licht).toMatch(/^\s*public string ModusName => AcModi\.Name\(Modus\);/m)
    expect(acTest).toMatch(/^\s*public string\? ModusName => .*AcModi\.Name\(Modus\);/m)

    const typen = readFileSync(new URL('./steuerung-typen.ts', import.meta.url), 'utf8')
    const lichtLive = typen.slice(typen.indexOf('export type LichtLive = {'), typen.indexOf('\n}', typen.indexOf('export type LichtLive = {')))
    expect(lichtLive).toMatch(/^\s{2}modusName: string$/m)
  })
})
