import { test, expect, type Page } from '@playwright/test'
import { KONTRAST_HELFER } from './kontrast-messung'

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
 */

type Json = Record<string, unknown>

/** Bru, 06.10.2026: Modus „fest" 26,5 / 25, Hilfsstärke normal. */
const EINSTELLUNGEN: Json = {
  hilfe: 'normal',
  automatikAktiv: true,
  tagbetriebErlauben: true,
  nachtDurchlaufen: true,
  vpdHystereseKpa: 0.15,
  zuschaltVerzoegerungMin: 10,
  folgeAbstandK: 1,
  wiederEinAbstandK: 1,
  mindestlaufzeitMin: 15,
  mindestpauseMin: 10,
  meldung: { aktiv: true, grenzeW: 60, dauerMin: 5, wiederholungH: 2 },
  ablauf: 'tank',
  tempMaxTagModus: 'fest', tempMaxTagAbstandK: 6.5, tempMaxTagFestC: 26.5,
  tempMaxNachtModus: 'fest', tempMaxNachtAbstandK: 9, tempMaxNachtFestC: 25,
}

/** Das Beispiel aus VERTRAG.md. */
const LIVE: Json = {
  haErreichbar: true, fuehrungName: 'RDWC Dehumi', zusatzName: 'Dehumi RDWC Tent',
  planWoche: 'Blütewoche 7', planLuftTagC: 20, planLuftNachtC: 16,
  tempMaxTagC: 26.5, tempMaxNachtC: 25,
  folgeAusTagC: 25.5, folgeAusNachtC: 24, wiederEinTagC: 24.5, wiederEinNachtC: 23,
  tempC: 25.1, feuchteProzent: 55.2, vpd: 1.31, tagPhase: true,
  schaltgroesse: 'vpd', vpdZiel: 1.4, vpdEinSchwelle: 1.25, vpdAusSchwelle: 1.55,
  feuchteEinProzent: 39, feuchteAusProzent: 35,
  zusatzAn: true, zusatzOnline: true, leistungW: 313, energieHeuteKwh: 2.9,
  fuehrungAn: true, ziehtNichts: false, planUnvollstaendig: false, automatikAn: true,
}

/** Der Hauptentfeuchter (Form von `EntfeuchterEinstellungen`). */
const HAUPT_EINSTELLUNGEN: Json = {
  vpdRegelung: true, hystereseProzent: 4, mindestlaufzeitMin: 20, einschaltverzoegerungMin: 10, wartezeitAussenluftMin: 25,
  tagbetriebErlauben: true, automatikAktiv: true,
  tempMaxTagModus: 'fest', tempMaxTagAbstandK: 6.5, tempMaxTagFestC: 26.5,
  tempMaxNachtModus: 'fest', tempMaxNachtAbstandK: 9, tempMaxNachtFestC: 25,
  feuchteEinTag: 60, feuchteAusTag: 57, feuchteEinNacht: 62, feuchteAusNacht: 60,
}
const HAUPT_LIVE: Json = {
  haErreichbar: true, feuchteProzent: 55.2, tempC: 25.1, vpd: 1.31, tagPhase: true,
  einAktivProzent: 50, ausAktivProzent: 46, tempMaxAktivC: 26.5, rhObergrenzeProzent: 51, deckelProzent: 48,
  vpdUnten: 1.4, vpdOben: 1.4, blattOffsetC: -1, planWoche: 'Blütewoche 7', planLuftTagC: 20, planLuftNachtC: 16,
  tempMaxTagC: 26.5, tempMaxNachtC: 25, co2CanopyGrenzeC: 27,
  portAn: true, portOnline: true, automatikAn: true, zuluftVorrang: false,
}

const MODULE = {
  haErreichbar: true,
  standUtc: '2026-10-06T20:00:00Z',
  module: [
    { kennung: 'co2', titel: 'CO₂', status: 'an', kurz: 'Begasung', wert: '800 ppm', unterzeile: 'Ziel 900', hatDetail: true },
    { kennung: 'entfeuchter', titel: 'RDWC Dehumi', status: 'an', kurz: 'Entfeuchter', wert: '55 %', unterzeile: 'läuft', hatDetail: true },
    { kennung: 'entfeuchter-zusatz', titel: 'Dehumi RDWC Tent', status: 'an', kurz: 'Zusatz', wert: '313 W', unterzeile: 'läuft', hatDetail: true },
  ],
}

