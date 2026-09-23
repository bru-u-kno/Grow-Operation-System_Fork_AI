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
  tiefAktiv: boolean | null
  tiefBisTempC: number | null
  tiefBisRhProzent: number | null
  /** Die Feuchte-Obergrenze, die in HA gerade gilt. */
  rhObergrenzeProzent: number | null
  /** Gesetzt, wenn der Wochenplan die Obergrenze führt (forkai.115) — dann schreibt die CO₂-Seite sie nicht. */
  rhObergrenzeAusPlan: number | null
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

// ------------------------------------------------------------------- Licht

export type LichtEinstellungen = {
  veggieEin: string
  veggieAus: string
  blueteEin: string
  blueteAus: string
  stufe: number
  schreibAbstandMs: number
  verifySekunden: number
  maxWiederholungen: number
  helferSpiegeln: boolean
}

export type LichtLive = {
  haErreichbar: boolean
  /** `Off` | `On` | `Schedule` | … — die Betriebsart am Controller. */
  modus: string | null
  stufe: number | null
  einZeit: string | null
  ausZeit: string | null
  lichtAn: boolean | null
  controllerOnline: boolean | null
  /** `veggie` | `bluete` | null, wenn die Zeiten zu keinem Zeitplan passen. */
  aktivesPreset: string | null
  naechsterWechsel: string | null
  /** Befehle, die der Controller noch nicht übernommen hat. */
  unbestaetigt: string[]
  /** Befehle, bei denen auch die Wiederholungen nichts gebracht haben. */
  fehlgeschlagen: string[]
  standUtc: string
}

export type LichtSeite = {
  einstellungen: LichtEinstellungen
  live: LichtLive
  haAngenommen: boolean | null
  geraeteZugeordnet: number
  geraeteGesamt: number
}

/** Die Modus-Werte des AC-Infinity-Selects. */
export const LICHT_MODI = { aus: 'Off', an: 'On', zeitplan: 'Schedule' } as const

export type LichtReiter = 'betrieb' | 'zeitplan' | 'erweitert'
export const LICHT_REITER: Array<{ value: LichtReiter; label: string }> = [
  { value: 'betrieb', label: 'Betrieb' },
  { value: 'zeitplan', label: 'Zeitplan' },
  { value: 'erweitert', label: 'Erweitert' },
]

// ------------------------------------------------------------------ Zuluft

export type ZuluftEinstellungen = {
  mindestDifferenzGm3: number
  aussentemperaturMinC: number
  stufeMin: number
  stufeMax: number
  mindestlaufzeitMin: number
  mindestpauseMin: number
  automatikAktiv: boolean
  /** Fork AI (forkai.128): darunter pausiert die Zuluft, wieder an ab + 1 °C. */
  zeltTemperaturMinC: number | null
}

export type ZuluftLive = {
  haErreichbar: boolean
  differenzGm3: number | null
  aussenAbsolutGm3: number | null
  kellerAbsolutGm3: number | null
  aussenTempC: number | null
  aussenRhProzent: number | null
  kellerTempC: number | null
  kellerRhProzent: number | null
  bedarf: boolean | null
  zielstufe: number | null
  istStufe: number | null
  portAn: boolean | null
  portOnline: boolean | null
  automatikAn: boolean | null
  /** Restliche Sperrzeit in Minuten; 0 = frei, null = noch nie geschaltet. */
  sperreRestMin: number | null
  letzterWechsel: string | null
  /** Fork AI (forkai.128) */
  zeltTempC: number | null
  /** True, wenn die Außenluft trocknen würde, das Zelt aber zu kalt ist. */
  pauseZeltKalt: boolean | null
}

export type ZuluftSeite = {
  einstellungen: ZuluftEinstellungen
  live: ZuluftLive
  haAngenommen: boolean | null
  /** True, solange die Werte aus den vorhandenen Helfern kommen. */
  ausHomeAssistantUebernommen: boolean
  geraeteZugeordnet: number
  geraeteGesamt: number
}

export type ZuluftReiter = 'regel' | 'luefter' | 'betrieb'

export const ZULUFT_REITER: Array<{ value: ZuluftReiter; label: string }> = [
  { value: 'regel', label: 'Regel' },
  { value: 'luefter', label: 'Lüfter' },
  { value: 'betrieb', label: 'Betrieb' },
]

// ------------------------------------------------------------- Entfeuchter

/** Fork AI (forkai.129): Temperatur max. aus dem Plan (+ Abstand) oder fest. */
export type TempMaxModus = 'plan' | 'fest'

