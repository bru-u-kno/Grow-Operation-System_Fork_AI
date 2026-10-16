import { test, expect, type APIRequestContext } from '@playwright/test'
import { darfUeberspringen } from './pflicht'
import { abraeumen, eigenenGrowAnlegen, type EigenerGrow } from './eigener-grow'
import { nimmSchloss, gibSchloss } from './schloss'

/**
 * Flush eine oder zwei Wochen — und der Plan führt die Blütewochen bis dorthin.
 *
 * <b>Der Anlass (16.10.2026).</b> „Blütewoche 8 von 8" bei einer Sorte mit 9–10 Wochen: Das
 * SKX-Programm bringt acht Blütewochen mit, der Flush war fest auf 14 Tage gerechnet, und weitere
 * Wochen entstanden erst am Tag, an dem die Blüte sie erreichte. Der Nutzer wollte den Flush selbst
 * wählen und die Werte neuer Wochen vorher prüfen.
 *
 * <b>Eigener Datensatz, weit in der Vergangenheit.</b> Eigene Sorte und eigener Grow (Start 2020),
 * damit er sich in keiner Liste vor den Demobestand schiebt; am Ende abgeräumt. Weil der Grow lange
 * vorbei ist, prüft diese Datei den Weg (Formular → Server → Plan → Aufgaben → Plan-Seite); dass die
 * Woche schon VOR ihrem Tag entsteht, prüfen die Backend-Tests (<c>FlushDauerUndPlanwochenTests</c>).
 *
 * <b>Der Weg im Browser, zweimal.</b> Speichern, neu laden, nochmal speichern (CLAUDE.md).
 */
test.beforeEach(async () => { await nimmSchloss() })
test.afterEach(() => { gibSchloss() })
test.describe.configure({ mode: 'serial' })
test.setTimeout(180_000)

const MARKE = `E2E Flush ${Date.now()}`
// Flip 01.03.2020: bei 10 Wochen und 14 Tagen Flush beginnt das Finish am 26.04., bei 7 Tagen am 03.05.
const FLIP = '2020-03-01'

async function holeGrow(api: APIRequestContext, id: number) {
  return (await api.get(`/api/grows/${id}`)).json()
}

async function holePlan(api: APIRequestContext, id: number) {
  return (await api.get(`/api/grows/${id}/plan`)).json()
}

