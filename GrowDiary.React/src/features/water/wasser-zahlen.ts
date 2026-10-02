/**
 * Zahl → Eingabefeld für das Wasserprofil.
 *
 * Die Falle, gegen die diese Datei existiert: `toLocaleString('de-DE')`
 * schreibt 1234 als „1.234" — und ein naives `replace(',', '.')` liest den
 * Tausenderpunkt danach als Dezimalpunkt. Wer hartes Wasser eintrug
 * (über 1000 µS/cm sind in deutschen Netzen real), bekam beim nächsten
 * Speichern still ein Tausendstel seines Werts. Rundweg-Sicherheit heißt:
 * `zahlOderNull(zahlZuText(x)) === x`, für jedes x.
 *
 * **Gelesen wird nicht mehr hier.** Bis zum 02.10.2026 stand in dieser Datei
 * eine eigene Leseregel (`textZuZahl`), die Tausenderpunkte verstand — als
 * einzige der App. Dieselbe Regel gilt jetzt überall und steht an einer
 * Stelle: `zahlOderNull` in `src/zahlenfeld.ts`.
 */

/** Zahl → Feldtext: Dezimalkomma, aber ohne Tausenderpunkte. */
export function zahlZuText(value: number | null | undefined): string {
  if (value === null || value === undefined) return ''
  return value.toLocaleString('de-DE', { maximumFractionDigits: 3, useGrouping: false })
}
