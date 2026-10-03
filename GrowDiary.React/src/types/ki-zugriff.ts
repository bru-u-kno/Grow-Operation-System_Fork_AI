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
  stufen: KiStufe[]
  erstelltAmUtc: string
  zuletztGenutztAmUtc: string | null
  gesperrtAmUtc: string | null
}

/** GET/PUT /api/settings/ki-zugriff */
export interface KiZugriffSeiteDto {
  aktiv: boolean
  /** Ab welcher Stufe der Assistent vorher nachfragen soll; `null` = nie. */
  rueckfrageAbStufe: KiStufe | null
  hoechstwerte: KiHoechstwerteDto
  schluessel: KiSchluesselDto[]
}

/** Rumpf von PUT /api/settings/ki-zugriff */
export interface KiZugriffSpeichernRequest {
  aktiv: boolean
  rueckfrageAbStufe: KiStufe | null
  hoechstwerte: KiHoechstwerteDto
}

/** Rumpf von POST …/schluessel und PUT …/schluessel/{id} */
export interface KiSchluesselRequest {
  name: string
  stufen: KiStufe[]
}

/** Antwort auf das Anlegen — der einzige Moment, in dem der Schlüssel im Klartext existiert. */
export interface KiSchluesselAngelegtDto {
  schluessel: KiSchluesselDto
  klartext: string
}
