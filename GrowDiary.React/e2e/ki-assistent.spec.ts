import { expect, test } from '@playwright/test'
import { backendAntwortet, darfUeberspringen } from './pflicht'

/**
 * KI-Assistent (Fork AI, 04.10.2026): alles zur eigenen KI an einer Stelle.
 *
 * Geprüft wird, was der Umzug verspricht — am gerenderten DOM, nicht am
 * Quelltext:
 * - die alte Adresse `/berater` landet auf dem Reiter „Mappe", mit dem Grow;
 * - die Einstellungen tragen keinen zweiten Zugriffs-Abschnitt mehr, nur den
 *   Wegweiser — sonst gäbe es dieselbe Handlung an zwei Orten;
 * - „Verbinden" nennt den ECHTEN Namen des Add-ons in der Anweisung zum
 *   Kopieren (aus dem Supervisor, im Demobestand `local_grow_os`), keinen
 *   Platzhalter — sonst funktioniert die Anweisung beim Bediener nicht.
 */

test.beforeEach(async ({ page }) => {
  darfUeberspringen(!(await backendAntwortet(page.request)), 'Kein Backend erreichbar.')
})

test('/berater leitet auf den Reiter Mappe und behält den Grow', async ({ page }) => {
  await page.goto('/berater?growId=1')
  await expect(page).toHaveURL(/\/ki\?(?=.*tab=mappe)(?=.*growId=1)/)
  await expect(page.locator('main h1').first()).toHaveText('KI-Assistent')
  await expect(page.locator('[data-audit="collection-tab-mappe"].active')).toBeVisible()
  await expect(page.getByRole('link', { name: 'Mappe herunterladen' }))
    .toHaveAttribute('href', /\/api\/agent-export\/grows\/1\/paket$/)
})

test('Grow-Seite: der Reiter „Mappe für eigene KI" führt auf die Mappe dieses Grows', async ({ page }) => {
  await page.goto('/grows/1')
  await page.getByRole('link', { name: 'Mappe für eigene KI' }).click()
  await expect(page).toHaveURL(/\/ki\?(?=.*tab=mappe)(?=.*growId=1)/)
  await expect(page.getByRole('link', { name: 'Mappe herunterladen' })).toBeVisible()
})

test('Einstellungen: nur der Wegweiser, kein zweiter Zugriffs-Abschnitt', async ({ page }) => {
  await page.goto('/settings')
  const verweis = page.locator('[data-audit="settings-ki-verweis"]')
  await expect(verweis).toBeVisible()
  await expect(page.locator('[data-audit="settings-ki-zugriff"]')).toHaveCount(0)
  await verweis.getByRole('link', { name: 'Zum KI-Assistenten' }).click()
  await expect(page).toHaveURL(/\/ki\?tab=zugriff$/)
  await expect(page.locator('[data-audit="ki-zugriff-form"]')).toBeVisible({ timeout: 15000 })
})

test('Verbinden: vier Wege, die Anweisung trägt den echten Namen des Add-ons', async ({ page }) => {
  const antwort = await page.request.get('/api/system/mobile-access')
  const { slug } = await antwort.json() as { slug: string | null }
  darfUeberspringen(!slug, 'Kein Add-on-Name vom Backend — ohne ihn gibt es nichts zu vergleichen.')

  await page.goto('/ki')
  await expect(page.locator('[data-audit="collection-tab-verbinden"].active')).toBeVisible()
  for (const weg of ['claude-app', 'claude-code', 'chatgpt', 'mappe']) {
    await expect(page.locator(`[data-audit="ki-weg-${weg}"]`)).toBeVisible()
  }
  const anweisung = page.locator('[data-audit="ki-weg-claude-app-text"]')
  await expect(anweisung).toContainText(`„${slug}“`)
  await expect(anweisung).not.toContainText('<Name des Add-ons')
  // Der Schlüssel selbst steht nie in einer Anleitung — nur der Platzhalter.
  await expect(anweisung).toContainText('Bearer gok_…')
  // Prüfer 04.10.2026: dieser Weg geht an Grow OS vorbei — das muss dabeistehen.
  await expect(page.locator('[data-audit="ki-weg-claude-app-warnung"]')).toContainText('vollen Zugriff auf Home Assistant')
})
