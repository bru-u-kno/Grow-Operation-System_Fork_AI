import { test, expect, type Page } from '@playwright/test'

/**
 * Auf dem Telefon darf nichts über den rechten Rand hinausragen.
 *
 * Der Anlass: ein Tester meldete, dass er sein Handy quer drehen muss, um die
 * VPD-Kachel zu sehen. Ursache ist die Grid-Falle in live-screen.css — ein
 * Grid-Item hat `min-width: auto`, also zieht das breiteste Kind (hier die
 * Phasen-Zeitachse) die ganze Seitenspalte auf, und jede Kachelreihe ragt mit.
 *
 * Die eigentliche Lehre steckt woanders: `ui-baseline.spec.ts` MISST genau das
 * schon lange — und schreibt die Befunde nur auf, ohne je fehlzuschlagen
 * („Recorded rather than asserted"). Ein Test, der nur protokolliert, ist kein
 * Wächter. Dieser hier behauptet.
 *
 * WARNUNG an den naechsten Leser: diese Tests laufen OHNE Backend, die Seiten
 * zeigen ihre Leer-/Ladezustaende. Auf `/` fehlen Kachelband, Kamera und
 * Zeitachse damit ganz — der urspruengliche Fehler waere hier NICHT
 * aufgefallen (eine Gegenprobe mit ausgebauter Regel blieb gruen). Wer das
 * dicht machen will, muss die API-Antworten mocken; bis dahin haelt
 * `src/features/live/schutzregeln.test.ts` wenigstens die Regel selbst fest.
 */

const BREITE = 360
const HOEHE = 800

const SEITEN = [
  { pfad: '/', name: 'live' },
  { pfad: '/messung', name: 'messen' },
  // Das Messprotokoll fehlte in dieser Liste, obwohl es genau die Bauform
  // traegt, um die es hier geht: Tabelle am Schreibtisch, Zeitachse am
  // Telefon. Die Seite war bis beta.50 auch in keinem Menue — beides ist
  // derselbe blinde Fleck.
  { pfad: '/messungen', name: 'messungen' },
  { pfad: '/addback', name: 'addback' },
  { pfad: '/aufgaben', name: 'aufgaben' },
  { pfad: '/grows', name: 'grows' },
  { pfad: '/diagnose', name: 'diagnose' },
  { pfad: '/sorten', name: 'sorten' },
  { pfad: '/hydro', name: 'hydro' },
  { pfad: '/zelte', name: 'zelte' },
  { pfad: '/wasser', name: 'wasser' },
  { pfad: '/sensoren', name: 'sensoren' },
  { pfad: '/dosierung', name: 'dosierung' },
  { pfad: '/regeln', name: 'regeln' },
  // Neu in beta.52 und beim Bauen in KEINER Sichtpruefung — genau der
  // blinde Fleck, den das Messprotokoll oben schon einmal hatte.
  { pfad: '/archiv', name: 'archiv' },
]

/** Was rechts hinausragt, ohne dass man es wegwischen könnte. */
async function ueberstand(page: Page) {
  return page.evaluate(() => {
    const breite = document.documentElement.clientWidth

    // In einem Wischbereich zu liegen ist eine Entscheidung, kein Fehler:
    // eine breite Tabelle oder ein Reiterband scrollen absichtlich in sich.
    const imWischbereich = (el: HTMLElement): boolean => {
      for (let n = el.parentElement; n && n !== document.body; n = n.parentElement) {
        const ox = getComputedStyle(n).overflowX
        if ((ox === 'auto' || ox === 'scroll') && n.scrollWidth > n.clientWidth + 1) return true
      }
      return false
    }

    // Screenreader-Text steht bewusst ausserhalb des Sichtfelds (clip-path).
    // Er ist unsichtbar — seine Geometrie zaehlt nicht.
    const nurFuerScreenreader = (el: HTMLElement): boolean => {
      for (let n: HTMLElement | null = el; n && n !== document.body; n = n.parentElement) {
        const cs = getComputedStyle(n)
        if (cs.clipPath !== 'none' && cs.position === 'absolute') return true
      }
      return false
    }

    return [...document.querySelectorAll<HTMLElement>('main *')]
      .filter((el) => {
        const box = el.getBoundingClientRect()
        if (box.width === 0 || box.height === 0) return false
        if (box.right <= breite + 1) return false
        return !imWischbereich(el) && !nurFuerScreenreader(el)
      })
      .slice(0, 8)
      .map((el) => `${el.tagName.toLowerCase()}.${(el.className || '').toString().split(' ')[0]}@${Math.round(el.getBoundingClientRect().right)}`)
  })
}

for (const seite of SEITEN) {
  test(`kein Ueberstand: ${seite.name} @ ${BREITE}px`, async ({ page }) => {
    await page.setViewportSize({ width: BREITE, height: HOEHE })
    await page.goto(seite.pfad, { waitUntil: 'networkidle' })
    await expect(page.locator('.v1-app-shell')).toBeVisible()

    expect(await ueberstand(page), `Diese Elemente ragen auf ${seite.pfad} rechts hinaus`).toEqual([])

    // Die Seite selbst darf nie seitwaerts scrollen — dann ist der Rahmen kaputt.
    const seitwaerts = await page.evaluate(
      () => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1)
    expect(seitwaerts, `${seite.pfad} scrollt seitwaerts`).toBe(false)
  })
}
