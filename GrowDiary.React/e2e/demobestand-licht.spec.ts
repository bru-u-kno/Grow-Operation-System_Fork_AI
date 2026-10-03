import { test, expect } from '@playwright/test'
import { darfUeberspringen } from './pflicht'

/**
 * Das Licht im Demobestand ist eingerichtet — der Weg mit echtem Lichtmodus
 * ist am Bestand zu sehen.
 *
 * **Der Anlass (offene Punkte 03.10.2026, D3).** Keine Licht-Rolle war
 * zugeordnet: die Lichtseite zeigte „Geräte 0/6", die Übersicht
 * „Modus – · Stufe –". Modus lesen, Stufe zeigen, Zeitplan erkennen — nichts
 * davon kam im Tor je vor, obwohl genau das die Seite ausmacht.
 */
test('Demobestand: Licht mit sechs Rollen, Zeitplan Blüte und Stufe', async ({ page }) => {
  const antwort = await page.request.get('/api/steuerung/licht').catch(() => null)
  darfUeberspringen(!antwort?.ok(), 'Kein Backend erreichbar — laeuft die App unter GROW_OS_URL?')

  await page.goto('/steuerung/licht', { waitUntil: 'networkidle' })
  const seite = page.locator('body')
  await expect(seite, 'Die Licht-Rollen des Demobestands sind nicht zugeordnet.').toContainText(/Geräte\s*6\s*\/\s*6\s*zugeordnet/)
  await expect(seite).toContainText(/Zeitplan \(Blüte\)/i)
  await expect(seite).toContainText('08:00 – 20:00')
  await expect(seite).toContainText('Stufe 7')

  await page.goto('/steuerung', { waitUntil: 'networkidle' })
  await expect(page.locator('body'), 'Die Übersicht zeigt keinen Zeitplan — Modus nicht gelesen?')
    .toContainText('Zeitplan Blüte 08:00 – 20:00 · Stufe 7')
})
