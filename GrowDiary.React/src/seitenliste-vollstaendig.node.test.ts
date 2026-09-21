import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { ALLE_SEITEN } from '../e2e/seiten'

/**
 * Die Seitenliste der Oberflächen-Prüfungen deckt jede Route ab.
 *
 * **Der Anlass (01.09.2026, vom Prüfer gefunden).** Auf `/grows/1/harvest`
 * standen die Summen englisch — „21.5 g" statt „21,5 g", direkt unter einem
 * Eingabefeld, in dem korrekt „21,5" stand. Gefunden hat es niemand, weil
 * `e2e/seiten.ts` eine **handgeschriebene** Liste war und diese Seite nicht
 * darauf stand: `deutsche-zahlen.spec.ts`, `kontrast.spec.ts`,
 * `handy-zuschnitt.spec.ts` und `zellen-kollision.spec.ts` haben die Seite nie
 * geöffnet.
 *
 * „Eine handgeschriebene Liste kann nur an dem scheitern, was schon
 * draufsteht." Deshalb ist die **Grundmenge** jetzt `App.tsx`: jede Route dort
 * muss in `ALLE_SEITEN` stehen oder hier unten einen ausgeschriebenen Grund
 * haben.
 */

/** Routen, die keine eigene Seite zum Ansehen sind — je mit Grund. */
const OHNE_EIGENE_SEITE: Record<string, string> = {
  '/': 'Leitet auf /live um — dieselbe Seite unter anderem Namen.',
  '/settings': 'Alter Pfad, leitet auf /einstellungen um.',
  '/action': 'Nimmt einen Klick aus einer Push-Nachricht entgegen und leitet weiter.',
  '/messung': 'Kurzform, leitet auf /messungen/new um.',
  '/live': 'Steht im Menü unter „/" und wird darüber geprüft — dieselbe Seite.',
  '/grows/measurements/:measurementId/edit':
    'Braucht eine bestehende Messung; die Id wechselt mit dem Bestand. '
    + 'Der Rundweg dafür läuft in formular-rundweg.spec.ts über die Liste.',
  '/hydro/:id/edit': 'Deckungsgleich mit /hydro/:setupId, nur im Bearbeiten-Modus.',
  '/zielwerte': 'Alte Adresse (bis forkai.132) — leitet je nach Reiter auf /plan, /grenzwerte oder /handy um.',
}

function routenAusApp(): string[] {
  const quelle = readFileSync(join(__dirname, 'App.tsx'), 'utf8')
  const treffer = [...quelle.matchAll(/path="([^"]+)"/g)].map((m) => m[1])
  return [...new Set(treffer)]
}

/** `/grows/:growId/harvest` → `/grows/1/harvest`. */
function mitEins(route: string): string {
  return route.replace(/:[A-Za-z0-9]+/g, '1')
}

describe('Seitenliste der Oberflächen-Prüfungen', () => {
  it('kennt jede Route aus App.tsx', () => {
    const routen = routenAusApp()

    // Mengenwächter: ohne Grundmenge läuft die Prüfung null Mal durch.
    expect(routen.length).toBeGreaterThanOrEqual(40)

    const fehlend = routen
      .filter((route) => !(route in OHNE_EIGENE_SEITE))
      .filter((route) => !ALLE_SEITEN.includes(mitEins(route)))

    expect(
      fehlend,
      `Diese Routen stehen in App.tsx, aber in keiner Oberflächen-Prüfung:\n  ${fehlend
        .map((r) => `${r}  →  ${mitEins(r)}`)
        .join('\n  ')}\n\n`
        + 'Damit sieht sie keine Prüfung auf deutsche Zahlen, Kontrast, Handy-Zuschnitt '
        + 'oder Zellen-Kollision an. Entweder in e2e/seiten.ts aufnehmen oder hier oben '
        + 'mit Grund ausnehmen.',
    ).toEqual([])
  })

  it('nimmt nur aus, was es auch gibt', () => {
    // Sonst bleibt eine Ausnahme fuer eine geloeschte Route stehen und deckt
    // spaeter eine neue Route mit demselben Pfad ab.
    const routen = routenAusApp()
    const tot = Object.keys(OHNE_EIGENE_SEITE).filter((r) => !routen.includes(r))

    expect(tot, `Ausnahmen ohne Route in App.tsx: ${tot.join(', ')}`).toEqual([])
  })
})
