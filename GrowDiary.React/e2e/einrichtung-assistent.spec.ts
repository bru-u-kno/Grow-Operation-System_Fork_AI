import { test, expect, type Page } from '@playwright/test'
import { backendAntwortet, darfUeberspringen } from './pflicht'
import { gibSchloss, nimmSchloss } from './schloss'

/**
 * A-016 (Etappe 3): Der Einrichtungs-Assistent — drei Schritte.
 *
 * <b>Schritt 1 und 2</b> laufen gegen die echte Demo-App (Auswahl, Rollen-Zuordnung). <b>Schritt 3</b> bekommt
 * seinen Bestand und die Antworten der Anlege-Aufrufe vorgegeben (`page.route`): die Demo kann in Home Assistant
 * nichts anlegen, und geprüft wird, was der Assistent daraus macht — Reihenfolge der Aufrufe und die zwei
 * Zustimmungen (Automationen erst nach Vorschau und Bestätigung).
 *
 * Wie `steuerung-auswahl.spec.ts`: Die Auswahl gehört der ganzen App, deshalb das Schloss, und am Ende der
 * Anfangszustand (alle fünf Steuerungen gewählt).
 */

test.describe.configure({ mode: 'serial' })

const ALLE = ['co2', 'entfeuchter', 'zuluft', 'chiller', 'licht']

test.beforeEach(async ({ request }) => {
  darfUeberspringen(!(await backendAntwortet(request)), 'Kein Backend unter GROW_OS_URL — der Assistent braucht die laufende App mit Demobestand.')
  await nimmSchloss()
})
test.afterEach(() => { gibSchloss() })

test.afterAll(async ({ browser, baseURL }) => {
  // Auch das Zurückstellen gehört unter das Schloss — sonst reißt es eine Prüfung aus einer anderen Datei mittendrin um.
  await nimmSchloss()
  const page = await browser.newPage({ baseURL })
  if (await backendAntwortet(page.request)) await page.request.put('/api/steuerung/auswahl', { data: { gewaehlt: ALLE } })
  await page.close()
  gibSchloss()
})

async function auswahlSetzen(page: Page, gewaehlt: string[]) {
  const antwort = await page.request.put('/api/steuerung/auswahl', { data: { gewaehlt } })
  expect(antwort.ok(), await antwort.text()).toBeTruthy()
}

const schritte = (page: Page) => page.locator('ol.st-schritte li')

test('Schritt 1: ankreuzen wird sofort gespeichert; ohne Auswahl geht es nicht weiter — zweimal', async ({ page }) => {
  await auswahlSetzen(page, ['licht'])
  await page.goto('/steuerung/einrichtung')
  await expect(schritte(page)).toHaveCount(3)
  await expect(schritte(page).nth(0)).toHaveAttribute('aria-current', 'step')

  // Erster Durchgang: Wasserkühler an.
  const kuehler = page.locator('label.v1-switch').filter({ hasText: 'Wasserkühler' })
  await kuehler.locator('input').check()
  await expect(kuehler.locator('input')).toBeChecked()
  // Zweiter Durchgang, ohne Neuladen: Zuluft an, Lampe aus.
  await page.locator('label.v1-switch').filter({ hasText: 'Zuluft' }).locator('input').check()
  await page.locator('label.v1-switch').filter({ hasText: 'Lampe mit Zeitplan' }).locator('input').uncheck()

  // Der Schalter springt sofort, der Server wird der Reihe nach nachgeholt — also warten, bis er angekommen ist.
  await expect.poll(async () => {
    const stand = await (await page.request.get('/api/steuerung/auswahl')).json()
    return stand.eintraege.filter((e: { gewaehlt: boolean }) => e.gewaehlt).map((e: { kennung: string }) => e.kennung)
  }, { message: 'Alle drei Tipps müssen beim Server ankommen — auch die schnell hintereinander.' }).toEqual(['zuluft', 'chiller'])

  // Nichts gewählt: Weiter gesperrt, der Grund steht da.
  await kuehler.locator('input').uncheck()
  await page.locator('label.v1-switch').filter({ hasText: 'Zuluft' }).locator('input').uncheck()
  await expect(page.getByRole('button', { name: 'Weiter' })).toBeDisabled()
  await expect(page.getByText('Wähle mindestens eine Steuerung aus.')).toBeVisible()
})

