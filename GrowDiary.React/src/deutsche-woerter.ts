import type {
  Ankerphase, Anzuchtart, GrowEntryPoint, GrowStage, GrowStatus, HydroSetupLayoutType, PhotoTag,
  HardwareItemStatus, PlantRole, PlantStatus, ReservoirPosition, SeedKind, SeedType,
  StartMaterial, TentType, ValueOrigin,
} from './types'
import type { MotherHealthStatus, QuarantineResult, SetupStatus, SetupType } from './types/shared'

/**
 * Deutsche Namen für die Enum-Werte, die auf dem Bildschirm landen.
 *
 * <b>Wozu.</b> In Auswahlfeldern standen die Entwickler-Bezeichner roh da:
 * „Seedling", „Overview", „HomeAssistant". Dieselbe Klasse Fehler wie die 65
 * rohen Symptom-Schlüssel im Wissen und wie „Ec" statt „EC" in der Diagnose —
 * nur an einer Stelle, an der man sie auch noch anklicken muss.
 *
 * <b>Warum hier und nicht je Seite.</b> Die Listen standen viermal im
 * Quelltext: zweimal `GrowStage` und zweimal `PhotoTag`, dazu acht fest
 * getippte `<option>`-Zeilen im Messformular, die keine Tabelle je erwischt
 * hätte. Vier Kopien einer Liste sind vier Gelegenheiten, eine zu vergessen.
 *
 * <b>Unbekanntes wird durchgereicht</b>, nicht verschluckt: ein englisches Wort
 * ist besser als ein leeres Feld. Die Zählung in
 * `deutsche-woerter.node.test.ts` sorgt dafür, dass es dabei nicht bleibt.
 */

/** Die Phasen eines Laufs, in ihrer natürlichen Reihenfolge. */
export const PHASEN: GrowStage[] = ['Seedling', 'Clone', 'Veg', 'Transition', 'Flower', 'Finish', 'Dry', 'Cure']

const PHASEN_NAMEN: Record<GrowStage, string> = {
  Seedling: 'Sämling',
  Clone: 'Steckling',
  Veg: 'Wachstum',
  Transition: 'Übergang',
  Flower: 'Blüte',
  Finish: 'Finish',
  Dry: 'Trocknen',
  Cure: 'Aushärten',
}

/** Die Kennzeichnungen für Fotos. */
export const FOTO_TAGS: PhotoTag[] = ['Overview', 'Canopy', 'Leaf', 'Root', 'Training', 'Flower', 'Problem', 'Comparison', 'Other']

const FOTO_NAMEN: Record<PhotoTag, string> = {
  Overview: 'Übersicht',
  Canopy: 'Blätterdach',
  Leaf: 'Blatt',
  Root: 'Wurzeln',
  Training: 'Training',
  Flower: 'Blüte',
  Problem: 'Problem',
  Comparison: 'Vergleich',
  Other: 'Sonstiges',
}

/**
 * Woher ein Wert stammt.
 *
 * <b>Achtung bei der Beschriftung.</b> `HomeAssistant` heißt „aus Home
 * Assistant" und NICHT „von der Automatik erzeugt": das Feld lässt sich im
 * Bearbeiten-Formular von Hand auf diesen Wert setzen. Dass in der heutigen
 * Datenbank beides zusammenfällt, ist ein Zufall der Daten.
 */
const HERKUNFT_NAMEN: Record<ValueOrigin, string> = {
  Manual: 'von Hand',
  HomeAssistant: 'aus Home Assistant',
  Imported: 'importiert',
  Derived: 'berechnet',
}

/** „Veg" wird „Wachstum". Unbekanntes bleibt, wie es ist. */
export function phaseName(wert: string | null | undefined): string {
  if (!wert) return ''
  return PHASEN_NAMEN[wert as GrowStage] ?? wert
}

/**
 * Die Phasen des Phasenankers. Wo es eine GrowStage gibt, heißt die Phase
 * wie dort — „Veg" ist auch hier „Wachstum".
 */
const ANKERPHASEN_NAMEN: Record<Ankerphase, string> = {
  Anzucht: 'Anzucht',
  Veg: PHASEN_NAMEN.Veg,
  Uebergang: PHASEN_NAMEN.Transition,
  Bluete: PHASEN_NAMEN.Flower,
  Finish: PHASEN_NAMEN.Finish,
  Ende: 'Beendet',
}

