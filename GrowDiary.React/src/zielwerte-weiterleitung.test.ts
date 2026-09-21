import { describe, expect, it } from 'vitest'
import { legacyRedirects, zielwerteZiel } from './navigation'

describe('alte Adressen von „Ziele & Meldungen“ (forkai.133)', () => {
  it('führen je nach Reiter auf die neue Seite', () => {
    expect(zielwerteZiel('')).toBe('/grenzwerte')
    expect(zielwerteZiel('?tab=jetzt')).toBe('/grenzwerte')
    expect(zielwerteZiel('?tab=plan&woche=flower-w5')).toBe('/plan?woche=flower-w5')
    expect(zielwerteZiel('?tab=meldungen')).toBe('/handy?tab=push')
  })

  it('ältere Lesezeichen landen ebenfalls richtig', () => {
    expect(legacyRedirects['/wochenplan']).toBe('/plan')
    expect(legacyRedirects['/alarme']).toBe('/grenzwerte')
    expect(legacyRedirects['/benachrichtigungen']).toBe('/handy?tab=push')
  })
})
