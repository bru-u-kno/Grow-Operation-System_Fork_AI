import { test, expect } from '@playwright/test'
import { darfUeberspringen } from './pflicht'

/**
 * Der Demobestand hat einen Steckling, dessen Bewurzelung länger dauert als die
 * Anzucht des Programms — der Fall „Bewurzelung N".
 *
 * **Der Anlass (offene Punkte 03.10.2026, D2).** Alle Grows des Bestands waren
 * Samen-Grows. Eine angehängte Woche hieß deshalb immer „Anzucht 2"; dass sie
 * beim Steckling „Bewurzelung 2" heißt (Entscheidung des Nutzers, 02.10.2026),
 * war nur per Unit-Test belegt. `verlaengerte-woche.spec.ts` prüft den
 * Haupt-Grow — der ist ein Samen-Grow.
 */
test('Demobestand: ein Steckling mit angehängter Woche „Bewurzelung 2"', async ({ page }) => {
  const antwort = await page.request.get('/api/grows?archived=false').catch(() => null)
  darfUeberspringen(!antwort?.ok(), 'Kein Backend erreichbar — laeuft die App unter GROW_OS_URL?')
  const grows = (await antwort!.json()) as Array<{ id: number; status: string }>

  const stecklinge: number[] = []
  for (const g of grows.filter((x) => x.status === 'Running')) {
    const grow = (await (await page.request.get(`/api/grows/${g.id}`)).json()) as { startMaterial: string }
    if (grow.startMaterial !== 'Clone') continue
    const werte = (await (await page.request.get(`/api/wochenplan/werte/${g.id}`)).json()) as { spalten: Array<{ label: string }> }
    const namen = werte.spalten.map((s) => s.label)
    expect(namen.filter((n) => n.startsWith('Anzucht')), `Grow ${g.id} ist ein Steckling — seine Anzucht heißt „Bewurzelung".`).toEqual([])
    if (namen.includes('Bewurzelung 2')) stecklinge.push(g.id)
  }

  // Mengenwächter: ohne einen solchen Steckling prüft der Fall nichts.
  expect(stecklinge, 'Kein laufender Steckling mit angehängter Woche „Bewurzelung 2" im Bestand.').not.toEqual([])

  // Und am Grow selbst, in der Plan-Tabelle.
  await page.goto(`/grows/${stecklinge[0]}`, { waitUntil: 'networkidle' })
  await expect(page.locator('[data-audit="plan-auswertung"]')).toContainText('Bewurzelung 2')
})
