import { test, expect, type Page } from '@playwright/test'

/**
 * Einstellungen → System zeigt die Version des installierten Add-ons.
 *
 * <b>Der Anlass.</b> Die Zeile „Version“ nannte nur „Grow OS · Add-on“, nie eine Nummer (Sichtprüfung 18.10.2026). Für
 * Fehlerberichte und Fremdinstallationen muss sichtbar sein, welcher Stand läuft. Die Nummer nennt der Supervisor
 * (`appVersion` in `/api/system/backend-health`); ohne Home Assistant steht dort null, und die Zeile sagt „unbekannt“.
 *
 * Die Antwort wird hier gestellt, damit die Prüfung ohne Supervisor läuft; der Weg Supervisor → Antwort hat Backend-Tests
 * (`SupervisorInfoServiceTests`, `SystemApiControllerTests`).
 */
async function oeffneMitVersion(page: Page, appVersion: string | null) {
  await page.route('**/api/system/backend-health', (route) => route.fulfill({
    contentType: 'application/json',
    body: JSON.stringify({ appName: 'Grow OS', backendSchema: 'backend-core.v0.18-candidate', checkedAtUtc: new Date().toISOString(), appVersion }),
  }))
  await page.goto('/einstellungen', { waitUntil: 'networkidle' })
  return page.locator('.co-row', { hasText: /^Version/ })
}

test('die Zeile „Version“ nennt die Nummer des Add-ons', async ({ page }) => {
  const zeile = await oeffneMitVersion(page, '2.0.0-forkai.246')
  await expect(zeile).toHaveCount(1)
  await expect(zeile).toContainText('2.0.0-forkai.246')
})

test('ohne Nummer steht dort „unbekannt“ statt eines erfundenen Werts', async ({ page }) => {
  const zeile = await oeffneMitVersion(page, null)
  await expect(zeile).toHaveCount(1)
  await expect(zeile).toContainText('unbekannt')
  await expect(zeile).not.toContainText('Add-on')
})
