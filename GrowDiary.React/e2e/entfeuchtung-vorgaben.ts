import { expect, type Page } from '@playwright/test'

/**
 * Die vorgegebenen Antworten des Backends für die Seite „Entfeuchtung" — gemeinsam für
 * `entfeuchtung.spec.ts` und `entfeuchtung-einstellungen.spec.ts`.
 *
 * <b>Warum eine eigene Datei.</b> Beide Spezifikationen brauchen dieselbe Anlage (zwei Geräte, ein Plan, die
 * Grenzwerte) und dieselbe Nachbildung des Backends: Der Zusatz-`PUT` übernimmt nur die Felder aus dem Körper,
 * der des Hauptentfeuchters ersetzt alles. Zweimal abgetippt liefen die Anlagen irgendwann auseinander
 * (CLAUDE.md, „Eine Wahrheit je Zahl").
 *
 * <b>Geprüft wird, was die Seite aus der Zusage macht</b> — nicht, ob das Backend sie hält. Die Datei hat keinen
 * Test und überspringt sich nie.
 */

export type Json = Record<string, unknown>

/** Zusatz-Entfeuchter: Modus „fest" 26,5 / 25, Hilfsstärke normal (06.10.2026). */
export const EINSTELLUNGEN: Json = {
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
export const LIVE: Json = {
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

/**
 * Der Hauptentfeuchter (Form von `EntfeuchterEinstellungen`). Die Werte ergeben die Stufe „Normal"
 * (Abstand 4, Einschalten nach 2, Mindestlauf 20; Zusatz 15) — Tag und Nacht stehen verschieden (60 / 62),
 * AUS ist je EIN minus Abstand.
 */
export const HAUPT_EINSTELLUNGEN: Json = {
  vpdRegelung: true, hystereseProzent: 4, mindestlaufzeitMin: 20, einschaltverzoegerungMin: 2, wartezeitAussenluftMin: 25,
  tagbetriebErlauben: true, automatikAktiv: true,
  tempMaxTagModus: 'fest', tempMaxTagAbstandK: 6.5, tempMaxTagFestC: 26.5,
  tempMaxNachtModus: 'fest', tempMaxNachtAbstandK: 9, tempMaxNachtFestC: 25,
  feuchteEinTag: 60, feuchteAusTag: 56, feuchteEinNacht: 62, feuchteAusNacht: 58,
}
export const HAUPT_LIVE: Json = {
  haErreichbar: true, feuchteProzent: 55.2, tempC: 25.1, vpd: 1.31, tagPhase: true,
  einAktivProzent: 50, ausAktivProzent: 46, tempMaxAktivC: 26.5, rhObergrenzeProzent: 51, deckelProzent: 48,
  vpdUnten: 1.4, vpdOben: 1.4, blattOffsetC: -1, planWoche: 'Blütewoche 7', planLuftTagC: 20, planLuftNachtC: 16,
  tempMaxTagC: 26.5, tempMaxNachtC: 25, co2CanopyGrenzeC: 27,
  portAn: true, portOnline: true, automatikAn: true, zuluftVorrang: false,
}

/**
 * `GET /api/zielwerte`, wie der Überblick es liest (Form: `useZielwerteKurz.ts`): Luftfeuchte 45 – 54,
 * Lufttemperatur tags 18 – 26,5, nachts 14 – 25.
 */
export const ZIELWERTE: Json = {
  growName: 'Testgrow', phase: 'Blüte', woche: 'Blütewoche 7', programm: 'Testprogramm',
  werte: [
    { key: 'temperature', name: 'Lufttemperatur', quelle: 'Plan', quelleZusatz: 'Blütewoche 7 · ±4 K', regel: { quelle: 'Fest', min: 18, max: 26.5, nachtMin: 14, nachtMax: 25, toleranz: 4, aktiv: true } },
    { key: 'humidity', name: 'Luftfeuchte', quelle: 'Fest', quelleZusatz: 'von dir gesetzt', regel: { quelle: 'Fest', min: 45, max: 54, nachtMax: 60, toleranz: 0, aktiv: true } },
  ],
}

export const MODULE = {
  haErreichbar: true,
  standUtc: '2026-10-06T20:00:00Z',
  module: [
    { kennung: 'co2', titel: 'CO₂', status: 'an', kurz: 'Begasung', wert: '800 ppm', unterzeile: 'Ziel 900', hatDetail: true },
    { kennung: 'entfeuchter', titel: 'RDWC Dehumi', status: 'an', kurz: 'Entfeuchter', wert: '55 %', unterzeile: 'läuft', hatDetail: true },
    { kennung: 'entfeuchter-zusatz', titel: 'Dehumi RDWC Tent', status: 'an', kurz: 'Zusatz', wert: '313 W', unterzeile: 'läuft', hatDetail: true },
  ],
}

/**
 * echte Nutzer-Anlage vom 12.10.2026 (Proxy-GET auf die Fork-API; nur der Zeitpunkt ist angepasst):
 * Hauptentfeuchter Abstand 4, Einschalten nach 2, Mindestlauf 20, Außenluft 5, EIN 50 / AUS 46 Tag und Nacht;
 * Zusatz Mindestlauf 15, Zuschalten nach 2, Mindestpause 5, Hilfe „eigene". Nacht, Flush, 55,7 % rF bei 21 °C.
 */
export const BRU_HAUPT_EINSTELLUNGEN: Json = {
  vpdRegelung: false, hystereseProzent: 4, mindestlaufzeitMin: 20, einschaltverzoegerungMin: 2, wartezeitAussenluftMin: 5,
  tagbetriebErlauben: true, automatikAktiv: true,
  tempMaxTagModus: 'fest', tempMaxTagAbstandK: 5, tempMaxTagFestC: 23,
  tempMaxNachtModus: 'fest', tempMaxNachtAbstandK: 5, tempMaxNachtFestC: 23,
  feuchteEinTag: 50, feuchteAusTag: 46, feuchteEinNacht: 50, feuchteAusNacht: 46,
}
export const BRU_HAUPT_LIVE: Json = {
  haErreichbar: true, feuchteProzent: 55.7, tempC: 21, vpd: 0.95, tagPhase: false,
  einAktivProzent: 50, ausAktivProzent: 46, tempMaxAktivC: 23, rhObergrenzeProzent: 54, deckelProzent: 53,
  vpdUnten: 1.6, vpdOben: 1.6, blattOffsetC: -1, planWoche: 'Flush', planLuftTagC: 18, planLuftNachtC: 16,
  tempMaxTagC: 23, tempMaxNachtC: 23, co2CanopyGrenzeC: 23,
  portAn: true, portOnline: true, automatikAn: true, zuluftVorrang: false,
}
export const BRU_ZUSATZ_EINSTELLUNGEN: Json = {
  hilfe: 'eigene', automatikAktiv: true, tagbetriebErlauben: true, nachtDurchlaufen: true,
  vpdHystereseKpa: 0.15, zuschaltVerzoegerungMin: 2, folgeAbstandK: 1, wiederEinAbstandK: 1,
  mindestlaufzeitMin: 15, mindestpauseMin: 5,
  meldung: { aktiv: true, grenzeW: 60, dauerMin: 5, wiederholungH: 2 },
  ablauf: 'schlauch',
  tempMaxTagModus: 'fest', tempMaxTagAbstandK: 5, tempMaxTagFestC: 23,
  tempMaxNachtModus: 'fest', tempMaxNachtAbstandK: 5, tempMaxNachtFestC: 23,
}
export const BRU_ZUSATZ_LIVE: Json = {
  haErreichbar: true, fuehrungName: 'RDWC Dehumi', zusatzName: 'Dehumi RDWC Tent', planWoche: 'Flush',
  planLuftTagC: 18, planLuftNachtC: 16, tempMaxTagC: 23, tempMaxNachtC: 23,
  folgeAusTagC: 22, folgeAusNachtC: 22, wiederEinTagC: 21, wiederEinNachtC: 21,
  tempC: 21, feuchteProzent: 55.7, vpd: 0.95, tagPhase: false,
  schaltgroesse: 'feuchte', vpdZiel: null, vpdEinSchwelle: null, vpdAusSchwelle: null,
  feuchteEinProzent: 50, feuchteAusProzent: 46,
  zusatzAn: true, zusatzOnline: true, leistungW: 300, energieHeuteKwh: 2.82,
  fuehrungAn: true, ziehtNichts: false, planUnvollstaendig: false, automatikAn: true,
}
export const BRU_ZIELWERTE: Json = {
  growName: '2026-01', phase: 'Finish', woche: 'Flush', programm: 'SKX Canna Aqua',
  werte: [
    { key: 'temperature', name: 'Lufttemperatur', quelle: 'Plan', quelleZusatz: 'Flush · ±4 K', regel: { quelle: 'Fest', min: 14, max: 22, nachtMin: 12, nachtMax: 20, toleranz: 4, aktiv: true } },
    { key: 'humidity', name: 'Luftfeuchte', quelle: 'Fest', quelleZusatz: 'von dir gesetzt — der Plan lässt sie in Ruhe', regel: { quelle: 'Fest', min: 45, max: 54, nachtMax: 60, toleranz: 0, aktiv: true } },
  ],
}

export type Stand = {
  einstellungen: Json
  puts: Json[]
  haupt: Json
  hauptPuts: Json[]
  einrichtung: { zusatzVorhanden: boolean | null }
  einrichtungPuts: Json[]
  /** Wie oft die Seite `GET /api/zielwerte` gefragt hat — der Überblick liest es, die anderen Reiter nicht. */
  zielwerteAbfragen: number
}

export type Vorgaben = {
  /** Ganze Einstellungen des Hauptentfeuchters statt `HAUPT_EINSTELLUNGEN`. */
  haupt?: Json
  /** Ganze Einstellungen des Zusatzes statt `EINSTELLUNGEN`. */
  zusatz?: Json
  /** Antwort von `GET /api/zielwerte`; `null`: der Server antwortet mit 500. Ohne Angabe `ZIELWERTE`. */
  zielwerte?: Json | null
}

/**
 * Die Antworten vorgeben. Der Zusatz-`PUT` übernimmt nur die Felder aus dem Körper (Vertrag), der des
 * Hauptentfeuchters ersetzt alles — und gibt die Höchsttemperatur an den Zusatz weiter, wie das Backend es tut.
 */
export async function backendVorgeben(page: Page, live: Json = {}, hauptLive: Json = {}, vorgaben: Vorgaben = {}): Promise<Stand> {
  const stand: Stand = {
    einstellungen: structuredClone(vorgaben.zusatz ?? EINSTELLUNGEN), puts: [],
    haupt: structuredClone(vorgaben.haupt ?? HAUPT_EINSTELLUNGEN), hauptPuts: [],
    einrichtung: { zusatzVorhanden: null }, einrichtungPuts: [], zielwerteAbfragen: 0,
  }
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
  await page.route(/\/api\/zielwerte$/, async (route) => {
    stand.zielwerteAbfragen++
    if (vorgaben.zielwerte === null) return route.fulfill({ status: 500, json: { title: 'Server-Fehler' } })
    return route.fulfill({ json: vorgaben.zielwerte ?? ZIELWERTE })
  })
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

// ------------------------------------------------------------------ Bedienung (gemeinsam für beide Spezifikationen)

/** Die Seite öffnen und warten, bis ihre Überschrift steht. */
export async function seiteOeffnen(page: Page): Promise<void> {
  await page.goto('/steuerung/entfeuchtung', { waitUntil: 'networkidle' })
  await expect(page.getByRole('heading', { name: 'Entfeuchtung', level: 1 })).toBeVisible()
}

/** A-015: Reiter und Klappkacheln. Die Seite öffnet auf „Überblick". */
export const reiterWahl = (page: Page, name: string) => page.locator('.v1-tab', { hasText: new RegExp(`^${name}$`) }).click()
export const aufklappen = async (page: Page, titel: RegExp) => {
  const kopf = page.locator('.st-kk-kopf[aria-expanded="false"]', { hasText: titel })
  if (await kopf.count()) await kopf.first().click()
}
export const alleKartenOeffnen = async (page: Page) => {
  const zu = page.locator('.st-kk-kopf[aria-expanded="false"]')
  // Die Liste schrumpft mit jedem Klick — immer die erste nehmen.
  for (let i = 0; i < 30 && (await zu.count()) > 0; i++) await zu.first().click()
}
/** Auch die zugeklappten „Woher kommt das?"-Zeilen des Überblicks (sie stecken nicht in einer Klappkachel). */
export const alleOeffnen = async (page: Page) => {
  await alleKartenOeffnen(page)
  const zu = page.locator('.ef-hk-kopf[aria-expanded="false"]')
  for (let i = 0; i < 10 && (await zu.count()) > 0; i++) await zu.first().click()
}
export const speichern = (page: Page) => page.getByRole('button', { name: 'Speichern', exact: true })
/** Ein Schalter (`V1Switch`): sein Name trägt den Hinweistext mit, deshalb über die Beschriftung suchen. */
export const schalter = (page: Page, titel: string) => page.locator('label.v1-switch').filter({ has: page.locator('strong', { hasText: titel }) }).locator('input')

/** Was rechts über den Seitenrand ragt (ohne Inhalte in einem Wischbereich) — leer ist gut. */
export async function ragtHinaus(page: Page): Promise<string[]> {
  return page.evaluate(() => {
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
}

/** Die Zahlen unter den Bändern (Grenzen), die sich überdecken — Text messen, nicht den Kasten. Leer ist gut. */
export async function bandzahlenKollidieren(page: Page): Promise<string[]> {
  return page.evaluate(() => {
    const funde: string[] = []
    for (const reihe of document.querySelectorAll('.gos-metric-band .skala')) {
      const boxen = [...reihe.querySelectorAll('span')].map((s) => {
        const r = document.createRange(); r.selectNodeContents(s)
        const b = r.getBoundingClientRect()
        return { t: s.textContent ?? '', l: b.left, r: b.right, o: b.top, u: b.bottom }
      })
      // Zwei Zahlen stören sich nur, wenn sich ihre Textkästen waagerecht UND senkrecht überdecken.
      for (let i = 0; i < boxen.length; i++) for (let j = i + 1; j < boxen.length; j++) {
        const a = boxen[i], c = boxen[j]
        if (a.l < c.r - 0.5 && c.l < a.r - 0.5 && a.o < c.u - 0.5 && c.o < a.u - 0.5) funde.push(`${a.t} / ${c.t}`)
      }
    }
    return funde
  })
}
