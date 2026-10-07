import { test, expect, type Page } from '@playwright/test'
import { KONTRAST_HELFER } from './kontrast-messung'

/**
 * A-009: Die Seite „Zusatz-Entfeuchter" und die Namen der Entfeuchter.
 *
 * <b>Womit diese Datei arbeitet.</b> Die Antworten des Backends sind
 * <b>vorgegeben</b> (`page.route`, wie in `ha-offline.spec.ts`): Strang 1 (der
 * Dienst) und Strang 2 (die Seite) entstehen getrennt, und der Vertrag
 * (`archiv/a009/VERTRAG.md`) ist die Zusage dazwischen. Geprüft wird deshalb,
 * was die Seite aus dieser Zusage macht — nicht, ob das Backend sie hält. Ein
 * Lauf gegen die echte App mit Backend ersetzt das nicht; er gehört nach dem
 * Zusammenführen dazu. Die Prüfung läuft ohne Backend und überspringt sich nie.
 *
 * <b>Die Zusagen, um die es geht</b> (ENTSCHEIDUNGEN.md, Punkt 8): Speichern
 * schreibt NUR geänderte Felder — am 06.10.2026 hat ein Speichern unbemerkt die
 * Tag-Grenze von 26,5 auf 29 °C zurückgesetzt. Der gelbe Kasten davor nennt
 * dieselben Felder. Und: zweimal speichern muss gehen (CLAUDE.md, „Die Reparatur
 * einmal WIEDERHOLEN").
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

const MODULE = {
  haErreichbar: true,
  standUtc: '2026-10-06T20:00:00Z',
  module: [
    { kennung: 'co2', titel: 'CO₂', status: 'an', kurz: 'Begasung', wert: '800 ppm', unterzeile: 'Ziel 900', hatDetail: true },
    { kennung: 'entfeuchter', titel: 'RDWC Dehumi', status: 'an', kurz: 'Entfeuchter', wert: '55 %', unterzeile: 'läuft', hatDetail: true },
    { kennung: 'entfeuchter-zusatz', titel: 'Dehumi RDWC Tent', status: 'an', kurz: 'Zusatz', wert: '313 W', unterzeile: 'läuft', hatDetail: true },
  ],
}

type Stand = { einstellungen: Json; puts: Json[] }

/**
 * Die Antworten vorgeben. `PUT` verhält sich wie der Vertrag: es werden nur die
 * Felder übernommen, die im Körper stehen, die Antwort ist die ganze Seite.
 */
async function backendVorgeben(page: Page, live: Json = {}): Promise<Stand> {
  const stand: Stand = { einstellungen: structuredClone(EINSTELLUNGEN), puts: [] }
  const antwort = () => ({
    einstellungen: stand.einstellungen,
    live: { ...LIVE, ...live },
    geraeteZugeordnet: 8, geraeteGesamt: 8, ausHomeAssistantUebernommen: false, haAngenommen: null,
  })
  await page.route(/\/api\/steuerung$/, (route) => route.fulfill({ json: MODULE }))
  await page.route(/\/api\/steuerung\/entfeuchter-zusatz$/, async (route) => {
    if (route.request().method() === 'PUT') {
      const koerper = route.request().postDataJSON() as Json
      stand.puts.push(koerper)
      for (const [feld, wert] of Object.entries(koerper)) {
        stand.einstellungen[feld] = feld === 'meldung' ? { ...(stand.einstellungen.meldung as Json), ...(wert as Json) } : wert
      }
    }
    await route.fulfill({ json: antwort() })
  })
  return stand
}

async function seiteOeffnen(page: Page): Promise<void> {
  await page.goto('/steuerung/entfeuchter-zusatz', { waitUntil: 'networkidle' })
  await expect(page.getByRole('heading', { name: 'Entfeuchtung', level: 1 })).toBeVisible()
}

/** A-014: Reiter und Klappkacheln. Die Seite öffnet auf „Überblick". */
const reiterWahl = (page: Page, name: string) => page.locator('.v1-tab', { hasText: new RegExp(`^${name}$`) }).click()
const aufklappen = async (page: Page, titel: RegExp) => {
  const kopf = page.locator('.st-kk-kopf[aria-expanded="false"]', { hasText: titel })
  if (await kopf.count()) await kopf.first().click()
}
const alleKartenOeffnen = async (page: Page) => {
  const zu = page.locator('.st-kk-kopf[aria-expanded="false"]')
  // Die Liste schrumpft mit jedem Klick — immer die erste nehmen.
  for (let i = 0; i < 20 && (await zu.count()) > 0; i++) await zu.first().click()
}
const TABS = ['Überblick', 'Regel', 'Schutz', 'Betrieb']

