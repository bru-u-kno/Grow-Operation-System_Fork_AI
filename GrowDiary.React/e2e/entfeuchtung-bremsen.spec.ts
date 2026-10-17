import { test, expect } from '@playwright/test'
import { EINSTELLUNGEN, HAUPT_EINSTELLUNGEN, alleKartenOeffnen, alleOeffnen, aufklappen, backendVorgeben, reiterWahl, seiteOeffnen } from './entfeuchtung-vorgaben'
import type { Json } from './entfeuchtung-vorgaben'

/**
 * A-022: „Was bremst gerade?" und Tag/Nacht getrennt aus dem Plan.
 *
 * <b>Womit diese Datei arbeitet.</b> Die Antworten des Backends sind vorgegeben (wie in `entfeuchtung.spec.ts`): geprüft wird, was
 * die Seite aus der Zusage macht. Die Zahlen sind der Befund vom 14.10.2026 aus der Anlage des Nutzers — Plan-Obergrenze Tag 53 %,
 * Nacht 50 %, gemessen 57,9 %, der Entfeuchter lief seit Stunden und die Feuchte sank nicht.
 */

const ANLAGE_TAG: Json = {
  feuchteProzent: 57.9, tempC: 22, tagPhase: true, einAktivProzent: 48, ausAktivProzent: 46,
  rhObergrenzeProzent: 53, rhObergrenzeNachtProzent: 50, grenzeAktivProzent: 53, tempMaxAktivC: 23,
  deckelProzent: 52, portAn: true, portOnline: true, automatikAn: true, zuluftVorrang: false,
  planWoche: 'Flush', planLuftTagC: 18, planLuftNachtC: 17, tempMaxTagC: 23, tempMaxNachtC: 23,
  ueberGrenzeSeitMin: 150, laeuftSeitMin: 130,
}

const KAPAZITAET = {
  art: 'kapazitaet', stufe: 'kritisch', titel: 'Der Entfeuchter schafft es nicht',
  text: 'Er läuft seit 2 h 10 Min. und die Feuchte liegt seit 2 h 30 Min. über dem Plan-Wert (57,9 statt 53 %). Einstellungen helfen hier nicht mehr.',
}
const AUSSENLUFT = {
  art: 'aussenluft', stufe: 'warnung', titel: 'Wartet auf Außenluft',
  text: 'Die Zuluft läuft und die Außenluft ist trockener als der Raum. Der Entfeuchter wartet deshalb 5 statt 1 Minuten, bevor er einschaltet.',
}

const ZUSATZSPERRE = {
  art: 'zusatzsperre', stufe: 'warnung', titel: 'Der Zusatz-Entfeuchter wartet auf Abkühlung',
  text: 'Das Zelt hat 22,4 °C. Der Zusatz schaltet erst unter 22 °C wieder ein, damit er das Zelt nicht weiter aufheizt. Solange läuft nur der Hauptentfeuchter; die Feuchte liegt mit 56,2 % über dem Plan-Wert (53 %).',
}

const bremseKachel = (page: import('@playwright/test').Page) => page.locator('.st-kk', { has: page.locator('.st-kk-titel', { hasText: 'Was bremst gerade?' }) })

test('Überblick: „Was bremst gerade?" nennt Kapazität und Außenluft mit Zahlen — zweimal', async ({ page }) => {
  await backendVorgeben(page, {}, { ...ANLAGE_TAG, bremsen: [KAPAZITAET, AUSSENLUFT] })
  await seiteOeffnen(page)

  const kachel = bremseKachel(page)
  await expect(kachel).toHaveCount(1)
  await expect(kachel.locator('.st-kk-kopf')).toHaveAttribute('aria-expanded', 'true')
  await expect(kachel.locator('.v1-alert')).toHaveCount(2)
  await expect(kachel.locator('.v1-alert').first()).toContainText('Der Entfeuchter schafft es nicht')
  await expect(kachel.locator('.v1-alert').first()).toContainText('57,9 statt 53 %')
  await expect(kachel.locator('.v1-alert').nth(1)).toContainText('5 statt 1 Minuten')

  // Zuklappen zeigt die Zahl der Hinweise, Aufklappen bringt sie zurück; und das ein zweites Mal.
  for (let i = 0; i < 2; i++) {
    await kachel.locator('.st-kk-kopf').click()
    await expect(kachel.locator('.st-kk-zus')).toHaveText('2 Hinweise')
    await kachel.locator('.st-kk-kopf').click()
    await expect(kachel.locator('.v1-alert')).toHaveCount(2)
  }
})

