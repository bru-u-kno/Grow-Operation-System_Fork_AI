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
