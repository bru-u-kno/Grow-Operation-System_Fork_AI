import { test, expect, type Page } from '@playwright/test'
import { darfUeberspringen } from './pflicht'
import { abraeumen, eigenenGrowAnlegen, type EigenerGrow } from './eigener-grow'

/**
 * Der Wasserwechsel-Ablauf (A-006): vier Schritte, speichern, neu laden,
 * wiederfinden — und ein zweites Mal speichern, ohne die Seite neu zu laden.
 *
 * **Der Anlass.** Ein Wasserwechsel musste an vier Stellen getrennt erfasst
 * werden (Messung mit Haken, Wechsel, Verbrauch, Journal). Seit A-006 legt ein
 * Speichern alles als einen Vorgang an. Geprüft wird hier, was der Nutzer
 * sieht und was danach in der App steht — nicht, ob ein Knopf da ist.
 *
 * **Zurückdatieren** bleibt Teil des Rundwegs: der Tester meldete am
 * 01.09.2026, dass man einen Wechsel nachtragen können muss. Der Ablauf fragt
 * deshalb in Schritt 1 „Wann".
 *
 * Eigener Grow mit eigenem Programm: der Rundweg schreibt Verbrauch und
 * Messungen, das darf den geteilten Demobestand nicht verändern.
 */

let eigener: EigenerGrow | null = null
/** Ein eigener Kosten-Artikel für den Grunddünger der Anzucht — der Vorschlag findet ihn über den Namen. */
let artikelId: number | null = null
const ARTIKEL = 'Aqua Vega A (Rundweg)'

test.describe.configure({ mode: 'serial' })

async function schritt(page: Page, name: string) {
  await page.getByRole('button', { name, exact: true }).click()
}

async function speichern(page: Page) {
  const antwort = page.waitForResponse((r) => /\/api\/grows\/\d+\/wasserwechsel$/.test(r.url()) && r.request().method() === 'POST')
  await page.locator('[data-audit="wasserwechsel-speichern"]').click()
  const fertig = await antwort
  expect(fertig.status(), `Speichern antwortete mit HTTP ${fertig.status()}: ${await fertig.text()}`).toBe(201)
  return fertig.json()
}

