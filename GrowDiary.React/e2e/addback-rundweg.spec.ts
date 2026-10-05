import { test, expect, type Page } from '@playwright/test'
import { darfUeberspringen } from './pflicht'
import { abraeumen, eigenenGrowAnlegen, type EigenerGrow } from './eigener-grow'

/**
 * Das Nachfüllen als ein Vorgang (A-006, Etappe 3): derselbe Ablauf wie beim
 * Wasserwechsel — ausfüllen, speichern, ein zweites Mal speichern ohne
 * Neuladen, neu laden, wiederfinden, löschen. Dazu die Vorbelegung per Link,
 * über die das Grow-Tagebuch („Nachfüllen eintragen" an einer Auffälligkeit)
 * den Ablauf öffnet, und die Weiterleitung vom früheren Assistenten.
 *
 * Eigener Grow mit eigenem Programm: der Rundweg schreibt Verbrauch und
 * Messungen, das darf den geteilten Demobestand nicht verändern.
 */

let eigener: EigenerGrow | null = null
let artikelId: number | null = null
/**
 * Bewusst OHNE „Aqua Vega A" im Namen: der Wasserwechsel-Rundweg legt einen
 * Artikel mit diesem Namen an, und laufen beide gleichzeitig, passen zwei
 * Artikel — dann rät der Vorschlag nicht und bucht keinen (gefunden beim
 * ersten gemeinsamen Lauf). Hier wird der Artikel deshalb von Hand gewählt.
 */
const ARTIKEL = 'Nachfüll-Rundweg Grunddünger'

test.describe.configure({ mode: 'serial' })

async function schritt(page: Page, name: string) {
  await page.getByRole('button', { name, exact: true }).click()
}

async function speichern(page: Page) {
  const antwort = page.waitForResponse((r) => /\/api\/grows\/\d+\/addback\/vorgaenge$/.test(r.url()) && r.request().method() === 'POST')
  await page.locator('[data-audit="addback-speichern"]').click()
  const fertig = await antwort
  expect(fertig.status(), `Speichern antwortete mit HTTP ${fertig.status()}: ${await fertig.text()}`).toBe(201)
  return fertig.json()
}

/** Ortszeit `yyyy-MM-ddTHH:mm`, wie sie das Feld „Wann" zeigt. */
function feldzeit(datum: Date): string {
  const zwei = (n: number) => String(n).padStart(2, '0')
  return `${datum.getFullYear()}-${zwei(datum.getMonth() + 1)}-${zwei(datum.getDate())}T${zwei(datum.getHours())}:${zwei(datum.getMinutes())}`
}