const kasten = (page: Page) => page.locator('[data-audit="zusatz-aenderungen"]')
const speichern = (page: Page) => page.getByRole('button', { name: 'Speichern', exact: true })
const block = (page: Page, titel: string) => page.locator('.ef-tempmax')
  .filter({ has: page.getByRole('radiogroup', { name: titel }) })
const festFeld = (page: Page, titel: string) => block(page, titel).getByLabel('Fester Wert', { exact: true })

test('die Seite lädt: Chip, Statuskarte mit zwei Bändern, Einstellungen — und noch nichts zu speichern', async ({ page }) => {
  await backendVorgeben(page)
  await seiteOeffnen(page)

  // Chip mit dem Namen des Zusatzes, der aktuelle ist markiert.
  const chip = page.getByRole('tab', { name: 'Dehumi RDWC Tent' })
  await expect(chip).toHaveAttribute('aria-current', 'true')
  await expect(page.getByRole('tab', { name: 'RDWC Dehumi' })).toBeVisible()

  // Überblick: Tag, also VPD (mit Band), dazu Luftfeuchte und Temperatur mit Farbzonen.
  const karte = page.locator('.ef-band')
  await expect(karte).toContainText('1,31')
  await expect(karte).toContainText('entfeuchtet')
  await expect(karte.locator('.ez-bandtitel')).toHaveCount(1)
  await expect(karte.locator('.ez-bandtitel').first()).toContainText('VPD · Plan-Ziel 1,40')
  await expect(karte).toContainText('Höchsttemperatur 26,5 °C')
  await expect(karte.locator('.ef-marken').last()).toContainText('Zusatz')
  await expect(karte.locator('.ef-marken').last()).toContainText('25,5')
  await expect(karte.locator('.ef-marken').last()).toContainText('Haupt')
  await expect(karte.locator('.ef-marken').last()).toContainText('26,5')
  // Zonen: 25,1 °C liegt im Ziel (bis „Zusatz aus" 25,5), 55,2 % rF weit über dem Ziel 39 % — die Lage sagt es als Wort.
  await expect(page.locator('.v1-alert').first()).toContainText('deutlich daneben')

  // Einstellungen: Höchsttemperatur Tag/Nacht aus den gemeinsamen Feldern (Reiter „Schutz").
  await reiterWahl(page, 'Schutz')
  await expect(festFeld(page, 'Höchsttemperatur tagsüber')).toHaveValue('26.5')
  await expect(festFeld(page, 'Höchsttemperatur nachts')).toHaveValue('25')
  await reiterWahl(page, 'Regel')
  await expect(page.getByRole('radio', { name: 'normal', exact: true })).toHaveAttribute('aria-checked', 'true')

  // Nichts geändert: kein Kasten, kein Speichern.
  await expect(kasten(page)).toHaveCount(0)
  await expect(speichern(page)).toHaveCount(0)

  // „Zuschalten erst nach" steht im Reiter „Betrieb"; keine festen Schwellen, keine Aufstellung, kein „Port 7".
  await expect(page.getByText('Zuschalten erst nach')).toHaveCount(0)
  await reiterWahl(page, 'Betrieb')
  await expect(page.getByText('Zuschalten erst nach')).toBeVisible()
  await reiterWahl(page, 'Schutz')
  await aufklappen(page, /Laufverhalten/)
  await aufklappen(page, /Meldung/)
  const text = await page.locator('main.v1-page').innerText()
  expect(text).not.toMatch(/Port\s*7|Aufstellung|Feste Schwellen|Kreislauf|Normalbetrieb|Mindestens ein Gerät läuft|So läuft immer eins/i)
  // Die Regel zum Führungsgerät steht als Satz mit dem Namen, ohne den Wert „immer".
  await expect(page.locator('[data-audit="zusatz-fuehrung-regel"]'))
    .toHaveText('Der Zusatz geht wegen VPD oder Feuchte nur aus, wenn das Hauptgerät RDWC Dehumi läuft.')
  // Die beiden Schalter sagen, womit sie wirken (Reiter „Regel", Kachel „Tag & Nacht").
  await reiterWahl(page, 'Regel')
  await aufklappen(page, /Tag & Nacht/)
  await expect(page.locator('.v1-switch', { hasText: 'Nachts durchlaufen' })).toContainText('Wirkt mit der vom Fork angelegten Regelung.')
  await expect(page.locator('.v1-switch', { hasText: 'Auch tagsüber entfeuchten' })).toContainText('Wirkt mit der vom Fork angelegten Regelung.')
  // Der Meldungsweg ist der, den der Fork wirklich hat, und die Ursache eine Frage.
  await reiterWahl(page, 'Schutz')
  await aufklappen(page, /Meldung/)
  await expect(page.getByText('Als Meldung in Home Assistant und als Push an die in den Meldungs-Einstellungen gewählte Adresse.')).toBeVisible()
  await expect(page.getByText(/Push aufs Handy|Meldungsliste/)).toHaveCount(0)
  await expect(page.locator('.v1-switch', { hasText: 'Melden, wenn der Shelly an ist' })).toContainText('Tank voll oder Gerät ausgeschaltet? Gerade nimmt Dehumi RDWC Tent 313 W auf.')
})

