import type { MotherHealthStatus, PlantRole, PlantStatus, QuarantineResult, SetupStatus, SetupType, StrainDominance, WaterSource } from './shared'
import type { JournalEntryDto, MeasurementDto } from './grow'

export type TentType = 'Production' | 'Mother' | 'Quarantine' | 'Propagation' | 'MultiPurpose'
export type TentStatus = 'Active' | 'Archived'
export interface SetupDto {
  id: number
  tentId: number
  name: string
  setupType: SetupType
  status: SetupStatus
  notes: string | null
  cloneCounterTotal: number | null
  lastCloneCutAt: string | null
  motherHealthStatus: MotherHealthStatus | null
  quarantineStartedAt: string | null
  quarantinePlannedEndAt: string | null
  quarantineResult: QuarantineResult | null
  createdAtUtc: string
  updatedAtUtc: string
}

export interface CreateSetupRequest {
  tentId: number
  name: string
  setupType: SetupType
  notes?: string | null
  cloneCounterTotal?: number | null
  lastCloneCutAt?: string | null
  motherHealthStatus?: MotherHealthStatus | null
  quarantineStartedAt?: string | null
  quarantinePlannedEndAt?: string | null
  quarantineResult?: QuarantineResult | null
}

export interface UpdateSetupRequest {
  name: string
  status: SetupStatus
  notes?: string | null
  cloneCounterTotal?: number | null
  lastCloneCutAt?: string | null
  motherHealthStatus?: MotherHealthStatus | null
  quarantineStartedAt?: string | null
  quarantinePlannedEndAt?: string | null
  quarantineResult?: QuarantineResult | null
}

export type SeedKind = 'Feminized' | 'Automatic' | 'Regular'

export interface StrainDto {
  seedKind: SeedKind | null
  thcPercent: number | null
  cbdPercent: number | null
  sativaPercent: number | null
  taste: string | null
  effect: string | null
  aroma: string | null
  yieldIndoorGm2: number | null
  heightIndoorCm: number | null
  id: number
  name: string
  breeder: string | null
  dominance: StrainDominance
  flowerWeeksMin: number | null
  flowerWeeksMax: number | null
  notes: string | null
  nutrientDemandFactor: number | null
  stretchFactor: number | null
  vpdPreferenceShift: number | null
  createdAtUtc: string
  updatedAtUtc: string
}

export interface CreateStrainRequest {
  name: string
  breeder?: string | null
  dominance: StrainDominance
  flowerWeeksMin?: number | null
  flowerWeeksMax?: number | null
  notes?: string | null
  nutrientDemandFactor?: number | null
  stretchFactor?: number | null
  vpdPreferenceShift?: number | null
  /* Zuechter-Felder aus beta.37 — die Leseseite (StrainDto) kannte sie schon,
     die Schreibseite lief untypisiert daran vorbei. */
  seedKind?: SeedKind | null
  thcPercent?: number | null
  cbdPercent?: number | null
  sativaPercent?: number | null
  taste?: string | null
  effect?: string | null
  aroma?: string | null
  yieldIndoorGm2?: number | null
  heightIndoorCm?: number | null
}

export type UpdateStrainRequest = CreateStrainRequest

export interface PlantInstanceDto {
  id: number
  strainId: number | null
  setupId: number | null
  growId: number | null
  /** Der Topf im System ab 1 — die Nummer aus der Draufsicht. */
  siteIndex: number | null
  parentPlantId: number | null
  label: string
  plantRole: PlantRole
  plantStatus: PlantStatus
  phenoLabel: string | null
  startedAt: string | null
  endedAt: string | null
  notes: string | null
  strainName: string | null
  createdAtUtc: string
  updatedAtUtc: string
}

export interface CreatePlantInstanceRequest {
  strainId?: number | null
  setupId?: number | null
  growId?: number | null
  parentPlantId?: number | null
  label: string
  plantRole: PlantRole
  plantStatus: PlantStatus
  phenoLabel?: string | null
  startedAt?: string | null
  endedAt?: string | null
  notes?: string | null
}

export type UpdatePlantInstanceRequest = CreatePlantInstanceRequest

export interface CreateCloneFromMotherRequest {
  motherPlantId: number
  targetSetupId?: number | null
  label: string
  phenoLabel?: string | null
  notes?: string | null
  strainId?: number | null
  cutAt?: string | null
}

export type QuarantineDecision = 'Cleared' | 'Rejected'

export interface DecideQuarantinePlantRequest {
  plantId: number
  decision: QuarantineDecision
  targetSetupId?: number | null
  targetGrowId?: number | null
  decidedAt?: string | null
  notes?: string | null
}

export type ChangeoutKind = 'Partial' | 'Full'

