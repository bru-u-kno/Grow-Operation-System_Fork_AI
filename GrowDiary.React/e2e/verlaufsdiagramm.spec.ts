import { test, expect, type Page, type Locator } from '@playwright/test'
import { backendAntwortet, darfUeberspringen } from './pflicht'
import { KONTRAST_HELFER } from './kontrast-messung'

/**
 * Das Verlaufsdiagramm der Live-Seite (Verlaufsdiagramm.tsx).
 *
 * **Der Anlass (03.10.2026).** Das alte Diagramm zeichnete auf 900 × 260 px
 * und liess den Browser auf die Handybreite stauchen (`preserveAspectRatio=
 * "none"`), und der Zeiger hörte nur auf die Maus. Der Nutzer hat als Ersatz
 * die Fassung nach dem Vorbild der AC-Infinity-App gewählt, dazu „Einzeln" mit
 * Zielband und den Fokus auf einen Wert.
 *
 * **Was hier geprüft wird, und warum hier.** Die Rechnung (Spanne, Zoom,
 * Lücken, Dunkelphase, Zielband) prüft `verlauf-modell.test.ts`. Hier steht,
 * was erst im Browser entsteht: ob ein Tipp die Kurve wirklich ausblendet, ob
 * das Bild in echten Pixeln gezeichnet ist, ob die Spanne in der echten
 * Schrift sauber bricht und ob die Seite am Telefon überläuft.
 *
 * Die Kachel gibt es nur in einer eigenen Anordnung — der Demobestand legt sie
 * für das erste Zelt an (`Demobestand.VerlaufsKachelAnlegen`). Fehlt sie, ist
 * das im strengen Lauf ein Fehler und kein Übersprung.
 */

async function oeffnen(page: Page, request: Parameters<typeof backendAntwortet>[0], breite = 390): Promise<Locator> {
  darfUeberspringen(!(await backendAntwortet(request)), 'Kein Backend — das Verlaufsdiagramm braucht die laufende App mit Demobestand.')
  await page.setViewportSize({ width: breite, height: 900 })
  await page.goto('/', { waitUntil: 'networkidle' })
  const verlauf = page.locator('[data-audit="verlauf"]')
  const da = await verlauf.count() > 0
  darfUeberspringen(!da, 'Keine Verlaufs-Kachel auf der Live-Seite, obwohl der Demobestand eine anlegt (Demobestand.VerlaufsKachelAnlegen).')
  await verlauf.scrollIntoViewIfNeeded()
  // Mengenwaechter: ohne Karten und Kurve misst keiner der Faelle etwas.
  await expect(verlauf.locator('.vd-wert').first()).toBeVisible()
  expect(await verlauf.locator('.vd-wert').count(), 'Weniger als zwei Wertekarten — der Bestand liefert zu wenig Verlauf.').toBeGreaterThanOrEqual(2)
  await expect(verlauf.locator('[data-audit="verlauf-bild"]').first()).toBeVisible()
  return verlauf
}

/** Die eingeschalteten Karten, als Schlüssel. */
async function eingeschaltet(verlauf: Locator): Promise<string[]> {
  return verlauf.locator('.vd-wert[aria-pressed="true"]').evaluateAll((knoepfe) =>
    knoepfe.map((k) => (k.getAttribute('data-audit') ?? '').replace('verlauf-karte-', '')))
}

async function kartenwerte(verlauf: Locator): Promise<string[]> {
  return verlauf.locator('[data-audit="verlauf-kartenwert"]').allInnerTexts()
}

function konsoleSammeln(page: Page): string[] {
  const fehler: string[] = []
  page.on('console', (m) => { if (m.type() === 'error') fehler.push(m.text()) })
  page.on('pageerror', (e) => fehler.push(String(e)))
  return fehler
}

