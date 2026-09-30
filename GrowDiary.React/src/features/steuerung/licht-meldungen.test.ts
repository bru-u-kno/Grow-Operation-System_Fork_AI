import { describe, expect, it } from 'vitest'
import { befehlsMeldung, speicherMeldung } from './licht-meldungen'

describe('befehlsMeldung', () => {
  it('meldet ein nicht bestätigtes Preset', () => {
    const meldung = befehlsMeldung('preset', false)
    expect(meldung).toContain('nicht bestätigt')
    expect(meldung).toContain('Modus wurde nicht umgestellt')
  })

  it('schweigt, wenn der Controller bestätigt hat', () => {
    expect(befehlsMeldung('preset', true)).toBeNull()
    expect(befehlsMeldung('aus', true)).toBeNull()
  })

  it('meldet einen nicht angekommenen Einzelbefehl', () => {
    expect(befehlsMeldung('aus', false)).toContain('nicht an')
  })

  it('schweigt beim Quittieren', () => {
    expect(befehlsMeldung('quittieren', false)).toBeNull()
  })
})

describe('speicherMeldung', () => {
  it('verspricht kein Nachprüfen, das es für Zeiten nicht mehr gibt', () => {
    const meldung = speicherMeldung(false)
    expect(meldung).not.toContain('prüft es weiter nach')
    expect(meldung).toContain('erneut speichern')
  })

  it('sagt schlicht „Gespeichert." bei Erfolg', () => {
    expect(speicherMeldung(true)).toBe('Gespeichert.')
  })
})
