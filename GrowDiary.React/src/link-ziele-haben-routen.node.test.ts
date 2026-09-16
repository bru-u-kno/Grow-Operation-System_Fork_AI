/* src/link-ziele-haben-routen.node.test.ts
   Jeder Link in der App führt auf eine Seite, die es gibt.

   <b>Der Anlass (15.09.2026).</b> forkai.105 hat „Töpfe & Sorten bearbeiten“ auf
   der Pflanzenkarte eingeführt — mit dem Ziel `/grows/{id}/bearbeiten`. Die
   Route heißt `/grows/:growId/setup`, und eine Auffangroute gibt es nicht: der
   Knopf führte auf eine leere Seite. Keine Prüfung hat es bemerkt, weil
   `routes-reachable.node.test.ts` nur die eine Richtung kennt — hat jede Route
   einen Link? Diese Datei ist die Gegenrichtung: hat jeder Link eine Route?

   <b>Was gezählt wird.</b> Über den ganzen Quelltext (ohne Tests und ohne
   `App.tsx`, wo die Routen selbst stehen): jedes `to=`, `to:` und `navigate(`
   mit einer festen Zeichenkette oder einem Template. Ein `${…}` direkt nach
   einem `/` steht für einen Pfadteil, eines mitten im Wort für einen Anhang
   (`/diagnose${scope}` ist `/diagnose` plus Suchparameter). Zieladressen, die
   aus Variablen kommen (`to={ziel}`), sind nicht auswertbar und fallen heraus —
   dafür gibt es die Selbsttests unten, damit die Menge nicht still schrumpft.
*/

import { readFileSync, readdirSync, statSync } from 'node:fs'
import { dirname, join, relative } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import { legacyRedirects } from './navigation'

const SRC = dirname(fileURLToPath(import.meta.url))

function alleDateien(ordner: string, treffer: string[] = []): string[] {
  for (const eintrag of readdirSync(ordner)) {
    const pfad = join(ordner, eintrag)
    if (statSync(pfad).isDirectory()) alleDateien(pfad, treffer)
    else if (/\.tsx?$/.test(eintrag) && !/\.test\.tsx?$/.test(eintrag) && !eintrag.endsWith('App.tsx')) treffer.push(pfad)
  }
  return treffer
}

/** Kommentare entfernen — eine Erwähnung in der Doku ist kein Link (CLAUDE.md, Regel 4). */
function ohneKommentare(text: string): string {
  return text
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/(^|[^:])\/\/.*$/gm, '$1')
}

type LinkZiel = { datei: string; ziel: string }

/** Alle auswertbaren Link-Ziele eines Quelltexts. */
function linkZiele(text: string, datei = ''): LinkZiel[] {
  const muster = /(?:\bto=\{?|\bto:\s*|\bnavigate\(\s*)(["'`])(\/[^"'`]*)\1/g
  return [...ohneKommentare(text).matchAll(muster)].map((treffer) => ({ datei, ziel: treffer[2] }))
}

/** Aus einem Link-Ziel den Pfad als Segmentliste machen; `*` steht für einen frei gefüllten Teil. */
function pfadSegmente(ziel: string): string[] {
  const pfad = ziel
    // `${…}` direkt nach `/` ist ein Pfadteil …
    .replace(/\/\$\{[^}]*\}/g, '/*')
    // … jedes andere ist ein Anhang an das Wort davor (Suchparameter o. ä.).
    .replace(/\$\{[^}]*\}.*$/, '')
    .replace(/[?#].*$/, '')
  return pfad.split('/').filter(Boolean)
}

/** Passt ein Link auf eine Route? `:param` in der Route nimmt jeden Teil, `*` im Link jeden Routenteil. */
function passt(link: string[], route: string[]): boolean {
  if (link.length !== route.length) return false
  return link.every((teil, i) => teil === '*' || route[i].startsWith(':') || teil === route[i])
}

const appTsx = readFileSync(join(SRC, 'App.tsx'), 'utf8')
const routen = [
  ...[...appTsx.matchAll(/<Route\s+path="([^"]+)"/g)].map((t) => t[1]),
  ...Object.keys(legacyRedirects),
].map((pfad) => pfad.split('/').filter(Boolean))

const dateien = alleDateien(SRC)
const ziele = dateien.flatMap((datei) => linkZiele(readFileSync(datei, 'utf8'), relative(SRC, datei)))

describe('Link-Ziele', () => {
  it('sieht ihre Grundmenge', () => {
    // Mengenwächter: ohne sie liefe die Prüfung bei leerer Menge null Mal durch.
    expect(routen.length, 'Keine Route aus App.tsx gelesen.').toBeGreaterThan(20)
    expect(ziele.length, 'Kaum Link-Ziele gefunden — das Suchmuster greift nicht mehr.').toBeGreaterThan(100)
    expect(dateien.some((d) => d.endsWith('App.tsx')), 'App.tsx wird mitgelesen.').toBe(false)
    // Die Template-Form muss mitgezählt werden — genau dort saß der Fehler.
    expect(ziele.some((z) => z.ziel.includes('${')), 'Keine Template-Links erkannt.').toBe(true)
  })

  it('erkennt einen Link ohne Route (Bissprobe)', () => {
    const kaputt = linkZiele('<V1LinkButton to={`/grows/${growId}/bearbeiten`}>x</V1LinkButton>')
    expect(kaputt).toHaveLength(1)
    expect(routen.some((r) => passt(pfadSegmente(kaputt[0].ziel), r))).toBe(false)

    const heil = linkZiele('<V1LinkButton to={`/grows/${growId}/setup`}>x</V1LinkButton>')
    expect(routen.some((r) => passt(pfadSegmente(heil[0].ziel), r))).toBe(true)
  })

  it('liest Suchparameter und Anhänge nicht als Pfad', () => {
    expect(pfadSegmente('/diagnose${scope}')).toEqual(['diagnose'])
    expect(pfadSegmente('/regeln?tab=push')).toEqual(['regeln'])
    expect(pfadSegmente('/grows/${grow.id}/addback')).toEqual(['grows', '*', 'addback'])
  })

  it('ignoriert Links in Kommentaren', () => {
    expect(linkZiele('/* früher: to="/gibt-es-nicht" */ const x = 1')).toEqual([])
    expect(linkZiele('// navigate("/weg")\nconst y = 2')).toEqual([])
  })

  it('jeder Link führt auf eine Route', () => {
    const tot = ziele
      .filter((z) => !routen.some((r) => passt(pfadSegmente(z.ziel), r)))
      .map((z) => `${z.datei}: ${z.ziel}`)

    expect(tot, `Diese Links führen auf keine Seite:\n${tot.join('\n')}`).toEqual([])
  })
})
