import { test, expect, type Page } from '@playwright/test'
import { darfUeberspringen } from './pflicht'

/**
 * Die Kühler-Seite zeigt den Bestand — wie die CO₂-Seite.
 *
 * <b>Der Befund (Prüfbericht 01.10.2026, „Noch offen, klein").</b>
 * `GET /api/steuerung/chiller/bestand` meldet seit dem 02.10.2026 die Lücke
 * „Kühler ohne Aus" (Sollwert-Eingang ohne Steckdose: der Wächter kann nicht
 * abschalten). Die Kühler-Seite rief den Bestand gar nicht ab — die Lücke stand
 * nur in der API. Jetzt zeigen beide Seiten ihn mit denselben Bauteilen
 * (`src/features/steuerung/Bestand.tsx`).
 */

test.use({ viewport: { width: 390, height: 740 } })

async function seiteLaden(page: Page, pfad: string): Promise<void> {
  const antwort = await page.goto(pfad, { waitUntil: 'networkidle' })
  darfUeberspringen(antwort == null || antwort.status() >= 400, `${pfad} antwortet nicht — läuft die App unter GROW_OS_URL?`)
  const da = await page.locator('.v1-tabs[aria-label="Bereich"]').waitFor({ timeout: 15_000 }).then(() => true, () => false)
  darfUeberspringen(!da, `${pfad}: die Seite hat nicht geladen — der Demobestand legt diese Steuerung an.`)
}

test('Kühler: fehlende Bauteile stehen da, die Vorschau der Automationen öffnet zweimal', async ({ page }) => {
  await seiteLaden(page, '/steuerung/chiller')

  // Im Demobestand fehlen die Helfer in Home Assistant — wie auf der CO₂-Seite.
  const hinweis = page.locator('.v1-alert', { hasText: 'Noch nicht vollständig eingerichtet' })
  await expect(hinweis).toBeVisible()
  await expect(hinweis).toContainText(/\d+ von \d+ Bauteilen fehlen/)
  await expect(hinweis).toContainText('Ohne Wächter läuft der Kühler weiter')

  const abschnitt = page.locator('.v1-section', { has: page.getByText('Was in Home Assistant fehlt', { exact: true }) })
  await expect(abschnitt).toBeVisible()
  await expect(abschnitt.getByRole('button', { name: 'Fehlende anlegen' })).toBeVisible()
  await expect(abschnitt).toContainText('Sie schalten den Kühler.')

  // Die Vorschau schreibt nichts — zweimal öffnen, beim zweiten Mal weggerollt.
  for (const durchgang of [1, 2]) {
    if (durchgang === 2) await page.evaluate(() => window.scrollTo(0, 0))
    const zeigen = abschnitt.getByRole('button', { name: 'Zeigen, was angelegt würde' })
    await zeigen.click()
    await expect(abschnitt.getByRole('button', { name: 'Jetzt anlegen' }), `Durchgang ${durchgang}`).toBeVisible()
    await expect(abschnitt).toContainText('Water Chiller Wächter')
    await abschnitt.getByRole('button', { name: 'Abbrechen' }).click()
    await expect(zeigen).toBeVisible()
  }
})

test('Kühler: die Lücke „Kühler ohne Aus" steht da — auch bei vollständigem Bestand', async ({ page }) => {
  // Die Lücke hängt an der Art des Geräts, nicht an einem fehlenden Bauteil.
  // Nachgestellt wird deshalb ein vollständiger Bestand mit genau dieser Lücke.
  const luecke = 'Der Wächter kann den Kühler nicht abschalten: number.kuehler_soll ist nur ein Sollwert-Eingang '
    + 'und es ist keine Steckdose zugeordnet.'
  await page.route('**/api/steuerung/chiller/bestand', (route) => route.fulfill({
    contentType: 'application/json',
    body: JSON.stringify({
      haErreichbar: true, eingerichtet: true, da: 9, fehlt: 0, entfaellt: 2, veraltet: 0,
      fehlendeRollen: [], ausgefalleneFunktionen: [luecke], bauteile: [],
    }),
  }))
  await seiteLaden(page, '/steuerung/chiller')

  const hinweis = page.locator('.v1-alert', { hasText: 'Eingerichtet — mit einer Lücke' })
  await expect(hinweis).toBeVisible()
  await expect(hinweis).toContainText(luecke)
  // Nichts fehlt — also auch kein Abschnitt zum Anlegen.
  await expect(page.getByText('Was in Home Assistant fehlt', { exact: true })).toHaveCount(0)
})

test('CO₂: derselbe Bestand steht weiter da, mit der Probeschaltung', async ({ page }) => {
  await seiteLaden(page, '/steuerung/co2')

  await expect(page.locator('.v1-alert', { hasText: 'Noch nicht vollständig eingerichtet' })).toBeVisible()
  const abschnitt = page.locator('.v1-section', { has: page.getByText('Was in Home Assistant fehlt', { exact: true }) })
  await expect(abschnitt.getByRole('button', { name: 'Fehlende anlegen' })).toBeVisible()
  await expect(abschnitt).toContainText('Sie schalten das Ventil.')
  await expect(abschnitt.getByRole('button', { name: 'Ventil kurz öffnen' })).toBeVisible()

  // Die Vorschau nennt die Automationen beim Namen aus der Vorlage, nicht beim
  // Dateinamen („waechter") — der stand vorher roh da.
  await abschnitt.getByRole('button', { name: 'Zeigen, was angelegt würde' }).click()
  await expect(abschnitt).toContainText('CO2 Wächter')
  await expect(abschnitt.locator('.st-etikett', { hasText: /^waechter/ })).toHaveCount(0)
})
