import { gleich } from './entfeuchter-zusatz'
import type { EntfeuchterEinstellungen, EntfeuchterZusatzEinstellungen } from './steuerung-typen'

/**
 * Fork AI (A-021): Die Stufe „Wie reagieren die Entfeuchter?" — Rechenteil ohne React.
 *
 * <b>Anlass (13.10.2026).</b> Die Seite „Entfeuchtung" hatte rund 36 Einzelwerte. Eine Wahl
 * „Ruhig · Normal · Schnell" setzt die Werte, die zusammen bestimmen, wie oft und wie schnell die
 * Geräte schalten, auf einmal; die Einzelwerte bleiben unter „Erweitert" änderbar.
 *
 * <b>Normal sind Nutzer-Werte vom 12.10.2026</b> (Abstand 4 %, Einschalten nach 2 min, Mindestlaufzeit
 * 20/15 min): Wer sie schon hat, sieht „Normal" und nichts ändert sich. Ruhig und Schnell sind ein
 * Vorschlag — der Abstand folgt den früheren Stufen knapp/normal/ruhig (2/4/6 %), die Zeiten sind
 * nicht gegen den Betrieb geprüft.
 *
 * <b>Was die Stufe NICHT setzt.</b> „Zuschalten nach" und „Mindestpause" des Zusatzes gehören seiner
 * Hilfsstärke (aus · sparsam · normal · kräftig) — beide Wahlen würden sich sonst gegenseitig
 * überschreiben. Die Wartezeit bei trockener Außenluft bleibt, wie sie ist, wird aber nie kürzer
 * als „Einschalten nach" (das Backend verlangt es).
 */

export type StufenName = 'ruhig' | 'normal' | 'schnell'

export type StufenWerte = {
  /** Abstand EIN → AUS in Prozent Luftfeuchte (Hauptentfeuchter). */
  abstandProzent: number
  /** Einschaltverzögerung des Hauptentfeuchters in Minuten. */
  einschaltMin: number
  mindestlaufHauptMin: number
  mindestlaufZusatzMin: number
}

export const STUFEN: Record<StufenName, StufenWerte> = {
  ruhig: { abstandProzent: 6, einschaltMin: 10, mindestlaufHauptMin: 30, mindestlaufZusatzMin: 25 },
  normal: { abstandProzent: 4, einschaltMin: 2, mindestlaufHauptMin: 20, mindestlaufZusatzMin: 15 },
  schnell: { abstandProzent: 2, einschaltMin: 1, mindestlaufHauptMin: 10, mindestlaufZusatzMin: 10 },
}

export const STUFEN_AUSWAHL: ReadonlyArray<{ wert: StufenName; label: string }> = [
  { wert: 'ruhig', label: 'Ruhig' },
  { wert: 'normal', label: 'Normal' },
  { wert: 'schnell', label: 'Schnell' },
]

type HauptFelder = Pick<EntfeuchterEinstellungen, 'hystereseProzent' | 'einschaltverzoegerungMin' | 'mindestlaufzeitMin'>
type ZusatzFelder = Pick<EntfeuchterZusatzEinstellungen, 'mindestlaufzeitMin'>

/** Welche Stufe die Einzelwerte ergeben — „eigene", wenn sie zu keiner passen. Ohne Zusatz zählt er nicht mit. */
export function stufeErkennen(haupt: HauptFelder, zusatz: ZusatzFelder | null): StufenName | 'eigene' {
  const treffer = (Object.keys(STUFEN) as StufenName[]).find((name) => {
    const s = STUFEN[name]
    return gleich(haupt.hystereseProzent, s.abstandProzent)
      && haupt.einschaltverzoegerungMin === s.einschaltMin
      && haupt.mindestlaufzeitMin === s.mindestlaufHauptMin
      && (zusatz == null || zusatz.mindestlaufzeitMin === s.mindestlaufZusatzMin)
  })
  return treffer ?? 'eigene'
}

/** Die Felder des Hauptentfeuchters, die eine Stufe setzt (die Außenluft-Wartezeit wächst bei Bedarf mit). */
export function stufeFuerHaupt(
  stufe: StufenName,
  aktuell: Pick<EntfeuchterEinstellungen, 'wartezeitAussenluftMin'>,
): Pick<EntfeuchterEinstellungen, 'hystereseProzent' | 'einschaltverzoegerungMin' | 'mindestlaufzeitMin' | 'wartezeitAussenluftMin'> {
  const s = STUFEN[stufe]
  return {
    hystereseProzent: s.abstandProzent,
    einschaltverzoegerungMin: s.einschaltMin,
    mindestlaufzeitMin: s.mindestlaufHauptMin,
    wartezeitAussenluftMin: Math.max(aktuell.wartezeitAussenluftMin, s.einschaltMin),
  }
}

/** Das Feld des Zusatz-Entfeuchters, das eine Stufe setzt. */
export function stufeFuerZusatz(stufe: StufenName): ZusatzFelder {
  return { mindestlaufzeitMin: STUFEN[stufe].mindestlaufZusatzMin }
}
