/**
 * Sicherung zurückspielen: was die Prüfung dem Nutzer zeigt.
 *
 * Die Zahlen und Gründe kommen vom Server (`restore-plan`). Hier steht nur,
 * wie sie gelesen werden — als Zeilen „Was geprüft wurde → ja/nein“ und als
 * Entscheidung, ob „Jetzt zurückspielen“ freigegeben ist.
 */

export type RestorePlan = {
  fileName: string
  backupValid: boolean
  databaseIncluded: boolean
  knowledgeIncluded: boolean
  schemaCompatible: boolean
  restoreSupported: boolean
  wouldOverwriteExistingDatabase: boolean
  blockers: string[]
  warnings: string[]
  /** Fotos und Kamerabilder in der Sicherung (nur mit dem Haken „mitsichern“ erstellt). */
  mediaIncluded?: boolean
  mediaFileCount?: number
  mediaBytes?: number
}

export type RestoreResult = {
  success: boolean
  safetyBackupFileName: string
  databaseRestored: boolean
  knowledgeFileCount: number
  neustartGeplant: boolean
  warnings: string[]
}

export type Pruefzeile = { text: string; gut: boolean; wert: string; neutral?: boolean }

/** Eine Größe für Menschen: „3,4 MB“. */
export function groesse(bytes: number): string {
  const zahl = (wert: number) => wert.toFixed(1).replace('.', ',')
  if (bytes >= 1e9) return `${zahl(bytes / 1e9)} GB`
  if (bytes >= 1e6) return `${zahl(bytes / 1e6)} MB`
  if (bytes >= 1e3) return `${Math.round(bytes / 1e3)} KB`
  return `${bytes} Byte`
}

/** Grenze, bis zu der die App eine Sicherung hochlädt (gepackt). Home Assistant kann davor eine kleinere setzen. */
export const MAX_SICHERUNG_BYTES = 4 * 1024 * 1024 * 1024

/** Die Zeilen der Prüfung. `gut` färbt grün, sonst rot. */
export function pruefzeilen(plan: RestorePlan): Pruefzeile[] {
  return [
    { text: 'Datenbank enthalten', gut: plan.databaseIncluded, wert: plan.databaseIncluded ? 'ja' : 'nein' },
    { text: 'Wissensbasis enthalten', gut: plan.knowledgeIncluded, wert: plan.knowledgeIncluded ? 'ja' : 'nein' },
    plan.mediaIncluded
      ? { text: 'Fotos und Kamerabilder', gut: true, wert: `${plan.mediaFileCount ?? 0} Dateien, ${groesse(plan.mediaBytes ?? 0)}` }
      : { text: 'Fotos und Kamerabilder', gut: false, neutral: true, wert: 'nicht enthalten' },
    { text: 'Datenbank-Stand passt zur App', gut: plan.schemaCompatible, wert: plan.schemaCompatible ? 'ja' : 'nein' },
    { text: 'Zugangsdaten und unsichere Pfade', gut: plan.backupValid, wert: plan.backupValid ? 'keine' : 'gefunden' },
  ]
}

/** „Jetzt zurückspielen“ ist nur frei, wenn der Server nichts blockiert. */
export function zurueckspielenFrei(plan: RestorePlan | null): boolean {
  return plan != null && plan.restoreSupported && plan.blockers.length === 0
}

/** Die Gründe, warum es nicht geht — leer, wenn es geht. */
export function sperrGruende(plan: RestorePlan): string[] {
  return plan.blockers
}
