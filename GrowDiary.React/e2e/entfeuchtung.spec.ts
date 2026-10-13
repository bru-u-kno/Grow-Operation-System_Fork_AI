import { test, expect, type Page } from '@playwright/test'
import { KONTRAST_HELFER } from './kontrast-messung'
import {
  alleKartenOeffnen, alleOeffnen, aufklappen, backendVorgeben, bandzahlenKollidieren, HAUPT_EINSTELLUNGEN, MODULE,
  ragtHinaus, reiterWahl, schalter, seiteOeffnen, speichern,
} from './entfeuchtung-vorgaben'
import type { Json } from './entfeuchtung-vorgaben'

/**
 * A-009 / A-014 / A-015: Die Seite „Entfeuchtung" — Hauptentfeuchter und Zusatz-Entfeuchter auf einer Seite —
 * und die Namen der Entfeuchter.
 *
 * <b>Womit diese Datei arbeitet.</b> Die Antworten des Backends sind <b>vorgegeben</b> (`page.route`, wie in
 * `ha-offline.spec.ts`): geprüft wird, was die Seite aus der Zusage macht — nicht, ob das Backend sie hält.
 * Die Prüfung läuft ohne Backend und überspringt sich nie.
 *
 * <b>Die Zusagen, um die es geht</b> (ENTSCHEIDUNGEN.md, Punkt 8): Speichern schreibt beim Zusatz NUR geänderte
 * Felder — am 06.10.2026 hat ein Speichern unbemerkt die Tag-Grenze von 26,5 auf 29 °C zurückgesetzt. Der gelbe
 * Kasten davor nennt dieselben Felder. Die Höchsttemperatur wird über den Hauptentfeuchter gespeichert; ein
 * zweites Speichern darf sie nicht auf einen alten Wert zurücksetzen. Und: zweimal speichern muss gehen.
 *
 * <b>Stand A-021 (13.10.2026).</b> Die Seite hat die Reiter Überblick · Einstellungen · Einrichtung („Regel" und
 * „Schutz" sind in „Einstellungen" aufgegangen). Die Antworten und Nutzer-Anlage stehen in
 * `entfeuchtung-vorgaben.ts`; was die Stufen, die Schwellen und den Überblick mit Nutzer-Werten angeht, steht es in
 * `entfeuchtung-einstellungen.spec.ts`. Diese Datei hält die älteren Zusagen: nur geänderte Felder beim Zusatz,
 * Höchsttemperatur ohne Rücksetzen, leere Felder sperren, Warnungen, Handy, Kontrast, Namen.
 */

const TABS = ['Überblick', 'Einstellungen', 'Einrichtung']

const kasten = (page: Page) => page.locator('[data-audit="zusatz-aenderungen"]')
/** Die Höchsttemperatur steht einmal im Reiter „Einstellungen" (Tag oder Nacht). */
const block = (page: Page, titel: 'Tag' | 'Nacht') => page.locator('.ef-tempmax')
  .filter({ has: page.getByRole('radiogroup', { name: `Temperatur max. ${titel}` }) })
const festFeld = (page: Page, titel: 'Tag' | 'Nacht') => block(page, titel).getByLabel('Fester Wert', { exact: true })

