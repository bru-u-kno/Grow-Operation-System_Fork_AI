/**
 * Fork AI (forkai.129): Rechenteil der Entfeuchter-Seite — ohne React, damit er
 * sich ohne Browser prüfen lässt.
 */

/** Eine Marke auf dem Schwellen-Band, Position in Prozent der Bandbreite. */
export type BandMarke = { art: 'aus' | 'ein' | 'deckel'; wert: number; pos: number }

export type Band = {
  von: number
  bis: number
  marken: BandMarke[]
  /** Position des Istwerts; null ohne Messwert. */
  ist: number | null
  /** Beginn des grünen Bereichs (ab AUS wird entfeuchtet); null ohne AUS. */
  zoneAb: number | null
}

/** Rand um die äußeren Werte, damit Punkt und Marken nicht am Rand kleben. */
const RAND = 4

/**
 * Das Band zeigt nur den Ausschnitt, auf den es ankommt.
 *
 * <b>Warum kein fester Bereich.</b> Auf 40–70 % lagen EIN (52), Istwert (53,2)
 * und Plan-Deckel (55) so dicht, dass man den Punkt kaum sah (Bru, 21.09.2026).
 * Der Ausschnitt reicht vom kleinsten bis zum größten der vier Werte, plus
 * {@link RAND} Prozentpunkte — ganzzahlig, damit die Skala ruhig bleibt.
 */
export function bandBerechnen(werte: { aus: number | null; ein: number | null; deckel: number | null; ist: number | null }): Band | null {
  const bekannt = [werte.aus, werte.ein, werte.deckel, werte.ist].filter((w): w is number => w != null && Number.isFinite(w))
  if (bekannt.length === 0) return null

  const von = Math.floor(Math.min(...bekannt) - RAND)
  const bis = Math.ceil(Math.max(...bekannt) + RAND)
  const pos = (w: number) => Math.round(((w - von) / (bis - von)) * 1000) / 10

  const marken: BandMarke[] = []
  if (werte.aus != null) marken.push({ art: 'aus', wert: werte.aus, pos: pos(werte.aus) })
  if (werte.ein != null) marken.push({ art: 'ein', wert: werte.ein, pos: pos(werte.ein) })
  if (werte.deckel != null) marken.push({ art: 'deckel', wert: werte.deckel, pos: pos(werte.deckel) })

  return {
    von,
    bis,
    marken,
    ist: werte.ist == null ? null : pos(werte.ist),
    zoneAb: werte.aus == null ? null : pos(werte.aus),
  }
}

/** Die drei Stufen für „Wie ruhig soll er schalten?". */
export const HYSTERESE_STUFEN: ReadonlyArray<{ wert: number; label: string }> = [
  { wert: 2, label: 'knapp' },
  { wert: 4, label: 'normal' },
  { wert: 6, label: 'ruhig' },
]

/** Welche Stufe ein Wert ist — null heißt „eigener Wert". */
export function hystereseStufe(wert: number): number | null {
  return HYSTERESE_STUFEN.some((s) => Math.abs(s.wert - wert) < 1e-9) ? wert : null
}

/** Temperatur max., wie sie der Server bildet: Plan + Abstand, ohne Plan fest. */
export function tempMax(modus: 'plan' | 'fest', abstandK: number, festC: number, planLuftC: number | null): number {
  return modus === 'plan' && planLuftC != null ? Math.round((planLuftC + abstandK) * 10) / 10 : festC
}

/** Zahl deutsch mit fester Stellenzahl, „–“ statt einer erfundenen Null. */
export function zahl(wert: number | null | undefined, stellen = 1): string {
  if (wert == null || !Number.isFinite(wert)) return '–'
  return wert.toLocaleString('de-DE', { minimumFractionDigits: stellen, maximumFractionDigits: stellen })
}
