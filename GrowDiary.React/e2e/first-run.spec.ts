import { test, expect } from '@playwright/test'

/**
 * Die erste Minute mit Grow OS.
 *
 * Eine frische Installation hat kein Zelt, keinen Grow, keine Sensoren — die
 * Live-Seite hatte dafür keinen eigenen Zustand und zeigte ein leeres Cockpit,
 * das aussah wie ein Fehler. Wer dann nicht weiß, dass es „Erste Schritte"
 * gibt, findet sie auch nicht: sie stehen nicht in der Navigation.
 *
 * Der leere Bestand lässt sich hier nicht herstellen, ohne die Datenbank zu
 * leeren — deshalb wird die Zeltliste für diesen Test abgefangen. Geprüft wird
 * das, was der Nutzer sieht: die Reihenfolge der Schritte und dass jeder davon
 * irgendwo hinführt.
 */
test.describe('Erstlauf ohne Daten', () => {
  test.beforeEach(async ({ page }) => {
    await page.route('**/api/settings/tents*', (route) => route.fulfill({
      status: 200, contentType: 'application/json', body: '[]',
    }))
    await page.route('**/api/grows*', (route) => route.fulfill({
      status: 200, contentType: 'application/json', body: '[]',
    }))
  })

  test('führt durch Zelt, Hydro und Grow — in dieser Reihenfolge', async ({ page }) => {
    await page.goto('/', { waitUntil: 'networkidle' })

    const erstlauf = page.locator('[data-audit="live-first-run"]')
    await expect(erstlauf).toBeVisible()

    const schritte = erstlauf.locator('.ls-firstrun li')
    await expect(schritte).toHaveCount(4)
    await expect(schritte.nth(0)).toContainText('Zelt anlegen')
    await expect(schritte.nth(1)).toContainText('Hydro')
    await expect(schritte.nth(2)).toContainText('Grow starten')
    // A-016: Die Steuerungen kommen danach — sie brauchen weder Zelt noch Grow, aber gehören zum Ankommen.
    await expect(schritte.nth(3)).toContainText('Steuerungen einrichten')

    // Jeder Schritt muss auch irgendwo hinführen.
    await expect(erstlauf.locator('a[href$="/zelte/new"]')).toBeVisible()
    await expect(erstlauf.locator('a[href$="/hydro/new"]')).toBeVisible()
    await expect(erstlauf.locator('a[href$="/grows/new"]')).toBeVisible()
    await expect(erstlauf.locator('a[href$="/steuerung/einrichtung"]')).toBeVisible()
  })

  test('bietet die Ersten Schritte an, die sonst nur in den Einstellungen stehen', async ({ page }) => {
    await page.goto('/', { waitUntil: 'networkidle' })

    const start = page.locator('[data-audit="live-first-run"] a[href$="/start"]')
    await expect(start).toBeVisible()
    await start.click()
    await expect(page).toHaveURL(/\/start$/)
  })
})
