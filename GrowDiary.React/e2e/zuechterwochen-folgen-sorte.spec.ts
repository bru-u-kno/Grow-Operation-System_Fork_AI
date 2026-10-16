import { test, expect, type APIRequestContext } from '@playwright/test'
import { darfUeberspringen } from './pflicht'
import { abraeumen, eigenenGrowAnlegen, type EigenerGrow } from './eigener-grow'
import { nimmSchloss, gibSchloss } from './schloss'

/**
 * Die Züchterwochen eines Grows folgen der Sorte — bis jemand einen eigenen Wert setzt.
 *
 * <b>Der Anlass (16.10.2026).</b> Mimosa EVO stand in der Sorte auf 9–10 Wochen,
 * im Grow auf 8–9: das Formular kopierte die Wochen beim Wählen der Sorte und
 * zeigte sie nie an. Finish, Plan-Woche und die Werte für Chiller und
 * Entfeuchter lagen dadurch eine Woche zu früh.
 *
 * <b>Eigener Datensatz.</b> Eigene Sorte und eigener Grow, beides am Ende
 * abgeräumt — der Demobestand, gegen den alle anderen Prüfungen laufen, bleibt
 * unberührt.
 *
 * <b>Der Weg im Browser, zweimal.</b> Speichern, neu laden, nochmal speichern
 * (CLAUDE.md: „speichern, dann nochmal speichern") — der Zustand „folgt der
 * Sorte" und „eigener Wert" muss beides überleben.
 */
/* Das Schloss gilt für jede schreibende E2E-Datei (e2e-schloss-vollstaendig):
   Sorten-Seite und Grow-Liste lesen den ganzen Bestand, und ein fremder Fall
   mit eigenem Grow würde hier sonst mitgezählt. */
test.beforeEach(async () => { await nimmSchloss() })
test.afterEach(() => { gibSchloss() })

test.describe.configure({ mode: 'serial' })
test.setTimeout(180_000)

const MARKE = `E2E Wochen ${Date.now()}`

async function holeGrow(api: APIRequestContext, id: number) {
  return (await api.get(`/api/grows/${id}`)).json()
}

async function sorteAendern(api: APIRequestContext, sorteId: number, min: number, max: number) {
  const antwort = await api.put(`/api/strains/${sorteId}`, {
    data: { name: MARKE, dominance: 'Hybrid', flowerWeeksMin: min, flowerWeeksMax: max },
  })
  expect(antwort.ok(), `Sorte ändern: ${antwort.status()}`).toBe(true)
}

