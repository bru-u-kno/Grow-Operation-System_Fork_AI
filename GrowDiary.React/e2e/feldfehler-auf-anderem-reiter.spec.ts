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
  return feld.evaluate((el) => {
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

test('Entfeuchter: ein leeres Feld in den eingeklappten festen Schwellen wird gezeigt', async ({ page }) => {
  await seiteLaden(page, '/steuerung/entfeuchter', 'Regel')

  await reiter(page, 'Regel').click()
  const klapp = page.locator('.ef-klapp')
  await klapp.click()
  await expect(klapp).toHaveAttribute('aria-expanded', 'true')
  const feld = page.getByLabel('Nacht · AUS unter', { exact: true })
  await feld.fill('')
  // Zuklappen und weg — das Feld steckt jetzt doppelt versteckt.
  await klapp.click()
  await expect(feld).toHaveCount(0)
  await reiter(page, 'Betrieb').click()

  await speichernUndPruefen(page, 'Regel', 'Nacht · AUS unter')
  await expect(klapp).toHaveAttribute('aria-expanded', 'true')
})
