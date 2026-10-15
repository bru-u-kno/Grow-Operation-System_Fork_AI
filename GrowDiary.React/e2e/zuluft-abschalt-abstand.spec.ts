import { test, expect, type Page } from '@playwright/test'
import { backendAntwortet, darfUeberspringen } from './pflicht'
import { gibSchloss, nimmSchloss } from './schloss'

/**
 * Zuluft: Der Abschalt-Abstand ist einstellbar und die Seite sagt, wann der Lüfter nur hält.
 *
 * <b>Der Anlass (15.10.2026).</b> Die Seite zeigte „Mindest-Differenz 1" und „saugt", die Differenz
 * stand bei 0,73 — der Nutzer hielt das für einen Fehler. Es ist die Hysterese: an ab 1,0, aus
 * erst unter 0,5, dazwischen bleibt der Zustand. Die 0,5 stand fest im Rechenwert in Home
 * Assistant und war weder zu sehen noch einzustellen.
 *
 * <b>Was hier geprüft wird.</b> Das Feld, das Band, das dem Feld folgt, das Speichern mit
 * Neuladen, die Ablehnung eines zu großen Werts am Feld — und der Zustand „saugt · hält" gegen
 * einen Livestand. Den Livestand liefert der Demobestand nicht (kein Zuluft-Fühler); er wird
 * über die Antwort der Seite gesetzt, gerendert wird die gebaute App.
 *
 * <b>Schreibt</b> die Zuluft-Einstellungen und stellt sie am Ende zurück.
 */

test.beforeEach(async ({ request }) => {
  darfUeberspringen(!(await backendAntwortet(request)), 'kein Backend unter GROW_OS_URL — diese Fälle bedienen die laufende App')
  await nimmSchloss()
})

test.afterEach(() => { gibSchloss() })

const FELD = 'Abschalt-Abstand'

/** Ein Livestand wie am 15.10.2026, 16:39: Bedarf an seit 03:45, geglättet 0,78, roh 0,73. */
const LIVE = {
  haErreichbar: true, differenzGm3: 0.73, differenzRegelGm3: 0.78, aussenAbsolutGm3: 6.1, kellerAbsolutGm3: 6.9,
  aussenTempC: 11.2, aussenRhProzent: 71, kellerTempC: 17.4, kellerRhProzent: 46,
  bedarf: true, zielstufe: 6, istStufe: 6, portAn: true, portOnline: true, automatikAn: true,
  sperreRestMin: 0, letzterWechsel: '2026-10-14T20:12:00', zeltTempC: 23.1, pauseZeltKalt: false,
}

async function zuluftOeffnen(page: Page): Promise<void> {
  const antwort = await page.goto('/steuerung/zuluft', { waitUntil: 'networkidle' })
  darfUeberspringen(antwort == null || antwort.status() >= 400, '/steuerung/zuluft antwortet nicht — läuft die App unter GROW_OS_URL?')
  const da = await page.getByLabel(FELD, { exact: true }).waitFor({ timeout: 15_000 }).then(() => true, () => false)
  darfUeberspringen(!da, `/steuerung/zuluft: kein Feld „${FELD}" — der Demobestand legt diese Steuerung an.`)
}

test('das Feld steht auf 0,5 und das Band folgt der Eingabe', async ({ page }) => {
  await zuluftOeffnen(page)
  const feld = page.getByLabel(FELD, { exact: true })
  await expect(feld).toBeVisible()
  await expect(feld).toHaveValue('0.5')

  // Das Band ist ein Bild mit Satz — Farbe allein trägt nichts.
  await expect(page.getByRole('img', { name: /Aus unter 0,5, an ab 1,0 g\/m³, dazwischen bleibt er, wie er ist/ })).toBeVisible()

  await feld.fill('0.3')
  await expect(page.getByRole('img', { name: /Aus unter 0,7, an ab 1,0/ })).toBeVisible()

  // 0 = kein Haltebereich: eine einzige Grenze.
  await feld.fill('0')
  await expect(page.getByRole('img', { name: /An und aus bei 1,0 g\/m³/ })).toBeVisible()
})

