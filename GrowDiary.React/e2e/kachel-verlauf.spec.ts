import { test, expect, type Page } from '@playwright/test'
import { backendAntwortet, darfUeberspringen } from './pflicht'

/**
 * Tipp auf eine Messwert-Kachel: der Verlauf erscheint direkt unter ihrer Zeile.
 *
 * **Der Anlass (05.10.2026).** Bru: „beim Klick auf eine Live-Kachel nicht wie
 * aktuell ein separates Fenster … sondern dafür deine Verlaufsgrafik", und am
 * Telefon löse ein Tipp auf den Wert „irgendeine Aktion" aus. Nachgemessen:
 * der alte Verlauf (ein kleines Liniendiagramm) stand unter dem GANZEN Bereich
 * — bei 390 px rund 1100 px unter der Kachel, ausserhalb des Bildschirms. Man
 * tippte, und sichtbar geschah nichts.
 *
 * **Warum hier.** Wo eine Zeile endet, entscheidet erst der Browser
 * (`flex-wrap`). Die Prüfung tippt deshalb die LINKE Kachel der ersten Zeile:
 * stünde der Verlauf direkt hinter der getippten Kachel statt am Zeilenende,
 * risse er die Zeile auseinander — bei der rechten fiele das nicht auf, dort
 * ist „direkt dahinter" zugleich das Zeilenende (Befund des Prüfers).
 *
 * Beide Ansichten: `/` ist im Demobestand Zelt 1 mit eigener Anordnung
 * (`DashboardBands`), `/live/2` ein Zelt mit der Standardanordnung
 * (`MetricBand` in `LiveScreen`). Der Kachel-Klick fehlte schon einmal in
 * einer der beiden (beta.38).
 */

const ANSICHTEN = [
  { name: 'eigene Anordnung', pfad: '/' },
  { name: 'Standardanordnung', pfad: '/live/2' },
] as const

const ZEILE = '.ls-metrics .gos-metric-row'

async function live(page: Page, request: Parameters<typeof backendAntwortet>[0], breite: number, pfad = '/') {
  darfUeberspringen(!(await backendAntwortet(request)), 'Kein Backend — die Kacheln brauchen die laufende App mit Demobestand.')
  await page.setViewportSize({ width: breite, height: 900 })
  await page.goto(pfad, { waitUntil: 'networkidle' })
  // Mengenwaechter: ohne anklickbare Kacheln misst keiner der Faelle etwas.
  await expect(page.locator('.gos-metric.is-clickable').first()).toBeVisible()
  expect(await page.locator('.gos-metric.is-clickable').count(), 'Weniger als drei anklickbare Kacheln — der Bestand liefert zu wenig Verlauf.')
    .toBeGreaterThanOrEqual(3)
}

/** Die Plätze der ersten Kachelzeile mit ihrer Oberkante, dazu der Verlauf. */
async function anordnung(page: Page) {
  return page.locator(ZEILE).first().evaluate((zeile) => [...zeile.children].map((kind) => ({
    platz: (kind as HTMLElement).dataset.kachelPlatz ?? null,
    verlauf: kind.hasAttribute('data-kachel-verlauf'),
    oben: (kind as HTMLElement).offsetTop,
    unten: (kind as HTMLElement).offsetTop + (kind as HTMLElement).offsetHeight,
  })))
}

for (const ansicht of ANSICHTEN) test(`Telefon, ${ansicht.name}: der Verlauf steht unter der Zeile der Kachel und ist im Bild`, async ({ page, request }) => {
  await live(page, request, 390, ansicht.pfad)
  const vorher = await anordnung(page)
  const erste = vorher.filter((k) => k.platz != null && k.oben === vorher[0].oben)
  expect(erste.length, 'Die erste Zeile hat bei 390 px keine zwei Kacheln — dann misst der Fall nichts.').toBeGreaterThanOrEqual(2)

  // Die linke Kachel der ersten Zeile, ein Tipp auf die Zahl.
  const links = page.locator(`${ZEILE} > [data-kachel-platz="${erste[0].platz}"] .gos-metric`).first()
  await expect(links).toHaveClass(/is-clickable/)
  await links.locator('.gos-metric-value').click()

  const verlauf = page.locator('[data-audit="metric-detail"]')
  await expect(verlauf).toHaveCount(1)
  await expect(verlauf.locator('[data-audit="verlauf-fokus"]')).toBeVisible()
  // Im Bild, nicht irgendwo darunter.
  await expect(verlauf.locator('[data-audit="verlauf-fokuswert"]')).toBeInViewport()

  const nachher = await anordnung(page)
  const index = nachher.findIndex((k) => k.verlauf)
  expect(index, 'Kein Verlauf in der Kachelzeile.').toBeGreaterThan(0)
  // Direkt nach der ersten Zeile, und die Zeile ist ganz geblieben.
  expect(nachher.slice(0, index).map((k) => k.platz)).toEqual(erste.map((k) => k.platz))
  expect(new Set(nachher.slice(0, index).map((k) => k.oben)).size, 'Der Verlauf hat die Zeile auseinandergerissen.').toBe(1)
  expect(nachher[index].oben).toBeGreaterThanOrEqual(Math.max(...nachher.slice(0, index).map((k) => k.unten)) - 1)

  // Fokus auf genau diesem Wert.
  await expect(verlauf.locator('.vd-wert[aria-pressed="true"]')).toHaveCount(1)

  // Kein Überlauf am Telefon.
  expect(await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)).toBeLessThanOrEqual(0)

  // Zweiter Tipp auf dieselbe Kachel schliesst.
  await links.locator('.gos-metric-value').click()
  await expect(verlauf).toHaveCount(0)
})

