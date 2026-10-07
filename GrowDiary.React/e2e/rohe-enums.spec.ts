import { test, expect, type Page } from '@playwright/test'
import { readFileSync } from 'node:fs'
import { darfUeberspringen, kiAmServerAn } from './pflicht'
import { TEXTSEITEN } from './seiten'

/**
 * Kein englischer Enum-Wert steht roh auf dem Bildschirm.
 *
 * <b>Der Anlass.</b> Das wichtigste Formular der App — „Grow starten" — bot
 * am 24.08.2026 an: Samen-Typ <i>Feminized / Autoflower / Regular</i>,
 * Startmaterial <i>Seed / Clone</i>, Startpunkt <i>Germination / Seedling /
 * Veg / Flower / Flush</i>, Status <i>Planning / Running / Completed /
 * Aborted</i>. Dazu <i>Grid2x2</i>, <i>Production</i> und <i>External</i> auf
 * den Zelt- und Hydro-Karten. Insgesamt 29 rohe Werte über die Seiten verteilt.
 *
 * <b>Warum der vorhandene Test das nicht gesehen hat.</b>
 * <c>deutsche-woerter.node.test.ts</c> prüft, dass die <i>Tabelle</i>
 * vollständig ist — nicht, dass eine Seite sie <i>benutzt</i>. Genau die
 * Unterscheidung aus CLAUDE.md: eine Erwähnung ist keine Verwendung. Eine
 * Seite, die den Wert direkt ausgibt, kommt an der Tabelle vorbei, und der
 * Test bleibt grün.
 *
 * <b>Die Grundmenge</b> sind die Werte der String-Unions aus
 * <c>src/types/shared.ts</c> und <c>src/types/production.ts</c> — also die
 * Wahrheit aus dem Typ, keine abgetippte Liste.
 */

/**
 * Wörter, die auf Deutsch genau so heißen — mit Grund.
 *
 * <b>Kein Freibrief:</b> hier steht nur, was ein deutscher Text ohnehin
 * enthielte. Wer einen Wert hier einträgt, weil eine Seite ihn roh ausgibt,
 * hat den Test abgeschaltet statt den Fehler behoben.
 */
const ERLAUBT: Record<string, string> = {
  Do: 'Wochentagskürzel „Do“ (Donnerstag) auf der Live-Seite — gleich wie der Enum-Wert für gelösten Sauerstoff; fiel erst am Donnerstag 08.10.2026 auf',
  RDWC: 'Fachbegriff, auf Deutsch genauso — Recirculating Deep Water Culture',
  DWC: 'dasselbe für Deep Water Culture',
  Autoflower: 'die deutsche Sortenbezeichnung ist genau dieses Wort',
  Finish: 'in PHASEN_NAMEN bewusst „Finish" — im Grow-Deutsch etabliert',
  Problem: 'in FOTO_NAMEN bewusst „Problem" — ist auch ein deutsches Wort',
  Training: 'in FOTO_NAMEN bewusst „Training" — LST/HST heissen hier so',
  Offline: 'deutsches Lehnwort; der Knopf „Offline" ist die Handlung',
  System: 'deutsches Wort; steht in normalen Saetzen wie „im selben System“',
  Top: 'steht in Geraetenamen wie „LED Top", nicht als Tankposition',
  Root: 'steht im Namen des Krankheitsbildes „Root Rot" im Wissen',
  Flush: 'steht im Titel des Ablaufs „Erntevorbereitung — Flush"',
  Flower: 'steht in SOP- und Programmnamen („Flower Fuel"), nicht als Phase',
  Indica: 'die deutsche Bezeichnung fuer diese Genetik ist genau dieses Wort',
  Sativa: 'dasselbe',
  Hybrid: 'dasselbe — ein deutsches Fremdwort, keine Uebersetzungsluecke',
  Normal: 'die Aufgaben-Stufe heisst auf Deutsch genauso (JournalStreamSection)',
  High: 'die Wirkung heisst auf Deutsch so — Feldbeschriftung „Effekt / High“ auf der Ernteseite',
  // Der Phasenanker (02.10.2026) führt deutsche Kennungen; drei davon sind
  // gewöhnliche Wörter, die auch anderswo stehen.
  Anzucht: 'Ankerphase — und der Zeltzweck „Anzucht“ heißt genauso',
  Keimung: 'Anzuchtart — das deutsche Wort selbst',
  // „Bewurzelung" stand hier bis 02.10.2026 als „auch Spaltenname im Plan" —
  // belegt nur durch die Plan-Spalte des Samen-Grows, und dort war das Wort
  // fachlich falsch. Seitdem heißt die Anzucht beim Samen „Anzucht"; das Wort
  // gehört dem Steckling, und der Demobestand hat keinen. Wer einen Steckling
  // in den Bestand bringt, trägt die Ausnahme mit diesem Grund wieder ein.
  Ende: 'Ankerphase — deutsches Wort, steht in normalen Sätzen („Ende der Blüte“)',
  // Zugriff für KI-Assistenten (A-003): zwei der vier Stufen heißen über die
  // Leitung schon so, wie sie auf dem Schirm stehen. „GrowPlanen" und
  // „GeraeteSchalten" stehen NICHT hier — die dürfen nie roh erscheinen.
  Dokumentieren: 'KiStufe — das deutsche Wort selbst, so steht es auch am Häkchen',
  Verwaltung: 'KiStufe — das deutsche Wort selbst, so steht es auch am Häkchen',
}


