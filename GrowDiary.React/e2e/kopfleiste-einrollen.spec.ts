import { test, expect, type Locator, type Page } from '@playwright/test'
import { darfUeberspringen } from './pflicht'

/**
 * Ein eingerolltes Ziel landet unter der GANZEN Kopfflaeche — auch unter der
 * Navigationsreihe.
 *
 * <b>Der Befund (02.10.2026).</b> `kopfUnterkante()` in
 * `src/components/reiter-ins-bild.ts` zaehlte nur feste Leisten mit Oberkante
 * bei 0. Die Navigationsreihe haengt darunter (Oberkante 52 px) und fiel heraus.
 * Solange sie in `--mobil-kopf` (109 px) passt, rettete der Rand aus
 * `scroll-margin-top` die Rechnung. Waechst sie mit der Schrift am Telefon,
 * landete die Reiterleiste — oder ein markiertes Feld — unter ihr.
 *
 * <b>Wie die groessere Schrift nachgestellt wird.</b> Die Schrift der Leiste
 * steht in px; eine groessere Schrift im Browser erreicht sie nicht, die
 * Text-Vergroesserung der HA-App (WebView) schon. Chromium kann das nicht
 * nachstellen, deshalb setzt der Test die Schrift der Leiste per Stil hoeher.
 * Das ist die Vorbedingung, nicht der Beleg: die Reparatur steckt im gebauten
 * Skript und wird hier nur gemessen.
 *
 * Zweimal, mit verschiedenen Zielen (CLAUDE.md, „Die Reparatur einmal
 * WIEDERHOLEN"): erst die Reiterleiste nach einem Wechsel, dann — ohne
 * Neuladen — ein markiertes Feld auf einem anderen Reiter.
 */

test.use({ viewport: { width: 390, height: 520 } })

const reiter = (page: Page, name: string) =>
  page.locator('.v1-tabs[aria-label="Bereich"] .v1-tab', { hasText: new RegExp(`^${name}$`) })

/**
 * Liegt das Element unter der ganzen festen Kopfflaeche und im Fenster?
 *
 * Gemessen wird erst, wenn das weiche Rollen steht: mitten im Rollen streift
 * das Ziel die richtige Stelle — im ersten Entwurf bestand der Test deshalb
 * auch ohne die Reparatur.
 */
async function unterDerKopfflaeche(ziel: Locator): Promise<string> {
  return ziel.evaluate(async (el) => {
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
    if (r.top < kopf) return `oben ${Math.round(r.top)} px, Kopfflaeche endet bei ${Math.round(kopf)} px`
    if (r.top > window.innerHeight - 20) return `oben ${Math.round(r.top)} px, unter der Fensterkante`
    return 'im Bild'
  })
}

test('Zuluft am Telefon mit grosser Schrift: Reiterleiste und markiertes Feld landen unter der Navigationsreihe', async ({ page }) => {
  const antwort = await page.goto('/steuerung/zuluft', { waitUntil: 'networkidle' })
  darfUeberspringen(antwort == null || antwort.status() >= 400, '/steuerung/zuluft antwortet nicht — läuft die App unter GROW_OS_URL?')
  const da = await reiter(page, 'Regel').waitFor({ timeout: 15_000 }).then(() => true, () => false)
  darfUeberspringen(!da, '/steuerung/zuluft: keine Reiterleiste — der Demobestand legt diese Steuerung an.')

  // Vorbedingung: die Navigationsreihe so hoch, wie sie mit doppelter Schrift wird.
  await page.addStyleTag({ content: '.forkai-leiste-icon { font-size: 34px !important; } .forkai-leiste-text { font-size: 20px !important; }' })
  const nav = page.locator('.v1-mobile-nav')
  const navUnten = await nav.evaluate((el) => el.getBoundingClientRect().bottom)
  const rand = await reiter(page, 'Regel').evaluate((el) => parseFloat(getComputedStyle(el.parentElement!).scrollMarginTop) || 0)
  expect(navUnten, `Selbsttest: die Navigationsreihe endet bei ${navUnten} px und damit innerhalb des festen Rands `
    + `(${rand} px) — dann prüft der Fall nichts.`).toBeGreaterThan(rand)

  // Ziel 1: die Reiterleiste nach einem Wechsel, von ganz unten aus.
  await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight))
  await reiter(page, 'Lüfter').click()
  const leiste = page.locator('.v1-tabs[aria-label="Bereich"]')
  await expect.poll(() => unterDerKopfflaeche(leiste), { timeout: 5_000, message: 'die Reiterleiste liegt unter der Kopfflaeche' })
    .toBe('im Bild')

  // Ziel 2, ohne Neuladen: ein markiertes Feld auf einem anderen Reiter.
  const pause = page.getByLabel('Mindestpause', { exact: true })
  expect(await pause.inputValue(), 'Mindestpause ist schon leer.').not.toBe('')
  await pause.fill('')
  await reiter(page, 'Betrieb').click()
  await expect(pause).toHaveCount(0)
  await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight))
  await page.getByRole('button', { name: 'Speichern' }).click()
  await expect(reiter(page, 'Lüfter')).toHaveClass(/\bactive\b/)
  const zeile = pause.locator('xpath=ancestor::*[contains(@class,"st-feldzeile")][1]')
  await expect.poll(() => unterDerKopfflaeche(zeile), { timeout: 5_000, message: 'das markierte Feld liegt unter der Kopfflaeche' })
    .toBe('im Bild')
})