test.describe('Wasserwechsel-Rundweg', () => {
  test.beforeAll(async ({ playwright, baseURL }) => {
    const api = await playwright.request.newContext({ baseURL })
    try {
      eigener = await eigenenGrowAnlegen(api, 'Rundweg-Wasserwechsel', { feedProgramId: 'skx-canna-aqua' })
      const artikel = await api.post('/api/kosten/artikel', { data: { name: ARTIKEL, einheit: 'ml', aktiv: true } })
      if (artikel.ok()) artikelId = (await artikel.json()).id
    } finally {
      await api.dispose()
    }
  })

  test.afterAll(async ({ playwright, baseURL }) => {
    const api = await playwright.request.newContext({ baseURL })
    try {
      await abraeumen(api, eigener)
      // Der Artikel nimmt seine Buchungen mit (ON DELETE CASCADE).
      if (artikelId != null) await api.delete(`/api/kosten/artikel/${artikelId}`)
    } finally {
      await api.dispose()
    }
  })

  test('Rundweg: WasserwechselAblauf — vier Schritte, zurückdatiert, zweimal speichern, wiederfinden, löschen', async ({ page }) => {
    darfUeberspringen(eigener == null || artikelId == null, 'Kein eigener Grow anlegbar — laeuft die App unter GROW_OS_URL?')
    const growId = eigener!.growId
    page.on('dialog', (dialog) => void dialog.accept())

    await page.goto(`/wasserwechsel?growId=${growId}`, { waitUntil: 'networkidle' })
    const ablauf = page.locator('[data-audit="wasserwechsel-ablauf"]')
    await expect(ablauf).toBeVisible()

    // ---- Schritt 1: vor drei Tagen, 14:30 — der Nachtrag aus der Meldung
    const vorbei = new Date(Date.now() - 3 * 24 * 3600 * 1000)
    const tag = `${vorbei.getFullYear()}-${String(vorbei.getMonth() + 1).padStart(2, '0')}-${String(vorbei.getDate()).padStart(2, '0')}`
    await ablauf.getByLabel('Wann').fill(`${tag}T14:30`)
    await ablauf.getByPlaceholder('z. B. 7,8').fill('7,8')
    await ablauf.getByLabel(/^EC selbst gemessen/).fill('1,63')
    await page.locator('[data-audit="wasserwechsel-weiter-2"]').click()

    // ---- Schritt 2: Liter, Plan-Zeilen, eigener Wert, ↺ und ↶
    // Der eigene Grow steht in der Anzucht: der Plan nennt Aqua Vega A/B, CalMag, Rhizotonic.
    await ablauf.getByRole('combobox', { name: 'Wasser' }).selectOption('Tap')
    await ablauf.getByLabel('Neues Wasser in Litern').fill('100')
    const zeileA = ablauf.getByLabel('Aqua Vega A eingesetzt')
    await expect(zeileA, 'Der Mischplan-Vorschlag kam nicht an — keine Zeile „Aqua Vega A".').toBeVisible({ timeout: 10_000 })
    const vorschlagA = await zeileA.inputValue()
    expect(Number(vorschlagA), `Vorschlag für Aqua Vega A ist „${vorschlagA}".`).toBeGreaterThan(0)
    await expect(ablauf.getByLabel('Aqua Vega A buchen'), 'Der Artikel wurde nicht über den Namen gefunden.').toBeChecked()
    // Ohne passenden Artikel bietet die Zeile die Wahl an, statt zu raten.
    await expect(ablauf.getByLabel('Artikel für Rhizotonic')).toBeVisible()

    await zeileA.fill('120')
    await expect(zeileA).toHaveClass(/is-geaendert/)
    await expect(ablauf.getByText('1 Wert von dir geändert')).toBeVisible()
    await ablauf.getByRole('button', { name: `↺ Vorschlag ${vorschlagA} ml` }).click()
    await expect(zeileA).toHaveValue(vorschlagA)
    await ablauf.getByRole('button', { name: '↶ deins 120 ml' }).click()
    await expect(zeileA).toHaveValue('120')

    // Calcium im Leitungswasser: CalMag steht auf 0 (Wasserprofil der Testdaten).
    await expect(ablauf.getByLabel('CalMag Agent eingesetzt')).toHaveValue('0')

    // + Produkt aus den Kosten-Artikeln
    await ablauf.getByLabel('Produkt hinzufügen').selectOption({ label: 'Purolyt' })
    await ablauf.getByRole('button', { name: '+ Produkt' }).click()
    await ablauf.getByLabel('Purolyt eingesetzt').fill('200')
    await page.locator('[data-audit="wasserwechsel-weiter-3"]').click()

    // ---- Schritt 3: nachher
    await ablauf.getByPlaceholder('z. B. 1,15').fill('1,15')
    await ablauf.getByPlaceholder('z. B. 6,1').fill('6,15')
    await ablauf.getByPlaceholder('z. B. 450').fill('450')
    const marke = `Rundweg ${Date.now()}`
    await ablauf.getByPlaceholder(/Warum so angesetzt/).fill(marke)
    await expect(page.locator('[data-audit="wasserwechsel-vergleich"]')).toContainText('−0,48')
    await page.locator('[data-audit="wasserwechsel-weiter-4"]').click()

    // ---- Schritt 4: Vorschau, speichern
    await expect(page.locator('[data-audit="wasserwechsel-tagebuch-vorschau"]')).toContainText(marke)
    const erster = await speichern(page)

    const gemeldet = new Date(erster.wechsel.performedAtUtc)
    const abstandStunden = Math.abs(gemeldet.getTime() - new Date(`${tag}T14:30:00`).getTime()) / 3600000
    expect(abstandStunden, `Eingetragen war ${tag} 14:30, gespeichert ${erster.wechsel.performedAtUtc}.`).toBeLessThan(1)
    expect(erster.vorher?.reservoirEc).toBe(1.63)
    expect(erster.vorher?.dissolvedOxygenMgL).toBe(7.8)
    expect(erster.nachher?.orpMv).toBe(450)
    const namen = (erster.buchungen as Array<{ artikelName: string; menge: number }>).map((b) => `${b.artikelName} ${b.menge}`)
    expect(namen).toContain('Purolyt 200')
    expect(namen).toContain(`${ARTIKEL} 120`)
    expect(namen).toContain('Leitungswasser 100')
    expect(erster.tagebuch?.entryType).toBe('ReservoirChange')
    expect(erster.tagebuch?.body).toContain(marke)

    // ---- Zweites Speichern ohne Neuladen: frischer Ablauf, Osmose
    await expect(page.getByText(/Wasserwechsel gespeichert — mit/)).toBeVisible()
    await expect(ablauf.getByLabel('Wann'), 'Nach dem Speichern steht nicht wieder ein frischer Ablauf da.').toBeVisible()
    await page.locator('[data-audit="wasserwechsel-weiter-2"]').click()
    await ablauf.getByLabel('Neues Wasser in Litern').fill('50')
    await ablauf.getByRole('combobox', { name: 'Wasser' }).selectOption('RO')
    await expect(ablauf.getByLabel('Aqua Vega A eingesetzt'), 'Der zweite Vorgang erbte den Wert „120" vom ersten.')
      .not.toHaveValue('120')
    // Befund des Prüfers: ein Foto ohne Messung „nachher" ging still verloren.
    await schritt(page, '3 · Nachher')
    await ablauf.getByPlaceholder('z. B. 1,15').fill('1,2')
    await ablauf.locator('input[type="file"]').setInputFiles({ name: 'tank.png', mimeType: 'image/png', buffer: Buffer.from('89504e470d0a1a0a', 'hex') })
    await ablauf.getByPlaceholder('z. B. 1,15').fill('')
    await schritt(page, '4 · Speichern')
    let abgeschickt = false
    const horcher = (r: { url: () => string; method: () => string }) => { if (/\/wasserwechsel$/.test(r.url()) && r.method() === 'POST') abgeschickt = true }
    page.on('request', horcher)
    await page.locator('[data-audit="wasserwechsel-speichern"]').click()
    await expect(ablauf.getByText(/Das Foto braucht die Messung „nachher"/)).toBeVisible()
    page.off('request', horcher)
    expect(abgeschickt, 'Mit Foto, aber ohne Messung „nachher" wurde trotzdem gespeichert — das Foto wäre verloren.').toBe(false)
    await ablauf.getByRole('button', { name: '✕ Foto herausnehmen' }).click()
    await schritt(page, '4 · Speichern')
    const zweiter = await speichern(page)
    expect(zweiter.wechsel.waterUsed).toBe('RO')
    expect((zweiter.buchungen as Array<{ artikelName: string }>).map((b) => b.artikelName)).toContain('Osmosewasser')

    // ---- Neu laden, wiederfinden, Stand
    await page.goto(`/wasserwechsel?growId=${growId}`, { waitUntil: 'networkidle' })
    const liste = page.locator('[data-audit="changeout-list"]')
    await expect(liste.getByText(marke, { exact: false })).toBeVisible()
    await expect(liste.locator(`[data-vorgang="${erster.id}"]`)).toContainText('Vorgang:')
    const stand = await (await page.request.get(`/api/grows/${growId}/changeouts/stand`)).json()
    expect(stand.tageSeit, 'Der Stand zählt den zweiten (heutigen) Wechsel nicht.').toBe(0)

    // ---- Löschen am Vorgang nimmt Messungen und Buchungen mit
    await liste.locator(`[data-vorgang="${erster.id}"]`).getByRole('button', { name: /entfernen/ }).click()
    await expect(liste.getByText(marke, { exact: false })).toHaveCount(0)
    const rest = await (await page.request.get(`/api/grows/${growId}/wasserwechsel`)).json() as Array<{ id: number }>
    expect(rest.map((v) => v.id)).toEqual([zweiter.id])
    const messung = await page.request.get(`/api/measurements/${erster.nachher.id}`)
    expect(messung.status(), 'Die Messung „nachher" blieb nach dem Löschen des Vorgangs stehen.').toBe(404)
  })
})