/** Die Werte aller String-Unions aus den Typdateien. */
function enumWerte(): Map<string, string> {
  const werte = new Map<string, string>()

  for (const datei of ['../src/types/shared.ts', '../src/types/production.ts']) {
    const quelltext = readFileSync(new URL(datei, import.meta.url), 'utf8')

    for (const zeile of quelltext.split(/\r?\n/)) {
      const treffer = zeile.match(/export type (\w+) =(.*)$/)
      if (!treffer) continue

      for (const wert of treffer[2].matchAll(/'([^']+)'/g)) {
        if (!werte.has(wert[1])) werte.set(wert[1], treffer[1])
      }
    }
  }

  return werte
}

const WERTE = enumWerte()

test('die Grundmenge ist da', () => {
  // Ohne sie liefe jede Seitenpruefung null Mal durch und waere gruen.
  expect(WERTE.size).toBeGreaterThan(80)
  expect(WERTE.get('Feminized')).toBe('SeedType')
  expect(WERTE.get('Grid2x2')).toBe('HydroSetupLayoutType')

  // Und die Ausnahmeliste nennt nur Werte, die es wirklich gibt — sonst
  // schuetzt sie etwas, das niemand je sieht.
  const erfunden = Object.keys(ERLAUBT).filter((wert) => !WERTE.has(wert))
  expect(erfunden, `Diese Ausnahmen sind keine Enum-Werte: ${erfunden.join(', ')}`).toEqual([])
})

async function roheWerte(page: Page): Promise<string[]> {
  const text = await page.evaluate(() =>
    (document.querySelector('main') as HTMLElement | null)?.innerText || '')

  const gefunden: string[] = []
  for (const [wert, typ] of WERTE) {
    if (wert in ERLAUBT) continue
    if (!new RegExp(`(?<![\\w-])${wert}(?![\\w-])`).test(text)) continue
    gefunden.push(`${wert} (${typ})`)
  }

  return gefunden
}

for (const pfad of TEXTSEITEN) {
  test(`${pfad} — kein englischer Enum-Wert`, async ({ page }) => {
    const antwort = await page.goto(pfad, { waitUntil: 'networkidle' })
    darfUeberspringen(
      antwort == null || antwort.status() >= 400,
      `${pfad} antwortet nicht — laeuft die App unter GROW_OS_URL?`,
    )
    await page.waitForTimeout(400)

    const laenge = await page.evaluate(() =>
      ((document.querySelector('main') as HTMLElement | null)?.innerText || '').trim().length)
    darfUeberspringen(laenge < 200, `${pfad} zeigt fast nichts — vermutlich ein Ladezustand`)

    const roh = await roheWerte(page)
    expect(
      roh,
      `Diese Entwickler-Bezeichner stehen roh auf ${pfad}: ${roh.join(', ')}\n`
      + 'Deutsches Wort in src/deutsche-woerter.ts eintragen und die Seite darauf ziehen.',
    ).toEqual([])
  })
}

