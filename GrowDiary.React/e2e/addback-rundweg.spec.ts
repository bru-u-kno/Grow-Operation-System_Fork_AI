import { test, expect, type Locator, type Page } from '@playwright/test'
import { darfUeberspringen } from './pflicht'
import { abraeumen, eigenenGrowAnlegen, type EigenerGrow } from './eigener-grow'
import { gibSchloss, nimmSchloss } from './schloss'

/**
 * Das vereinfachte Addback (A-006, Etappe 3, 10.10.2026): ausfüllen, speichern, ein zweites Mal
 * speichern ohne Neuladen, neu laden, wiederfinden, löschen — für alle drei Fälle (nur Wasser,
 * Wasser mit Dünger und Zusätzen, nur Zusätze). Dazu: Artikel nur nach Rückfrage, die
 * automatische Nachmessung durch den Hintergrundtakt, die Vorgabe „als Standard merken" und die
 * Vorbelegung per Link, über die das Grow-Tagebuch („Nachfüllen eintragen") die Seite öffnet.
 *
 * Eigener Grow mit eigenem Programm: der Rundweg schreibt Verbrauch und Messungen, das darf den
 * geteilten Demobestand nicht verändern.
 */

let eigener: EigenerGrow | null = null
const angelegteArtikel: number[] = []
/**
 * Bewusst OHNE „Aqua Vega A" im Namen: der Wasserwechsel-Rundweg legt einen Artikel mit diesem
 * Namen an, und laufen beide gleichzeitig, passen zwei Artikel — dann rät der Vorschlag nicht und
 * bucht keinen. Hier wird der Artikel deshalb von Hand gewählt.
 */
const ARTIKEL = 'Nachfüll-Rundweg Grunddünger'
/** Ein Zusatz, den es nicht gibt — an ihm wird die Rückfrage „als Artikel anlegen?" durchgespielt. */
const NEUER_ZUSATZ = `Rundweg-Zusatz ${Date.now()}`

test.describe.configure({ mode: 'serial' })
// Die Fälle teilen Kostenartikel und die Vorgabe der Nachmessung mit anderen Dateien — also das Schloss nehmen.
test.beforeEach(async () => { await nimmSchloss() })
test.afterEach(() => { gibSchloss() })

const formular = (page: Page) => page.locator('[data-audit="nachfuellen-formular"]')

async function oeffnen(page: Page, growId: number, suche = '') {
  await page.goto(`/addback?growId=${growId}${suche}`, { waitUntil: 'networkidle' })
  await expect(formular(page)).toBeVisible()
}

async function speichern(page: Page) {
  const antwort = page.waitForResponse((r) => /\/api\/grows\/\d+\/addback\/vorgaenge$/.test(r.url()) && r.request().method() === 'POST')
  await page.locator('[data-audit="nachfuellen-speichern"]').click()
  const fertig = await antwort
  expect(fertig.status(), `Speichern antwortete mit HTTP ${fertig.status()}: ${await fertig.text()}`).toBe(201)
  return fertig.json()
}

/** Ortszeit `yyyy-MM-ddTHH:mm`, wie sie das Feld „Zeitpunkt" zeigt. */
function feldzeit(datum: Date): string {
  const zwei = (n: number) => String(n).padStart(2, '0')
  return `${datum.getFullYear()}-${zwei(datum.getMonth() + 1)}-${zwei(datum.getDate())}T${zwei(datum.getHours())}:${zwei(datum.getMinutes())}`
}

async function weitereAngabenOeffnen(page: Page) {
  const knopf = page.getByRole('button', { name: /Weitere Angaben/ })
  if ((await knopf.getAttribute('aria-expanded')) !== 'true') await knopf.click()
}