test('die Seite lädt: ein Eintrag über der Überschrift, der Überblick ist nur zum Lesen, die Geräte zeigen nur ihren Status', async ({ page }) => {
  await backendVorgeben(page)
  await seiteOeffnen(page)

  // EIN Eintrag „Entfeuchtung" statt zwei Chips, und die Auswahl steht über der Überschrift (08.10.2026).
  const chip = page.getByRole('tab', { name: 'Entfeuchtung' })
  await expect(chip).toHaveAttribute('aria-current', 'true')
  await expect(page.getByRole('tab', { name: 'RDWC Dehumi' })).toHaveCount(0)
  await expect(page.getByRole('tab', { name: 'Dehumi RDWC Tent' })).toHaveCount(0)
  const chipOben = (await chip.boundingBox())!.y
  const titelOben = (await page.getByRole('heading', { name: 'Entfeuchtung', level: 1 }).boundingBox())!.y
  expect(chipOben, 'die Auswahl der Steuerungen steht unter der Überschrift').toBeLessThan(titelOben)
  await expect(page.locator('main .v1-eyebrow').first()).toHaveText('Betrieb')

  // Überblick: Messwerte einmal fürs Zelt — die große Zahl mit dem Urteil in Worten, die ZIEL-Zeile, das Band mit Nadel.
  const karte = page.locator('.ef-band')
  await expect(page.locator('.v1-alert').first()).toContainText('Deutlich daneben')
  const bloecke = karte.locator('.ef-block')
  await expect(bloecke).toHaveCount(2)
  await expect(bloecke.nth(0).locator('.ef-gross')).toContainText('55,2')
  await expect(bloecke.nth(0).locator('.ef-zustand')).toContainText('deutlich über dem Ziel')
  await expect(bloecke.nth(0).locator('.gos-metric-ziel')).toContainText('ZIEL')
  await expect(bloecke.nth(0).locator('.gos-metric-ziel')).toContainText('45 – 51')
  await expect(bloecke.nth(1).locator('.ef-gross')).toContainText('25,1')
  await expect(bloecke.nth(1).locator('.ef-zustand')).toContainText('im Ziel')
  await expect(bloecke.nth(1).locator('.ef-zustand')).toContainText('Entfeuchter dürfen bis 26,5 °C')
  await expect(bloecke.nth(1).locator('.gos-metric-ziel')).toContainText('18 – 26,5')
  await expect(karte.locator('.gos-metric-band')).toHaveCount(2)
  await expect(karte.locator('.gos-metric-band .nadel')).toHaveCount(2)
  await expect(karte.locator('.ef-legende span')).toHaveText(['im Ziel', 'knapp daneben', 'deutlich daneben'])
  // Die alten Skalen mit den Marken „Zusatz" und „Haupt" gibt es im Überblick nicht mehr.
  await expect(page.locator('.ef-skala, .ef-marken')).toHaveCount(0)
  // Wo die Geräte abschalten, steht zugeklappt unter „So liest du das Band" — Höchsttemperatur und früheres Aus des Zusatzes.
  const lesen = karte.locator('.ef-herkunft-box').filter({ hasText: 'So liest du das Band' })
  await expect(lesen.locator('.ef-hk-koerper')).toBeHidden()
  await lesen.locator('.ef-hk-kopf').click()
  await expect(lesen.locator('.ef-hk-koerper')).toBeVisible()
  await expect(lesen.locator('.ef-hk-koerper')).toContainText('bei 26,5 °C gehen alle Entfeuchter aus, der Zusatz schon bei 25,5 °C')

  // Geräte: nur der Status, nie eine Leistung.
  const geraete = page.locator('.st-kk', { hasText: 'Geräte' }).filter({ has: page.getByText('Zusatz-Entfeuchter · hilft') })
  await expect(geraete).toContainText('RDWC Dehumi')
  await expect(geraete).toContainText('Dehumi RDWC Tent')
  await expect(geraete.locator('.st-nurlesen')).toHaveText(['läuft', 'läuft'])
  expect(await geraete.innerText()).not.toMatch(/\d\s*W\b/)

  // Der Überblick hat kein einziges Eingabefeld.
  await expect(page.locator('main input')).toHaveCount(0)

  // Nichts geändert: kein Kasten, kein Speichern.
  await expect(kasten(page)).toHaveCount(0)
  await expect(speichern(page)).toHaveCount(0)

  // Einstellungen: die Höchsttemperatur steht einmal, für alle.
  await reiterWahl(page, 'Einstellungen')
  await expect(festFeld(page, 'Tag')).toHaveValue('26.5')
  await expect(festFeld(page, 'Nacht')).toHaveValue('25')
  await expect(page.getByText('Die Höchsttemperatur gilt für alle Entfeuchter')).toBeVisible()

  // Stufe: eine Wahl für beide Geräte (Haupt-Abstand und -Einschalten, Mindestlauf beider); die Anlage ergibt „Normal".
  // Die Hilfsstärke des Zusatzes bleibt eine eigene Wahl.
  const stufe = page.getByRole('radiogroup', { name: 'Wie reagieren die Entfeuchter?' })
  await expect(stufe.getByRole('radio')).toHaveText(['Ruhig', 'Normal', 'Schnell'])
  await expect(stufe.getByRole('radio', { name: 'Normal', exact: true })).toHaveAttribute('aria-checked', 'true')
  await expect(page.getByRole('radiogroup', { name: 'Hilfsstärke' }).getByRole('radio', { name: 'normal', exact: true })).toHaveAttribute('aria-checked', 'true')

  // Die Reiter heißen so; „Regel" und „Schutz" gibt es nicht mehr.
  await expect(page.locator('.v1-tab')).toHaveText(TABS)

  // Der Zusatz geht wegen VPD oder Feuchte nur aus, wenn das Hauptgerät läuft — als Satz mit dem Namen.
  await expect(page.locator('[data-audit="zusatz-fuehrung-regel"]'))
    .toHaveText('Der Zusatz geht wegen VPD oder Feuchte nur aus, wenn das Hauptgerät RDWC Dehumi läuft.')
  await aufklappen(page, /Erweitert/)
  await expect(page.getByText('Die Meldung läuft in Home Assistant und steht zusätzlich unter Ziele & Meldungen.')).toBeVisible()
  await expect(page.getByText(/Push aufs Handy|Meldungsliste/)).toHaveCount(0)
  await alleKartenOeffnen(page)
  const text = await page.locator('main.v1-page').innerText()
  expect(text).not.toMatch(/Port\s*7|Aufstellung|Kreislauf|Normalbetrieb|Mindestens ein Gerät läuft|So läuft immer eins/i)
})

