import { expect, test } from '@playwright/test'
import { backendAntwortet, darfUeberspringen } from './pflicht'

/**
 * Kosten bei mehreren gleichzeitigen Grows — am laufenden Stand gelesen.
 *
 * <b>Der Anlass (02.10.2026).</b> Ein zweiter laufender Grow bekam nie
 * Stromkosten (jeder Zählerstand trug die Id des ältesten), und das Archiv
 * schätzte seine eigene Zahl aus Lampen-Watt, während die Kostenseite mit
 * Zählerständen rechnete — derselbe Grow, zwei Eurobeträge.
 *
 * Der Demobestand hat dafür zwei laufende Grows am selben Zähler, einen Lauf
 * im Archiv mit Zählerdaten und einen ohne (dort bleibt die Schätzung).
 *
 * Diese Datei schreibt nichts.
 */

type Durchgang = {
  growId: number
  name: string
  laeuft: boolean
  stromEur: number | null
  gesamtEur: number | null
  stromGeteiltTage: number
}

/** Wie die Archiv-Zelle formatiert: ganze Euro, deutsch — mit beliebigem (auch geschütztem) Leerzeichen. */
function ganzeEuro(wert: number): RegExp {
  const zahl = new Intl.NumberFormat('de-DE', { maximumFractionDigits: 0 }).format(wert).replace('.', '\\.')
  return new RegExp(`^${zahl}[\\s\\u00a0]€`)
}

test.describe('Kosten mit mehreren Grows', () => {
  test.beforeEach(async ({ page }) => {
    darfUeberspringen(!(await backendAntwortet(page.request)), 'Kein Backend erreichbar.')
  })

  test('jeder laufende Grow hat seinen Strom, und die Seite sagt, dass geteilt wird', async ({ page }) => {
    const durchgaenge = await (await page.request.get('/api/kosten/durchgaenge')).json() as Durchgang[]
    const laufend = durchgaenge.filter((d) => d.laeuft)
    darfUeberspringen(laufend.length < 2, 'Weniger als zwei laufende Grows — der Demobestand legt zwei an.')

    for (const grow of laufend) {
      await page.goto('/kosten?tab=strom')
      const wahl = page.locator('[data-audit="kosten-grow-wahl"]')
      await expect(wahl).toBeVisible({ timeout: 15000 })
      await wahl.selectOption(String(grow.growId))

      // Der Kopf nennt den gewählten Grow, und sein Strom ist eine Zahl.
      await expect(page.locator('.ko-hero')).toContainText(grow.name)
      const strom = page.locator('[data-audit="kosten-strom"]')
      await expect(strom).not.toContainText('Noch kein Zählerstand')
      expect(grow.stromEur, `${grow.name}: kein Strom in der Durchgänge-Rechnung.`).not.toBeNull()

      // Der Satz zur Teilung nennt den anderen Grow.
      const andere = laufend.find((d) => d.growId !== grow.growId)!
      const teilung = page.locator('[data-audit="kosten-strom-teilung"]')
      await expect(teilung).toContainText(`geteilt mit „${andere.name}“`)
    }
  })

  test('das Archiv zeigt die Zahl der Kostenseite — und schätzt nur ohne Zählerdaten', async ({ page }) => {
    const durchgaenge = await (await page.request.get('/api/kosten/durchgaenge')).json() as Durchgang[]
    const archiv = durchgaenge.filter((d) => !d.laeuft)
    const mitZaehler = archiv.filter((d) => d.stromEur != null && d.gesamtEur != null)
    const ohneZaehler = archiv.filter((d) => d.stromEur == null)
    darfUeberspringen(mitZaehler.length === 0 || ohneZaehler.length === 0,
      'Das Archiv braucht einen Lauf mit und einen ohne Zählerdaten — der Demobestand legt beide an.')

    await page.goto('/archiv')
    const tabelle = page.locator('[data-audit="grows-archive"]')
    await expect(tabelle).toBeVisible({ timeout: 15000 })

    for (const lauf of mitZaehler) {
      const zelle = tabelle.locator('[data-kosten="gemessen"]').filter({ hasText: ganzeEuro(lauf.gesamtEur!) })
      await expect(zelle, `${lauf.name}: das Archiv zeigt nicht ${ganzeEuro(lauf.gesamtEur!)} wie die Kostenseite.`).toHaveCount(1)
    }

    // Wo geschätzt wird, steht es in der Zelle — nicht nur im Titel.
    const geschaetzt = tabelle.locator('[data-kosten="geschaetzt"]')
    await expect(geschaetzt).toHaveCount(ohneZaehler.length)
    await expect(geschaetzt.first()).toContainText('geschätzt')
  })
})
