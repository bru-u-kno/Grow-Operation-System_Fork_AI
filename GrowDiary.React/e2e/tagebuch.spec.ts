import { expect, test, type Page } from '@playwright/test'
import { backendAntwortet, darfUeberspringen } from './pflicht'
import { gibSchloss, nimmSchloss } from './schloss'

/**
 * Das Grow-Tagebuch (A-006) an der laufenden App mit Demobestand.
 *
 * Der Bestand trägt GENAU eine Auffälligkeit: ein Nachfüllen ohne Eintrag in
 * Zelt 1, 1–6 Tage zurück (`Demoverlauf.NachfuellenOhneEintrag`, gehalten von
 * `DemobestandStimmigTests`). Alles, was hier einträgt, räumt hinterher auf —
 * über die Löschwege der Oberfläche, wo es sie gibt.
 *
 * Gefahren wird jede Bedienung zweimal (CLAUDE.md: die Reparatur einmal
 * wiederholen): Klappen auf-zu-auf, „War nichts" mit Rückgängig und danach
 * noch einmal mit Neuladen.
 */

test.describe.configure({ mode: 'serial' })
test.beforeEach(async () => { await nimmSchloss() })
test.afterEach(() => { gibSchloss() })

const MARKE = `Rundweg-Tagebuch ${new Date().toISOString().slice(11, 19)}`
const ADRESSE = '/tagebuch?growId=1'
/** Was „War nichts" ausgeblendet hat — für das Aufräumen. */
const verworfeneIds: number[] = []

async function oeffnen(seite: Page) {
  darfUeberspringen(!(await backendAntwortet(seite.request)), 'Kein Backend unter GROW_OS_URL.')
  await seite.goto(ADRESSE, { waitUntil: 'networkidle' })
  await expect(seite.locator('[data-audit="tagebuch-tag"]').first()).toBeVisible()
}

const auffaellig = (seite: Page) => seite.locator('[data-audit="tagebuch-ereignis-auffaellig"]')

/** Auf die Antwort warten und den gesendeten Rumpf zurückgeben. */
async function gesendet(seite: Page, methode: string, muster: RegExp, handlung: () => Promise<void>) {
  const antwort = seite.waitForResponse((r) => r.request().method() === methode && muster.test(r.url()))
  await handlung()
  const r = await antwort
  expect(r.ok(), `${methode} ${r.url()} antwortete ${r.status()}`).toBeTruthy()
  return JSON.parse(r.request().postData() ?? '{}') as Record<string, unknown>
}

test('Menü: „Tagebuch" ersetzt Messungen und Journal — beide bleiben über das Tagebuch erreichbar', async ({ page }) => {
  await oeffnen(page)
  // Die Seitenleiste (Desktop-Projekt) — die Links IN der Seite zählen nicht als Menü.
  const menue = page.locator('aside.v1-desktop-nav')
  await expect(menue.getByRole('link', { name: 'Tagebuch', exact: true })).toBeVisible()
  // Kein Menüpunkt heißt mehr „Verlauf", „Messungen" oder „Journal & Fotos".
  for (const alt of ['Verlauf', 'Messungen', 'Journal & Fotos']) {
    await expect(menue.getByRole('link', { name: alt, exact: true }), `Menüpunkt „${alt}" steht noch da.`).toHaveCount(0)
  }
  // Selbsttest: die Leiste trägt überhaupt Einträge.
  expect(await menue.getByRole('link').count()).toBeGreaterThan(15)

  await page.getByRole('link', { name: 'Messungen als Tabelle' }).click()
  await expect(page).toHaveURL(/\/messungen\?growId=1/)
  await expect(page.getByRole('heading', { name: 'Messungen' })).toBeVisible()
  await page.goBack()
  await page.getByRole('link', { name: 'Journal & Fotos' }).click()
  await expect(page).toHaveURL(/\/journal\?growId=1/)
  await expect(page.locator('[data-audit="journal-stream"]')).toBeVisible()
})

