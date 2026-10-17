/* src/entitaets-passung-katalog.node.test.ts
   ZÄHLUNG STATT LISTE: Jede Rolle der Steuerungen hat eine Vorschlagsregel.

   Die Auswahl je Rolle sortiert Entitäten vor (features/geraete/entitaets-passung.ts): Rollen MIT Einheit nach
   Einheit/Geräteklasse, Rollen OHNE Einheit nach dem Namen. Kommt eine Rolle mit einer neuen Einheit oder einem
   Namen ohne bekannten Begriff dazu, bliebe ihre Auswahl still unsortiert — "Passt zu dieser Rolle" bliebe leer.
   Diese Prüfung liest den Katalog selbst (`SteuerungGeraeteRollen.Alle`), keine abgeschriebene Liste.
*/

import { readFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import { begriffeAusRollenname, BEGRIFFE, EINHEIT_PASSUNG } from './features/geraete/entitaets-passung'

const WURZEL = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..')
const KATALOG = join(WURZEL, 'GrowDiary.Web', 'Models', 'SteuerungGeraet.cs')

type Rolle = { modul: string; schluessel: string; label: string; domains: string[]; einheit: string | null }

/** Alle `new(Modul, "schluessel", "Label", Gruppe, new[] { "domain", … }, … Einheit: "x" …)` aus dem Katalog; Kommentarzeilen zählen nicht. */
function rollen(): Rolle[] {
  const zeilen = readFileSync(KATALOG, 'utf8').split('\n').filter((z) => !z.trim().startsWith('//') && !z.trim().startsWith('*'))
  const text = zeilen.join('\n')
  const start = text.indexOf('public static IReadOnlyList<GeraeteRolle> Alle')
  const ende = text.indexOf('public const string ZusatzModul')
  const katalog = text.slice(start, ende)
  const stuecke = katalog.split(/\n\s*new\(/).slice(1)
  const treffer: Rolle[] = []
  for (const stueck of stuecke) {
    const kopf = /^\s*(?:"([\w-]+)"|(ZusatzModul)),\s*"(\w+)",\s*"([^"]+)",\s*Gruppe\w+,\s*new\[\]\s*\{([^}]*)\}/.exec(stueck)
    if (!kopf) continue
    const einheit = /Einheit:\s*"([^"]+)"/.exec(stueck)
    treffer.push({
      modul: kopf[1] ?? 'entfeuchter-zusatz',
      schluessel: kopf[3],
      label: kopf[4],
      domains: [...kopf[5].matchAll(/"(\w+)"/g)].map((m) => m[1]),
      einheit: einheit ? einheit[1] : null,
    })
  }
  return treffer
}

/** Rollen ohne Einheit, deren Name keinen Begriff nennt — ausdrücklich, mit Grund. Heute keine. */
const AUSNAHMEN: Record<string, string> = {}

describe('Vorschlagsregel je Rolle', () => {
  const alle = rollen()

  it('die Prüfung sieht den Katalog (Mengenwächter)', () => {
    expect(alle.length).toBeGreaterThanOrEqual(50)
    expect(alle.some((r) => r.modul === 'entfeuchter-zusatz')).toBe(true)
    expect(alle.every((r) => r.domains.length > 0)).toBe(true)
  })

  it('jede Einheit einer Rolle steht in der Passungstabelle', () => {
    const fehlend = alle.filter((r) => r.einheit && !EINHEIT_PASSUNG[r.einheit]).map((r) => `${r.modul}/${r.schluessel}: ${r.einheit}`)
    expect(fehlend).toEqual([])
  })

  it('jede Rolle ohne Einheit nennt in ihrem Namen einen bekannten Begriff — oder steht mit Grund in AUSNAHMEN', () => {
    const ohneRegel = alle
      .filter((r) => !r.einheit)
      .filter((r) => begriffeAusRollenname(r.label).length === 0)
      .filter((r) => !AUSNAHMEN[`${r.modul}/${r.schluessel}`])
      .map((r) => `${r.modul}/${r.schluessel}: ${r.label}`)
    expect(ohneRegel).toEqual([])
  })

  it('auch Rollen mit Einheit haben mindestens einen Namensbegriff (für Entitäten ohne Einheit)', () => {
    const ohne = alle.filter((r) => r.einheit && begriffeAusRollenname(r.label).length === 0).map((r) => `${r.modul}/${r.schluessel}: ${r.label}`)
    expect(ohne).toEqual([])
  })

  it('AUSNAHMEN nennt nur Rollen, die es gibt', () => {
    const vorhanden = new Set(alle.map((r) => `${r.modul}/${r.schluessel}`))
    expect(Object.keys(AUSNAHMEN).filter((k) => !vorhanden.has(k))).toEqual([])
  })

  it('Begriffe haben eindeutige Kennungen und Wörter', () => {
    const ids = BEGRIFFE.map((b) => b.id)
    expect(new Set(ids).size).toBe(ids.length)
    expect(BEGRIFFE.every((b) => b.woerter.length > 0)).toBe(true)
  })
})