test('Grow folgt der Sorte, eigener Wert bleibt, „Wie Sorte" führt zurück', async ({ page, request }) => {
  darfUeberspringen(!(await request.get('/api/grows')).ok(), 'Kein Backend.')

  let eigener: EigenerGrow | null = null
  let sorteId: number | null = null
  try {
    const angelegt = await request.post('/api/strains', {
      data: { name: MARKE, dominance: 'Hybrid', flowerWeeksMin: 9, flowerWeeksMax: 10 },
    })
    expect(angelegt.ok(), `Sorte anlegen: ${angelegt.status()}`).toBe(true)
    sorteId = (await angelegt.json()).id as number

    eigener = await eigenenGrowAnlegen(request, `${MARKE} Grow`, { strainId: sorteId, strain: MARKE, seedType: 'Feminized' })
    darfUeberspringen(eigener == null, 'Eigener Grow ließ sich nicht anlegen (Zelt oder Hydro-System fehlt).')
    const growId = eigener!.growId

    // 1. Neu angelegt ohne eigene Wochen: folgt der Sorte.
    let grow = await holeGrow(request, growId)
    expect([grow.breederFlowerWeeksMin, grow.breederFlowerWeeksMax, grow.breederWeeksEigen]).toEqual([9, 10, false])

    // 2. Die Sorte ändert sich — das Grow zieht ohne Zutun nach. Das war der Fehler.
    await sorteAendern(request, sorteId, 10, 11)
    grow = await holeGrow(request, growId)
    expect([grow.breederFlowerWeeksMin, grow.breederFlowerWeeksMax]).toEqual([10, 11])
    const inListe = (await (await request.get('/api/grows')).json()).find((g: { id: number }) => g.id === growId)
    expect([inListe.breederFlowerWeeksMin, inListe.breederFlowerWeeksMax]).toEqual([10, 11])

    // 3. Das Formular zeigt es an und verrät, woher es kommt.
    await page.goto(`/grows/${growId}/setup`, { waitUntil: 'networkidle' })
    const folgt = page.locator('[data-audit="zuechterwochen-folgt-sorte"]')
    await expect(folgt).toContainText('10–11 Wochen')
    await expect(folgt).toContainText(MARKE)

    // 4. Eigene Werte eintragen, speichern, neu laden, nochmal speichern.
    await folgt.getByRole('button', { name: 'Eigene Werte eintragen' }).click()
    const eigen = page.locator('[data-audit="zuechterwochen-eigen"]')
    await eigen.getByLabel('Blüte von (Wochen)').fill('8')
    await eigen.getByLabel('Blüte bis (Wochen)').fill('9')
    await expect(page.locator('[data-audit="zuechterwochen-abweichung"]'))
      .toContainText('Weicht von der Sorte ab (Sorte: 10–11). Finish beginnt dadurch 14 Tage früher.')
    await page.getByRole('button', { name: 'Speichern' }).click()
    await expect.poll(async () => (await holeGrow(request, growId)).breederWeeksEigen).toBe(true)
    grow = await holeGrow(request, growId)
    expect([grow.breederFlowerWeeksMin, grow.breederFlowerWeeksMax]).toEqual([8, 9])

    for (const durchgang of [1, 2]) {
      await page.goto(`/grows/${growId}/setup`, { waitUntil: 'networkidle' })
      const feld = page.locator('[data-audit="zuechterwochen-eigen"]')
      await expect(feld.getByLabel('Blüte von (Wochen)'), `Durchgang ${durchgang}`).toHaveValue('8')
      await expect(feld.getByLabel('Blüte bis (Wochen)'), `Durchgang ${durchgang}`).toHaveValue('9')
      await page.getByRole('button', { name: 'Speichern' }).click()
      await page.waitForTimeout(800)
      grow = await holeGrow(request, growId)
      expect([grow.breederFlowerWeeksMin, grow.breederFlowerWeeksMax, grow.breederWeeksEigen], `Durchgang ${durchgang}`).toEqual([8, 9, true])
    }

    // 5. Ändert sich die Sorte jetzt, bleibt der eigene Wert.
    await sorteAendern(request, sorteId, 11, 12)
    grow = await holeGrow(request, growId)
    expect([grow.breederFlowerWeeksMin, grow.breederFlowerWeeksMax, grow.strainFlowerWeeksMax]).toEqual([8, 9, 12])

    // 6. „Wie Sorte" führt zurück: folgt wieder, und zwar dem neuen Stand.
    await page.goto(`/grows/${growId}/setup`, { waitUntil: 'networkidle' })
    await page.getByRole('button', { name: 'Wie Sorte (11–12)' }).click()
    await page.getByRole('button', { name: 'Speichern' }).click()
    await expect.poll(async () => (await holeGrow(request, growId)).breederWeeksEigen).toBe(false)
    grow = await holeGrow(request, growId)
    expect([grow.breederFlowerWeeksMin, grow.breederFlowerWeeksMax]).toEqual([11, 12])
    await page.goto(`/grows/${growId}/setup`, { waitUntil: 'networkidle' })
    await expect(page.locator('[data-audit="zuechterwochen-folgt-sorte"]')).toContainText('11–12 Wochen')

    // 7. Ein weiteres Speichern eines ANDEREN Feldes kopiert die Sorte nicht ins Grow.
    await page.getByRole('button', { name: 'Speichern' }).click()
    await page.waitForTimeout(800)
    await sorteAendern(request, sorteId, 12, 13)
    grow = await holeGrow(request, growId)
    expect([grow.breederFlowerWeeksMin, grow.breederFlowerWeeksMax, grow.breederWeeksEigen]).toEqual([12, 13, false])
  } finally {
    await abraeumen(request, eigener)
    if (sorteId != null) await request.delete(`/api/strains/${sorteId}`).catch(() => undefined)
  }
})