test('Luftfeuchte-Zone: das Ziel ist die Plan-Obergrenze, wie beim Hauptentfeuchter — nicht die EIN-Schwelle', async ({ page }) => {
  // 07.10.2026, echte Anlage: 48,1 % rF, EIN-Schwelle 45,8 %, Plan-Obergrenze 51 %. Der Hauptentfeuchter zeigte „im Ziel",
  // der Zusatz „knapp daneben" — zwei Zonen für dieselbe Messung.
  await backendVorgeben(page, { feuchteProzent: 48.1, feuchteEinProzent: 45.8, feuchteAusProzent: 43.8, rhObergrenzeProzent: 51, tempC: 23.5 })
  await seiteOeffnen(page)
  await expect(page.locator('.v1-alert').first()).toContainText('Im Ziel')
  const punkte = page.locator('.ef-band .ef-ist')
  for (let i = 0; i < await punkte.count(); i++) await expect(punkte.nth(i)).not.toHaveClass(/\bis-(knapp|kritisch)\b/)

  // Zweiter Durchgang, ohne Neuladen des Skripts: ohne Plan-Obergrenze gilt die Schwelle EIN — dann ist es „knapp".
  await page.unroute(/\/api\/steuerung\/entfeuchter-zusatz$/)
  await backendVorgeben(page, { feuchteProzent: 48.1, feuchteEinProzent: 45.8, feuchteAusProzent: 43.8, rhObergrenzeProzent: null, tempC: 23.5 })
  await page.reload({ waitUntil: 'networkidle' })
  await expect(page.locator('.v1-alert').first()).toContainText('Knapp daneben')
})

test('ohne Verbindung zu Home Assistant steht nur der HA-Hinweis, nie zusätzlich „Plan unvollständig"', async ({ page }) => {
  await backendVorgeben(page, { haErreichbar: false, planUnvollstaendig: true, schaltgroesse: 'keine' })
  await seiteOeffnen(page)
  const seite = page.locator('main.v1-page')
  await expect(seite).toContainText('Home Assistant antwortet nicht')
  await expect(seite).not.toContainText('Plan unvollständig')
})

test('„zieht nichts" erscheint nur, wenn der Server es meldet — auch bei kleiner Leistung', async ({ page }) => {
  await backendVorgeben(page, { ziehtNichts: false, leistungW: 3 })
  await seiteOeffnen(page)
  await expect(page.locator('main.v1-page')).not.toContainText('zieht nichts')
  await expect(page.locator('.ef-band')).toContainText('entfeuchtet')
})

test('ein Feld ändern: der gelbe Kasten nennt nur dieses Feld, Speichern schickt nur dieses Feld — zweimal', async ({ page }) => {
  const stand = await backendVorgeben(page)
  await seiteOeffnen(page)

  await reiterWahl(page, 'Schutz')
  // Durchgang 1: Tag-Grenze 26,5 → 27.
  await festFeld(page, 'Höchsttemperatur tagsüber').fill('27')
  await expect(kasten(page)).toBeVisible()
  await expect(kasten(page).getByText('Wird gespeichert — nur das:')).toBeVisible()
  await expect(kasten(page).locator('li')).toHaveCount(1)
  await expect(kasten(page).locator('li')).toContainText('Höchsttemperatur tagsüber (fest): 26,5 → 27,0 °C')
  await speichern(page).click()
  await expect(page.getByText('Gespeichert.')).toBeVisible()
  expect(stand.puts).toEqual([{ tempMaxTagFestC: 27 }])
  // Nach dem Speichern: Kasten und Knopf weg, das Feld trägt die Antwort.
  await expect(kasten(page)).toHaveCount(0)
  await expect(speichern(page)).toHaveCount(0)
  await expect(festFeld(page, 'Höchsttemperatur tagsüber')).toHaveValue('27')

  // Durchgang 2, ohne Neuladen: die NACHT-Grenze. Der Körper darf die Tag-Grenze nicht mehr enthalten.
  await festFeld(page, 'Höchsttemperatur nachts').fill('24')
  await expect(kasten(page).locator('li')).toHaveCount(1)
  await expect(kasten(page).locator('li')).toContainText('Höchsttemperatur nachts (fest): 25,0 → 24,0 °C')
  await speichern(page).click()
  await expect(page.getByText('Gespeichert.')).toBeVisible()
  expect(stand.puts).toEqual([{ tempMaxTagFestC: 27 }, { tempMaxNachtFestC: 24 }])
  expect(stand.einstellungen).toMatchObject({ tempMaxTagFestC: 27, tempMaxNachtFestC: 24 })
})

