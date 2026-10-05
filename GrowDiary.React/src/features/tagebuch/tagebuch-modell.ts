import type { TagebuchEreignisDto, TagebuchNotizDto, TagebuchSprungDto, TagebuchWerteDto } from '../../types'
import { ABLAUF_ARTEN, EINTRAG_ARTEN, MEILENSTEIN_ARTEN } from '../grow-detail/journal-bearbeiten'
import { zahl } from '../live/verlauf-modell'

/**
 * Das Grow-Tagebuch (A-006) ohne Oberfläche: Filter, Beschriftungen, Zahlen.
 *
 * Die Tage, Uhrzeiten und Minuten rechnet der Server (Ortszeit der Anlage) —
 * hier wird nur noch dargestellt, nie neu geschnitten.
 */

export type Filter = 'alles' | 'messwerte' | 'wasser' | 'notizen'

export const FILTER: ReadonlyArray<{ value: Filter; label: string }> = [
  { value: 'alles', label: 'Alles' },
  { value: 'messwerte', label: 'Messwerte' },
  { value: 'wasser', label: 'Wasser' },
  { value: 'notizen', label: 'Notizen & Fotos' },
]

/** Gehört das Ereignis in diesen Filter? */
export function passtZumFilter(e: TagebuchEreignisDto, filter: Filter): boolean {
  switch (filter) {
    case 'alles': return true
    case 'messwerte': return e.art === 'messung' || e.art === 'auffaellig'
    case 'wasser': return e.wasser
    case 'notizen': return e.art === 'notiz' || e.art === 'meilenstein' || e.art === 'foto' || e.fotos.length > 0
  }
}

/** Wasserart, wie das Addback-Formular sie nennt. */
export function wasserName(roh: string | null): string | null {
  switch (roh) {
    case 'Tap': return 'Leitungswasser'
    case 'RO': return 'Osmose / VE-Wasser'
    case 'Mixed': return 'Mischung'
    default: return null
  }
}

/** Art eines Addback-Eintrags. */
export function addbackArtName(roh: string): string {
  switch (roh) {
    case 'TopOff': return 'Nachfüllen'
    case 'Correction': return 'Korrektur'
    default: return 'Addback'
  }
}

export type Ton = 'accent' | 'warn' | 'info' | 'muted'

/**
 * Das Etikett eines Journaleintrags — dieselben Namen wie im Formular
 * „Bearbeiten" (`EINTRAG_ARTEN`), damit eine Art nicht an zwei Stellen
 * verschieden heißt.
 */
export function notizEtikett(notiz: TagebuchNotizDto): { tag: string; ton: Ton } {
  const meilenstein = MEILENSTEIN_ARTEN[notiz.entryType]
  if (meilenstein) return { tag: 'Meilenstein', ton: 'accent' }
  // Wasserwechsel und Fütterung setzt seit forkai.172 nur der Ablauf — ihr Name steht in ABLAUF_ARTEN.
  const name = EINTRAG_ARTEN.find((art) => art.value === notiz.entryType)?.label ?? ABLAUF_ARTEN[notiz.entryType] ?? 'Notiz'
  const ton: Ton = notiz.entryType === 'Problem' || notiz.entryType === 'Observation'
    ? 'warn'
    : notiz.entryType === 'ReservoirChange' || notiz.entryType === 'Feeding' ? 'info' : 'muted'
  return { tag: name, ton }
}

/** Ein Messwert als Zelle. */
export type Zelle = { name: string; wert: string; einheit: string | null }

