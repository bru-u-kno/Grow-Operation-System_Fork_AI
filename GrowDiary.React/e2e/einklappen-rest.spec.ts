import { expect, test, type Locator } from '@playwright/test'
import { backendAntwortet, darfUeberspringen } from './pflicht'

/**
 * cc.268: Die übrigen Stellen zum Ein- und Ausklappen folgen dem Muster der Live-Seite — Pfeilknopf links
 * (▾ offen, ▸ zu), Titel, zugeklappt die Kurzauskunft. Geprüft wird das Ergebnis an der echten Seite
 * (Demobestand), und jede Stelle zweimal: aufklappen, zuklappen, wieder aufklappen.
 */
test.beforeEach(async ({ request }) => {
  darfUeberspringen(!(await backendAntwortet(request)), 'Kein Backend unter GROW_OS_URL — die Seiten brauchen die laufende App.')
})

async function zweimalUmschalten(kopf: Locator, pfeil = kopf.locator('.kk-pfeil')) {
  for (let i = 0; i < 2; i++) {
    await expect(kopf).toHaveAttribute('aria-expanded', 'false')
    await expect(pfeil).toHaveText('▸')
    await kopf.click()
    await expect(kopf).toHaveAttribute('aria-expanded', 'true')
    await expect(pfeil).toHaveText('▾')
    await kopf.click()
  }
}

test('Kosten: „Verbrauch" hat den Pfeilknopf links und zugeklappt eine Kurzauskunft', async ({ page }) => {
  await page.goto('/kosten?tab=verbrauch')
  const kopf = page.locator('.ko-verbrauch-zeile').first()
  await expect(kopf.locator('.kk-titel')).toHaveText('Verbrauch')
  await expect(kopf.locator('.kk-kurz')).toBeVisible()
  // Der Pfeil steht links vom Titel, nicht rechts.
  const [pfeil, titel] = await Promise.all([kopf.locator('.kk-pfeil').boundingBox(), kopf.locator('.kk-titel').boundingBox()])
  expect(pfeil!.x).toBeLessThan(titel!.x)
  await zweimalUmschalten(kopf)
})

test('Kosten: das Formular schließt über den Pfeilknopf im Kopf', async ({ page }) => {
  await page.goto('/kosten?tab=anschaffungen')
  await page.locator('[data-audit="kosten-anschaffung-erfassen"]').click()
  const kopf = page.locator('[data-audit="kosten-form-zu"]')
  await expect(kopf).toHaveAttribute('aria-expanded', 'true')
  await expect(kopf.locator('.kk-pfeil')).toHaveText('▾')
  await kopf.click()
  await expect(kopf).toHaveCount(0)
})

test('Probelauf: „Erweitert · Grenzen" ist ein Pfeilknopf, die Felder bleiben im Baum', async ({ page }) => {
  await page.goto('/steuerung/probelauf')
  const kopf = page.locator('.pl-karte .kk-kopf').filter({ hasText: 'Erweitert' })
  await expect(kopf.locator('.kk-kurz')).toContainText('leer = nicht überwacht')
  const feld = page.locator('.pl-grenzen input').first()
  await expect(feld).toBeHidden()
  await zweimalUmschalten(kopf)
  await kopf.click()
  await expect(feld).toBeVisible()
})

test('Diagnose: jeder Eintrag des Beobachtungs-Leitfadens hat Pfeil und Kurzauskunft', async ({ page }) => {
  await page.goto('/diagnose')
  await page.locator('.og-bereiche button').first().click()
  const eintrag = page.locator('.og-eintrag').first()
  await expect(eintrag.locator('.kk-kurz')).toContainText(/\d+ Ursache/)
  await zweimalUmschalten(eintrag)
})

test('Geräte: die Zeile hat den Pfeilknopf links vor dem Namen', async ({ page }) => {
  await page.goto('/geraete')
  const kopf = page.locator('.gr-kopf').first()
  await expect(kopf.locator('.kk-pfeil')).toBeVisible()
  const [pfeil, name] = await Promise.all([kopf.locator('.kk-pfeil').boundingBox(), kopf.locator('.gr-name').boundingBox()])
  expect(pfeil!.x).toBeLessThan(name!.x)
})
