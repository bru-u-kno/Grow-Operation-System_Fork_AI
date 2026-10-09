import { expect, test, type Page, type Request } from '@playwright/test'
import { backendAntwortet, darfUeberspringen } from './pflicht'
import { gibSchloss, nimmSchloss } from './schloss'

/**
 * Bedien- und Anzeigefehler aus der Durchsicht vom 02.10.2026 — jeder Fall
 * bedient die laufende App und liest, was auf dem Schirm steht.
 *
 * Wo der Demobestand den Fall nicht hergibt (ein HA-Zustand „heat_cool", eine
 * Messung von vor drei Tagen, ein abgelehnter Speicherversuch), wird die
 * ANTWORT des Backends abgefangen und verändert — die Seite selbst läuft
 * unverändert. So prüft der Test die Schicht, in der der Fehler entsteht: die
 * Darstellung.
 *
 * <b>Schreibt in die Datenbank</b> (CO₂-Hysterese, Ruhezeit) und stellt am
 * Ende den vorherigen Wert wieder her. Die Fälle berühren verschiedene Werte
 * und dürfen deshalb nebeneinander laufen — ein roter Fall soll die anderen
 * nicht verdecken.
 */

test.beforeEach(async ({ request }) => {
  darfUeberspringen(!(await backendAntwortet(request)), 'kein Backend unter GROW_OS_URL — diese Fälle bedienen die laufende App')
  // Schreibt in den geteilten Bestand (Licht-Stufe, Ruhezeit) — wie alle
  // schreibenden Dateien nur mit dem Schloss (`e2e-schloss-vollstaendig`).
  await nimmSchloss()
})

test.afterEach(() => { gibSchloss() })

/** Alle Anfragen einer Art mitschreiben. */
function mitschreiben(seite: Page, methode: string, muster: RegExp): Request[] {
  const liste: Request[] = []
  seite.on('request', (r) => { if (r.method() === methode && muster.test(r.url())) liste.push(r) })
  return liste
}

/** Ein Reiter der Seite (`V1Tabs` — Knöpfe in einer `tablist`, ohne eigene Rolle). */
function reiter(seite: Page, name: string) {
  return seite.locator('.v1-tab').filter({ hasText: new RegExp(`^${name}`) })
}

/** Die Antwort eines GET abfangen und verändern. */
// Die Seiten fragen ihre Daten wiederholt ab (Live z. B. alle paar Sekunden).
// Endet ein Test, während ein abgefangener Abruf noch in `route.fetch()`
// steckt, wirft Playwright „route.fetch: Test ended" — im Linux-Tor zweimal
// rot (CI #470, #472), lokal grün. Am Testende alle Umleitungen abbauen und
// noch laufende Bearbeiter nicht mehr als Fehler werten.
test.afterEach(async ({ page }) => {
  await page.unrouteAll({ behavior: 'ignoreErrors' })
})

async function antwortAendern(seite: Page, muster: RegExp, aendern: (daten: Record<string, unknown>) => void): Promise<void> {
  await seite.route(muster, async (route) => {
    if (route.request().method() !== 'GET') return route.fallback()
    const antwort = await route.fetch()
    const daten = await antwort.json() as Record<string, unknown>
    aendern(daten)
    await route.fulfill({ response: antwort, json: daten })
  })
}