test('Überblick: ohne Befund steht „nichts" — und die Kachel ist zugeklappt', async ({ page }) => {
  await backendVorgeben(page, {}, { ...ANLAGE_TAG, feuchteProzent: 47.2, bremsen: [], ueberGrenzeSeitMin: null })
  await seiteOeffnen(page)

  const kachel = bremseKachel(page)
  await expect(kachel.locator('.st-kk-kopf')).toHaveAttribute('aria-expanded', 'false')
  await expect(kachel.locator('.st-kk-zus')).toHaveText('nichts')
  await aufklappen(page, /Was bremst gerade/)
  await expect(kachel).toContainText('Nichts bremst')
  await expect(kachel.locator('.v1-alert')).toHaveCount(0)
})

test('Überblick: ein Server ohne die neuen Felder bricht die Seite nicht', async ({ page }) => {
  await backendVorgeben(page, {}, { feuchteProzent: 50 })
  await seiteOeffnen(page)
  await expect(bremseKachel(page).locator('.st-kk-zus')).toHaveText('nichts')
})

test('Überblick: nachts gilt die Nacht-Obergrenze des Plans als Ziel', async ({ page }) => {
  // Nacht, Plan-Obergrenze Nacht 50 %: das Ziel der Luftfeuchte endet bei 50, nicht bei den 53 des Tages.
  await backendVorgeben(page, {}, { ...ANLAGE_TAG, tagPhase: false, grenzeAktivProzent: 50, feuchteProzent: 49, bremsen: [] })
  await seiteOeffnen(page)
  const ziel = page.locator('.ef-band .ef-block').first().locator('.gos-metric-ziel')
  await expect(ziel).toContainText('50')
  await expect(ziel).not.toContainText('53')
})

test('Einstellungen: Tag und Nacht stehen getrennt da, jeweils aus der eigenen Plan-Obergrenze — zweimal', async ({ page }) => {
  const haupt = {
    vpdRegelung: false, hystereseProzent: 2, mindestlaufzeitMin: 10, einschaltverzoegerungMin: 1, wartezeitAussenluftMin: 5,
    tagbetriebErlauben: true, automatikAktiv: true, arbeitsbereichQuelle: 'plan', vorlaufProzent: 5,
    tempMaxTagModus: 'plan', tempMaxTagAbstandK: 5, tempMaxTagFestC: 23, tempMaxNachtModus: 'plan', tempMaxNachtAbstandK: 6, tempMaxNachtFestC: 23,
    feuchteEinTag: 48, feuchteAusTag: 46, feuchteEinNacht: 45, feuchteAusNacht: 43,
  }
  await backendVorgeben(page, {}, { ...ANLAGE_TAG, bremsen: [] }, { haupt })
  await seiteOeffnen(page)
  await reiterWahl(page, 'Einstellungen')
  await alleKartenOeffnen(page)

  const ziel = page.locator('.st-kk', { has: page.locator('.st-kk-titel', { hasText: 'Ziel · Luftfeuchte' }) })
  for (let i = 0; i < 2; i++) {
    await expect(ziel).toContainText('Plan-Obergrenze Tag')
    await expect(ziel).toContainText('≤ 53 %')
    await expect(ziel).toContainText('Plan-Obergrenze Nacht')
    await expect(ziel).toContainText('≤ 50 %')
    const zeile = (titel: string) => ziel.locator('.st-feldzeile', { hasText: titel }).locator('.st-nurlesen')
    await expect(zeile('Einschalten über · Tag')).toHaveText('48 %')
    await expect(zeile('Ausschalten unter · Tag')).toHaveText('46 %')
    await expect(zeile('Einschalten über · Nacht')).toHaveText('45 %')
    await expect(zeile('Ausschalten unter · Nacht')).toHaveText('43 %')
    await reiterWahl(page, 'Überblick')
    await reiterWahl(page, 'Einstellungen')
    await alleKartenOeffnen(page)
  }
})

