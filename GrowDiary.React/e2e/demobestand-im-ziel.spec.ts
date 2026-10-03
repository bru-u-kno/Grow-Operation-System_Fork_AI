import { test, expect } from '@playwright/test'
import { darfUeberspringen } from './pflicht'

/**
 * Der Demobestand hält seine eigenen Ziele ein.
 *
 * **Der Anlass (offene Punkte 03.10.2026, D1).** Beide Blütezelte lasen
 * dieselbe Demo-Kurve aus einem älteren Profil — Luftfeuchte um 54 %, EC um
 * 1,03. Ihre Grows laufen aber nach dem SKX-Plan: Zelt 1 verlangt höchstens
 * 50 % und EC 1,5–1,7, das Blütezelt 2 höchstens 40 % und EC 0,87–1,13. Die
 * Live-Kacheln standen auf „daneben", und die Kopfzeile zählte die Werte
 * mit. Nach der Regel in CLAUDE.md ist dann der Bestand falsch — er ist die
 * Grundlage jeder Oberflächen-Prüfung, und ein Bestand, der seine eigenen
 * Ziele verfehlt, verdeckt, ob eine Kachel richtig urteilt.
 *
 * **Warum E2E und nicht im Backend-Test.** Die Pläne entstehen erst beim
 * Start der Demo-App (Program.cs legt sie nach dem Bestand an); der
 * Backend-Test sieht Ziele aus dem Sollwert-Profil. Gemessen wird dort, wo
 * der Nutzer und das Tor hinsehen.
 *
 * **Einzelwerte zählen nicht.** Ein Ziel wie „23 °C" trifft keine echte Kurve
 * auf die Kommastelle genau — die Kachel zeigt dort die Abweichung als Zahl
 * („+2,2 K"), das ist ihr gewolltes Bild und kein Widerspruch.
 */
test('Demobestand: jeder Wert mit Bereichs- oder Höchstziel liegt darin', async ({ page }) => {
  const antwort = await page.request.get('/api/settings/tents').catch(() => null)
  darfUeberspringen(!antwort?.ok(), 'Kein Backend erreichbar — laeuft die App unter GROW_OS_URL?')
  const zelte = (await antwort!.json()) as Array<{ id: number; name: string }>

  type Kachel = { key: string; numericValue?: number | null; targetMin?: number | null; targetMax?: number | null }
  const geprueft: string[] = []
  const daneben: string[] = []

  for (const zelt of zelte) {
    const live = (await (await page.request.get(`/api/live/tents/${zelt.id}`)).json()) as { metrics: Kachel[] }
    for (const k of live.metrics) {
      const wert = k.numericValue
      const unten = k.targetMin ?? null
      const oben = k.targetMax ?? null
      if (wert == null || (unten == null && oben == null)) continue
      if (unten != null && oben != null && Math.abs(oben - unten) < 1e-9) continue   // Einzelwert
      geprueft.push(`${zelt.name}/${k.key}`)
      if ((unten != null && wert < unten) || (oben != null && wert > oben)) {
        daneben.push(`${zelt.name}: ${k.key} = ${wert} (Ziel ${unten ?? '…'}–${oben ?? '…'})`)
      }
    }
  }

  // Mengenwächter: ohne Bereichsziele prüft der Fall nichts.
  expect(geprueft.length, `Nur ${geprueft.length} Kacheln mit Bereichsziel gefunden: ${geprueft.join(', ')}`)
    .toBeGreaterThanOrEqual(8)
  expect(daneben, 'Der Demobestand verfehlt seine eigenen Planziele — dann ist der Bestand falsch, nicht die Kachel.')
    .toEqual([])
})
