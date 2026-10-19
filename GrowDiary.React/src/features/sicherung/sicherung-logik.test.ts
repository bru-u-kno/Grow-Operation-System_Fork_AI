import { describe, expect, it } from 'vitest'
import { pruefzeilen, sperrGruende, zurueckspielenFrei, type RestorePlan } from './sicherung-logik'

const gut: RestorePlan = {
  fileName: 'grow-os-backup-1.zip',
  backupValid: true,
  databaseIncluded: true,
  knowledgeIncluded: true,
  schemaCompatible: true,
  restoreSupported: true,
  wouldOverwriteExistingDatabase: true,
  blockers: [],
  warnings: [],
}

describe('Sicherung zurückspielen', () => {
  it('gibt eine saubere Sicherung frei und zeigt alle Zeilen grün', () => {
    expect(zurueckspielenFrei(gut)).toBe(true)
    expect(pruefzeilen(gut).every((zeile) => zeile.gut)).toBe(true)
    expect(pruefzeilen(gut).map((zeile) => zeile.wert)).toEqual(['ja', 'ja', 'ja', 'keine'])
  })

  it('sperrt bei einem Blocker und nennt den Grund', () => {
    const blockiert: RestorePlan = {
      ...gut,
      schemaCompatible: false,
      restoreSupported: false,
      blockers: ['Backup-Schema ist nicht mit der aktuellen Backend-Version kompatibel.'],
    }
    expect(zurueckspielenFrei(blockiert)).toBe(false)
    expect(sperrGruende(blockiert)).toEqual(['Backup-Schema ist nicht mit der aktuellen Backend-Version kompatibel.'])
    expect(pruefzeilen(blockiert)[2]).toMatchObject({ gut: false, wert: 'nein' })
  })

  it('bleibt ohne Prüfung gesperrt, auch wenn der Server „unterstützt“ sagt, aber Blocker dabeistehen', () => {
    expect(zurueckspielenFrei(null)).toBe(false)
    expect(zurueckspielenFrei({ ...gut, blockers: ['x'] })).toBe(false)
  })
})
