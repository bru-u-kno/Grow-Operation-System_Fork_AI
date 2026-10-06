/**
 * Die Bedienung der Licht-Seite, ohne React.
 *
 * **Zwei Fehler aus der Durchsicht vom 02.10.2026:**
 * - Die Leistungsstufe schickte bei JEDEM Tastendruck einen Befehl. Wer „10"
 *   tippen wollte, schickte beim „1" schon Stufe 1 an den Controller — und das
 *   Feld sprang danach auf den Livewert zurück, die „0" kam nie an.
 * - Jeder Befehl (−, +, Aus, An, Preset) übernahm die Antwort des Backends
 *   ungeprüft als Entwurf. Eine angefangene, noch nicht gespeicherte
 *   Zeitplan-Änderung war damit weg, sobald jemand „+" drückte.
 */

/** Die Stufe aus dem Feld — eine ganze Zahl von 1 bis 10, sonst `null`. */
export function stufeAusFeld(text: string): number | null {
  const roh = text.trim()
  if (!/^\d+$/.test(roh)) return null
  const wert = Number(roh)
  return wert >= 1 && wert <= 10 ? wert : null
}

/** Die zuletzt gesendete Stufe und der Livewert, auf dem sie aufsetzte. */
export type StufenZiel = { ziel: number; basis: number | null }

/**
 * Von welcher Stufe „−" und „+" weiterzählen.
 *
 * Home Assistant meldet eine neue Stufe erst beim nächsten Abruf der Cloud. Bis
 * dahin steht im Livewert noch die alte — wer von ihr aus zählt, schickt bei
 * jedem Klick dasselbe Ziel (6 → 7, 7, 7 …) und die Anzeige rührt sich nicht.
 * Solange der Livewert noch auf dem Stand von damals steht, zählt deshalb das
 * gesendete Ziel; bewegt er sich oder scheitert der Befehl, gilt wieder er.
 */
export function stufeBasis(live: number | null, ziel: StufenZiel | null, entwurf: number, gescheitert: boolean): number {
  if (ziel && !gescheitert && live === ziel.basis) return ziel.ziel
  return live ?? entwurf
}

/** Ein Schritt hoch (+1) oder runter (−1), begrenzt auf 1 bis 10. */
export function stufeSchritt(von: number, richtung: 1 | -1): number {
  return Math.min(10, Math.max(1, von + richtung))
}

const gleich = (a: unknown, b: unknown) => Object.is(a, b) || JSON.stringify(a) === JSON.stringify(b)

/**
 * Der Entwurf, nachdem das Backend neue gespeicherte Einstellungen geschickt hat.
 *
 * Ein Feld, das der Nutzer nicht angefasst hat (Entwurf = alter gespeicherter
 * Wert), übernimmt den neuen gespeicherten Wert. Ein angefasstes Feld bleibt,
 * wie es getippt wurde. So geht weder die ungespeicherte Änderung verloren noch
 * ein Wert, den der Befehl selbst geändert hat (etwa die Stufe).
 */
export function entwurfAbgleichen<T extends object>(entwurf: T, alt: T, neu: T): T {
  const ergebnis = { ...neu } as Record<string, unknown>
  const getippt = entwurf as Record<string, unknown>
  const vorher = alt as Record<string, unknown>
  for (const name of Object.keys(getippt)) {
    if (!gleich(getippt[name], vorher[name])) ergebnis[name] = getippt[name]
  }
  return ergebnis as T
}
