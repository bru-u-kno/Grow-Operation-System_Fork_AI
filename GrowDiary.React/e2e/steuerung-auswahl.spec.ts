import { test, expect, type Page } from '@playwright/test'
import { backendAntwortet, darfUeberspringen } from './pflicht'
import { gibSchloss, nimmSchloss } from './schloss'

/**
 * A-016 (Etappe 2): Welche Steuerungen der Nutzer hat — gegen die echte Demo-App.
 *
 * Die Demo hat alle fünf Steuerungen gewählt. Die Prüfung blendet welche aus, sieht nach, was die Übersicht
 * daraus macht, richtet eine wieder ein — und stellt am Ende den Anfangszustand her (die Demo-App wird von
 * anderen Prüfungen mitbenutzt). Die Handlung läuft ZWEIMAL, mit verschiedenen Steuerungen und ohne Neuladen
 * dazwischen: fast jede Zustandsverwaltung hat einen Fall „schon offen", den der erste Durchgang nicht berührt.
 */

test.describe.configure({ mode: 'serial' })

// Die Auswahl gehört der ganzen App: Prüfungen, die die Übersicht lesen, dürfen sie nicht mittendrin sehen.
test.beforeEach(async ({ request }) => {
  // Im Rauchtest ohne Backend gibt es keine Auswahl, die sich setzen ließe — dort überspringt sich die Datei.
  darfUeberspringen(!(await backendAntwortet(request)), 'Kein Backend unter GROW_OS_URL — die Auswahl braucht die laufende App mit Demobestand.')
  await nimmSchloss()
})
test.afterEach(() => { gibSchloss() })

async function auswahlSetzen(page: Page, gewaehlt: string[]) {
  const antwort = await page.request.put('/api/steuerung/auswahl', { data: { gewaehlt } })
  expect(antwort.ok(), await antwort.text()).toBeTruthy()
}

const ALLE = ['co2', 'entfeuchter', 'zuluft', 'chiller', 'licht']

test.afterAll(async ({ browser, baseURL }) => {
  const page = await browser.newPage({ baseURL })
  // Ohne Backend (Rauchtest) war nichts zu ändern — und nichts wiederherzustellen.
  if (await backendAntwortet(page.request)) await auswahlSetzen(page, ALLE)
  await page.close()
})

