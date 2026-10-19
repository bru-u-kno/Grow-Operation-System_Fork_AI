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
}

export type RestoreResult = {
  success: boolean
  safetyBackupFileName: string
  databaseRestored: boolean
  knowledgeFileCount: number
  neustartGeplant: boolean
  warnings: string[]
}

export type Pruefzeile = { text: string; gut: boolean; wert: string }

/** Die Zeilen der Prüfung. `gut` färbt grün, sonst rot. */
export function pruefzeilen(plan: RestorePlan): Pruefzeile[] {
  return [
    { text: 'Datenbank enthalten', gut: plan.databaseIncluded, wert: plan.databaseIncluded ? 'ja' : 'nein' },
    { text: 'Wissensbasis enthalten', gut: plan.knowledgeIncluded, wert: plan.knowledgeIncluded ? 'ja' : 'nein' },
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
