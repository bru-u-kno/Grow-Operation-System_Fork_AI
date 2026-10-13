import { test, expect, type APIRequestContext, type Page } from '@playwright/test'
import { backendAntwortet, darfUeberspringen } from './pflicht'
import { ZIELWERTE } from './entfeuchtung-vorgaben'

/**
 * A-014 / A-021: Farbzonen und gesperrte Felder auf der Seite „Entfeuchtung".
 *
 * Die Antwort des laufenden Backends wird nur an den Messwerten verändert
 * (`route.fetch()` + eigene Zahlen) — so bleibt die Form echt und die Zone
 * eindeutig. Gesucht ist, was der Nutzer sieht: die Nadel im Band trägt die Farbe
 * ihrer Zone, das Urteil steht in Worten da, und Felder ohne Wirkung sind gesperrt.
 *
 * <b>Zweiseitig seit A-021.</b> Das Ziel hat eine Unter- und eine Obergrenze: zu trocken und zu kühl zählen
 * genauso als Abweichung wie zu feucht und zu warm. Die Grenzen kommen aus `GET /api/zielwerte`; die Prüfung
 * gibt diese Antwort vor (`ZIELWERTE`: Luftfeuchte 45 – 54, Temperatur nachts 14 – 25), damit die Zonen eindeutig sind —
 * der Demobestand hat dort keine Alarmregeln.
 */

