import { test, expect, type Locator, type Page } from '@playwright/test'
import {
  alleOeffnen, aufklappen, backendVorgeben, bandzahlenKollidieren, BRU_HAUPT_EINSTELLUNGEN, BRU_HAUPT_LIVE,
  BRU_ZIELWERTE, BRU_ZUSATZ_EINSTELLUNGEN, BRU_ZUSATZ_LIVE, EINSTELLUNGEN, HAUPT_EINSTELLUNGEN,
  ragtHinaus, reiterWahl, schalter, seiteOeffnen, speichern,
} from './entfeuchtung-vorgaben'
import type { Stand, Vorgaben } from './entfeuchtung-vorgaben'

/**
 * A-021: Die Seite „Entfeuchtung" nach dem Umbau — Reiter „Einstellungen" (Stufe, Ziel, Betrieb) und der neue Überblick.
 *
 * <b>Womit diese Datei arbeitet.</b> Wie `entfeuchtung.spec.ts`: Die Antworten des Backends sind vorgegeben
 * (`entfeuchtung-vorgaben.ts`), geprüft wird, was die Seite daraus macht und was sie beim Speichern schickt. Sie läuft
 * ohne Backend und überspringt sich nie.
 *
 * <b>Die Zusagen, um die es geht</b> (13.10.2026):
 * <ul>
 * <li>Eine Wahl „Ruhig · Normal · Schnell" setzt Abstand, „Einschalten nach" und Mindestlaufzeit des Hauptentfeuchters und die
 *   Mindestlaufzeit des Zusatzes — und sonst nichts. Der Hauptentfeuchter schickt dabei ALLE Felder, der Zusatz nur das
 *   Geänderte. „Ruhig" hebt die Wartezeit auf Außenluft mit an (das Backend verlangt: nie kürzer als „Einschalten nach").</li>
 * <li>echte Nutzer-Werte ergeben „Normal" und „Eigene Werte" bei der Hilfsstärke — und nichts ist dabei geändert.</li>
 * <li>Automatik und „Wann darf entfeuchtet werden?" setzen BEIDE Geräte.</li>
 * <li>„Einschalten über" ist ein Wert für Tag und Nacht; AUS folgt als EIN minus Abstand — aber nur, wenn der Nutzer
 *   EIN oder den Abstand anfasst.</li>
 * <li>Der Überblick nennt Urteil in Worten, Ziel und Band; „Woher kommt das?" ist zu bis zum Klick; fehlt
 *   `/api/zielwerte`, bleibt die Seite stehen.</li>
 * </ul>
 */

const stufenWahl = (page: Page) => page.getByRole('radiogroup', { name: 'Wie reagieren die Entfeuchter?' })
const stufe = (page: Page, name: 'Ruhig' | 'Normal' | 'Schnell') => stufenWahl(page).getByRole('radio', { name, exact: true })
const hilfsWahl = (page: Page) => page.getByRole('radiogroup', { name: 'Hilfsstärke' })
const wannWahl = (page: Page) => page.getByRole('radiogroup', { name: 'Wann darf entfeuchtet werden?' })

/** Nutzer-Anlage vom 12.10.2026 vorgeben (Nacht, Flush, 55,7 % rF bei 21 °C). */
const bruAnlage = (page: Page, nachVorgaben: Vorgaben = {}): Promise<Stand> =>
  backendVorgeben(page, BRU_ZUSATZ_LIVE, BRU_HAUPT_LIVE, { haupt: BRU_HAUPT_EINSTELLUNGEN, zusatz: BRU_ZUSATZ_EINSTELLUNGEN, zielwerte: BRU_ZIELWERTE, ...nachVorgaben })

const gespeichert = async (page: Page) => {
  await expect(page.getByText('Gespeichert.')).toBeVisible()
  await expect(speichern(page)).toHaveCount(0)
}

// ------------------------------------------------------------------ a) Stufe wählen und speichern

