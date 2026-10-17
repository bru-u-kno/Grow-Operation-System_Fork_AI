import { test, expect, type Page } from '@playwright/test'
import { backendAntwortet, darfUeberspringen } from './pflicht'

/**
 * Steuerung › Übersicht: Die Zuluft-Kachel sagt, WARUM der Lüfter nicht läuft (forkai.238).
 *
 * <b>Der Anlass (17.10.2026).</b> Die Kachel sagte „bereit", die Differenz stand bei 1,43 g/m³ und der Lüfter lief
 * nicht. „bereit" hieß nur: kein Bedarf, keine Sperre, keine Kälte-Pause — dass die Einschaltschwelle (1,5) noch
 * nicht erreicht war, stand nirgends. Jetzt steht in der Kachel ein Schild („aus"), der Grund mit Zahlen und — bei
 * „zu wenig Unterschied" — das Band mit Messwert und Grenzen.
 *
 * <b>Was hier geprüft wird.</b> Gerendert wird die GEBAUTE App; die Antwort der Übersicht wird gesetzt, weil der
 * Demobestand keinen Zuluft-Fühler hat (derselbe Weg wie in <c>zuluft-abschalt-abstand.spec.ts</c>). Was das Backend
 * für welchen Stand liefert, prüft <c>ZuluftZustandTests</c>; hier geht es um das, was der Nutzer sieht: die
 * Zustandszeile steht über die ganze Breite (die gebaute CSS gewinnt), nichts ragt über den Rand, die anderen
 * Module behalten ihre Unterzeile, und ein Tipp öffnet die Detailseite — zweimal hintereinander.
 *
 * <b>Liest nur.</b> Kein Schreibzugriff auf den Bestand, deshalb ohne Schloss.
 */

test.beforeEach(async ({ request }) => {
  darfUeberspringen(!(await backendAntwortet(request)), 'kein Backend unter GROW_OS_URL — diese Fälle bedienen die laufende App')
})

type Modul = Record<string, unknown>

const ZU_WENIG: Modul = {
  kennung: 'zuluft', titel: 'Zuluft', status: 'aus',
  kurz: 'Ansaugen ab 1,5 g/m³ · Automatik an', wert: '1,43 g/m³',
  unterzeile: 'aus · zu wenig Unterschied · noch 0,07 bis 1,5', hatDetail: true,
  zustand: { wort: 'aus', satz: 'zu wenig Unterschied · noch 0,07 bis 1,5', ton: 'neutral' },
  balken: { ist: 1.43, ausUnter: 1.0, anAb: 1.5 },
}

const SAUGT: Modul = {
  kennung: 'zuluft', titel: 'Zuluft', status: 'an',
  kurz: 'Ansaugen ab 1,2 g/m³ · Automatik an', wert: '1,12 g/m³',
  unterzeile: 'saugt · Stufe 6 · hält bis unter 1,0', hatDetail: true,
  zustand: { wort: 'saugt', satz: 'Stufe 6 · hält bis unter 1,0', ton: 'ok' },
}

const SPERRE: Modul = {
  kennung: 'zuluft', titel: 'Zuluft', status: 'warn',
  kurz: 'Ansaugen ab 1,2 g/m³ · Automatik an', wert: '1,44 g/m³',
  unterzeile: 'wartet · Mindestpause noch 8 min, dann Stufe 6', hatDetail: true,
  zustand: { wort: 'wartet', satz: 'Mindestpause noch 8 min, dann Stufe 6', ton: 'warn' },
}

/** Ein anderes Modul wie vom Backend: die kleine Unterzeile steht rechts unter dem Wert. */
const CO2: Modul = {
  kennung: 'co2', titel: 'CO₂-Begasung', status: 'an', kurz: 'Fest · Ziel 900 ppm · Automatik an',
  wert: '812 ppm', unterzeile: 'Ventil zu · Lüfter Stufe 6', hatDetail: true,
}

async function uebersichtMit(page: Page, zuluft: Modul): Promise<void> {
  await page.route('**/api/steuerung', async (route) => {
    if (route.request().method() !== 'GET') { await route.continue(); return }
    const echt = await (await route.fetch()).json()
    await route.fulfill({ json: { ...echt, module: [CO2, zuluft] } })
  })
  const antwort = await page.goto('/steuerung', { waitUntil: 'networkidle' })
  darfUeberspringen(antwort == null || antwort.status() >= 400, '/steuerung antwortet nicht — läuft die App unter GROW_OS_URL?')
}

const zuluftZeile = (page: Page) => page.locator('button.st-zeile').filter({ hasText: 'Zuluft' })

