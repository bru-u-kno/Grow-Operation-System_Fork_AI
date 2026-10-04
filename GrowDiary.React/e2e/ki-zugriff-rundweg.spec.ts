import { expect, test, type Locator, type Page } from '@playwright/test'
import { backendAntwortet, darfUeberspringen } from './pflicht'
import { gibSchloss, nimmSchloss } from './schloss'

/**
 * Zugriff für KI-Assistenten (A-003): die drei Formulare des Abschnitts auf der
 * Seite KI-Assistent (Reiter „Zugriff & Schlüssel“, bis forkai.164 in den
 * Einstellungen) werden ausgefüllt, abgeschickt und nach dem Neuladen
 * wiedergefunden.
 *
 * <b>Was hier ein Rundweg heißt</b> (dieselbe Regel wie in
 * `formular-rundweg.spec.ts`): ausfüllen, den ausgehenden Aufruf abfangen und
 * im Rumpf nachsehen, dass die getippten Werte wirklich drinstehen, auf die
 * Antwort warten, neu laden und prüfen, dass der Wert noch da ist.
 *
 * <b>Dazu zwei Regeln, die nur dieser Abschnitt hat:</b>
 * <ul>
 *   <li>Je Stufe drei Zustände: Gesperrt · Mit Rückfrage · Frei (A-005).
 *       „Geräte schalten" und „Verwaltung" verlassen Gesperrt erst nach dem
 *       Warnhinweis — „Gesperrt lassen" lässt sie gesperrt.</li>
 *   <li>Der Klartext steht genau einmal da: nach dem Neuladen ist er weg, und
 *       die Liste zeigt nur seinen Anfang.</li>
 * </ul>
 *
 * <b>Diese Datei schreibt.</b> Sie stellt den Hauptschalter und die
 * Höchstwerte am Ende auf den Stand von vorher zurück und löscht den
 * Schlüssel, den sie angelegt hat (über die Oberfläche — auch das ist ein
 * Weg, der geprüft gehört). Das Schloss hält sie, damit kein paralleler Lauf
 * zwischen Speichern und Nachlesen schreibt.
 */

test.describe.configure({ mode: 'serial' })
test.beforeEach(async () => { await nimmSchloss() })
test.afterEach(() => { gibSchloss() })

const SEITE = '/ki?tab=zugriff'
const WEG = '/api/settings/ki-zugriff'

/** Ein Wert, der in diesem Lauf einmalig ist — sonst prüft der zweite Lauf den ersten. */
function marke(): string {
  return `Rundweg ${new Date().toISOString().slice(11, 19)}`
}

/** Auf die ANTWORT warten und den Rumpf der Anfrage zurückgeben — Begründung in `formular-rundweg.spec.ts`. */
async function abgeschickt(
  seite: Page,
  methode: 'POST' | 'PUT' | 'DELETE',
  muster: RegExp,
  handlung: () => Promise<void>,
): Promise<Record<string, unknown>> {
  const antwort = seite.waitForResponse((r) => r.request().method() === methode && muster.test(new URL(r.url()).pathname))
  await handlung()
  const fertig = await antwort
  expect(fertig.ok(), `${methode} ${fertig.url()} kam mit HTTP ${fertig.status()} zurück.`).toBe(true)
  return JSON.parse(fertig.request().postData() || '{}') as Record<string, unknown>
}

async function abschnitt(seite: Page): Promise<Locator> {
  await seite.goto(SEITE)
  const bereich = seite.locator('[data-audit="settings-ki-zugriff"]')
  await expect(bereich.locator('[data-audit="ki-zugriff-form"]')).toBeVisible({ timeout: 15000 })
  return bereich
}

/**
 * Fork AI (A-005, 03.10.2026): Ein Zustand im Umschalter einer Stufe — über
 * die Radiogruppe, die der sichtbare Name der Stufe beschriftet, und den
 * sichtbaren Wortlaut des Zustands. Nie über die Bezeichner.
 */