test('Sensorkurven: beim Öffnen zu, auf-zu-auf ohne Neuladen, nach dem Neuladen wieder zu', async ({ page }) => {
  await oeffnen(page)
  const tage = page.locator('[data-audit="tagebuch-tag"]')
  expect(await tage.count(), 'Der Bestand zeigt keine Tage — die Prüfung sähe nichts.').toBeGreaterThanOrEqual(3)

  // Jeder Tag ist zu, und eingeklappt steht keine Zahlenzeile.
  const klappen = page.locator('[data-audit="tagebuch-sensorkurven"] button[aria-expanded]')
  await expect(klappen.first()).toHaveAttribute('aria-expanded', 'false')
  expect(await page.locator('[aria-expanded="true"]').count()).toBe(0)
  await expect(page.locator('[data-audit="tagebuch-kurve-reservoir-ec"]')).toHaveCount(0)

  // Der Tag mit der Auffälligkeit sagt das auch zugeklappt.
  const tagMitBefund = tage.filter({ has: auffaellig(page) })
  await expect(tagMitBefund.locator('.ls-bilanz.is-warn')).toHaveText('1 Auffälligkeit')

  const erste = klappen.first()
  await erste.click()
  await expect(erste).toHaveAttribute('aria-expanded', 'true')
  const kurven = tage.first().locator('[data-audit^="tagebuch-kurve-"]')
  await expect(kurven).toHaveCount(6)
  await expect(tage.first().locator('.tb-legende')).toContainText('Licht an')
  await expect(tage.first().locator('svg.tb-kurve').first()).toBeVisible()

  // Zu — und ein zweites Mal auf, ohne Neuladen.
  await erste.click()
  await expect(kurven).toHaveCount(0)
  await page.mouse.wheel(0, 800)
  await erste.click()
  await expect(kurven).toHaveCount(6)

  // Das letzte Tagebuch-Klappen klappt auch (erster und letzter Eintrag).
  const letzte = klappen.last()
  await letzte.scrollIntoViewIfNeeded()
  await letzte.click()
  await expect(letzte).toHaveAttribute('aria-expanded', 'true')

  // Nicht gespeichert: nach dem Neuladen ist wieder alles zu.
  await page.reload({ waitUntil: 'networkidle' })
  await expect(page.locator('[data-audit="tagebuch-sensorkurven"] button[aria-expanded="true"]')).toHaveCount(0)
})

test('Schalter „Alle Kurven": alle auf, alle zu — zweimal', async ({ page }) => {
  await oeffnen(page)
  const schalter = page.locator('[data-audit="tagebuch-alle-kurven"]')
  const anzahl = await page.locator('[data-audit="tagebuch-tag"]').count()
  for (let runde = 0; runde < 2; runde++) {
    await schalter.click()
    await expect(schalter).toHaveAttribute('aria-checked', 'true')
    await expect(page.locator('[data-audit="tagebuch-sensorkurven"] button[aria-expanded="true"]')).toHaveCount(anzahl)
    await schalter.click()
    await expect(schalter).toHaveAttribute('aria-checked', 'false')
    await expect(page.locator('[data-audit="tagebuch-sensorkurven"] button[aria-expanded="true"]')).toHaveCount(0)
  }
})

test('Filter: Messwerte, Wasser, Notizen & Fotos, Alles', async ({ page }) => {
  await oeffnen(page)
  const zeilen = page.locator('[data-audit^="tagebuch-ereignis-"]')
  const alle = await zeilen.count()

  await page.locator('[data-audit="tagebuch-filter-messwerte"]').click()
  const arten = await zeilen.evaluateAll((els) => [...new Set(els.map((e) => e.getAttribute('data-audit')))])
  expect(arten.sort()).toEqual(['tagebuch-ereignis-auffaellig', 'tagebuch-ereignis-messung'])

  await page.locator('[data-audit="tagebuch-filter-wasser"]').click()
  await expect(page.locator('[data-audit="tagebuch-ereignis-wechsel"]').first()).toBeVisible()
  await expect(page.locator('[data-audit="tagebuch-ereignis-notiz"]').filter({ hasText: 'Blattränder' })).toHaveCount(0)

  await page.locator('[data-audit="tagebuch-filter-notizen"]').click()
  await expect(page.locator('[data-audit="tagebuch-sensorkurven"]')).toHaveCount(0)
  await expect(page.locator('[data-audit="tagebuch-ereignis-messung"]')).toHaveCount(0)

  await page.locator('[data-audit="tagebuch-filter-alles"]').click()
  await expect(zeilen).toHaveCount(alle)
})