test.describe('Nachfüll-Rundweg', () => {
  test.beforeAll(async ({ playwright, baseURL }) => {
    const api = await playwright.request.newContext({ baseURL })
    try {
      eigener = await eigenenGrowAnlegen(api, 'Rundweg-Nachfuellen', { feedProgramId: 'skx-canna-aqua' })
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
      if (artikelId != null) await api.delete(`/api/kosten/artikel/${artikelId}`)
    } finally {
      await api.dispose()
    }
  })

  test('Rundweg: VorgangAblauf (Nachfüllen) — ausfüllen, zweimal speichern, wiederfinden, löschen', async ({ page }) => {
    darfUeberspringen(eigener == null || artikelId == null, 'Kein eigener Grow anlegbar — laeuft die App unter GROW_OS_URL?')
    const growId = eigener!.growId
    page.on('dialog', (dialog) => void dialog.accept())

    await page.goto(`/addback?growId=${growId}`, { waitUntil: 'networkidle' })
    const ablauf = page.locator('[data-audit="addback-ablauf"]')
    await expect(ablauf).toBeVisible()
    // Eine Hauptaktion, ein Weg: der frühere Assistent ist nicht mehr da.
    await expect(page.getByText('Prüfen & Dosierung berechnen')).toHaveCount(0)

    // ---- Schritt 1: vor zwei Tagen, von Hand mit DO
    const vorbei = new Date(Date.now() - 2 * 24 * 3600 * 1000)
    vorbei.setHours(10, 15, 0, 0)
    await ablauf.getByLabel('Wann').fill(feldzeit(vorbei))
    await ablauf.getByPlaceholder('z. B. 7,8').fill('7,6')
    await ablauf.getByLabel(/^EC selbst gemessen/).fill('1,75')
    await page.locator('[data-audit="addback-weiter-2"]').click()

    // ---- Schritt 2: nachgefüllte Liter, Vorschlag auf diese Liter, ↺ und ↶, + Produkt
    await expect(ablauf.getByLabel('Art des Nachfüllens')).toHaveValue('Addback')
    await expect(ablauf.getByLabel('Nachgefüllt in Litern'), 'Nachfüllen wird nicht mit dem Anlagevolumen vorbelegt.').toHaveValue('')
    await ablauf.getByRole('combobox', { name: 'Wasser' }).selectOption('Tap')
    await ablauf.getByLabel('Nachgefüllt in Litern').fill('10')
    const zeileA = ablauf.getByLabel('Aqua Vega A eingesetzt')
    await expect(zeileA, 'Der Mischplan-Vorschlag kam nicht an — keine Zeile „Aqua Vega A".').toBeVisible({ timeout: 10_000 })
    await expect(ablauf.getByRole('columnheader', { name: 'Vorschlag für 10 L' })).toBeVisible()
    const vorschlagA = await zeileA.inputValue()
    expect(Number(vorschlagA), `Vorschlag für Aqua Vega A ist „${vorschlagA}".`).toBeGreaterThan(0)
    // Auf die Liter gerechnet: doppelte Liter, doppelter Vorschlag (± Rundung).
    await ablauf.getByLabel('Nachgefüllt in Litern').fill('20')
    await expect(ablauf.getByRole('columnheader', { name: 'Vorschlag für 20 L' })).toBeVisible()
    await expect.poll(async () => Number(await zeileA.inputValue())).toBeGreaterThanOrEqual(2 * Number(vorschlagA) - 1)
    await ablauf.getByLabel('Nachgefüllt in Litern').fill('10')
    await expect(zeileA).toHaveValue(vorschlagA)

    await zeileA.fill('25')
    await expect(zeileA).toHaveClass(/is-geaendert/)
    await expect(ablauf.getByText('1 Wert von dir geändert')).toBeVisible()
    await ablauf.getByRole('button', { name: `↺ Vorschlag ${vorschlagA} ml` }).click()
    await expect(zeileA).toHaveValue(vorschlagA)
    await ablauf.getByRole('button', { name: '↶ deins 25 ml' }).click()
    await expect(zeileA).toHaveValue('25')
    // Ohne passenden Artikel bietet die Zeile die Wahl an — dort den eigenen wählen.
    const wahl = ablauf.getByLabel('Artikel für Aqua Vega A')
    if (await wahl.isVisible()) await wahl.selectOption({ label: ARTIKEL })
    await expect(ablauf.getByLabel('Aqua Vega A buchen')).toBeChecked()

    await ablauf.getByLabel('Produkt hinzufügen').selectOption({ label: 'Purolyt' })
    await ablauf.getByRole('button', { name: '+ Produkt' }).click()
    await ablauf.getByLabel('Purolyt eingesetzt').fill('15')
    // Die Mischrechnung „Tank danach" kennt Anlage, EC vorher und Liter.
    await expect(page.locator('[data-audit="addback-tank"]')).toContainText('Mischrechnung')
    await page.locator('[data-audit="addback-weiter-3"]').click()

    // ---- Schritt 3: nachher
    await ablauf.getByPlaceholder('z. B. 1,15').fill('1,61')
    await ablauf.getByPlaceholder('z. B. 450').fill('430')
    const marke = `Nachfüll-Rundweg ${Date.now()}`
    await ablauf.getByPlaceholder(/Warum nachgefüllt/).fill(marke)
    await expect(page.locator('[data-audit="addback-vergleich"]')).toContainText('−0,14')
    await page.locator('[data-audit="addback-weiter-4"]').click()

    // ---- Schritt 4: Vorschau, keine Wechsel-Erinnerung, speichern
    const vorschau = page.locator('[data-audit="addback-tagebuch-vorschau"]')
    await expect(vorschau).toContainText('Addback 10 L Leitungswasser')
    await expect(vorschau).toContainText(marke)
    await expect(ablauf.getByText('Wasserwechsel-Erinnerung neu starten')).toHaveCount(0)
    const erster = await speichern(page)

    const gemeldet = new Date(erster.eintrag.performedAtUtc)
    expect(Math.abs(gemeldet.getTime() - vorbei.getTime()) / 3600000, `Eingetragen war ${feldzeit(vorbei)}, gespeichert ${erster.eintrag.performedAtUtc}.`).toBeLessThan(1)
    expect(erster.eintrag.kind).toBe('Addback')
    expect(erster.eintrag.litersAdded).toBe(10)
    expect(erster.eintrag.ecBefore).toBe(1.75)
    expect(erster.eintrag.ecAfter).toBe(1.61)
    expect(erster.vorher?.dissolvedOxygenMgL).toBe(7.6)
    expect(erster.nachher?.orpMv).toBe(430)
    expect(erster.nachher?.solutionChange, 'Nachfüllen ist kein Lösungswechsel.').toBe(false)
    const namen = (erster.buchungen as Array<{ artikelName: string; menge: number }>).map((b) => `${b.artikelName} ${b.menge}`)
    expect(namen).toContain('Purolyt 15')
    expect(namen.some((n) => n.endsWith(' 25')), `Der eigene Wert 25 ml wurde nicht gebucht: ${namen.join(', ')}`).toBe(true)
    expect(namen).toContain('Leitungswasser 10')
    expect(erster.tagebuch?.entryType).toBe('Feeding')
    expect(erster.tagebuch?.body).toContain(marke)

    // ---- Zweites Speichern ohne Neuladen: frischer Ablauf, nur Wasser, Osmose
    await expect(page.getByText(/Nachfüllen gespeichert — mit/)).toBeVisible()
    await expect(ablauf.getByLabel('Wann'), 'Nach dem Speichern steht nicht wieder ein frischer Ablauf da.').toBeVisible()
    await page.locator('[data-audit="addback-weiter-2"]').click()
    await expect(ablauf.getByLabel('Nachgefüllt in Litern'), 'Der zweite Vorgang erbte die Liter vom ersten.').toHaveValue('')
    await ablauf.getByLabel('Art des Nachfüllens').selectOption('TopOff')
    await ablauf.getByLabel('Nachgefüllt in Litern').fill('5')
    await ablauf.getByRole('combobox', { name: 'Wasser' }).selectOption('RO')
    await expect(ablauf.getByLabel('Osmosewasser eingesetzt')).toHaveValue('5')
    await expect(ablauf.getByLabel('Aqua Vega A eingesetzt'), '„Nur Wasser" zeigt trotzdem die Plan-Zeilen.').toHaveCount(0)
    await schritt(page, '4 · Speichern')
    await expect(page.locator('[data-audit="addback-tagebuch-vorschau"]')).toContainText('Nachfüllen 5 L Osmosewasser')
    const zweiter = await speichern(page)
    expect(zweiter.eintrag.kind).toBe('TopOff')
    expect(zweiter.eintrag.waterUsed).toBe('RO')
    expect((zweiter.buchungen as Array<{ artikelName: string; menge: number }>).map((b) => `${b.artikelName} ${b.menge}`)).toEqual(['Osmosewasser 5'])

    // ---- Neu laden, wiederfinden
    await page.goto(`/addback?growId=${growId}`, { waitUntil: 'networkidle' })
    const liste = page.locator('[data-audit="addback-log-list"]')
    await expect(liste.getByText(marke, { exact: false })).toBeVisible()
    await expect(liste.locator(`[data-nachfuell-vorgang="${erster.id}"]`)).toContainText('Vorgang: 2 Messwerten, 3 Buchungen und der Tagebuchzeile')
    await expect(liste.locator(`[data-nachfuell-vorgang="${zweiter.id}"]`)).toContainText('Nachfüllen')

    // ---- Löschen am Vorgang nimmt Messungen und Buchungen mit
    await liste.locator(`[data-nachfuell-vorgang="${erster.id}"]`).getByRole('button', { name: /entfernen/ }).click()
    await expect(liste.getByText(marke, { exact: false })).toHaveCount(0)
    const rest = await (await page.request.get(`/api/grows/${growId}/addback/vorgaenge`)).json() as Array<{ id: number }>
    expect(rest.map((v) => v.id)).toEqual([zweiter.id])
    expect((await page.request.get(`/api/measurements/${erster.nachher.id}`)).status(), 'Die Messung „nachher" blieb stehen.').toBe(404)
    expect((await page.request.get(`/api/grows/${growId}/addback/logs`)).ok()).toBe(true)
    const logs = await (await page.request.get(`/api/grows/${growId}/addback/logs`)).json() as Array<{ id: number }>
    expect(logs.map((l) => l.id)).toEqual([zweiter.eintrag.id])
  })

  test('Vorbelegung per Link — so öffnet das Tagebuch „Nachfüllen eintragen"', async ({ page }) => {
    darfUeberspringen(eigener == null, 'Kein eigener Grow anlegbar — laeuft die App unter GROW_OS_URL?')
    const growId = eigener!.growId
    const zeitpunkt = new Date(Date.now() - 26 * 3600 * 1000)
    zeitpunkt.setSeconds(0, 0)
    const marke = `Sprung ${Date.now()}`

    // Über die alte Adresse: die Weiterleitung muss die Vorbelegung mitnehmen.
    const suche = new URLSearchParams({
      zeitpunkt: zeitpunkt.toISOString(), ecVorher: '1.75', ecNachher: '1.61', phVorher: '6.02', liter: '20', notiz: marke,
    })
    await page.goto(`/grows/${growId}/addback?${suche.toString()}`, { waitUntil: 'networkidle' })
    await expect(page).toHaveURL(new RegExp(`/addback\\?growId=${growId}&`))
    const ablauf = page.locator('[data-audit="addback-ablauf"]')

    await expect(ablauf.getByText('Vorbelegt aus dem Link')).toBeVisible()
    await expect(ablauf.getByLabel('Wann')).toHaveValue(feldzeit(zeitpunkt))
    const sensor = page.locator('[data-audit="addback-sensor"]')
    await expect(sensor).toContainText('Vom Sensor, aus dem Link')
    await expect(sensor).toContainText('1,75')
    await expect(sensor).toContainText('6,02')

    await page.locator('[data-audit="addback-weiter-2"]').click()
    await expect(ablauf.getByLabel('Nachgefüllt in Litern')).toHaveValue('20')
    await page.locator('[data-audit="addback-weiter-3"]').click()
    await expect(ablauf.getByPlaceholder('z. B. 1,15')).toHaveValue('1,61')
    await expect(ablauf.getByPlaceholder(/Warum nachgefüllt/)).toHaveValue(marke)
    await expect(page.locator('[data-audit="addback-vergleich"]')).toContainText('−0,14')
    await schritt(page, '4 · Speichern')
    const vorgang = await speichern(page)

    expect(vorgang.vorher.reservoirEc).toBe(1.75)
    expect(vorgang.vorher.reservoirPh).toBe(6.02)
    expect(vorgang.vorherHerkunft).toBe('Sensor')
    expect(vorgang.vorher.source).toBe('HomeAssistant')
    expect(vorgang.nachher.reservoirEc).toBe(1.61)
    expect(vorgang.nachher.source, 'Unveränderte Sensorwerte „nachher" zählen als Sensor.').toBe('HomeAssistant')
    expect(Math.abs(new Date(vorgang.eintrag.performedAtUtc).getTime() - zeitpunkt.getTime())).toBeLessThan(60_000)
    expect(vorgang.eintrag.litersAdded).toBe(20)
    expect(vorgang.tagebuch.body).toContain(marke)

    // Nach dem Speichern ist die Vorbelegung verbraucht: der nächste Ablauf beginnt leer.
    await expect(ablauf.getByText('Vorbelegt aus dem Link')).toHaveCount(0)

    // Wiederfinden nach dem Neuladen — ohne Vorbelegung in der Adresse.
    await page.goto(`/addback?growId=${growId}&vorgang=${vorgang.id}`, { waitUntil: 'networkidle' })
    await expect(page.locator(`[data-nachfuell-vorgang="${vorgang.id}"]`)).toHaveClass(/is-markiert/)
  })
})
