import { test, expect, type Locator, type Page } from '@playwright/test'
import { darfUeberspringen } from './pflicht'

/**
 * Ein gesperrtes Speichern zeigt das markierte Feld — auch auf einem anderen Reiter.
 *
 * <b>Der Befund (02.10.2026).</b> Auf den Steuerungsseiten sperrt ein leeres
 * Zahlenfeld das Speichern, und oben steht „Bitte die markierten Felder
 * prüfen.". Lag das Feld auf einem anderen Reiter als dem offenen, war keine
 * Markierung zu sehen. Erwartet: die Seite wechselt auf den Reiter des Felds
 * und rollt es unter die feste Kopfleiste.
 *
 * <b>Zweimal, mit erschwerten Umständen</b> (CLAUDE.md, „Die Reparatur einmal
 * WIEDERHOLEN"): derselbe Ablauf ein zweites Mal ohne Neuladen, mit einem
 * anderen Feld — dem letzten seines Reiters — und weggerollt. Dazu der Fall
 * eines Felds, das zusätzlich in einer eingeklappten Liste steckt.
 *
 * Gespeichert wird dabei nichts: das Speichern bricht schon im Browser ab.
 */

// Ein Telefon: dort liegt die feste Kopfleiste über dem Inhalt. Niedrig, damit
// die Seite weit genug rollt — bei 740 px reichte der Reiter nicht so weit, und
// ein Rollen OHNE Rand für die Kopfleiste blieb unentdeckt (nachgemessen).
test.use({ viewport: { width: 390, height: 520 } })

const reiter = (page: Page, name: string) =>
  page.locator('.v1-tabs[aria-label="Bereich"] .v1-tab', { hasText: new RegExp(`^${name}$`) })

/** Liegt das Feld ganz im Fenster und nicht unter der festen Kopfleiste? */
async function imBild(feld: Locator): Promise<string> {
  return feld.evaluate(async (el) => {
    // Erst messen, wenn das weiche Rollen steht — mitten im Rollen streift das
    // Feld die richtige Stelle, und die Abfrage war schon grün (02.10.2026,
    // gefunden an kopfleiste-einrollen.spec.ts).
    let ruhig = 0
    let zuletzt = window.scrollY
    for (let bild = 0; bild < 300 && ruhig < 15; bild++) {
      await new Promise((fertig) => requestAnimationFrame(fertig))
      ruhig = window.scrollY === zuletzt ? ruhig + 1 : 0
      zuletzt = window.scrollY
    }
    let kopf = 0
    for (const leiste of document.querySelectorAll<HTMLElement>('.v1-mobile-topbar, .v1-mobile-nav')) {
      if (getComputedStyle(leiste).position === 'fixed') kopf = Math.max(kopf, leiste.getBoundingClientRect().bottom)
    }
    const r = el.getBoundingClientRect()
    if (r.height === 0) return 'nicht gerendert'
    if (r.top < kopf) return `oben ${Math.round(r.top)} px, Kopfleiste endet bei ${Math.round(kopf)} px`
    if (r.bottom > window.innerHeight) return `unten ${Math.round(r.bottom)} px, Fenster ${window.innerHeight} px`
    return 'im Bild'
  })
}

async function seiteLaden(page: Page, pfad: string, ersterReiter: string): Promise<void> {
  const antwort = await page.goto(pfad, { waitUntil: 'networkidle' })
  darfUeberspringen(antwort == null || antwort.status() >= 400, `${pfad} antwortet nicht — läuft die App unter GROW_OS_URL?`)
  const da = await reiter(page, ersterReiter).waitFor({ timeout: 15_000 }).then(() => true, () => false)
  darfUeberspringen(!da, `${pfad}: keine Reiterleiste — der Demobestand legt diese Steuerung an.`)
}