test('Auffällig: „War nichts" — Rückgängig, dann wirklich, und nach dem Neuladen weg', async ({ page }) => {
  await oeffnen(page)
  await expect(auffaellig(page)).toHaveCount(1)
  const zeile = auffaellig(page)
  await expect(zeile).toContainText('dazu ist nichts eingetragen')
  await expect(zeile).toContainText('Gemeldet ab 5 %')

  await gesendet(page, 'PUT', /\/api\/tagebuch\/auffaelligkeiten\/\d+$/,
    () => zeile.locator('[data-audit="tagebuch-war-nichts"]').click())
  await expect(zeile.getByRole('status')).toContainText('Ausgeblendet')
  const rumpf = await gesendet(page, 'PUT', /\/api\/tagebuch\/auffaelligkeiten\/\d+$/,
    () => zeile.locator('[data-audit="tagebuch-war-nichts-zurueck"]').click())
  expect(rumpf.verworfen).toBe(false)
  await expect(zeile.locator('[data-audit="tagebuch-war-nichts"]')).toBeVisible()

  // Zweites Mal — und neu laden: serverseitig gemerkt.
  // Eine Zeile kann mehrere Befunde tragen (EC und Wasserstand) — jede Id merken.
  const mitschreiben = (r: import('@playwright/test').Response) => {
    if (r.request().method() === 'PUT' && /\/api\/tagebuch\/auffaelligkeiten\/\d+$/.test(r.url())) verworfeneIds.push(Number(r.url().split('/').pop()))
  }
  page.on('response', mitschreiben)
  await zeile.locator('[data-audit="tagebuch-war-nichts"]').click()
  await expect(zeile.getByRole('status')).toContainText('Ausgeblendet')
  page.off('response', mitschreiben)
  await page.reload({ waitUntil: 'networkidle' })
  await expect(auffaellig(page)).toHaveCount(0)

  // Aufräumen: wieder zeigen — über dieselbe Schnittstelle, mit der gemerkten Id.
  for (const id of verworfeneIds) {
    const r = await page.request.put(`/api/tagebuch/auffaelligkeiten/${id}`, { data: { verworfen: false } })
    expect(r.ok()).toBeTruthy()
  }
  verworfeneIds.length = 0
  await page.reload({ waitUntil: 'networkidle' })
  await expect(auffaellig(page)).toHaveCount(1)
  // Alle Befunde der Zeile sind zurück — nicht nur der erste.
  await expect(auffaellig(page)).toContainText('Wasserstand')
})