test('Die Frage nach dem zweiten Entfeuchter erscheint nur beim Entfeuchter', async ({ page }) => {
  await auswahlSetzen(page, ['licht'])
  await page.goto('/steuerung/einrichtung')
  await expect(page.getByText('Zweiter Entfeuchter')).toHaveCount(0)
  await page.locator('label.v1-switch').filter({ hasText: /^Entfeuchter/ }).locator('input').check()
  await expect(page.getByText('Zweiter Entfeuchter')).toBeVisible()
  await page.locator('label.v1-switch').filter({ hasText: /^Entfeuchter/ }).locator('input').uncheck()
  await expect(page.getByText('Zweiter Entfeuchter')).toHaveCount(0)
})

test('Schritt 2 zeigt nur die gewählten Steuerungen — und der Schritt steht in der Adresse', async ({ page }) => {
  await auswahlSetzen(page, ['chiller', 'licht'])
  await page.goto('/steuerung/einrichtung')
  await page.getByRole('button', { name: 'Weiter' }).click()
  await expect(page).toHaveURL(/schritt=2/)
  await expect(schritte(page).nth(1)).toHaveAttribute('aria-current', 'step')

  const chips = page.locator('.st-wechsel [role="tab"]')
  await expect(chips).toHaveCount(2)
  await expect(chips.filter({ hasText: 'CO' })).toHaveCount(0)

  // Neuladen landet nicht wieder bei Schritt 1; Zurück im Browser geht einen Schritt zurück.
  await page.reload()
  await expect(schritte(page).nth(1)).toHaveAttribute('aria-current', 'step')
  await page.goBack()
  await expect(schritte(page).nth(0)).toHaveAttribute('aria-current', 'step')
})