test('ohne Zusatz-Entfeuchter steht nur der Hauptentfeuchter da — ohne Hilfsstärke und ohne zweite Zeile', async ({ page }) => {
  await backendVorgeben(page, { zusatzAn: null, zusatzOnline: null, leistungW: null, fuehrungAn: null, energieHeuteKwh: null })
  await seiteOeffnen(page)
  await expect(page.getByText('Dehumi RDWC Tent')).toHaveCount(0)
  await expect(page.locator('.st-kk', { hasText: 'Geräte' }).locator('.st-nurlesen')).toHaveText(['läuft'])
  await reiterWahl(page, 'Einstellungen')
  await expect(page.getByRole('radiogroup', { name: 'Hilfsstärke' })).toHaveCount(0)
  await expect(page.locator('.st-kk', { hasText: 'Zusatz-Entfeuchter · Hilfsstärke' })).toHaveCount(0)
  // Die Stufe gilt dann nur für den Hauptentfeuchter: die Tabelle nennt einen Mindestlauf, nicht „Haupt / Zusatz".
  await expect(page.getByRole('radiogroup', { name: 'Wie reagieren die Entfeuchter?' }).getByRole('radio')).toHaveCount(3)
  await expect(page.locator('.ef-stufentabelle').getByRole('rowheader', { name: 'Mindestlauf', exact: true })).toBeVisible()
  await expect(page.locator('.ef-stufentabelle')).not.toContainText('Zusatz')
  // Unter „Erweitert" fehlen die Werte des Zusatzes.
  await aufklappen(page, /Erweitert/)
  await expect(page.getByLabel('Zuschalten nach', { exact: true })).toHaveCount(0)
  await expect(page.getByLabel('Mindestpause', { exact: true })).toHaveCount(0)
  await expect(page.getByLabel('Mindestlaufzeit (RDWC Dehumi)', { exact: true })).toBeVisible()
  await reiterWahl(page, 'Einrichtung')
  const wahl = page.locator('.st-kk', { hasText: 'Welche Entfeuchter hast du?' }).locator('.v1-switch input')
  await expect(wahl).toHaveCount(2)
  await expect(wahl.nth(1), 'der Zusatz ist nicht gewählt').not.toBeChecked()
})

test('Einrichtung: der Nutzer sagt, wie viele Entfeuchter er hat — die Seite und die Übersicht folgen, zweimal', async ({ page }) => {
  const stand = await backendVorgeben(page)
  await seiteOeffnen(page)
  await reiterWahl(page, 'Einrichtung')
  const geraete = page.locator('.st-kk', { hasText: 'Welche Entfeuchter hast du?' }).locator('.v1-switch')
  await expect(geraete).toHaveCount(2)
  const haupt = geraete.nth(0).locator('input')
  const zusatz = geraete.nth(1).locator('input')
  await expect(haupt).toBeDisabled()
  await expect(zusatz, 'der Zusatz lässt sich an- und abwählen').toBeEnabled()
  await expect(zusatz).toBeChecked()
  await expect(page.locator('main').getByRole('link', { name: /Geräte & Entitäten/ })).toBeVisible()

  // Durchgang 1: „Ich habe keinen Zusatz." — gespeichert wird sofort, die Seite folgt.
  await zusatz.uncheck()
  await expect(page.getByText('Der Zusatz-Entfeuchter läuft in Home Assistant weiter')).toBeVisible()
  expect(stand.einrichtungPuts).toEqual([{ zusatzVorhanden: false }])
  await expect(page.getByText('Haupt- und Zusatz-Entfeuchter arbeiten zusammen')).toHaveCount(0)
  await reiterWahl(page, 'Einstellungen')
  await expect(page.getByRole('radiogroup', { name: 'Hilfsstärke' })).toHaveCount(0)
  await reiterWahl(page, 'Überblick')
  await expect(page.locator('.st-kk', { hasText: 'Geräte' }).locator('.st-nurlesen')).toHaveText(['läuft'])
  // …und nach dem Neuladen bleibt es so.
  await page.reload({ waitUntil: 'networkidle' })
  await reiterWahl(page, 'Überblick')
  await expect(page.locator('.st-kk', { hasText: 'Geräte' }).locator('.st-nurlesen')).toHaveText(['läuft'])

  // Durchgang 2, ohne den Test neu zu starten: wieder „ja", dann „automatisch".
  await reiterWahl(page, 'Einrichtung')
  await zusatz.check()
  expect(stand.einrichtungPuts.at(-1)).toEqual({ zusatzVorhanden: true })
  await expect(page.locator('.st-kk', { hasText: 'Wie arbeiten die Entfeuchter?' })).toBeVisible()
  await page.getByRole('button', { name: 'Automatisch' }).click()
  expect(stand.einrichtungPuts.at(-1)).toEqual({ zusatzVorhanden: null })
  await expect(page.getByRole('button', { name: 'Automatisch' })).toHaveCount(0)
})

