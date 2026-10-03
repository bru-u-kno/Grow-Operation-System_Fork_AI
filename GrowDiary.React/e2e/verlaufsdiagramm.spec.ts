import { test, expect, type Page, type Locator } from '@playwright/test'
import { backendAntwortet, darfUeberspringen } from './pflicht'

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
    await expect(spanne).toHaveText(spanne24)
    expect(nochmal, 'Die 7 Tage wurden ein zweites Mal geladen.').toBe(false)
    expect(fehler).toEqual([])
  })

  test('◀ blättert um eine Fensterbreite, ▶ ist an „jetzt" gesperrt', async ({ page, request }) => {
    const verlauf = await oeffnen(page, request)
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

    // Die Werte bleiben deutsch.
    for (const wert of await kartenwerte(verlauf)) expect(wert).not.toMatch(/\d\.\d{1,2}(?!\d)/)

    await page.mouse.move(kasten.x + kasten.width / 2, kasten.y - 120)
    await expect(verlauf.locator('[data-audit="verlauf-zeiger"]')).toHaveCount(0)
    expect(await kartenwerte(verlauf)).toEqual(jetzt)
  })

  test('das Mausrad zoomt, der Doppelklick führt zurück auf den Zeitraum', async ({ page, request }) => {
    const verlauf = await oeffnen(page, request, 1280)
    const bild = verlauf.locator('[data-audit="verlauf-bild"]').first()
    const kasten = (await bild.boundingBox())!
    const spanne = verlauf.locator('[data-audit="verlauf-spanne"]')
    const vorher = await spanne.innerText()
    const scroll = await page.evaluate(() => window.scrollY)

    await page.mouse.move(kasten.x + kasten.width * 0.5, kasten.y + kasten.height / 2)
    await page.mouse.wheel(0, -300)
    await expect(spanne).not.toHaveText(vorher)
    await expect(verlauf.locator('.vd-segment[aria-label="Zeitraum"] [aria-pressed="true"]'), 'Nach dem Zoom ist noch ein Zeitraum markiert.').toHaveCount(0)
    expect(await page.evaluate(() => window.scrollY), 'Das Mausrad hat die Seite mitgescrollt.').toBe(scroll)

    await page.mouse.dblclick(kasten.x + kasten.width * 0.5, kasten.y + kasten.height / 2)
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

      const zurueck = fokus.getByRole('button', { name: '← Alle Werte' })
      const kasten = (await zurueck.boundingBox())!
      expect(kasten.height, '„← Alle Werte" ist keine 36 px hoch.').toBeGreaterThanOrEqual(36)
      await zurueck.click()
      await expect(fokus).toHaveCount(0)
      await expect(zeilen, 'Nach „← Alle Werte" fehlen die Zeilen.').toHaveCount(anzahl)
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
      for (const ansicht of ['zusammen', 'einzeln'] as const) {
        await verlauf.locator(`[data-audit="verlauf-darstellung-${ansicht}"]`).click()
        const messung = await verlauf.evaluate((wurzel) => {
          const leinwand = document.createElement('canvas').getContext('2d')!
          const rgba = (farbe: string): number[] => {
            leinwand.clearRect(0, 0, 1, 1)
            leinwand.fillStyle = '#000'
            leinwand.fillStyle = farbe
            leinwand.fillRect(0, 0, 1, 1)
            return Array.from(leinwand.getImageData(0, 0, 1, 1).data).map((v, i) => (i === 3 ? v / 255 : v))
          }
          const mischen = (oben: number[], unten: number[]) => [0, 1, 2].map((i) => oben[i] * oben[3] + unten[i] * (1 - oben[3])).concat(1)
          const grund = (el: Element): number[] => {
            const kette: number[][] = []
            for (let n: Element | null = el; n; n = n.parentElement) {
              const f = rgba(getComputedStyle(n).backgroundColor)
              if (f[3] > 0) kette.push(f)
              if (f[3] >= 1) break
            }
            let ergebnis = rgba(getComputedStyle(document.body).backgroundColor)
            for (const f of kette.reverse()) ergebnis = mischen(f, ergebnis)
            return ergebnis
          }
          const hell = (f: number[]) => {
            const [r, g, b] = f.slice(0, 3).map((v) => { const c = v / 255; return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4 })
            return 0.2126 * r + 0.7152 * g + 0.0722 * b
          }
          const kontrast = (a: number[], b: number[]) => { const [x, y] = [hell(a), hell(b)].sort((p, q) => q - p); return (x + 0.05) / (y + 0.05) }
          const proben: Array<{ was: string; wert: number }> = []
          const achse = wurzel.querySelector('svg text.vd-achse')
          if (achse) proben.push({ was: 'Achsentext', wert: kontrast(rgba(getComputedStyle(achse).fill), grund(achse.closest('svg')!)) })
          const spanne = wurzel.querySelector('.vd-teil')!
          proben.push({ was: 'Spanne', wert: kontrast(rgba(getComputedStyle(spanne).color), grund(spanne)) })
          const aktiv = wurzel.querySelector('.vd-seg[aria-pressed="true"]')!
          proben.push({ was: 'aktiver Knopf', wert: kontrast(rgba(getComputedStyle(aktiv).color), grund(aktiv)) })
          const name = wurzel.querySelector('.vd-wert-name')!
          proben.push({ was: 'Kartenname', wert: kontrast(rgba(getComputedStyle(name).color), grund(name)) })
          const nacht = wurzel.querySelector('.vd-nacht')
          const nachtSichtbar = nacht ? Number(getComputedStyle(nacht).opacity) > 0.02 : false
          return { proben, nachtSichtbar, nachtDa: Boolean(nacht) }
        })
        for (const probe of messung.proben) {
          expect(probe.wert, `${thema}, ${ansicht}: ${probe.was} hat Kontrast ${probe.wert.toFixed(2)}.`).toBeGreaterThanOrEqual(4.5)
        }
        expect(messung.nachtDa, `${thema}, ${ansicht}: keine Dunkelphase gezeichnet, obwohl der Lichtplan 08–20 Uhr gilt.`).toBe(true)
        expect(messung.nachtSichtbar).toBe(true)
      }
      await verlauf.locator('[data-audit="verlauf-darstellung-zusammen"]').click()
      expect(fehler).toEqual([])
    })
  }
})