const WERTE: ReadonlyArray<{ feld: keyof TagebuchWerteDto; name: string; einheit: string | null; nachkomma: number }> = [
  { feld: 'ph', name: 'pH', einheit: null, nachkomma: 2 },
  { feld: 'ec', name: 'EC', einheit: 'mS/cm', nachkomma: 2 },
  { feld: 'wasserC', name: 'Wasser', einheit: '°C', nachkomma: 1 },
  { feld: 'luftC', name: 'Luft', einheit: '°C', nachkomma: 1 },
  { feld: 'feuchteProzent', name: 'Feuchte', einheit: '%', nachkomma: 0 },
  { feld: 'co2Ppm', name: 'CO₂', einheit: 'ppm', nachkomma: 0 },
  { feld: 'orpMv', name: 'ORP', einheit: 'mV', nachkomma: 0 },
  { feld: 'sauerstoffMgL', name: 'DO', einheit: 'mg/L', nachkomma: 1 },
  { feld: 'fuellstandL', name: 'Wasserstand', einheit: 'L', nachkomma: 0 },
]

/** Die Zellen einer Messung — nur Felder, die gemessen wurden. */
export function zellen(werte: TagebuchWerteDto): Zelle[] {
  return WERTE
    .filter((w) => werte[w.feld] != null)
    .map((w) => ({ name: w.name, wert: zahl(werte[w.feld], w.nachkomma), einheit: w.einheit }))
}

/** Die Zeilen der Vorher/Nachher-Tabelle eines Wasserwechsels — fest, damit „nicht gemessen" sichtbar ist. */
export const VORGANG_ZEILEN: ReadonlyArray<{ feld: keyof TagebuchWerteDto; name: string; einheit: string | null; nachkomma: number }> = [
  { feld: 'ec', name: 'EC', einheit: 'mS/cm', nachkomma: 2 },
  { feld: 'ph', name: 'pH', einheit: null, nachkomma: 2 },
  { feld: 'wasserC', name: 'Wasser', einheit: '°C', nachkomma: 1 },
  { feld: 'sauerstoffMgL', name: 'DO', einheit: 'mg/L', nachkomma: 1 },
  { feld: 'orpMv', name: 'ORP', einheit: 'mV', nachkomma: 0 },
]

/** „+0,03", „−0,48", „±0" — oder null, wenn eine Seite fehlt. */
export function aenderung(vorher: number | null, nachher: number | null, nachkomma: number): { text: string; richtung: 'hoch' | 'runter' | 'gleich' } | null {
  if (vorher == null || nachher == null) return null
  const gerundet = Number((nachher - vorher).toFixed(nachkomma))
  if (gerundet === 0) return { text: '±0', richtung: 'gleich' }
  return {
    text: `${gerundet > 0 ? '+' : '−'}${zahl(Math.abs(gerundet), nachkomma)}`,
    richtung: gerundet > 0 ? 'hoch' : 'runter',
  }
}

/** „von 1,74 auf 1,60" — die Werte eines Sprungs in deutscher Schreibung. */
export function sprungSatz(b: TagebuchSprungDto): string {
  const einheit = b.einheit ? ` ${b.einheit}` : ''
  return `${b.name} von ${zahl(b.vorher, b.nachkomma)} auf ${zahl(b.nachher, b.nachkomma)}${einheit} (${b.beginnUhrzeit}–${b.endeUhrzeit} Uhr)`
}

/** Ein Strich in den Kurven: blau für Wasser, gelb für Notiz und Auffälliges. */
export type Marke = { minute: number; farbe: 'blau' | 'gelb' }

export function markenFuer(ereignisse: TagebuchEreignisDto[]): Marke[] {
  const marken: Marke[] = []
  for (const e of ereignisse) {
    if (e.art === 'wechsel' || e.art === 'addback' || e.art === 'dosierung') marken.push({ minute: e.minute, farbe: 'blau' })
    else if (e.art === 'auffaellig') marken.push({ minute: e.minute, farbe: 'gelb' })
    // Automatische Einträge (CO₂-Tagesbilanz) sind kein Ereignis am Becken.
    else if (e.art === 'notiz' && !e.notiz?.automatisch) marken.push({ minute: e.minute, farbe: 'gelb' })
  }
  return marken
}

/** „Samstag, 03.10." */
export function tagTitel(datum: string, wochentag: string): string {
  const [, monat, tag] = datum.split('-')
  return `${wochentag}, ${tag}.${monat}.`
}

