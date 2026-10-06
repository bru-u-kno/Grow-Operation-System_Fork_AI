/* src/features/probelauf/probelauf-anzeige.ts — Fork AI (A-010)
   Wie der Probelauf auf dem Schirm steht: Statuswörter, Zeiten, Zahlen. Reine Funktionen, ohne Browser prüfbar. */

import { formatNumber } from '../../utils'
import type { ProbelaufGrenzen, ProbelaufKennzahl, ProbelaufLauf, ProbelaufMessreihe, ProbelaufMesswerte, ProbelaufStatus } from '../../types'
import { unlesbarMeldung, unlesbareFelder, zahlOderNull } from '../../zahlenfeld'

/** Die deutschen Wörter für den Status — nie der Bezeichner (`RueckstellungOffen`). */
const STATUS_TEXT: Record<ProbelaufStatus, string> = {
  Laeuft: 'läuft',
  Nachlauf: 'Nachlauf',
  Fertig: 'fertig',
  Abgebrochen: 'abgebrochen',
  RueckstellungOffen: 'Zurückstellen offen',
}

export function statusText(status: ProbelaufStatus): string {
  return STATUS_TEXT[status] ?? status
}

/** Ein Lauf, der noch nicht abgeschlossen ist: der Eingriff, der Nachlauf oder ein unbestätigtes Zurückstellen. */
export function istOffen(status: ProbelaufStatus): boolean {
  return status === 'Laeuft' || status === 'Nachlauf' || status === 'RueckstellungOffen'
}

/** 760 s → „12:40". Negatives gilt als 0. */
export function restzeit(sekunden: number): string {
  const s = Math.max(0, Math.round(sekunden))
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`
}

export function kennzahlEinheit(groesse: ProbelaufKennzahl['groesse']): string {
  return groesse === 'Feuchte' ? '%' : groesse === 'Temperatur' ? '°C' : 'kPa'
}

/** Nachkommastellen, mit denen eine Größe angezeigt wird: VPD braucht zwei, der Rest eine. */
function stellen(groesse: ProbelaufKennzahl['groesse']): number {
  return groesse === 'VPD' ? 2 : 1
}

export function wertText(groesse: ProbelaufKennzahl['groesse'], wert: number): string {
  return `${formatNumber(wert, stellen(groesse))} ${kennzahlEinheit(groesse)}`
}

/** „+0,29 %/Min." — mit Vorzeichen, damit Anstieg und Abfall auf einen Blick unterscheidbar sind. */
export function proMinuteText(k: ProbelaufKennzahl): string {
  const stellenPro = k.groesse === 'VPD' ? 3 : 2
  const betrag = formatNumber(Math.abs(k.aenderungProMinute), stellenPro)
  const zeichen = k.aenderungProMinute > 0 ? '+' : k.aenderungProMinute < 0 ? '−' : ''
  return `${zeichen}${betrag} ${kennzahlEinheit(k.groesse)}/Min.`
}

export function erholungText(k: ProbelaufKennzahl): string {
  return k.erholungMinuten == null ? 'nicht erreicht' : `${formatNumber(k.erholungMinuten, 0)} Min.`
}

/** Ein Punkt der Kurve: Minuten seit Beginn des Eingriffs und der Wert. */
export interface KurvenPunkt {
  minute: number
  wert: number
}

export type KurvenGroesse = 'feuchte' | 'temp' | 'vpd'

/**
 * Die Messreihe einer Größe, in Minuten seit dem Start des Eingriffs.
 * Vorlauf liegt davor (negative Minuten), der Nachlauf dahinter.
 */
export function kurvenPunkte(
  reihe: ProbelaufMessreihe, groesse: KurvenGroesse, startUtc: string,
): { vorlauf: KurvenPunkt[]; waehrend: KurvenPunkt[]; nachlauf: KurvenPunkt[] } {
  const start = new Date(startUtc).getTime()
  const wandeln = (liste: ProbelaufMesswerte[]): KurvenPunkt[] => liste
    .filter((m) => m[groesse] != null)
    .map((m) => ({ minute: (new Date(m.zeitUtc).getTime() - start) / 60000, wert: m[groesse] as number }))
    .sort((a, b) => a.minute - b.minute)
  return { vorlauf: wandeln(reihe.vorlauf), waehrend: wandeln(reihe.waehrend), nachlauf: wandeln(reihe.nachlauf) }
}

/** Die jüngste Messung aus dem Eingriff oder — falls dort noch keine ist — dem Vorlauf. */
export function jetztWerte(lauf: ProbelaufLauf): ProbelaufMesswerte | null {
  const r = lauf.messreihe
  if (!r) return null
  return r.nachlauf.at(-1) ?? r.waehrend.at(-1) ?? r.vorlauf.at(-1) ?? null
}

/** „heute 14:10" / „gestern 22:40" / „05.10. 22:40" — wie die Liste einen Lauf datiert. */
export function startText(startUtc: string, jetzt: Date = new Date()): string {
  const d = new Date(startUtc)
  const uhr = d.toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })
  const tag = (x: Date) => `${x.getFullYear()}-${x.getMonth()}-${x.getDate()}`
  const gestern = new Date(jetzt)
  gestern.setDate(jetzt.getDate() - 1)
  if (tag(d) === tag(jetzt)) return `heute ${uhr}`
  if (tag(d) === tag(gestern)) return `gestern ${uhr}`
  return `${d.toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit' })} ${uhr}`
}

/** Die Grenzen, wie sie in den Eingabefeldern stehen — Text, deutsch geschrieben. */
export type GrenzenText = { feuchteMax: string; tempMax: string; vpdMin: string; vpdMax: string }

/**
 * Aus den getippten Grenzen die Zahlen — oder ein Satz, warum es nicht geht.
 *
 * <b>Leer heißt nicht überwacht</b>, unlesbar heißt Fehler (sonst verschwindet eine Grenze, ohne dass es jemand
 * merkt), und <b>mindestens eine</b> Grenze muss gesetzt sein: ohne Grenze kann ein Lauf nicht abbrechen, wenn
 * etwas aus dem Ruder läuft.
 */
export function grenzenAusText(t: GrenzenText): { grenzen: ProbelaufGrenzen } | { fehler: string } {
  const meldung = unlesbarMeldung(unlesbareFelder([
    [t.feuchteMax, 'Luftfeuchte max.'], [t.tempMax, 'Temperatur max.'], [t.vpdMin, 'VPD min.'], [t.vpdMax, 'VPD max.'],
  ]))
  if (meldung) return { fehler: meldung }

  const g: ProbelaufGrenzen = {
    feuchteMax: zahlOderNull(t.feuchteMax), tempMax: zahlOderNull(t.tempMax),
    vpdMin: zahlOderNull(t.vpdMin), vpdMax: zahlOderNull(t.vpdMax),
  }
  if (Object.values(g).every((v) => v == null)) {
    return { fehler: 'Mindestens eine Grenze muss gesetzt sein — sonst kann der Lauf nicht abbrechen, wenn etwas aus dem Ruder läuft.' }
  }
  return { grenzen: g }
}