test('Stufe „Schnell": der Haupt-PUT trägt alle Felder samt Abstand, Einschalten nach, Mindestlauf und AUS = EIN − 2, der Zusatz-PUT nur den Mindestlauf — zweimal', async ({ page }) => {
  const stand = await backendVorgeben(page)
  await seiteOeffnen(page)
  await reiterWahl(page, 'Einstellungen')
  await expect(stufe(page, 'Normal')).toHaveAttribute('aria-checked', 'true')
  await expect(speichern(page)).toHaveCount(0)

  // Durchgang 1: Schnell.
  await stufe(page, 'Schnell').click()
  await expect(stufe(page, 'Schnell')).toHaveAttribute('aria-checked', 'true')
  await speichern(page).click()
  await gespeichert(page)
  expect(stand.hauptPuts).toHaveLength(1)
  expect(stand.hauptPuts[0]).toMatchObject({
    hystereseProzent: 2, einschaltverzoegerungMin: 1, mindestlaufzeitMin: 10,
    // EIN bleibt (Tag 60, Nacht 62), AUS = EIN − 2.
    feuchteEinTag: 60, feuchteAusTag: 58, feuchteEinNacht: 62, feuchteAusNacht: 60,
    // Die Wartezeit auf Außenluft war 25 und bleibt — sie wird nie kürzer als „Einschalten nach".
    wartezeitAussenluftMin: 25,
  })
  expect(Object.keys(stand.hauptPuts[0]).sort(), 'der Hauptentfeuchter schickt ALLE Felder').toEqual(Object.keys(HAUPT_EINSTELLUNGEN).sort())
  // Der Zusatz schickt nur sein Geändertes: den Mindestlauf. Hilfsstärke, Mindestpause und „Zuschalten nach" gehören ihm.
  await expect.poll(() => stand.puts).toEqual([{ mindestlaufzeitMin: 10 }])
  await expect(stufe(page, 'Schnell')).toHaveAttribute('aria-checked', 'true')
  await expect(hilfsWahl(page).getByRole('radio', { name: 'normal', exact: true })).toHaveAttribute('aria-checked', 'true')

  // Durchgang 2, ohne Neuladen: Ruhig. Der Zusatz-Körper trägt nur noch das, was sich gegenüber dem Gespeicherten ändert.
  await stufe(page, 'Ruhig').click()
  await speichern(page).click()
  await gespeichert(page)
  expect(stand.hauptPuts).toHaveLength(2)
  expect(stand.hauptPuts[1]).toMatchObject({
    hystereseProzent: 6, einschaltverzoegerungMin: 10, mindestlaufzeitMin: 30,
    feuchteEinTag: 60, feuchteAusTag: 54, feuchteEinNacht: 62, feuchteAusNacht: 56,
    wartezeitAussenluftMin: 25,
  })
  await expect.poll(() => stand.puts).toEqual([{ mindestlaufzeitMin: 10 }, { mindestlaufzeitMin: 25 }])
  await expect(stufe(page, 'Ruhig')).toHaveAttribute('aria-checked', 'true')

  // Nach dem Neuladen steht es immer noch da (das Backend hat es gespeichert, die Seite liest es zurück).
  await page.reload({ waitUntil: 'networkidle' })
  await reiterWahl(page, 'Einstellungen')
  await expect(stufe(page, 'Ruhig')).toHaveAttribute('aria-checked', 'true')
  await expect(speichern(page)).toHaveCount(0)
})

// ------------------------------------------------------------------ b) echte Nutzer-Werte

test('echte Nutzer-Werte ergeben „Normal" bei den Reaktionen und „Eigene Werte" bei der Hilfsstärke — nichts ist geändert', async ({ page }) => {
  const stand = await bruAnlage(page)
  await seiteOeffnen(page)
  for (const durchgang of [1, 2]) {
    await reiterWahl(page, 'Einstellungen')
    await expect(stufe(page, 'Normal'), `Durchgang ${durchgang}: Stufe`).toHaveAttribute('aria-checked', 'true')
    await expect(stufe(page, 'Ruhig')).toHaveAttribute('aria-checked', 'false')
    await expect(stufe(page, 'Schnell')).toHaveAttribute('aria-checked', 'false')
    await expect(stufenWahl(page).getByRole('radio')).toHaveCount(3)
    // Die Tabelle hebt die Spalte „Normal" hervor: 4 % · 2 min · 20 / 15 min.
    const tabelle = page.locator('.ef-stufentabelle')
    await expect(tabelle.locator('thead th.is-gewaehlt')).toHaveText(['Normal'])
    await expect(tabelle.locator('tbody td.is-gewaehlt')).toHaveText(['4 %', '2 min', '20 / 15 min'])

    // Hilfsstärke: keine der vier Wahlen, dafür „Eigene Werte" als Befund.
    const hilfe = hilfsWahl(page)
    await expect(hilfe.getByRole('radio', { name: 'Eigene Werte', exact: true })).toHaveAttribute('aria-checked', 'true')
    for (const name of ['aus', 'sparsam', 'normal', 'kräftig']) await expect(hilfe.getByRole('radio', { name, exact: true })).toHaveAttribute('aria-checked', 'false')
    await expect(page.locator('.ef-folge')).toContainText('zuschalten nach 2 min · Mindestpause 5 min')

    // Ein Wert für Tag und Nacht (50 / 50): ein Feld, AUS zur Ansicht.
    await expect(page.getByLabel('Einschalten über', { exact: true })).toHaveValue('50')
    await expect(page.getByLabel('Einschalten über (Tag)', { exact: true })).toHaveCount(0)
    await expect(page.locator('.st-kk', { hasText: 'Ziel · Luftfeuchte' }).locator('.st-feldzeile', { hasText: 'Ausschalten unter' }).locator('.st-nurlesen')).toHaveText('46 %')
    await expect(page.getByLabel('Ausschalten unter', { exact: false })).toHaveCount(0)

    // Nichts ist geändert: kein „Speichern", kein gelber Kasten, auf keinem Reiter.
    await expect(speichern(page)).toHaveCount(0)
    await expect(page.locator('[data-audit="zusatz-aenderungen"]')).toHaveCount(0)
    await reiterWahl(page, 'Überblick')
    await expect(speichern(page)).toHaveCount(0)
    await reiterWahl(page, 'Einrichtung')
    await expect(speichern(page)).toHaveCount(0)
    // Durchgang 2: Seite neu laden — dasselbe Bild, kein stilles Umrechnen beim Laden.
    if (durchgang === 1) { await page.reload({ waitUntil: 'networkidle' }) }
  }
  expect(stand.hauptPuts, 'es wurde nichts geschrieben').toEqual([])
  await expect.poll(() => stand.puts, { message: 'es wurde nichts geschrieben' }).toEqual([])
})

// ------------------------------------------------------------------ c) „Ruhig" hebt die Wartezeit auf Außenluft an

