import { test, expect } from '@playwright/test'
import { darfUeberspringen } from './pflicht'

/**
 * Die Seitenleiste muss auf JEDER Seite stehen bleiben, auch auf langen.
 *
 * Der Fehler, den dieser Test festhält, war unsichtbar in jedem Stylesheet, das
 * ihn verursacht hat: ein ungeschichtetes `html, body, #root { height: 100% }`
 * in einer Altlast-Datei deckelte den App-Rahmen auf eine Bildschirmhöhe. Die
 * Leiste klebt zwar (`position: sticky`), hatte damit aber nur einen Bildschirm
 * Spielraum — beim Weiterscrollen lief sie oben raus und darunter stand der
 * nackte Hintergrund.
 *
 * Deshalb prüft der Test nicht die CSS-Regel, sondern das, was man sieht: nach
 * dem Scrollen ans Seitenende steht die Leiste immer noch oben und reicht bis
 * zum unteren Rand.
 */
// Fork AI (forkai.124): /regeln trägt nur noch die Auto-Messungen und ist zu kurz;
// die lange Sammelseite ist jetzt /grenzwerte (bis forkai.132 /zielwerte).
const LANGE_SEITEN = ['/messung', '/grenzwerte', '/wissen']

for (const route of LANGE_SEITEN) {
  test(`Seitenleiste bleibt stehen auf ${route}`, async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 })
    await page.goto(route, { waitUntil: 'networkidle' })

    const scrollbar = await page.evaluate(() => document.documentElement.scrollHeight - window.innerHeight)
    darfUeberspringen(scrollbar < 200,
      `${route} ist nur ${scrollbar} px länger als das Fenster — zum Scrollen zu kurz. Mit `
      + 'gefülltem Bestand ist sie das nicht; ohne Daten prüft dieser Test nichts.')

    await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight))
    await page.waitForFunction(() => window.scrollY > 0)

    const nav = page.locator('.v1-desktop-nav')
    const box = await nav.boundingBox()
    expect(box, 'Seitenleiste nicht gefunden').not.toBeNull()

    // Oben angeheftet …
    expect(Math.abs(box!.y), `Seitenleiste ist bei ${box!.y}px statt oben`).toBeLessThan(2)
    // … und über die volle Höhe sichtbar, kein Streifen Hintergrund darunter.
    const viewport = page.viewportSize()!.height
    expect(box!.y + box!.height, 'Unter der Seitenleiste klafft der Hintergrund').toBeGreaterThanOrEqual(viewport - 2)
  })
}

test('der App-Rahmen wächst mit dem Inhalt statt auf Bildschirmhöhe zu deckeln', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto('/messung', { waitUntil: 'networkidle' })

  const { shell, doc } = await page.evaluate(() => ({
    shell: Math.round(document.querySelector('.v1-app-shell')!.getBoundingClientRect().height),
    doc: document.documentElement.scrollHeight,
  }))

  // Gleich hoch wie das Dokument — nicht ein Bildschirm mit Überhang.
  expect(Math.abs(shell - doc), `Rahmen ${shell}px, Dokument ${doc}px`).toBeLessThan(4)
})