test('Überblick: Zusatz steht im Bereich zwischen Abschalt- und Wieder-ein-Temperatur — Hinweis, „wartet" und kein ✔ bei der Temperatur', async ({ page }) => {
  // Beispiel-Grenzen: Höchsttemperatur 24, Zusatz aus bei 23, wieder an unter 22 — Zelt 22,4 °C, Zusatz aus, Feuchte über dem Plan-Wert.
  await backendVorgeben(
    page,
    { tempC: 22.4, feuchteProzent: 56.2, tagPhase: true, zusatzAn: false, leistungW: 0, fuehrungAn: true, schaltgroesse: 'feuchte', tempMaxTagC: 24, folgeAusTagC: 23, wiederEinTagC: 22 },
    { ...ANLAGE_TAG, tempC: 22.4, feuchteProzent: 56.2, tempMaxAktivC: 24, tempMaxTagC: 24, bremsen: [ZUSATZSPERRE], ueberGrenzeSeitMin: 40, laeuftSeitMin: 30 },
    { haupt: { ...HAUPT_EINSTELLUNGEN, tempMaxTagFestC: 24 }, zusatz: { ...EINSTELLUNGEN, tempMaxTagFestC: 24 } },
  )
  await seiteOeffnen(page)

  const kachel = bremseKachel(page)
  await expect(kachel.locator('.v1-alert')).toHaveCount(1)
  await expect(kachel.locator('.v1-alert').first()).toContainText('wartet auf Abkühlung')
  await expect(kachel.locator('.v1-alert').first()).toContainText('unter 22 °C')

  // Der obere „pausiert"-Hinweis gilt erst über der Abschalt-Temperatur — hier (22,4 < 23) steht er nicht da, die Kachel sagt es.
  await expect(page.getByText('pausiert: Zelt zu warm')).toHaveCount(0)

  // „Warum läuft welches Gerät?": die Temperaturzeile des Zusatzes ist NICHT in Ordnung, weil er erst unter 22 °C startet.
  await alleOeffnen(page)
  const zeile = page.locator('.st-feldzeile', { hasText: 'Dehumi RDWC Tent: Zelt nicht zu warm' })
  await expect(zeile).toContainText('wieder an unter 22')
  await expect(zeile.locator('.st-nurlesen')).toHaveAttribute('aria-label', 'nicht erfüllt')

  // Gerätestatus des Zusatzes: „wartet" statt „aus".
  await expect(page.locator('.st-feldzeile', { hasText: 'Zusatz-Entfeuchter · hilft' })).toContainText('wartet')
})

test('Überblick: im selben Temperaturbereich, aber die Feuchte reicht — der Zusatz steht, ohne zu „warten"', async ({ page }) => {
  await backendVorgeben(
    page,
    { tempC: 22.4, feuchteProzent: 47, tagPhase: true, zusatzAn: false, leistungW: 0, fuehrungAn: true, schaltgroesse: 'feuchte', tempMaxTagC: 24, folgeAusTagC: 23, wiederEinTagC: 22 },
    { ...ANLAGE_TAG, tempC: 22.4, feuchteProzent: 47, tempMaxAktivC: 24, tempMaxTagC: 24, bremsen: [], ueberGrenzeSeitMin: null },
    { haupt: { ...HAUPT_EINSTELLUNGEN, tempMaxTagFestC: 24 }, zusatz: { ...EINSTELLUNGEN, tempMaxTagFestC: 24 } },
  )
  await seiteOeffnen(page)
  await expect(page.locator('.st-feldzeile', { hasText: 'Zusatz-Entfeuchter · hilft' })).not.toContainText('wartet')
})
