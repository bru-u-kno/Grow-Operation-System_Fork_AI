import { test, expect } from '@playwright/test'

/**
 * Das Banner, wenn Home Assistant nicht antwortet.
 *
 * Der Fall lässt sich lokal kaum herstellen — der Unterbrecher im Backend macht
 * nach kurzer Zeit von selbst wieder zu. Also wird die Antwort vorgegeben: das
 * ist ohnehin die Zusage, die zählt, nämlich was die Oberfläche aus dieser
 * Antwort macht.
 */
test('meldet einmal oben, wenn Home Assistant nicht antwortet', async ({ page }) => {
  await page.route('**/api/home-assistant/health', (route) =>
    route.fulfill({ json: { configured: true, reachable: false, retryAtUtc: null } }))

  await page.goto('/', { waitUntil: 'networkidle' })

  const banner = page.locator('[data-audit="ha-offline"]')
  await expect(banner).toBeVisible()
  // Die Meldung muss sagen, was weiterhin geht — ein RDWC läuft, auch wenn Home
  // Assistant aussetzt.
  await expect(banner).toContainText('Messen')

  // Genau eine Meldung, nicht eine pro Karte.
  await expect(banner).toHaveCount(1)
})

test('schweigt, solange Home Assistant antwortet', async ({ page }) => {
  await page.route('**/api/home-assistant/health', (route) =>
    route.fulfill({ json: { configured: true, reachable: true, retryAtUtc: null } }))

  await page.goto('/', { waitUntil: 'networkidle' })
  await expect(page.locator('[data-audit="ha-offline"]')).toHaveCount(0)
})

test('schweigt bei jemandem, der Home Assistant gar nicht eingerichtet hat', async ({ page }) => {
  // Sonst stünde bei jedem, der Cultivate Control ohne HA benutzt, dauerhaft eine Warnung.
  await page.route('**/api/home-assistant/health', (route) =>
    route.fulfill({ json: { configured: false, reachable: false, retryAtUtc: null } }))

  await page.goto('/', { waitUntil: 'networkidle' })
  await expect(page.locator('[data-audit="ha-offline"]')).toHaveCount(0)
})
