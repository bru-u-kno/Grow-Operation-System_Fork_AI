import { describe, expect, it } from 'vitest'
import { MAX_ANGEHEFTET, umschalten, type KopfKnopf } from './kopf-knoepfe'

describe('umschalten', () => {
  it('heftet an, was noch nicht oben steht', () => {
    expect(umschalten(['messen'], 'addback')).toEqual(['messen', 'addback'])
  })

  it('nimmt wieder ab, was schon oben steht', () => {
    expect(umschalten(['messen', 'addback'], 'messen')).toEqual(['addback'])
  })

  it('lässt bei voller Zeile den ältesten Knopf los statt nichts zu tun', () => {
    const nachher = umschalten(['messen', 'addback'], 'anpassen')
    expect(nachher).toEqual(['addback', 'anpassen'])
    expect(nachher).toHaveLength(MAX_ANGEHEFTET)
  })

  it('kommt auch mit leerer Zeile klar', () => {
    const leer: KopfKnopf[] = []
    expect(umschalten(leer, 'messen')).toEqual(['messen'])
  })
})