/** Was in der Anzucht gerade passiert. */
const ANZUCHT_NAMEN: Record<Anzuchtart, string> = {
  Keimung: 'Keimung',
  Saemling: PHASEN_NAMEN.Seedling,
  Bewurzelung: 'Bewurzelung',
}

/** „Uebergang" wird „Übergang". */
export function ankerphaseName(wert: string | null | undefined): string {
  if (!wert) return ''
  return ANKERPHASEN_NAMEN[wert as Ankerphase] ?? wert
}

/** „Saemling" wird „Sämling". */
export function anzuchtName(wert: string | null | undefined): string {
  if (!wert) return ''
  return ANZUCHT_NAMEN[wert as Anzuchtart] ?? wert
}

/** „Overview" wird „Übersicht". */
export function fotoTagName(wert: string | null | undefined): string {
  if (!wert) return ''
  return FOTO_NAMEN[wert as PhotoTag] ?? wert
}

/** „HomeAssistant" wird „aus Home Assistant". */
export function herkunftName(wert: string | null | undefined): string {
  if (!wert) return ''
  return HERKUNFT_NAMEN[wert as ValueOrigin] ?? wert
}

/** Der Zustand eines Laufs. */
const STATUS_NAMEN: Record<GrowStatus, string> = {
  Planning: 'geplant',
  Running: 'läuft',
  Completed: 'abgeschlossen',
  Aborted: 'abgebrochen',
}

/** „Running" wird „läuft". */
export function statusName(wert: string | null | undefined): string {
  if (!wert) return ''
  return STATUS_NAMEN[wert as GrowStatus] ?? wert
}

/** Woraus der Lauf gestartet ist. */
const SAMEN_NAMEN: Record<SeedType, string> = {
  Feminized: 'feminisiert',
  Autoflower: 'Autoflower',
  Regular: 'regulär',
}

/** Samen oder Steckling. */
const MATERIAL_NAMEN: Record<StartMaterial, string> = {
  Seed: 'Samen',
  Clone: 'Steckling',
}

/** Wo der Lauf eingestiegen ist. */
const EINSTIEG_NAMEN: Record<GrowEntryPoint, string> = {
  Germination: 'Keimung',
  Seedling: 'Sämling',
  Veg: 'Wachstum',
  Flower: 'Blüte',
  Flush: 'Spülen',
}

/** Wie die Töpfe stehen. */
const AUFSTELLUNG_NAMEN: Record<HydroSetupLayoutType, string> = {
  SingleBucket: 'Einzeleimer',
  Row: 'Reihe',
  Grid2x2: '2×2-Raster',
  Grid2x3: '2×3-Raster',
  Grid2x4: '2×4-Raster',
  Custom: 'eigene Anordnung',
}

/** Die Rolle einer Pflanze. */
const PFLANZENROLLE_NAMEN: Record<PlantRole, string> = {
  Production: 'Produktion',
  Mother: 'Mutter',
  Clone: 'Klon',
  Quarantine: 'Quarantäne',
}

/** „Mother" wird „Mutter". */
export function pflanzenRolleName(wert: string | null | undefined): string {
  if (!wert) return ''
  return PFLANZENROLLE_NAMEN[wert as PlantRole] ?? wert
}

/** Der Zustand einer Pflanze. */
const PFLANZENSTATUS_NAMEN: Record<PlantStatus, string> = {
  Planned: 'geplant',
  Active: 'aktiv',
  Archived: 'archiviert',
  Culled: 'aussortiert',
  Harvested: 'geerntet',
}

/** „Culled" wird „aussortiert". */
export function pflanzenStatusName(wert: string | null | undefined): string {
  if (!wert) return ''
  return PFLANZENSTATUS_NAMEN[wert as PlantStatus] ?? wert
}

/** Der Zustand eines Geräts. */
const GERAETESTATUS_NAMEN: Record<HardwareItemStatus, string> = {
  Active: 'in Betrieb',
  MaintenanceDue: 'Wartung fällig',
  Offline: 'offline',
  Retired: 'ausgemustert',
}

/** „MaintenanceDue" wird „Wartung fällig". */
export function geraeteStatusName(wert: string | null | undefined): string {
  if (!wert) return ''
  return GERAETESTATUS_NAMEN[wert as HardwareItemStatus] ?? wert
}

/**
 * Der Samen-Typ in der Sorten-Bibliothek.
 *
 * **Dasselbe wie `SeedType` am Grow, nur anders benannt.** Am Grow heisst der
 * Wert `Autoflower`, in der Bibliothek `Automatic` — beide meinen dieselbe
 * Pflanze. Auf dem Schirm steht deshalb beide Male **Autoflower**: ein Ding,
 * ein Wort. Vorher hatte `StrainsPage` eine eigene Tabelle mit der
 * Beschriftung „Automatic", also die vierte Kopie einer Uebersetzung.
 */