test.describe('Licht: Leistungsstufe und ungespeicherter Zeitplan', () => {
  test('„10" tippen schickt einmal 10 — und „+" lässt den Zeitplan-Entwurf stehen', async ({ page, request }) => {
    // Ausgangslage: Stufe 5 — dann ist „10" garantiert eine Änderung. Am Ende
    // kommt die Stufe von vorher zurück.
    const vorherSeite = await (await request.get('/api/steuerung/licht')).json() as { einstellungen: { stufe: number } }
    const stufeVorher = vorherSeite.einstellungen.stufe
    const stufeSetzen = (stufe: number) => request.post('/api/steuerung/licht/befehl', { data: { art: 'stufe', preset: null, stufe } })
    await stufeSetzen(5)
    test.info().annotations.push({ type: 'Stufe vorher', description: String(stufeVorher) })
    try {
      await stufeTippen(page)
    } finally {
      await stufeSetzen(stufeVorher)
    }
  })

  async function stufeTippen(page: Page): Promise<void> {
    const befehle = mitschreiben(page, 'POST', /\/api\/steuerung\/licht\/befehl$/)
    await page.goto('/steuerung/licht')
    const feld = page.getByLabel('Leistungsstufe')
    await expect(feld).toBeVisible({ timeout: 15000 })

    // Ungespeicherte Änderung im Zeitplan.
    await reiter(page, 'Zeitplan').click()
    const veggieAn = page.getByLabel('Veggie · an')
    const vorher = await veggieAn.inputValue()
    const neu = vorher === '06:15' ? '06:45' : '06:15'
    await veggieAn.fill(neu)
    await expect(page.getByRole('button', { name: 'Speichern' })).toBeVisible()

    // Erster Durchgang: tippen und Enter.
    await reiter(page, 'Betrieb').click()
    await feld.click()
    await feld.press('ControlOrMeta+a')
    await feld.pressSequentially('10')
    expect(befehle, 'Beim Tippen ging schon ein Befehl hinaus — „1" vor „10".').toHaveLength(0)
    const erste = page.waitForResponse((r) => /\/licht\/befehl$/.test(r.url()))
    await feld.press('Enter')
    await erste
    expect(befehle.map((r) => r.postDataJSON().stufe)).toEqual([10])

    // Der Entwurf lebt noch.
    await expect(page.getByRole('button', { name: 'Speichern' })).toBeVisible()
    await reiter(page, 'Zeitplan').click()
    await expect(veggieAn).toHaveValue(neu)

    // Zweiter Durchgang, erschwert: weggescrollt, anderer Wert, verlassen per
    // Klick daneben statt Enter, danach „+".
    await reiter(page, 'Betrieb').click()
    await page.mouse.wheel(0, 2000)
    await feld.scrollIntoViewIfNeeded()
    await feld.click()
    await feld.press('ControlOrMeta+a')
    await feld.pressSequentially('3')
    const zweite = page.waitForResponse((r) => /\/licht\/befehl$/.test(r.url()))
    await page.getByRole('heading', { name: 'Licht LED Top' }).click()
    await zweite
    expect(befehle.map((r) => r.postDataJSON().stufe)).toEqual([10, 3])

    const dritte = page.waitForResponse((r) => /\/licht\/befehl$/.test(r.url()))
    await page.getByRole('button', { name: '+', exact: true }).click()
    await dritte
    expect(befehle).toHaveLength(3)
    await reiter(page, 'Zeitplan').click()
    await expect(veggieAn, 'Ein Befehl hat den ungespeicherten Zeitplan verworfen.').toHaveValue(neu)
    await expect(page.getByRole('button', { name: 'Speichern' })).toBeVisible()
  }

  test('ein unlesbarer Wert schickt nichts und wird markiert', async ({ page }) => {
    const befehle = mitschreiben(page, 'POST', /\/api\/steuerung\/licht\/befehl$/)
    await page.goto('/steuerung/licht')
    const feld = page.getByLabel('Leistungsstufe')
    await expect(feld).toBeVisible({ timeout: 15000 })
    await feld.fill('12')
    await feld.press('Enter')
    await expect(page.getByText('Bitte eine ganze Zahl von 1 bis 10 eintragen.')).toBeVisible()
    await expect(feld).toHaveAttribute('aria-invalid', 'true')
    expect(befehle).toHaveLength(0)
  })
})