test('zu wenig Unterschied: Schild, Satz mit Zahlen und das Band stehen in der Kachel', async ({ page }) => {
  await page.setViewportSize({ width: 1100, height: 900 })
  await uebersichtMit(page, ZU_WENIG)
  const zeile = zuluftZeile(page)

  await expect(zeile.locator('.st-schild')).toHaveText('aus')
  await expect(zeile.locator('.st-zustand-satz')).toHaveText('zu wenig Unterschied · noch 0,07 bis 1,5')
  await expect(zeile.locator('.st-wert')).toContainText('1,43 g/m³')
  // Das Band ist ein Bild mit Satz — Farbe allein trägt nichts.
  await expect(zeile.getByRole('img', { name: /Aus unter 1,0, an ab 1,5 g\/m³, dazwischen bleibt er, wie er ist; jetzt 1,43/ })).toBeVisible()
  // Die kompakte Fassung hat keine Legende (der Satz steht schon darüber).
  await expect(zeile.locator('.zl-legende')).toHaveCount(0)
  // Rechts unter dem Wert steht nichts mehr: der Grund steht jetzt unten, nicht doppelt.
  await expect(zeile.locator('.st-wert small')).toHaveCount(0)
})

test('die Zustandszeile steht über die ganze Breite — die gebaute CSS gewinnt', async ({ page }) => {
  await page.setViewportSize({ width: 1100, height: 900 })
  await uebersichtMit(page, ZU_WENIG)
  const zeile = zuluftZeile(page)
  const kasten = (await zeile.boundingBox())!
  const zustand = (await zeile.locator('.st-zustand').boundingBox())!
  const wert = (await zeile.locator('.st-wert').boundingBox())!

  // Breit: mindestens neun Zehntel der Kachel (Innenabstand abgezogen) …
  expect(zustand.width).toBeGreaterThan(kasten.width * 0.9)
  // … und UNTER dem Wert, nicht daneben.
  expect(zustand.y).toBeGreaterThanOrEqual(wert.y + wert.height - 1)
})

test('andere Module behalten ihre kleine Unterzeile rechts unter dem Wert', async ({ page }) => {
  await uebersichtMit(page, SAUGT)
  const co2 = page.locator('button.st-zeile').filter({ hasText: 'CO₂-Begasung' })

  await expect(co2.locator('.st-wert small')).toHaveText('Ventil zu · Lüfter Stufe 6')
  await expect(co2.locator('.st-zustand')).toHaveCount(0)
})

test('Schildfarben: saugt ist grün, wartet ist gelb, aus ist neutral — und das Wort trägt es allein', async ({ page }) => {
  for (const [modul, ton, wort] of [[SAUGT, 'ok', 'saugt'], [SPERRE, 'warn', 'wartet'], [ZU_WENIG, 'neutral', 'aus']] as const) {
    await page.unroute('**/api/steuerung').catch(() => undefined)
    await uebersichtMit(page, modul)
    const schild = zuluftZeile(page).locator('.st-schild')
    await expect(schild).toHaveText(wort)
    await expect(schild).toHaveClass(new RegExp(`ton-${ton}`))
  }
})

test('Handy: bei 320, 360 und 390 px ragt weder Satz noch Band über die Kachel', async ({ page }) => {
  for (const breite of [320, 360, 390]) {
    await page.setViewportSize({ width: breite, height: 800 })
    await page.unroute('**/api/steuerung').catch(() => undefined)
    await uebersichtMit(page, ZU_WENIG)
    const zeile = zuluftZeile(page)
    await expect(zeile.locator('.st-zustand-satz')).toBeVisible()

    // Gemessen wird der TEXT, nicht sein Kasten: ein abgeschnittener Satz bleibt mit dem Kasten in der Spalte.
    const messung = await zeile.evaluate((el) => {
      const rand = el.getBoundingClientRect()
      const satz = el.querySelector('.st-zustand-satz')!
      const spanne = document.createRange()
      spanne.selectNodeContents(satz)
      const rechts = Math.max(...Array.from(spanne.getClientRects()).map((r) => r.right))
      const band = el.querySelector('.zl-band')!.getBoundingClientRect()
      return { kachelRechts: rand.right, satzRechts: rechts, bandRechts: band.right, seite: document.documentElement.scrollWidth }
    })
    expect(messung.satzRechts, `Satz bei ${breite} px`).toBeLessThanOrEqual(messung.kachelRechts + 0.5)
    expect(messung.bandRechts, `Band bei ${breite} px`).toBeLessThanOrEqual(messung.kachelRechts + 0.5)
    expect(messung.seite, `Seite bei ${breite} px`).toBeLessThanOrEqual(breite)
  }
})

test('ein Tipp öffnet die Detailseite — zweimal hintereinander, ohne Neuladen', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 800 })
  await uebersichtMit(page, ZU_WENIG)

  for (let durchgang = 0; durchgang < 2; durchgang++) {
    await zuluftZeile(page).click()
    await expect(page).toHaveURL(/\/steuerung\/zuluft$/)
    await expect(page.getByRole('heading', { name: 'Zuluft' }).first()).toBeVisible()
    await page.goBack()
    await expect(zuluftZeile(page).locator('.st-schild')).toBeVisible()
  }
})
