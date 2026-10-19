import { test, expect } from '@playwright/test'
import { backendAntwortet, darfUeberspringen } from './pflicht'
import { imHaRahmen } from './geraete/ha-rahmen'

/**
 * Handy → App einrichten: die Hilfe „Ohne HA-Titelleiste“ (Entwurf `titelleiste-hilfe`, 19.10.2026).
 *
 * <b>Der Anlass.</b> Über der App liegt in Home Assistant eine Titelleiste, die ein Add-on nicht abschalten kann; nur die
 * HACS-Integration „Ingress“ legt ein Panel ohne sie an. Wer den Fork installiert, soll das nicht erraten müssen: Die App
 * erkennt die Leiste und zeigt die Schritte samt dem fertigen Block mit der richtigen Kennung.
 *
 * Die Lage wird nachgestellt: HA-Rahmen mit Kopfleiste (`imHaRahmen`), und die Antwort von `/api/system/mobile-access`
 * sagt, ob es das Panel schon gibt. Was der Nutzer sieht, wird gelesen, nicht der Zustand dahinter.
 */
const SLUG = '00d580e3_cultivate_control'
const KEIN_BACKEND = 'Kein Backend unter der Basis-Adresse: die Seite lädt ihre Daten von der App.'

async function mitAntwort(page: import('@playwright/test').Page, ingressPanel: string | null) {
  await page.route('**/api/system/mobile-access', (route) => route.fulfill({
    contentType: 'application/json',
    body: JSON.stringify({ available: true, slug: SLUG, panelPath: `/app/${SLUG}`, ingressPanel, reason: null }),
  }))
}

test.use({ viewport: { width: 420, height: 900 } })

test('Leiste da, kein Panel: die Anleitung nennt die Kennung dieser Installation und lässt sich kopieren', async ({ page, request, context }) => {
  darfUeberspringen(!(await backendAntwortet(request)), KEIN_BACKEND)
  await context.grantPermissions(['clipboard-read', 'clipboard-write']).catch(() => undefined)
  await mitAntwort(page, null)

  const rahmen = await imHaRahmen(page, '/handy?tab=app')
  const karte = rahmen.locator('[data-audit="titelleiste-hilfe"]')
  await expect(karte).toBeVisible()
  await expect(karte).toContainText('Über der App liegt die Titelleiste von Home Assistant')
  await expect(karte).toContainText('lovelylain/hass_ingress')
  const yaml = rahmen.locator('[data-audit="titelleiste-yaml"]')
  await expect(yaml).toContainText(`url: ${SLUG}`)
  await expect(yaml).toContainText('ui_mode: normal')
  await expect(yaml).toContainText('work_mode: hassio')

  await karte.getByRole('button', { name: 'In die Zwischenablage kopieren' }).click()
  await expect(karte.getByRole('button', { name: 'Kopiert' })).toBeVisible()
})

test('Panel vorhanden, App über den normalen Eintrag geöffnet: der Hinweis nennt die Adresse des Panels', async ({ page, request }) => {
  darfUeberspringen(!(await backendAntwortet(request)), KEIN_BACKEND)
  await mitAntwort(page, 'growos')

  const rahmen = await imHaRahmen(page, '/handy?tab=app')
  await expect(rahmen.getByText('Du hast schon ein Panel ohne Titelleiste.')).toBeVisible()
  await expect(rahmen.getByText('/growos', { exact: false }).first()).toBeVisible()
  await expect(rahmen.locator('[data-audit="titelleiste-yaml"]')).toHaveCount(0)
})

test('ohne Home-Assistant-Rahmen erscheint die Karte nicht', async ({ page, request }) => {
  darfUeberspringen(!(await backendAntwortet(request)), KEIN_BACKEND)
  await mitAntwort(page, null)

  await page.goto('/handy?tab=app', { waitUntil: 'networkidle' })
  await expect(page.getByText('Scannen — mit der Kamera des Handys')).toBeVisible()
  await expect(page.locator('[data-audit="titelleiste-hilfe"]')).toHaveCount(0)
  await expect(page.getByText('Ohne HA-Titelleiste')).toHaveCount(0)
})
