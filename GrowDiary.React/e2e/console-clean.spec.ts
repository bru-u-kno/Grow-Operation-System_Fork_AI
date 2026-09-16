import { test, expect } from '@playwright/test'

/**
 * Keine Seite darf mit einem Fehler in der Konsole oder einem gescheiterten
 * Request aufmachen.
 *
 * Beides sieht man beim Durchklicken nicht: eine React-Warnung über doppelte
 * Keys, ein 500er auf einem Nebenpfad, ein Bild, das nie ankommt — die Seite
 * sieht trotzdem richtig aus und ist es nicht. Dieser Test schaut dorthin,
 * wo man beim Ansehen nicht hinschaut.
 *
 * WICHTIG — zwei Umgebungen: lokal läuft ein Backend hinter dem Vorschau-Server,
 * in der CI nicht. Dort beantwortet der Proxy jeden API-Aufruf mit 502, und der
 * Test hat genau das gemeldet: 24-mal rot, ohne dass an der App etwas falsch
 * war. Deshalb wird zuerst geprüft, ob überhaupt ein Backend antwortet. Ohne
 * eines bleiben die API-Fehler außen vor — die Prüfung auf echte Skriptfehler
 * läuft weiter, denn die braucht keinen Server.
 */
const ROUTEN = [
  '/', '/messung', '/addback', '/aufgaben',
  '/grows', '/grows/1', '/grows/new', '/diagnose', '/journal', '/sorten', '/archiv',
  '/zelte', '/zelte/1', '/hydro', '/sensoren', '/regeln', '/home-assistant',
  '/wissen', '/settings', '/start', '/release',
  '/regeln?tab=automatik', '/regeln?tab=ki',
  '/zielwerte', '/zielwerte?tab=plan', '/zielwerte?tab=meldungen',
]

// Die Liste muss etwas hergeben. Ohne diese Zeile laeuft die Schleife
// darunter bei einer leeren Liste null Mal durch — und der Testlauf meldet
// gruen, obwohl er nichts geprueft hat. Genau diese Falle hat in diesem
// Projekt schon zugeschlagen.
if (ROUTEN.length < 10) throw new Error(`Diese Pruefung sieht nur ${ROUTEN.length} Seiten — sie wuerde nichts messen.`)

/** Was am Testaufbau liegt, nicht an der App. */
function istAufbauRauschen(text: string): boolean {
  return /\/api\/live\/tents\/\d+\/camera/.test(text)
    || /\/api\/home-assistant\/entities/.test(text)
    || /favicon/.test(text)
}

/** Ohne Backend ist jeder API-Aufruf zum Scheitern verurteilt. */
function istApiOhneBackend(text: string, backendLaeuft: boolean): boolean {
  return !backendLaeuft && /\/api\//.test(text)
}

let backendLaeuft = false

test.beforeAll(async ({ request }) => {
  try {
    const antwort = await request.get('/api/system/backend-health', { timeout: 5000 })
    backendLaeuft = antwort.ok()
  } catch {
    backendLaeuft = false
  }
  if (!backendLaeuft) {
    // Sichtbar im Bericht, damit niemand glaubt, die API sei mitgeprüft worden.
    test.info().annotations.push({
      type: 'Hinweis',
      description: 'Kein Backend erreichbar — geprüft werden nur Skriptfehler, nicht die API-Antworten.',
    })
  }
})

for (const route of ROUTEN) {
  test(`ohne Fehler: ${route}`, async ({ page }) => {
    const konsole: string[] = []
    const netz: string[] = []

    page.on('console', (msg) => {
      if (msg.type() !== 'error') return
      // „Failed to load resource" nennt die URL nicht im Text, sondern nur in
      // der Herkunft — ohne die liesse sich Aufbau-Rauschen nicht aussortieren.
      const text = `${msg.text()} ${msg.location()?.url ?? ''}`.trim()
      if (istAufbauRauschen(text) || istApiOhneBackend(text, backendLaeuft)) return
      konsole.push(text)
    })
    page.on('pageerror', (error) => konsole.push(`pageerror: ${error.message}`))
    page.on('response', (response) => {
      if (response.status() < 400) return
      const text = `${response.status()} ${response.url()}`
      if (istAufbauRauschen(text) || istApiOhneBackend(text, backendLaeuft)) return
      netz.push(text)
    })

    await page.goto(route, { waitUntil: 'networkidle' })
    // Kurz nachlaufen lassen: Effekte holen nach dem ersten Anstrich nach.
    await page.waitForTimeout(400)

    expect(konsole, `Konsolenfehler auf ${route}:\n${konsole.join('\n')}`).toEqual([])
    expect(netz, `Gescheiterte Requests auf ${route}:\n${netz.join('\n')}`).toEqual([])
  })
}
