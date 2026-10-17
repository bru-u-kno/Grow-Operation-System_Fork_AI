import { test, expect } from '@playwright/test'
import { backendAntwortet, darfUeberspringen } from './pflicht'

/**
 * Eine Aufgabe, die ein KI-Assistent angelegt hat, trägt in der Aufgabenliste das Kennzeichen „von KI" —
 * eine von Hand angelegte nicht.
 *
 * Die Antwort der Liste wird um zwei Aufgaben ergänzt (eine mit, eine ohne `createdByAssistant`); geprüft wird, was die Seite daraus macht.
 */
test('Aufgabenliste: „von KI" nur an der Aufgabe, die ein Assistent angelegt hat', async ({ page }) => {
  darfUeberspringen(
    !(await backendAntwortet(page.request)),
    'Kein Backend unter GROW_OS_URL — die Seite zeigt dann nur einen Ladezustand.',
  )

  const faellig = new Date(Date.now() + 24 * 3600 * 1000).toISOString()
  await page.route('**/api/grows/*/tasks', async (route) => {
    if (route.request().method() !== 'GET') return route.continue()
    const antwort = await route.fetch()
    const wirklich = (await antwort.json()) as unknown[]
    const wachs = (id: number, titel: string, vonKi: string | null) => ({
      id, growId: 1, growName: 'Test', title: titel, notes: null, dueAtUtc: faellig, priority: 'Normal', status: 'Open',
      createdAtUtc: faellig, completedAtUtc: null, createdByAssistant: vonKi,
    })
    return route.fulfill({
      response: antwort,
      json: [...wirklich, wachs(990001, 'Kennzeichen-Test von KI', 'Claude Code VM'), wachs(990002, 'Kennzeichen-Test von Hand', null)],
    })
  })

  await page.goto('/aufgaben', { waitUntil: 'networkidle' })

  // Die Demo hat mehrere Grows; die Liste wird für jeden abgefragt, die Aufgabe steht also mehrfach da.
  const vonKi = page.locator('.af-row', { hasText: 'Kennzeichen-Test von KI' })
  const vonHand = page.locator('.af-row', { hasText: 'Kennzeichen-Test von Hand' })
  expect(await vonKi.count()).toBeGreaterThan(0)
  expect(await vonHand.count()).toBeGreaterThan(0)
  for (const zeile of await vonKi.all()) {
    await expect(zeile.locator('.af-ki')).toHaveText(/von KI/i)
    await expect(zeile.locator('.af-ki')).toHaveAttribute('title', 'Angelegt von Claude Code VM')
  }
  for (const zeile of await vonHand.all()) await expect(zeile.locator('.af-ki')).toHaveCount(0)
})
