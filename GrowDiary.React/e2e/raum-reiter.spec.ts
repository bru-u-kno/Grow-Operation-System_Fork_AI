import { test, expect } from '@playwright/test'
import { backendAntwortet, darfUeberspringen } from './pflicht'
import { gibSchloss, nimmSchloss } from './schloss'

/**
 * forkai.241: Der Raum als eigene Einheit — Reiter „Räume" (Name, Fühler, Zelte), das Feld „Aufstellraum" am Zelt und der
 * Verweis in den Rollen der Zuluft. Läuft gegen die echte Demo-App; ohne Backend überspringt sich die Datei.
 */
// Beide Fälle ändern den einen Raumnamen der geteilten App — nacheinander, nicht nebeneinander.
test.describe.configure({ mode: 'serial' })

test.beforeEach(async ({ request }) => {
  await nimmSchloss()
  darfUeberspringen(!(await backendAntwortet(request)), 'Kein Backend unter GROW_OS_URL — der Raum braucht die laufende App.')
})

test.afterEach(() => { gibSchloss() })

test('Räume: Name speichern, Fühler-Zeilen da, Rollen-Reiter führt den Raum nicht als Steuerung', async ({ page, request }) => {
  await page.goto('/geraete?reiter=raeume')
  await expect(page.getByRole('heading', { name: 'Name des Raums' }).first()).toBeVisible()
  await expect(page.getByText('Raum-Fühler · Temperatur')).toBeVisible()
  await expect(page.getByText('Raum-Fühler · Feuchte')).toBeVisible()
  // Keine Chip-Leiste der Steuerungen im Raum-Reiter.
  await expect(page.getByRole('tablist', { name: 'Steuerung' })).toHaveCount(0)

  const eingabe = page.getByLabel('Name des Raums')
  await eingabe.fill('Testkeller')
  await page.getByRole('button', { name: 'Namen speichern' }).click()
  await expect(page.getByText('Gespeichert.').first()).toBeVisible()
  expect((await (await request.get('/api/steuerung/raum')).json()).name).toBe('Testkeller')

  // Leeren heißt Vorgabe.
  await eingabe.fill('')
  await page.getByRole('button', { name: 'Namen speichern' }).click()
  await expect.poll(async () => (await (await request.get('/api/steuerung/raum')).json()).name).toBe('Aufstellraum')

  // Rollen-Reiter: der Raum ist keine Steuerung (kein Chip), die Zuluft verweist auf den Reiter Räume.
  await page.goto('/geraete?reiter=rollen&modul=zuluft')
  const leiste = page.getByRole('tablist', { name: 'Steuerung' })
  await expect(leiste).toBeVisible()
  await expect(leiste.getByRole('tab', { name: /^Raum$/ })).toHaveCount(0)
  await expect(page.getByText('Raum-Fühler (Temperatur, Feuchte der Raumluft)')).toBeVisible()
  await page.getByRole('link', { name: /Reiter „Räume“/ }).click()
  await expect(page).toHaveURL(/reiter=raeume/)
})

test('Zelt: Aufstellraum wählen, speichern, kommt wieder und steht im Raum', async ({ page, request }) => {
  await request.put('/api/steuerung/raum', { data: { name: 'Testkeller' } })
  const zelte = (await (await request.get('/api/settings/tents')).json()) as Array<{ id: number; name: string; raumId: string | null }>
  const ziel = zelte[0]
  try {
    await page.goto('/zelte')
    await page.locator('.tn-item', { hasText: ziel.name }).first().click()
    await page.getByRole('button', { name: 'Bearbeiten' }).first().click()
    const wahl = page.getByLabel('Aufstellraum')
    await expect(wahl.locator('option')).toHaveText(['Kein eigener Raum', 'Testkeller'])
    await wahl.selectOption('haupt')
    await page.getByRole('button', { name: /Speichern/ }).first().click()
    await expect.poll(async () => ((await (await request.get(`/api/settings/tents/${ziel.id}`)).json()).raumId)).toBe('haupt')
    const raum = await (await request.get('/api/steuerung/raum')).json()
    expect(raum.zelte.map((z: { id: number }) => z.id)).toContain(ziel.id)
  } finally {
    const aktuell = await (await request.get(`/api/settings/tents/${ziel.id}`)).json()
    await request.put(`/api/settings/tents/${ziel.id}`, { data: { ...aktuell, raumId: ziel.raumId } })
    await request.put('/api/steuerung/raum', { data: { name: null } })
  }
})
