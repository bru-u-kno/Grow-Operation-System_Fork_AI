import { test, type APIRequestContext, type Page } from '@playwright/test'

/**
 * Ein übersprungener Test ist kein bestandener.
 *
 * <b>Der Anlass.</b> Die E2E-Mappe fährt im Tor gegen einen statischen Server
 * ohne Backend. Von 34 Fällen aus vier Dateien sind dort <b>31 übersprungen und
 * 3 grün</b> — und im Bericht steht „275 passed", was niemand als Warnung
 * liest. Genau die Prüfung, die das leere Archiv gefunden hätte, lief nie mit;
 * gefunden hat es der Tester.
 *
 * <b>Was diese Datei ändert.</b> Ein Übersprung braucht ab jetzt einen Grund,
 * und mit <c>E2E_STRENG=1</c> wird er zum Fehler statt zur stillen Zeile. Im
 * Tor läuft die Mappe gegen die laufende App mit vollem Demobestand — dort
 * darf sich nichts mehr wegducken. Auf dem Entwicklungsrechner, wo oft kein
 * Backend läuft, bleibt der Übersprung erlaubt.
 */

/** Läuft dieser Durchgang streng? */
export const streng = process.env.E2E_STRENG === '1'

/**
 * Überspringen — aber nur, wenn es erlaubt ist.
 *
 * @param bedingung Wahr heißt: es fehlt etwas, der Test kann nicht laufen.
 * @param grund Ausgeschrieben, in ganzen Worten. Steht im Bericht und ist im
 *   strengen Lauf die Fehlermeldung — „kein Grow" hilft dort niemandem,
 *   „kein laufender Grow im Bestand, obwohl der Demobestand einen anlegen
 *   sollte" schon.
 */
export function darfUeberspringen(bedingung: boolean, grund: string): void {
  if (!bedingung) return

  if (streng) {
    throw new Error(
      `Übersprungen wegen „${grund}" — im strengen Lauf ist das ein Fehler.\n`
      + 'Das Tor fährt gegen die laufende App mit vollem Demobestand; wenn hier etwas fehlt, '
      + 'fehlt es im Demobestand (GrowDiary.Web/Services/Demobestand.cs) oder die App läuft nicht.',
    )
  }

  test.skip(true, grund)
}

/**
 * Antwortet ein Backend?
 *
 * **Warum das hier steht.** Ein Rundweg ohne Backend scheitert sonst mit
 * „Element nicht gefunden" — die Seite selbst liefert ja 200, sie zeigt nur
 * einen Ladezustand. Diese Diagnose kostet jedesmal zehn Minuten; genau das ist
 * am 02.09.2026 passiert. Der Satz steht deshalb einmal hier statt in jeder
 * Datei neu.
 *
 * Im strengen Lauf ist ein fehlendes Backend ohnehin ein Fehler — dann sagt die
 * Meldung wenigstens, welcher.
 */
export async function backendAntwortet(anfrage: {
  get: (url: string) => Promise<{ ok: () => boolean }>
}): Promise<boolean> {
  try {
    return (await anfrage.get('/api/grows')).ok()
  } catch {
    return false
  }
}

/**
 * Fork AI (A-011): Die Seite so zeigen, als wäre „KI-Funktionen" an — nur im Browser.
 *
 * Neue Installationen starten mit „KI aus", und dann gibt es die KI-Seite nicht (sie leitet
 * auf die Startseite). Wer nur die Oberfläche prüft, auch im Durchgang ohne Backend, meldet
 * dem Browser den Schalter als an; der Server bleibt unberührt. Wer echte KI-Endpunkte braucht,
 * nimmt <see cref="kiAmServerAn"/>.
 */
export async function kiImBrowserAn(page: Page): Promise<void> {
  await page.route('**/api/settings/ki', (route) => (route.request().method() === 'GET'
    ? route.fulfill({ json: { aktiv: true } })
    : route.continue()))
}

/**
 * Fork AI (A-011): „KI-Funktionen" am Server einschalten — so, wie es ein Anwender zuerst tut.
 *
 * Ohne das antworten die KI-Endpunkte (Zugriff, Mappe, Home Assistant) mit 404 `ki_aus`.
 * Der Schalter bleibt danach an: „an" ist Brus Zustand, und keine andere Prüfung verlässt sich auf „aus".
 */
export async function kiAmServerAn(request: APIRequestContext): Promise<void> {
  const antwort = await request.put('/api/settings/ki', { data: { aktiv: true } })
  if (!antwort.ok()) throw new Error(`KI-Funktionen ließen sich nicht einschalten: HTTP ${antwort.status()}`)
}
