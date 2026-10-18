import { describe, expect, it } from 'vitest'
import { ohneWaise } from './utils'

describe('ohneWaise', () => {
  it('hängt ein bis zu dreistelliges letztes Wort an das davor', () => {
    expect(ohneWaise('Aqua Flores A')).toBe('Aqua Flores\u00a0A')
    expect(ohneWaise('gemischt (2)')).toBe('gemischt\u00a0(2)')
  })
  it('lässt längere Endwörter und einzelne Wörter in Ruhe', () => {
    expect(ohneWaise('Aqua Flores')).toBe('Aqua Flores')
    expect(ohneWaise('A')).toBe('A')
  })
})
