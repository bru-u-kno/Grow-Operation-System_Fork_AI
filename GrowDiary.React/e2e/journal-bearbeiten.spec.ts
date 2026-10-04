import { expect, test, type Page } from '@playwright/test'
import { backendAntwortet, darfUeberspringen } from './pflicht'
import { gibSchloss, nimmSchloss } from './schloss'

/**
 * Einen Journaleintrag bearbeiten: öffnen, ändern, speichern, neu laden —
 * und dasselbe ein zweites Mal.
 *
 * **Der Anlass (04.10.2026).** Ein Wasserwechsel stand mit „CANNA pH- Pro
 * Bloom: Menge nicht notiert" im Journal. Die Menge war bekannt, ließ sich aber
 * nicht nachtragen — es gab nur „+ Eintrag" und „Entfernen".
 *
 * **Was hier gefahren wird** (CLAUDE.md: Formular erst geprüft, wenn ausgefüllt,
 * abgeschickt und nach dem Neuladen wiedergefunden; jede Bedienung ein zweites
 * Mal unter erschwerten Umständen):
 * - „Bearbeiten" öffnet `journal-edit-form` vorbefüllt, der Fokus steht im Titel;
 * - „Änderungen speichern" schickt PUT ohne Zeitpunkt (nicht geändert) und der
 *   Text steht nach dem Neuladen da — mehrzeilig;
 * - ein zweites Mal am selben Eintrag, ohne Neuladen;
 * - Escape bricht ab, ohne zu senden, und der Fokus kehrt auf „Bearbeiten" zurück;
 * - bei 412 px Breite ragt nichts über den Rand und beide Knöpfe sind im Bild.
 *
 * Aufgeräumt wird über den Löschweg — den gibt es seit dem 25.08.2026.
 */

test.describe.configure({ mode: 'serial' })
test.beforeEach(async () => { await nimmSchloss() })
test.afterEach(() => { gibSchloss() })

const MARKE = `Rundweg-Bearbeiten ${new Date().toISOString().slice(11, 19)}`
let eintragId: number | null = null

test.afterAll(async ({ playwright, baseURL }) => {
  if (eintragId == null) return
  const api = await playwright.request.newContext({ baseURL })
  try {
    await api.delete(`/api/journal/${eintragId}`)
  } finally {
    await api.dispose()
  }
})

/** Auf die Antwort warten und den gesendeten Rumpf zurückgeben. */
async function gesendet(seite: Page, methode: string, muster: RegExp, handlung: () => Promise<void>) {
  const antwort = seite.waitForResponse((r) => r.request().method() === methode && muster.test(r.url()))
  await handlung()
  const r = await antwort
  expect(r.ok(), `${methode} ${r.url()} antwortete ${r.status()}`).toBeTruthy()
  return { url: r.url(), rumpf: JSON.parse(r.request().postData() ?? '{}') as Record<string, unknown> }
}

function bearbeitenKnopf(seite: Page, titel: string) {
  return seite.getByRole('button', { name: `Eintrag „${titel}" bearbeiten` })
}