async function speichernUndPruefen(page: Page, erwarteterReiter: string, feldName: string): Promise<void> {
  await page.getByRole('button', { name: 'Speichern' }).click()
  await expect(page.getByText('Bitte die markierten Felder prüfen.')).toBeVisible()

  await expect(reiter(page, erwarteterReiter), `der Reiter „${erwarteterReiter}" mit dem leeren Feld ist nicht offen`)
    .toHaveClass(/\bactive\b/)
  const feld = page.getByLabel(feldName, { exact: true })
  await expect(feld).toBeVisible()
  await expect(feld.locator('xpath=ancestor::*[contains(@class,"st-feldzeile")][1]').locator('.st-fehler'))
    .toHaveText('Bitte eine Zahl eintragen.')
  // Gerollt wird weich — warten, bis das Feld angekommen ist.
  await expect.poll(() => imBild(feld), { timeout: 5_000, message: `„${feldName}" liegt nicht im Bild` })
    .toBe('im Bild')
}

test('Zuluft: das leere Feld auf einem anderen Reiter wird gezeigt — zweimal', async ({ page }) => {
  await seiteLaden(page, '/steuerung/zuluft', 'Regel')

  // Durchgang 1: erstes Feld des ersten Reiters leeren, auf den zweiten wechseln.
  await reiter(page, 'Regel').click()
  const differenz = page.getByLabel('Mindest-Differenz', { exact: true })
  const differenzAlt = await differenz.inputValue()
  expect(differenzAlt, 'Mindest-Differenz ist schon leer — dann prüft der Fall nichts.').not.toBe('')
  await differenz.fill('')
  await reiter(page, 'Lüfter').click()
  await expect(differenz).toHaveCount(0)
  await speichernUndPruefen(page, 'Regel', 'Mindest-Differenz')

  // Wieder füllen, sonst fände der zweite Durchgang dieses Feld zuerst.
  await differenz.fill(differenzAlt)

  // Durchgang 2, ohne Neuladen: das LETZTE Feld des zweiten Reiters, dann auf
  // den dritten Reiter und ans Seitenende gerollt.
  await reiter(page, 'Lüfter').click()
  const pause = page.getByLabel('Mindestpause', { exact: true })
  expect(await pause.inputValue(), 'Mindestpause ist schon leer.').not.toBe('')
  await pause.fill('')
  await reiter(page, 'Betrieb').click()
  await expect(pause).toHaveCount(0)
  await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight))
  await speichernUndPruefen(page, 'Lüfter', 'Mindestpause')
})

test('Entfeuchter: ein leeres Feld in den zugeklappten festen Schwellen wird gezeigt', async ({ page }) => {
  await seiteLaden(page, '/steuerung/entfeuchter', 'Regel')

  await reiter(page, 'Regel').click()
  // A-014: bei „Nach VPD regeln" sind die festen Schwellen gesperrt — erst ausschalten, dann bearbeiten.
  const schalter = page.getByLabel('Nach VPD regeln')
  if (await schalter.isChecked()) await schalter.uncheck()
  const kopf = page.locator('.st-kk-kopf', { hasText: 'Feste Schwellen' })
  if ((await kopf.getAttribute('aria-expanded')) === 'false') await kopf.click()
  await expect(kopf).toHaveAttribute('aria-expanded', 'true')
  const feld = page.getByLabel('Nacht · AUS unter', { exact: true })
  await feld.fill('')
  // Zuklappen und auf einen anderen Reiter — das Feld steckt jetzt doppelt versteckt.
  await kopf.click()
  await expect(feld).toBeHidden()
  await reiter(page, 'Betrieb').click()

  await speichernUndPruefen(page, 'Regel', 'Nacht · AUS unter')
  // Die Kachel hat sich von selbst geöffnet (CSS), das Feld ist zu sehen.
  await expect(feld).toBeVisible()
})

/**
 * Ein leeres Feld, das der gewählte Modus ausblendet („Plan +"/„Fest").
 *
 * <b>Der Befund (Prüfbericht 01.10.2026, „Noch offen, klein").</b> Den festen
 * Wert leeren, auf „Plan +" stellen, speichern: die Zeile wurde markiert, aber
 * neben einem gefüllten Feld — das leere stand im ausgeblendeten Modus, und
 * nichts sagte das. Jetzt nennt die Markierung Feld und Modus
 * (`modusFehler` in feld-fehler.ts). Den Modus selbst stellt die Seite nicht um:
 * das ist eine Einstellung des Nutzers.
 */
