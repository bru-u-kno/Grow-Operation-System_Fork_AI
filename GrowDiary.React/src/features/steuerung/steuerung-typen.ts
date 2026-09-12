/**
 * Fork AI (forkai.20): Die Typen der Steuerungs-Seite.
 *
 * Sie spiegeln die DTOs aus `SteuerungApiController` — eine Datei, damit die
 * Seite und spätere Steuerungen (Entfeuchter, Chiller, Abluft, Licht) dieselbe
 * Form teilen und nicht jede ihre eigene Abschrift mitbringt.
 */

/** Eine Zeile auf der Übersicht — je Steuerung eine. */
export type SteuerungModul = {
  kennung: string
  titel: string
  /** `an` | `warn` | `aus` — färbt den Punkt vor dem Titel. */
  status: string
  kurz: string
  wert: string
  unterzeile: string
  hatDetail: boolean
}

export type SteuerungUebersicht = {
  haErreichbar: boolean
  module: SteuerungModul[]
  standUtc: string
}

export type Co2Einstellungen = {
  zielQuelle: 'fest' | 'plan'
  zielWarmPpm: number
  zielMittelPpm: number
  zielKuehlPpm: number
  anteilWarmProzent: number
  anteilMittelProzent: number
  anteilKuehlProzent: number
  hysteresePpm: number
  impulsMinSekunden: number
  impulsMaxSekunden: number
  wartezeitSekunden: number
  maxImpulseJeZyklus: number
  autokalibrierung: boolean
  zeltvolumenM3: number
  rhObergrenzeProzent: number
  klimaHystereseProzent: number
  canopyObergrenzeC: number
  t6StufeNormal: number
  t6StufeDosierung: number
  t6StufeTief: number
  t6TiefMaxTempC: number
  abluftDrosseln: boolean
  startNachLichtAnMinuten: number
  endeVorLichtAusMinuten: number
  kostenArtikelId: number | null
  journalBuchen: boolean
  automatikAktiv: boolean
}

export type Co2Live = {
  haErreichbar: boolean
  co2Ppm: number | null
  zielPpm: number | null
  zielQuelle: string
  planPpm: number | null
  planHerkunft: string | null
  zielWarm: number
  zielMittel: number
  zielKuehl: number
  hysteresePpm: number
  nachschubUnterPpm: number | null
  bedarf: boolean | null
  klimaOk: boolean | null
  ventilOffen: boolean | null
  automatikAn: boolean | null
  lichtAn: boolean | null
  t6Stufe: number | null
  canopyC: number | null
  rhProzent: number | null
  vpd: number | null
  impulseHeute: number | null
  grammProSekunde: number | null
  letzteMessungGps: number | null
  flascheRestKg: number | null
  impulsBedarfSekunden: number | null
  letzterImpuls: string | null
}

export type Co2Tag = {
  datum: string
  impulse: number
  ventilSekunden: number
  gramm: number
  zielErreichtUm: string | null
  abgeschlossen: boolean
  imJournal: boolean
  inKosten: boolean
  flaschenwechsel: boolean
}

export type KostenArtikelKurz = { id: number; name: string; einheit: string }

export type Co2Seite = {
  einstellungen: Co2Einstellungen
  live: Co2Live
  tage: Co2Tag[]
  artikel: KostenArtikelKurz[]
  growName: string | null
  phase: string | null
  planPpm: number | null
  planHerkunft: string | null
  haAngenommen: boolean | null
  geraeteZugeordnet: number
  geraeteGesamt: number
}

/** Die Reiter der CO₂-Seite, in der Reihenfolge des freigegebenen Entwurfs. */
export type Co2Reiter = 'ziel' | 'dosierung' | 'klima' | 'zeiten' | 'heute'
export const CO2_REITER: Array<{ value: Co2Reiter; label: string }> = [
  { value: 'ziel', label: 'Ziel' },
  { value: 'dosierung', label: 'Dosierung' },
  { value: 'klima', label: 'Klima' },
  { value: 'zeiten', label: 'Zeiten' },
  { value: 'heute', label: 'Heute' },
]

/**
 * Die drei wirksamen Ziele — dieselbe Rechnung wie im Backend.
 *
 * Sie steht hier ein zweites Mal, weil die Seite beim Tippen zeigen soll, was
 * herauskommt, ohne für jede Ziffer zu speichern. Backend bleibt die Wahrheit:
 * gespeichert wird, was von dort zurückkommt.
 */
export function wirksameZiele(e: Co2Einstellungen, planPpm: number | null): { warm: number; mittel: number; kuehl: number } {
  if (e.zielQuelle === 'plan' && planPpm != null) {
    const runden = (v: number) => Math.round((v / 10)) * 10
    return {
      warm: runden((planPpm * e.anteilWarmProzent) / 100),
      mittel: runden((planPpm * e.anteilMittelProzent) / 100),
      kuehl: runden((planPpm * e.anteilKuehlProzent) / 100),
    }
  }
  return { warm: e.zielWarmPpm, mittel: e.zielMittelPpm, kuehl: e.zielKuehlPpm }
}

/** Sekunden als Minuten, wie sie in der Tagesliste stehen. */
export function minuten(sekunden: number): string {
  return `${Math.round(sekunden / 60)} min`
}

/**
 * Fork AI (forkai.21): Geräte-Zuordnung. Eine Rolle beschreibt, WAS gebraucht
 * wird; `eingetragen` ist, was im Feld steht (Entity-ID oder `@Name`),
 * `entityId` das, worauf es am Ende hinausläuft.
 */
export type GeraetZeile = {
  rolle: string
  label: string
  gruppe: string
  einheit: string | null
  hinweis: string | null
  pflicht: boolean
  domains: string[]
  vorgabe: string
  eingetragen: string
  entityId: string | null
  livewert: string | null
  gefunden: boolean
}

export type GeraeteModul = {
  modul: string
  titel: string
  zeilen: GeraetZeile[]
}

export type EigenesGeraet = {
  name: string
  entityId: string
  livewert: string | null
  gefunden: boolean
}

export type GeraeteSeite = {
  haErreichbar: boolean
  module: GeraeteModul[]
  eigene: EigenesGeraet[]
}

// --------------------------------------------------- Bestand (forkai.45)

/** Wie es um ein einzelnes Bauteil steht — Spiegel von `SteuerungBestandService.Stand`. */
export type BauteilStand = {
  entityId: string
  name: string
  art: string
  zweck: string
  /** `Da` | `Stumm` | `Fehlt` | `Entfaellt` */
  stand: string
  pflicht: boolean
  ohneDas: string | null
}

export type Bestandsaufnahme = {
  haErreichbar: boolean
  eingerichtet: boolean
  da: number
  fehlt: number
  entfaellt: number
  fehlendeRollen: string[]
  ausgefalleneFunktionen: string[]
  bauteile: BauteilStand[]
}
