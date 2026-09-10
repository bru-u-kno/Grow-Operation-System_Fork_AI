import { expect, test, type Page } from '@playwright/test'
import { backendAntwortet, darfUeberspringen } from './pflicht'

/**
 * Die vier Formulare der Kosten-Seite werden ausgefüllt, abgeschickt und nachgelesen.
 *
 * <b>Der Anlass (10.09.2026).</b> `formularbloecke-vollstaendig` meldete vier
 * Eingabeblöcke ohne jeden Rundweg: `kosten-strom-quelle-form`,
 * `kosten-artikel-form`, `kosten-nachfuellung-form`, `kosten-anschaffung-form`.
 * Sie sind mit forkai.6 bis forkai.9 entstanden und nie angefasst worden —
 * die Zählung war seitdem rot, und beim Umbau der Navigation fiel sie auf,
 * weil derselbe Lauf endlich wieder bis dorthin kam.
 *
 * <b>Was hier ein Rundweg heißt</b> (dieselbe Regel wie in
 * `formular-rundweg.spec.ts`): ausfüllen, den ausgehenden Aufruf abfangen und
 * im Rumpf nachsehen, dass die getippten Werte wirklich drinstehen, absenden,
 * neu laden und prüfen, dass der Wert noch da ist. Erst das letzte Stück
 * schließt die Lücke — eine Oberfläche, die den Wert nur anzeigt, ohne ihn
 * gespeichert zu haben, besteht alles davor.
 *
 * <b>Diese Datei schreibt in die Datenbank.</b> Deshalb seriell, und jeder
 * Datensatz trägt „Rundweg“ im Namen: im Bestand erkennbar, und keine Zählung,
 * die auf Inhalt prüft, stolpert darüber.
 *
 * <b>Die Strom-Quelle ist der heikle Fall.</b> Sie zeigt auf Entitäten in
 * Home Assistant; würde der Rundweg dort etwas Echtes eintragen, verstellte er
 * die Zählerstände der laufenden Anlage. Deshalb schreibt er einen erkennbar
 * erfundenen Namen (`sensor.rundweg_…`) und stellt am Ende den vorherigen Wert
 * wieder her — er nimmt sich also nur das, was er auch zurückgibt.
 */

test.describe.configure({ mode: 'serial' })

/** Ein Wert, der in diesem Lauf einmalig ist — sonst prüft der zweite Lauf den ersten. */
function marke(): string {
  return `Rundweg ${new Date().toISOString().slice(11, 19)}`
}

/**
 * Auf die ANTWORT warten, nicht auf die Anfrage — und ihren Rumpf zurückgeben.
 * Begründung ausführlich in `formular-rundweg.spec.ts`; kurz: `waitForRequest`
 * ist schon erfüllt, bevor der Server geantwortet hat, und wer danach neu lädt,
 * bricht die Anfrage auf einem langsamen Läufer sogar ab.
 */
async function abgeschickt(
  seite: Page,
  methode: 'POST' | 'PUT',
  muster: RegExp,
  handlung: () => Promise<void>,
): Promise<Record<string, unknown>> {
  const antwort = seite.waitForResponse((r) => r.request().method() === methode && muster.test(r.url()))
  await handlung()
  const fertig = await antwort
  expect(fertig.ok(), `${methode} ${fertig.url()} kam mit HTTP ${fertig.status()} zurück.`).toBe(true)
  return JSON.parse(fertig.request().postData() ?? '{}') as Record<string, unknown>
}

async function kostenSeite(seite: Page, reiter: string): Promise<void> {
  await seite.goto(`/kosten?tab=${reiter}`)
  await expect(seite.locator('[data-audit="kosten-summe"], [data-audit="kosten-strom"]').first())
    .toBeVisible({ timeout: 15000 })
}