export type EntfeuchterEinstellungen = {
  vpdRegelung: boolean
  hystereseProzent: number
  mindestlaufzeitMin: number
  einschaltverzoegerungMin: number
  wartezeitAussenluftMin: number
  tagbetriebErlauben: boolean
  automatikAktiv: boolean
  tempMaxTagModus: TempMaxModus
  tempMaxTagAbstandK: number
  tempMaxTagFestC: number
  tempMaxNachtModus: TempMaxModus
  tempMaxNachtAbstandK: number
  tempMaxNachtFestC: number
  feuchteEinTag: number
  feuchteAusTag: number
  feuchteEinNacht: number
  feuchteAusNacht: number
}

export type EntfeuchterLive = {
  haErreichbar: boolean
  feuchteProzent: number | null
  tempC: number | null
  vpd: number | null
  tagPhase: boolean | null
  einAktivProzent: number | null
  ausAktivProzent: number | null
  tempMaxAktivC: number | null
  rhObergrenzeProzent: number | null
  /** Höchste mögliche EIN-Schwelle: Plan-Feuchte max. − Klima-Hysterese. */
  deckelProzent: number | null
  vpdUnten: number | null
  vpdOben: number | null
  blattOffsetC: number | null
  planWoche: string | null
  planLuftTagC: number | null
  planLuftNachtC: number | null
  tempMaxTagC: number | null
  tempMaxNachtC: number | null
  co2CanopyGrenzeC: number | null
  portAn: boolean | null
  portOnline: boolean | null
  automatikAn: boolean | null
  /** Läuft die Zuluft UND trocknet die Außenluft? Dann gilt die lange Wartezeit. */
  zuluftVorrang: boolean | null
}

export type EntfeuchterSeite = {
  einstellungen: EntfeuchterEinstellungen
  live: EntfeuchterLive
  haAngenommen: boolean | null
  ausHomeAssistantUebernommen: boolean
  geraeteZugeordnet: number
  geraeteGesamt: number
}

export type EntfeuchterReiter = 'regel' | 'schutz' | 'betrieb'

export const ENTFEUCHTER_REITER: Array<{ value: EntfeuchterReiter; label: string }> = [
  { value: 'regel', label: 'Regel' },
  { value: 'schutz', label: 'Schutz' },
  { value: 'betrieb', label: 'Betrieb' },
]

// ----------------------------------------------------------------- Chiller

export type ChillerEinstellungen = {
  zielTagC: number
  zielNachtC: number
  /** Einschalten ab Ziel plus diesem Abstand, ausschalten beim Ziel (F-030). */
  hystereseK: number
  mindestlaufzeitMin: number
  mindestpauseMin: number
  automatikAktiv: boolean
}

export type ChillerAnsteuerung = 'steckdose' | 'regelbar' | 'beides' | 'keine'

export type ChillerLive = {
  haErreichbar: boolean
  wasserC: number | null
  zielAktivC: number | null
  zielTagC: number | null
  zielNachtC: number | null
  /** True, solange die Lampe brennt — daran hängt, welches Ziel gilt. */
  tagPhase: boolean | null
  kuehlbedarf: boolean | null
  steckdoseAn: boolean | null
  leistungW: number | null
  automatikAn: boolean | null
  waechterAn: boolean | null
  /** Woher das Zielpaar kommt: plan oder hand. */
  zielQuelle: string
  sperreRestMin: number | null
  letzterWechsel: string | null
  einschaltenAbC: number | null
  /** Ausgeschaltet wird beim Ziel, nicht darunter (F-030). */
  ausschaltenBeiC: number | null
  /** steckdose, regelbar, beides oder keine — ergibt sich aus den Rollen. */
  ansteuerung: ChillerAnsteuerung
  kuehlerEntity: string | null
  kuehlerSollC: number | null
  kuehlerZustand: string | null
  /** Gesetzt, wenn Crop Steering dieselbe Steckdose schaltet wie die Regelung. */
  doppelSteuerungEntity: string | null
}

export type ChillerSeite = {
  einstellungen: ChillerEinstellungen
  live: ChillerLive
  haAngenommen: boolean | null
  ausHomeAssistantUebernommen: boolean
  geraeteZugeordnet: number
  geraeteGesamt: number
}

export type ChillerReiter = 'betrieb' | 'schutz'

export const CHILLER_REITER: Array<{ value: ChillerReiter; label: string }> = [
  { value: 'betrieb', label: 'Betrieb' },
  { value: 'schutz', label: 'Schutz' },
]

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