test('Übersicht der Steuerungen: „Entfeuchtung" ist EINE Zeile, und ihre zwei Spalten überdecken sich am Handy nicht', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 900 })
  const stand = await backendVorgeben(page)
  // Echte Namen aus Nutzer-Anlage — lang genug, um die Zeile zu sprengen (08.10.2026, Screenshot).
  await page.unroute(/\/api\/steuerung$/)
  await page.route(/\/api\/steuerung$/, (route) => route.fulfill({ json: { ...MODULE, module: [
    MODULE.module[0],
    { kennung: 'entfeuchter', titel: 'RDWC Dehumi', status: 'an', kurz: 'VPD-Modus · ein ab 45,8 % · aus unter 43,8 %', wert: '52,9 %', unterzeile: 'entfeuchtet · VPD 1,26', hatDetail: true },
    { kennung: 'entfeuchter-zusatz', titel: 'Dehumi RDWC Tent', status: 'an', kurz: 'normal · Automatik an · folgt RDWC Dehumi', wert: '0 W', unterzeile: 'bereit', hatDetail: true },
  ] } }))
  await page.goto('/steuerung', { waitUntil: 'networkidle' })
  const zeile = page.locator('.st-zeile', { hasText: 'Entfeuchtung' })
  await expect(zeile).toHaveCount(1)
  await expect(page.locator('.st-zeile', { hasText: 'Dehumi RDWC Tent' })).toHaveCount(0)
  await expect(zeile).toContainText('Zusatz bereit')
  const ueberdeckt = await zeile.evaluate((el) => {
    const kasten = (sel: string) => { const r = document.createRange(); r.selectNodeContents(el.querySelector(sel)!); return r.getBoundingClientRect() }
    const a = kasten('.st-titel'), b = kasten('.st-wert')
    return a.left < b.right - 0.5 && b.left < a.right - 0.5 && a.top < b.bottom - 0.5 && b.top < a.bottom - 0.5
  })
  expect(ueberdeckt, 'Titel und Wert der Zeile liegen übereinander').toBe(false)

  // Hat der Nutzer „kein Zusatz" gesagt, steht nur noch das Hauptgerät in der Zeile.
  stand.einrichtung = { zusatzVorhanden: false }
  await page.reload({ waitUntil: 'networkidle' })
  await expect(page.locator('.st-zeile', { hasText: 'Entfeuchtung' })).not.toContainText('Zusatz')
})

test('Luftfeuchte-Zone: das Ziel ist die Plan-Obergrenze — auf der ganzen Seite dieselbe Zone', async ({ page }) => {
  // 07.10.2026, echte Anlage: 48,1 % rF, EIN-Schwelle 45,8 %, Plan-Obergrenze 51 %.
  const werte = { feuchteProzent: 48.1, tempC: 23.5 }
  await backendVorgeben(page, { ...werte, feuchteEinProzent: 45.8, feuchteAusProzent: 43.8, rhObergrenzeProzent: 51 }, { ...werte, einAktivProzent: 45.8, ausAktivProzent: 43.8, rhObergrenzeProzent: 51 })
  await seiteOeffnen(page)
  await expect(page.locator('.v1-alert').first()).toContainText('Im Ziel')
  const nadeln = page.locator('.ef-band .gos-metric-band .nadel')
  await expect(nadeln).toHaveCount(2)
  for (let i = 0; i < await nadeln.count(); i++) await expect(nadeln.nth(i)).toHaveClass(/\bist-ok\b/)
  await expect(page.locator('.ef-band .ef-block').first().locator('.ef-zustand')).toContainText('im Ziel')

  // Zweiter Durchgang, ohne Plan-Obergrenze: dann gilt die EIN-Schwelle — und 48,1 % sind „knapp daneben".
  await page.unroute(/\/api\/steuerung\/entfeuchter$/)
  await page.unroute(/\/api\/steuerung\/entfeuchter-zusatz$/)
  await backendVorgeben(page, { ...werte, feuchteEinProzent: 45.8 }, { ...werte, einAktivProzent: 45.8, ausAktivProzent: 43.8, rhObergrenzeProzent: null })
  await page.reload({ waitUntil: 'networkidle' })
  await expect(page.locator('.v1-alert').first()).toContainText('Knapp daneben')
  await expect(page.locator('.ef-band .gos-metric-band .nadel').first()).toHaveClass(/\bist-warn\b/)
  await expect(page.locator('.ef-band .ef-block').first().locator('.ef-zustand')).toContainText('knapp über dem Ziel')
  // Das Ziel ist jetzt die EIN-Schwelle (45,8 → „45,8"), nicht mehr die Plan-Obergrenze.
  await expect(page.locator('.ef-band .ef-block').first().locator('.gos-metric-ziel')).toContainText('45 – 45,8')
})