test('Stufe „Ruhig" hebt die Wartezeit auf Außenluft auf 10 — das Backend verlangt: nie kürzer als „Einschalten nach"', async ({ page }) => {
  const stand = await bruAnlage(page)
  await seiteOeffnen(page)
  await reiterWahl(page, 'Einstellungen')
  await stufe(page, 'Ruhig').click()
  await speichern(page).click()
  await gespeichert(page)
  expect(stand.hauptPuts).toHaveLength(1)
  expect(stand.hauptPuts[0]).toMatchObject({
    einschaltverzoegerungMin: 10, wartezeitAussenluftMin: 10, hystereseProzent: 6, mindestlaufzeitMin: 30,
    feuchteEinTag: 50, feuchteAusTag: 44, feuchteEinNacht: 50, feuchteAusNacht: 44,
  })
  await expect.poll(() => stand.puts).toEqual([{ mindestlaufzeitMin: 25 }])

  // Zweiter Durchgang, ohne Neuladen: wieder auf „Normal" — die Außenluft-Wartezeit bleibt bei 10 (sie wird nur angehoben,
  // nie von selbst gekürzt), und sie ist nie kürzer als „Einschalten nach".
  await stufe(page, 'Normal').click()
  await speichern(page).click()
  await gespeichert(page)
  expect(stand.hauptPuts[1]).toMatchObject({ einschaltverzoegerungMin: 2, wartezeitAussenluftMin: 10, hystereseProzent: 4, feuchteAusTag: 46 })
  await expect.poll(() => stand.puts.at(-1)).toEqual({ mindestlaufzeitMin: 15 })
  for (const put of stand.hauptPuts) {
    expect(put.wartezeitAussenluftMin as number, 'Wartezeit auf Außenluft kürzer als „Einschalten nach"').toBeGreaterThanOrEqual(put.einschaltverzoegerungMin as number)
  }
})

// ------------------------------------------------------------------ d) Automatik und Tagbetrieb setzen beide Geräte

test('Automatik und „Wann darf entfeuchtet werden?" setzen BEIDE Geräte — zweimal, auch aus „teils" heraus', async ({ page }) => {
  const stand = await backendVorgeben(page)
  await seiteOeffnen(page)
  await reiterWahl(page, 'Einstellungen')
  const automatik = schalter(page, 'Automatik')
  await expect(automatik).toBeChecked()
  await expect(wannWahl(page).getByRole('radio', { name: 'Tag und Nacht', exact: true })).toHaveAttribute('aria-checked', 'true')

  // Durchgang 1: Automatik aus.
  await automatik.uncheck()
  await speichern(page).click()
  await gespeichert(page)
  expect(stand.hauptPuts).toHaveLength(1)
  expect(stand.hauptPuts[0]).toMatchObject({ automatikAktiv: false, tagbetriebErlauben: true })
  await expect.poll(() => stand.puts).toEqual([{ automatikAktiv: false }])
  await expect(automatik).not.toBeChecked()

  // Durchgang 2, ohne Neuladen: „Nur nachts" (die Automatik bleibt aus und darf nicht von selbst zurückspringen).
  await wannWahl(page).getByRole('radio', { name: 'Nur nachts', exact: true }).click()
  await speichern(page).click()
  await gespeichert(page)
  expect(stand.hauptPuts[1]).toMatchObject({ automatikAktiv: false, tagbetriebErlauben: false })
  await expect.poll(() => stand.puts).toEqual([{ automatikAktiv: false }, { tagbetriebErlauben: false }])
  await expect(wannWahl(page).getByRole('radio', { name: 'Nur nachts', exact: true })).toHaveAttribute('aria-checked', 'true')

  // Durchgang 3: beides zurück, in einem Speichern — beide Geräte folgen.
  await automatik.check()
  await wannWahl(page).getByRole('radio', { name: 'Tag und Nacht', exact: true }).click()
  await speichern(page).click()
  await gespeichert(page)
  expect(stand.hauptPuts[2]).toMatchObject({ automatikAktiv: true, tagbetriebErlauben: true })
  await expect.poll(() => stand.puts.at(-1)).toEqual({ automatikAktiv: true, tagbetriebErlauben: true })
})

test('Automatik: stehen die zwei Geräte verschieden, sagt die Seite es — und ein Klick setzt beide', async ({ page }) => {
  // Der Zusatz hat die Automatik aus (zum Beispiel in Home Assistant abgeschaltet), der Hauptentfeuchter an.
  const stand = await backendVorgeben(page, {}, {}, { zusatz: { ...EINSTELLUNGEN, automatikAktiv: false } })
  await seiteOeffnen(page)
  await reiterWahl(page, 'Einstellungen')
  await expect(page.locator('.st-kk-kopf', { hasText: 'Betrieb' })).toBeVisible()
  const automatik = schalter(page, 'Automatik')
  await expect(automatik, 'bei verschiedenem Stand steht der Schalter nicht auf „an"').not.toBeChecked()
  await expect(page.locator('label.v1-switch', { hasText: 'Automatik' }).first()).toContainText('nur ein Gerät an')

  await automatik.check()
  await speichern(page).click()
  await gespeichert(page)
  // Der Hauptentfeuchter war schon an: sein Körper trägt weiter alle Felder, mit „an". Der Zusatz schickt nur sein Geändertes.
  await expect.poll(() => stand.puts).toEqual([{ automatikAktiv: true }])
  await expect(automatik).toBeChecked()
  await expect(page.locator('label.v1-switch', { hasText: 'Automatik' }).first()).not.toContainText('nur ein Gerät an')
})

