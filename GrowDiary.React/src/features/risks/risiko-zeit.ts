import type { RiskEventDto } from '../../types'
import { formatNumber } from '../../utils'

/**
 * Was eine Risiko-Karte über die Zeit sagt: seit wann, wie aktuell, wie oft.
 *
 * Reine Funktionen ohne Uhr — „jetzt" kommt immer als Zahl herein, damit die
 * Tests nicht von der Tageszeit abhängen.
 */

const MINUTE = 60_000
const STUNDE = 60 * MINUTE
const TAG = 24 * STUNDE

/** Wie lange ein vom Abgleich geschlossenes Risiko noch als „wieder okay" sichtbar bleibt. */
export const WIEDER_OKAY_SICHTBAR_MS = TAG

/** „32 Min.", „3 Std.", „1 Tag", „2 Tagen" — ohne Vorwort, damit „Seit …" und „vor …" daraus werden. */
export function dauerText(ms: number): string {
  const dauer = Math.max(0, ms)
  if (dauer < MINUTE) return 'unter 1 Min.'
  if (dauer < STUNDE) return `${Math.floor(dauer / MINUTE)} Min.`
  if (dauer < TAG) return `${Math.floor(dauer / STUNDE)} Std.`
  const tage = Math.floor(dauer / TAG)
  return tage === 1 ? '1 Tag' : `${tage} Tagen`
}

/** „Seit 2 Tagen" — ab dem ersten Erkennen. */
export function seitText(startIso: string, jetzt: number): string {
  return `Seit ${dauerText(jetzt - new Date(startIso).getTime())}`
}

/** „vor 32 Min." / „gerade eben" — für den Zeitpunkt der letzten Messung. */
export function vorText(iso: string, jetzt: number): string {
  const ms = jetzt - new Date(iso).getTime()
  return ms < MINUTE ? 'gerade eben' : `vor ${dauerText(ms)}`
}

export type RohWert = {
  istwert: number | null
  min: number | null
  max: number | null
  einheit: string
}

/**
 * Liest `actual=6.22;target=5.6-6.2;unit=pH` — das Rohwert-Feld, das der
 * Abgleich schreibt. Die Zahlen stehen dort mit Punkt; unlesbar → `null`, dann
 * zeigt die Karte nur den Text und erfindet nichts.
 */
export function leseRohwert(roh: string | null | undefined): RohWert | null {
  if (!roh) return null
  const teile = Object.fromEntries(
    roh.split(';').map((stueck) => {
      const gleich = stueck.indexOf('=')
      return gleich < 0 ? [stueck, ''] : [stueck.slice(0, gleich), stueck.slice(gleich + 1)]
    }),
  ) as Record<string, string>
  const zahl = (text: string | undefined): number | null => {
    if (text === undefined || text === '' || text === '-') return null
    const wert = Number(text)
    return Number.isFinite(wert) ? wert : null
  }
  const istwert = zahl(teile.actual)
  if (istwert === null) return null

  const ziel = teile.target ?? ''
  let min: number | null = null
  let max: number | null = null
  if (ziel.startsWith('>=')) {
    min = zahl(ziel.slice(2))
  } else if (ziel.startsWith('<=')) {
    max = zahl(ziel.slice(2))
  } else {
    // „5.6-6.2" — die Zahlen sind nicht negativ, ein Bindestrich trennt also eindeutig.
    const treffer = /^(\d+(?:\.\d+)?)-(\d+(?:\.\d+)?)$/.exec(ziel)
    if (treffer) {
      min = Number(treffer[1])
      max = Number(treffer[2])
    }
  }
  // Der Abgleich schreibt die Temperatur als „C", gezeigt wird „°C".
  const einheit = teile.unit === 'C' ? '°C' : (teile.unit ?? '')
  return { istwert, min, max, einheit }
}

/** „Ziel 5,6 – 6,2", „Ziel 1,6" (Band ohne Breite), „Ziel ≥ 1,6", „Ziel ≤ 22". `null`, wenn kein Ziel bekannt. */
export function zielText(wert: RohWert, stellen = 2): string | null {
  const { min, max } = wert
  if (min !== null && max !== null) {
    return min === max
      ? `Ziel ${formatNumber(min, stellen)}`
      : `Ziel ${formatNumber(min, stellen)} – ${formatNumber(max, stellen)}`
  }
  if (min !== null) return `Ziel ≥ ${formatNumber(min, stellen)}`
  if (max !== null) return `Ziel ≤ ${formatNumber(max, stellen)}`
  return null
}

export type Wiederholung = { anzahl: number; seitIso: string }

/**
 * Wie oft dieselbe Abweichung (gleicher Entdopplungs-Schlüssel) schon eine
 * Karte hatte, und seit wann. `null` beim ersten Mal — dann gibt es nichts zu
 * sagen, und die Karte zeigt keine Zeile.
 */
export function wiederholungen(risiko: RiskEventDto, alle: readonly RiskEventDto[]): Wiederholung | null {
  if (!risiko.dedupeKey) return null
  const gleiche = alle.filter((anderes) => anderes.dedupeKey === risiko.dedupeKey)
  if (!gleiche.some((anderes) => anderes.id === risiko.id)) gleiche.push(risiko)
  if (gleiche.length < 2) return null
  const frueheste = gleiche.reduce((a, b) => (a.startedAtUtc <= b.startedAtUtc ? a : b))
  return { anzahl: gleiche.length, seitIso: frueheste.startedAtUtc }
}