test.describe('Verlaufsdiagramm', () => {
  test('eine Karte blendet ihre Kurve aus und wieder ein — zweimal, auch nach Wegscrollen, und gemerkt', async ({ page, request }) => {
    const fehler = konsoleSammeln(page)
    const verlauf = await oeffnen(page, request)
    const an = await eingeschaltet(verlauf)
    expect(an.length, 'Keine Kurve ist anfangs eingeschaltet — die Kachel-Werte kommen nicht an.').toBeGreaterThanOrEqual(2)

    // Erst die erste, dann die letzte eingeschaltete Karte (CLAUDE.md: den
    // ersten Eintrag, dann den letzten), dazwischen weggescrollt.
    for (const [runde, key] of [an[0], an[an.length - 1]].entries()) {
      if (runde === 1) {
        await page.evaluate(() => window.scrollTo(0, 0))
        await verlauf.scrollIntoViewIfNeeded()
      }
      const karte = verlauf.locator(`[data-audit="verlauf-karte-${key}"]`)
      const kurve = verlauf.locator(`[data-audit="verlauf-kurve-${key}"]`)
      await expect(kurve, `Die Kurve ${key} fehlt, obwohl ihre Karte eingeschaltet ist.`).toHaveCount(1)

      await karte.click()
      await expect(karte).toHaveAttribute('aria-pressed', 'false')
      await expect(kurve, `Runde ${runde + 1}: Nach dem Tipp auf die Karte ${key} steht ihre Kurve noch da.`).toHaveCount(0)
      await expect(verlauf.locator('[data-audit="verlauf-tabelle"]')).not.toContainText(await karte.locator('.vd-wert-name').innerText())

      await karte.click()
      await expect(karte).toHaveAttribute('aria-pressed', 'true')
      await expect(kurve, `Runde ${runde + 1}: Der zweite Tipp bringt die Kurve ${key} nicht zurück.`).toHaveCount(1)
    }

    // Gemerkt: ausblenden, neu laden, bleibt aus.
    await verlauf.locator(`[data-audit="verlauf-karte-${an[0]}"]`).click()
    await page.reload({ waitUntil: 'networkidle' })
    await expect(page.locator(`[data-audit="verlauf-karte-${an[0]}"]`)).toHaveAttribute('aria-pressed', 'false')
    await page.locator(`[data-audit="verlauf-karte-${an[0]}"]`).click()
    expect(fehler).toEqual([])
  })

  test('„6 Std" und „7 Tage" ändern Spanne und Kurve', async ({ page, request }) => {
    const fehler = konsoleSammeln(page)
    const verlauf = await oeffnen(page, request)
    const key = (await eingeschaltet(verlauf))[0]
    const spanne = verlauf.locator('[data-audit="verlauf-spanne"]')
    const kurve = verlauf.locator(`[data-audit="verlauf-kurve-${key}"]`)
    await expect(verlauf.locator('[data-audit="verlauf-zeitraum-24h"]')).toHaveAttribute('aria-pressed', 'true')
    const spanne24 = await spanne.innerText()
    const kurve24 = await kurve.getAttribute('d')

    await verlauf.locator('[data-audit="verlauf-zeitraum-6h"]').click()
    await expect(verlauf.locator('[data-audit="verlauf-zeitraum-6h"]')).toHaveAttribute('aria-pressed', 'true')
    await expect(spanne).not.toHaveText(spanne24)
    expect(await kurve.getAttribute('d'), '6 Std zeichnet dieselbe Kurve wie 24 Std.').not.toBe(kurve24)

    const wochenAbruf = page.waitForRequest((r) => /\/history\?.*days=7/.test(r.url()))
    await verlauf.locator('[data-audit="verlauf-zeitraum-7t"]').click()
    await wochenAbruf
    await expect(verlauf.locator('.vd-status')).toHaveCount(0, { timeout: 15_000 })
    await expect(verlauf.locator('[data-audit="verlauf-zeitraum-7t"]')).toHaveAttribute('aria-pressed', 'true')
    // Länger als zwei Tage: nur die Tage, ohne Uhrzeit.
    await expect(spanne).toHaveText(/^[A-Z][a-z] \d\d\.\d\d\. –\s+[A-Z][a-z] \d\d\.\d\d\.$/)
    const kurve7 = await kurve.getAttribute('d')
    expect(kurve7).not.toBe(kurve24)
    // Sieben Tage haben sieben Nächte; mindestens sechs davon liegen ganz im Bild.
    expect(await verlauf.locator('[data-audit="verlauf-nacht"]').count()).toBeGreaterThanOrEqual(6)

    // Zurück auf 24 Std: nicht noch einmal laden.
    let nochmal = false
    page.on('request', (r) => { if (/days=7/.test(r.url())) nochmal = true })
    await verlauf.locator('[data-audit="verlauf-zeitraum-24h"]').click()
    // Nicht wörtlich dieselbe Spanne: vor dem Nachladen reicht sie nur bis zum
    // ersten der 24-h-Punkte, danach volle 24 h. Wieder Tag UND Uhrzeit.
    await expect(spanne).toHaveText(/^[A-Z][a-z] \d\d\.\d\d\. \d\d:\d\d –\s+[A-Z][a-z] \d\d\.\d\d\. \d\d:\d\d$/)
    expect(nochmal, 'Die 7 Tage wurden ein zweites Mal geladen.').toBe(false)
    expect(fehler).toEqual([])
  })

  test('◀ blättert um eine Fensterbreite, ▶ ist an „jetzt" gesperrt', async ({ page, request }) => {
    const verlauf = await oeffnen(page, request)
    for (const name of ['Früher', 'Später']) {
      const k = (await verlauf.getByRole('button', { name }).boundingBox())!
      expect(Math.min(k.width, k.height), `„${name}" ist kleiner als 44 × 44 px.`).toBeGreaterThanOrEqual(44)
    }
    await verlauf.locator('[data-audit="verlauf-zeitraum-6h"]').click()
    const spanne = verlauf.locator('[data-audit="verlauf-spanne"]')
    const vorher = await spanne.innerText()
    await expect(verlauf.getByRole('button', { name: 'Später' })).toBeDisabled()
    await verlauf.getByRole('button', { name: 'Früher' }).click()
    await expect(spanne).not.toHaveText(vorher)
    await expect(verlauf.locator('[data-audit="verlauf-zeitraum-6h"]'), 'Nach dem Blättern ist „6 Std" nicht mehr markiert, obwohl die Breite gleich blieb.')
      .toHaveAttribute('aria-pressed', 'true')
    await expect(verlauf.getByRole('button', { name: 'Später' })).toBeEnabled()
    await verlauf.getByRole('button', { name: 'Später' }).click()
    await expect(spanne).toHaveText(vorher)
  })

  test('der Mauszeiger zeigt die Uhrzeit und setzt die Kartenwerte', async ({ page, request }) => {
    const verlauf = await oeffnen(page, request, 1280)
    const bild = verlauf.locator('[data-audit="verlauf-bild"]').first()
    const kasten = (await bild.boundingBox())!
    expect(kasten.width, 'Das Diagramm ist keine 200 px breit — jeder Zeiger träfe denselben Punkt.').toBeGreaterThan(200)
    const jetzt = await kartenwerte(verlauf)

    await page.mouse.move(kasten.x + kasten.width * 0.3, kasten.y + kasten.height / 2)
    const zeit = verlauf.locator('[data-audit="verlauf-zeigerzeit"]')
    await expect(zeit).toHaveText(/^\d\d:\d\d$/)
    const links = await kartenwerte(verlauf)
    expect(links, 'Unter dem Zeiger zeigen die Karten noch die Werte von jetzt.').not.toEqual(jetzt)
    // SVG-Text hat kein innerText — textContent.
    const zeitLinks = (await zeit.textContent()) ?? ''

    await page.mouse.move(kasten.x + kasten.width * 0.8, kasten.y + kasten.height / 2)
    await expect(zeit).not.toHaveText(zeitLinks)
    expect(await kartenwerte(verlauf)).not.toEqual(links)

    // Die Werte bleiben deutsch — und ohne Tausenderpunkt, wie die Kacheln („1020 ppm").
    for (const wert of await kartenwerte(verlauf)) {
      expect(wert).not.toMatch(/\d\.\d{1,2}(?!\d)/)
      expect(wert, `„${wert}" trägt einen Tausenderpunkt, die Kachel daneben nicht.`).not.toMatch(/\d\.\d{3}/)
    }

    // Die Uhrzeit am Zeiger liegt auf einem Schild ÜBER den Kurven: ein
    // deckender Grund hinter dem Text, im Dokument NACH allen Kurven gemalt.
    const schild = await verlauf.locator('[data-audit="verlauf-zeigerzeit"]').evaluate((text) => {
      const grund = text.parentElement?.querySelector('rect.vd-schild')
      if (!grund) return 'kein Grund hinter der Uhrzeit'
      const t = text.getBoundingClientRect()
      const g = grund.getBoundingClientRect()
      if (g.left > t.left || g.right < t.right || g.top > t.top || g.bottom < t.bottom) return 'der Grund deckt die Uhrzeit nicht ab'
      if (Number(getComputedStyle(grund).fillOpacity) < 0.8) return 'der Grund ist durchsichtig'
      const svg = text.closest('svg')!
      const spaeter = Array.from(svg.querySelectorAll('path')).every((p) => p.compareDocumentPosition(grund) & Node.DOCUMENT_POSITION_FOLLOWING)
      return spaeter ? 'ok' : 'eine Kurve wird nach dem Schild gemalt'
    })
    expect(schild, 'Die Uhrzeit am Zeiger kann von Kurven überdeckt werden.').toBe('ok')

    await page.mouse.move(kasten.x + kasten.width / 2, kasten.y - 120)
    await expect(verlauf.locator('[data-audit="verlauf-zeiger"]')).toHaveCount(0)
    expect(await kartenwerte(verlauf)).toEqual(jetzt)
  })

  test('das Mausrad scrollt die Seite, Strg+Rad zoomt, der Doppelklick führt zurück', async ({ page, request }) => {
    const verlauf = await oeffnen(page, request, 1280)
    const bild = verlauf.locator('[data-audit="verlauf-bild"]').first()
    await bild.scrollIntoViewIfNeeded()
    const kasten = (await bild.boundingBox())!
    const spanne = verlauf.locator('[data-audit="verlauf-spanne"]')
    const vorher = await spanne.innerText()
    await page.mouse.move(kasten.x + kasten.width * 0.5, kasten.y + kasten.height / 2)

    // Einfaches Rad: die Seite scrollt, das Diagramm bleibt.
    const scrollVorher = await page.evaluate(() => window.scrollY)
    await page.mouse.wheel(0, 200)
    await expect.poll(() => page.evaluate(() => window.scrollY), { message: 'Das einfache Mausrad über dem Diagramm scrollt die Seite nicht.' })
      .toBeGreaterThan(scrollVorher)
    await expect(spanne).toHaveText(vorher)
    await bild.scrollIntoViewIfNeeded()
    const k2 = (await bild.boundingBox())!

    // Strg + Rad: Zoom um die Mausposition, die Seite steht.
    const scroll = await page.evaluate(() => window.scrollY)
    await page.mouse.move(k2.x + k2.width * 0.5, k2.y + k2.height / 2)
    await page.keyboard.down('Control')
    await page.mouse.wheel(0, -300)
    await page.keyboard.up('Control')
    await expect(spanne).not.toHaveText(vorher)
    await expect(verlauf.locator('.vd-segment[aria-label="Zeitraum"] [aria-pressed="true"]'), 'Nach dem Zoom ist noch ein Zeitraum markiert.').toHaveCount(0)
    expect(await page.evaluate(() => window.scrollY), 'Strg+Rad hat die Seite mitgescrollt.').toBe(scroll)

    await page.mouse.dblclick(k2.x + k2.width * 0.5, k2.y + k2.height / 2)
    await expect(spanne).toHaveText(vorher)
    await expect(verlauf.locator('[data-audit="verlauf-zeitraum-24h"]')).toHaveAttribute('aria-pressed', 'true')
  })

  test('Finger: zwei Finger zoomen und verschieben, einer zeigt, Doppeltipp führt zurück — ohne die Seite zu scrollen', async ({ browser, request, browserName }) => {
    // Echte Berührungen gibt es in Playwright nur über das Chrome-Protokoll.
    // `page.touchscreen` kennt nur einen Finger und keine Bewegung.
    darfUeberspringen(browserName !== 'chromium', 'Mehrfinger-Berührungen gibt es nur über das Chrome-Protokoll.')
    const ctx = await browser.newContext({ viewport: { width: 360, height: 800 }, hasTouch: true, isMobile: true, baseURL: test.info().project.use.baseURL })
    const seite = await ctx.newPage()
    darfUeberspringen(!(await backendAntwortet(request)), 'Kein Backend.')
    await seite.goto('/', { waitUntil: 'networkidle' })
    const verlauf = seite.locator('[data-audit="verlauf"]')
    darfUeberspringen(await verlauf.count() === 0, 'Keine Verlaufs-Kachel, obwohl der Demobestand eine anlegt.')
    await verlauf.scrollIntoViewIfNeeded()
    const kasten = (await verlauf.locator('[data-audit="verlauf-bild"]').first().boundingBox())!
    const cdp = await ctx.newCDPSession(seite)
    const y = kasten.y + kasten.height / 2
    const mitte = kasten.x + kasten.width / 2
    const zwei = (abstand: number, versatz = 0) => [{ x: mitte - abstand + versatz, y, id: 1 }, { x: mitte + abstand + versatz, y, id: 2 }]
    const spanne = verlauf.locator('[data-audit="verlauf-spanne"]')
    const vorher = await spanne.innerText()
    const scroll = await seite.evaluate(() => window.scrollY)

    // Spreizen: von 40 auf 200 px Abstand → ein Fünftel der Breite.
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: zwei(20) })
    for (let d = 25; d <= 100; d += 5) await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: zwei(d) })
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] })
    await expect(spanne).not.toHaveText(vorher)
    const gezoomt = await spanne.innerText()
    await expect(verlauf.locator('.vd-segment[aria-label="Zeitraum"] [aria-pressed="true"]')).toHaveCount(0)
    expect(await seite.evaluate(() => window.scrollY), 'Die Zwei-Finger-Geste hat die Seite gescrollt.').toBe(scroll)

    // Verschieben: beide Finger 80 px nach rechts → früherer Ausschnitt, gleiche Breite.
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: zwei(40) })
    for (let dx = 10; dx <= 80; dx += 10) await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: zwei(40, dx) })
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] })
    await expect(spanne).not.toHaveText(gezoomt)

    // Ein Finger: Zeiger, wandert mit, verschwindet beim Loslassen.
    const zeit = verlauf.locator('[data-audit="verlauf-zeigerzeit"]')
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x: mitte - 60, y, id: 3 }] })
    await expect(zeit).toHaveCount(1)
    const zeitLinks = await zeit.textContent()
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x: mitte + 60, y, id: 3 }] })
    await expect(zeit).not.toHaveText(zeitLinks ?? '')
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] })
    await expect(zeit).toHaveCount(0)

    // Doppeltipp: zurück auf 24 Std.
    for (let i = 0; i < 2; i++) {
      await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x: mitte, y, id: 4 }] })
      await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] })
    }
    await expect(verlauf.locator('[data-audit="verlauf-zeitraum-24h"]')).toHaveAttribute('aria-pressed', 'true')

    // Ein Finger SENKRECHT auf dem Diagramm: die Seite scrollt (touch-action: pan-y).
    // Mit `none` blieb die Seite hier stehen — wer am Telefon über das
    // Diagramm hinweg scrollen wollte, kam nicht weiter.
    await seite.waitForTimeout(400)
    const scrollWisch = await seite.evaluate(() => window.scrollY)
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x: mitte, y: y + 60, id: 5 }] })
    for (let dy = 10; dy <= 160; dy += 10) await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x: mitte, y: y + 60 - dy, id: 5 }] })
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] })
    await expect.poll(() => seite.evaluate(() => window.scrollY), { message: 'Ein senkrechter Wisch über das Diagramm scrollt die Seite nicht.' })
      .toBeGreaterThan(scrollWisch + 40)
    await ctx.close()
  })

  test('kein gestauchtes Bild: die viewBox ist so breit wie das gezeichnete SVG', async ({ page, request }) => {
    const verlauf = await oeffnen(page, request, 320)
    for (const darstellung of ['zusammen', 'einzeln'] as const) {
      await verlauf.locator(`[data-audit="verlauf-darstellung-${darstellung}"]`).click()
      for (const breite of [320, 390, 768, 1280]) {
        await page.setViewportSize({ width: breite, height: 900 })
        await page.waitForTimeout(250)
        const bilder = await verlauf.locator('[data-audit="verlauf-bild"]').evaluateAll((svgs) => svgs.map((svg) => ({
          viewBox: (svg as SVGSVGElement).viewBox.baseVal.width,
          gerendert: svg.getBoundingClientRect().width,
          hoeheViewBox: (svg as SVGSVGElement).viewBox.baseVal.height,
          hoehe: svg.getBoundingClientRect().height,
          stauchen: svg.getAttribute('preserveAspectRatio'),
        })))
        expect(bilder.length, `${darstellung} bei ${breite} px: kein Diagramm gefunden.`).toBeGreaterThanOrEqual(1)
        for (const b of bilder) {
          expect(b.stauchen, 'preserveAspectRatio="none" staucht das Bild wieder.').not.toBe('none')
          expect(Math.abs(b.viewBox - b.gerendert), `${darstellung} bei ${breite} px: viewBox ${b.viewBox} px, gezeichnet ${b.gerendert} px.`).toBeLessThanOrEqual(1)
          expect(Math.abs(b.hoeheViewBox - b.hoehe)).toBeLessThanOrEqual(1)
        }
      }
    }
  })

  test('die Spanne bricht nur am Strich, nie mitten in einem Teil (320 / 360 / 390 px)', async ({ page, request }) => {
    const verlauf = await oeffnen(page, request, 320)
    let umbrueche = 0
    for (const breite of [320, 360, 390]) {
      await page.setViewportSize({ width: breite, height: 900 })
      for (const zeitraum of ['1h', '6h', '24h', '7t']) {
        await verlauf.locator(`[data-audit="verlauf-zeitraum-${zeitraum}"]`).click()
        await expect(verlauf.locator('.vd-status')).toHaveCount(0, { timeout: 15_000 })
        const messung = await verlauf.locator('[data-audit="verlauf-spanne"]').evaluate((spanne) => {
          const huelle = spanne.getBoundingClientRect()
          const teile = Array.from(spanne.querySelectorAll('.vd-teil')).map((teil) => {
            const bereich = document.createRange()
            bereich.selectNodeContents(teil)
            const rects = Array.from(bereich.getClientRects()).filter((r) => r.width > 0)
            return {
              text: teil.textContent ?? '',
              zeilen: new Set(rects.map((r) => Math.round(r.top))).size,
              links: Math.min(...rects.map((r) => r.left)),
              rechts: Math.max(...rects.map((r) => r.right)),
              oben: Math.min(...rects.map((r) => r.top)),
            }
          })
          return { teile, links: huelle.left, rechts: huelle.right }
        })
        expect(messung.teile.length).toBeGreaterThanOrEqual(2)
        for (const teil of messung.teile) {
          expect(teil.zeilen, `${breite} px, ${zeitraum}: „${teil.text}" ist über ${teil.zeilen} Zeilen gebrochen.`).toBe(1)
          expect(teil.links, `${breite} px, ${zeitraum}: „${teil.text}" ragt links aus der Spanne.`).toBeGreaterThanOrEqual(messung.links - 0.5)
          expect(teil.rechts, `${breite} px, ${zeitraum}: „${teil.text}" ragt rechts aus der Spanne.`).toBeLessThanOrEqual(messung.rechts + 0.5)
        }
        if (new Set(messung.teile.map((t) => Math.round(t.oben))).size > 1) umbrueche++
      }
    }
    // Mengenwaechter: bei 320 px reicht die Breite für die lange Spanne nicht —
    // gäbe es keinen einzigen Umbruch, hätte dieser Fall den Bruch nie gesehen.
    expect(umbrueche, 'Die Spanne brach bei keiner Breite um — die Prüfung hat den Umbruch nie gesehen.').toBeGreaterThanOrEqual(1)
  })

  test('Zusammen → Einzeln → Zusammen, zweimal: eine Zeile je eingeschalteter Kurve', async ({ page, request }) => {
    const fehler = konsoleSammeln(page)
    const verlauf = await oeffnen(page, request)
    const zeilen = verlauf.locator('.vd-zeile[data-audit^="verlauf-zeile-"]')
    for (let runde = 1; runde <= 2; runde++) {
      if (runde === 2) {
        // Erschwert: eine Kurve mehr eingeschaltet und weggescrollt.
        const aus = verlauf.locator('.vd-wert[aria-pressed="false"]').first()
        if (await aus.count()) await aus.click()
        await page.evaluate(() => window.scrollTo(0, 0))
        await verlauf.scrollIntoViewIfNeeded()
      }
      const an = await eingeschaltet(verlauf)
      await verlauf.locator('[data-audit="verlauf-darstellung-einzeln"]').click()
      await expect(verlauf.locator('[data-audit="verlauf-darstellung-einzeln"]')).toHaveAttribute('aria-pressed', 'true')
      await expect(zeilen, `Runde ${runde}: ${an.length} Kurven eingeschaltet, aber nicht so viele Zeilen.`).toHaveCount(an.length)
      for (const key of an) await expect(verlauf.locator(`[data-audit="verlauf-zeile-${key}"] [data-audit="verlauf-kurve-${key}"]`)).toHaveCount(1)

      await verlauf.locator('[data-audit="verlauf-darstellung-zusammen"]').click()
      await expect(zeilen, `Runde ${runde}: In „Zusammen" stehen noch Zeilen.`).toHaveCount(0)
      await expect(verlauf.locator('[data-audit="verlauf-bild"]')).toHaveCount(1)
    }
    // Gemerkt: Einzeln bleibt nach dem Neuladen Einzeln.
    await verlauf.locator('[data-audit="verlauf-darstellung-einzeln"]').click()
    await page.reload({ waitUntil: 'networkidle' })
    await expect(page.locator('[data-audit="verlauf-darstellung-einzeln"]')).toHaveAttribute('aria-pressed', 'true')
    await page.locator('[data-audit="verlauf-darstellung-zusammen"]').click()
    expect(fehler).toEqual([])
  })

  test('Fokus: eine Zeile groß, „← Alle Werte" zurück, dann eine andere Zeile — und Karten wechseln den Wert', async ({ page, request }) => {
    const verlauf = await oeffnen(page, request)
    await verlauf.locator('[data-audit="verlauf-darstellung-einzeln"]').click()
    const zeilen = verlauf.locator('.vd-zeile[data-audit^="verlauf-zeile-"]')
    const anzahl = await zeilen.count()
    expect(anzahl).toBeGreaterThanOrEqual(2)
    const fokus = verlauf.locator('[data-audit="verlauf-fokus"]')

    for (const index of [0, anzahl - 1]) {
      const zeile = zeilen.nth(index)
      const key = ((await zeile.getAttribute('data-audit')) ?? '').replace('verlauf-zeile-', '')
      const name = (await zeile.locator('.vd-zeile-name').innerText()).trim()
      await zeile.locator('.vd-zeile-kopf').click()
      await expect(fokus, `Der Tipp auf „${name}" öffnet keinen Fokus.`).toBeVisible()
      await expect(fokus.locator('.vd-fokus-name')).toHaveText(name)
      await expect(fokus.locator(`[data-audit="verlauf-kurve-${key}"]`)).toHaveCount(1)
      // Echte y-Achse: mindestens drei Teilstriche mit deutscher Zahl.
      const striche = await fokus.locator('svg text.vd-achse').allTextContents()
      expect(striche.filter((t) => /^-?\d+(,\d+)?$/.test(t.replace(/\./g, ''))).length).toBeGreaterThanOrEqual(3)
      await expect(zeilen).toHaveCount(0)
      // Wortlaut des Urteils (falls es ein Ziel gibt): nie mehr „außerhalb bis 50 %".
      const urteil = fokus.locator('[data-audit="verlauf-zielstatus"]')
      if (await urteil.count()) await expect(urteil).toHaveText(/^(im Ziel|über dem Ziel|unter dem Ziel) \(.+\)$|^Soll /)

      const zurueck = fokus.getByRole('button', { name: '← Alle Werte' })
      const kasten = (await zurueck.boundingBox())!
      expect(kasten.height, '„← Alle Werte" ist keine 36 px hoch.').toBeGreaterThanOrEqual(36)
      await zurueck.click()
      await expect(fokus).toHaveCount(0)
      await expect(zeilen, 'Nach „← Alle Werte" fehlen die Zeilen.').toHaveCount(anzahl)
    }

    // Der Wortlaut am Zielband: die Luftfeuchte des Demobestands hat „höchstens
    // 50 %" — dort stand vorher „außerhalb bis 50 %".
    const feuchte = verlauf.locator('[data-audit="verlauf-zeile-humidity"] .vd-zeile-kopf')
    if (await feuchte.count()) {
      await feuchte.click()
      await expect(fokus.locator('[data-audit="verlauf-zielstatus"]')).toHaveText(/^(im Ziel|über dem Ziel|unter dem Ziel) \(bis 50 %\)$/)
      await fokus.getByRole('button', { name: '← Alle Werte' }).click()
    }

    // Im Fokus wechselt eine Karte den Wert, statt Kurven auszublenden.
    await zeilen.first().locator('.vd-zeile-kopf').click()
    // Fest über die Kennung: ein Locator „nicht gedrückt" zeigte nach dem
    // Tipp schon auf eine andere Karte.
    const andereKennung = await verlauf.locator('.vd-wert[aria-pressed="false"]').last().getAttribute('data-audit')
    const andere = verlauf.locator(`[data-audit="${andereKennung}"]`)
    const andererName = (await andere.locator('.vd-wert-name').innerText()).trim()
    await andere.click()
    await expect(fokus.locator('.vd-fokus-name')).toHaveText(andererName)
    await expect(andere).toHaveAttribute('aria-pressed', 'true')
    await fokus.getByRole('button', { name: '← Alle Werte' }).click()
    // Die Auswahl der Kurven hat der Wechsel nicht angefasst.
    await expect(zeilen).toHaveCount(anzahl)
    await verlauf.locator('[data-audit="verlauf-darstellung-zusammen"]').click()
  })

  test('der Zeiger in „Einzeln" setzt die Werte ALLER Zeilen', async ({ page, request }) => {
    const verlauf = await oeffnen(page, request, 1280)
    await verlauf.locator('[data-audit="verlauf-darstellung-einzeln"]').click()
    const zeilen = verlauf.locator('.vd-zeile[data-audit^="verlauf-zeile-"]')
    const anzahl = await zeilen.count()
    expect(anzahl).toBeGreaterThanOrEqual(2)
    const werte = verlauf.locator('[data-audit="verlauf-zeilenwert"]')
    const jetzt = await werte.allInnerTexts()
    await expect(verlauf.locator('[data-audit="verlauf-zeilen-zeit"]')).toHaveText(/^jetzt · \d\d:\d\d$/)

    // Auf die LETZTE Zeile zeigen — die erste soll trotzdem folgen.
    const kasten = (await zeilen.last().locator('[data-audit="verlauf-bild"]').boundingBox())!
    await page.mouse.move(kasten.x + kasten.width * 0.3, kasten.y + kasten.height / 2)
    await expect(verlauf.locator('[data-audit="verlauf-zeilen-zeit"]')).toHaveText(/^\d\d:\d\d$/)
    await expect(verlauf.locator('[data-audit="verlauf-zeiger"]'), 'Nicht jede Zeile zeigt den Zeiger.').toHaveCount(anzahl)
    const unterZeiger = await werte.allInnerTexts()
    const geaendert = unterZeiger.filter((wert, i) => wert !== jetzt[i]).length
    expect(geaendert, `Nur ${geaendert} von ${anzahl} Zeilen folgen dem Zeiger.`).toBeGreaterThanOrEqual(anzahl - 1)
    await verlauf.locator('[data-audit="verlauf-darstellung-zusammen"]').click()
  })

  test('eine gemerkte Auswahl für ANDERE Kachel-Werte überstimmt die Kachel nicht', async ({ page, request }) => {
    darfUeberspringen(!(await backendAntwortet(request)), 'Kein Backend.')
    const anordnung = await (await request.get('/api/tents/1/dashboard')).json() as { sections: Array<{ tiles: Array<{ id: string; kind: string; metricKeys?: string[] }> }> }
    const kachel = anordnung.sections.flatMap((s) => s.tiles).find((t) => t.kind === 'Chart')
    darfUeberspringen(!kachel, 'Keine Verlaufs-Kachel im Demobestand.')
    const werte = kachel!.metricKeys ?? []
    expect(werte.length).toBeGreaterThanOrEqual(2)
    // Gemerkt wurde einmal „nur PPFD" — für eine Kachel, die damals andere
    // Werte hatte, und in der alten Ablage ohne Fingerabdruck.
    await page.addInitScript(({ id }) => {
      try {
        localStorage.setItem(`growos.verlauf.${id}`, JSON.stringify({ an: ['ppfd'] }))
        localStorage.setItem(`growos.verlauf.${id}.ppfd,temperature`, JSON.stringify({ an: ['ppfd'] }))
      } catch { /* egal */ }
    }, { id: kachel!.id })
    const verlauf = await oeffnen(page, request)
    expect((await eingeschaltet(verlauf)).sort(), 'Eine alte Auswahl hat die Werte der Kachel überstimmt.').toEqual([...werte].sort())
  })

  test('vor dem Nachladen zeigt die Leiste nur die 24 h; ◀ am Rand lädt die 7 Tage nach', async ({ page, request }) => {
    let wochenAbrufe = 0
    page.on('request', (r) => { if (/\/history\?.*days=7/.test(r.url())) wochenAbrufe++ })
    const verlauf = await oeffnen(page, request)
    const leiste = verlauf.locator('[data-audit="verlauf-leiste"]')
    const anteil = () => leiste.evaluate((svg) => {
      const rahmen = svg.querySelector('.vd-fenster')!.getBoundingClientRect()
      return rahmen.width / svg.getBoundingClientRect().width
    })
    expect(wochenAbrufe, 'Die Grundansicht hat die 7 Tage geladen.').toBe(0)
    expect(await anteil(), 'Vor dem Nachladen steht in der Leiste ein kleiner Kasten vor leerem Vorlauf.').toBeGreaterThan(0.9)

    const frueher = verlauf.getByRole('button', { name: 'Früher' })
    await expect(frueher, '◀ ist am Rand der 24 h gesperrt — älter kommt man dann nie.').toBeEnabled()
    const abruf = page.waitForRequest((r) => /days=7/.test(r.url()))
    await frueher.click()
    await abruf
    await expect(verlauf.locator('.vd-status')).toHaveCount(0, { timeout: 15_000 })
    await verlauf.locator('[data-audit="verlauf-zeitraum-24h"]').click()
    await expect.poll(anteil, { message: 'Nach dem Nachladen ist die Leiste nicht auf 7 Tage gewachsen.' }).toBeLessThan(0.3)
    expect(wochenAbrufe).toBe(1)
  })

  test('am Telefon: kein Überlauf (320–768 px) und kein abgeschnittener Text in den Zeilen', async ({ page, request }) => {
    const verlauf = await oeffnen(page, request, 320)
    for (const ansicht of ['zusammen', 'einzeln', 'fokus'] as const) {
      await verlauf.locator(`[data-audit="verlauf-darstellung-${ansicht === 'fokus' ? 'einzeln' : ansicht}"]`).click()
      if (ansicht === 'fokus') await verlauf.locator('.vd-zeile-kopf').first().click()
      for (const breite of [320, 360, 390, 414, 600, 768]) {
        await page.setViewportSize({ width: breite, height: 900 })
        await page.waitForTimeout(200)
        const ueberlauf = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
        expect(ueberlauf, `${ansicht} bei ${breite} px: die Seite läuft um ${ueberlauf} px über.`).toBeLessThanOrEqual(0)
        const kachel = await verlauf.evaluate((e) => { const r = e.getBoundingClientRect(); return { links: r.left, rechts: r.right } })
        expect(kachel.rechts, `${ansicht} bei ${breite} px: die Kachel ragt über den Rand.`).toBeLessThanOrEqual(breite)

        // Text in Zeilen und Fokus: der gemalte Text, nicht der Kasten.
        const abgeschnitten = await verlauf.evaluate((wurzel) => {
          const raus: string[] = []
          for (const el of Array.from(wurzel.querySelectorAll('.vd-zeile-kopf > span:not(.sr-only), .vd-gross > span, .vd-wert > span, .vd-fokus-kopf > *'))) {
            const kasten = (el.closest('.vd-zeile-kopf, .vd-wert, .vd-fokus, .vd-gross') ?? el).getBoundingClientRect()
            const bereich = document.createRange()
            bereich.selectNodeContents(el)
            for (const r of Array.from(bereich.getClientRects())) {
              if (r.width === 0) continue
              if (r.right > kasten.right + 0.5 || r.left < kasten.left - 0.5) raus.push(`„${(el.textContent ?? '').trim()}" ${Math.round(r.left)}–${Math.round(r.right)} in ${Math.round(kasten.left)}–${Math.round(kasten.right)}`)
            }
          }
          return raus
        })
        expect(abgeschnitten, `${ansicht} bei ${breite} px:\n${abgeschnitten.join('\n')}`).toEqual([])
      }
      if (ansicht === 'fokus') await verlauf.getByRole('button', { name: '← Alle Werte' }).click()
    }
    await verlauf.locator('[data-audit="verlauf-darstellung-zusammen"]').click()
  })

  for (const thema of ['light', 'dark'] as const) {
    test(`Thema ${thema}: Achsen, Spanne und aktiver Knopf sind lesbar, die Nacht ist sichtbar`, async ({ page, request }) => {
      const fehler = konsoleSammeln(page)
      await page.addInitScript((t) => { try { localStorage.setItem('growos.theme', t) } catch { /* egal */ } }, thema)
      const verlauf = await oeffnen(page, request, 360)
      await expect(page.locator('html')).toHaveAttribute('data-theme', thema)
      // Alle Karten einschalten: die hellen Kurvenfarben (Gelb, Grün, Türkis)
      // sind im Bestand anfangs aus — gerade deren Rand muss 3:1 halten.
      // Ein Locator „nicht gedrückt" schrumpft mit jedem Tipp; erst die
      // Kennungen holen, dann tippen.
      const aus = await verlauf.locator('.vd-wert[aria-pressed="false"]').evaluateAll((k) => k.map((e) => e.getAttribute('data-audit')))
      for (const kennung of aus) await verlauf.locator(`[data-audit="${kennung}"]`).click()
      expect(await verlauf.locator('.vd-wert[aria-pressed="true"]').count()).toBeGreaterThanOrEqual(8)
      for (const ansicht of ['zusammen', 'einzeln'] as const) {
        await verlauf.locator(`[data-audit="verlauf-darstellung-${ansicht}"]`).click()
        // Die Messung aus kontrast.spec.ts (KONTRAST_HELFER): die SCHRIFT wird
        // samt Deckkraft über ihren Grund gemalt, nicht nur der Grund gemischt.
        const messung = await page.evaluate(`(() => {
          ${KONTRAST_HELFER}
          const wurzel = document.querySelector('[data-audit="verlauf"]')
          const kontrast = (vorne, grund) => {
            const l1 = lum(vorne), l2 = lum(grund)
            return (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05)
          }
          const schrift = (el, farbe) => { const grund = flaeche(el); return kontrast(alsRgb(farbe, grund), grund) }
          const proben = []
          const achse = wurzel.querySelector('svg text.vd-achse')
          if (achse) proben.push({ was: 'Achsentext', wert: schrift(achse.closest('svg'), getComputedStyle(achse).fill), mindestens: 4.5 })
          const spanne = wurzel.querySelector('.vd-teil')
          proben.push({ was: 'Spanne', wert: schrift(spanne, getComputedStyle(spanne).color), mindestens: 4.5 })
          const aktiv = wurzel.querySelector('.vd-seg[aria-pressed="true"]')
          proben.push({ was: 'aktiver Knopf', wert: schrift(aktiv, getComputedStyle(aktiv).color), mindestens: 4.5 })
          for (const name of wurzel.querySelectorAll('.vd-wert-name')) {
            proben.push({ was: 'Kartenname ' + name.textContent.trim(), wert: schrift(name, getComputedStyle(name).color), mindestens: 4.5 })
          }
          // Der Rand einer EINGESCHALTETEN Karte trägt die Aussage „an" — er
          // braucht als Bedienelement 3:1 zum Grund (WCAG 1.4.11).
          for (const karte of wurzel.querySelectorAll('.vd-wert[aria-pressed="true"]')) {
            const grund = flaeche(karte)
            proben.push({ was: 'Rand der Karte ' + karte.querySelector('.vd-wert-name').textContent.trim(),
              wert: kontrast(alsRgb(getComputedStyle(karte).borderTopColor, grund), grund), mindestens: 3 })
          }
          const nacht = wurzel.querySelector('.vd-nacht')
          return { proben, nachtDa: Boolean(nacht), nachtSichtbar: nacht ? Number(getComputedStyle(nacht).opacity) > 0.02 : false }
        })()`) as { proben: Array<{ was: string; wert: number; mindestens: number }>; nachtDa: boolean; nachtSichtbar: boolean }
        expect(messung.proben.length, 'Die Kontrastmessung hat fast nichts gefunden.').toBeGreaterThanOrEqual(6)
        for (const probe of messung.proben) {
          expect(probe.wert, `${thema}, ${ansicht}: ${probe.was} hat Kontrast ${probe.wert.toFixed(2)}.`).toBeGreaterThanOrEqual(probe.mindestens)
        }
        expect(messung.nachtDa, `${thema}, ${ansicht}: keine Dunkelphase gezeichnet, obwohl der Lichtplan 08–20 Uhr gilt.`).toBe(true)
        expect(messung.nachtSichtbar).toBe(true)
      }
      await verlauf.locator('[data-audit="verlauf-darstellung-zusammen"]').click()
      expect(fehler).toEqual([])
    })
  }
})