test('ohne Verbindung zu Home Assistant steht nur der HA-Hinweis, nie zusätzlich „Plan unvollständig"', async ({ page }) => {
  await backendVorgeben(page, { haErreichbar: false, planUnvollstaendig: true, schaltgroesse: 'keine' }, { haErreichbar: false })
  await seiteOeffnen(page)
  const seite = page.locator('main.v1-page')
  await expect(seite).toContainText('Home Assistant antwortet nicht')
  await expect(seite).not.toContainText('Plan unvollständig')
})

test('„zieht nichts" erscheint nur, wenn der Server es meldet — auch bei kleiner Leistung', async ({ page }) => {
  await backendVorgeben(page, { ziehtNichts: false, leistungW: 3 })
  await seiteOeffnen(page)
  await expect(page.locator('main.v1-page')).not.toContainText('zieht nichts')
})

test('einen Wert des Zusatzes ändern: der gelbe Kasten nennt nur dieses Feld, Speichern schickt nur dieses Feld — zweimal', async ({ page }) => {
  const stand = await backendVorgeben(page)
  await seiteOeffnen(page)
  await reiterWahl(page, 'Einstellungen')

  // Durchgang 1: Mindestpause 10 → 12 (unter „Erweitert"). Die Hilfsstärke wird dabei zu „eigene Werte".
  await aufklappen(page, /Erweitert/)
  await page.getByLabel('Mindestpause', { exact: true }).fill('12')
  await expect(kasten(page)).toBeVisible()
  await expect(kasten(page).getByText('Wird gespeichert — nur das:')).toBeVisible()
  await expect(kasten(page)).toContainText('Hilfsstärke: normal → eigene Werte')
  await speichern(page).click()
  await expect(page.getByText('Gespeichert.')).toBeVisible()
  expect(stand.puts).toEqual([{ mindestpauseMin: 12 }])
  expect(stand.hauptPuts, 'der Hauptentfeuchter wurde nicht angefasst').toEqual([])
  // Nach dem Speichern: Kasten und Knopf weg, das Feld trägt die Antwort.
  await expect(kasten(page)).toHaveCount(0)
  await expect(speichern(page)).toHaveCount(0)
  await expect(page.getByLabel('Mindestpause', { exact: true })).toHaveValue('12')

  // Durchgang 2, ohne Neuladen: ein anderes Feld. Der Körper darf die Mindestpause nicht mehr enthalten.
  await page.getByLabel('Geht früher aus um', { exact: true }).fill('2')
  await speichern(page).click()
  await expect(page.getByText('Gespeichert.')).toBeVisible()
  expect(stand.puts.at(-1)).toEqual({ folgeAbstandK: 2 })
})

test('die Höchsttemperatur geht über den Hauptentfeuchter — und das zweite Speichern setzt die erste nicht zurück', async ({ page }) => {
  const stand = await backendVorgeben(page)
  await seiteOeffnen(page)
  await reiterWahl(page, 'Einstellungen')

  // Durchgang 1: Tag-Grenze 26,5 → 27.
  await festFeld(page, 'Tag').fill('27')
  await speichern(page).click()
  await expect(page.getByText('Gespeichert.')).toBeVisible()
  expect(stand.hauptPuts).toHaveLength(1)
  expect(stand.hauptPuts[0]).toMatchObject({ tempMaxTagFestC: 27, tempMaxNachtFestC: 25, hystereseProzent: 4 })
  // Der Hauptentfeuchter schickt ALLE Felder, nicht nur das eine (sein Vertrag: er ersetzt alles).
  expect(Object.keys(stand.hauptPuts[0]).sort()).toEqual(Object.keys(HAUPT_EINSTELLUNGEN).sort())
  expect(stand.puts, 'der Zusatz schreibt die Höchsttemperatur nicht mit').toEqual([])
  await expect(festFeld(page, 'Tag')).toHaveValue('27')

  // Durchgang 2, ohne Neuladen: die NACHT-Grenze. Die Tag-Grenze darf nicht auf 26,5 zurückspringen (06.10.2026!).
  await festFeld(page, 'Nacht').fill('24')
  await speichern(page).click()
  await expect(page.getByText('Gespeichert.')).toBeVisible()
  expect(stand.hauptPuts).toHaveLength(2)
  expect(stand.hauptPuts[1]).toMatchObject({ tempMaxTagFestC: 27, tempMaxNachtFestC: 24 })
  expect(stand.einstellungen).toMatchObject({ tempMaxTagFestC: 27, tempMaxNachtFestC: 24 })
})