type Stand = { einstellungen: Json; puts: Json[]; haupt: Json; hauptPuts: Json[]; einrichtung: { zusatzVorhanden: boolean | null }; einrichtungPuts: Json[] }

/**
 * Die Antworten vorgeben. Der Zusatz-`PUT` übernimmt nur die Felder aus dem Körper (Vertrag), der des
 * Hauptentfeuchters ersetzt alles — und gibt die Höchsttemperatur an den Zusatz weiter, wie das Backend es tut.
 */
async function backendVorgeben(page: Page, live: Json = {}, hauptLive: Json = {}): Promise<Stand> {
  const stand: Stand = { einstellungen: structuredClone(EINSTELLUNGEN), puts: [], haupt: structuredClone(HAUPT_EINSTELLUNGEN), hauptPuts: [], einrichtung: { zusatzVorhanden: null }, einrichtungPuts: [] }
  const zusatzAntwort = () => ({
    einstellungen: stand.einstellungen,
    live: { ...LIVE, ...live },
    geraeteZugeordnet: 8, geraeteGesamt: 8, ausHomeAssistantUebernommen: false, haAngenommen: null,
  })
  const hauptAntwort = () => ({
    einstellungen: stand.haupt,
    live: { ...HAUPT_LIVE, ...hauptLive },
    geraeteZugeordnet: 7, geraeteGesamt: 7, ausHomeAssistantUebernommen: false, haAngenommen: null,
  })
  await page.route(/\/api\/steuerung$/, (route) => route.fulfill({ json: MODULE }))
  await page.route(/\/api\/steuerung\/entfeuchtung-einrichtung$/, async (route) => {
    if (route.request().method() === 'PUT') {
      const koerper = route.request().postDataJSON() as { zusatzVorhanden: boolean | null }
      stand.einrichtungPuts.push(koerper)
      stand.einrichtung = { zusatzVorhanden: koerper.zusatzVorhanden }
    }
    await route.fulfill({ json: stand.einrichtung })
  })
  await page.route(/\/api\/steuerung\/entfeuchter-namen$/, (route) => route.fulfill({
    json: { fuehrung: { anzeigename: 'RDWC Dehumi', vorgabe: 'RDWC Dehumi' }, zusatz: { anzeigename: 'Dehumi RDWC Tent', vorgabe: 'Dehumi RDWC Tent' } },
  }))
  await page.route(/\/api\/steuerung\/entfeuchter-zusatz$/, async (route) => {
    if (route.request().method() === 'PUT') {
      const koerper = route.request().postDataJSON() as Json
      stand.puts.push(koerper)
      for (const [feld, wert] of Object.entries(koerper)) {
        stand.einstellungen[feld] = feld === 'meldung' ? { ...(stand.einstellungen.meldung as Json), ...(wert as Json) } : wert
      }
    }
    await route.fulfill({ json: zusatzAntwort() })
  })
  await page.route(/\/api\/steuerung\/entfeuchter$/, async (route) => {
    if (route.request().method() === 'PUT') {
      const koerper = route.request().postDataJSON() as Json
      stand.hauptPuts.push(koerper)
      stand.haupt = koerper
      for (const feld of ['tempMaxTagModus', 'tempMaxTagAbstandK', 'tempMaxTagFestC', 'tempMaxNachtModus', 'tempMaxNachtAbstandK', 'tempMaxNachtFestC']) {
        stand.einstellungen[feld] = koerper[feld]
      }
    }
    await route.fulfill({ json: hauptAntwort() })
  })
  return stand
}

async function seiteOeffnen(page: Page): Promise<void> {
  await page.goto('/steuerung/entfeuchtung', { waitUntil: 'networkidle' })
  await expect(page.getByRole('heading', { name: 'Entfeuchtung', level: 1 })).toBeVisible()
}