/**
 * Formulare, die erst nach einem Klick da sind.
 *
 * <b>Der blinde Fleck.</b> Die Prüfung oben sieht nur den Ruhezustand einer
 * Seite. Auf „Sensoren &amp; Wartung" standen im Bearbeiten-Formular
 * <i>Active</i>, <i>MaintenanceDue</i> und <i>Retired</i> roh — eine Zeile
 * unter einem Feld, das ordentlich übersetzt. Im Journal zeigte die Auswahl
 * „Art" alle neun Foto-Tags englisch. Beides blieb grün, weil niemand geklickt
 * hat.
 */
const HINTER_EINEM_KLICK: Array<{ pfad: string, knopf: string, was: string }> = [
  { pfad: '/sensoren', knopf: 'Bearbeiten', was: 'das Geräte-Formular' },
  { pfad: '/journal', knopf: '+ Eintrag', was: 'das Journal-Formular' },
  { pfad: '/sorten', knopf: '+ Sorte', was: 'das Sorten-Formular' },
  { pfad: '/zelte', knopf: 'Bearbeiten', was: 'das Zelt-Formular' },
  // A-003: die Stufen-Häkchen eines neuen Schlüssels stehen erst nach dem Klick da. Seit 04.10.2026 auf der Seite KI-Assistent.
  { pfad: '/ki?tab=zugriff', knopf: 'Neuer Schlüssel', was: 'das Schlüssel-Formular' },
]

/**
 * Ein Formular aufklappen — und WARTEN, bis es wirklich da ist.
 *
 * Eine feste Wartezeit reichte auf dem Entwicklungsrechner und auf dem
 * Bauserver nicht: dort klappte das Journal-Formular spaeter auf, die
 * Pruefung las den alten Text und meldete eine Ausnahme als tot, die es
 * sehr wohl gibt. Gewartet wird deshalb darauf, dass der Text WAECHST.
 */
async function aufklappen(page: Page, knopfName: string): Promise<boolean> {
  const laenge = async () => page.evaluate(() =>
    ((document.querySelector('main') as HTMLElement | null)?.innerText || '').length)

  const vorher = await laenge()
  const knopf = page.getByRole('button', { name: knopfName, exact: false }).first()
  if (await knopf.count() === 0) return false

  await knopf.click()

  for (let versuch = 0; versuch < 20; versuch++) {
    await page.waitForTimeout(150)
    if (await laenge() > vorher) return true
  }

  return false
}

for (const fall of HINTER_EINEM_KLICK) {
  test(`${fall.pfad} — ${fall.was} spricht Deutsch`, async ({ page }) => {
    // Fork AI (A-011): die KI-Seiten gibt es nur bei KI an; ohne Backend bleibt das ein Übersprung wie bisher.
    if (fall.pfad.startsWith('/ki')) await kiAmServerAn(page.request).catch(() => undefined)
    const antwort = await page.goto(fall.pfad, { waitUntil: 'networkidle' })
    darfUeberspringen(
      antwort == null || antwort.status() >= 400,
      `${fall.pfad} antwortet nicht — laeuft die App unter GROW_OS_URL?`,
    )

    const offen = await aufklappen(page, fall.knopf)
    darfUeberspringen(
      !offen,
      `Auf ${fall.pfad} liess sich ${fall.was} nicht aufklappen — dann prueft dieser Fall nichts.`,
    )

    const roh = await roheWerte(page)
    expect(
      roh,
      `Nach dem Klick auf „${fall.knopf}" stehen auf ${fall.pfad} rohe Bezeichner: ${roh.join(', ')}`,
    ).toEqual([])
  })
}

