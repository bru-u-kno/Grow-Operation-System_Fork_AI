/**
 * Was die Licht-Seite nach einem Befehl oder dem Speichern sagt.
 *
 * Seit forkai.153 gehen Presets und Zeiten durch den AcSchreiber im Backend:
 * jeder Schritt wird nachgeprüft, nach dem ersten nicht bestätigten wird
 * abgebrochen. Das Ergebnis steht in `haAngenommen`. Vorher wertete die Seite
 * es bei Befehlen gar nicht aus — ein verworfenes Preset sah aus wie ein
 * erfolgreiches (Prüfer-Befund 29.09.2026).
 */

/** Die Meldung nach einem Befehl; `null`, wenn es nichts zu sagen gibt. */
export function befehlsMeldung(art: string, haAngenommen: boolean | null | undefined): string | null {
  if (haAngenommen !== false) return null
  if (art === 'preset') {
    return 'Der Controller hat das Preset nicht bestätigt. Der Modus wurde nicht umgestellt — '
      + 'bitte am Controller nachsehen und erneut versuchen.'
  }
  if (art === 'quittieren') return null
  return 'Der Befehl kam bei Home Assistant nicht an. Bitte erneut versuchen.'
}

/** Die Meldung nach dem Speichern. */
export function speicherMeldung(haAngenommen: boolean | null | undefined): string {
  return haAngenommen === false
    ? 'Gespeichert — aber der Controller hat nicht alles bestätigt. Bitte am Controller nachsehen und erneut speichern.'
    : 'Gespeichert.'
}
