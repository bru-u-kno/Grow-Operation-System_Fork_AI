import { describe, expect, it } from 'vitest'
import { schlechtereZone, zonenKlasse, zonenTon } from './entfeuchter-zonen'

describe('Zonen der Entfeuchter-Seiten (A-014, A-021: die Zonen im Band stehen in entfeuchtung-ziel.test.ts)', () => {
  it('die schlechtere Zone gewinnt, „keine Aussage" zählt nicht', () => {
    expect(schlechtereZone('ziel', 'knapp')).toBe('knapp')
    expect(schlechtereZone('kritisch', 'ziel')).toBe('kritisch')
    expect(schlechtereZone(null, 'ziel')).toBe('ziel')
    expect(schlechtereZone(null, null)).toBeNull()
  })

  it('Klasse und Ton: im Ziel bleibt die Grundfarbe', () => {
    expect(zonenKlasse('ziel')).toBe('')
    expect(zonenKlasse(null)).toBe('')
    expect(zonenKlasse('knapp')).toBe('is-knapp')
    expect(zonenKlasse('kritisch')).toBe('is-kritisch')
    expect([zonenTon('ziel'), zonenTon('knapp'), zonenTon('kritisch')]).toEqual(['ok', 'warn', 'critical'])
  })

})
