import { describe, expect, it } from 'vitest'
import { formatDeviationTarget } from './grow-detail-model'
import type { GrowDeviationDto } from '../../types'

const abweichung = (teil: Partial<GrowDeviationDto>) => ({ targetMin: null, targetMax: null, unit: null, ...teil }) as GrowDeviationDto

describe('formatDeviationTarget', () => {
  it('hält Zahl und Einheit mit geschütztem Leerzeichen zusammen', () => {
    expect(formatDeviationTarget(abweichung({ targetMin: 1, targetMax: 1.2, unit: 'kPa' }))).toBe('1-1,2 kPa')
  })

  it('zeigt ein Ziel ohne Spanne als einzelnen Wert', () => {
    expect(formatDeviationTarget(abweichung({ targetMin: 1.4, targetMax: 1.4, unit: 'kPa' }))).toBe('1,4 kPa')
  })

  it('kennt einseitige Grenzen', () => {
    expect(formatDeviationTarget(abweichung({ targetMin: 5, unit: '%' }))).toBe('>= 5 %')
    expect(formatDeviationTarget(abweichung({ targetMax: 7 }))).toBe('<= 7')
  })
})