/**
 * Jede Ausnahme muss irgendwo wirklich vorkommen.
 *
 * <b>Sonst ist ihr Grund eine Erfindung.</b> Beim ersten Anlauf standen fünf
 * Wörter in der Liste, die auf keiner einzigen Seite auftauchen — mit Gründen
 * wie „steht im Titel des Ablaufs …". Zwei davon waren Werte, die derselbe
 * Commit als behoben meldete: die Ausnahme hätte sie nie wieder finden können.
 */
test('jede Ausnahme kommt wirklich vor', async ({ page }) => {
  // Ein Fall, der 31 Seiten besucht und vier Formulare aufklappt, passt
  // nicht in die Standardzeit von 30 s — im Tor lief er genau deshalb in
  // eine Zeitueberschreitung.
  test.setTimeout(180_000)

  // OHNE BACKEND zeigen alle Seiten nur ihre Huelle: kein Formular klappt
  // auf, kein Wort steht irgendwo, und JEDE Ausnahme gaelte als tot. Im Tor
  // ist genau das passiert — der Rauchtest ohne Backend meldete acht tote
  // Ausnahmen, die es alle gibt. Eine Zaehlung ohne Grundmenge zaehlt nicht,
  // sie raet.
  //
  // Gefragt wird die API und NICHT die Textlaenge: eine Seite ohne Daten
  // rendert trotzdem Ueberschrift, Menue und Leertext — oft ueber 200
  // Zeichen. Der erste Anlauf hat sich genau daran vertan.
  darfUeberspringen(
    !(await page.request.get('/api/grows')).ok(),
    'Kein Backend erreichbar — ohne Daten prueft diese Zaehlung nichts.',
  )

  const gesehen = new Set<string>()
  let geoeffnet = 0

  async function mitlesen() {
    const text = await page.evaluate(() =>
      (document.querySelector('main') as HTMLElement | null)?.innerText || '')

    for (const wert of Object.keys(ERLAUBT)) {
      // ZWEI Schraegstriche: im Template-String wird `\w` sonst zu einem
      // einfachen w, und aus der Wortgrenze wird die Zeichenklasse [w-].
      if (new RegExp(`(?<![\\w-])${wert}(?![\\w-])`).test(text)) gesehen.add(wert)
    }
  }

  for (const pfad of TEXTSEITEN) {
    const antwort = await page.goto(pfad, { waitUntil: 'networkidle' })
    if (antwort == null || antwort.status() >= 400) continue
    await page.waitForTimeout(200)


    await mitlesen()
  }


  // Auch hinter die Knoepfe sehen: sonst gilt eine Ausnahme als tot, nur weil
  // sie in einem Formular steht, das erst ein Klick oeffnet. Genau so sind
  // „Indica", „Sativa" und „Hybrid" beim ersten Anlauf durchgefallen.
  for (const fall of HINTER_EINEM_KLICK) {
    const antwort = await page.goto(fall.pfad, { waitUntil: 'networkidle' })
    if (antwort == null || antwort.status() >= 400) continue

    if (!await aufklappen(page, fall.knopf)) continue
    geoeffnet++
    await mitlesen()
  }

  // Mengenwaechter: ohne Treffer haette die Schleife nichts gelesen — und
  // ohne aufgeklappte Formulare gilt jede Ausnahme aus einem Formular
  // faelschlich als tot. Genau daran ist der erste Lauf im Tor gescheitert.
  expect(gesehen.size).toBeGreaterThan(3)
  expect(
    geoeffnet,
    `Nur ${geoeffnet} von ${HINTER_EINEM_KLICK.length} Formularen liessen sich aufklappen.`,
  ).toBe(HINTER_EINEM_KLICK.length)

  const tot = Object.keys(ERLAUBT).filter((wert) => !gesehen.has(wert))
  expect(
    tot,
    `Diese Ausnahmen kommen auf keiner Seite vor — ihr Grund ist damit unbelegt: ${tot.join(', ')}
`
    + 'Wer sie behaelt, schuetzt etwas, das es nicht gibt. Raus damit.',
  ).toEqual([])
})