/** Offen oder bestätigt, also noch nicht vom Abgleich oder von Hand geschlossen. */
export function istOffen(risiko: RiskEventDto): boolean {
  return risiko.status === 'Open' || risiko.status === 'Acknowledged'
}

/** Vom Abgleich geschlossen, weil die Abweichung nicht mehr besteht, und noch nicht länger als 24 h her. */
export function istWiederOkay(risiko: RiskEventDto, jetzt: number): boolean {
  if (!risiko.resolvedAutomatically || !risiko.resolvedAtUtc) return false
  const her = jetzt - new Date(risiko.resolvedAtUtc).getTime()
  return her >= 0 && her <= WIEDER_OKAY_SICHTBAR_MS
}

const WOCHENTAG = new Intl.DateTimeFormat('de-DE', { weekday: 'short' })
const UHR = new Intl.DateTimeFormat('de-DE', { hour: '2-digit', minute: '2-digit' })
const TAG_MONAT = new Intl.DateTimeFormat('de-DE', { day: '2-digit', month: '2-digit' })

/** „heute 17:32", „gestern 05:33", „morgen 05:30", sonst „Mi. 14.10., 17:32" — in der Ortszeit des Geräts. */
export function tagUhrText(iso: string, jetzt: number): string {
  const zeit = new Date(iso)
  const uhr = UHR.format(zeit)
  const unterschied = Math.round((new Date(zeit.getFullYear(), zeit.getMonth(), zeit.getDate()).getTime()
    - new Date(new Date(jetzt).getFullYear(), new Date(jetzt).getMonth(), new Date(jetzt).getDate()).getTime()) / TAG)
  if (unterschied === 0) return `heute ${uhr}`
  if (unterschied === -1) return `gestern ${uhr}`
  if (unterschied === 1) return `morgen ${uhr}`
  return `${WOCHENTAG.format(zeit)} ${TAG_MONAT.format(zeit)}, ${uhr}`
}

/**
 * Ein Messabstand für „Üblich wären …": in Minuten bis zwei Stunden, in Stunden bis
 * drei Tage, darüber in Tagen — und gerundet statt abgeschnitten. `dauerText` schneidet
 * ab; aus 36 Stunden wurde damit „1 Tag", aus 90 Minuten „1 Std.".
 */
export function abstandText(minuten: number): string {
  if (minuten <= 120) return `${Math.round(minuten)} Min.`
  const stunden = minuten / 60
  if (stunden < 72) return `${Math.round(stunden)} Std.`
  const tage = Math.round(stunden / 24)
  return tage === 1 ? '1 Tag' : `${tage} Tagen`
}

/** Hängt einen Punkt an, wenn der Text nicht schon mit einem endet („12 Std." bleibt „12 Std."). */
function satzEnde(text: string): string {
  return text.endsWith('.') ? text : `${text}.`
}

export type PruefStand = {
  /** Erste Zeile des Blocks „Stand der Prüfung". */
  zeile: string
  /** Zweite Zeile, falls es etwas zu sagen gibt (nächste Messung bzw. „üblich wären …"). */
  zusatz: string | null
  /** Überfällig: die Karte sagt „Nicht mehr geprüft" statt „Besteht noch". */
  veraltet: boolean
}

/**
 * Wie frisch die Prüfung hinter einer offenen Karte ist. `null`, wenn die
 * Antwort keinen Messzeitpunkt trägt (Karten ohne Grow, von Hand angelegte,
 * geschlossene) — dann zeigt die Karte nichts davon.
 */
export function pruefStand(risiko: RiskEventDto, jetzt: number): PruefStand | null {
  if (!risiko.latestMeasurementAtUtc) return null
  const letzte = risiko.latestMeasurementAtUtc
  const abstand = risiko.measurementIntervalMinutes ?? null
  const wann = tagUhrText(letzte, jetzt)

  if (risiko.measurementStale) {
    return {
      zeile: `Letzte Messung ${vorText(letzte, jetzt)} (${wann})`,
      zusatz: abstand
        ? `Üblich wären ${satzEnde(abstandText(abstand))} Ob es noch besteht, ist unklar.`
        : 'Ob es noch besteht, ist unklar.',
      veraltet: true,
    }
  }

  // Die erwartete nächste Messung kommt vom Server: sie folgt dem Rhythmus der
  // AUTOMATISCHEN Messungen. Ab der neuesten Messung zu rechnen (die auch von Hand
  // sein kann) ergab am 16.10.2026 „morgen 02:36" statt „17:00".
  const naechste = risiko.nextMeasurementExpectedAtUtc
  return {
    zeile: `Messung ${wann} (${vorText(letzte, jetzt)})`,
    zusatz: naechste && new Date(naechste).getTime() > jetzt
      ? `Nächste Messung ca. ${tagUhrText(naechste, jetzt)}`
      : null,
    veraltet: false,
  }
}
