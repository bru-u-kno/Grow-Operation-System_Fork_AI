import { zahlOderNull } from '../../zahlenfeld'

/**
 * Was aus dem Grenzwert-Formular an den Server geht.
 *
 * <b>Der Anlass (01.09.2026).</b> Die Seite schickte nur die Zeilen mit Haken
 * „Aktiv" — und der Server ersetzt beim Speichern den <b>ganzen</b> Satz für
 * das Zelt. Wer den Haken herausnahm und speicherte, bekam die Meldung
 * „gespeichert", sah die Zahlen weiter im Formular stehen (neu geladen wird
 * erst beim Zeltwechsel) und hatte sie auf dem Server verloren. Beim nächsten
 * Aufruf der Seite waren sie weg.
 *
 * „Aktiv" heisst <b>pausiert</b>, nicht gelöscht. Diese Datei trennt die
 * Entscheidung von der Anzeige — im Formular liess sie sich nicht prüfen.
 */

/** Eine Zeile des Formulars, so wie sie auf dem Schirm steht. */
export type Grenzwertzeile = {
  min: string
  max: string
  cooldown: string
  enabled: boolean
  /** Woher die Grenzen kommen — Vorgabe ist die eingetragene Zahl. */
  quelle: Grenzwertquelle
  /** Nur bei 'Plan' gefüllt: Abstand zum Zielband. */
  toleranz: string
}

/** 'Fest' = eingetragene Zahlen, 'Plan' = Zielband der laufenden Woche. */
export type Grenzwertquelle = 'Fest' | 'Plan'

/**
 * Messgrößen, für die der Wochenplan überhaupt einen Wert kennt.
 *
 * Luftfeuchte, Sauerstoff und Wasserstand stehen in keinem Sollwertprofil und
 * in keinem Feed-Chart. Dort darf „Plan“ nicht wählbar sein — eine Regel, die
 * nie melden kann, ist schlimmer als keine.
 */
export const PLANFAEHIGE_METRIKEN: readonly string[] = [
  'reservoir-ph', 'reservoir-ec', 'reservoir-temp', 'orp', 'vpd', 'co2', 'ppfd',
]

export function kannPlan(metricKey: string): boolean {
  return PLANFAEHIGE_METRIKEN.includes(metricKey)
}

/** Eine Regel, wie der Server sie erwartet. */
export type Grenzwertregel = {
  metricKey: string
  minValue: number | null
  maxValue: number | null
  notifyService: string
  enabled: boolean
  cooldownMinutes: number
  quelle: Grenzwertquelle
  toleranz: number | null
}

/**
 * Die Regeln, die gespeichert werden.
 *
 * Mitgeschickt wird jede Zeile, die <b>mindestens eine Grenze</b> trägt — mit
 * ihrem eigenen Haken. Nur eine Zeile ganz ohne Grenzen ist nichts und fällt
 * weg; genau die lehnt der Server ohnehin ab.
 */
export function speicherbareRegeln(
  kennungen: readonly string[],
  zeilen: Record<string, Grenzwertzeile>,
): Grenzwertregel[] {
  return kennungen
    .map((kennung) => ({ kennung, zeile: zeilen[kennung] }))
    .filter((x): x is { kennung: string; zeile: Grenzwertzeile } => x.zeile != null)
    /* Eine Plan-Zeile trägt bewusst keine Zahlen — sie holt ihre Grenzen aus
       dem Wochenplan. Sie muss deshalb auch ohne Min/Max mitgeschickt werden,
       sonst wäre sie beim nächsten Laden verschwunden. */
    .filter(({ kennung, zeile }) =>
      (zeile.quelle === 'Plan' && kannPlan(kennung))
      || zahlOderNull(zeile.min) != null
      || zahlOderNull(zeile.max) != null)
    .map(({ kennung, zeile }) => ({
      metricKey: kennung,
      minValue: zeile.quelle === 'Plan' ? null : zahlOderNull(zeile.min),
      maxValue: zeile.quelle === 'Plan' ? null : zahlOderNull(zeile.max),
      notifyService: '',
      enabled: zeile.enabled,
      cooldownMinutes: Math.max(1, zahlOderNull(zeile.cooldown) ?? 30),
      quelle: zeile.quelle,
      toleranz: zeile.quelle === 'Plan' ? zahlOderNull(zeile.toleranz) : null,
    }))
}

/** Wieviele davon wirklich wachen — für die Meldung nach dem Speichern. */
export function aktiveRegeln(regeln: readonly Grenzwertregel[]): number {
  return regeln.filter((r) => r.enabled).length
}

/**
 * Eine Zeile, deren Untergrenze über der Obergrenze liegt.
 *
 * <b>Der Anlass.</b> Der Server nahm ein vertauschtes Paar an: bei
 * <c>min 22 / max 18</c> rechnet er <c>wert &lt; min ? unten : wert &gt; max ?
 * oben : im Rahmen</c> — bei 20 °C greift die erste Bedingung, und die Regel
 * meldet dauerhaft „zu kalt", obwohl 20 zwischen den beiden Zahlen liegt. Wer
 * sich beim Eintippen vertut, bekommt eine Warnung, die nie mehr aufhört.
 *
 * Gemeldet wird vor dem Absenden, damit der Nutzer es korrigieren kann, statt
 * eine Fehlermeldung vom Server zu lesen.
 */
export function vertauschteGrenzen(regeln: readonly Grenzwertregel[]): string[] {
  return regeln
    .filter((r) => r.minValue != null && r.maxValue != null && r.minValue > r.maxValue)
    .map((r) => r.metricKey)
}