test('Speichern behält den Wert über das Neuladen — und er wird zurückgestellt', async ({ page, request }) => {
  const vorher = await (await request.get('/api/steuerung/zuluft')).json()
  const alt = vorher.einstellungen.abschaltAbstandGm3 as number
  try {
    await zuluftOeffnen(page)
    const feld = page.getByLabel(FELD, { exact: true })
    await feld.fill('0.3')

    const gespeichert = page.waitForResponse((r) => r.url().endsWith('/api/steuerung/zuluft') && r.request().method() === 'PUT')
    await page.getByRole('button', { name: 'Speichern' }).click()
    expect((await gespeichert).status()).toBe(200)

    const danach = await (await request.get('/api/steuerung/zuluft')).json()
    expect(danach.einstellungen.abschaltAbstandGm3).toBe(0.3)

    await page.reload({ waitUntil: 'networkidle' })
    await expect(page.getByLabel(FELD, { exact: true })).toHaveValue('0.3')
  } finally {
    const aktuell = await (await request.get('/api/steuerung/zuluft')).json()
    await request.put('/api/steuerung/zuluft', { data: { ...aktuell.einstellungen, abschaltAbstandGm3: alt } })
  }
})

test('ein Abstand über der Mindest-Differenz wird am Feld abgelehnt', async ({ page, request }) => {
  const vorher = await (await request.get('/api/steuerung/zuluft')).json()
  await zuluftOeffnen(page)
  const feld = page.getByLabel(FELD, { exact: true })
  await feld.fill('1.5') // Mindest-Differenz ist 1 — die Aus-Grenze läge unter 0.
  await page.getByRole('button', { name: 'Speichern' }).click()

  const markierung = feld.locator('xpath=ancestor::*[contains(@class,"st-feldzeile")][1]').locator('.st-fehler')
  await expect(markierung).toHaveText('Abschalt-Abstand darf nicht größer sein als die Mindest-Differenz.')
  await expect(markierung).toBeVisible()

  // Abgelehnt heißt: nichts gespeichert.
  const danach = await (await request.get('/api/steuerung/zuluft')).json()
  expect(danach.einstellungen.abschaltAbstandGm3).toBe(vorher.einstellungen.abschaltAbstandGm3)
})

test('der Zustand sagt „saugt · hält", solange die Differenz im Haltebereich liegt', async ({ page }) => {
  const mitLive = async (live: Record<string, unknown>) => {
    await page.route('**/api/steuerung/zuluft', async (route) => {
      if (route.request().method() !== 'GET') { await route.continue(); return }
      const echt = await (await route.fetch()).json()
      await route.fulfill({ json: { ...echt, live: { ...LIVE, ...live } } })
    })
    await zuluftOeffnen(page)
  }
  const zustand = () => page.locator('.v1-stat', { hasText: 'Zustand' })

  // Im Haltebereich: Regelwert 0,78 (roh 0,73), Bedarf an, Lüfter läuft.
  await mitLive({})
  await expect(zustand()).toContainText('saugt · hält')
  await expect(zustand()).toContainText('Aus erst unter 0,5 g/m³')
  // Die Kachel zeigt den Wert, nach dem die Regelung entscheidet — nicht den rohen.
  await expect(page.locator('.v1-stat', { hasText: 'Differenz' }).first()).toContainText('0,78')

  // Über der Einschaltgrenze: er saugt ohnehin, „hält" wäre falsch.
  await page.unroute('**/api/steuerung/zuluft')
  await mitLive({ differenzGm3: 1.3, differenzRegelGm3: 1.34 })
  await expect(zustand()).toContainText('saugt')
  await expect(zustand()).not.toContainText('hält')
})
