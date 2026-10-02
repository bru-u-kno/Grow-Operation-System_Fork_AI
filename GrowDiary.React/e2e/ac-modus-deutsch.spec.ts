import { test, expect } from '@playwright/test'
import { backendAntwortet, darfUeberspringen } from './pflicht'

/**
 * „Zelt (AC-Test)": der Modus eines AC-Infinity-Geräts steht auf Deutsch da.
 *
 * <b>Der Anlass (02.10.2026).</b> Neben „Stufe 5" stand roh „On" — der Wert, den
 * die Home-Assistant-Integration im `select.*_aktiver_modus` meldet. Die Zählung in
 * `rohe-enums.spec.ts` sieht das nicht: ihre Grundmenge sind die Typen der App,
 * dieser Wert kommt aus Home Assistant. Der Demobestand meldet seit demselben Tag
 * „Schedule" (`DemoData.LichtModus`) — passend zu seinem 12/12-Lichtplan.
 */
test('AC-Test: der Modus steht übersetzt neben der Stufe', async ({ page, request }) => {
  darfUeberspringen(!(await backendAntwortet(request)), 'Kein Backend — ohne Gerät gibt es keinen Modus.')
  const stand = await (await request.get('/api/ac-test/1')).json() as { geraete: Array<{ modus: string | null }> }
  const mitModus = stand.geraete.filter((g) => g.modus)
  expect(mitModus.length, 'Der Demobestand sollte ein Gerät mit Modus haben.').toBeGreaterThanOrEqual(1)

  await page.goto('/ac-test', { waitUntil: 'networkidle' })
  const kopf = page.locator('.ac-kopf').first()
  await expect(kopf).toBeVisible()
  await expect(kopf).toContainText(/Modus Zeitplan\b/)
  await expect(kopf, 'Der Modus steht roh auf Englisch da.').not.toContainText(/\bSchedule\b/)
})