/** Beantwortet die Rückfrage „als neuen Verbrauchsartikel anlegen?" an der Zeile, die `name` nennt. */
async function artikelAnlegen(page: Page, zeile: Locator, ja: boolean) {
  await zeile.getByRole('button', { name: 'Als Artikel anlegen …' }).click()
  const frage = page.getByRole('group', { name: 'Neuen Verbrauchsartikel anlegen' })
  await expect(frage).toBeVisible()
  await expect(frage).toContainText('ohne Preis')
  if (!ja) {
    await frage.getByRole('button', { name: 'Nicht jetzt' }).click()
    await expect(frage).toHaveCount(0)
    return
  }
  const antwort = page.waitForResponse((r) => /\/api\/kosten\/artikel$/.test(r.url()) && r.request().method() === 'POST')
  await frage.getByRole('button', { name: 'Ja, anlegen' }).click()
  const fertig = await antwort
  expect(fertig.status()).toBe(201)
  angelegteArtikel.push((await fertig.json()).id)
}

async function artikelId(page: Page, name: string): Promise<number | null> {
  const liste = await (await page.request.get('/api/kosten/artikel')).json() as Array<{ id: number; name: string }>
  return liste.find((a) => a.name === name)?.id ?? null
}

test.describe('Nachfüll-Rundweg', () => {
  test.beforeAll(async ({ playwright, baseURL }) => {
    const api = await playwright.request.newContext({ baseURL })
    try {
      eigener = await eigenenGrowAnlegen(api, 'Rundweg-Nachfuellen', { feedProgramId: 'skx-canna-aqua' })
      const artikel = await api.post('/api/kosten/artikel', { data: { name: ARTIKEL, einheit: 'ml', aktiv: true } })
      if (artikel.ok()) angelegteArtikel.push((await artikel.json()).id)
    } finally {
      await api.dispose()
    }
  })

  test.afterAll(async ({ playwright, baseURL }) => {
    const api = await playwright.request.newContext({ baseURL })
    try {
      await abraeumen(api, eigener)
      for (const id of angelegteArtikel) await api.delete(`/api/kosten/artikel/${id}`).catch(() => undefined)
      // Die Vorgabe der Nachmessung zurück auf den Standard — andere Läufe sollen sie nicht verändert sehen.
      await api.put('/api/addback/einstellungen', { data: { nachmessungAutomatisch: true, nachmessungMinuten: 15 } }).catch(() => undefined)
    } finally {
      await api.dispose()
    }
  })

  test('Rundweg: Wasser + Dünger — Mischplan, eigene Mengen, Zusätze, Artikel nur nach Rückfrage, zweimal speichern', async ({ page }) => {
    darfUeberspringen(eigener == null || angelegteArtikel.length === 0, 'Kein eigener Grow anlegbar — laeuft die App unter GROW_OS_URL?')
    const growId = eigener!.growId
    await oeffnen(page, growId)
    const f = formular(page)
    // Eine Hauptaktion, ein Weg: weder der frühere Assistent noch der 4-Schritte-Ablauf sind noch da.
    await expect(page.getByText('Prüfen & Dosierung berechnen')).toHaveCount(0)
    await expect(page.locator('[data-audit="addback-ablauf"]')).toHaveCount(0)
    // Live: die Sensoren des Zelts, nicht die letzte Messung.
    await expect(page.locator('[data-audit="nachfuellen-live"]')).toContainText('Live jetzt')

    // ---- Der Normalfall zuerst: „Nur Wasser" ist vorgewählt und braucht nur Quelle und Liter.
    await expect(page.locator('[data-audit="nachfuellen-modus-wasser"]')).toHaveAttribute('aria-pressed', 'true')
    await expect(f.getByLabel('Aqua Vega A Menge'), '„Nur Wasser" zeigt trotzdem Plan-Zeilen.').toHaveCount(0)

    await page.locator('[data-audit="nachfuellen-modus-mix"]').click()
    await f.getByLabel('Womit hast du aufgefüllt?').selectOption('Tap')
    await f.getByLabel('Wie viel Wasser?').fill('10')

    // ---- Der Mischplan, auf die Liter gerechnet
    const zeileA = f.getByLabel('Aqua Vega A Menge')
    await expect(zeileA, 'Der Mischplan-Vorschlag kam nicht an — keine Zeile „Aqua Vega A".').toBeVisible({ timeout: 10_000 })
    const vorschlagA = await zeileA.inputValue()
    expect(Number(vorschlagA.replace(',', '.')), `Vorschlag für Aqua Vega A ist „${vorschlagA}".`).toBeGreaterThan(0)
    await f.getByLabel('Wie viel Wasser?').fill('20')
    await expect.poll(async () => Number((await zeileA.inputValue()).replace(/\./g, '').replace(',', '.'))).toBeGreaterThanOrEqual(2 * Number(vorschlagA.replace(/\./g, '').replace(',', '.')) - 1)
    await f.getByLabel('Wie viel Wasser?').fill('10')
    await expect(zeileA).toHaveValue(vorschlagA)

    // ---- Eigene Menge, zurück auf den Vorschlag, wieder die eigene
    await zeileA.fill('25')
    await expect(f.getByText('von dir geändert')).toBeVisible()
    await f.getByRole('button', { name: `↺ Vorschlag ${vorschlagA}` }).click()
    await expect(zeileA).toHaveValue(vorschlagA)
    await zeileA.fill('25')

    // ---- Gehört der Plan-Name keinem Artikel, wird nichts still gebucht — der eigene wird gewählt.
    // (Läuft der Wasserwechsel-Rundweg gleichzeitig, legt er einen Artikel „Aqua Vega A" an; dann hat
    // die Zeile schon einen. Die Rückfrage-Regel selbst prüft der unbekannte Zusatz weiter unten.)
    const zeileAKopf = f.locator('[data-audit="nachfuellen-zeile"]').filter({ has: page.getByLabel('Aqua Vega A Menge') })
    const fehlt = zeileAKopf.locator('[data-audit="nachfuellen-artikel-fehlt"]')
    if (await fehlt.count() > 0) {
      await expect(fehlt).toContainText('wird nicht gebucht')
      await zeileAKopf.getByLabel('Vorhandenen Artikel für Aqua Vega A wählen').selectOption({ label: ARTIKEL })
    }
    await expect(zeileAKopf.locator('[data-audit="nachfuellen-artikel-da"]')).toBeVisible()

    // ---- Ein vorhandener Zusatz: sofort gebucht
    await f.getByRole('button', { name: '+ Anderer …' }).click()
    await f.getByLabel('Name des Zusatzes').last().fill('Purolyt')
    await f.getByLabel('Purolyt Menge').fill('15')
    const purolyt = f.locator('[data-audit="nachfuellen-zeile"]').filter({ has: page.getByLabel('Purolyt Menge') })
    await expect(purolyt.locator('[data-audit="nachfuellen-artikel-da"]')).toContainText('wird als Verbrauch gebucht')

    // ---- Ein unbekannter Zusatz: erst die Rückfrage, nichts entsteht ohne „Ja"
    await f.getByRole('button', { name: '+ Anderer …' }).click()
    await f.getByLabel('Name des Zusatzes').last().fill(NEUER_ZUSATZ)
    await f.getByLabel(`${NEUER_ZUSATZ} Menge`).fill('7')
    const neu = f.locator('[data-audit="nachfuellen-zeile"]').filter({ has: page.getByLabel(`${NEUER_ZUSATZ} Menge`) })
    await expect(neu.locator('[data-audit="nachfuellen-artikel-fehlt"]')).toContainText('wird nicht gebucht')
    await artikelAnlegen(page, neu, false)
    expect(await artikelId(page, NEUER_ZUSATZ), 'Der Artikel entstand ohne Rückfrage.').toBeNull()
    await expect(page.locator('[data-audit="nachfuellen-eintrag"]')).toContainText(`${NEUER_ZUSATZ} · kein Artikel`)
    await artikelAnlegen(page, neu, true)
    await expect(neu.locator('[data-audit="nachfuellen-artikel-da"]')).toContainText('wird als Verbrauch gebucht')
    expect(await artikelId(page, NEUER_ZUSATZ)).not.toBeNull()

    // ---- Mischrechnung steht da, die Nachmessung ist geplant
    await expect(page.locator('[data-audit="nachfuellen-erwartung"]')).toContainText('Mischrechnung')
    await expect(page.locator('[data-audit="nachfuellen-eintrag"]')).toContainText('automatisch um')

    // ---- Zeitpunkt vor zwei Tagen, Verbrauch in Litern, Notiz
    await weitereAngabenOeffnen(page)
    const vorbei = new Date(Date.now() - 2 * 24 * 3600 * 1000)
    vorbei.setHours(10, 15, 0, 0)
    await f.getByLabel('Zeitpunkt').fill(feldzeit(vorbei))
    await f.getByLabel('Verbrauch seit dem letzten Mal').fill('14,5')
    const marke = `Nachfüll-Rundweg ${Date.now()}`
    await f.getByLabel('Notiz').fill(marke)
    // Selbst eingetragen hat Vorrang: die Automatik entfällt.
    await f.getByLabel('EC nachher').fill('1,61')
    await expect(page.locator('[data-audit="nachfuellen-eintrag"]')).toContainText('selbst eingetragen')

    const erster = await speichern(page)
    const gemeldet = new Date(erster.eintrag.performedAtUtc)
    expect(Math.abs(gemeldet.getTime() - vorbei.getTime()) / 3600000, `Eingetragen war ${feldzeit(vorbei)}, gespeichert ${erster.eintrag.performedAtUtc}.`).toBeLessThan(1)
    expect(erster.eintrag.kind).toBe('Addback')
    expect(erster.eintrag.litersAdded).toBe(10)
    expect(erster.eintrag.consumedLiters, 'Der Verbrauch wird in Litern gespeichert.').toBe(14.5)
    expect(erster.eintrag.ecAfter).toBe(1.61)
    expect(erster.nachher.solutionChange, 'Nachfüllen ist kein Lösungswechsel.').toBe(false)
    expect(erster.nachmessungStatus ?? null, 'Eigene Werte „nachher" haben Vorrang vor der Automatik.').toBeNull()
    const namen = (erster.buchungen as Array<{ artikelName: string; menge: number }>).map((b) => `${b.artikelName} ${b.menge}`)
    expect(namen).toContain('Purolyt 15')
    expect(namen).toContain(`${NEUER_ZUSATZ} 7`)
    expect(namen.some((n) => n.endsWith(' 25')), `Der eigene Wert 25 ml wurde nicht gebucht: ${namen.join(', ')}`).toBe(true)
    expect(namen).toContain('Leitungswasser 10')
    expect(erster.tagebuch.entryType).toBe('Feeding')
    expect(erster.tagebuch.body).toContain(marke)

    // ---- Zweites Speichern ohne Neuladen: frisches Formular, nur Wasser, Osmose (Artikel erst nach Rückfrage)
    await expect(page.getByText(/Nachfüllen gespeichert — mit/)).toBeVisible()
    await expect(f.getByLabel('Wie viel Wasser?'), 'Das zweite Nachfüllen erbte die Liter vom ersten.').toHaveValue('')
    await expect(page.locator('[data-audit="nachfuellen-modus-wasser"]')).toHaveAttribute('aria-pressed', 'true')
    await f.getByLabel('Womit hast du aufgefüllt?').selectOption('RO')
    await f.getByLabel('Wie viel Wasser?').fill('5')
    const osmose = page.locator('[data-audit="nachfuellen-artikel-fehlt"]').filter({ hasText: 'Osmosewasser' })
    if (await osmose.count() > 0) {
      await expect(osmose).toContainText('wird nicht gebucht')
      await artikelAnlegen(page, osmose, true)
    }
    await expect(page.locator('[data-audit="nachfuellen-artikel-da"]').filter({ hasText: 'Osmosewasser' })).toBeVisible()
    await expect(page.locator('[data-audit="nachfuellen-eintrag"]')).toContainText('Nachfüllen 5 L Osmosewasser')
    const zweiter = await speichern(page)
    expect(zweiter.eintrag.kind).toBe('TopOff')
    expect(zweiter.eintrag.waterUsed).toBe('RO')
    expect((zweiter.buchungen as Array<{ artikelName: string; menge: number }>).map((b) => `${b.artikelName} ${b.menge}`)).toEqual(['Osmosewasser 5'])
    // Mit Pegel-Annahme „danach wieder voll": Füllstand wird nur gespeichert, wenn der Nutzer ihn ändert.
    expect(zweiter.eintrag.newReservoirVolumeLiters ?? null).toBeNull()

    // ---- Neu laden, wiederfinden
    await oeffnen(page, growId)
    const liste = page.locator('[data-audit="addback-log-list"]')
    await expect(liste.getByText(marke, { exact: false })).toBeVisible()
    await expect(liste.locator(`[data-nachfuell-vorgang="${erster.id}"]`)).toContainText('Vorgang:')

    // ---- Löschen am Vorgang nimmt Messungen und Buchungen mit
    page.once('dialog', (d) => void d.accept())
    await liste.locator(`[data-nachfuell-vorgang="${erster.id}"]`).getByRole('button', { name: /entfernen/ }).click()
    await expect(liste.getByText(marke, { exact: false })).toHaveCount(0)
    const rest = await (await page.request.get(`/api/grows/${growId}/addback/vorgaenge`)).json() as Array<{ id: number }>
    expect(rest.map((v) => v.id)).toEqual([zweiter.id])
    expect((await page.request.get(`/api/measurements/${erster.nachher.id}`)).status(), 'Die Messung „nachher" blieb stehen.').toBe(404)
  })

  test('Rundweg: Nur Zusätze — ohne Wasser, ohne Liter, Volumen unverändert', async ({ page }) => {
    darfUeberspringen(eigener == null, 'Kein eigener Grow anlegbar — laeuft die App unter GROW_OS_URL?')
    await oeffnen(page, eigener!.growId, '&modus=zusatz')
    const f = formular(page)
    await expect(page.locator('[data-audit="nachfuellen-modus-zusatz"]')).toHaveAttribute('aria-pressed', 'true')
    // Kein Wasser-Block, kein Liter-Feld.
    await expect(f.getByLabel('Wie viel Wasser?')).toHaveCount(0)
    await expect(f.getByLabel('Womit hast du aufgefüllt?')).toHaveCount(0)

    // Ohne eine Zugabe lässt sich nichts eintragen — und die Meldung sagt, was fehlt.
    await page.locator('[data-audit="nachfuellen-speichern"]').click()
    await expect(page.getByText('Was hast du zugegeben?')).toBeVisible()

    await f.getByRole('button', { name: '+ Anderer …' }).click()
    await f.getByLabel('Name des Zusatzes').fill('Purolyt')
    await f.getByLabel('Purolyt Menge').fill('12')
    await expect(page.locator('[data-audit="nachfuellen-erwartung"]')).toContainText('unverändert')
    const vorgang = await speichern(page)
    expect(vorgang.eintrag.kind).toBe('Correction')
    expect(vorgang.eintrag.litersAdded ?? null, 'Ohne Wasser gibt es keine Liter.').toBeNull()
    expect(vorgang.eintrag.waterUsed ?? null, 'Ohne Wasser gibt es keine Wasserquelle.').toBeNull()
    expect((vorgang.buchungen as Array<{ artikelName: string; menge: number }>).map((b) => `${b.artikelName} ${b.menge}`)).toEqual(['Purolyt 12'])
    expect(vorgang.tagebuch.title).toBe('Zusätze zugegeben')
    expect(vorgang.tagebuch.body).toContain('Purolyt 12')
  })

  test('Rundweg: Mischung und eigene Wasserwerte', async ({ page }) => {
    darfUeberspringen(eigener == null, 'Kein eigener Grow anlegbar — laeuft die App unter GROW_OS_URL?')
    await oeffnen(page, eigener!.growId)
    const f = formular(page)
    await f.getByLabel('Wie viel Wasser?').fill('20')

    // Mischung: Anteil Osmose, die Liter teilen sich auf
    await f.getByLabel('Womit hast du aufgefüllt?').selectOption('Mixed')
    await expect(f.getByLabel('Anteil Osmosewasser', { exact: true })).toHaveValue('80')
    await expect(page.locator('[data-audit="nachfuellen-wasserwerte"]')).toContainText('16,0 L Osmose + 4,0 L Leitung')
    await f.getByLabel('Anteil Osmosewasser', { exact: true }).fill('50')
    await expect(page.locator('[data-audit="nachfuellen-wasserwerte"]')).toContainText('10,0 L Osmose + 10,0 L Leitung')

    // Eigene Werte: EC und pH eintragen, Härte und Temperatur dazu
    await f.getByLabel('Womit hast du aufgefüllt?').selectOption('eigen')
    await f.getByLabel('EC des Wassers').fill('0,45')
    await f.getByLabel('pH des Wassers').fill('7,2')
    await f.getByLabel('Härte des Wassers').fill('8')
    await expect(f.getByText('Eigenes Wasser hat keinen Artikel')).toBeVisible()
    const vorgang = await speichern(page)
    expect(vorgang.eintrag.wasserEcMsCm ?? vorgang.eintrag.waterEcMsCm).toBe(0.45)
    expect(vorgang.eintrag.notes).toContain('Wasser (eigene Werte)')
    expect(vorgang.eintrag.notes).toContain('Härte 8')
    expect(vorgang.buchungen).toHaveLength(0)

    // Ohne EC bei eigenem Wasser lässt sich nichts eintragen
    await f.getByLabel('Wie viel Wasser?').fill('5')
    await f.getByLabel('Womit hast du aufgefüllt?').selectOption('eigen')
    await page.locator('[data-audit="nachfuellen-speichern"]').click()
    await expect(page.getByText('Trag den EC deines Wassers ein')).toBeVisible()
  })

  test('Nachmessung: der Fork trägt EC und pH aus den Sensoren selbst ein — und die Vorgabe lässt sich merken', async ({ page }) => {
    darfUeberspringen(eigener == null, 'Kein eigener Grow anlegbar — laeuft die App unter GROW_OS_URL?')
    test.setTimeout(180_000)
    const growId = eigener!.growId
    await oeffnen(page, growId)
    const f = formular(page)
    await f.getByLabel('Wie viel Wasser?').fill('8')

    // Die Nachmessung fällt auf einen Zeitpunkt, für den es einen echten Rohwert gibt: der Zeitpunkt liegt
    // 30 Minuten vor dem neuesten Wert im Verlauf des Zelts. Ein fester „vor einer Stunde" hinge am Alter
    // der Demo-App — deren Sensorverlauf endet beim Start.
    const verlauf = await (await page.request.get(`/api/tents/${eigener!.tentId}/history?metrics=reservoir-ec&days=2&resolution=raw`)).json() as { series: Array<{ points: Array<{ t: string }> }> }
    const punkte = verlauf.series[0]?.points ?? []
    darfUeberspringen(punkte.length === 0, 'Das Zelt hat keinen Sensorverlauf — ohne Rohwerte gibt es nichts nachzumessen.')
    const neuester = new Date(punkte[punkte.length - 1].t)
    await weitereAngabenOeffnen(page)
    await f.getByRole('button', { name: 'vor 1 Std.' }).click()
    await expect(f.getByLabel('Zeitpunkt')).not.toHaveValue('')
    await f.getByLabel('Zeitpunkt').fill(feldzeit(new Date(neuester.getTime() - 30 * 60_000)))
    await expect(page.locator('[data-audit="nachfuellen-nachmessung"]')).toContainText('liegt schon in der Vergangenheit')

    // Minuten ändern und als Standard merken
    await f.getByRole('button', { name: '30 min', exact: true }).click()
    await expect(f.getByLabel('Minuten bis zur Nachmessung')).toHaveValue('30')
    await f.getByLabel('als meinen Standard merken').check()
    const gemerkt = page.waitForResponse((r) => /\/api\/addback\/einstellungen$/.test(r.url()) && r.request().method() === 'PUT')
    const vorgang = await speichern(page)
    expect((await gemerkt).ok()).toBe(true)
    expect(vorgang.nachmessungStatus).toBe('offen')
    expect(await (await page.request.get('/api/addback/einstellungen')).json()).toEqual({ nachmessungAutomatisch: true, nachmessungMinuten: 30 })

    // Die nächste Seite startet mit der gemerkten Vorgabe
    await oeffnen(page, growId)
    await expect(formular(page).getByLabel('Minuten bis zur Nachmessung')).toHaveValue('30')

    // Der Hintergrundtakt (alle 30 s) schließt den Auftrag ab
    await expect.poll(async () => {
      const v = await (await page.request.get(`/api/grows/${growId}/addback/vorgaenge/${vorgang.id}`)).json() as { nachmessungStatus: string }
      return v.nachmessungStatus
    }, { timeout: 90_000, intervals: [3_000], message: 'Die Nachmessung wurde nicht abgeschlossen — läuft der Takt?' }).not.toBe('offen')
    const danach = await (await page.request.get(`/api/grows/${growId}/addback/vorgaenge/${vorgang.id}`)).json()
    expect(danach.nachmessungStatus, `Nachmessung: ${danach.nachmessungHinweis ?? ''}`).toBe('erledigt')
    expect(danach.nachher.source).toBe('HomeAssistant')
    expect(danach.nachher.reservoirEc).toBeGreaterThan(0)
    expect(danach.eintrag.ecAfter).toBe(danach.nachher.reservoirEc)
    expect(danach.tagebuch.body).toContain('Nachmessung (automatisch')

    // Die Vorgabe zurücksetzen (der Abräumer macht es zur Sicherheit auch)
    await page.request.put('/api/addback/einstellungen', { data: { nachmessungAutomatisch: true, nachmessungMinuten: 15 } })
  })

  test('Vorbelegung per Link — so öffnet das Tagebuch „Nachfüllen eintragen"', async ({ page }) => {
    darfUeberspringen(eigener == null, 'Kein eigener Grow anlegbar — laeuft die App unter GROW_OS_URL?')
    const growId = eigener!.growId
    const zeitpunkt = new Date(Date.now() - 26 * 3600 * 1000)
    zeitpunkt.setSeconds(0, 0)
    const marke = `Sprung ${Date.now()}`

    // Über die alte Adresse: die Weiterleitung muss die Vorbelegung mitnehmen.
    const suche = new URLSearchParams({ zeitpunkt: zeitpunkt.toISOString(), ecVorher: '1.75', ecNachher: '1.61', phVorher: '6.02', liter: '20', notiz: marke })
    await page.goto(`/grows/${growId}/addback?${suche.toString()}`, { waitUntil: 'networkidle' })
    await expect(page).toHaveURL(new RegExp(`/addback\\?growId=${growId}&`))
    const f = formular(page)
    await expect(page.locator('[data-audit="nachfuellen-vorbelegt"]')).toBeVisible()
    await expect(f.getByLabel('Wie viel Wasser?')).toHaveValue('20')
    await expect(f.getByLabel('EC nachher')).toHaveValue('1,61')
    await expect(f.getByLabel('Zeitpunkt')).toHaveValue(feldzeit(zeitpunkt))
    await expect(f.getByLabel('Notiz')).toHaveValue(marke)
    // Die Werte nachher sind da — die Automatik entfällt.
    await expect(page.locator('[data-audit="nachfuellen-eintrag"]')).toContainText('selbst eingetragen')

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

    // Nach dem Speichern ist die Vorbelegung verbraucht: das nächste Formular beginnt leer.
    await expect(page.locator('[data-audit="nachfuellen-vorbelegt"]')).toHaveCount(0)

    // Wiederfinden nach dem Neuladen — ohne Vorbelegung in der Adresse.
    await page.goto(`/addback?growId=${growId}&vorgang=${vorgang.id}`, { waitUntil: 'networkidle' })
    await expect(page.locator(`[data-nachfuell-vorgang="${vorgang.id}"]`)).toHaveClass(/is-markiert/)
  })
})
