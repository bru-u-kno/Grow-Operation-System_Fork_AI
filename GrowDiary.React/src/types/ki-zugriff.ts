import type { KiStufe } from './shared'

/*
 * Zugriff für KI-Assistenten (A-003) — die Verträge aus
 * `GrowDiary.Web/Api/Contracts/KiZugriffContracts.cs`, in camelCase wie über
 * die Leitung. Zeitpunkte kommen als ISO-Text (UTC).
 */

/** Grenzen für Befehle über einen Schlüssel — zusätzlich zu den Grenzen der Geräte. */
export interface KiHoechstwerteDto {
  maxDosisMlJeBefehl: number
  maxSchaltbefehleJeStunde: number
}

export interface KiSchluesselDto {
  id: number
  name: string
  /** Die ersten Zeichen nach `gok_` — nie der ganze Schlüssel. */
  praefix: string
  /** Freigegebene Stufen — frei und mit Rückfrage. */
  stufen: KiStufe[]
  /**
   * Fork AI (A-005, 03.10.2026): Bei diesen Stufen soll der Assistent vorher
   * fragen. Immer eine Teilmenge von `stufen`.
   */
  rueckfrageBei: KiStufe[]
  erstelltAmUtc: string
  zuletztGenutztAmUtc: string | null
  gesperrtAmUtc: string | null
}

/** GET/PUT /api/settings/ki-zugriff */
export interface KiZugriffSeiteDto {
  aktiv: boolean
  hoechstwerte: KiHoechstwerteDto
  schluessel: KiSchluesselDto[]
}

/** Rumpf von PUT /api/settings/ki-zugriff */
export interface KiZugriffSpeichernRequest {
  aktiv: boolean
  hoechstwerte: KiHoechstwerteDto
}

/** Rumpf von POST …/schluessel und PUT …/schluessel/{id} */
export interface KiSchluesselRequest {
  name: string
  stufen: KiStufe[]
  /** Teilmenge von `stufen` — sonst 400 mit Feldfehler `RueckfrageBei`. */
  rueckfrageBei: KiStufe[]
}

/** Antwort auf das Anlegen — der einzige Moment, in dem der Schlüssel im Klartext existiert. */
export interface KiSchluesselAngelegtDto {
  schluessel: KiSchluesselDto
  klartext: string
}

/**
 * Fork AI (A-003, 03.10.2026): Ein Eintrag in „Was die KI zuletzt getan hat" —
 * GET /api/settings/ki-zugriff/protokoll (`KiProtokollEintragDto`).
 */
export interface KiProtokollEintragDto {
  id: number
  zeitpunktUtc: string
  /** `null` bei ungültigem Schlüssel, ausgeschaltetem Zugriff und alten Einträgen. */
  schluesselId: number | null
  schluesselName: string | null
  methode: string | null
  pfad: string | null
  /** `null`, wenn der Eintrag keine Antwort beschreibt (Sicherung vorher, Adresse gesperrt). */
  status: number | null
  /** Etwa `ki_stufe_fehlt` — nie zum Anzeigen. */
  fehlercode: string | null
  erfolg: boolean
  /** Die Art des Eintrags (`ki-zugriff-schreibend` …) — ein Bezeichner, nie zum Anzeigen. */
  art: string
  /** Der Satz aus dem Prüfprotokoll. */
  beschreibung: string
  /**
   * Bei `POST /api/ki-ha/dienst`: Dienst und Entität, etwa `light.turn_on → light.zelt`
   * (nie die Daten). Fehlt bei allen anderen Einträgen und bei älteren.
   */
  haDienst: string | null
}