// ------------------------------------------------------------------ e) „Einschalten über" und AUS

test('„Einschalten über": ein Wert für Tag und Nacht, AUS = EIN − Abstand für beide — und wer nichts anfasst, behält sein gespeichertes AUS', async ({ page }) => {
  const stand = await bruAnlage(page)
  await seiteOeffnen(page)
  await reiterWahl(page, 'Einstellungen')
  const ein = page.getByLabel('Einschalten über', { exact: true })
  await expect(ein).toHaveValue('50')

  // Durchgang 1: 52 → EIN Tag = EIN Nacht = 52, AUS = 48 (Abstand 4).
  await ein.fill('52')
  await expect(page.locator('.st-kk', { hasText: 'Ziel · Luftfeuchte' }).locator('.st-feldzeile', { hasText: 'Ausschalten unter' }).locator('.st-nurlesen')).toHaveText('48 %')
  await speichern(page).click()
  await gespeichert(page)
  expect(stand.hauptPuts[0]).toMatchObject({ feuchteEinTag: 52, feuchteEinNacht: 52, feuchteAusTag: 48, feuchteAusNacht: 48, hystereseProzent: 4 })
  expect(stand.hauptPuts[0].feuchteEinTag).toBe(stand.hauptPuts[0].feuchteEinNacht)
  await expect.poll(() => stand.puts, { message: 'der Zusatz bekommt die Schwellen nicht' }).toEqual([])

  // Durchgang 2, ohne Neuladen: der Abstand (unter „Erweitert") auf 3 → AUS = 49 für beide, EIN bleibt; die Stufe heißt „Eigene Werte".
  await aufklappen(page, /Erweitert/)
  await page.getByLabel('Abstand EIN → AUS', { exact: true }).fill('3')
  await speichern(page).click()
  await gespeichert(page)
  expect(stand.hauptPuts[1]).toMatchObject({ feuchteEinTag: 52, feuchteEinNacht: 52, feuchteAusTag: 49, feuchteAusNacht: 49, hystereseProzent: 3 })
  await expect(page.getByRole('radio', { name: 'Eigene Werte', exact: true }).first()).toHaveAttribute('aria-checked', 'true')
})

test('„Einschalten über": stehen Tag und Nacht im Bestand verschieden, zeigt die Kachel zwei Felder und ändert nur das angefasste', async ({ page }) => {
  // Geregelt nach Luftfeuchte (bei VPD sind die festen Schwellen gesperrt) und Tag 60 / Nacht 62 im Bestand.
  const stand = await backendVorgeben(page, {}, {}, { haupt: { ...HAUPT_EINSTELLUNGEN, vpdRegelung: false } })
  await seiteOeffnen(page)
  await reiterWahl(page, 'Einstellungen')
  await expect(page.getByLabel('Einschalten über', { exact: true }), 'bei verschiedenen Werten kein einzelnes Feld').toHaveCount(0)
  const tag = page.getByLabel('Einschalten über (Tag)', { exact: true })
  const nacht = page.getByLabel('Einschalten über (Nacht)', { exact: true })
  await expect(tag).toHaveValue('60')
  await expect(nacht).toHaveValue('62')

  // Durchgang 1: nur die Nacht.
  await nacht.fill('64')
  await speichern(page).click()
  await gespeichert(page)
  expect(stand.hauptPuts[0]).toMatchObject({ feuchteEinTag: 60, feuchteAusTag: 56, feuchteEinNacht: 64, feuchteAusNacht: 60 })

  // Durchgang 2: nur der Tag — die Nacht darf nicht auf den alten Wert zurück.
  await tag.fill('58')
  await speichern(page).click()
  await gespeichert(page)
  expect(stand.hauptPuts[1]).toMatchObject({ feuchteEinTag: 58, feuchteAusTag: 54, feuchteEinNacht: 64, feuchteAusNacht: 60 })

  // Zusammenlegen (Schalter unter „Erweitert"): die Nacht folgt dem Tag, ein Feld bleibt.
  await aufklappen(page, /Erweitert/)
  await schalter(page, 'Einschalt-Schwelle für Tag und Nacht getrennt').uncheck()
  await expect(page.getByLabel('Einschalten über', { exact: true })).toHaveValue('58')
  await speichern(page).click()
  await gespeichert(page)
  expect(stand.hauptPuts[2]).toMatchObject({ feuchteEinTag: 58, feuchteEinNacht: 58, feuchteAusTag: 54, feuchteAusNacht: 54 })
})

test('wer EIN und Abstand nicht anfasst, behält sein gespeichertes AUS — die Seite rechnet nicht beim Laden um', async ({ page }) => {
  // Bestand mit einem AUS, das nicht EIN − Abstand ist (57 statt 56): ein Speichern der Höchsttemperatur darf es nicht „reparieren".
  const stand = await backendVorgeben(page, {}, {}, { haupt: { ...HAUPT_EINSTELLUNGEN, feuchteEinTag: 60, feuchteAusTag: 57, feuchteEinNacht: 60, feuchteAusNacht: 55 } })
  await seiteOeffnen(page)
  await reiterWahl(page, 'Einstellungen')
  await page.locator('.ef-tempmax').filter({ has: page.getByRole('radiogroup', { name: 'Temperatur max. Tag' }) }).getByLabel('Fester Wert', { exact: true }).fill('27')
  await speichern(page).click()
  await gespeichert(page)
  expect(stand.hauptPuts[0]).toMatchObject({ tempMaxTagFestC: 27, feuchteAusTag: 57, feuchteAusNacht: 55 })
})