async function seiteMitWerten(page: Page, request: APIRequestContext, live: Record<string, unknown>): Promise<void> {
  // Im Rauchtest ohne Backend gibt es nichts, was sich verändern ließe.
  darfUeberspringen(!(await backendAntwortet(request)), 'Kein Backend unter GROW_OS_URL — die Zonen brauchen die laufende App mit Demobestand.')
  await page.route(/\/api\/zielwerte$/, (route) => route.fulfill({ json: ZIELWERTE }))
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

/** Dieselbe Seite mit anderen Messwerten, ohne die Seite zu verlassen (die Antwort wird beim Neuladen neu gebildet). */
async function werteSetzen(page: Page, live: Record<string, unknown>): Promise<void> {
  await page.unroute(/\/api\/steuerung\/entfeuchter$/)
  await page.route(/\/api\/steuerung\/entfeuchter$/, async (route) => {
    if (route.request().method() !== 'GET') return route.continue()
    const antwort = await route.fetch()
    const json = await antwort.json() as { live: Record<string, unknown> }
    json.live = { ...json.live, haErreichbar: true, ...WERTE, ...live }
    return route.fulfill({ response: antwort, json })
  })
  await page.reload({ waitUntil: 'networkidle' })
}

const block = (page: Page, nr: 0 | 1) => page.locator('.ef-band .ef-block').nth(nr)
const nadel = (page: Page, nr: 0 | 1) => block(page, nr).locator('.gos-metric-band .nadel')

test('Entfeuchtung: die Nadel trägt die Farbe ihrer Zone — und die Zone steht auch als Wort da', async ({ page, request }) => {
  // 07.10.2026: 57,9 % bei Ziel 51 % ist „deutlich daneben", 22,7 °C bei 25 °C Höchsttemperatur im Ziel.
  await seiteMitWerten(page, request, { ...WERTE, feuchteProzent: 57.9, tempC: 22.7 })
  await expect(page.locator('.gos-metric-band .nadel')).toHaveCount(2)
  await expect(nadel(page, 0)).toHaveClass(/\bist-crit\b/)
  await expect(nadel(page, 1)).toHaveClass(/\bist-ok\b/)
  await expect(block(page, 0).locator('.ef-zustand')).toContainText('deutlich über dem Ziel')
  await expect(block(page, 1).locator('.ef-zustand')).toContainText('im Ziel')
  await expect(page.locator('.v1-alert').filter({ hasText: /Im Ziel|daneben/ }).first()).toContainText('Deutlich daneben')

  // Zweiter Durchgang, ohne die Seite zu verlassen: im Ziel, dann knapp.
  for (const [rf, klasse, wort] of [[49, 'ist-ok', 'im Ziel'], [53, 'ist-warn', 'knapp über dem Ziel']] as const) {
    await werteSetzen(page, { feuchteProzent: rf, tempC: 22.7 })
    await expect(nadel(page, 0)).toHaveClass(new RegExp(`\\b${klasse}\\b`))
    await expect(block(page, 0).locator('.ef-zustand')).toContainText(wort)
  }
})

test('Entfeuchtung: zu trocken und zu kühl zählen genauso als Abweichung wie zu feucht und zu warm', async ({ page, request }) => {
  // Ziel Luftfeuchte 45 – 51 (Obergrenze aus dem Plan, Untergrenze aus dem Grenzwert), nachts Temperatur 14 – 25.
  // Knapp: bis 4 Prozentpunkte außerhalb der Luftfeuchte, bis 2 K unter der Temperatur-Untergrenze.
  await seiteMitWerten(page, request, { ...WERTE, feuchteProzent: 38, tempC: 22.7 })
  await expect(nadel(page, 0)).toHaveClass(/\bist-crit\b/)
  await expect(block(page, 0).locator('.ef-zustand')).toContainText('deutlich unter dem Ziel')
  await expect(block(page, 0).locator('.gos-metric-ziel')).toContainText('45 – 51')
  await expect(page.locator('.v1-alert').filter({ hasText: /Im Ziel|daneben/ }).first()).toContainText('Deutlich daneben')

  const faelle = [
    { live: { feuchteProzent: 43, tempC: 22.7 }, block: 0 as const, klasse: 'ist-warn', wort: 'knapp unter dem Ziel' },
    { live: { feuchteProzent: 48, tempC: 22.7 }, block: 0 as const, klasse: 'ist-ok', wort: 'im Ziel' },
    { live: { feuchteProzent: 48, tempC: 13 }, block: 1 as const, klasse: 'ist-warn', wort: 'knapp unter dem Plan' },
    { live: { feuchteProzent: 48, tempC: 10 }, block: 1 as const, klasse: 'ist-crit', wort: 'deutlich unter dem Plan' },
    { live: { feuchteProzent: 48, tempC: 20 }, block: 1 as const, klasse: 'ist-ok', wort: 'im Ziel' },
  ]
  for (const fall of faelle) {
    await werteSetzen(page, fall.live)
    await expect(nadel(page, fall.block), JSON.stringify(fall.live)).toHaveClass(new RegExp(`\\b${fall.klasse}\\b`))
    await expect(block(page, fall.block).locator('.ef-zustand'), JSON.stringify(fall.live)).toContainText(fall.wort)
  }
})

test('Entfeuchtung: bei Regelgröße VPD sind die festen Schwellen gesperrt, bei Luftfeuchte bearbeitbar', async ({ page, request }) => {
  await seiteMitWerten(page, request, { ...WERTE, feuchteProzent: 49, tempC: 22.7 })
  await page.locator('.v1-tab', { hasText: /^Einstellungen$/ }).click()
  const kopf = page.locator('.st-kk-kopf', { hasText: 'Erweitert' })
  if ((await kopf.getAttribute('aria-expanded')) === 'false') await kopf.click()
  // „Nach VPD regeln …" steht unter „Erweitert"; sein Name trägt den Hinweistext mit, deshalb über die Beschriftung suchen.
  const vpd = page.locator('label.v1-switch').filter({ has: page.locator('strong', { hasText: 'Nach VPD regeln statt nach Luftfeuchte' }) }).locator('input')
  // Die Schwelle als EIN Feld oder als zwei (Tag / Nacht), je nach Bestand der Demo-App: beide Fälle zählen.
  const schwellen = page.getByLabel(/^Einschalten über/)
  const ziel = page.locator('.st-kk', { hasText: 'Ziel · Luftfeuchte' })

  await vpd.check()
  expect(await schwellen.count(), 'die Schwelle „Einschalten über" ist da').toBeGreaterThan(0)
  for (const feld of await schwellen.all()) await expect(feld).toBeDisabled()
  await expect(ziel).toContainText('Gerade wird nach VPD geregelt')

  // Zweiter Durchgang: auf Luftfeuchte stellen, bearbeiten, wieder auf VPD.
  await vpd.uncheck()
  for (const feld of await schwellen.all()) await expect(feld).toBeEnabled()
  await expect(ziel).toContainText('Geregelt wird nach der Luftfeuchte')
  await expect(ziel).not.toContainText('Gerade wird nach VPD geregelt')
  await schwellen.first().fill('55')
  await expect(schwellen.first()).toHaveValue('55')
  await vpd.check()
  for (const feld of await schwellen.all()) await expect(feld).toBeDisabled()
  // Der Abstand EIN → AUS und die Stufe gelten in beiden Regelgrößen und bleiben bedienbar.
  await expect(page.getByLabel('Abstand EIN → AUS', { exact: true })).toBeEnabled()
  await expect(page.getByRole('radiogroup', { name: 'Wie reagieren die Entfeuchter?' }).getByRole('radio', { name: 'Normal', exact: true })).toBeEnabled()
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

  // Auch die Kacheln der Einstellungen: jede zu, jede wieder auf — zweimal.
  await page.locator('.v1-tab', { hasText: /^Einstellungen$/ }).click()
  const koepfe = page.locator('.st-kk-kopf')
  const anzahl = await koepfe.count()
  expect(anzahl, 'die Einstellungen haben mehrere Kacheln').toBeGreaterThanOrEqual(6)
  for (let runde = 0; runde < 2; runde++) {
    for (let i = 0; i < anzahl; i++) {
      const k = koepfe.nth(i)
      const vorher = await k.getAttribute('aria-expanded')
      await k.click()
      await expect(k, `Kachel ${i + 1}, Runde ${runde + 1}`).toHaveAttribute('aria-expanded', vorher === 'true' ? 'false' : 'true')
      await k.click()
      await expect(k).toHaveAttribute('aria-expanded', vorher!)
    }
  }
})
