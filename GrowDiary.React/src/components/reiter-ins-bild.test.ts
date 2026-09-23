import { describe, expect, it } from 'vitest'
import { auslaufBerechnen } from './reiter-ins-bild'

describe('auslaufBerechnen (F-048)', () => {
  it('lange Seite braucht keinen Auslauf', () => {
    expect(auslaufBerechnen(600, 3000, 800)).toBe(0)
  })
  it('genau erreichbar braucht keinen Auslauf', () => {
    expect(auslaufBerechnen(600, 1400, 800)).toBe(0)
  })
  it('kurzer Reiter bekommt genau die fehlende Hoehe', () => {
    expect(auslaufBerechnen(600, 1100, 800)).toBe(300)
  })
  it('rundet Bruchteile auf', () => {
    expect(auslaufBerechnen(600.4, 1100, 800)).toBe(301)
  })
})
