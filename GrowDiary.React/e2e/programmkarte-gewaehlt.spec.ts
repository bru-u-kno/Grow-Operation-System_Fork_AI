import { test, expect } from '@playwright/test'
import { darfUeberspringen } from './pflicht'

/**
 * Beim Bearbeiten eines Grows steht seine Programmkarte als gewählt da — auch
 * wenn am Grow kein Programm-NAME steht, nur die Programm-ID.
 *
 * <b>Der Anlass (offene Punkte 03.10.2026, B11).</b> Die Karte verglich den
 * Namen aus `nutrients`. Beide Grows im Demobestand laufen nach
 * `skx-canna-aqua`, tragen aber keinen Namen — beim Bearbeiten war keine Karte
 * markiert, als hätte der Grow gar kein Programm.
 *
 * Nur lesend: der Fall öffnet das Formular und speichert nichts.
 */
test('Programmkarte: die Programm-ID des Grows markiert ihre Karte', async ({ page }) => {
  const antwort = await page.request.get('/api/grows?archived=false').catch(() => null)
  const grows = antwort?.ok() ? (await antwort.json()) as Array<{ id: number }> : []
  const details = await Promise.all(grows.map(async (g) =>
    (await (await page.request.get(`/api/grows/${g.id}`)).json()) as { id: number; nutrients: string | null; feedProgramId: string | null }))
  const fall = details.find((g) => g.feedProgramId && !g.nutrients)
  darfUeberspringen(fall == null,
    'Kein Grow mit Programm-ID, aber ohne Programm-Namen im Bestand — genau der Fall, den der Demobestand zeigen sollte.')

  const programme = (await (await page.request.get('/api/knowledge')).json()) as { programs: Array<{ key: string; name: string }> }
  const name = programme.programs.find((p) => p.key === fall!.feedProgramId)?.name
  expect(name, `Das Programm ${fall!.feedProgramId} steht nicht in der Wissensbasis.`).toBeTruthy()

  await page.goto(`/grows/${fall!.id}/setup`, { waitUntil: 'networkidle' })

  const gewaehlt = page.locator('.program-card.active')
  await expect(gewaehlt, `Beim Bearbeiten ist keine Programmkarte gewählt, obwohl der Grow nach „${name}" läuft.`)
    .toHaveCount(1)
  await expect(gewaehlt).toContainText(name!)
})
