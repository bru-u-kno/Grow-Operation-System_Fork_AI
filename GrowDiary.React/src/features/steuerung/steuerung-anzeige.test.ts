import { describe, expect, it } from 'vitest'
import { haZustandName } from '../../deutsche-woerter'
import { probeWerte, tagKurz } from './steuerung-typen'

/**
 * Rohe Werte auf der Steuerungsseite (Durchsicht 02.10.2026): die CO₂-Tagesliste
 * zeigte „2026-09-30", die Probeschaltung „812.0" und „unavailable", der Kühler
 * „heat_cool".
 */
describe('tagKurz', () => {
  it('zeigt Wochentag und Tag statt ISO', () => {
    expect(tagKurz('2026-09-30')).toBe('Mi 30.09.')
    expect(tagKurz('2026-10-04')).toBe('So 04.10.')
  })
  it('lässt Unbekanntes stehen', () => {
    expect(tagKurz('gestern')).toBe('gestern')
  })
})

describe('probeWerte', () => {
  it('zwei Zahlen werden deutsch und ohne Nachkomma-Null', () => {
    expect(probeWerte('812.0', '845.4')).toBe(' CO₂ 812 → 845 ppm.')
    expect(probeWerte('1012.0', '1100')).toBe(' CO₂ 1.012 → 1.100 ppm.')
  })
  it('ein Fühlerzustand steht auf Deutsch da', () => {
    expect(probeWerte('unavailable', null)).toBe(' CO₂-Fühler: nicht erreichbar.')
    expect(probeWerte('812.0', 'unknown')).toBe(' CO₂-Fühler: unbekannt.')
  })
  it('ohne Werte nichts', () => {
    expect(probeWerte(null, null)).toBe('')
  })
})

describe('haZustandName', () => {
  it('übersetzt Klimazustände', () => {
    expect(haZustandName('cool')).toBe('kühlt')
    expect(haZustandName('heat_cool')).toBe('heizt und kühlt')
    expect(haZustandName('off')).toBe('aus')
  })
  it('reicht Unbekanntes durch', () => {
    expect(haZustandName('turbo')).toBe('turbo')
  })
})
