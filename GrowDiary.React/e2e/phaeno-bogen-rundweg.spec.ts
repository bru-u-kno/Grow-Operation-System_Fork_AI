import { expect, test, type Page } from '@playwright/test'
import { backendAntwortet, darfUeberspringen } from './pflicht'

/**
 * Der Phäno-Bogen: ein Komma tippen, speichern, neu laden, wiederfinden.
 *
 * <b>Der Anlass (02.10.2026).</b> Jedes Zahlenfeld des Bogens hing direkt an
 * der Zahl, gelesen mit `parseFloat`. Aus „22," wurde beim Tippen 22, das Komma
 * verschwand, und die nächste Ziffer machte 225 daraus — THC 22,5 % liess sich
 * nicht eintragen. Derselbe Fehler war auf der Ernte-Seite am 01.09.2026
 * behoben worden. Und `parseFloat('6,2x')` ist 6,2: der Rest verschwand ohne
 * Meldung.
 *
 * Getippt wird Taste für Taste (`pressSequentially`), nicht mit `fill` — der
 * Fehler steckte in der Zwischenform „22,", die `fill` nie erzeugt.
 *
 * <b>Diese Datei schreibt in die Datenbank</b> (den Bogen der ersten Pflanze im
 * Demobestand). Deshalb seriell.
 */

test.describe.configure({ mode: 'serial' })

function feld(seite: Page, beschriftung: RegExp) {
  return seite.locator('label.v1-field').filter({ hasText: beschriftung }).locator('input').last()
}

async function bogenOeffnen(seite: Page): Promise<void> {
  await seite.goto('/sorten', { waitUntil: 'networkidle' })
  const knopf = seite.locator('.co-cand button').first()
  await expect(knopf, 'Der Demobestand sollte eine Phäno-Pflanze haben.').toBeVisible()
  await knopf.click()
  await expect(seite.getByRole('button', { name: 'Bogen speichern' })).toBeVisible()
}

async function speichern(seite: Page): Promise<Record<string, unknown>> {
  const antwort = seite.waitForResponse((r) => r.request().method() === 'PUT' && /\/api\/pheno\/plants\/\d+$/.test(r.url()))
  await seite.getByRole('button', { name: 'Bogen speichern' }).click()
  const fertig = await antwort
  expect(fertig.ok(), `PUT ${fertig.url()} kam mit HTTP ${fertig.status()} zurück.`).toBe(true)
  return JSON.parse(fertig.request().postData() ?? '{}') as Record<string, unknown>
}

test('Phäno-Bogen: Dezimalkomma und Tausenderpunkt überstehen Tippen, Speichern und Neuladen', async ({ page }) => {
  darfUeberspringen(!(await backendAntwortet(page.request)), 'Kein Backend erreichbar.')

  await bogenOeffnen(page)
  const thc = feld(page, /^THC \(%\)/)
  await thc.fill('')
  await thc.pressSequentially('22,5')
  expect(await thc.inputValue(), 'Das Komma ist beim Tippen verschwunden.').toBe('22,5')
  expect((await speichern(page)).thcPercent, 'Aus getippten „22,5" wurde nicht 22,5.').toBe(22.5)

  await bogenOeffnen(page)
  expect(await feld(page, /^THC \(%\)/).inputValue(), 'Nach dem Neuladen steht ein anderer Wert im Feld.').toBe('22,5')

  // Zweiter Durchgang, ohne Neuladen: ein Tippfehler wird gemeldet statt still
  // verkürzt, danach ein Tausenderpunkt.
  const trocken = feld(page, /^Ertrag trocken/)
  await trocken.fill('')
  await trocken.pressSequentially('6,2x')
  await page.getByRole('button', { name: 'Bogen speichern' }).click()
  await expect(page.locator('.v1-alert').filter({ hasText: 'Ertrag trocken (g)' }),
    'Ein unlesbares Feld wurde nicht gemeldet.').toBeVisible()

  await trocken.fill('')
  await trocken.pressSequentially('1.200')
  const rumpf = await speichern(page)
  expect(rumpf.dryYieldG, '„1.200" ist deutsch tausendzweihundert.').toBe(1200)
  expect(rumpf.thcPercent).toBe(22.5)
})
