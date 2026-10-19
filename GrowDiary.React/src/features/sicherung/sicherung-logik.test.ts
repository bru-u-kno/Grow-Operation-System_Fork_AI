import { describe, expect, it } from 'vitest'
import { groesse, pruefzeilen, sperrGruende, zurueckspielenFrei, type RestorePlan } from './sicherung-logik'

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
  mediaIncluded: true,
  mediaFileCount: 6,
  mediaBytes: 3_400_000,
}

describe('Sicherung zurückspielen', () => {
  it('gibt eine saubere Sicherung frei und zeigt alle Zeilen grün', () => {
    expect(zurueckspielenFrei(gut)).toBe(true)
    expect(pruefzeilen(gut).every((zeile) => zeile.gut)).toBe(true)
    expect(pruefzeilen(gut).map((zeile) => zeile.wert)).toEqual(['ja', 'ja', '6 Dateien, 3,4 MB', 'ja', 'keine'])
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
    expect(pruefzeilen(blockiert)[3]).toMatchObject({ gut: false, wert: 'nein' })
  })

  it('bleibt ohne Prüfung gesperrt, auch wenn der Server „unterstützt“ sagt, aber Blocker dabeistehen', () => {
    expect(zurueckspielenFrei(null)).toBe(false)
    expect(zurueckspielenFrei({ ...gut, blockers: ['x'] })).toBe(false)
  })

  it('sagt bei einer Sicherung ohne Bilder „nicht enthalten“, ohne es als Fehler zu färben', () => {
    const ohne: RestorePlan = { ...gut, mediaIncluded: false, mediaFileCount: 0, mediaBytes: 0 }
    const zeile = pruefzeilen(ohne).find((z) => z.text === 'Fotos und Kamerabilder')
    expect(zeile).toMatchObject({ wert: 'nicht enthalten', neutral: true })
    expect(zurueckspielenFrei(ohne)).toBe(true)
    // Eine Sicherung aus einer älteren Fassung kennt die Felder gar nicht.
    const alt = { ...gut } as Partial<RestorePlan>
    delete alt.mediaIncluded
    expect(pruefzeilen(alt as RestorePlan).find((z) => z.text === 'Fotos und Kamerabilder')?.wert).toBe('nicht enthalten')
  })

  it('nennt Größen so, wie man sie liest', () => {
    expect(groesse(0)).toBe('0 Byte')
    expect(groesse(30_000)).toBe('30 KB')
    expect(groesse(3_400_000)).toBe('3,4 MB')
    expect(groesse(2_500_000_000)).toBe('2,5 GB')
  })
})