test('Rundweg: journal-edit-form — Eintrag korrigieren, zweimal, und wiederfinden', async ({ page }) => {
  darfUeberspringen(!(await backendAntwortet(page.request)), 'Kein Backend unter GROW_OS_URL.')

  await page.goto('/journal', { waitUntil: 'networkidle' })

  // Anlegen über das Formular, das der Nutzer auch nimmt.
  await page.locator('[data-audit="journal-add-entry"]').click()
  const neu = page.locator('[data-audit="journal-entry-form"]')
  await neu.locator('select').first().selectOption('ReservoirChange')
  await neu.getByLabel('Titel', { exact: true }).fill(MARKE)
  await neu.getByLabel('Text', { exact: true }).fill('CANNA pH- Pro Bloom: Menge nicht notiert')
  const angelegt = await gesendet(page, 'POST', /\/journal$/,
    () => neu.getByRole('button', { name: 'Eintrag speichern' }).click())
  expect(angelegt.rumpf.title).toBe(MARKE)

  // Erster Durchgang: öffnen, vorbefüllt, Fokus im Titel.
  await bearbeitenKnopf(page, MARKE).click()
  const formular = page.locator('[data-audit="journal-edit-form"]')
  await expect(formular).toBeVisible()
  await expect(formular.getByLabel('Titel', { exact: true })).toHaveValue(MARKE)
  await expect(formular.getByLabel('Titel', { exact: true })).toBeFocused()
  await expect(formular.locator('textarea')).toHaveValue('CANNA pH- Pro Bloom: Menge nicht notiert')
  await expect(formular.locator('select').first(), 'Die Art kam nicht vorbefüllt an.')
    .toHaveValue('ReservoirChange')

  const korrigiert = 'CANNA pH- Pro Bloom: ca. 25 ml\nPurolyt: 200 ml direkt nach dem Wechsel → ORP 450 mV'
  await formular.locator('textarea').fill(korrigiert)
  const erstes = await gesendet(page, 'PUT', /\/api\/journal\/\d+$/,
    () => formular.getByRole('button', { name: 'Änderungen speichern' }).click())
  eintragId = Number(erstes.url.match(/\/api\/journal\/(\d+)$/)![1])
  expect(erstes.rumpf.body).toBe(korrigiert)
  expect(erstes.rumpf.entryType, 'Die Art ging beim Speichern verloren.').toBe('ReservoirChange')
  expect(erstes.rumpf.occurredAtLocal,
    'Der Zeitpunkt wurde nicht angefasst und darf nicht mitgehen — sonst verliert der Eintrag seine Sekunden.')
    .toBeNull()

  await expect(formular).toHaveCount(0)
  await expect(bearbeitenKnopf(page, MARKE), 'Nach dem Speichern steht der Fokus nicht wieder auf „Bearbeiten".')
    .toBeFocused()

  // Zweiter Durchgang, ohne Neuladen: derselbe Knopf, anderer Wert.
  const neuerTitel = `${MARKE} korrigiert`
  await bearbeitenKnopf(page, MARKE).click()
  await expect(formular.locator('textarea'), 'Beim zweiten Öffnen stand der alte Text im Formular.')
    .toHaveValue(korrigiert)
  await formular.getByLabel('Titel', { exact: true }).fill(neuerTitel)
  const zweites = await gesendet(page, 'PUT', /\/api\/journal\/\d+$/,
    () => formular.getByRole('button', { name: 'Änderungen speichern' }).click())
  expect(zweites.rumpf.title).toBe(neuerTitel)

  // Neu laden: steht beides da?
  await page.reload({ waitUntil: 'networkidle' })
  const strom = page.locator('[data-audit="journal-stream"]')
  await expect(strom).toContainText(neuerTitel)
  await expect(strom).toContainText('CANNA pH- Pro Bloom: ca. 25 ml')
  await expect(strom).toContainText('Purolyt: 200 ml direkt nach dem Wechsel')
  const gelesen = await (await page.request.get(`/api/journal/${eintragId}`)).json()
  expect(gelesen.body).toBe(korrigiert)
  expect(gelesen.updatedAtUtc, '„zuletzt geändert" fehlt nach dem Speichern.').toBeTruthy()

  // Escape bricht ab, ohne zu senden.
  let gesendetBeimAbbrechen = false
  page.on('request', (r) => { if (r.method() === 'PUT' && /\/api\/journal\//.test(r.url())) gesendetBeimAbbrechen = true })
  await bearbeitenKnopf(page, neuerTitel).click()
  await formular.getByLabel('Titel', { exact: true }).fill('soll nicht ankommen')
  await page.keyboard.press('Escape')
  await expect(formular).toHaveCount(0)
  await expect(bearbeitenKnopf(page, neuerTitel)).toBeFocused()
  expect(gesendetBeimAbbrechen, 'Abbrechen hat trotzdem gespeichert.').toBe(false)
})

for (const breite of [320, 412]) {
  test(`Bearbeiten passt auf das Telefon (${breite} px)`, async ({ page }) => {
    darfUeberspringen(!(await backendAntwortet(page.request)), 'Kein Backend unter GROW_OS_URL.')
    darfUeberspringen(eintragId == null, 'Der Eintrag aus dem Rundweg fehlt — der erste Fall ist gescheitert.')

    await page.setViewportSize({ width: breite, height: 900 })
    await page.goto('/journal', { waitUntil: 'networkidle' })
    const knopf = bearbeitenKnopf(page, `${MARKE} korrigiert`)
    await knopf.scrollIntoViewIfNeeded()
    await knopf.click()

    const formular = page.locator('[data-audit="journal-edit-form"]')
    await expect(formular).toBeVisible()
    for (const name of ['Änderungen speichern', 'Abbrechen']) {
      const box = await formular.getByRole('button', { name }).boundingBox()
      expect(box, `„${name}" ist nicht zu sehen.`).not.toBeNull()
      expect(box!.x + box!.width, `„${name}" ragt über den rechten Rand.`).toBeLessThanOrEqual(breite)
      expect(box!.height, `„${name}" ist kleiner als ein Tippziel.`).toBeGreaterThanOrEqual(32)
    }
    // Prüfer 05.10.2026: neben der Zeitspalte war das Zeitfeld bei 320 px
    // 136 px breit, die Uhrzeit abgeschnitten — ohne Seitenüberlauf.
    const zeit = await formular.locator('input[type="datetime-local"]').boundingBox()
    expect(zeit!.width, 'Das Zeitfeld ist zu schmal, die Uhrzeit wird abgeschnitten.').toBeGreaterThanOrEqual(220)
    const ueberlauf = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)
    expect(ueberlauf, 'Mit offenem Bearbeiten-Formular läuft die Seite seitlich über.').toBeLessThanOrEqual(0)
  })
}