/**
 * Die Dunkelphase liegt dort, wo die Messwerte Nacht zeigen — auch wenn der
 * Browser in einer anderen Zone steht als der Server.
 *
 * Die Schaltzeiten bildet der Server in SEINER Zone (bzw. der des Lichtplans).
 * Das Diagramm las sie als Browserzeit: Server in UTC, Browser in Berlin, und
 * der graue Streifen begann um 20:00, während PPFD erst um 22:00 auf 0 fiel
 * (Befund des Prüfers, 03.10.2026). Geprüft wird gegen die Messwerte selbst:
 * innerhalb jeder Nacht ist PPFD 0, ausserhalb nicht.
 */
test.describe('Verlaufsdiagramm in anderer Zeitzone', () => {
  test.use({ timezoneId: 'Europe/Berlin' })

  test('die Dunkelphase deckt sich mit PPFD 0 aus dem Demobestand', async ({ page, request }) => {
    const verlauf = await oeffnen(page, request, 1280)
    const live = await (await request.get('/api/live/tents/1')).json() as { metrics: Array<{ key: string; lightUtcOffsetMinutes?: number | null }> }
    const versatz = live.metrics.find((m) => m.key === 'light-cycle')?.lightUtcOffsetMinutes
    expect(versatz, 'Der Server schickt keine Zone zu den Lichtzeiten.').not.toBeNull()
    const browserVersatz = await page.evaluate(() => -new Date().getTimezoneOffset())
    // Mengenwaechter: ohne Unterschied der Zonen prüft dieser Fall nichts.
    expect(browserVersatz, 'Browser und Lichtplan liegen in derselben Zone — der Fall misst nichts.').not.toBe(versatz)

    const naechte = await verlauf.locator('[data-audit="verlauf-nacht"]').evaluateAll((rects) =>
      rects.map((r) => ({ von: Number(r.getAttribute('data-von')), bis: Number(r.getAttribute('data-bis')) })))
    expect(naechte.length).toBeGreaterThanOrEqual(1)

    const verlaufPpfd = await (await request.get('/api/tents/1/history?metrics=ppfd&days=1&resolution=raw')).json() as { series: Array<{ points: Array<{ t: string; v: number }> }> }
    const punkte = verlaufPpfd.series[0].points.map((p) => ({ t: new Date(p.t).getTime(), v: p.v }))
    const von = Math.min(...naechte.map((n) => n.von))
    const bis = Math.max(...naechte.map((n) => n.bis))
    const RAND = 20 * 60_000
    let geprueft = 0
    const fehler: string[] = []
    for (const p of punkte) {
      if (p.t < von - 12 * 3600_000 || p.t > bis + 12 * 3600_000) continue
      const nacht = naechte.some((n) => p.t > n.von + RAND && p.t < n.bis - RAND)
      const tag = naechte.every((n) => p.t < n.von - RAND || p.t > n.bis + RAND)
      if (nacht && p.v !== 0) fehler.push(`${new Date(p.t).toISOString()} im grauen Streifen, PPFD ${p.v}`)
      if (tag && p.v === 0) fehler.push(`${new Date(p.t).toISOString()} ausserhalb der Nacht, PPFD 0`)
      if (nacht || tag) geprueft++
    }
    expect(geprueft, 'Zu wenige PPFD-Werte geprüft.').toBeGreaterThanOrEqual(20)
    expect(fehler, fehler.slice(0, 6).join('\n')).toEqual([])
  })
})