test('Rundweg: AuffaelligAktionen — Notiz dazu und Nachfüllen eintragen, beide erklären den Sprung', async ({ page }) => {
  await oeffnen(page)
  await expect(auffaellig(page)).toHaveCount(1)

  // --- Notiz dazu: vorbefüllt, abschicken, nach dem Neuladen da, Sprung erklärt.
  await auffaellig(page).locator('[data-audit="tagebuch-notiz-dazu"]').click()
  const notiz = page.locator('[data-audit="tagebuch-notiz-form"]')
  await expect(notiz.getByLabel('Titel', { exact: true })).toHaveValue(/^EC-Sprung \d\d:\d\d–\d\d:\d\d Uhr$/)
  await expect(notiz.locator('textarea')).toHaveValue(/Vom Sensor erkannt: EC von \d,\d\d auf \d,\d\d/)
  await notiz.getByLabel('Titel', { exact: true }).fill(MARKE)
  const notizRumpf = await gesendet(page, 'POST', /\/api\/grows\/1\/journal$/,
    () => notiz.getByRole('button', { name: 'Notiz speichern' }).click())
  expect(notizRumpf.title).toBe(MARKE)
  expect(notizRumpf.entryType).toBe('Observation')
  await page.reload({ waitUntil: 'networkidle' })
  await expect(auffaellig(page)).toHaveCount(0)
  const neueNotiz = page.locator('[data-audit="tagebuch-ereignis-notiz"]').filter({ hasText: MARKE })
  await expect(neueNotiz).toHaveCount(1)

  // Aufräumen über „Entfernen" im Tagebuch — danach steht die Auffälligkeit wieder da.
  page.once('dialog', (d) => void d.accept())
  await gesendet(page, 'DELETE', /\/api\/journal\/\d+$/,
    () => neueNotiz.getByRole('button', { name: `Eintrag „${MARKE}" entfernen` }).click())
  await page.reload({ waitUntil: 'networkidle' })
  await expect(auffaellig(page)).toHaveCount(1)

  // --- Nachfüllen eintragen: führt auf die Addback-Seite (A-006, Etappe 3),
  // vorbelegt mit Zeitpunkt und EC davor/danach — ein Vorgang, kein zweites Formular.
  await auffaellig(page).locator('[data-audit="tagebuch-nachfuellen"]').click()
  await expect(page).toHaveURL(/\/addback\?growId=1&zeitpunkt=/)
  const formular = page.locator('[data-audit="nachfuellen-formular"]')
  await expect(page.locator('[data-audit="nachfuellen-vorbelegt"]')).toBeVisible()
  // Nur Wasser ist vorgewählt; die Liter trägt der Nutzer ein, der Rest steht schon da.
  await formular.getByLabel('Wie viel Wasser?').fill('12,5')
  await expect(formular.getByLabel('EC nachher')).toHaveValue(/^\d,\d+$/)
  await expect(formular.getByLabel('Notiz')).toHaveValue(/Nachgetragen aus dem Tagebuch/)
  await formular.getByLabel('Notiz').fill(MARKE)
  const rumpf = await gesendet(page, 'POST', /\/api\/grows\/1\/addback\/vorgaenge$/,
    () => page.locator('[data-audit="nachfuellen-speichern"]').click())
  expect(rumpf.art).toBe('TopOff')
  expect(rumpf.liter).toBe(12.5)
  const vorher = rumpf.vorher as { reservoirEc: number; herkunft: string }
  const nachher = rumpf.nachher as { reservoirEc: number }
  expect(vorher.herkunft).toBe('Sensor')
  expect(vorher.reservoirEc - nachher.reservoirEc).toBeGreaterThan(0.1)

  await oeffnen(page)
  await expect(auffaellig(page)).toHaveCount(0)
  const nachgefuellt = page.locator('[data-audit="tagebuch-ereignis-addback"]').filter({ hasText: MARKE })
  await expect(nachgefuellt).toContainText('Nachfüllen 12,5 L')
  // Gebündelt wie der Wechsel: Vorher/Nachher, Posten, „Vorgang öffnen" — keine eigene Messzeile.
  await expect(nachgefuellt.getByRole('table', { name: 'Vorher und nachher' })).toBeVisible()
  await expect(nachgefuellt).toContainText(/Posten im Verbrauch gebucht/)
  await expect(nachgefuellt.getByRole('link', { name: 'Vorgang öffnen' })).toHaveAttribute('href', /\/addback\?growId=1&vorgang=\d+/)

  // Aufräumen über „Entfernen" — der Löschweg gehört zum Rundweg und nimmt den ganzen Vorgang.
  page.once('dialog', (d) => void d.accept())
  await gesendet(page, 'DELETE', /\/api\/grows\/1\/addback\/logs\/\d+$/,
    () => nachgefuellt.getByRole('button', { name: /entfernen$/ }).click())
  await page.reload({ waitUntil: 'networkidle' })
  await expect(auffaellig(page)).toHaveCount(1)
  const rest = await (await page.request.get('/api/grows/1/addback/vorgaenge')).json() as Array<{ eintrag: { notes: string | null } | null }>
  expect(rest.filter((v) => v.eintrag?.notes === MARKE)).toEqual([])
})