test('ohne Tipp springt nichts: Bereich zu und wieder auf lässt die Seite stehen', async ({ page, request }) => {
  // Befund des Prüfers: der Verlauf blieb offen, hängte sich nach dem
  // Wiederaufklappen neu ein und rollte die Seite um rund 500 px.
  await live(page, request, 390, '/live/2')
  await page.locator('.gos-metric.is-clickable').first().click()
  await expect(page.locator('[data-audit="metric-detail"]')).toHaveCount(1)
  const bereich = page.locator('.ls-fold-titel').first()
  await bereich.click()
  await expect(page.locator('[data-audit="metric-detail"]')).toHaveCount(0)
  await page.evaluate(() => window.scrollTo(0, 0))
  await bereich.click()
  await expect(page.locator('[data-audit="metric-detail"]')).toHaveCount(1)
  await page.waitForTimeout(800)
  expect(await page.evaluate(() => window.scrollY), 'Die Seite ist ohne Tipp gesprungen.').toBeLessThan(5)
})

test('Schließen gibt den Fokus an die Kachel zurück', async ({ page, request }) => {
  await live(page, request, 1280)
  const kachel = page.locator('.gos-metric.is-clickable').first()
  await kachel.focus()
  await page.keyboard.press('Enter')
  await expect(kachel).toHaveAttribute('aria-expanded', 'true')
  await page.locator('[data-audit="metric-detail-schliessen"]').click()
  await expect(page.locator('[data-audit="metric-detail"]')).toHaveCount(0)
  await expect(kachel).toBeFocused()
})

test('zweimal: andere Kachel, dann Schließen, dann gedreht — immer genau ein Verlauf am Zeilenende', async ({ page, request }) => {
  await live(page, request, 390)
  const kacheln = page.locator('.gos-metric.is-clickable')
  const n = await kacheln.count()

  // Die erste, dann die letzte anklickbare Kachel (anderer Bereich, weit unten).
  await kacheln.first().click()
  await expect(page.locator('[data-audit="metric-detail"]')).toHaveCount(1)
  await kacheln.nth(n - 1).click()
  await expect(page.locator('[data-audit="metric-detail"]')).toHaveCount(1)
  await expect(page.locator('[data-audit="metric-detail"] [data-audit="verlauf-fokuswert"]')).toBeInViewport()

  await page.locator('[data-audit="metric-detail-schliessen"]').click()
  await expect(page.locator('[data-audit="metric-detail"]')).toHaveCount(0)
  await expect(kacheln.nth(n - 1)).toHaveAttribute('aria-expanded', 'false')

  // Nochmal öffnen und das Fenster breit ziehen: die Kacheln brechen anders
  // um, der Verlauf muss ans neue Zeilenende wandern.
  await kacheln.first().click()
  await page.setViewportSize({ width: 1280, height: 900 })
  await expect.poll(async () => {
    const a = await anordnung(page)
    const index = a.findIndex((k) => k.verlauf)
    if (index < 1) return 'kein Verlauf'
    const oben = a[0].oben
    // Alles vor dem Verlauf in einer Zeile, nichts danach mehr in ihr.
    const vorne = a.slice(0, index).every((k) => k.oben === oben)
    const hinten = a.slice(index + 1).every((k) => k.oben > oben)
    return vorne && hinten && index >= 3 ? 'am Zeilenende' : JSON.stringify(a)
  }).toBe('am Zeilenende')
})

test('zweimal öffnen, zweimal „7 Tage" — die Woche wird nur einmal geladen', async ({ page, request }) => {
  // Befund des Prüfers: der Verlauf hängt sich bei jedem Öffnen neu ein und
  // holte jedes Mal die ganze Woche (gut 20 000 Punkte).
  await live(page, request, 1280, '/live/2')
  const wochenAbrufe: string[] = []
  page.on('request', (anfrage) => { if (/\/history\?.*days=7/.test(anfrage.url())) wochenAbrufe.push(anfrage.url()) })
  const kachel = page.locator('.gos-metric.is-clickable').first()
  for (let runde = 0; runde < 2; runde++) {
    await kachel.click()
    const verlauf = page.locator('[data-audit="metric-detail"]')
    await verlauf.locator('[data-audit="verlauf-zeitraum-7t"]').click()
    await expect(verlauf.locator('[data-audit="verlauf-zeitraum-7t"]')).toHaveAttribute('aria-pressed', 'true')
    await expect(verlauf.locator('.vd-status')).toHaveCount(0)
    await page.locator('[data-audit="metric-detail-schliessen"]').click()
    await expect(verlauf).toHaveCount(0)
  }
  expect(wochenAbrufe.length, 'Die Woche wurde gar nicht geladen — dann misst der Fall nichts.').toBeGreaterThanOrEqual(1)
  expect(wochenAbrufe.length, wochenAbrufe.join('\n')).toBe(1)
})
