import { RF_KNAPP_PUNKTE } from './entfeuchter-zonen'
import type { Zone } from './entfeuchter-zonen'

/**
 * Fork AI (A-021): Das Ziel der Luftfeuchte und der Temperatur als BAND mit zwei Seiten — Rechenteil
 * ohne React.
 *
 * <b>Anlass (13.10.2026).</b> Das grüne Band stand immer ganz links: es kannte nur eine
 * Obergrenze. „Aber grundsätzlich wäre es ja auch kritisch anzusehen, wenn die Luftfeuchtigkeit zu
 * niedrig ist. Genauso auch die Temperatur."
 *
 * <b>Woher die Grenzen kommen.</b>
 * <ul>
 * <li>Luftfeuchte: Obergrenze aus dem Plan („RH max"); der Plan kennt keine Untergrenze — sie kommt aus
 *   dem Grenzwert für die Meldung aufs Handy (Seite Grenzwerte).</li>
 * <li>Temperatur: Plan-Luft ± Toleranz; gemeint sind die Grenzen des Grenzwerts für die Lufttemperatur,
 *   denn der folgt dem Plan (Wochenplan-Sync) und meldet auch, wenn das Zelt außerhalb liegt.</li>
 * </ul>
 * Fehlt der Grenzwert, bleibt die Untergrenze leer und das Band so einseitig wie vorher.
 */

export type ZielBand = {
  unten: number | null
  oben: number | null
}

/** Die Grenzen eines Grenzwerts für Tag und Nacht, wie `GET /api/zielwerte` sie liefert. */
export type GrenzwertRegel = {
  min: number | null
  max: number | null
  nachtMin?: number | null
  nachtMax?: number | null
  toleranz?: number | null
}

/** Die für die gerade geltende Lichtphase gültigen Grenzen; die Nachtwerte nur nachts und nur, wenn gesetzt. */
export function grenzenFuerPhase(regel: GrenzwertRegel | null | undefined, tagPhase: boolean | null): ZielBand {
  if (!regel) return { unten: null, oben: null }
  const nacht = tagPhase === false
  return {
    unten: (nacht ? regel.nachtMin : undefined) ?? regel.min ?? null,
    oben: (nacht ? regel.nachtMax : undefined) ?? regel.max ?? null,
  }
}

/**
 * Das Zielband der Luftfeuchte: Obergrenze aus dem Plan (Vorrang vor der des Grenzwerts, denn nur die
 * ist das Ziel der Entfeuchter), Untergrenze aus dem Grenzwert.
 */
export function feuchteBand(planOben: number | null, regel: GrenzwertRegel | null | undefined, tagPhase: boolean | null): ZielBand {
  const g = grenzenFuerPhase(regel, tagPhase)
  return ohneUmgedrehtes({ unten: g.unten, oben: planOben ?? g.oben })
}

/**
 * Liegt die Untergrenze über der Obergrenze (etwa ein Grenzwert-Minimum über der Plan-Obergrenze), wäre nichts „im Ziel":
 * dann gilt nur die Obergrenze, wie ohne Grenzwert.
 */
function ohneUmgedrehtes(band: ZielBand): ZielBand {
  return band.unten != null && band.oben != null && band.unten > band.oben ? { unten: null, oben: band.oben } : band
}

/** Das Zielband der Temperatur: die Grenzen des Grenzwerts, sonst Plan-Luft ± Toleranz, sonst leer. */
export function temperaturBand(planLuftC: number | null, regel: GrenzwertRegel | null | undefined, tagPhase: boolean | null): ZielBand {
  const g = grenzenFuerPhase(regel, tagPhase)
  if (g.unten != null || g.oben != null) return ohneUmgedrehtes(g)
  // Ohne Toleranz wäre das Band einen Punkt breit („16 – 16 °C"): dann gibt es keines, und der Überblick fällt auf die
  // Höchsttemperatur zurück.
  const toleranz = regel?.toleranz ?? 0
  return planLuftC == null || toleranz <= 0 ? { unten: null, oben: null } : { unten: planLuftC - toleranz, oben: planLuftC + toleranz }
}

const gueltig = (w: number | null | undefined): w is number => w != null && Number.isFinite(w)