export interface ChangeoutDto {
  id: number
  growId: number
  hydroSetupId: number | null
  kind: ChangeoutKind
  performedAtUtc: string
  volumeChangedLiters: number | null
  percentChanged: number | null
  ecBefore: number | null
  ecAfter: number | null
  phBefore: number | null
  phAfter: number | null
  notes: string | null
  createdAtUtc: string
  /** A-006: ob der Wechsel die Erinnerung neu startet; Altdaten true. */
  erinnerungNeuStarten?: boolean
}

/**
 * Wie es um den Wasserwechsel steht — gerechnet im Backend, nicht hier.
 *
 * Die Zahl ,vor N Tagen' steht auf der Seite, in der Aufgabe, in der
 * Risiko-Karte und im Trend. Sie kommt aus EINER Quelle
 * (`WasserwechselStandService`), sonst laufen die vier auseinander.
 */
export interface WasserwechselStandDto {
  zuletztUtc: string | null
  tageSeit: number | null
  intervallTage: number
  warnungAbTagen: number
  kritischAbTagen: number
  /** unbekannt · frisch · faellig · ueberfaellig */
  zustand: string
}

export interface CreateChangeoutRequest {
  kind: ChangeoutKind
  performedAtUtc?: string | null
  volumeChangedLiters?: number | null
  percentChanged?: number | null
  ecBefore?: number | null
  ecAfter?: number | null
  phBefore?: number | null
  phAfter?: number | null
  notes?: string | null
}

/* ---------------------------------------------------------------------------
 * A-006: der Wasserwechsel als ein Vorgang. Verträge aus
 * `Api/Contracts/WasserwechselVorgangContracts.cs` und `Services/MischplanVorschlag.cs`.
 * ------------------------------------------------------------------------- */

/** Rolle einer Zeile im Mischplan — bestimmt, wie der Vorschlag rechnet. */
export type MischplanRolle = 'Grundduenger' | 'CalMag' | 'Zusatz'

export interface MischplanVorschlagZeile {
  komponente: string
  rolle: MischplanRolle
  mlProLiter: number
  mlProLiterText: string
  vorschlagMl: number
  artikelId: number | null
  artikelName: string | null
  artikelEinheit: string | null
  hinweis: string | null
}

/** Der Plan dieser Woche, auf Liter und Wasser gerechnet — `GET /api/grows/{id}/mixing-plan/vorschlag`. */
export interface MischplanVorschlag {
  programmName: string | null
  spalteLabel: string | null
  anlageLiter: number | null
  liter: number
  wasser: WaterSource
  osmoseAnteil: number
  wasserEcVorschlag: number | null
  wasserEcQuelle: string
  wasserEc: number | null
  ecZielDuenger: number | null
  ecZielGesamt: number | null
  phMin: number | null
  phMax: number | null
  zeilen: MischplanVorschlagZeile[]
  calMagHinweis: string | null
  luecke: string | null
}

export interface SensorWertDto { wert: number; zeitUtc: string }

/** Was die Sensoren kurz vor einem Zeitpunkt zeigten — `GET /api/grows/{id}/wasserwechsel/sensor`. */
export interface WasserwechselSensorDto {
  zeitpunktUtc: string
  fensterMinuten: number
  ec: SensorWertDto | null
  ph: SensorWertDto | null
  wasserTemp: SensorWertDto | null
  hinweis: string | null
}

export interface VorgangMessungRequest {
  zeitpunktLokal?: string | null
  herkunft?: 'Sensor' | 'Hand' | 'gemischt'
  sensorZeitUtc?: string | null
  reservoirEc?: number | null
  reservoirPh?: number | null
  reservoirWaterTempC?: number | null
  dissolvedOxygenMgL?: number | null
  orpMv?: number | null
}

export interface VorgangBuchungRequest {
  artikelId?: number | null
  /** Tap = Leitungswasser, RO = Osmosewasser; nur ohne artikelId. */
  wasser?: 'Tap' | 'RO' | null
  menge: number
}

export interface WasserwechselVorgangRequest {
  zeitpunktLokal: string | null
  art: ChangeoutKind
  liter: number | null
  wasser: WaterSource
  osmoseProzent: number | null
  wasserEcMsCm: number | null
  vorher: VorgangMessungRequest | null
  nachher: VorgangMessungRequest | null
  buchungen: VorgangBuchungRequest[]
  erinnerungNeuStarten: boolean
  notiz: string | null
  tagebuch: { titel: string; text: string } | null
}

export interface VorgangBuchungDto { id: number; artikelId: number; artikelName: string; einheit: string; menge: number }

export interface WasserwechselVorgangDto {
  id: number
  growId: number
  erstelltAmUtc: string
  wechsel: ChangeoutDto | null
  vorher: MeasurementDto | null
  nachher: MeasurementDto | null
  buchungen: VorgangBuchungDto[]
  tagebuch: JournalEntryDto | null
  osmoseProzent: number | null
  vorherHerkunft: string | null
  vorherSensorZeitUtc: string | null
}
