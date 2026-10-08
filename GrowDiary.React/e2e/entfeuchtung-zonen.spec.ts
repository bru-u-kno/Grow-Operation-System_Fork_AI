import { test, expect, type APIRequestContext, type Page } from '@playwright/test'
import { backendAntwortet, darfUeberspringen } from './pflicht'

/**
 * A-014: Farbzonen und gesperrte Felder auf der Seite „Entfeuchter".
 *
 * Die Antwort des laufenden Backends wird nur an den Messwerten verändert
 * (`route.fetch()` + eigene Zahlen) — so bleibt die Form echt und die Zone
 * eindeutig. Gesucht ist, was der Nutzer sieht: der Messpunkt trägt die Farbe
 * seiner Zone, und Felder ohne Wirkung sind gesperrt.
 */

async function seiteMitWerten(page: Page, request: APIRequestContext, live: Record<string, unknown>): Promise<void> {
  // Im Rauchtest ohne Backend gibt es nichts, was sich verändern ließe.
  darfUeberspringen(!(await backendAntwortet(request)), 'Kein Backend unter GROW_OS_URL — die Zonen brauchen die laufende App mit Demobestand.')
  await page.route(/\/api\/steuerung\/entfeuchter$/, async (route) => {
    if (route.request().method() !== 'GET') return route.continue()
    const antwort = await route.fetch()
    const json = await antwort.json() as { live: Record<string, unknown> }
    json.live = { ...json.live, haErreichbar: true, ...live }
    return route.fulfill({ response: antwort, json })
  })
  const antwort = await page.goto('/steuerung/entfeuchtung', { waitUntil: 'networkidle' })
  darfUeberspringen(antwort == null || antwort.status() >= 400, 'die App antwortet nicht — läuft sie unter GROW_OS_URL?')
  const da = await page.locator('.v1-tab', { hasText: 'Überblick' }).waitFor({ timeout: 15_000 }).then(() => true, () => false)
  darfUeberspringen(!da, 'keine Reiterleiste — der Demobestand legt diese Steuerung an.')
}

const WERTE = { rhObergrenzeProzent: 51, einAktivProzent: 50, ausAktivProzent: 46, portAn: true, tagPhase: false, portOnline: true, automatikAn: true }

test('Entfeuchter: der Messpunkt trägt die Farbe seiner Zone — und die Zone steht auch als Wort da', async ({ page, request }) => {
  // Bru, 07.10.2026: 57,9 % bei Ziel 51 % ist „deutlich daneben", 22,7 °C bei 25 °C Höchsttemperatur im Ziel.
  await seiteMitWerten(page, request, { ...WERTE, feuchteProzent: 57.9, tempC: 22.7 })
  const karte = page.locator('.ef-band')
  const punkte = karte.locator('.ef-ist')
  await expect(punkte).toHaveCount(2)
  await expect(punkte.nth(0)).toHaveClass(/\bis-kritisch\b/)
  await expect(punkte.nth(1)).not.toHaveClass(/\bis-(knapp|kritisch)\b/)
  await expect(page.locator('.v1-alert').filter({ hasText: /Im Ziel|daneben/ }).first()).toContainText('deutlich daneben')

  // Zweiter Durchgang, ohne Neuladen der Seite: im Ziel, dann knapp.
  for (const [rf, klasse] of [[49, null], [53, 'is-knapp']] as const) {
    await page.unroute(/\/api\/steuerung\/entfeuchter$/)
    await page.route(/\/api\/steuerung\/entfeuchter$/, async (route) => {
      if (route.request().method() !== 'GET') return route.continue()
      const antwort = await route.fetch()
      const json = await antwort.json() as { live: Record<string, unknown> }
      json.live = { ...json.live, haErreichbar: true, ...WERTE, feuchteProzent: rf, tempC: 22.7 }
      return route.fulfill({ response: antwort, json })
    })
    await page.reload({ waitUntil: 'networkidle' })
    const erster = page.locator('.ef-band .ef-ist').first()
    if (klasse) await expect(erster).toHaveClass(new RegExp(`\\b${klasse}\\b`))
    else await expect(erster).not.toHaveClass(/\bis-(knapp|kritisch)\b/)
  }
})

test('Entfeuchter: bei „Nach VPD regeln" sind die festen Schwellen gesperrt, ohne den Haken sind sie bearbeitbar', async ({ page, request }) => {
  await seiteMitWerten(page, request, { ...WERTE, feuchteProzent: 49, tempC: 22.7 })
  await page.locator('.v1-tab', { hasText: /^Regel$/ }).click()
  const schalter = page.getByLabel('Nach VPD regeln')
  if (!(await schalter.isChecked())) await schalter.check()
  const kopf = page.locator('.st-kk-kopf', { hasText: 'Feste Schwellen' })
  if ((await kopf.getAttribute('aria-expanded')) === 'false') await kopf.click()
  const feld = page.getByLabel('Tag · EIN ab', { exact: true })
  await expect(feld).toBeDisabled()
  await expect(page.getByText('Gerade ohne Wirkung')).toBeVisible()

  // Zweiter Durchgang: ausschalten, bearbeiten, wieder einschalten.
  await schalter.uncheck()
  await expect(feld).toBeEnabled()
  await expect(page.getByText('Gerade ohne Wirkung')).toHaveCount(0)
  await schalter.check()
  await expect(feld).toBeDisabled()
  // Der Abstand EIN → AUS gilt in beiden Regelarten und bleibt bedienbar.
  await expect(page.getByRole('radio', { name: /^normal/ }).first()).toBeEnabled()
})

test('jede Kachel lässt sich zuklappen und wieder aufklappen — zweimal', async ({ page, request }) => {
  await seiteMitWerten(page, request, { ...WERTE, feuchteProzent: 49, tempC: 22.7 })
  const kopf = page.locator('.st-kk-kopf', { hasText: 'Messwerte & Zonen (Zelt)' })
  for (let runde = 0; runde < 2; runde++) {
    await expect(kopf).toHaveAttribute('aria-expanded', 'true')
    await expect(page.locator('.ef-band')).toBeVisible()
    await kopf.click()
    await expect(kopf).toHaveAttribute('aria-expanded', 'false')
    await expect(page.locator('.ef-band')).toBeHidden()
    // Zugeklappt nennt die Kopfzeile den Wert.
    await expect(kopf).toContainText('49,0 % rF')
    await kopf.click()
  }
})
