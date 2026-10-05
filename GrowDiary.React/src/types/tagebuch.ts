import type { PhotoAssetDto } from './grow'

/* Das Grow-Tagebuch (A-006) — Verträge aus GrowDiary.Web/Api/Contracts/TagebuchContracts.cs. */

export type TagebuchArt = 'messung' | 'wechsel' | 'addback' | 'dosierung' | 'notiz' | 'meilenstein' | 'foto' | 'verbrauch' | 'auffaellig'

export interface TagebuchWerteDto {
  ph: number | null
  ec: number | null
  wasserC: number | null
  luftC: number | null
  feuchteProzent: number | null
  co2Ppm: number | null
  orpMv: number | null
  sauerstoffMgL: number | null
  fuellstandL: number | null
  ppfd: number | null
}

export interface TagebuchAbgleichDto {
  name: string
  hand: number
  sensor: number
  toleranz: number
  nachkomma: number
  passt: boolean
}

export interface TagebuchMessungDto {
  id: number
  herkunft: 'sensor' | 'hand' | 'import'
  loesungswechsel: boolean
  werte: TagebuchWerteDto
  abgleich: TagebuchAbgleichDto[]
  notiz: string | null
}

export interface TagebuchNotizDto {
  id: number
  entryType: string
  titel: string | null
  text: string | null
  occurredAtUtc: string
  automatisch: boolean
}

export interface TagebuchWechselDto {
  changeoutId: number
  komplett: boolean
  liter: number | null
  prozent: number | null
  /** Roh: Tap · RO · Mixed — übersetzt in tagebuch-modell.ts. */
  wasser: string | null
  wasserEc: number | null
  vorher: TagebuchWerteDto
  nachher: TagebuchWerteDto
  messungId: number | null
  notiz: string | null
  journal: TagebuchNotizDto | null
}

export interface TagebuchAddbackDto {
  id: number
  /** Roh: Addback · TopOff · Correction. */
  art: string
  literDazu: number | null
  ecVorher: number | null
  ecNachher: number | null
  phVorher: number | null
  phNachher: number | null
  wasser: string | null
  notiz: string | null
}

export interface TagebuchDosisDto {
  id: number
  pumpe: string
  ml: number
  /** „pH", „EC" — null bei eigenem Mittel. */
  messgroesse: string | null
  vorher: number | null
  nachher: number | null
  automatisch: boolean
}

export interface TagebuchPostenDto {
  name: string
  menge: number
  einheit: string
}

export interface TagebuchSprungDto {
  id: number
  messgroesse: string
  name: string
  einheit: string | null
  nachkomma: number
  vorher: number
  nachher: number
  beginnUtc: string
  endeUtc: string
  beginnUhrzeit: string
  endeUhrzeit: string
  /** yyyy-MM-ddTHH:mm in Ortszeit der Anlage — für Felder, die der Server als Ortszeit liest. */
  beginnOrtszeit: string
  endeOrtszeit: string
  dauerMinuten: number
  regel: string
}

export interface TagebuchEreignisDto {
  schluessel: string
  art: TagebuchArt
  zeitpunktUtc: string
  uhrzeit: string
  minute: number
  titel: string
  wasser: boolean
  messung: TagebuchMessungDto | null
  wechsel: TagebuchWechselDto | null
  addback: TagebuchAddbackDto | null
  dosis: TagebuchDosisDto | null
  notiz: TagebuchNotizDto | null
  auffaellig: { befunde: TagebuchSprungDto[] } | null
  posten: TagebuchPostenDto[]
  fotos: PhotoAssetDto[]
}

export interface TagebuchTagDto {
  datum: string
  wochentag: string
  phase: string | null
  ereignisse: TagebuchEreignisDto[]
  auffaellig: number
}

export interface TagebuchSeiteDto {
  growId: number
  zeltId: number | null
  tage: TagebuchTagDto[]
  aeltereAb: string | null
  rohdatenAb: string | null
}

export interface TagebuchKurveDto {
  schluessel: string
  name: string
  einheit: string | null
  nachkomma: number
  min: number | null
  median: number | null
  max: number | null
  punkte: Array<{ minute: number; wert: number }>
}

export interface TagebuchKurvenDto {
  datum: string
  aufloesung: 'roh' | 'tag' | 'keine'
  kurven: TagebuchKurveDto[]
  licht: Array<{ von: number; bis: number }>
  lichtQuelle: 'sensor' | 'plan' | 'keine'
}