test('Sorten-Seite sagt vor dem Speichern, was die Änderung bei den Grows bewirkt', async ({ page, request }) => {
  darfUeberspringen(!(await request.get('/api/grows')).ok(), 'Kein Backend.')

  let folgend: EigenerGrow | null = null
  let eigenwertig: EigenerGrow | null = null
  let sorteId: number | null = null
  try {
    const angelegt = await request.post('/api/strains', {
      data: { name: `${MARKE} B`, dominance: 'Hybrid', flowerWeeksMin: 8, flowerWeeksMax: 9 },
    })
    expect(angelegt.ok()).toBe(true)
    sorteId = (await angelegt.json()).id as number

    folgend = await eigenenGrowAnlegen(request, `${MARKE} folgt`, { strainId: sorteId, strain: `${MARKE} B`, status: 'Running' })
    eigenwertig = await eigenenGrowAnlegen(request, `${MARKE} eigen`, {
      strainId: sorteId, strain: `${MARKE} B`, status: 'Running',
      breederFlowerWeeksMin: 7, breederFlowerWeeksMax: 8, breederWeeksEigen: true,
    })
    darfUeberspringen(folgend == null || eigenwertig == null, 'Eigene Grows ließen sich nicht anlegen.')

    await page.goto('/sorten', { waitUntil: 'networkidle' })
    await page.getByRole('button', { name: `${MARKE} B` }).click()
    const hinweis = page.locator('[data-audit="sorte-wirkt-auf-grows"]')
    await expect(hinweis).toHaveCount(0) // noch nichts geändert

    await page.getByLabel('Blüte von (Wochen)').fill('9')
    await page.getByLabel('Blüte bis (Wochen)').fill('10')
    await expect(hinweis).toContainText('Wirkt auf 1 laufendes Grow')
    // Der Helfer hängt dem Namen ein Suffix an — gezählt wird deshalb am Wortlaut, nicht am Namen.
    await expect(hinweis).toContainText(new RegExp(`${MARKE} folgt \\d+: Finish verschiebt sich um 7 Tage nach hinten`))
    await expect(hinweis).toContainText(new RegExp(`Bleibt bei eigenem Wert: ${MARKE} eigen \\d+ \\(7–8 Wochen\\)`))
    await expect(hinweis).not.toContainText(new RegExp(`${MARKE} eigen \\d+: Finish`))
  } finally {
    await abraeumen(request, folgend)
    await abraeumen(request, eigenwertig)
    if (sorteId != null) await request.delete(`/api/strains/${sorteId}`).catch(() => undefined)
  }
})

test('Autoflower, die photoperiodisch wird, folgt danach der Sorte', async ({ page, request }) => {
  darfUeberspringen(!(await request.get('/api/grows')).ok(), 'Kein Backend.')

  let eigener: EigenerGrow | null = null
  let sorteId: number | null = null
  try {
    const angelegt = await request.post('/api/strains', {
      data: { name: `${MARKE} C`, dominance: 'Hybrid', flowerWeeksMin: 9, flowerWeeksMax: 10 },
    })
    expect(angelegt.ok()).toBe(true)
    sorteId = (await angelegt.json()).id as number

    eigener = await eigenenGrowAnlegen(request, `${MARKE} Auto`, {
      strainId: sorteId, strain: `${MARKE} C`, seedType: 'Autoflower', autoflowerDaysSinceGermination: 20,
    })
    darfUeberspringen(eigener == null, 'Eigener Autoflower-Grow ließ sich nicht anlegen.')
    const growId = eigener!.growId

    // Eine Autoflower rechnet nicht mit Züchterwochen: kein Feld, keine Wochen im Grow.
    await page.goto(`/grows/${growId}/setup`, { waitUntil: 'networkidle' })
    await expect(page.locator('[data-audit^="zuechterwochen"]')).toHaveCount(0)
    expect((await holeGrow(request, growId)).breederFlowerWeeksMax ?? null).toBeNull()

    // Wird sie photoperiodisch, folgt sie der Sorte — nicht einem leeren eigenen Feld.
    await page.getByLabel('Samen-Typ').selectOption('Feminized')
    await expect(page.locator('[data-audit="zuechterwochen-folgt-sorte"]')).toContainText('9–10 Wochen')
    await page.getByRole('button', { name: 'Speichern' }).click()
    await expect.poll(async () => (await holeGrow(request, growId)).seedType).toBe('Feminized')
    const grow = await holeGrow(request, growId)
    expect([grow.breederFlowerWeeksMin, grow.breederFlowerWeeksMax, grow.breederWeeksEigen]).toEqual([9, 10, false])
  } finally {
    await abraeumen(request, eigener)
    if (sorteId != null) await request.delete(`/api/strains/${sorteId}`).catch(() => undefined)
  }
})