// ------------------------------------------------------------------ f) Überblick mit Nutzer-Live-Werten

test('Überblick mit Nutzer-Live-Werten: „knapp" in Worten, ZIEL-Zeilen, Band, „Woher kommt das?" zu bis zum Klick — zweimal', async ({ page }) => {
  const stand = await bruAnlage(page)
  await seiteOeffnen(page)

  // Lagemeldung und Plaketten.
  await expect(page.locator('.v1-alert').first()).toContainText('Knapp daneben')
  await expect(page.locator('.v1-alert').first()).toContainText('Luftfeuchte 55,7 % — knapp über dem Ziel (Ziel 45 – 54 %)')
  await expect(page.locator('.v1-alert').first()).toContainText('Temperatur 21,0 °C — knapp über dem Plan (Ziel 12 – 20 °C)')
  await expect(page.locator('.ef-kontext span')).toHaveText(['☾ Nacht', 'Woche Flush'])

  // Je Messwert: große Zahl, Urteil in Worten, ZIEL-Zeile, Band mit Nadel und nur den Grenzzahlen darunter.
  const bloecke = page.locator('.ef-band .ef-block')
  await expect(bloecke).toHaveCount(2)
  await expect(bloecke.nth(0).locator('.ef-gross')).toContainText('55,7')
  await expect(bloecke.nth(0).locator('.ef-zustand')).toContainText('knapp über dem Ziel')
  await expect(bloecke.nth(0).locator('.gos-metric-ziel')).toHaveText(/ZIEL\s*45 – 54\s*%/)
  await expect(bloecke.nth(1).locator('.ef-gross')).toContainText('21,0')
  await expect(bloecke.nth(1).locator('.ef-zustand')).toContainText('knapp über dem Plan')
  await expect(bloecke.nth(1).locator('.gos-metric-ziel')).toHaveText(/ZIEL\s*12 – 20\s*°C/)
  for (const i of [0, 1]) {
    await expect(bloecke.nth(i).locator('.gos-metric-band'), `Band ${i + 1}`).toHaveCount(1)
    await expect(bloecke.nth(i).locator('.gos-metric-band .nadel')).toHaveClass(/\bist-warn\b/)
  }
  // Unter dem Band stehen nur die Grenzen, nicht das Ziel ein zweites Mal.
  await expect(bloecke.nth(0).locator('.gos-metric-band .skala span')).toHaveText(['45', '58'])
  await expect(bloecke.nth(1).locator('.gos-metric-band .skala span')).toHaveText(['12', '23'])
  await expect(page.locator('.ef-band .ef-skala, .ef-band .ef-marken')).toHaveCount(0)

  // „Woher kommt das?" ist zu: der Inhalt trägt `hidden` und ist nicht zu sehen.
  const woher = page.locator('.ef-herkunft-box').filter({ hasText: 'Woher kommt das?' })
  await expect(woher).toHaveCount(2)
  for (const i of [0, 1]) {
    await expect(woher.nth(i).locator('.ef-hk-kopf')).toHaveAttribute('aria-expanded', 'false')
    await expect(woher.nth(i).locator('.ef-hk-koerper')).toHaveAttribute('hidden', '')
    await expect(woher.nth(i).locator('.ef-hk-koerper')).toBeHidden()
  }

  // Durchgang 1: die Luftfeuchte aufklappen — die Quellen stehen da, die Temperatur bleibt zu.
  await woher.nth(0).locator('.ef-hk-kopf').click()
  await expect(woher.nth(0).locator('.ef-hk-koerper')).toBeVisible()
  await expect(woher.nth(0).locator('.ef-hk-koerper')).toContainText('Obergrenze 54 %')
  await expect(woher.nth(0).locator('.ef-hk-koerper')).toContainText('Plan · Grow 2026-01 · SKX Canna Aqua · Woche Flush')
  await expect(woher.nth(0).locator('.ef-hk-koerper')).toContainText('Untergrenze 45 %')
  await expect(woher.nth(0).locator('.ef-hk-koerper')).toContainText('dein Grenzwert')
  await expect(woher.nth(0).locator('.ef-hk-koerper').getByRole('link', { name: 'Plan öffnen' })).toHaveAttribute('href', /\/plan$/)
  await expect(woher.nth(0).locator('.ef-hk-koerper').getByRole('link', { name: 'Grenzwerte einstellen' })).toHaveAttribute('href', /\/grenzwerte$/)
  await expect(woher.nth(1).locator('.ef-hk-koerper')).toBeHidden()

  // Durchgang 2, ohne Neuladen: die Temperatur aufklappen, die Luftfeuchte zuklappen.
  await woher.nth(1).locator('.ef-hk-kopf').click()
  await woher.nth(0).locator('.ef-hk-kopf').click()
  await expect(woher.nth(0).locator('.ef-hk-koerper')).toBeHidden()
  await expect(woher.nth(1).locator('.ef-hk-koerper')).toBeVisible()
  await expect(woher.nth(1).locator('.ef-hk-koerper')).toContainText('Ziel 16 °C')
  await expect(woher.nth(1).locator('.ef-hk-koerper')).toContainText('Höchsttemperatur 23,0 °C')
  await expect(woher.nth(1).locator('.ef-hk-koerper')).toContainText('von dir eingestellt (fest)')
  await expect(woher.nth(1).locator('.ef-hk-koerper')).toContainText('Spielraum ± 4 K (12 – 20 °C)')

  expect(stand.zielwerteAbfragen, 'der Überblick hat /api/zielwerte gelesen').toBeGreaterThan(0)
  await expect(page.locator('main.v1-page')).not.toContainText(/NaN|undefined/)
})

