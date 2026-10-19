import { test, expect, type Page } from '@playwright/test'
import { backendAntwortet, darfUeberspringen } from './pflicht'

/**
 * Einstellungen → System zeigt die Version des installierten Add-ons.
 *
 * <b>Der Anlass.</b> Die Zeile „Version“ nannte nur „Cultivate Control · Add-on“, nie eine Nummer (Sichtprüfung 18.10.2026). Für
 * Fehlerberichte und Fremdinstallationen muss sichtbar sein, welcher Stand läuft. Die Nummer nennt der Supervisor
 * (`appVersion` in `/api/system/backend-health`); ohne Home Assistant steht dort null, und die Zeile sagt „unbekannt“.
 *
 * Die Seite lädt erst, wenn `/api/settings` antwortet — ohne Backend (Rauchtest im Tor) zeigt sie nur den Fehlerzustand und die
 * Zeile bliebe „unbekannt“. Deshalb läuft die Prüfung nur mit Backend; im strengen Lauf ist ein fehlendes Backend ein Fehler.
 * Die Antwort wird hier gestellt, damit die Prüfung ohne Supervisor läuft; der Weg Supervisor → Antwort hat Backend-Tests
 * (`SupervisorInfoServiceTests`, `SystemApiControllerTests`).
 */
async function oeffneMitVersion(page: Page, appVersion: string | null) {
  await page.route('**/api/system/backend-health', (route) => route.fulfill({
    contentType: 'application/json',
    body: JSON.stringify({ appName: 'Cultivate Control', backendSchema: 'backend-core.v0.18-candidate', checkedAtUtc: new Date().toISOString(), appVersion }),
  }))
  await page.goto('/einstellungen', { waitUntil: 'networkidle' })
  return page.locator('.co-row', { hasText: /^Version/ })
}

test('die Zeile „Version“ nennt die Nummer des Add-ons', async ({ page, request }) => {
  darfUeberspringen(!(await backendAntwortet(request)), 'Kein Backend unter der Basis-Adresse: die Einstellungsseite lädt nur mit /api/settings.')
  const zeile = await oeffneMitVersion(page, '2.0.0-forkai.246')
  await expect(zeile).toHaveCount(1)
  await expect(zeile).toContainText('2.0.0-forkai.246')
})

test('ohne Nummer steht dort „unbekannt“ statt eines erfundenen Werts', async ({ page, request }) => {
  darfUeberspringen(!(await backendAntwortet(request)), 'Kein Backend unter der Basis-Adresse: die Einstellungsseite lädt nur mit /api/settings.')
  const zeile = await oeffneMitVersion(page, null)
  await expect(zeile).toHaveCount(1)
  await expect(zeile).toContainText('unbekannt')
  await expect(zeile).not.toContainText('Add-on')
})