test('Hilfsstärke „sparsam": der Kasten nennt sie samt den Einzelwerten, der Körper schickt sie — und „aus" nur sich selbst', async ({ page }) => {
  const stand = await backendVorgeben(page)
  await seiteOeffnen(page)
  await reiterWahl(page, 'Einstellungen')
  const hilfe = page.getByRole('radiogroup', { name: 'Hilfsstärke' })

  await hilfe.getByRole('radio', { name: 'sparsam', exact: true }).click()
  await expect(kasten(page).locator('li')).toHaveCount(1)
  await expect(kasten(page).locator('li')).toContainText('Hilfsstärke: normal → sparsam')
  await expect(kasten(page).locator('li')).toContainText('Zusatz geht früher aus: 1,0 → 1,5 K')
  await speichern(page).click()
  await expect(page.getByText('Gespeichert.')).toBeVisible()
  expect(stand.puts[0]).toEqual({ hilfe: 'sparsam', folgeAbstandK: 1.5, vpdHystereseKpa: 0.25, zuschaltVerzoegerungMin: 20, mindestpauseMin: 15 })

  // Zweiter Durchgang: auf „aus" und sofort speichern — nur das Wort, nicht die Einzelwerte von „sparsam".
  await hilfe.getByRole('radio', { name: 'aus', exact: true }).click()
  await speichern(page).click()
  await expect(page.getByText('Gespeichert.')).toBeVisible()
  expect(stand.puts[1]).toEqual({ hilfe: 'aus' })
})

test('ein geleertes Feld sperrt das Speichern und wird markiert, auch in einer zugeklappten Kachel — zweimal', async ({ page }) => {
  const stand = await backendVorgeben(page)
  await seiteOeffnen(page)
  await reiterWahl(page, 'Einstellungen')

  // Durchgang 1: ein Feld des Zusatzes unter „Erweitert" leeren, die Kachel zuklappen, auf einen anderen Reiter.
  const pause = page.getByLabel('Mindestpause', { exact: true })
  await aufklappen(page, /Erweitert/)
  await pause.fill('')
  await page.locator('.st-kk-kopf', { hasText: /Erweitert/ }).click()
  await expect(pause).toBeHidden()
  await reiterWahl(page, 'Einrichtung')
  await speichern(page).click()
  await expect(page.getByText('Bitte die markierten Felder prüfen.')).toBeVisible()
  // Der Reiter „Einstellungen" ist wieder offen, die Kachel hat sich geöffnet, das Feld trägt die Markierung.
  await expect(page.locator('.v1-tab.active')).toHaveText('Einstellungen')
  await expect(pause).toBeVisible()
  await expect(page.locator('.st-fehler')).toHaveText('Bitte eine Zahl eintragen.')
  expect(stand.puts).toEqual([])
  expect(stand.hauptPuts).toEqual([])

  // Wieder füllen: die Markierung verschwindet, Speichern geht.
  await pause.fill('12')
  await speichern(page).click()
  await expect(page.getByText('Gespeichert.')).toBeVisible()
  await expect(page.locator('.st-fehler')).toHaveCount(0)
  expect(stand.puts).toEqual([{ mindestpauseMin: 12 }])

  // Durchgang 2, ohne Neuladen: ein Feld des Hauptentfeuchters („Einschalten über" in der Kachel „Ziel · Luftfeuchte"),
  // diesmal in zugeklappter Kachel auf einem anderen Reiter. Bei VPD-Regelung ist es gesperrt — erst auf Luftfeuchte stellen.
  await reiterWahl(page, 'Einstellungen')
  await aufklappen(page, /Erweitert/)
  await schalter(page, 'Nach VPD regeln statt nach Luftfeuchte').uncheck()
  const einNacht = page.getByLabel('Einschalten über (Nacht)', { exact: true })
  await einNacht.fill('')
  await page.locator('.st-kk-kopf', { hasText: /Ziel · Luftfeuchte/ }).click()
  await expect(einNacht).toBeHidden()
  await reiterWahl(page, 'Überblick')
  await expect(page.locator('main.v1-page')).not.toContainText('NaN')
  await speichern(page).click()
  await expect(page.getByText('Bitte die markierten Felder prüfen.')).toBeVisible()
  await expect(page.locator('.v1-tab.active')).toHaveText('Einstellungen')
  await expect(einNacht).toBeVisible()
  await expect(einNacht.locator('xpath=ancestor::*[contains(@class,"st-feldzeile")][1]').locator('.st-fehler')).toHaveText('Bitte eine Zahl eintragen.')
  expect(stand.hauptPuts).toEqual([])
})

test('Warnungen: „zieht nichts" mit Namen und Leistung, „Plan unvollständig", Zelt zu warm', async ({ page }) => {
  await backendVorgeben(page, { ziehtNichts: true, leistungW: 3, planUnvollstaendig: true, schaltgroesse: 'keine', vpdZiel: null, vpdEinSchwelle: null, vpdAusSchwelle: null, zusatzAn: false, tempC: 27.6 })
  await seiteOeffnen(page)
  const main = page.locator('main.v1-page')
  await expect(main).toContainText('Dehumi RDWC Tent zieht nichts')
  await expect(main).toContainText('nur 3 W')
  await expect(main).toContainText('Plan unvollständig')
  await expect(main).toContainText('Dehumi RDWC Tent pausiert: Zelt zu warm')
  await expect(main).toContainText('unter 24,5 °C')
})