/** A-015: Reiter und Klappkacheln. Die Seite öffnet auf „Überblick". */
const reiterWahl = (page: Page, name: string) => page.locator('.v1-tab', { hasText: new RegExp(`^${name}$`) }).click()
const aufklappen = async (page: Page, titel: RegExp) => {
  const kopf = page.locator('.st-kk-kopf[aria-expanded="false"]', { hasText: titel })
  if (await kopf.count()) await kopf.first().click()
}
const alleKartenOeffnen = async (page: Page) => {
  const zu = page.locator('.st-kk-kopf[aria-expanded="false"]')
  // Die Liste schrumpft mit jedem Klick — immer die erste nehmen.
  for (let i = 0; i < 30 && (await zu.count()) > 0; i++) await zu.first().click()
}
const TABS = ['Überblick', 'Regel', 'Schutz', 'Einrichtung']

const kasten = (page: Page) => page.locator('[data-audit="zusatz-aenderungen"]')
const speichern = (page: Page) => page.getByRole('button', { name: 'Speichern', exact: true })
/** Die Höchsttemperatur steht einmal im Reiter „Schutz" (Tag oder Nacht). */
const block = (page: Page, titel: 'Tag' | 'Nacht') => page.locator('.ef-tempmax')
  .filter({ has: page.getByRole('radiogroup', { name: `Temperatur max. ${titel}` }) })
const festFeld = (page: Page, titel: 'Tag' | 'Nacht') => block(page, titel).getByLabel('Fester Wert', { exact: true })

test('die Seite lädt: ein Eintrag über der Überschrift, der Überblick ist nur zum Lesen, die Geräte zeigen nur ihren Status', async ({ page }) => {
  await backendVorgeben(page)
  await seiteOeffnen(page)

  // EIN Eintrag „Entfeuchtung" statt zwei Chips, und die Auswahl steht über der Überschrift (Bru, 08.10.2026).
  const chip = page.getByRole('tab', { name: 'Entfeuchtung' })
  await expect(chip).toHaveAttribute('aria-current', 'true')
  await expect(page.getByRole('tab', { name: 'RDWC Dehumi' })).toHaveCount(0)
  await expect(page.getByRole('tab', { name: 'Dehumi RDWC Tent' })).toHaveCount(0)
  const chipOben = (await chip.boundingBox())!.y
  const titelOben = (await page.getByRole('heading', { name: 'Entfeuchtung', level: 1 }).boundingBox())!.y
  expect(chipOben, 'die Auswahl der Steuerungen steht unter der Überschrift').toBeLessThan(titelOben)
  await expect(page.locator('main .v1-eyebrow').first()).toHaveText('Betrieb')

  // Überblick: Messwerte einmal fürs Zelt, mit Farbzonen.
  const karte = page.locator('.ef-band')
  await expect(karte).toContainText('55,2')
  await expect(karte).toContainText('Höchsttemperatur 26,5 °C')
  await expect(karte.locator('.ef-marken').last()).toContainText('Zusatz')
  await expect(karte.locator('.ef-marken').last()).toContainText('Haupt')
  await expect(page.locator('.v1-alert').first()).toContainText('deutlich daneben')

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

  // Schutz: die Höchsttemperatur steht einmal, für alle.
  await reiterWahl(page, 'Schutz')
  await expect(festFeld(page, 'Tag')).toHaveValue('26.5')
  await expect(festFeld(page, 'Nacht')).toHaveValue('25')
  await expect(page.getByText('Die Höchsttemperatur gilt für alle Entfeuchter.')).toBeVisible()

  // Regel: dieselben Felder für beide Geräte, danach „Nur für …".
  await reiterWahl(page, 'Regel')
  const ruhig = page.locator('.st-kk', { hasText: 'Wie ruhig schaltet er?' })
  await expect(ruhig.getByRole('radiogroup')).toHaveCount(2)
  await expect(page.getByRole('radio', { name: 'normal · 4 %' })).toHaveAttribute('aria-checked', 'true')
  await expect(page.getByRole('radio', { name: 'normal · 0,15' })).toHaveAttribute('aria-checked', 'true')
  await expect(page.getByText('Nur für Dehumi RDWC Tent')).toBeVisible()
  await expect(page.getByRole('radiogroup', { name: 'Hilfsstärke' }).getByRole('radio', { name: 'normal', exact: true })).toHaveAttribute('aria-checked', 'true')

  // Die Reiter heißen so, und „Betrieb" gibt es nicht mehr.
  await expect(page.locator('.v1-tab')).toHaveText(TABS)

  // Der Zusatz geht wegen VPD oder Feuchte nur aus, wenn das Hauptgerät läuft — als Satz mit dem Namen.
  await reiterWahl(page, 'Schutz')
  await expect(page.locator('[data-audit="zusatz-fuehrung-regel"]'))
    .toHaveText('Der Zusatz geht wegen VPD oder Feuchte nur aus, wenn das Hauptgerät RDWC Dehumi läuft.')
  await aufklappen(page, /Meldung/)
  await expect(page.getByText('Als Meldung in Home Assistant und als Push an die in den Meldungs-Einstellungen gewählte Adresse.')).toBeVisible()
  await expect(page.getByText(/Push aufs Handy|Meldungsliste/)).toHaveCount(0)
  const text = await page.locator('main.v1-page').innerText()
  expect(text).not.toMatch(/Port\s*7|Aufstellung|Kreislauf|Normalbetrieb|Mindestens ein Gerät läuft|So läuft immer eins/i)
})