test('ändern und zurückstellen: nichts mehr zu speichern', async ({ page }) => {
  await backendVorgeben(page)
  await seiteOeffnen(page)
  await reiterWahl(page, 'Schutz')
  const tag = festFeld(page, 'Höchsttemperatur tagsüber')
  await tag.fill('29')
  await expect(speichern(page)).toBeVisible()
  // „Empfohlen" mit Zurücksetzen: 20 + 6,5 = 26,5.
  const block1 = block(page, 'Höchsttemperatur tagsüber')
  await expect(block1.locator('.ez-empf')).toContainText('Empfohlen: 26,5 °C')
  await block1.getByRole('button', { name: 'zurücksetzen' }).click()
  await expect(tag).toHaveValue('26.5')
  await expect(kasten(page)).toHaveCount(0)
  await expect(speichern(page)).toHaveCount(0)
  await expect(block1.locator('.ez-empf')).toContainText('Empfohlen: 26,5 °C ✓')
})

test('Hilfsstärke „sparsam": der Kasten nennt sie samt den Einzelwerten, der Körper schickt sie — und „aus" nur sich selbst', async ({ page }) => {
  const stand = await backendVorgeben(page)
  await seiteOeffnen(page)
  await reiterWahl(page, 'Regel')

  await page.getByRole('radio', { name: 'sparsam', exact: true }).click()
  await expect(kasten(page).locator('li')).toHaveCount(1)
  await expect(kasten(page).locator('li')).toContainText('Hilfsstärke: normal → sparsam')
  await expect(kasten(page).locator('li')).toContainText('Zusatz geht früher aus: 1,0 → 1,5 K')
  await speichern(page).click()
  await expect(page.getByText('Gespeichert.')).toBeVisible()
  expect(stand.puts[0]).toEqual({ hilfe: 'sparsam', folgeAbstandK: 1.5, vpdHystereseKpa: 0.25, zuschaltVerzoegerungMin: 20, mindestpauseMin: 15 })

  // Zweiter Durchgang: auf „aus" und sofort speichern — nur das Wort, nicht die Einzelwerte von „sparsam".
  await page.getByRole('radio', { name: 'sparsam', exact: true }).waitFor()
  await page.getByRole('radio', { name: 'aus', exact: true }).click()
  await speichern(page).click()
  await expect(page.getByText('Gespeichert.')).toBeVisible()
  expect(stand.puts[1]).toEqual({ hilfe: 'aus' })
})

test('ein Einzelwert unter „Schutz" macht aus der Stufe „eigene Werte" — gesendet wird nur der Einzelwert', async ({ page }) => {
  const stand = await backendVorgeben(page)
  await seiteOeffnen(page)
  await reiterWahl(page, 'Schutz')
  await aufklappen(page, /Laufverhalten/)
  await page.getByLabel('Mindestpause', { exact: true }).fill('12')
  await reiterWahl(page, 'Regel')
  await expect(page.getByRole('radio', { name: 'eigene Werte' })).toHaveAttribute('aria-checked', 'true')
  await expect(kasten(page)).toContainText('Hilfsstärke: normal → eigene Werte')
  await speichern(page).click()
  await expect(page.getByText('Gespeichert.')).toBeVisible()
  expect(stand.puts).toEqual([{ mindestpauseMin: 12 }])
})

test('ein geleertes Feld sperrt das Speichern und wird markiert, auch in einer zugeklappten Kachel', async ({ page }) => {
  const stand = await backendVorgeben(page)
  await seiteOeffnen(page)
  await reiterWahl(page, 'Schutz')
  await aufklappen(page, /Meldung/)
  await page.getByLabel('Meldung unter', { exact: true }).fill('')
  await page.locator('.st-kk-kopf', { hasText: /Meldung/ }).click()
  await expect(page.getByLabel('Meldung unter', { exact: true })).toBeHidden()
  await reiterWahl(page, 'Betrieb')
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
  // Ohne Schaltgröße gibt es kein erstes Band — kein erfundenes.
  await expect(page.locator('.ef-band .ez-bandtitel')).toHaveCount(0)
})