const SAMENART_NAMEN: Record<SeedKind, string> = {
  Feminized: 'feminisiert',
  Automatic: 'Autoflower',
  Regular: 'regulär',
}

/** „Automatic" wird „Autoflower" — dasselbe Wort wie am Grow. */
export function samenartName(wert: string | null | undefined): string {
  if (!wert) return ''
  return SAMENART_NAMEN[wert as SeedKind] ?? wert
}

/** Wo der Tank steht. */
const TANKPLATZ_NAMEN: Record<ReservoirPosition, string> = {
  None: 'kein Tank',
  Left: 'links',
  Right: 'rechts',
  Top: 'oben',
  Bottom: 'unten',
  External: 'ausserhalb',
}

/** „Feminized" wird „feminisiert". */
export function samenName(wert: string | null | undefined): string {
  if (!wert) return ''
  return SAMEN_NAMEN[wert as SeedType] ?? wert
}

/** „Seed" wird „Samen". */
export function materialName(wert: string | null | undefined): string {
  if (!wert) return ''
  return MATERIAL_NAMEN[wert as StartMaterial] ?? wert
}

/** „Germination" wird „Keimung". */
export function einstiegName(wert: string | null | undefined): string {
  if (!wert) return ''
  return EINSTIEG_NAMEN[wert as GrowEntryPoint] ?? wert
}

/** „Grid2x2" wird „2×2-Raster". */
export function aufstellungName(wert: string | null | undefined): string {
  if (!wert) return ''
  return AUFSTELLUNG_NAMEN[wert as HydroSetupLayoutType] ?? wert
}

/**
 * Wozu ein Zelt da ist.
 *
 * **Diese Tabelle stand zweimal im Code** — einmal in `live-model.ts`, einmal
 * in `TentDetailPage.tsx`, beide Male als Kette von Fragezeichen-Operatoren.
 * Am dritten Ort („Grow starten") stand gar keine, deshalb las man dort
 * „Production". Zwei Wahrheiten laufen auseinander; drei erst recht.
 */
const ZELTZWECK_NAMEN: Record<TentType, string> = {
  Production: 'Blüte / Run',
  Mother: 'Mutter',
  Quarantine: 'Quarantäne',
  Propagation: 'Anzucht',
  MultiPurpose: 'Mehrzweck',
}

/** „Production" wird „Blüte / Run". */
export function zeltZweckName(wert: string | null | undefined): string {
  if (!wert) return ''
  return ZELTZWECK_NAMEN[wert as TentType] ?? wert
}

/** „External" wird „ausserhalb". */
export function tankplatzName(wert: string | null | undefined): string {
  if (!wert) return ''
  return TANKPLATZ_NAMEN[wert as ReservoirPosition] ?? wert
}

/**
 * Stufen — Dringlichkeit, Schwere, Kritikalität.
 *
 * <b>Der Anlass (02.09.2026).</b> Diese Tabelle stand als <code>severityLabels</code>
 * in <code>utils.ts</code>: eine <b>zwölfte</b> Übersetzungstabelle ausserhalb
 * dieser Datei, und damit ausserhalb jeder Zählung. Genau dort ist „Info"
 * schon einmal roh auf den Schirm gefallen — zwischen lauter deutschen Stufen
 * stand ein englisches Wort.
 *
 * Sie bedient vier Typen auf einmal: <code>DeviationSeverity</code>,
 * <code>RiskEventSeverity</code>, <code>TaskPriority</code> und
 * <code>HardwareItemCriticality</code>. Alle vier werden jetzt gezählt.
 */
const STUFEN_NAMEN: Record<string, string> = {
  Critical: 'Kritisch',
  Warning: 'Warnung',
  Info: 'Hinweis',
  High: 'Hoch',
  Medium: 'Mittel',
  Low: 'Niedrig',
  Normal: 'Normal',
}

/** „Warning" wird „Warnung", „High" wird „Hoch". */
export function stufenName(wert: string | null | undefined): string {
  if (!wert) return '–'
  return STUFEN_NAMEN[wert] ?? wert
}

/** Nur für die Zählung: was übersetzt ist. */
/**
 * Bereiche im Zelt (Mutter, Quarantäne …) — bis zum 01.10.2026 stand auf der
 * Zeltseite „Mother", „Active", „Pending" roh da.
 *
 * Nachgeschlagen wird ohne Rücksicht auf Groß-/Kleinschreibung: der
 * `SetupsApiController` nimmt etwa „watch" an und speichert es so.
 */
