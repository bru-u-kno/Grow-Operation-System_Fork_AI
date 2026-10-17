import { test, expect } from '@playwright/test'
import { backendAntwortet, darfUeberspringen } from './pflicht'

/**
 * A-019 / Lücke 5: Dieselbe Vorsortierung (features/geraete/entitaets-passung.ts) steht an allen Stellen, an denen man
 * eine Entität wählt — Rollen der Steuerungen (Geräte-Reiter), Messgrößen (Seite Home Assistant und Reiter Messgrößen).
 * Der Assistent ist in `einrichtung-assistent.spec.ts` geprüft. Hier nur lesen, nichts speichern: kein Schloss nötig.
 */
test.beforeEach(async ({ request }) => {
  darfUeberspringen(!(await backendAntwortet(request)), 'Kein Backend unter GROW_OS_URL — die Auswahl braucht die Entitäten der Demo.')
})

const eintraege = (page: import('@playwright/test').Page) => page.locator('.v1-select-liste [role="option"]')
// Erster Text jeder Zeile (Name), ohne die kleine Zeile darunter.
const namen = (page: import('@playwright/test').Page) =>
  eintraege(page).locator('xpath=./span').evaluateAll((els) => els.map((el) => (el.firstChild?.textContent ?? '').trim()))

test('Geräte · Rollen: die Auswahl ist vorsortiert (gleiche Komponente wie im Assistenten) — zweimal, andere Rolle', async ({ page }) => {
  await page.goto('/geraete?reiter=rollen')
  await page.getByRole('tab', { name: /Entfeuchter/ }).first().click()
  await page.getByRole('button', { name: 'Zeltfühler · Temperatur' }).click()
  let liste = await namen(page)
  expect(liste[0]).toBe('Demo Lufttemperatur')
  // Wassertemperatur und Außentemperatur nennen einen anderen Ort als das Zelt: unter "passt", aber dahinter.
  expect(liste.indexOf('Demo Wassertemperatur')).toBeGreaterThan(0)
  expect(liste.indexOf('Demo Wassertemperatur')).toBeLessThan(liste.indexOf('Demo Kamera'))
  await page.keyboard.press('Escape')

  await page.getByRole('button', { name: 'Zeltfühler · Feuchte' }).click()
  liste = await namen(page)
  expect(liste[0]).toBe('Demo Luftfeuchte')
  await page.keyboard.press('Escape')
})

test('Home Assistant · Messgrößen: Luftfeuchte und CO₂ stehen oben, nichts ist ausgeblendet', async ({ page, request }) => {
  const alle = await (await request.get('/api/home-assistant/entities')).json() as unknown[]
  await page.goto('/home-assistant')
  await page.getByRole('button', { name: 'Luftfeuchte', exact: true }).first().click()
  let liste = await namen(page)
  // Erster Eintrag ist "— nicht zugeordnet —", dann die Entitäten.
  expect(liste[0]).toMatch(/nicht zugeordnet/)
  expect(liste[1]).toBe('Demo Luftfeuchte')
  expect(liste.length).toBe(alle.length + 1)
  await page.keyboard.press('Escape')

  await page.getByRole('button', { name: 'CO₂', exact: true }).first().click()
  liste = await namen(page)
  expect(liste[1]).toBe('Demo CO₂')
  await page.keyboard.press('Escape')
})