test.describe('Steuerung: leeres Zahlenfeld', () => {
  test('CO₂: leer wird markiert statt „Es wurde nichts übergeben." — und ein Feldfehler des Backends markiert sein Feld', async ({ page }) => {
    const puts = mitschreiben(page, 'PUT', /\/api\/steuerung\/co2$/)
    await page.goto('/steuerung/co2')
    const hysterese = page.getByLabel('Hysterese')
    await expect(hysterese).toBeVisible({ timeout: 15000 })
    const alt = await hysterese.inputValue()

    await hysterese.fill('')
    await page.getByRole('button', { name: 'Speichern' }).click()
    await expect(page.getByText('Bitte die markierten Felder prüfen.')).toBeVisible()
    await expect(page.getByText('Bitte eine Zahl eintragen.')).toBeVisible()
    await expect(hysterese).toHaveAttribute('aria-invalid', 'true')
    await expect(page.getByText('Es wurde nichts übergeben.')).toHaveCount(0)
    expect(puts, 'Ein leeres Feld ging ans Backend.').toHaveLength(0)

    // Zweiter Versuch: das Feld wieder gefüllt, dafür eine Notbremse unter der
    // Obergrenze — das lehnt das Backend mit einem Feldfehler ab. Vorher suchte
    // die Seite den in `caught.fields` und markierte nie ein Feld.
    // Gespeichert wird dabei nichts.
    await hysterese.fill(alt)
    await reiter(page, 'Klima').click()
    const notbremse = page.getByLabel('Notbremse Feuchte', { exact: true })
    darfUeberspringen(await notbremse.count() === 0, 'Notbremse steht im Demobestand nicht auf „Fest"')
    await notbremse.fill('20')
    const abgelehnt = page.waitForResponse((r) => r.request().method() === 'PUT' && /\/steuerung\/co2$/.test(r.url()))
    await page.getByRole('button', { name: 'Speichern' }).click()
    expect((await abgelehnt).status()).toBe(400)
    await expect(notbremse).toHaveAttribute('aria-invalid', 'true')
    await expect(page.locator('.st-fehler')).toContainText('Notbremse')
    await expect(page.getByText('Bitte eine Zahl eintragen.')).toHaveCount(0)
  })

  test('Licht: leeres Feld unter „Erweitert" wird markiert, nichts geht hinaus', async ({ page }) => {
    const puts = mitschreiben(page, 'PUT', /\/api\/steuerung\/licht$/)
    await page.goto('/steuerung/licht')
    await reiter(page, 'Erweitert').click()
    const feld = page.getByLabel('Wiederholungen')
    await expect(feld).toBeVisible({ timeout: 15000 })
    await feld.fill('')
    await page.getByRole('button', { name: 'Speichern' }).click()
    await expect(page.getByText('Bitte eine Zahl eintragen.')).toBeVisible()
    await expect(feld).toHaveAttribute('aria-invalid', 'true')
    expect(puts).toHaveLength(0)
  })
})