test('Schritt 2: ungespeicherte Zuordnung sperrt Weiter, bis gespeichert ist', async ({ page }) => {
  await auswahlSetzen(page, ['chiller'])
  // Das Speichern wird vorgegeben (erst abgelehnt, dann angenommen): ein echtes Speichern ließe sich in der Demo
  // nicht zurücknehmen — Pflicht-Zuordnungen lassen sich nicht leeren — und würde andere Prüfungen verfälschen.
  const stand = await (await page.request.get('/api/steuerung/geraete')).json()
  let versuche = 0
  await page.route(/\/api\/steuerung\/geraete\/chiller$/, (route) => {
    versuche += 1
    return versuche === 1
      ? route.fulfill({ status: 400, json: { code: 'validation_failed', message: 'Eingaben konnten nicht validiert werden.', fieldErrors: { licht_zustand: ['Diese Rolle braucht ein Gerät.'] }, status: 400 } })
      : route.fulfill({ json: stand })
  })
  await page.goto('/steuerung/einrichtung?schritt=2')
  await expect(page.getByRole('button', { name: 'Weiter' })).toBeEnabled()

  // Eine Zeile allein lässt sich nicht speichern: der Kühler hat zwei Pflichtgeräte (Wasserfühler, Lampe).
  await page.getByRole('button', { name: 'Wasserfühler' }).click()
  await page.getByRole('option').nth(1).click()
  await expect(page.getByText('Du hast Änderungen noch nicht gespeichert')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Weiter' })).toBeDisabled()

  // Scheitert das Speichern (die zweite Pflicht-Zeile fehlt), bleibt Weiter gesperrt — die Eingabe ginge sonst verloren.
  await page.getByRole('button', { name: 'Speichern' }).click()
  await expect(page.getByText('Bitte die markierten Zeilen prüfen.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Weiter' })).toBeDisabled()

  await page.getByRole('button', { name: 'Lampe · Zustand' }).click()
  await page.getByRole('option').nth(1).click()
  await page.getByRole('button', { name: 'Speichern' }).click()
  await expect(page.getByText('Gespeichert.', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Weiter' })).toBeEnabled()
  await expect(page.getByText('Du hast Änderungen noch nicht gespeichert')).toHaveCount(0)
})

test('Ohne Auswahl springt Schritt 3 zurück auf Schritt 1', async ({ page }) => {
  await auswahlSetzen(page, [])
  await page.goto('/steuerung/einrichtung?schritt=3')
  await expect(page).toHaveURL(/schritt=1/)
})

// ---------------------------------------------------------------- Schritt 3

type Json = Record<string, unknown>
const bauteil = (art: string, stand: string, i: number): Json => ({ entityId: `x.${art}_${i}`, name: art, art, zweck: art, stand, pflicht: true, ohneDas: null })
function bestand(fehlend: Array<[string, number]>): Json {
  const bauteile = fehlend.flatMap(([art, n]) => Array.from({ length: n }, (_, i) => bauteil(art, 'Fehlt', i)))
  return { haErreichbar: true, eingerichtet: true, da: 0, fehlt: bauteile.length, entfaellt: 0, veraltet: 0, fehlendeRollen: [], ausgefalleneFunktionen: [], bauteile }
}

test('Schritt 3: erst Helfer und Rechenwerte, dann Vorschau — Automationen erst nach der zweiten Zustimmung', async ({ page }) => {
  await auswahlSetzen(page, ['entfeuchter', 'licht'])
  const aufrufe: string[] = []
  let helferDa = false
  await page.route(/\/api\/steuerung\/entfeuchter\/bestand$/, (route) => route.fulfill({
    json: helferDa ? bestand([['Automation', 1]]) : bestand([['Zahl', 3], ['RechenSensor', 2], ['Automation', 1]]),
  }))
  await page.route(/\/api\/steuerung\/entfeuchter\/(helfer|rechenwerte)$/, (route) => {
    const art = route.request().url().split('/').pop()
    aufrufe.push(`POST ${art}`)
    if (art === 'rechenwerte') helferDa = true
    return route.fulfill({ json: { erreichbar: true, angelegt: art === 'helfer' ? 3 : 2, uebersprungen: 0, fehlgeschlagen: 0, ohneGeraet: [], einzeln: [] } })
  })
  await page.route(/\/api\/steuerung\/entfeuchter\/automationen(\?vorschau=true)?$/, (route) => {
    const vorschau = route.request().url().includes('vorschau=true')
    aufrufe.push(vorschau ? 'POST automationen (Vorschau)' : 'POST automationen')
    return route.fulfill({ json: { erreichbar: true, angelegt: vorschau ? 1 : 1, fremd: 0, fehlgeschlagen: 0, einzeln: [
      { kennung: 'fork_ai_entfeuchter_regelung', name: 'regelung', titel: 'Entfeuchter Regelung Tag/Nacht', stand: 'Angelegt', hinweis: null },
    ] } })
  })

  await page.goto('/steuerung/einrichtung?schritt=3')
  const entf = page.locator('.st-feldzeile').filter({ hasText: 'Entfeuchter' })
  await expect(entf).toContainText('3 Helfer · 2 Rechenwerte · 1 Automation')
  await expect(entf).toContainText('bereit zum Anlegen')
  await expect(page.locator('.st-feldzeile').filter({ hasText: 'Lampe mit Zeitplan' })).toContainText('fertig')

  await page.getByRole('button', { name: 'Alles Gewählte bereitstellen' }).click()
  await expect(page.getByText('Diese Automationen würden geschrieben')).toBeVisible()
  await expect(page.getByText('Entfeuchter Regelung Tag/Nacht')).toBeVisible()
  // Bis hierher ist KEINE Automation geschrieben worden.
  expect(aufrufe).toEqual(['POST helfer', 'POST rechenwerte', 'POST automationen (Vorschau)'])

  await page.getByRole('button', { name: 'Automationen jetzt anlegen' }).click()
  await expect(page.getByText('1 Automation geschrieben')).toBeVisible()
  expect(aufrufe.at(-1)).toBe('POST automationen')
})

test('Schritt 3: „Nicht anlegen" lässt die Automationen ungeschrieben', async ({ page }) => {
  await auswahlSetzen(page, ['entfeuchter'])
  const aufrufe: string[] = []
  await page.route(/\/api\/steuerung\/entfeuchter\/bestand$/, (route) => route.fulfill({ json: bestand([['Automation', 1]]) }))
  await page.route(/\/api\/steuerung\/entfeuchter\/automationen(\?vorschau=true)?$/, (route) => {
    aufrufe.push(route.request().url().includes('vorschau=true') ? 'Vorschau' : 'geschrieben')
    return route.fulfill({ json: { erreichbar: true, angelegt: 1, fremd: 0, fehlgeschlagen: 0, einzeln: [
      { kennung: 'k', name: 'regelung', titel: 'Entfeuchter Regelung Tag/Nacht', stand: 'Angelegt', hinweis: null }] } })
  })
  await page.goto('/steuerung/einrichtung?schritt=3')
  await page.getByRole('button', { name: 'Alles Gewählte bereitstellen' }).click()
  await expect(page.getByText('Diese Automationen würden geschrieben')).toBeVisible()
  await page.getByRole('button', { name: 'Nicht anlegen' }).click()
  await expect(page.getByText('Diese Automationen würden geschrieben')).toHaveCount(0)
  expect(aufrufe).toEqual(['Vorschau'])
})

test('Schritt 3: eine Steuerung mit fehlenden Pflichtgeräten wird nicht angefasst', async ({ page }) => {
  await auswahlSetzen(page, ['chiller'])
  const aufrufe: string[] = []
  await page.route(/\/api\/steuerung\/chiller\/(helfer|rechenwerte|automationen)/, (route) => { aufrufe.push(route.request().url()); return route.abort() })
  await page.route(/\/api\/steuerung\/chiller\/bestand$/, (route) => route.fulfill({ json: bestand([['Zahl', 5]]) }))
  await page.goto('/steuerung/einrichtung?schritt=3')
  const zeile = page.locator('.st-feldzeile').filter({ hasText: 'Wasserkühler' })
  await expect(zeile).toContainText('Pflichtgerät fehlt')
  await expect(zeile).toContainText('Pflichtgeräten zugeordnet')
  await expect(page.getByRole('button', { name: 'Alles Gewählte bereitstellen' })).toBeDisabled()
  expect(aufrufe).toEqual([])
})

test('Die Übersicht führt zum Assistenten — auch wenn noch nichts gewählt ist', async ({ page }) => {
  await auswahlSetzen(page, ALLE)
  await page.goto('/steuerung')
  await page.getByRole('link', { name: 'Einrichtungs-Assistent öffnen' }).click()
  await expect(page).toHaveURL(/\/steuerung\/einrichtung$/)

  await auswahlSetzen(page, [])
  await page.goto('/steuerung')
  await expect(page.getByText('Noch keine Steuerung eingerichtet')).toBeVisible()
  await page.getByRole('link', { name: 'Einrichtung starten' }).click()
  await expect(page).toHaveURL(/\/steuerung\/einrichtung$/)
})

// ------------------------------------------------- Abhängigkeiten (Rest von Etappe 4)

const hinweise = (page: Page) => page.locator('[data-audit="abhaengigkeit"]')

test('Schritt 3: Entfeuchter ohne CO₂ bekommt einen Hinweis, mit CO₂ keinen — zweimal', async ({ page }) => {
  await page.route(/\/api\/steuerung\/entfeuchter\/bestand$/, (route) => route.fulfill({ json: bestand([['Zahl', 1]]) }))

  await auswahlSetzen(page, ['entfeuchter', 'licht'])
  await page.goto('/steuerung/einrichtung?schritt=3')
  await expect(page.locator('.st-feldzeile').filter({ hasText: 'Entfeuchter' }).first()).toBeVisible()
  await expect(hinweise(page)).toHaveCount(1)
  await expect(hinweise(page)).toContainText('Ohne die CO₂-Steuerung gibt es keine Feuchte-Obergrenze')

  // Zweiter Durchgang mit CO₂, ohne die Seite zu verlassen: neu laden genügt, der Stand kommt vom Server.
  await auswahlSetzen(page, ['co2', 'entfeuchter', 'licht'])
  await page.reload()
  await expect(page.locator('.st-feldzeile').filter({ hasText: 'Entfeuchter' }).first()).toBeVisible()
  await expect(hinweise(page)).toHaveCount(0)
})

test('Schritt 3: Zuluft-Rolle des Entfeuchters ohne gewählte Zuluft zeigt ins Leere — Warnung', async ({ page }) => {
  const geraete = await (await page.request.get('/api/steuerung/geraete')).json()
  for (const m of geraete.module) {
    if (m.modul !== 'entfeuchter') continue
    const z = m.zeilen.find((x: Json) => x.rolle === 'zuluft_bedarf')
    z.eingetragen = 'binary_sensor.zuluft_bedarf'
    z.gefunden = false
  }
  await page.route(/\/api\/steuerung\/geraete$/, (route) => route.fulfill({ json: geraete }))
  await page.route(/\/api\/steuerung\/entfeuchter\/bestand$/, (route) => route.fulfill({ json: bestand([['Zahl', 1]]) }))

  await auswahlSetzen(page, ['co2', 'entfeuchter'])
  await page.goto('/steuerung/einrichtung?schritt=3')
  const warn = hinweise(page).filter({ hasText: 'Zuluft · Bedarf' })
  await expect(warn).toHaveCount(1)
  await expect(warn).toContainText('binary_sensor.zuluft_bedarf')
  await expect(warn).toContainText('Wähle Zuluft oder leere die Rolle')

  // Mit gewählter Zuluft ist die Rolle in Ordnung — die Warnung entfällt.
  await auswahlSetzen(page, ['co2', 'entfeuchter', 'zuluft'])
  await page.reload()
  await expect(page.locator('.st-feldzeile').filter({ hasText: 'Zuluft' }).first()).toBeVisible()
  await expect(hinweise(page).filter({ hasText: 'Zuluft · Bedarf' })).toHaveCount(0)
})