test('Überblick ohne /api/zielwerte (Antwort 500): die Seite bleibt stehen, die Obergrenze zeigt das Ziel „≤ 54", nichts steht als NaN oder undefined da', async ({ page }) => {
  const stand = await bruAnlage(page, { zielwerte: null })
  await seiteOeffnen(page)
  await expect(page.getByRole('heading', { name: 'Entfeuchtung', level: 1 })).toBeVisible()
  await expect.poll(() => stand.zielwerteAbfragen, { message: 'die Seite hat /api/zielwerte gefragt (und 500 bekommen)' }).toBeGreaterThan(0)

  const bloecke = page.locator('.ef-band .ef-block')
  await expect(bloecke).toHaveCount(2)
  await expect(bloecke.nth(0).locator('.gos-metric-ziel')).toHaveText(/ZIEL\s*≤ 54\s*%/)
  await expect(bloecke.nth(0).locator('.ef-gross')).toContainText('55,7')
  await expect(page.locator('.ef-band .gos-metric-band')).toHaveCount(2)
  // Plaketten und Quellen kommen jetzt aus den Entfeuchter-Daten: die Woche steht da, ein Grow nicht.
  await expect(page.locator('.ef-kontext span')).toHaveText(['☾ Nacht', 'Woche Flush'])
  const woher = page.locator('.ef-herkunft-box').filter({ hasText: 'Woher kommt das?' }).first()
  await woher.locator('.ef-hk-kopf').click()
  await expect(woher.locator('.ef-hk-koerper')).toContainText('Obergrenze 54 %')
  await expect(woher.locator('.ef-hk-koerper')).not.toContainText('Untergrenze')
  await expect(page.locator('main.v1-page')).not.toContainText(/NaN|undefined/)
  // Die anderen Reiter laufen weiter.
  await reiterWahl(page, 'Einstellungen')
  await expect(stufe(page, 'Normal')).toHaveAttribute('aria-checked', 'true')
  await expect(page.locator('main.v1-page')).not.toContainText(/NaN|undefined/)
})

// ------------------------------------------------------------------ g) „Höchsttemperatur einstellen" im Überblick

/**
 * Steht die Kachel gleich unter der festen Kopfleiste? Gemessen wird erst, wenn das weiche Rollen steht (siehe
 * `feldfehler-auf-anderem-reiter.spec.ts`). „Irgendwo im Fenster" genügt nicht: nach dem Reiterwechsel steht die Seite
 * oft zufällig schon dort, und die Prüfung wäre auch ohne das Rollen grün.
 */
const kachelImBild = (kachel: Locator) => kachel.evaluate(async (el) => {
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
  if (r.top > kopf + 80) return `zu weit unten: ${Math.round(r.top)} px, Kopfleiste endet bei ${Math.round(kopf)} px, Fenster ${window.innerHeight} px`
  return 'im Bild'
})

test('„Höchsttemperatur einstellen" im Überblick wechselt in „Einstellungen" und bringt die Kachel ins Bild — zweimal', async ({ page }) => {
  // Niedriges Fenster am Handy: die Kachel liegt unter dem Bildrand, wenn die Seite nicht rollt.
  await page.setViewportSize({ width: 390, height: 640 })
  await bruAnlage(page)
  await seiteOeffnen(page)

  const kachel = page.locator('#ef-k-hoechst')
  const imBild = () => kachelImBild(kachel)

  for (const durchgang of [1, 2]) {
    await reiterWahl(page, 'Überblick')
    await page.evaluate(() => window.scrollTo(0, 0))
    // Die Zeile „Woher kommt das?" der Temperatur aufklappen (beim zweiten Mal ist sie wegen des Reiterwechsels wieder zu).
    const woher = page.locator('.ef-herkunft-box').filter({ hasText: 'Woher kommt das?' }).nth(1)
    await expect(woher.locator('.ef-hk-kopf')).toHaveAttribute('aria-expanded', 'false')
    await woher.locator('.ef-hk-kopf').click()
    await woher.getByRole('button', { name: 'Höchsttemperatur einstellen' }).click()

    await expect(page.locator('.v1-tab.active'), `Durchgang ${durchgang}: Reiter`).toHaveText('Einstellungen')
    await expect(kachel).toBeVisible()
    await expect(kachel.locator('.st-kk-kopf')).toHaveAttribute('aria-expanded', 'true')
    await expect(kachel.getByRole('radiogroup', { name: 'Temperatur max. Tag' })).toBeVisible()
    await expect.poll(imBild, { timeout: 5_000, message: `Durchgang ${durchgang}: die Kachel „Höchsttemperatur" liegt nicht im Bild` }).toBe('im Bild')
  }
})

test('„Ein-/Ausschalten einstellen" im Überblick bringt die Kachel „Ziel · Luftfeuchte" ins Bild', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 640 })
  await bruAnlage(page)
  await seiteOeffnen(page)
  const woher = page.locator('.ef-herkunft-box').filter({ hasText: 'Woher kommt das?' }).first()
  await woher.locator('.ef-hk-kopf').click()
  await woher.getByRole('button', { name: 'Ein-/Ausschalten einstellen' }).click()
  await expect(page.locator('.v1-tab.active')).toHaveText('Einstellungen')
  const kachel = page.locator('#ef-k-ziel')
  await expect(kachel.getByLabel('Einschalten über', { exact: true })).toBeVisible()
  await expect.poll(() => kachelImBild(kachel), { timeout: 5_000, message: 'die Kachel „Ziel · Luftfeuchte" liegt nicht im Bild' }).toBe('im Bild')
})