test('Rundweg: NotizBearbeiten — eine Notiz im Tagebuch korrigieren, zweimal', async ({ page }) => {
  await oeffnen(page)
  const angelegt = await page.request.post('/api/grows/1/journal', {
    data: { title: MARKE, body: 'erste Fassung', entryType: 'Note', occurredAtLocal: new Date(Date.now() - 3600_000 - new Date().getTimezoneOffset() * 60000).toISOString().slice(0, 16) },
  })
  expect(angelegt.ok()).toBeTruthy()
  const id = (await angelegt.json() as { id: number }).id
  try {
    await page.reload({ waitUntil: 'networkidle' })
    const zeile = page.locator('[data-audit="tagebuch-ereignis-notiz"]').filter({ hasText: MARKE })
    for (const fassung of ['zweite Fassung', 'dritte Fassung']) {
      await zeile.getByRole('button', { name: `Eintrag „${MARKE}" bearbeiten` }).click()
      const form = page.locator('[data-audit="tagebuch-notiz-bearbeiten"]')
      await expect(form.getByLabel('Titel', { exact: true })).toHaveValue(MARKE)
      await form.locator('textarea').fill(fassung)
      const rumpf = await gesendet(page, 'PUT', new RegExp(`/api/journal/${id}$`),
        () => form.getByRole('button', { name: 'Änderungen speichern' }).click())
      expect(rumpf.body).toBe(fassung)
      expect(rumpf.occurredAtLocal, 'Unveränderter Zeitpunkt ging mit.').toBeNull()
      await expect(zeile).toContainText(fassung)
    }
    await page.reload({ waitUntil: 'networkidle' })
    await expect(page.locator('[data-audit="tagebuch-ereignis-notiz"]').filter({ hasText: MARKE })).toContainText('dritte Fassung')
  } finally {
    await page.request.delete(`/api/journal/${id}`)
  }
})


test('Handy: alles aufgeklappt, Formular offen — nichts ragt über den Rand (320–768 px, beide Themen)', async ({ page }) => {
  // handy-zuschnitt misst die Seite im Ausgangszustand — die Kurven und das
  // Notiz-Formular sind dann zu. Hier im aufgeklappten Zustand.
  await oeffnen(page)
  const gemessen: string[] = []
  for (const thema of ['dark', 'light']) {
    await page.evaluate((t) => { localStorage.setItem('growos.theme', t) }, thema)
    for (const breite of [320, 360, 390, 768]) {
      await page.setViewportSize({ width: breite, height: 800 })
      await page.goto(ADRESSE, { waitUntil: 'networkidle' })
      await page.locator('[data-audit="tagebuch-alle-kurven"]').click()
      await expect(page.locator('[data-audit="tagebuch-kurve-reservoir-ec"]').first()).toBeVisible()
      // „Nachfüllen eintragen" führt seit A-006 Etappe 3 auf /addback — offen bleibt hier die Notiz.
      await auffaellig(page).locator('[data-audit="tagebuch-notiz-dazu"]').click()
      await expect(page.locator('[data-audit="tagebuch-notiz-form"]')).toBeVisible()
      const befund = await page.evaluate(() => {
        const raus: string[] = []
        const rand = document.documentElement.clientWidth
        if (document.documentElement.scrollWidth > rand) raus.push(`Seite ${document.documentElement.scrollWidth} > ${rand}`)
        // Den Text messen, nicht den Kasten (CLAUDE.md): jede Textstelle im Tagebuch.
        for (const el of document.querySelectorAll('.tb-tag *')) {
          for (const knoten of el.childNodes) {
            if (knoten.nodeType !== Node.TEXT_NODE || !knoten.textContent?.trim()) continue
            const r = document.createRange()
            r.selectNodeContents(knoten)
            for (const box of r.getClientRects()) {
              // Gegen den engsten Rahmen: Zelle, Kachel, Vorgang, sonst der Tag.
              const rahmen = el.closest('.tb-zelle, .tb-kachel, .tb-vorgang, .tb-tag')!.getBoundingClientRect()
              if (box.right > rahmen.right + 0.5 || box.left < rahmen.left - 0.5) raus.push(`„${knoten.textContent.trim().slice(0, 30)}" ragt aus seinem Rahmen`)
            }
          }
        }
        return [...new Set(raus)]
      })
      gemessen.push(`${thema}/${breite}`)
      expect(befund, `${thema} ${breite} px`).toEqual([])
    }
  }
  expect(gemessen).toHaveLength(8)
})