test('fehlende Werte stehen als „–", nie als erfundene Null', async ({ page }) => {
  await backendVorgeben(page, { vpd: null, tempC: null, feuchteProzent: null, leistungW: null, energieHeuteKwh: null, zusatzAn: null })
  await seiteOeffnen(page)
  const karte = page.locator('.ef-band')
  await expect(karte.locator('.ef-gross').first()).toContainText('–')
  await expect(karte).toContainText('– % rF · – °C')
  await expect(karte).toContainText('Zustand unbekannt')
  // Kein Istwert-Punkt ohne Messwert, und keine „Heute"-Karte ohne Werte.
  await expect(karte.locator('.ef-ist')).toHaveCount(0)
  await reiterWahl(page, 'Betrieb')
  await expect(page.getByRole('heading', { name: 'Heute' })).toHaveCount(0)
})

test('nachts mit „Nachts durchlaufen": die Plan-Feuchte ist das erste Band', async ({ page }) => {
  await backendVorgeben(page, { tagPhase: false, tempC: 23.6, feuchteProzent: 49, vpd: 1.28 })
  await seiteOeffnen(page)
  const karte = page.locator('.ef-band')
  await expect(karte.locator('.ef-gross').first()).toContainText('49')
  await expect(karte.locator('.ez-bandtitel').first()).toContainText('Luftfeuchte')
  await expect(karte).toContainText('Nachts durchlaufen')
  // 08.10.2026: Die Luftfeuchte stand nachts ZWEIMAL da — im alten Band und noch einmal im neuen Zonenblock.
  await expect(karte.locator('.ef-gross', { hasText: 'rF' })).toHaveCount(1)
  await expect(karte.locator('.ef-ist').first()).toHaveClass(/\bis-kritisch\b/)  // 49 % gegen Ziel 39 %
  // Tags dagegen gibt es VPD und Luftfeuchte — zwei verschiedene Größen, je einmal.
  await page.unroute(/\/api\/steuerung\/entfeuchter-zusatz$/)
  await backendVorgeben(page, { tagPhase: true })
  await page.reload({ waitUntil: 'networkidle' })
  await expect(karte.locator('.ef-gross', { hasText: 'kPa' })).toHaveCount(1)
  await expect(karte.locator('.ef-gross', { hasText: 'rF' })).toHaveCount(1)
})

for (const breite of [320, 390]) {
  test(`Handy ${breite} px: nichts ragt über den Rand — auf allen vier Reitern, mit offenen Kacheln, Kasten und Warnungen`, async ({ page }) => {
    await page.setViewportSize({ width: breite, height: 800 })
    await backendVorgeben(page, { ziehtNichts: true, leistungW: 3, zusatzAn: false, tempC: 27.6 })
    await seiteOeffnen(page)
    await reiterWahl(page, 'Schutz')
    await festFeld(page, 'Höchsttemperatur tagsüber').fill('27')
    await reiterWahl(page, 'Regel')
    await page.getByRole('radio', { name: 'sparsam', exact: true }).click()

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
            return { t: s.textContent ?? '', l: b.left, r: b.right }
          }).sort((a, b) => a.l - b.l)
          for (let i = 1; i < boxen.length; i++) if (boxen[i].l < boxen[i - 1].r - 0.5) funde.push(`${boxen[i - 1].t} / ${boxen[i].t}`)
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
    await page.getByRole('radio', { name: 'sparsam', exact: true }).click()
    await reiterWahl(page, 'Schutz')
    await block(page, 'Höchsttemperatur tagsüber').getByRole('radio', { name: 'Fest' }).click()
    await festFeld(page, 'Höchsttemperatur tagsüber').fill('28')
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
          if (el.closest('[disabled], [aria-disabled="true"], .is-disabled, [hidden]')) continue
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
      anzahlGeraete: 1, anzahlEntitaeten: 1, anzahlVermutet: 0, anzahlVerschoben: 0, hinweise: [],
      geraete: [{
        schluessel: 'g1', name: 'RDWC Dehumi', elternSchluessel: null, anschluss: null, istController: false, istRubrik: false,
        elternVomNutzer: false, nameVomNutzer: false, abgeleiteterEltern: null, modell: null, bestaetigt: true, vermutet: false,
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