test('ohne Zusatz-Entfeuchter steht nur der Hauptentfeuchter da — ohne Hilfsstärke und ohne zweite Zeile', async ({ page }) => {
  await backendVorgeben(page, { zusatzAn: null, zusatzOnline: null, leistungW: null, fuehrungAn: null, energieHeuteKwh: null })
  await seiteOeffnen(page)
  await expect(page.getByText('Dehumi RDWC Tent')).toHaveCount(0)
  await expect(page.locator('.st-kk', { hasText: 'Geräte' }).locator('.st-nurlesen')).toHaveText(['läuft'])
  await reiterWahl(page, 'Regel')
  await expect(page.getByRole('radiogroup', { name: 'Hilfsstärke' })).toHaveCount(0)
  await expect(page.getByRole('radiogroup').filter({ hasText: 'kPa' })).toHaveCount(0)
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
  await reiterWahl(page, 'Regel')
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
  // Echte Namen aus Brus Anlage — lang genug, um die Zeile zu sprengen (08.10.2026, Screenshot).
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
  const punkte = page.locator('.ef-band .ef-ist')
  for (let i = 0; i < await punkte.count(); i++) await expect(punkte.nth(i)).not.toHaveClass(/\bis-(knapp|kritisch)\b/)

  // Zweiter Durchgang, ohne Plan-Obergrenze: dann gilt die EIN-Schwelle — und 48,1 % sind „knapp daneben".
  await page.unroute(/\/api\/steuerung\/entfeuchter$/)
  await page.unroute(/\/api\/steuerung\/entfeuchter-zusatz$/)
  await backendVorgeben(page, { ...werte, feuchteEinProzent: 45.8 }, { ...werte, einAktivProzent: 45.8, ausAktivProzent: 43.8, rhObergrenzeProzent: null })
  await page.reload({ waitUntil: 'networkidle' })
  await expect(page.locator('.v1-alert').first()).toContainText('Knapp daneben')
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
  await reiterWahl(page, 'Schutz')

  // Durchgang 1: Mindestpause 10 → 12. Die Hilfsstärke wird dabei zu „eigene Werte".
  await aufklappen(page, /Mindestpause/)
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
  await aufklappen(page, /Früher aus, später wieder an/)
  await page.getByLabel('Zusatz geht früher aus', { exact: true }).fill('2')
  await speichern(page).click()
  await expect(page.getByText('Gespeichert.')).toBeVisible()
  expect(stand.puts.at(-1)).toEqual({ folgeAbstandK: 2 })
})