test('fehlende Werte stehen als „–", nie als erfundene Null', async ({ page }) => {
  const leer = { vpd: null, tempC: null, feuchteProzent: null }
  await backendVorgeben(page, { ...leer, leistungW: null, energieHeuteKwh: null, zusatzAn: null, zusatzOnline: true }, { ...leer, portAn: null })
  await seiteOeffnen(page)
  const karte = page.locator('.ef-band')
  await expect(karte.locator('.ef-gross').first()).toContainText('–')
  await expect(karte.locator('.ef-gross').nth(1)).toContainText('–')
  await expect(karte.locator('.ef-zustand')).toHaveText([/keine Aussage/, /keine Aussage/])
  // Keine Nadel ohne Messwert, keine „Verbrauch heute"-Kachel ohne Werte, Geräte „unbekannt".
  await expect(karte.locator('.gos-metric-band .nadel')).toHaveCount(0)
  await expect(page.locator('main.v1-page')).not.toContainText(/NaN|undefined/)
  await expect(page.getByText(/Verbrauch heute/)).toHaveCount(0)
  await expect(page.locator('.st-kk', { hasText: 'Geräte' }).locator('.st-nurlesen').first()).toHaveText('unbekannt')
})

for (const breite of [320, 390]) {
  test(`Handy ${breite} px: nichts ragt über den Rand — auf allen drei Reitern, mit offenen Kacheln, offenem „Woher kommt das?", Kasten und Warnungen`, async ({ page }) => {
    await page.setViewportSize({ width: breite, height: 800 })
    await backendVorgeben(page, { ziehtNichts: true, leistungW: 3, zusatzAn: false, tempC: 27.6 })
    await seiteOeffnen(page)
    await reiterWahl(page, 'Einstellungen')
    await festFeld(page, 'Tag').fill('27')
    await page.getByRole('radiogroup', { name: 'Hilfsstärke' }).getByRole('radio', { name: 'sparsam', exact: true }).click()

    for (const reiter of TABS) {
      await reiterWahl(page, reiter)
      await alleOeffnen(page)
      // Nichts bleibt zugeklappt, sonst misst die Prüfung nur, was ohnehin im Bild steht.
      await expect(page.locator('.st-kk-kopf[aria-expanded="false"], .ef-hk-kopf[aria-expanded="false"]')).toHaveCount(0)

      const ueberstand = await ragtHinaus(page)
      expect(ueberstand, `${reiter}: ragt bei ${breite} px rechts hinaus`).toEqual([])
      expect(await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1),
        `${reiter}: die Seite scrollt bei ${breite} px seitwärts`).toBe(false)

      // Die Zahlen unter den Bändern (die Grenzen) liegen nicht übereinander (Text, nicht Kasten messen).
      const kollisionen = await bandzahlenKollidieren(page)
      expect(kollisionen, `${reiter}: Bandbeschriftungen überlappen bei ${breite} px`).toEqual([])
      if (reiter === 'Überblick') expect(await page.locator('.gos-metric-band .skala span').count(), 'der Überblick zeigt keine Grenzzahlen unter dem Band').toBeGreaterThan(0)
    }
  })
}

for (const schema of ['light', 'dark'] as const) {
  test(`Lesbarkeit der Seite in der ${schema === 'light' ? 'hellen' : 'dunklen'} Ansicht (Kontrast mindestens 4,5)`, async ({ page }) => {
    await page.emulateMedia({ colorScheme: schema })
    await page.addInitScript((s) => localStorage.setItem('growos.theme', s), schema)
    await backendVorgeben(page, { ziehtNichts: true, leistungW: 3, zusatzAn: false, tempC: 27.6 })
    await seiteOeffnen(page)
    await reiterWahl(page, 'Einstellungen')
    // Zustände, die sonst keiner ansieht: geänderte Stufe und Hilfsstärke, der gelbe Kasten, die Warnung bei zu hoher
    // Höchsttemperatur, ein markiertes Feld samt roter Meldung.
    await page.getByRole('radiogroup', { name: 'Wie reagieren die Entfeuchter?' }).getByRole('radio', { name: 'Schnell', exact: true }).click()
    await page.getByRole('radiogroup', { name: 'Hilfsstärke' }).getByRole('radio', { name: 'sparsam', exact: true }).click()
    await block(page, 'Tag').getByRole('radio', { name: 'Fest' }).click()
    await festFeld(page, 'Tag').fill('28')
    await expect(page.locator('.ez-aender')).toBeVisible()
    await expect(page.locator('.ef-stufentabelle .is-gewaehlt').first()).toBeVisible()
    await aufklappen(page, /Erweitert/)
    await page.getByLabel('Mindestpause', { exact: true }).fill('')
    await speichern(page).click()
    await expect(page.locator('.st-fehler')).toHaveText('Bitte eine Zahl eintragen.')
    await expect(page.getByText('Bitte die markierten Felder prüfen.')).toBeVisible()

    for (const reiter of TABS) {
      await reiterWahl(page, reiter)
      await alleOeffnen(page)
      const funde = await page.evaluate(`(() => {
        ${KONTRAST_HELFER}
        const funde = []
        for (const el of document.querySelectorAll('main *')) {
          const eigen = [...el.childNodes].filter((k) => k.nodeType === 3).map((k) => k.textContent).join('').trim()
          if (!eigen) continue
          const s = getComputedStyle(el)
          if (s.visibility === 'hidden' || s.display === 'none') continue
          if (el.closest('[disabled], [aria-disabled="true"], .is-disabled, [hidden], .is-ruht')) continue
          const grund = flaeche(el)
          const vorne = alsRgb(s.color, grund)
          const l1 = lum(vorne), l2 = lum(grund)
          const k = (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05)
          const px = parseFloat(s.fontSize)
          const gross = px >= 24 || (px >= 18.66 && Number(s.fontWeight) >= 700)
          if (k < (gross ? 3 : 4.5)) funde.push((el.className || el.tagName) + ' — ' + eigen.slice(0, 40) + ' — ' + k.toFixed(2))
        }
        return [...new Set(funde)]
      })()`) as string[]
      expect(funde, `${reiter}: zu wenig Kontrast`).toEqual([])
    }
  })
}