test('Wasserwechsel aus dem Ablauf: ein Vorgang im Tagebuch — Werte, Posten, Link, und Entfernen nimmt alles mit', async ({ page }) => {
  await oeffnen(page)
  // Gestern 10:00 Ortszeit — fern vom Nachfüllen ohne Eintrag, damit es nichts erklärt.
  const gestern = new Date(Date.now() - 86_400_000)
  const lokal = `${gestern.getFullYear()}-${String(gestern.getMonth() + 1).padStart(2, '0')}-${String(gestern.getDate()).padStart(2, '0')}T10:00`
  const angelegt = await page.request.post('/api/grows/1/wasserwechsel', {
    data: {
      zeitpunktLokal: lokal, art: 'Full', liter: 100, wasser: 'Tap', erinnerungNeuStarten: false,
      vorher: { zeitpunktLokal: lokal, herkunft: 'hand', reservoirEc: 1.66, reservoirPh: 6.08 },
      nachher: { zeitpunktLokal: lokal, reservoirEc: 1.52, reservoirPh: 5.86, orpMv: 444, dissolvedOxygenMgL: 8.2 },
      buchungen: [{ wasser: 'Tap', menge: 100 }],
      tagebuch: { titel: MARKE, text: 'Vorgang aus dem Rundweg' },
    },
  })
  expect(angelegt.ok(), await angelegt.text()).toBeTruthy()
  const vorgang = await angelegt.json() as { id: number; wechsel: { id: number } }
  let entfernt = false
  try {
    await page.reload({ waitUntil: 'networkidle' })
    const zeile = page.locator(`[data-schluessel="wechsel-${vorgang.wechsel.id}"]`)
    await expect(zeile).toContainText('Vorgang')
    await expect(zeile.locator('.tb-vt')).toContainText('444')
    await expect(zeile.locator('.tb-vt')).toContainText('8,2')
    await expect(zeile.locator('.tb-vt')).toContainText('1,66')
    await expect(zeile.locator('.tb-posten')).toContainText('100 L')
    await expect(zeile.getByRole('link', { name: 'Vorgang öffnen' })).toHaveAttribute('href', new RegExp(`vorgang=${vorgang.id}`))
    // Das Journal des Vorgangs steht IM Vorgang, nicht noch einmal einzeln — und ohne eigenes Entfernen.
    await expect(zeile).toContainText(MARKE)
    await expect(page.locator('[data-audit="tagebuch-ereignis-notiz"]').filter({ hasText: MARKE })).toHaveCount(0)
    await expect(zeile.getByRole('button', { name: `Eintrag „${MARKE}" entfernen` })).toHaveCount(0)

    await zeile.getByRole('link', { name: 'Vorgang öffnen' }).click()
    await expect(page).toHaveURL(new RegExp(`/wasserwechsel\\?growId=1&vorgang=${vorgang.id}`))
    await page.goBack({ waitUntil: 'networkidle' })

    page.once('dialog', (d) => void d.accept())
    await gesendet(page, 'DELETE', new RegExp(`/api/grows/1/wasserwechsel/${vorgang.id}$`),
      () => page.locator(`[data-schluessel="wechsel-${vorgang.wechsel.id}"]`).getByRole('button', { name: /mit allem entfernen$/ }).click())
    entfernt = true
    await page.reload({ waitUntil: 'networkidle' })
    await expect(page.locator(`[data-schluessel="wechsel-${vorgang.wechsel.id}"]`)).toHaveCount(0)
    await expect(page.getByText(MARKE)).toHaveCount(0)
    expect((await page.request.get(`/api/grows/1/wasserwechsel/${vorgang.id}`)).status()).toBe(404)
  } finally {
    if (!entfernt) await page.request.delete(`/api/grows/1/wasserwechsel/${vorgang.id}`)
  }
})
