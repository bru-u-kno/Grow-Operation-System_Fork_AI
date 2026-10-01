import { describe, expect, it } from 'vitest'
import { bereichsartName, bereichsstatusName, mutterzustandName, quarantaeneErgebnisName } from './deutsche-woerter'

describe('Bereiche auf der Zeltseite', () => {
  it('übersetzt — auch kleingeschrieben, wie der SetupsApiController es speichern kann', () => {
    expect(bereichsartName('Mother')).toBe('Mutter')
    expect(bereichsstatusName('Active')).toBe('aktiv')
    expect(mutterzustandName('watch')).toBe('beobachten')
    expect(quarantaeneErgebnisName('CLEARED')).toBe('freigegeben')
  })
  it('reicht Unbekanntes durch statt es zu verschlucken', () => {
    expect(bereichsartName('Neu')).toBe('Neu')
  })
})