test('die Höchsttemperatur geht über den Hauptentfeuchter — und das zweite Speichern setzt die erste nicht zurück', async ({ page }) => {
  const stand = await backendVorgeben(page)
  await seiteOeffnen(page)
  await reiterWahl(page, 'Schutz')

  // Durchgang 1: Tag-Grenze 26,5 → 27.
  await festFeld(page, 'Tag').fill('27')
  await speichern(page).click()
  await expect(page.getByText('Gespeichert.')).toBeVisible()
  expect(stand.hauptPuts).toHaveLength(1)
  expect(stand.hauptPuts[0]).toMatchObject({ tempMaxTagFestC: 27, tempMaxNachtFestC: 25, hystereseProzent: 4 })
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
  await reiterWahl(page, 'Regel')
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

test('ein geleertes Feld sperrt das Speichern und wird markiert, auch in einer zugeklappten Kachel', async ({ page }) => {
  const stand = await backendVorgeben(page)
  await seiteOeffnen(page)
  await reiterWahl(page, 'Schutz')
  await aufklappen(page, /Meldung/)
  await page.getByLabel('Meldung unter', { exact: true }).fill('')
  await page.locator('.st-kk-kopf', { hasText: /Meldung/ }).click()
  await expect(page.getByLabel('Meldung unter', { exact: true })).toBeHidden()
  await reiterWahl(page, 'Einrichtung')
  await speichern(page).click()
  await expect(page.getByText('Bitte die markierten Felder prüfen.')).toBeVisible()
  // Der Reiter „Schutz" ist wieder offen, die Kachel hat sich geöffnet, das Feld trägt die Markierung.
  await expect(page.getByLabel('Meldung unter', { exact: true })).toBeVisible()
  await expect(page.locator('.st-fehler')).toHaveText('Bitte eine Zahl eintragen.')
  expect(stand.puts).toEqual([])
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
  await expect(karte).toContainText('keine Aussage')
  // Kein Istwert-Punkt ohne Messwert, keine „Verbrauch heute"-Kachel ohne Werte, Geräte „unbekannt".
  await expect(karte.locator('.ef-ist')).toHaveCount(0)
  await expect(page.getByText(/Verbrauch heute/)).toHaveCount(0)
  await expect(page.locator('.st-kk', { hasText: 'Geräte' }).locator('.st-nurlesen').first()).toHaveText('unbekannt')
})

for (const breite of [320, 390]) {
  test(`Handy ${breite} px: nichts ragt über den Rand — auf allen vier Reitern, mit offenen Kacheln, Kasten und Warnungen`, async ({ page }) => {
    await page.setViewportSize({ width: breite, height: 800 })
    await backendVorgeben(page, { ziehtNichts: true, leistungW: 3, zusatzAn: false, tempC: 27.6 })
    await seiteOeffnen(page)
    await reiterWahl(page, 'Schutz')
    await festFeld(page, 'Tag').fill('27')
    await reiterWahl(page, 'Regel')
    await page.getByRole('radiogroup', { name: 'Hilfsstärke' }).getByRole('radio', { name: 'sparsam', exact: true }).click()

    for (const reiter of TABS) {
      await reiterWahl(page, reiter)
      await alleKartenOeffnen(page)

      const ueberstand = await page.evaluate(() => {
        const w = document.documentElement.clientWidth
        const imWischbereich = (el: HTMLElement): boolean => {
          for (let n = el.parentElement; n && n !== document.body; n = n.parentElement) {
            const ox = getComputedStyle(n).overflowX
            if ((ox === 'auto' || ox === 'scroll') && n.scrollWidth > n.clientWidth + 1) return true
          }
          return false
        }
        return [...document.querySelectorAll<HTMLElement>('main *')]
          .filter((el) => {
            const r = el.getBoundingClientRect()
            return r.width > 0 && r.height > 0 && r.right > w + 1 && !imWischbereich(el)
          })
          .slice(0, 8)
          .map((el) => `${el.tagName.toLowerCase()}.${String(el.className).split(' ')[0]}@${Math.round(el.getBoundingClientRect().right)}`)
      })
      expect(ueberstand, `${reiter}: ragt bei ${breite} px rechts hinaus`).toEqual([])
      expect(await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1),
        `${reiter}: die Seite scrollt bei ${breite} px seitwärts`).toBe(false)

      // Die Beschriftungen der Bänder liegen nicht übereinander (Text, nicht Kasten messen).
      const kollisionen = await page.evaluate(() => {
        const funde: string[] = []
        for (const reihe of document.querySelectorAll('.ef-marken')) {
          const boxen = [...reihe.querySelectorAll('span')].map((s) => {
            const r = document.createRange(); r.selectNodeContents(s)
            const b = r.getBoundingClientRect()
            return { t: s.textContent ?? '', l: b.left, r: b.right, o: b.top, u: b.bottom }
          })
          // Zwei Marken stören sich nur, wenn sich ihre Textkästen waagerecht UND senkrecht überdecken (gestaffelte Marken liegen in zwei Zeilen).
          for (let i = 0; i < boxen.length; i++) for (let j = i + 1; j < boxen.length; j++) {
            const a = boxen[i], c = boxen[j]
            if (a.l < c.r - 0.5 && c.l < a.r - 0.5 && a.o < c.u - 0.5 && c.o < a.u - 0.5) funde.push(`${a.t} / ${c.t}`)
          }
        }
        return funde
      })
      expect(kollisionen, `${reiter}: Bandbeschriftungen überlappen bei ${breite} px`).toEqual([])
    }
  })
}

for (const schema of ['light', 'dark'] as const) {
  test(`Lesbarkeit der Seite in der ${schema === 'light' ? 'hellen' : 'dunklen'} Ansicht (Kontrast mindestens 4,5)`, async ({ page }) => {
    await page.emulateMedia({ colorScheme: schema })
    await page.addInitScript((s) => localStorage.setItem('growos.theme', s), schema)
    await backendVorgeben(page, { ziehtNichts: true, leistungW: 3, zusatzAn: false, tempC: 27.6 })
    await seiteOeffnen(page)
    await reiterWahl(page, 'Regel')
    await page.getByRole('radiogroup', { name: 'Hilfsstärke' }).getByRole('radio', { name: 'sparsam', exact: true }).click()
    await reiterWahl(page, 'Schutz')
    await block(page, 'Tag').getByRole('radio', { name: 'Fest' }).click()
    await festFeld(page, 'Tag').fill('28')
    await expect(page.locator('.ez-aender')).toBeVisible()
    await reiterWahl(page, 'Regel')
    await expect(page.locator('.ez-empf.is-abweichend').first()).toBeVisible()

    for (const reiter of TABS) {
      await reiterWahl(page, reiter)
      await alleKartenOeffnen(page)
      const funde = await page.evaluate(`(() => {
        ${KONTRAST_HELFER}
        const funde = []
        for (const el of document.querySelectorAll('main *')) {
          const eigen = [...el.childNodes].filter((k) => k.nodeType === 3).map((k) => k.textContent).join('').trim()
          if (!eigen) continue
          const s = getComputedStyle(el)
          if (s.visibility === 'hidden' || s.display === 'none') continue
          if (el.closest('[disabled], [aria-disabled="true"], .is-disabled, [hidden], .is-ruht')) continue
          // Die Zahl über dem Istwert-Punkt (.ef-ist em) liegt über dem Punkt, nicht auf ihm:
          // gemessen wird gegen die Fläche des Bandes, nicht gegen die Punktfarbe.
          const punkt = el.closest('.ef-ist')
          const grund = flaeche(punkt ? punkt.parentElement : el)
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
  await zwei.fill('Zelt-Trotec')
  await knopf.click()
  await expect(page.getByText('Gespeichert.')).toBeVisible()
  expect(puts).toEqual([{ zusatz: 'Zelt-Trotec' }])
  await expect(knopf).toBeDisabled()

  // Durchgang 2, ohne Neuladen: nur die Führung — der Zusatz steht schon in der Antwort und fehlt im Körper.
  await eins.fill('Hauptgerät')
  await knopf.click()
  await expect(knopf).toBeDisabled()
  expect(puts).toEqual([{ zusatz: 'Zelt-Trotec' }, { fuehrung: 'Hauptgerät' }])

  // Zurück auf die Vorgabe: leer geschickt.
  await page.locator('.st-feldzeile', { hasText: 'Entfeuchter 2 · Zusatz' }).getByRole('button', { name: 'Vorgabe', exact: true }).click()
  await knopf.click()
  await expect(knopf).toBeDisabled()
  expect(puts.at(-1)).toEqual({ zusatz: '' })
})