test('Übersicht: nur gewählte Steuerungen, der Rest steht als „Nicht eingerichtet" daneben', async ({ page }) => {
  await auswahlSetzen(page, ALLE)
  await page.goto('/steuerung')
  await expect(page.locator('.st-zeile').filter({ hasText: 'Zuluft' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Nicht eingerichtet' })).toHaveCount(0)

  await auswahlSetzen(page, ['co2', 'licht'])
  await page.goto('/steuerung')

  const gewaehlt = page.locator('button.st-zeile')
  await expect(gewaehlt.filter({ hasText: 'CO₂' }).first()).toBeVisible()
  await expect(gewaehlt.filter({ hasText: 'Licht' }).first()).toBeVisible()
  // Die Abluft-Zeile gehört zur CO₂-Begasung und bleibt.
  await expect(gewaehlt.filter({ hasText: 'Abluft' })).toHaveCount(1)
  for (const weg of ['Zuluft', 'Water Chiller', 'Entfeuchtung']) {
    await expect(gewaehlt.filter({ hasText: weg })).toHaveCount(0)
  }

  const leer = page.locator('.st-zeile-leer')
  await expect(page.getByRole('heading', { name: 'Nicht eingerichtet' })).toBeVisible()
  await expect(leer).toHaveCount(3)
  await expect(leer.filter({ hasText: 'Zuluft' })).toContainText('Außenluft ansaugen')
  await expect(leer.filter({ hasText: 'Wasserkühler' })).toContainText('Nährwasser')
  await expect(leer.filter({ hasText: 'Entfeuchter' })).toBeVisible()
})

test('„Einrichten" wählt die Steuerung und öffnet ihre Seite — zweimal, mit verschiedenen', async ({ page }) => {
  await auswahlSetzen(page, ['co2'])
  await page.goto('/steuerung')

  // Erster Durchgang: Zuluft.
  await page.locator('.st-zeile-leer').filter({ hasText: 'Zuluft' }).getByRole('button', { name: 'Einrichten' }).click()
  await expect(page).toHaveURL(/\/steuerung\/zuluft$/)

  // Zurück zur Übersicht: Zuluft ist jetzt gewählt, der Rest bleibt „nicht eingerichtet".
  await page.goto('/steuerung')
  await expect(page.locator('button.st-zeile').filter({ hasText: 'Zuluft' })).toBeVisible()
  await expect(page.locator('.st-zeile-leer').filter({ hasText: 'Zuluft' })).toHaveCount(0)

  // Zweiter Durchgang: Entfeuchter — führt auf die eine Seite „Entfeuchtung".
  await page.locator('.st-zeile-leer').filter({ hasText: 'Entfeuchter' }).getByRole('button', { name: 'Einrichten' }).click()
  await expect(page).toHaveURL(/\/steuerung\/entfeuchtung$/)

  const stand = await (await page.request.get('/api/steuerung/auswahl')).json()
  expect(stand.gespeichert).toBe(true)
  expect(stand.eintraege.filter((e: { gewaehlt: boolean }) => e.gewaehlt).map((e: { kennung: string }) => e.kennung))
    .toEqual(['co2', 'entfeuchter', 'zuluft'])
})

test('„Meine Steuerungen wählen": ausblenden und wieder einblenden, ohne Neuladen', async ({ page }) => {
  await auswahlSetzen(page, ALLE)
  await page.goto('/steuerung')
  await page.getByRole('button', { name: 'Meine Steuerungen wählen' }).click()
  await expect(page.getByText('Home Assistant läuft weiter', { exact: false })).toBeVisible()

  const zuluft = page.locator('label.v1-switch').filter({ hasText: 'Zuluft' })
  const chiller = page.locator('label.v1-switch').filter({ hasText: 'Wasserkühler' })
  for (const schalter of [zuluft, chiller]) {
    await expect(schalter.locator('input')).toBeChecked()
    await schalter.locator('input').uncheck()
    await expect(schalter.locator('input')).not.toBeChecked()
  }
  await expect(page.locator('button.st-zeile').filter({ hasText: 'Zuluft' })).toHaveCount(0)
  await expect(page.locator('.st-zeile-leer')).toHaveCount(2)

  await zuluft.locator('input').check()
  await expect(zuluft.locator('input')).toBeChecked()
  await expect(page.locator('button.st-zeile').filter({ hasText: 'Zuluft' })).toBeVisible()
  await expect(page.locator('.st-zeile-leer')).toHaveCount(1)
})

test('Nichts gewählt: die Übersicht sagt es und bietet alle fünf an', async ({ page }) => {
  await auswahlSetzen(page, [])
  await page.goto('/steuerung')
  await expect(page.getByText('Noch keine Steuerung eingerichtet')).toBeVisible()
  await expect(page.locator('button.st-zeile')).toHaveCount(0)
  await expect(page.locator('.st-zeile-leer')).toHaveCount(5)
})

test('Unbekannte Steuerung wird abgelehnt und ändert nichts', async ({ page }) => {
  await auswahlSetzen(page, ['licht'])
  const antwort = await page.request.put('/api/steuerung/auswahl', { data: { gewaehlt: ['licht', 'kaffeemaschine'] } })
  expect(antwort.status()).toBe(400)
  const stand = await (await page.request.get('/api/steuerung/auswahl')).json()
  expect(stand.eintraege.filter((e: { gewaehlt: boolean }) => e.gewaehlt).map((e: { kennung: string }) => e.kennung)).toEqual(['licht'])
})