function wahl(formular: Locator, stufe: string, zustand: 'Gesperrt' | 'Mit Rückfrage' | 'Frei'): Locator {
  return formular.getByRole('radiogroup', { name: stufe, exact: true }).getByRole('radio', { name: zustand, exact: true })
}

test.describe('Zugriff für KI-Assistenten', () => {
  let vorher: Record<string, unknown> | null = null

  test.beforeEach(async ({ page }) => {
    darfUeberspringen(!(await backendAntwortet(page.request)), 'Kein Backend erreichbar.')
    const antwort = await page.request.get(WEG)
    darfUeberspringen(!antwort.ok(), `${WEG} antwortet mit HTTP ${antwort.status()} — ist das Backend von A-003 eingespielt?`)
    vorher ??= await antwort.json() as Record<string, unknown>
  })

  test.afterEach(async ({ page }) => {
    if (!vorher) return
    // Den Stand von vorher zurückschreiben — der Hauptschalter soll nach dem
    // Lauf nicht an bleiben. Je Fall und noch unter dem Schloss (innere
    // afterEach laufen vor der äusseren, die es zurückgibt).
    await page.request.put(WEG, {
      data: { aktiv: vorher.aktiv, hoechstwerte: vorher.hoechstwerte },
    })
  })

  test('Rundweg: KiZugriffAbschnitt — Einstellungen speichern, neu laden, nochmal speichern', async ({ page }) => {
    let bereich = await abschnitt(page)
    let formular = bereich.locator('[data-audit="ki-zugriff-form"]')

    // Fork AI (A-005): die globale Rückfrage-Auswahl gibt es nicht mehr — die Rückfrage steht je Schlüssel.
    await expect(formular.getByLabel('Vorher nachfragen')).toHaveCount(0)
    await formular.locator('label.v1-switch input[type="checkbox"]').check()
    await formular.getByLabel('Höchstens ml je Dosierbefehl').fill('7,5')
    await formular.getByLabel('Höchstens Befehle je Stunde').fill('13')

    const rumpf = await abgeschickt(page, 'PUT', /\/api\/settings\/ki-zugriff$/, async () => {
      await formular.locator('[data-audit="ki-zugriff-speichern"]').click()
    })
    expect(rumpf.aktiv).toBe(true)
    expect(rumpf).not.toHaveProperty('rueckfrageAbStufe')
    // Deutsches Komma: „7,5" muss als 7.5 ankommen und nicht als 75 oder leer.
    expect(rumpf.hoechstwerte).toEqual({ maxDosisMlJeBefehl: 7.5, maxSchaltbefehleJeStunde: 13 })

    // Neu laden: erst das beweist, dass gespeichert wurde und nicht nur angezeigt.
    bereich = await abschnitt(page)
    formular = bereich.locator('[data-audit="ki-zugriff-form"]')
    await expect(formular.locator('label.v1-switch input[type="checkbox"]')).toBeChecked()
    await expect(formular.getByLabel('Höchstens ml je Dosierbefehl')).toHaveValue('7,5')
    await expect(formular.getByLabel('Höchstens Befehle je Stunde')).toHaveValue('13')

    // Zweites Speichern ohne Neuladen dazwischen.
    await formular.getByLabel('Höchstens ml je Dosierbefehl').fill('2,5')
    const zweiter = await abgeschickt(page, 'PUT', /\/api\/settings\/ki-zugriff$/, async () => {
      await formular.locator('[data-audit="ki-zugriff-speichern"]').click()
    })
    expect(zweiter.hoechstwerte).toEqual({ maxDosisMlJeBefehl: 2.5, maxSchaltbefehleJeStunde: 13 })

    bereich = await abschnitt(page)
    formular = bereich.locator('[data-audit="ki-zugriff-form"]')
    await expect(formular.getByLabel('Höchstens ml je Dosierbefehl')).toHaveValue('2,5')
    // Kein „ab" im Kopf des Abschnitts.
    expect(await formular.innerText()).not.toMatch(/\bab\b/)
  })

  test('Rundweg: KiZugriffAbschnitt — Schlüssel anlegen, Klartext einmal, Stufen ändern, sperren, löschen', async ({ page }) => {
    const name = `KI ${marke()}`
    let bereich = await abschnitt(page)

    // --- ki-schluessel-form ---
    await bereich.locator('[data-audit="ki-schluessel-neu"]').click()
    const neu = bereich.locator('[data-audit="ki-schluessel-form"]')
    await expect(neu).toBeVisible()
    await expect(wahl(neu, 'Dokumentieren', 'Frei'), 'Vorbelegung: Dokumentieren frei').toBeChecked()
    await expect(wahl(neu, 'Grow planen', 'Gesperrt')).toBeChecked()
    await expect(wahl(neu, 'Verwaltung', 'Gesperrt')).toBeChecked()

    await neu.getByLabel('Name des Schlüssels').fill(name)
    await wahl(neu, 'Grow planen', 'Mit Rückfrage').check()

    // Warnhinweis: ohne Bestätigung bleibt die Stufe gesperrt.
    await wahl(neu, 'Geräte schalten', 'Frei').click()
    await expect(neu.locator('[data-audit="ki-stufen-warnung"]')).toBeVisible()
    await expect(wahl(neu, 'Geräte schalten', 'Gesperrt')).toBeChecked()
    await neu.locator('[data-audit="ki-warnung-ablehnen"]').click()
    await expect(neu.locator('[data-audit="ki-stufen-warnung"]')).toHaveCount(0)
    await expect(wahl(neu, 'Geräte schalten', 'Gesperrt')).toBeChecked()
    // Zweiter Anlauf, diesmal freigeben.
    await wahl(neu, 'Geräte schalten', 'Frei').click()
    await neu.locator('[data-audit="ki-warnung-bestaetigen"]').click()
    await expect(wahl(neu, 'Geräte schalten', 'Frei')).toBeChecked()

    const rumpf = await abgeschickt(page, 'POST', /\/api\/settings\/ki-zugriff\/schluessel$/, async () => {
      await neu.locator('[data-audit="ki-schluessel-anlegen"]').click()
    })
    expect(rumpf.name).toBe(name)
    expect(rumpf.stufen, 'Die Stufen gehen als Namen über die Leitung.').toEqual(['Dokumentieren', 'GrowPlanen', 'GeraeteSchalten'])
    expect(rumpf.rueckfrageBei).toEqual(['GrowPlanen'])

    // Der Klartext — einmal, gut lesbar, mit dem Satz.
    const anzeige = bereich.locator('[data-audit="ki-klartext"]')
    await expect(anzeige).toBeVisible()
    await expect(anzeige).toContainText('Wird nur jetzt angezeigt — danach nicht mehr.')
    const klartext = (await anzeige.locator('[data-audit="ki-klartext-wert"]').innerText()).trim()
    expect(klartext).toMatch(/^gok_[A-Za-z0-9_-]{43}$/)

    // Neu laden: der Klartext ist weg, die Zeile zeigt nur den Anfang.
    bereich = await abschnitt(page)
    await expect(bereich.locator('[data-audit="ki-klartext"]')).toHaveCount(0)
    expect(await page.locator('body').innerText(), 'Der Klartext steht nach dem Neuladen noch auf der Seite.')
      .not.toContain(klartext)
    let zeile = bereich.locator('[data-audit="ki-schluessel"]', { hasText: name })
    await expect(zeile).toHaveCount(1)
    await expect(zeile).toContainText(`${klartext.slice(0, 12)}…`)
    await expect(zeile).toContainText('Dokumentieren · frei')
    await expect(zeile).toContainText('Grow planen · mit Rückfrage')
    await expect(zeile).toContainText('Geräte schalten · frei')
    await expect(zeile, 'Gesperrte Stufen erscheinen nicht.').not.toContainText('Verwaltung')
    await expect(zeile).toContainText('noch nie')

    // --- ki-stufen-form ---
    await zeile.locator('[data-audit="ki-schluessel-stufen-aendern"]').click()
    let stufen = zeile.locator('[data-audit="ki-stufen-form"]')
    await expect(stufen).toBeVisible()
    await expect(wahl(stufen, 'Grow planen', 'Mit Rückfrage'), 'Das Formular zeigt den gespeicherten Zustand.').toBeChecked()
    await wahl(stufen, 'Geräte schalten', 'Gesperrt').check()
    await wahl(stufen, 'Verwaltung', 'Mit Rückfrage').click()
    await stufen.locator('[data-audit="ki-warnung-bestaetigen"]').click()
    const geaendert = await abgeschickt(page, 'PUT', /\/api\/settings\/ki-zugriff\/schluessel\/\d+$/, async () => {
      await stufen.locator('[data-audit="ki-stufen-speichern"]').click()
    })
    expect(geaendert.stufen).toEqual(['Dokumentieren', 'GrowPlanen', 'Verwaltung'])
    expect(geaendert.rueckfrageBei).toEqual(['GrowPlanen', 'Verwaltung'])

    // Zweites Ändern ohne Neuladen — der Zustand „schon gespeichert" ist ein eigener Fall.
    await zeile.locator('[data-audit="ki-schluessel-stufen-aendern"]').click()
    stufen = zeile.locator('[data-audit="ki-stufen-form"]')
    await expect(wahl(stufen, 'Verwaltung', 'Mit Rückfrage')).toBeChecked()
    // Zwischen Mit Rückfrage und Frei fragt niemand.
    await wahl(stufen, 'Verwaltung', 'Frei').check()
    await expect(stufen.locator('[data-audit="ki-stufen-warnung"]')).toHaveCount(0)
    await wahl(stufen, 'Grow planen', 'Frei').check()
    const zweimal = await abgeschickt(page, 'PUT', /\/api\/settings\/ki-zugriff\/schluessel\/\d+$/, async () => {
      await stufen.locator('[data-audit="ki-stufen-speichern"]').click()
    })
    expect(zweimal.stufen).toEqual(['Dokumentieren', 'GrowPlanen', 'Verwaltung'])
    expect(zweimal.rueckfrageBei).toEqual([])

    bereich = await abschnitt(page)
    zeile = bereich.locator('[data-audit="ki-schluessel"]', { hasText: name })
    await expect(zeile).toContainText('Verwaltung · frei')
    await expect(zeile).toContainText('Grow planen · frei')
    await expect(zeile).not.toContainText('Geräte schalten')

    // Sperren — mit Rückfrage.
    page.once('dialog', (dialog) => { void dialog.accept() })
    await abgeschickt(page, 'POST', /\/api\/settings\/ki-zugriff\/schluessel\/\d+\/sperren$/, async () => {
      await zeile.locator('[data-audit="ki-schluessel-sperren"]').click()
    })
    bereich = await abschnitt(page)
    zeile = bereich.locator('[data-audit="ki-schluessel"]', { hasText: name })
    await expect(zeile).toContainText('gesperrt')
    await expect(zeile.locator('[data-audit="ki-schluessel-sperren"]')).toHaveCount(0)

    // Löschen — mit Rückfrage; danach ist die Zeile weg.
    page.once('dialog', (dialog) => { void dialog.accept() })
    await abgeschickt(page, 'DELETE', /\/api\/settings\/ki-zugriff\/schluessel\/\d+$/, async () => {
      await zeile.locator('[data-audit="ki-schluessel-loeschen"]').click()
    })
    bereich = await abschnitt(page)
    await expect(bereich.locator('[data-audit="ki-schluessel"]', { hasText: name })).toHaveCount(0)
  })

  /**
   * Fork AI (A-003, 04.10.2026): Bru legte am Handy einen Schlüssel an, die
   * Ansicht blieb unten stehen, und der Kasten mit dem Klartext lag über der
   * Bildkante — er hielt den Schlüssel für verloren. Gemessen wird deshalb im
   * schmalen Fenster (412 × 732 px, Telefon mit Browserleiste), ob der Kasten
   * nach dem Anlegen unterhalb der festen Kopfleiste ganz im Bild steht und
   * den Fokus hat. Ohne Reparatur lag er hier mit der Oberkante bei 0 — unter
   * der Kopfleiste; bei 915 px Höhe lag er auch ohne Reparatur im Bild.
   * Zweimal: das zweite Mal ohne Neuladen, vorher ganz nach unten gerollt und
   * mit „Bewegung reduzieren" — der Kasten ist dann schon offen.
   */
  test('Klartext-Kasten kommt am Handy nach dem Anlegen ins Bild und bekommt den Fokus', async ({ page }) => {
    await page.setViewportSize({ width: 412, height: 732 })
    const bereich = await abschnitt(page)
    const ids: number[] = []

    async function anlegen(name: string) {
      if (await bereich.locator('[data-audit="ki-schluessel-form"]').count() === 0) {
        await bereich.locator('[data-audit="ki-schluessel-neu"]').click()
      }
      const neu = bereich.locator('[data-audit="ki-schluessel-form"]')
      await neu.getByLabel('Name des Schlüssels').fill(name)
      const antwort = page.waitForResponse((r) => r.request().method() === 'POST' && /\/api\/settings\/ki-zugriff\/schluessel$/.test(new URL(r.url()).pathname))
      await neu.locator('[data-audit="ki-schluessel-anlegen"]').click()
      const fertig = await antwort
      expect(fertig.ok()).toBe(true)
      ids.push(((await fertig.json()) as { schluessel: { id: number } }).schluessel.id)
    }

    async function imBild(name: string) {
      const kasten = bereich.locator('[data-audit="ki-klartext"]')
      await expect(kasten).toContainText(`Neuer Schlüssel „${name}`)
      // Der sanfte Lauf braucht einen Moment — gemessen wird, wo er ankommt.
      await expect.poll(async () => kasten.evaluate((el) => {
        const kopf = [...document.querySelectorAll<HTMLElement>('.v1-mobile-topbar, .v1-mobile-nav')]
          .filter((k) => getComputedStyle(k).position === 'fixed')
          .reduce((unten, k) => Math.max(unten, k.getBoundingClientRect().bottom), 0)
        const r = el.getBoundingClientRect()
        // Mengenwächter: ohne gefundene Kopfleiste wäre „unter der Kopfleiste" immer wahr.
        if (kopf <= 0) return 'keine feste Kopfleiste gefunden'
        return r.top >= kopf - 1 && r.bottom <= window.innerHeight + 1
      }), { message: 'Der Klartext-Kasten steht nicht ganz zwischen Kopfleiste und Unterkante.', timeout: 5000 }).toBe(true)
      await expect(kasten).toBeFocused()
      await expect(kasten.locator('[data-audit="ki-klartext-kopieren"]')).toBeInViewport({ ratio: 1 })
    }

    try {
      const erster = `Bild ${marke()}`
      await anlegen(erster)
      await imBild(erster)

      await page.emulateMedia({ reducedMotion: 'reduce' })
      await bereich.locator('[data-audit="ki-schluessel-neu"]').click()
      await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight))
      const zweiter = `Bild 2 ${marke()}`
      await anlegen(zweiter)
      await imBild(zweiter)
      await expect(bereich.locator('[data-audit="ki-klartext"]'), 'Der Kasten des ersten Schlüssels bleibt nicht stehen.').toHaveCount(1)

      // Ausblenden: der Fokus fällt nicht auf <body>, sondern zurück auf „Neuer Schlüssel".
      await bereich.locator('[data-audit="ki-klartext-ausblenden"]').click()
      await expect(bereich.locator('[data-audit="ki-klartext"]')).toHaveCount(0)
      await expect(bereich.locator('[data-audit="ki-schluessel-neu"]')).toBeFocused()
    } finally {
      for (const id of ids) await page.request.delete(`${WEG}/schluessel/${id}`)
    }
  })

  /**
   * Fork AI (A-003, 04.10.2026 abends): forkai.166 rollte per scrollIntoView —
   * im Chromium hier richtig, in Brus HA-App lag der Kasten trotzdem ein Stück
   * unter der Kopfleiste (dieselbe Falle wie F-049). Nachgestellt werden die
   * zwei Unterschiede der App-Ansicht, die wir kennen: sie beachtet
   * `scroll-margin-top` nicht, und die Kopfleiste ist bei größerer Schrift
   * höher als der feste Rand. Das Stylesheet hier stellt die UMGEBUNG nach,
   * nicht die Reparatur — die steckt im gebauten Stand.
   * Verlangt wird: Kasten OBEN, direkt unter der Kopfleiste, ganz zu sehen —
   * egal wie weit unten man beim Anlegen war. Dasselbe für „Nur diesen
   * zeigen", das zur Liste „Was die KI zuletzt getan hat" rollt.
   */
  test('wie in der HA-App: Kasten und Protokoll landen oben unter der Kopfleiste', async ({ page }) => {
    await page.setViewportSize({ width: 412, height: 732 })
    const ids: number[] = []
    try {
      // Mehrere Schlüssel wie bei Bru — sonst ist die Seite zu kurz, um weit unten zu sein.
      for (let i = 0; i < 4; i++) {
        const r = await page.request.post(`${WEG}/schluessel`, { data: { name: `Vorrat ${i} ${marke()}`, stufen: ['Dokumentieren'], rueckfrageBei: [] } })
        expect(r.ok()).toBe(true)
        ids.push(((await r.json()) as { schluessel: { id: number } }).schluessel.id)
      }
      const bereich = await abschnitt(page)
      await page.addStyleTag({ content: `
        .scroll-ziel, .ki-protokoll { scroll-margin-top: 0 !important; }
        .v1-mobile-nav { padding-bottom: 28px !important; }` })

      async function obenUnterDerKopfleiste(el: Locator, was: string) {
        await expect.poll(async () => el.evaluate((e) => {
          const kopf = [...document.querySelectorAll<HTMLElement>('.v1-mobile-topbar, .v1-mobile-nav')]
            .filter((k) => getComputedStyle(k).position === 'fixed')
            .reduce((unten, k) => Math.max(unten, k.getBoundingClientRect().bottom), 0)
          if (kopf <= 125) return `Kopfleiste nicht vergrößert (${kopf} px) — der Fall stellt nichts nach`
          const r = e.getBoundingClientRect()
          if (r.top < kopf - 1) return `Oberkante ${Math.round(r.top)} liegt unter der Kopfleiste (${Math.round(kopf)})`
          if (r.top > kopf + 40) return `Oberkante ${Math.round(r.top)} steht nicht oben (Kopfleiste ${Math.round(kopf)})`
          return 'oben'
        }), { message: was, timeout: 5000 }).toBe('oben')
      }

      for (const durchgang of [1, 2]) {
        await bereich.locator('[data-audit="ki-schluessel-neu"]').click()
        const neu = bereich.locator('[data-audit="ki-schluessel-form"]')
        await neu.getByLabel('Name des Schlüssels').fill(`App ${durchgang} ${marke()}`)
        // Weit nach unten, wie beim Ausfüllen am Telefon.
        await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight))
        const antwort = page.waitForResponse((r) => r.request().method() === 'POST' && /\/api\/settings\/ki-zugriff\/schluessel$/.test(new URL(r.url()).pathname))
        await neu.locator('[data-audit="ki-schluessel-anlegen"]').click()
        ids.push(((await (await antwort).json()) as { schluessel: { id: number } }).schluessel.id)
        const kasten = bereich.locator('[data-audit="ki-klartext"]')
        await obenUnterDerKopfleiste(kasten, `Durchgang ${durchgang}: Klartext-Kasten`)
        await expect(kasten.locator('[data-audit="ki-klartext-kopieren"]')).toBeInViewport({ ratio: 1 })
        await expect(kasten).toBeFocused()
      }

      await page.evaluate(() => window.scrollTo(0, 0))
      await bereich.locator('[data-audit="ki-schluessel"]').first().getByRole('button', { name: 'Nur diesen zeigen' }).click()
      await obenUnterDerKopfleiste(page.locator('#ki-protokoll'), 'Nur diesen zeigen: Protokoll')
    } finally {
      for (const id of ids) await page.request.delete(`${WEG}/schluessel/${id}`)
    }
  })
})