// ------------------------------------------------------------------ h) Handy mit Nutzer-Werten

for (const breite of [360, 390]) {
  test(`Handy ${breite} px, Nutzer-Werte: nichts ragt über den Rand — auf allen drei Reitern, mit offenen Kacheln und offenem „Woher kommt das?"`, async ({ page }) => {
    await page.setViewportSize({ width: breite, height: 800 })
    await bruAnlage(page)
    await seiteOeffnen(page)
    for (const reiter of ['Überblick', 'Einstellungen', 'Einrichtung']) {
      await reiterWahl(page, reiter)
      await alleOeffnen(page)
      // Nichts bleibt zugeklappt, sonst misst die Prüfung nur, was ohnehin im Bild steht.
      await expect(page.locator('.st-kk-kopf[aria-expanded="false"], .ef-hk-kopf[aria-expanded="false"]'), `${reiter}: etwas blieb zu`).toHaveCount(0)
      // …und das Gemessene war wirklich da: der Überblick zeigt zwei Bänder und drei offene „Woher/So liest du"-Zeilen,
      // die Einstellungen die Stufentabelle.
      if (reiter === 'Überblick') {
        await expect(page.locator('.gos-metric-band')).toHaveCount(2)
        await expect(page.locator('.ef-hk-koerper:visible')).toHaveCount(3)
      }
      if (reiter === 'Einstellungen') await expect(page.locator('.ef-stufentabelle')).toBeVisible()
      expect(await ragtHinaus(page), `${reiter}: ragt bei ${breite} px rechts hinaus`).toEqual([])
      expect(await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1),
        `${reiter}: die Seite scrollt bei ${breite} px seitwärts`).toBe(false)
      expect(await bandzahlenKollidieren(page), `${reiter}: Bandzahlen überlappen bei ${breite} px`).toEqual([])
    }
    // Die Stufentabelle steckt in keinem Wischbereich: sie passt in ihre Spalte (Text nicht abgeschnitten).
    await reiterWahl(page, 'Einstellungen')
    const tabelle = await page.locator('.ef-stufentabelle').evaluate((el) => ({ breite: el.getBoundingClientRect().width, inhalt: el.scrollWidth }))
    expect(tabelle.inhalt, 'die Stufentabelle ist breiter als ihre Spalte').toBeLessThanOrEqual(Math.ceil(tabelle.breite) + 1)
  })
}

test('Überblick ohne /api/zielwerte: die ZIEL-Zeile der Temperatur ist ein Bereich oder eine Grenze, nie „16 – 16"', async ({ page }) => {
  // Befund (13.10.2026): ohne Grenzwert bildet `temperaturBand` aus Plan-Luft ± Toleranz (Toleranz fehlt = 0) das Band
  // „16 – 16 °C"; die Rückfallregel im Überblick („im Ziel bis zum früheren Aus des Zusatzes") kommt nie zum Zug.
  await bruAnlage(page, { zielwerte: null })
  await seiteOeffnen(page)
  const ziel = page.locator('.ef-band .ef-block').nth(1).locator('.gos-metric-ziel')
  await expect(ziel).toContainText('ZIEL')
  const text = (await ziel.innerText()).replace(/\s+/g, ' ')
  const bereich = /(\d+(?:,\d)?) – (\d+(?:,\d)?)/.exec(text)
  if (bereich) expect(bereich[1], `ZIEL-Zeile „${text}": Untergrenze und Obergrenze sind gleich`).not.toBe(bereich[2])
})

test('„Einschalten nach": das Feld leeren und neu eintippen lässt sich speichern — die Wartezeit auf Außenluft bleibt eine Zahl', async ({ page }) => {
  const stand = await bruAnlage(page)
  await seiteOeffnen(page)
  await reiterWahl(page, 'Einstellungen')
  await aufklappen(page, /Erweitert/)
  const feld = page.getByLabel('Einschalten nach (RDWC Dehumi)', { exact: true })
  // Wie getippt: erst löschen, dann die neue Zahl.
  await feld.fill('')
  await feld.fill('3')
  await expect(feld).toHaveValue('3')
  await speichern(page).click()
  await expect(page.getByText('Bitte die markierten Felder prüfen.'), 'das Speichern bleibt gesperrt, obwohl das Feld gefüllt ist').toHaveCount(0)
  await gespeichert(page)
  expect(stand.hauptPuts).toHaveLength(1)
  expect(stand.hauptPuts[0]).toMatchObject({ einschaltverzoegerungMin: 3, wartezeitAussenluftMin: 5 })
})

// ------------------------------------------------------------------ forkai.212: Arbeitsbereich aus dem Plan

const bereichWahl = (page: Page) => page.getByRole('radiogroup', { name: 'Arbeitsbereich' })
const bereich = (page: Page, name: 'Aus dem Plan' | 'Fest') => bereichWahl(page).getByRole('radio', { name, exact: true })
const einFeld = (page: Page) => page.getByLabel('Einschalten über', { exact: true })
const vorlaufFeld = (page: Page) => page.getByLabel('Vorlauf unter der Plan-Obergrenze', { exact: true })

