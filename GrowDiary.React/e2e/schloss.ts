import { mkdirSync, openSync, closeSync, unlinkSync, readFileSync, writeFileSync, existsSync, statSync } from 'node:fs'
import { join } from 'node:path'
import { test } from '@playwright/test'
import { tmpdir } from 'node:os'

/**
 * Ein Schloss für die Tests, die denselben Grow beschreiben.
 *
 * <b>Der Anlass.</b> Vier E2E-Dateien fassen Grow 1 an: der Flipdatum-Rundweg,
 * die Feldprüfung des Grow-Formulars, die Pflanze-je-Topf-Fälle und der
 * Formular-Rundweg. Playwright fährt mit <c>fullyParallel: true</c>, und
 * verschiedene Dateien laufen in verschiedenen Prozessen — also gleichzeitig
 * auf demselben Datensatz. Zwei von drei vollen Läufen meldeten daraufhin
 * zwei Fehlschläge, ein dritter keinen. Genau die beiden Dateien allein liefen
 * dreimal grün.
 *
 * <b>Warum das schlimmer ist als ein Fehler.</b> „Ein Test, dessen Ausgang vom
 * Zeitpunkt abhängt, hat nichts geprüft" (CLAUDE.md). Schlimmer noch: schlägt
 * der Pflanzen-Fall in der Mitte fehl, bleibt eine Pflanze gelöscht — und der
 * <i>nächste</i> Lauf beginnt auf einem kaputten Bestand. Die Verschmutzung
 * pflanzt sich fort.
 *
 * <b>Warum ein Lockfile.</b> Playwright-Worker sind eigene Prozesse; ein
 * Semaphor im Speicher wirkt nur innerhalb eines Workers. Eine Datei sehen
 * alle. <c>wx</c> legt sie exklusiv an — schlägt fehl, wenn sie schon
 * existiert, und das ist genau die Prüfung, die es braucht.
 */

const SCHLOSS = join(tmpdir(), 'grow-os-e2e', 'grow1.lock')
/** Nach dieser Zeit gilt ein Schloss als vergessen — ein abgestürzter Worker
    soll die Mappe nicht dauerhaft blockieren. */
const VERFALL_MS = 90_000

function schlafe(ms: number): Promise<void> {
  return new Promise((weiter) => { setTimeout(weiter, ms) })
}

/**
 * Nimmt das Schloss. In `test.beforeEach` aufrufen, nicht in `beforeAll`:
 * Playwright darf die Fälle EINER Datei auf mehrere Worker verteilen, und
 * `beforeAll` liefe dann zweimal fuer dieselbe Datei — der zweite Worker
 * warte auf ein Schloss, das der erste erst am Dateiende abgibt.
 */
export async function nimmSchloss(): Promise<void> {
  mkdirSync(join(tmpdir(), 'grow-os-e2e'), { recursive: true })

  // Die Wartezeit gehört nicht zum Fall. Sie zählt sonst in sein Limit (30 s), und ein Fall, der hinter einem langen
  // Halter steht — der Addback-Fall „Nachmessung" hält das Schloss gut eine Minute —, bräche im `beforeEach` ab, ohne dass
  // die App etwas falsch gemacht hätte (CI 14.10.2026, drei von sechs Läufen rot). Das Limit läuft schon WÄHREND des
  // Wartens ab, deshalb wird es vorher um die längste mögliche Wartezeit erweitert (so lange wartet die Schleife unten
  // höchstens) und nach dem Erhalt auf „sein eigenes Limit + tatsächlich Gewartetes" zurückgesetzt. Dem Fall bleibt so
  // sein volles Limit, und ein hängender Halter bremst ihn nicht länger als VERFALL_MS.
  // Außerhalb eines Falls oder Hooks (kein TestInfo) und bei „kein Limit" (0) gibt es nichts zu verlängern.
  let info: ReturnType<typeof test.info> | null = null
  try { info = test.info() } catch { /* kein laufender Fall */ }
  const eigenesLimit = info?.timeout ?? 0
  const verlaengern = info != null && eigenesLimit > 0
  if (verlaengern) info!.setTimeout(eigenesLimit + VERFALL_MS + 5_000)

  const angefangen = Date.now()
  const bis = angefangen + VERFALL_MS
  let habe = false
  while (!habe) {
    try {
      const griff = openSync(SCHLOSS, 'wx')
      writeFileSync(griff, String(process.pid))
      closeSync(griff)
      habe = true
    } catch {
      // Ein vergessenes Schloss aufbrechen — aber erst, wenn es wirklich alt ist.
      if (existsSync(SCHLOSS) && Date.now() - statSync(SCHLOSS).mtimeMs > VERFALL_MS) {
        try { unlinkSync(SCHLOSS) } catch { /* jemand war schneller */ }
        continue
      }
      if (Date.now() > bis) {
        const wer = existsSync(SCHLOSS) ? readFileSync(SCHLOSS, 'utf8') : '?'
        throw new Error(
          `Das Schloss für Grow 1 war ${VERFALL_MS / 1000} s lang belegt (Prozess ${wer}). `
          + 'Läuft ein alter Playwright-Lauf noch?',
        )
      }
      await schlafe(120)
    }
  }

  if (verlaengern) info!.setTimeout(eigenesLimit + (Date.now() - angefangen))
}

/** Gibt das Schloss zurück. Gehört in `test.afterEach` — auch nach einem
    roten Fall, sonst hängt die ganze Mappe am ersten Fehler. */
export function gibSchloss(): void {
  try { unlinkSync(SCHLOSS) } catch { /* schon weg */ }
}