test.describe('Geräte: ein Speicherfehler', () => {
  test('ersetzt nicht die Seite, und „steht jetzt bei" kommt nur nach Erfolg', async ({ page }) => {
    // Jeder schreibende Aufruf wird abgelehnt — so, wie das Backend ablehnt.
    await page.route(/\/api\/geraete\/.+/, async (route) => {
      if (route.request().method() === 'GET') return route.fallback()
      await route.fulfill({
        status: 400,
        json: { code: 'test_abgelehnt', message: 'Testfehler: abgelehnt.', status: 400, traceId: 'e2e', schemaVersion: 'grow-os.api-error.v1' },
      })
    })
    await page.goto('/geraete')
    await expect(page.getByRole('button', { name: 'Rubrik anlegen' })).toBeVisible({ timeout: 15000 })

    // Umbenennen am ersten Gerät.
    await page.getByRole('button', { name: /^Aktionen für / }).first().click()
    await page.getByRole('menuitem', { name: /Umbenennen/ }).click()
    const name = page.getByLabel(/^Name von /)
    await name.fill('Rundweg Fehlerfall')
    await page.locator('.gr-werkzeug').getByRole('button', { name: 'Speichern' }).click()
    await expect(page.getByText('Testfehler: abgelehnt.')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Rubrik anlegen' }), 'Der Fehler hat die Geräteliste ersetzt.').toBeVisible()

    // Zweiter Fall, erschwert: eine Entität weiter unten verschieben.
    await page.locator('.gr-werkzeug').getByRole('button', { name: 'Abbrechen' }).click()
    const zeilen = page.locator('.gr-kopf[aria-expanded="false"]')
    const anzahl = await zeilen.count()
    expect(anzahl, 'keine aufklappbare Gerätezeile').toBeGreaterThan(0)
    // forkai.195: Das Verschieben-Feld steht nicht mehr bei jedem Eintrag, sondern hinter dessen ⋯.
    const eintragMenue = page.locator('.gr-eintrag .gr-mehr')
    for (let i = anzahl - 1; i >= 0 && await eintragMenue.count() === 0; i--) {
      await zeilen.nth(i).click()
    }
    darfUeberspringen(await eintragMenue.count() === 0, 'kein Gerät mit Entität im Demobestand')
    await eintragMenue.first().click()
    await page.getByRole('button', { name: 'Verschieben nach' }).first().click()
    await page.locator('[role="option"][aria-selected="false"]').nth(1).click()
    await expect(page.getByText('Testfehler: abgelehnt.')).toBeVisible()
    await expect(page.getByText(/steht jetzt bei|steht wieder dort/)).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Rubrik anlegen' })).toBeVisible()
  })
})

test.describe('Wartung: Fälligkeit aus den erledigten Einträgen', () => {
  test('eine frisch kalibrierte Sonde ohne Einbaudatum hat eine Frist', async ({ page }) => {
    await page.goto('/geraete?reiter=wartung')
    const zeilen = page.locator('.gr-wartung')
    await expect(zeilen.first()).toBeVisible({ timeout: 15000 })
    // Im Demobestand: „Demo pH-Sonde" ohne Einbaudatum, vor drei Tagen
    // kalibriert — Folgetermin in elf Tagen. Vorher stand „ohne Frist".
    const demo = zeilen.filter({ hasText: 'Demo pH-Sonde' })
    expect(await demo.count(), 'Demo pH-Sonde fehlt im Wartungs-Reiter').toBeGreaterThan(0)
    for (const zeile of await demo.all()) {
      await expect(zeile.locator('.gr-frist')).toHaveText(/^in \d+ T$/)
    }
    // Die Bluelab-Sonde hat einen geplanten Termin vor fünf Tagen, nicht
    // „Einbau + 14 Tage" vor über hundert.
    const bluelab = zeilen.filter({ hasText: 'Bluelab pH-Sonde' }).locator('.gr-frist')
    await expect(bluelab).toHaveText(/^überfällig · \d T$/)
  })
})

test.describe('Ruhezeiten: Rundweg', () => {
  test('„22:00" und „7 Uhr" kommen an und stehen nach dem Neuladen da; Unlesbares wird gemeldet', async ({ page }) => {
    await page.goto('/handy')
    const von = page.getByLabel('Von (Uhr)')
    const bis = page.getByLabel('Bis (Uhr)')
    await expect(von).toBeVisible({ timeout: 15000 })
    const altVon = await von.inputValue()
    const altBis = await bis.inputValue()
    const speichern = page.getByRole('button', { name: 'Speichern', exact: true })

    await von.fill('22:00')
    await bis.fill('7 Uhr')
    const antwort = page.waitForResponse((r) => r.request().method() === 'PUT' && /\/api\/notifications\/settings$/.test(r.url()))
    await speichern.click()
    const fertig = await antwort
    expect(fertig.ok()).toBe(true)
    const rumpf = fertig.request().postDataJSON() as { quietHoursStartHour: number | null; quietHoursEndHour: number | null }
    expect(rumpf.quietHoursStartHour).toBe(22)
    expect(rumpf.quietHoursEndHour).toBe(7)
    await expect(page.getByText('Gespeichert.')).toBeVisible()
    await page.reload()
    await expect(von).toHaveValue('22', { timeout: 15000 })
    await expect(bis).toHaveValue('7')

    // Zweiter Durchgang: Unlesbares — nichts geht hinaus, die Ruhezeit bleibt.
    const puts = mitschreiben(page, 'PUT', /\/api\/notifications\/settings$/)
    await von.fill('abends')
    await speichern.click()
    await expect(page.getByText(/keine Uhrzeit/)).toBeVisible()
    await expect(von).toHaveAttribute('aria-invalid', 'true')
    await expect(page.getByText('Gespeichert.')).toHaveCount(0)
    expect(puts).toHaveLength(0)
    await page.reload()
    await expect(von).toHaveValue('22', { timeout: 15000 })

    // Aufräumen: den Stand von vorher zurück.
    await von.fill(altVon)
    await bis.fill(altBis)
    const zurueck = page.waitForResponse((r) => r.request().method() === 'PUT' && /\/api\/notifications\/settings$/.test(r.url()))
    await speichern.click()
    expect((await zurueck).ok()).toBe(true)
  })
})

test.describe('Anzeigen', () => {
  test('Live: eine Messung von vor drei Tagen trägt ihr Datum', async ({ page }) => {
    const vorDreiTagen = new Date(Date.now() - 3 * 86_400_000)
    vorDreiTagen.setHours(9, 30, 0, 0)
    await page.route(/\/api\/grows(\?.*)?$/, async (route) => {
      if (route.request().method() !== 'GET') return route.fallback()
      const antwort = await route.fetch()
      const daten = await antwort.json() as unknown
      const liste = (Array.isArray(daten) ? daten : (daten as { items?: unknown[] }).items ?? []) as Array<Record<string, unknown>>
      for (const grow of liste) if (grow.latestMeasurementAt) grow.latestMeasurementAt = vorDreiTagen.toISOString()
      await route.fulfill({ response: antwort, json: daten })
    })
    await page.goto('/')
    const kopf = page.locator('.ls-head-meta')
    await expect(kopf).toContainText('Letzte Messung', { timeout: 15000 })
    const tag = `${String(vorDreiTagen.getDate()).padStart(2, '0')}.${String(vorDreiTagen.getMonth() + 1).padStart(2, '0')}.`
    await expect(kopf).toContainText(`Letzte Messung ${tag} 09:30`)
  })

  test('Steuerung: die CO₂-Tagesliste zeigt keinen ISO-Tag', async ({ page }) => {
    await antwortAendern(page, /\/api\/steuerung\/co2$/, (d) => {
      d.tage = [{ datum: '2026-09-30', impulse: 4, ventilSekunden: 120, gramm: 12, zielErreichtUm: '09:12', abgeschlossen: true, imJournal: false, inKosten: false, flaschenwechsel: false }]
    })
    await page.goto('/steuerung/co2')
    await reiter(page, 'Heute').click()
    await expect(page.locator('.st-tage tbody th').first()).toHaveText('Mi 30.09.', { timeout: 15000 })
  })

  test('Steuerung: die Probeschaltung zeigt keinen rohen HA-Zustand', async ({ page }) => {
    await page.route(/\/api\/steuerung\/co2\/probe$/, (route) => route.fulfill({
      json: { urteil: 'Der Port hat geschaltet.', co2Vorher: '812.0', co2Nachher: 'unavailable' },
    }))
    await page.goto('/steuerung/co2')
    const knopf = page.getByRole('button', { name: 'Ventil kurz öffnen' })
    const sichtbar = await knopf.waitFor({ state: 'visible', timeout: 15000 }).then(() => true, () => false)
    darfUeberspringen(!sichtbar,'Probeschaltung nicht sichtbar — der Demobestand meldet keine fehlenden Bauteile')
    await knopf.click()
    const probe = page.getByText(/^Der Port hat geschaltet\./)
    await expect(probe).toBeVisible()
    await expect(probe).toContainText('CO₂-Fühler: nicht erreichbar.')
    await expect(probe).not.toContainText('unavailable')
  })

  test('Steuerung: der Kühlerzustand steht auf Deutsch da', async ({ page }) => {
    await antwortAendern(page, /\/api\/steuerung\/chiller$/, (d) => {
      const live = d.live as Record<string, unknown>
      live.ansteuerung = 'regelbar'
      live.kuehlerZustand = 'heat_cool'
      live.kuehlerEntity = 'climate.rundweg_kuehler'
    })
    await page.goto('/steuerung/chiller')
    await expect(page.getByText(/Gerät mit eigenem Thermostat/)).toContainText('heizt und kühlt', { timeout: 15000 })
    await expect(page.getByText('heat_cool')).toHaveCount(0)
  })

  test('Push: während des Ladens steht nicht „nicht erreichbar"', async ({ page }) => {
    let freigeben = () => {}
    const gesperrt = new Promise<void>((weiter) => { freigeben = weiter })
    await page.route(/\/api\/meldungen\/ha-waechter$/, async (route) => {
      await gesperrt
      await route.fallback()
    })
    await page.goto('/handy')
    await expect(page.getByText('Aus Home Assistant · nur Ansicht')).toBeVisible({ timeout: 15000 })
    await expect(page.getByText('Home Assistant ist gerade nicht erreichbar.')).toHaveCount(0)
    await expect(page.getByText('Wächter werden aus Home Assistant gelesen …')).toBeVisible()
    freigeben()
    await expect(page.getByText('Wächter werden aus Home Assistant gelesen …')).toHaveCount(0)
  })

  test('Plan-Auswertung: beim Wechsel des Grows bleibt „kein Plan" nicht hängen', async ({ page }) => {
    // Grow 2 hat im Demobestand keinen Plan, Grow 1 schon.
    await page.goto('/grows/2')
    await expect(page.getByText(/Für diesen Grow ist kein Plan gespeichert/)).toBeVisible({ timeout: 15000 })
    await page.evaluate(() => {
      window.history.pushState({}, '', '/grows/1')
      window.dispatchEvent(new PopStateEvent('popstate'))
    })
    await expect(page).toHaveURL(/\/grows\/1$/)
    await expect(page.getByRole('heading', { name: 'Plan · Auswertung' })).toBeVisible({ timeout: 15000 })
    await expect(page.getByText(/Für diesen Grow ist kein Plan gespeichert/)).toHaveCount(0)
  })

  test('Kosten: der Füllstand steht als Zahl da, der Preis bricht nicht um', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 800 })
    await page.goto('/kosten?tab=verbrauch')
    const karten = page.locator('[data-audit="kosten-artikel-karte"]')
    await expect(karten.first()).toBeVisible({ timeout: 15000 })
    // Vorher stand die Zahl nur im aria-label — sichtbar war allein der Balken.
    // Die Karte mit Balken — der Kosten-Rundweg legt Artikel ohne Füllstand an.
    const mitBalken = karten.filter({ has: page.locator('.ko-balken') })
    expect(await mitBalken.count(), 'keine Artikelkarte mit Füllstandsbalken im Bestand').toBeGreaterThanOrEqual(1)
    const stand = mitBalken.first().getByText(/^(geschätzt )?noch\s\d+\s%$/)
    await expect(stand).toBeVisible()
    // „39,00 € je 10 kg" auf einer Zeile: zwischen Betrag, „je" und Menge stehen
    // geschützte Leerzeichen. Mengenwächter: ohne Preis prüfte die Schleife nichts.
    const texte = await karten.locator('.ko-artikel-fakten').allTextContents()
    const preise = texte.flatMap((text) => [...text.matchAll(/\d+,\d{2}(\s)€(\s)je(\s)\S/g)])
    expect(preise.length, `kein Preis „… € je …" auf den Karten: ${texte.join(' | ')}`).toBeGreaterThanOrEqual(2)
    for (const treffer of preise) {
      expect(treffer[1] + treffer[2] + treffer[3], `normale Leerzeichen in „${treffer.input}"`).toBe('\u00a0\u00a0\u00a0')
    }
  })
})