async function ausgeblendetPruefen(page: Page, zeile: Locator, erwarteterReiter: string, text: string): Promise<void> {
  await page.getByRole('button', { name: 'Speichern' }).click()
  await expect(page.getByText('Bitte die markierten Felder prüfen.')).toBeVisible()
  await expect(reiter(page, erwarteterReiter)).toHaveClass(/\bactive\b/)
  const markierung = zeile.locator('.st-fehler')
  await expect(markierung).toHaveText(text)
  await expect.poll(() => imBild(markierung), { timeout: 5_000, message: 'die Markierung liegt nicht im Bild' })
    .toBe('im Bild')
}

test('Entfeuchter: das leere Feld im ausgeblendeten Modus wird mit Feld und Modus genannt — zweimal', async ({ page }) => {
  await seiteLaden(page, '/steuerung/entfeuchter', 'Schutz')
  const block = (titel: string) => page.locator('.ef-tempmax')
    .filter({ has: page.getByRole('radiogroup', { name: `Temperatur max. ${titel}` }) })
  const chip = (titel: string, modus: string) =>
    block(titel).getByRole('radio', { name: modus, exact: true })

  // Durchgang 1: Tag — den festen Wert leeren, dann „Plan +" wählen.
  await reiter(page, 'Schutz').click()
  await chip('Tag', 'Fest').click()
  const festTag = block('Tag').getByLabel('Fester Wert', { exact: true })
  const festTagAlt = await festTag.inputValue()
  expect(festTagAlt, 'Der feste Wert ist schon leer — dann prüft der Fall nichts.').not.toBe('')
  await festTag.fill('')
  await chip('Tag', 'Plan +').click()
  await expect(festTag).toHaveCount(0)
  await reiter(page, 'Betrieb').click()
  await ausgeblendetPruefen(page, block('Tag').locator('.st-feldzeile').first(), 'Schutz',
    'Fester Wert: Bitte eine Zahl eintragen — steht unter „Fest" und ist ausgeblendet, solange „Plan +" gewählt ist.')
  // Der Modus bleibt, wie der Nutzer ihn gewählt hat.
  await expect(chip('Tag', 'Plan +')).toHaveAttribute('aria-checked', 'true')

  // Zurück auf Fest und wieder füllen.
  await chip('Tag', 'Fest').click()
  await block('Tag').getByLabel('Fester Wert', { exact: true }).fill(festTagAlt)

  // Durchgang 2, ohne Neuladen und andersherum: Nacht — den Abstand leeren,
  // zurück auf „Fest", ans Seitenende gerollt, auf einen anderen Reiter.
  await chip('Nacht', 'Plan +').click()
  const abstand = block('Nacht').getByLabel('Abstand zum Plan', { exact: true })
  expect(await abstand.inputValue(), 'Der Abstand ist schon leer.').not.toBe('')
  await abstand.fill('')
  await chip('Nacht', 'Fest').click()
  await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight))
  await reiter(page, 'Regel').click()
  await ausgeblendetPruefen(page, block('Nacht').locator('.st-feldzeile').first(), 'Schutz',
    'Abstand zum Plan: Bitte eine Zahl eintragen — steht unter „Plan +" und ist ausgeblendet, solange „Fest" gewählt ist.')
  // Die Tag-Zeile ist wieder gefüllt und trägt keine Markierung mehr.
  await expect(block('Tag').locator('.st-fehler')).toHaveCount(0)
})

test('CO₂: das leere Feld im ausgeblendeten Modus wird mit Feld und Modus genannt', async ({ page }) => {
  await seiteLaden(page, '/steuerung/co2', 'Klima')
  await reiter(page, 'Klima').click()
  const zeile = page.locator('.st-feldzeile', { hasText: 'Canopy-Obergrenze' })
  await zeile.getByRole('button', { name: 'Fest', exact: true }).click()
  const fest = page.getByLabel('Canopy-Obergrenze', { exact: true })
  expect(await fest.inputValue(), 'Der feste Wert ist schon leer.').not.toBe('')
  await fest.fill('')
  await zeile.getByRole('button', { name: 'Plan +', exact: true }).click()
  await expect(fest).toHaveCount(0)
  await reiter(page, 'Ziel').click()
  await ausgeblendetPruefen(page, zeile, 'Klima',
    'Canopy-Obergrenze, fester Wert: Bitte eine Zahl eintragen — steht unter „Fest" und ist ausgeblendet, solange „Plan +" gewählt ist.')
})