/** Nutzer-Anlage mit den neuen Feldern: Arbeitsbereich fest, Vorlauf 4 — so liefert sie das Backend nach forkai.212. */
const bruMitBereich = (page: Page, haupt: Json = {}, hauptLive: Json = {}): Promise<Stand> =>
  backendVorgeben(page, BRU_ZUSATZ_LIVE, { ...BRU_HAUPT_LIVE, ...hauptLive }, {
    haupt: { ...BRU_HAUPT_EINSTELLUNGEN, arbeitsbereichQuelle: 'fest', vorlaufProzent: 4, ...haupt },
    zusatz: BRU_ZUSATZ_EINSTELLUNGEN, zielwerte: BRU_ZIELWERTE,
  })

test('Arbeitsbereich „Aus dem Plan": Obergrenze 54 und Vorlauf 4 ergeben 50/46 — der Haupt-PUT trägt Quelle, Vorlauf und die gerechneten Schwellen, der Zusatz nichts — zweimal', async ({ page }) => {
  const stand = await bruMitBereich(page)
  await seiteOeffnen(page)
  await reiterWahl(page, 'Einstellungen')
  await expect(bereich(page, 'Fest')).toHaveAttribute('aria-checked', 'true')
  await expect(einFeld(page)).toHaveValue('50')
  await expect(speichern(page)).toHaveCount(0)

  // Durchgang 1: Aus dem Plan — Nutzer-Werte ergeben dieselben Schwellen, das Feld „Einschalten über" weicht dem Vorlauf.
  await bereich(page, 'Aus dem Plan').click()
  await expect(bereich(page, 'Aus dem Plan')).toHaveAttribute('aria-checked', 'true')
  await expect(einFeld(page)).toHaveCount(0)
  await expect(vorlaufFeld(page)).toHaveValue('4')
  await expect(page.getByText('= Plan-Obergrenze 54 % minus Vorlauf 4 %')).toBeVisible()
  await speichern(page).click()
  await gespeichert(page)
  expect(stand.hauptPuts).toHaveLength(1)
  expect(stand.hauptPuts[0]).toMatchObject({
    arbeitsbereichQuelle: 'plan', vorlaufProzent: 4,
    feuchteEinTag: 50, feuchteAusTag: 46, feuchteEinNacht: 50, feuchteAusNacht: 46,
  })
  await expect.poll(() => stand.puts).toHaveLength(0)

  // Durchgang 2: Vorlauf 6 → EIN 48, AUS 44; wieder speichern (zweites Speichern, Seite nicht neu geladen).
  await vorlaufFeld(page).fill('6')
  await expect(page.getByText('= Plan-Obergrenze 54 % minus Vorlauf 6 %')).toBeVisible()
  await speichern(page).click()
  await gespeichert(page)
  expect(stand.hauptPuts).toHaveLength(2)
  expect(stand.hauptPuts[1]).toMatchObject({ arbeitsbereichQuelle: 'plan', vorlaufProzent: 6, feuchteEinTag: 48, feuchteAusTag: 44, feuchteEinNacht: 48, feuchteAusNacht: 44 })
})

test('Arbeitsbereich „Aus dem Plan": die Stufe verschiebt AUS mit, EIN bleibt Obergrenze minus Vorlauf — und zurück auf „Fest" erscheint das Feld wieder', async ({ page }) => {
  const stand = await bruMitBereich(page, { arbeitsbereichQuelle: 'plan' })
  await seiteOeffnen(page)
  await reiterWahl(page, 'Einstellungen')
  await expect(bereich(page, 'Aus dem Plan')).toHaveAttribute('aria-checked', 'true')

  await stufe(page, 'Ruhig').click() // Abstand 6 → AUS 44
  await speichern(page).click()
  await gespeichert(page)
  expect(stand.hauptPuts[0]).toMatchObject({ arbeitsbereichQuelle: 'plan', hystereseProzent: 6, feuchteEinTag: 50, feuchteAusTag: 44 })

  await bereich(page, 'Fest').click()
  await expect(einFeld(page)).toHaveValue('50')
  await expect(vorlaufFeld(page)).toHaveCount(0)
})

test('Arbeitsbereich: nennt der Plan keine Obergrenze, ist „Aus dem Plan" gesperrt — die Seite erfindet keine', async ({ page }) => {
  await bruMitBereich(page, {}, { rhObergrenzeProzent: null, deckelProzent: null })
  await seiteOeffnen(page)
  await reiterWahl(page, 'Einstellungen')
  await expect(bereich(page, 'Aus dem Plan')).toBeDisabled()
  await expect(bereich(page, 'Fest')).toHaveAttribute('aria-checked', 'true')
  await expect(einFeld(page)).toHaveValue('50')
})

test('Überblick: bei „Aus dem Plan" nennt „Woher kommt das?" die Quelle der Schwellen mit Obergrenze und Vorlauf', async ({ page }) => {
  await bruMitBereich(page, { arbeitsbereichQuelle: 'plan', vorlaufProzent: 4 })
  await seiteOeffnen(page)
  const kopf = page.locator('.ef-hk-kopf', { hasText: 'Woher kommt das?' }).first()
  await kopf.click()
  await expect(page.getByText('aus dem Plan (Obergrenze 54 % minus Vorlauf 4 %)')).toBeVisible()
  await kopf.click()
  await kopf.click()
  await expect(page.getByText('aus dem Plan (Obergrenze 54 % minus Vorlauf 4 %)')).toBeVisible()
})