test('Namen: die Geräte-Seite zeigt die Namen mit Vorgabe, speichert nur den geänderten — zweimal', async ({ page }) => {
  const namen = {
    fuehrung: { anzeigename: 'RDWC Dehumi', vorgabe: 'RDWC Dehumi' },
    zusatz: { anzeigename: 'Dehumi RDWC Tent', vorgabe: 'Dehumi RDWC Tent' },
  }
  const puts: Json[] = []
  await page.route(/\/api\/steuerung\/entfeuchter-namen$/, async (route) => {
    if (route.request().method() === 'PUT') {
      const k = route.request().postDataJSON() as Record<string, string>
      puts.push(k)
      for (const rolle of ['fuehrung', 'zusatz'] as const) {
        if (rolle in k) namen[rolle].anzeigename = k[rolle] === '' ? namen[rolle].vorgabe : k[rolle]
      }
    }
    await route.fulfill({ json: namen })
  })
  await page.route(/\/api\/geraete$/, (route) => route.fulfill({
    json: {
      anzahlGeraete: 1, anzahlEntitaeten: 1, anzahlUnzugeordnet: 0, anzahlVerschoben: 0, hinweise: [],
      geraete: [{
        schluessel: 'g1', name: 'RDWC Dehumi', elternSchluessel: null, anschluss: null, istController: false, istRubrik: false, istUnzugeordnet: false,
        elternVomNutzer: false, nameVomNutzer: false, abgeleiteterEltern: null, modell: null,
        tentId: null, hardwareItemId: null, entitaeten: [],
      }],
    },
  }))
  await page.route(/\/api\/steuerung\/geraete$/, (route) => route.fulfill({
    json: {
      haErreichbar: true, eigene: [],
      module: [
        { modul: 'entfeuchter', titel: 'Entfeuchter', zeilen: [] },
        { modul: 'entfeuchter-zusatz', titel: 'Dehumi RDWC Tent', zeilen: [] },
      ],
    },
  }))
  await page.route(/\/api\/home-assistant\/entities$/, (route) => route.fulfill({ json: [] }))

  await page.goto('/geraete?reiter=rollen&modul=entfeuchter', { waitUntil: 'networkidle' })
  await expect(page.getByRole('heading', { name: 'Namen der Entfeuchter' })).toBeVisible()
  const eins = page.getByLabel('Name von Entfeuchter 1 · führt', { exact: true })
  const zwei = page.getByLabel('Name von Entfeuchter 2 · Zusatz', { exact: true })
  await expect(eins).toHaveValue('RDWC Dehumi')
  await expect(page.getByText('Vorgabe aus Home Assistant: Dehumi RDWC Tent')).toBeVisible()
  const knopf = page.getByRole('button', { name: 'Namen speichern' })
  await expect(knopf).toBeDisabled()

  // Durchgang 1: nur der Zusatz.
  await zwei.fill('Zelt-Entfeuchter')
  await knopf.click()
  await expect(page.getByText('Gespeichert.')).toBeVisible()
  expect(puts).toEqual([{ zusatz: 'Zelt-Entfeuchter' }])
  await expect(knopf).toBeDisabled()

  // Durchgang 2, ohne Neuladen: nur die Führung — der Zusatz steht schon in der Antwort und fehlt im Körper.
  await eins.fill('Hauptgerät')
  await knopf.click()
  await expect(knopf).toBeDisabled()
  expect(puts).toEqual([{ zusatz: 'Zelt-Entfeuchter' }, { fuehrung: 'Hauptgerät' }])

  // Zurück auf die Vorgabe: leer geschickt.
  await page.locator('.st-feldzeile', { hasText: 'Entfeuchter 2 · Zusatz' }).getByRole('button', { name: 'Vorgabe', exact: true }).click()
  await knopf.click()
  await expect(knopf).toBeDisabled()
  expect(puts.at(-1)).toEqual({ zusatz: '' })
})
