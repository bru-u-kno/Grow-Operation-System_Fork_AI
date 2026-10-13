import type { EntfeuchterEinstellungen } from './steuerung-typen'

/**
 * Fork AI (A-021): Die Einschalt-Schwelle der Luftfeuchte und ihr Abstand zur Ausschalt-Schwelle —
 * Rechenteil ohne React.
 *
 * <b>Eine Schwelle statt vier.</b> Die Seite zeigt „Einschalten über" (ein Wert für Tag und Nacht,
 * auf Wunsch getrennt) und leitet „Ausschalten unter" daraus ab: EIN minus Abstand. Gespeichert
 * werden weiter die vier Felder (EIN/AUS, Tag/Nacht) — das Backend und die Regelung in Home
 * Assistant kennen nur diese. Der Fork rechnet AUS erst neu, wenn der Nutzer EIN oder den Abstand
 * anfasst; wer nichts ändert, behält seine gespeicherten Werte.
 */

/** Untergrenze der Schwellen, wie das Backend sie prüft (30 bis 90 %). */
export const SCHWELLE_MIN = 30
export const SCHWELLE_MAX = 90

type Schwellen = Pick<EntfeuchterEinstellungen, 'feuchteEinTag' | 'feuchteAusTag' | 'feuchteEinNacht' | 'feuchteAusNacht'>

/** Auf eine Nachkommastelle, damit 50 − 4 nie als 46,00000001 gespeichert wird. */
const rund = (w: number) => Math.round(w * 10) / 10

/** AUS aus EIN und Abstand — nie unter der Untergrenze des Backends. */
export function ausAusEin(ein: number, abstandProzent: number): number {
  return Math.max(SCHWELLE_MIN, rund(ein - abstandProzent))
}

/** Stehen Tag und Nacht verschieden? Dann zeigt die Seite beide Felder von Anfang an. */
export function schwellenGetrennt(s: Pick<Schwellen, 'feuchteEinTag' | 'feuchteEinNacht'>): boolean {
  return s.feuchteEinTag !== s.feuchteEinNacht
}

/**
 * Die Felder nach einer Änderung an EIN. Ohne „getrennt" gilt der Wert für Tag und Nacht;
 * mit „getrennt" ändert sich nur die gewählte Tageszeit.
 */
export function einSchwelleSetzen(
  aktuell: Schwellen,
  abstandProzent: number,
  neu: { wert: number; tageszeit: 'tag' | 'nacht' | 'beide' },
): Schwellen {
  const tag = neu.tageszeit === 'nacht' ? aktuell.feuchteEinTag : neu.wert
  const nacht = neu.tageszeit === 'tag' ? aktuell.feuchteEinNacht : neu.wert
  return {
    feuchteEinTag: tag,
    feuchteAusTag: ausAusEin(tag, abstandProzent),
    feuchteEinNacht: nacht,
    feuchteAusNacht: ausAusEin(nacht, abstandProzent),
  }
}

/** Die AUS-Felder nach einer Änderung am Abstand (EIN bleibt). */
export function abstandAnwenden(aktuell: Schwellen, abstandProzent: number): Schwellen {
  return {
    feuchteEinTag: aktuell.feuchteEinTag,
    feuchteAusTag: ausAusEin(aktuell.feuchteEinTag, abstandProzent),
    feuchteEinNacht: aktuell.feuchteEinNacht,
    feuchteAusNacht: ausAusEin(aktuell.feuchteEinNacht, abstandProzent),
  }
}

/** Kleinster EIN-Wert, bei dem AUS noch über der Untergrenze des Backends liegt. */
export function einMinimum(abstandProzent: number): number {
  return SCHWELLE_MIN + Math.ceil(abstandProzent) + 1
}

/** Das kleinste Maß an Wartezeit auf Außenluft, das zu „Einschalten nach" passt — das Backend verlangt es. */
export function aussenluftNachEinschalten(aussenluftMin: number, einschaltMin: number): number {
  // Ein geleertes Feld gibt NaN weiter — Math.max(5, NaN) wäre NaN und bliebe es: die Wartezeit hat kein eigenes Feld,
  // in dem man es korrigieren könnte, und das Speichern bliebe gesperrt (E2E-Befund 13.10.2026).
  if (!Number.isFinite(einschaltMin)) return aussenluftMin
  return Math.max(aussenluftMin, einschaltMin)
}