/**
 * Zone eines Messwerts gegen ein Band mit zwei Seiten.
 *
 * Im Ziel: zwischen unten und oben. „Knapp daneben": bis `randOben` über der Obergrenze, bis `randUnten`
 * unter der Untergrenze. Weiter weg: deutlich daneben. Ohne Untergrenze zählt „zu niedrig" nicht
 * (wie vor A-021); ohne Obergrenze zählt „zu hoch" nicht.
 */
export function zoneImBand(
  ist: number | null | undefined,
  band: ZielBand,
  randOben: number,
  randUnten: number,
): Zone | null {
  if (!gueltig(ist)) return null
  if (band.unten == null && band.oben == null) return null
  if (band.oben != null && ist > band.oben) return ist <= band.oben + randOben ? 'knapp' : 'kritisch'
  if (band.unten != null && ist < band.unten) return ist >= band.unten - randUnten ? 'knapp' : 'kritisch'
  return 'ziel'
}

/** Luftfeuchte: so viele Prozentpunkte außerhalb des Bandes gelten noch als „knapp daneben" (oben wie unten). */
export function feuchteZoneImBand(ist: number | null | undefined, band: ZielBand): Zone | null {
  return zoneImBand(ist, band, RF_KNAPP_PUNKTE, RF_KNAPP_PUNKTE)
}

/**
 * Temperatur: oben ist „knapp daneben", bis die Höchsttemperatur erreicht ist (darüber sind alle
 * Entfeuchter aus — deutlich daneben); unten gilt ein Rand von `randUnten` Kelvin.
 */
export function temperaturZoneImBand(
  ist: number | null | undefined,
  band: ZielBand,
  hoechstC: number,
  randUnten: number,
): Zone | null {
  const oben = band.oben ?? hoechstC
  return zoneImBand(ist, { unten: band.unten, oben }, Math.max(0, hoechstC - oben), randUnten)
}

/** Zahl auf eine Nachkommastelle, ohne „,0" bei ganzen Zahlen — für die Zielzeile („45 – 54 %"). */
export function kurzeZahl(wert: number): string {
  const r = Math.round(wert * 10) / 10
  return r.toLocaleString('de-DE', { minimumFractionDigits: 0, maximumFractionDigits: 1 })
}

/** Die Zielzeile: „45 – 54", „≤ 54", „≥ 45" oder null ohne jede Grenze. */
export function zielText(band: ZielBand): string | null {
  if (band.unten != null && band.oben != null) return `${kurzeZahl(band.unten)} – ${kurzeZahl(band.oben)}`
  if (band.oben != null) return `≤ ${kurzeZahl(band.oben)}`
  if (band.unten != null) return `≥ ${kurzeZahl(band.unten)}`
  return null
}

/** Die Plaketten oben im Überblick: Lichtphase und Woche des Plans. Fehlendes bleibt weg, nie ein Platzhalter. */
export function kontextPlaketten(tagPhase: boolean | null, planWoche: string | null): string[] {
  return [
    tagPhase === true ? '☀ Tag' : tagPhase === false ? '☾ Nacht' : null,
    planWoche ? `Woche ${planWoche}` : null,
  ].filter((p): p is string => p != null)
}

/**
 * Das Urteil in Worten neben der großen Zahl — die Zone steht nie nur als Farbe da. Richtung und Maß:
 * „knapp über dem Ziel", „deutlich unter dem Plan", bei der Temperatur über der Höchsttemperatur „zu warm — alle aus".
 */
export function abweichungsWort(
  art: 'feuchte' | 'temperatur',
  ist: number | null | undefined,
  band: ZielBand,
  zone: Zone | null,
): string {
  if (zone == null || !gueltig(ist)) return 'keine Aussage'
  if (zone === 'ziel') return 'im Ziel'
  const ueber = band.oben != null && ist > band.oben
  const bezug = art === 'temperatur' ? 'dem Plan' : 'dem Ziel'
  if (art === 'temperatur' && ueber && zone === 'kritisch') return 'zu warm — alle aus'
  return `${zone === 'knapp' ? 'knapp' : 'deutlich'} ${ueber ? 'über' : 'unter'} ${bezug}`
}
