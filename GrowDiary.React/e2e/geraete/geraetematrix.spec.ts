import { test, expect, type Frame, type Page } from '@playwright/test'
import { istAufbauRauschen, einblendungenAbwarten, scrolltSeitwaerts, ueberstand, zuKleineZiele } from '../oberflaeche-messung'
import { FASSUNGEN, type Fassung } from './geraete'
import { imHaRahmen } from './ha-rahmen'

/**
 * Die Gerätematrix: dieselben Seiten auf mehreren Endgeräten, in vier Fassungen.
 *
 * <b>Was sie sieht, was andere Prüfungen nicht sehen.</b> `handy-zuschnitt`, `touch-targets` und `kontrast`
 * messen auf EINEM Gerät (Desktop-Chromium, ab und zu auf 360/390 px verkleinert). Die Matrix stellt Telefone
 * (Android, 360 und 412 px), Tablets (hoch und quer) und große Bildschirme nach, jeweils
 * <b>hell</b>, <b>dunkel</b>, mit <b>doppelter Systemschrift</b> (Bedienungshilfe „große Schrift") und
 * <b>im HA-Rahmen</b> (App in einem iframe unter der HA-Kopfleiste oder neben der Seitenleiste, wie über Ingress).
 *
 * <b>Wie sie läuft.</b> Nur in den Projekten `geraet-*` der Konfiguration: `GERAETE=1 npx playwright test` (Chromium-Geräte)
 * oder `GERAETE=alle` (mit WebKit und Firefox, wenn installiert). Gegen eine laufende Demo-App
 * (`GROW_OS_URL=http://127.0.0.1:5191`) sehen die Seiten Daten; ohne Backend nur ihre Leerzustände — das ist ein
 * Messen der Rahmen, nicht der Inhalte (siehe Warnung in `handy-zuschnitt.spec.ts`).
 *
 * <b>Was geprüft wird.</b> (1) Nichts ragt rechts hinaus, die Seite scrollt nicht seitwärts; (2) auf Geräten mit Touch sind
 * Bedienelemente mindestens 44 px treffbar; (3) keine Skript- oder Konsolenfehler beim Öffnen.
 * Die Farbmessung (Kontrast) bleibt in `kontrast.spec.ts`: sie ist unabhängig vom Gerät.
 *
 * <b>Nicht nachgestellt:</b> das Scrollverhalten der WebView in der HA-App (F-049) — das zeigt nur ein echtes Gerät.
 */

/**
 * Ein Satz Seiten, der jede Bauform trägt: Live-Kacheln, Formular, Tabelle, Geräte-Rollen, Zahlenfelder der Steuerung,
 * mehrschrittiger Ablauf, Kosten-Reiter. Der volle Satz steht in `seiten.ts` (Menü und Detailseiten) und wäre mit
 * vier Fassungen je Gerät zu lang.
 */
const SEITEN = [
  '/',
  '/messung',
  '/addback',
  '/aufgaben',
  '/grows/1',
  '/zelte',
  '/geraete?reiter=rollen',
  '/steuerung/co2',
  '/kosten?tab=verbrauch',
  '/wasserwechsel?growId=1&schritt=2',
  '/plan',
  '/handy?tab=push',
]

// Eine leere Liste liefe null Mal durch und meldete grün — genau die Falle, die dieses Projekt schon kannte.
if (SEITEN.length < 8) throw new Error(`Die Gerätematrix sieht nur ${SEITEN.length} Seiten — sie würde nichts messen.`)

/** Fassung `gross`: die Grundschrift verdoppeln. Die App rechnet durchweg in rem, also wächst jede Schrift mit. */
async function schriftSetzen(ziel: Page | Frame, prozent: number): Promise<void> {
  if (prozent === 100) return
  await ziel.evaluate((p) => { document.documentElement.style.fontSize = `${p}%` }, prozent)
}

async function oeffnen(page: Page, pfad: string, fassung: Fassung): Promise<Page | Frame> {
  await page.addInitScript((t) => localStorage.setItem('growos.theme', t), fassung.thema)
  // Große Schrift gilt auf einem echten Gerät von Anfang an: Rechnungen beim Aufbau (Diagramm-Achsen, Textbreiten) sehen sie
  // dann. Nur nachträglich umzustellen ließ „Licht · an 08:00 · aus 20:00" im Verlauf rot werden, obwohl es auf dem Gerät passt.
  if (!fassung.haRahmen && fassung.schrift !== 100) {
    await page.addInitScript((p) => {
      const setzen = () => {
        const st = document.createElement('style')
        st.textContent = `html{font-size:${p}% !important}`
        document.documentElement.appendChild(st)
      }
      // Das Skript läuft vor dem Dokument: erst setzen, wenn es ein <html> gibt.
      if (document.documentElement) setzen()
      else new MutationObserver((_, o) => { if (document.documentElement) { o.disconnect(); setzen() } }).observe(document, { childList: true })
    }, fassung.schrift)
  }
  if (fassung.haRahmen) {
    const rahmen = await imHaRahmen(page, pfad)
    await rahmen.waitForLoadState('networkidle')
    await expect(rahmen.locator('.v1-app-shell')).toBeVisible()
    await schriftSetzen(rahmen, fassung.schrift)
    return rahmen
  }
  await page.goto(pfad, { waitUntil: 'networkidle' })
  await expect(page.locator('.v1-app-shell')).toBeVisible()
  return page
}

for (const fassung of FASSUNGEN) {
  for (const pfad of SEITEN) {
    test(`${fassung.name}: ${pfad}`, async ({ page }, testInfo) => {
      const meldungen: string[] = []
      page.on('pageerror', (e) => meldungen.push(`Skriptfehler: ${e.message}`))
      page.on('console', (m) => { if (m.type() === 'error' && !istAufbauRauschen(m.text())) meldungen.push(`Konsole: ${m.text().slice(0, 140)}`) })

      const ziel = await oeffnen(page, pfad, fassung)
      await einblendungenAbwarten(ziel)

      expect(await ueberstand(ziel), `Diese Elemente ragen auf ${pfad} (${fassung.name}, ${testInfo.project.name}) rechts hinaus`).toEqual([])
      expect(await scrolltSeitwaerts(ziel), `${pfad} scrollt seitwärts (${fassung.name}, ${testInfo.project.name})`).toBe(false)

      // Trefferflächen zählen nur, wo es einen groben Zeiger gibt: die Vergrößerung hängt an `@media (pointer: coarse)`.
      if (testInfo.project.use.hasTouch) {
        expect(await zuKleineZiele(ziel, 44), `Zu kleine Ziele auf ${pfad} (${fassung.name}, ${testInfo.project.name})`).toEqual([])
      }

      // Nur Skriptfehler und App-Konsolenfehler; API-Fehler ohne Backend sind Aufbau, kein Befund.
      const echte = meldungen.filter((m) => !/\/api\//.test(m) && !/Failed to load resource/.test(m))
      expect(echte, `Fehler beim Öffnen von ${pfad} (${fassung.name}, ${testInfo.project.name})`).toEqual([])
    })
  }
}
