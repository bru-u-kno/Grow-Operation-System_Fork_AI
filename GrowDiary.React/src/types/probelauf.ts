/**
 * Fork AI (A-010): Probelauf — die Verträge von `GET/POST /api/steuerung/probelauf`.
 * Quelle: `GrowDiary.Web/Api/Contracts/ProbelaufContracts.cs` und `Models/Probelauf.cs`.
 */

export type ProbelaufStatus = 'Laeuft' | 'Nachlauf' | 'Fertig' | 'Abgebrochen' | 'RueckstellungOffen'

export interface ProbelaufModul {
  modul: string
  /** Der zentral gepflegte Titel der Steuerung — nie selbst geschrieben. */
  titel: string
}

export interface ProbelaufGrenzen {
  feuchteMax: number | null
  tempMax: number | null
  vpdMin: number | null
  vpdMax: number | null
}

export interface ProbelaufMesswerte {
  zeitUtc: string
  feuchte: number | null
  temp: number | null
  vpd: number | null
}

export interface ProbelaufMessreihe {
  vorlauf: ProbelaufMesswerte[]
  waehrend: ProbelaufMesswerte[]
  nachlauf: ProbelaufMesswerte[]
}

export interface ProbelaufKennzahl {
  groesse: 'Feuchte' | 'Temperatur' | 'VPD'
  start: number
  spitze: number
  ende: number
  aenderungProMinute: number
  erholungMinuten: number | null
}

export interface ProbelaufAuswertung {
  kennzahlen: ProbelaufKennzahl[]
  hinweise: string[]
  tagPhaseBeiStart: boolean | null
  tagPhaseBeiEnde: boolean | null
}

export interface ProbelaufLauf {
  id: number
  modul: string
  modulTitel: string
  status: ProbelaufStatus
  startUtc: string
  geplantesEndeUtc: string
  eingriffEndeUtc: string | null
  endeUtc: string | null
  restSekunden: number
  abbruchGrund: string | null
  grenzen: ProbelaufGrenzen
  auswertung: ProbelaufAuswertung | null
  empfehlung: string | null
  /** Nur in der Einzelansicht. */
  messreihe: ProbelaufMessreihe | null
}

// ---------------------------------------------------------------- Kenntnisstand (Etappe 2)
// Quelle: `GrowDiary.Web/Models/Kenntnisstand.cs`, `GET /api/steuerung/probelauf/kenntnisstand`.

export type ZielUrteil = 'Unbekannt' | 'Erreichbar' | 'Knapp' | 'Luecke'

export interface ZielPhase {
  zielText: string | null
  anteilProzent: number | null
  urteil: ZielUrteil
  minuten: number
}

export interface ZielZeile {
  groesse: 'Luftfeuchte' | 'Temperatur' | 'VPD'
  tag: ZielPhase
  nacht: ZielPhase
}

export interface WirkungWert {
  groesse: 'Feuchte' | 'Temperatur' | 'VPD'
  proMinute: number
  erholungMinuten: number | null
}

export interface WirkungZeile {
  modul: string
  titel: string
  tag: boolean
  laeufe: number
  werte: WirkungWert[]
}

export interface AbdeckungZeile {
  modul: string
  titel: string
  laeufeTag: number
  laeufeNacht: number
  tagMoeglich: boolean
  nachtMoeglich: boolean
}

export interface NaechsterLauf {
  modul: string
  titel: string
  tag: boolean
  dauerMinuten: number
  begruendung: string
}

export interface Kenntnisstand {
  standUtc: string
  tageBetrachtet: number
  zielabgleich: ZielZeile[]
  wirkung: WirkungZeile[]
  abdeckung: AbdeckungZeile[]
  naechster: NaechsterLauf | null
  hinweise: string[]
}