function ohneSchreibweise(tabelle: Record<string, string>, wert: string): string {
  const schluessel = Object.keys(tabelle).find((k) => k.toLowerCase() === wert.toLowerCase())
  return schluessel ? tabelle[schluessel] : wert
}

const BEREICHSART_NAMEN: Record<SetupType, string> = {
  Production: 'Produktion',
  Mother: 'Mutter',
  Quarantine: 'Quarantäne',
  Propagation: 'Anzucht',
}

/** „Mother" wird „Mutter". */
export function bereichsartName(wert: string | null | undefined): string {
  if (!wert) return ''
  return ohneSchreibweise(BEREICHSART_NAMEN, wert)
}

const BEREICHSSTATUS_NAMEN: Record<SetupStatus, string> = {
  Planning: 'geplant',
  Active: 'aktiv',
  Archived: 'archiviert',
}

/** „Active" wird „aktiv". */
export function bereichsstatusName(wert: string | null | undefined): string {
  if (!wert) return ''
  return ohneSchreibweise(BEREICHSSTATUS_NAMEN, wert)
}

const MUTTERZUSTAND_NAMEN: Record<MotherHealthStatus, string> = {
  Stable: 'stabil',
  Watch: 'beobachten',
  Critical: 'kritisch',
}

/** „Watch" wird „beobachten". */
export function mutterzustandName(wert: string | null | undefined): string {
  if (!wert) return ''
  return ohneSchreibweise(MUTTERZUSTAND_NAMEN, wert)
}

const QUARANTAENE_NAMEN: Record<QuarantineResult, string> = {
  Pending: 'offen',
  Cleared: 'freigegeben',
  Rejected: 'abgelehnt',
}

/** „Cleared" wird „freigegeben". */
export function quarantaeneErgebnisName(wert: string | null | undefined): string {
  if (!wert) return ''
  return ohneSchreibweise(QUARANTAENE_NAMEN, wert)
}

export const WOERTERBUECHER = {
  stufe: STUFEN_NAMEN,
  phase: PHASEN_NAMEN,
  fotoTag: FOTO_NAMEN,
  herkunft: HERKUNFT_NAMEN,
  status: STATUS_NAMEN,
  samen: SAMEN_NAMEN,
  material: MATERIAL_NAMEN,
  einstieg: EINSTIEG_NAMEN,
  ankerphase: ANKERPHASEN_NAMEN,
  anzucht: ANZUCHT_NAMEN,
  aufstellung: AUFSTELLUNG_NAMEN,
  tankplatz: TANKPLATZ_NAMEN,
  zeltZweck: ZELTZWECK_NAMEN,
  samenart: SAMENART_NAMEN,
  geraeteStatus: GERAETESTATUS_NAMEN,
  pflanzenRolle: PFLANZENROLLE_NAMEN,
  pflanzenStatus: PFLANZENSTATUS_NAMEN,
  bereichsart: BEREICHSART_NAMEN,
  bereichsstatus: BEREICHSSTATUS_NAMEN,
  mutterzustand: MUTTERZUSTAND_NAMEN,
  quarantaene: QUARANTAENE_NAMEN,
}

/**
 * Zustände, wie Home Assistant sie liefert — allgemein und für Klimageräte
 * (`climate.*`: `cool`, `heat_cool` …).
 *
 * Bis zum 02.10.2026 standen sie roh auf der Steuerungsseite: bei der
 * Probeschaltung „unavailable", beim Kühler „heat_cool". Zahlen gehören nicht
 * hierher, die formatiert `haWert()`.
 */
const HA_ZUSTAND_NAMEN: Record<string, string> = {
  on: 'an',
  off: 'aus',
  unavailable: 'nicht erreichbar',
  unknown: 'unbekannt',
  cool: 'kühlt',
  heat: 'heizt',
  heat_cool: 'heizt und kühlt',
  auto: 'automatisch',
  dry: 'entfeuchtet',
  fan_only: 'nur Lüfter',
  idle: 'bereit',
  cooling: 'kühlt',
  heating: 'heizt',
}

/** „unavailable" wird „nicht erreichbar", „heat_cool" wird „heizt und kühlt". */
export function haZustandName(wert: string | null | undefined): string {
  if (!wert) return '–'
  return ohneSchreibweise(HA_ZUSTAND_NAMEN, wert.trim())
}