test('Flush wählen: Hinweis, Speichern, Plan bekommt Woche 9, Aufgabe, Plan-Seite, geprüft', async ({ page, request }) => {
  darfUeberspringen(!(await request.get('/api/grows')).ok(), 'Kein Backend.')

  let eigener: EigenerGrow | null = null
  let sorteId: number | null = null
  try {
    const angelegt = await request.post('/api/strains', {
      data: { name: MARKE, dominance: 'Hybrid', flowerWeeksMin: 9, flowerWeeksMax: 10 },
    })
    expect(angelegt.ok(), `Sorte anlegen: ${angelegt.status()}`).toBe(true)
    sorteId = (await angelegt.json()).id as number

    eigener = await eigenenGrowAnlegen(request, `${MARKE} Grow`, {
      strainId: sorteId, strain: MARKE, seedType: 'Feminized', startDate: '2020-02-01', flipDate: FLIP,
      entryPoint: 'Germination', feedProgramId: 'skx-canna-aqua',
    })
    darfUeberspringen(eigener == null, 'Eigener Grow ließ sich nicht anlegen (Zelt oder Hydro-System fehlt).')
    const growId = eigener!.growId

    // 1. Vorgabe: zwei Wochen Flush, der Plan führt die acht Blütewochen des Programms — nichts fehlt.
    let grow = await holeGrow(request, growId)
    expect(grow.flushTage).toBe(14)
    expect(grow.phasenanker.finishAb).toContain('2020-04-26')
    let plan = await holePlan(request, growId)
    expect(plan.chart.columns.filter((c: { stage: string, week?: number }) => c.stage === 'Flower' && c.week != null)).toHaveLength(8)
    expect(plan.ungeprueft ?? []).toHaveLength(0)

    // 2. Formular: Auswahl sichtbar, bei zwei Wochen kein Hinweis; „1 Woche" zeigt, was beim Speichern passiert.
    await page.goto(`/grows/${growId}/setup`, { waitUntil: 'networkidle' })
    const feld = page.locator('[data-audit="flush-dauer"]')
    await expect(feld).toBeVisible()
    await expect(feld.getByRole('button', { name: '2 Wochen' })).toHaveClass(/is-primary/)
    await expect(page.locator('[data-audit="flush-plan-fehlt"]')).toHaveCount(0)

    await feld.getByRole('button', { name: '1 Woche' }).click()
    await expect(feld.getByRole('button', { name: '1 Woche' })).toHaveClass(/is-primary/)
    await expect(page.locator('[data-audit="flush-finish"]')).toContainText('03.05.2020')
    const hinweis = page.locator('[data-audit="flush-plan-fehlt"]')
    await expect(hinweis).toContainText('hat 8 Blütewochen')
    await expect(hinweis).toContainText('kommt Blütewoche 9 in den Plan')
    await expect(hinweis).toContainText('1 Woche Flush')

    // 3. Speichern: Flush 7, Finish eine Woche später, Blütewoche 9 sofort im Plan — ungeprüft.
    await page.getByRole('button', { name: 'Speichern' }).click()
    await expect.poll(async () => (await holeGrow(request, growId)).flushTage).toBe(7)
    grow = await holeGrow(request, growId)
    expect(grow.phasenanker.finishAb).toContain('2020-05-03')
    plan = await holePlan(request, growId)
    const w9 = plan.chart.columns.find((c: { id: string }) => c.id === 'flower-w9')
    expect(w9?.label).toBe('Blütewoche 9')
    expect(plan.ungeprueft).toEqual(['flower-w9'])
    expect(plan.verlaengert['flower-w9']).toBe('flower-w8')
    const liste = (await (await request.get('/api/grows')).json()).find((g: { id: number }) => g.id === growId)
    expect(liste.planUngeprueft).toEqual([{ id: 'flower-w9', label: 'Blütewoche 9' }])
    expect(liste.flushTage).toBe(7)

    // 4. Neu laden, nochmal speichern: die Auswahl bleibt, und die Woche kommt nicht ein zweites Mal.
    for (const durchgang of [1, 2]) {
      await page.goto(`/grows/${growId}/setup`, { waitUntil: 'networkidle' })
      await expect(page.locator('[data-audit="flush-dauer"]').getByRole('button', { name: '1 Woche' }), `Durchgang ${durchgang}`).toHaveClass(/is-primary/)
      // Der Plan führt die Woche jetzt: kein Hinweis mehr, dass sie fehlt.
      await expect(page.locator('[data-audit="flush-plan-fehlt"]'), `Durchgang ${durchgang}`).toHaveCount(0)
      await page.getByRole('button', { name: 'Speichern' }).click()
      await page.waitForTimeout(800)
      grow = await holeGrow(request, growId)
      expect(grow.flushTage, `Durchgang ${durchgang}`).toBe(7)
      plan = await holePlan(request, growId)
      expect(plan.chart.columns.filter((c: { id: string }) => c.id.startsWith('flower-w9')), `Durchgang ${durchgang}`).toHaveLength(1)
    }

    // 5. Aufgaben: die Karte nennt Grow und Woche und führt zum Plan dieses Grows.
    await page.goto('/aufgaben', { waitUntil: 'networkidle' })
    const karte = page.locator('[data-audit="plan-pruefen-section"]')
    await expect(karte).toContainText(`${MARKE} Grow`)
    await expect(karte).toContainText('Blütewoche 9 prüfen')
    // Der Bestand hat eigene Grows mit ungeprüften Wochen (Gorilla Glue braucht mehr Blütewochen als sein Plan):
    // geklickt wird die Karte DIESES Grows, nicht „die erste".
    await karte.locator('.af-due-grow').filter({ hasText: `${MARKE} Grow` }).getByRole('link', { name: 'Zum Plan' }).click()

    // 6. Plan-Seite: genau diese Woche, als ungeprüft gekennzeichnet; „Als geprüft markieren" löscht die Erinnerung.
    await expect(page).toHaveURL(/\/plan/)
    // Kopfzeile und Plan gehören zum selben Grow (Link mit ?grow=…) — nicht der Plan dieses Grows unter
    // der Woche des ersten laufenden.
    await expect(page.locator('[data-audit="wochen-zeile"]')).toContainText(MARKE)
    await expect(page.locator('[data-audit="plan-reiter"]')).toContainText(`${MARKE} Grow`)
    const banner = page.locator('[data-audit="plan-ungeprueft"]')
    await expect(banner).toContainText('noch nicht von dir geprüft')
    await expect(banner).toContainText('Blütewoche 8')
    const antwort = page.waitForResponse((r) => r.url().includes('/geprueft') && r.request().method() === 'POST')
    await banner.getByRole('button', { name: 'Als geprüft markieren' }).click()
    const gesendet = await antwort
    expect(gesendet.status(), `POST geprueft: ${await gesendet.text()}`).toBe(200)
    await expect(page.locator('[data-audit="plan-ungeprueft"]')).toHaveCount(0)
    plan = await holePlan(request, growId)
    expect(plan.ungeprueft ?? []).toHaveLength(0)
    // Die Woche selbst bleibt (nie gelöscht) und behält ihre Herkunft.
    expect(plan.chart.columns.some((c: { id: string }) => c.id === 'flower-w9')).toBe(true)
    expect(plan.verlaengert['flower-w9']).toBe('flower-w8')

    await page.goto('/aufgaben', { waitUntil: 'networkidle' })
    // Die Karten anderer Grows (Gorilla Glue im Bestand) bleiben — nur dieser Grow hat keine mehr.
    await expect(page.locator('[data-audit="plan-pruefen-section"] .af-due-grow').filter({ hasText: `${MARKE} Grow` })).toHaveCount(0)

    // 7. Zurück auf zwei Wochen: die angelegte Woche bleibt stehen, das Formular sagt es.
    await page.goto(`/grows/${growId}/setup`, { waitUntil: 'networkidle' })
    await page.locator('[data-audit="flush-dauer"]').getByRole('button', { name: '2 Wochen' }).click()
    await expect(page.locator('[data-audit="flush-plan-mehr"]')).toContainText('nichts gelöscht')
    await page.getByRole('button', { name: 'Speichern' }).click()
    await expect.poll(async () => (await holeGrow(request, growId)).flushTage).toBe(14)
    plan = await holePlan(request, growId)
    expect(plan.chart.columns.some((c: { id: string }) => c.id === 'flower-w9')).toBe(true)
    expect((await holeGrow(request, growId)).phasenanker.finishAb).toContain('2020-04-26')
  } finally {
    await abraeumen(request, eigener)
    if (sorteId != null) await request.delete(`/api/strains/${sorteId}`).catch(() => undefined)
  }
})