test.describe('Kosten-Rundweg', () => {
  test.beforeEach(async ({ page }) => {
    darfUeberspringen(!(await backendAntwortet(page.request)), 'Kein Backend erreichbar.')
  })

  test('Artikel anlegen, Nachfüllung erfassen und beides wiederfinden', async ({ page }) => {
    const name = `CO₂ ${marke()}`

    // --- kosten-artikel-form ---
    await kostenSeite(page, 'verbrauch')
    await page.locator('[data-audit="kosten-artikel-anlegen"]').click()
    const formular = page.locator('[data-audit="kosten-artikel-form"]')
    await expect(formular).toBeVisible()

    await formular.locator('input[placeholder="CO₂-Flasche 10 kg"]').fill(name)
    await formular.locator('input[placeholder="10"]').fill('10')
    await formular.locator('input[placeholder="36,75"]').fill('36,75')

    const artikelRumpf = await abgeschickt(page, 'POST', /\/api\/kosten\/artikel$/, async () => {
      await page.locator('[data-audit="kosten-artikel-speichern"]').click()
    })
    expect(artikelRumpf.name, 'Der getippte Name steht nicht im abgeschickten Rumpf.').toBe(name)
    // Deutsches Komma: „36,75“ muss als Zahl 36.75 ankommen und nicht als 3675
    // oder leer. Genau dieser Fehler ist der Grund, warum es `zahlenfeld.ts`
    // gibt — ein Wert wurde still als leer gespeichert, mit Erfolgsmeldung.
    expect(artikelRumpf.preisEur, 'Aus „36,75“ wurde nicht 36.75.').toBe(36.75)

    // Neu laden: erst das beweist, dass gespeichert wurde und nicht nur angezeigt.
    await kostenSeite(page, 'verbrauch')
    await expect(page.locator('[data-audit="kosten-artikel"]')).toContainText(name)

    // --- kosten-nachfuellung-form ---
    await page.locator('[data-audit="kosten-nachfuellung-erfassen"]').click()
    const fuellung = page.locator('[data-audit="kosten-nachfuellung-form"]')
    await expect(fuellung).toBeVisible()

    // Den eben angelegten Artikel wählen — nicht den ersten der Liste: in einem
    // gewachsenen Bestand ist das irgendein fremder.
    await fuellung.locator('select').first().selectOption({ label: name })
    await fuellung.locator('input[placeholder="10"]').fill('10')
    await fuellung.locator('input[placeholder="34,90"]').fill('41,20')

    const fuellungRumpf = await abgeschickt(page, 'POST', /\/api\/kosten\/nachfuellungen/, async () => {
      await page.locator('[data-audit="kosten-nachfuellung-speichern"]').click()
    })
    expect(fuellungRumpf.kostenEur, 'Aus „41,20“ wurde nicht 41.2.').toBe(41.2)

    await kostenSeite(page, 'verbrauch')
    await expect(page.locator('[data-audit="kosten-artikel"]')).toContainText(name)
  })

  test('Anschaffung erfassen und wiederfinden', async ({ page }) => {
    const name = `Erntescheren ${marke()}`

    await kostenSeite(page, 'anschaffungen')
    await page.locator('[data-audit="kosten-anschaffung-erfassen"]').click()
    const formular = page.locator('[data-audit="kosten-anschaffung-form"]')
    await expect(formular).toBeVisible()

    await formular.locator('input[placeholder="Erntescheren"]').fill(name)
    await formular.locator('input[placeholder="1"]').fill('3')
    await formular.locator('input[placeholder="4,90"]').fill('4,90')

    // Die Vorschau rechnet Stück × Einzelpreis. Wenn die schon falsch steht,
    // ist der Rumpf darunter Zufall.
    await expect(formular.locator('[data-audit="kosten-anschaffung-vorschau"]')).toContainText('14,70')

    const rumpf = await abgeschickt(page, 'POST', /\/api\/kosten\/anschaffungen/, async () => {
      await page.locator('[data-audit="kosten-anschaffung-speichern"]').click()
    })
    expect(rumpf.name).toBe(name)
    expect(rumpf.stueck, 'Aus „3“ wurden nicht 3 Stück.').toBe(3)
    expect(rumpf.einzelpreisEur, 'Aus „4,90“ wurde nicht 4.9.').toBe(4.9)

    await kostenSeite(page, 'anschaffungen')
    // Die Tabelle, nicht `main`: die Shell hat seit dem Navigationsumbau mehr
    // als ein <main> (Seitenrahmen und Blattinhalt), und Playwright bricht bei
    // mehrdeutigen Treffern ab, statt eines auszuwuerfeln — zu Recht.
    await expect(page.locator('[data-audit="kosten-anschaffungen"]')).toContainText(name)
  })

  test('Strom-Quelle speichern und den vorherigen Stand zurückgeben', async ({ page }) => {
    await kostenSeite(page, 'strom')
    await page.locator('[data-audit="kosten-strom-quelle"]').click()
    const formular = page.locator('[data-audit="kosten-strom-quelle-form"]')
    await expect(formular).toBeVisible()

    const zaehlerFeld = formular.locator('input[placeholder="sensor.fritz_dect_210_1_total_energy"]')
    const vorher = await zaehlerFeld.inputValue()

    // Erkennbar erfunden: diese Entität gibt es in keiner Anlage. Sie darf
    // nicht bloß „falsch“ sein, sie muss als Testrest lesbar sein, falls der
    // Lauf zwischen Schreiben und Zurückstellen abbricht.
    const testEntitaet = 'sensor.rundweg_kein_echter_zaehler'
    await zaehlerFeld.fill(testEntitaet)

    const rumpf = await abgeschickt(page, 'PUT', /\/api\/kosten\/strom-quelle/, async () => {
      await page.locator('[data-audit="kosten-strom-quelle-speichern"]').click()
    })
    expect(rumpf.zaehlerEntityId).toBe(testEntitaet)

    await kostenSeite(page, 'strom')
    await page.locator('[data-audit="kosten-strom-quelle"]').click()
    await expect(zaehlerFeld).toHaveValue(testEntitaet)

    // Zurückgeben, was geliehen war. Ohne das zeigt die Kosten-Seite der
    // laufenden Anlage anschliessend auf eine Entität, die es nicht gibt, und
    // der Zählerstand-Worker findet nichts mehr.
    await zaehlerFeld.fill(vorher)
    await abgeschickt(page, 'PUT', /\/api\/kosten\/strom-quelle/, async () => {
      await page.locator('[data-audit="kosten-strom-quelle-speichern"]').click()
    })

    await kostenSeite(page, 'strom')
    await page.locator('[data-audit="kosten-strom-quelle"]').click()
    await expect(zaehlerFeld).toHaveValue(vorher)
  })
})
