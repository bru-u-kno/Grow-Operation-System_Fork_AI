import { test, expect } from '@playwright/test'

/**
 * Die Zahlenzeile der Geräteseite darf am Handy nicht über den Kartenrand ragen.
 *
 * Anlass (09.10.2026): Mit dem Label „nicht zugeordnet" (statt „vermutet") brauchten
 * vier Spalten mit `nowrap` zusammen ca. 337 px, die Karte hat bei 360 px innen nur 302.
 * „korrigiert" stand dadurch von x=296 bis 366 — rechts neben der Karte (Rand bei 348).
 *
 * Gemessen wird gegen die gebaute App; nur die Geräte-Antwort ist gemockt, damit
 * alle vier Zahlen mit ihren echten Labels stehen.
 */
const SEITE = {
  anzahlGeraete: 14, anzahlEntitaeten: 46, anzahlUnzugeordnet: 1, anzahlVerschoben: 3, hinweise: [],
  geraete: [{
    schluessel: 'ha:a', name: 'RDWC', elternSchluessel: null, anschluss: null, istController: true, istRubrik: false,
    istUnzugeordnet: false, elternVomNutzer: false, nameVomNutzer: false, abgeleiteterEltern: null, modell: null,
    tentId: null, hardwareItemId: null, entitaeten: [],
  }],
}

for (const breite of [320, 360, 390, 430, 1280]) {
  test(`Zahlenzeile bleibt in der Karte bei ${breite} px`, async ({ page }) => {
    await page.setViewportSize({ width: breite, height: 800 })
    await page.route(/\/api\/geraete$/, (route) => route.fulfill({ json: SEITE }))
    await page.goto('/geraete')
    const zahlen = page.locator('.gr-zahlen')
    await expect(zahlen).toBeVisible()

    const m = await zahlen.evaluate((zeile) => {
      const karte = zeile.parentElement!.getBoundingClientRect()
      const stil = getComputedStyle(zeile.parentElement!)
      const innenRechts = karte.right - parseFloat(stil.paddingRight)
      return {
        innenRechts,
        viewport: document.documentElement.clientWidth,
        labels: [...zeile.querySelectorAll('span')].map((s) => {
          // Textbreite, nicht Spaltenbreite: der Block-span ist immer so breit wie die Spalte.
          const bereich = document.createRange()
          bereich.selectNodeContents(s)
          return { text: s.textContent, rechts: bereich.getBoundingClientRect().right }
        }),
      }
    })

    expect(m.labels).toHaveLength(4)
    for (const label of m.labels) {
      expect(label.rechts, `„${label.text}" ragt über den Kartenrand`).toBeLessThanOrEqual(m.innenRechts + 0.5)
      expect(label.rechts, `„${label.text}" ragt über den Bildschirm`).toBeLessThanOrEqual(m.viewport)
    }
  })
}
