import { describe, expect, it } from 'vitest'
import { istHandgesetzt, ZUSTAND_CO2_STEUERUNG, ZUSTAND_FOLGT_PLAN } from './uebergabe-zustand'

describe('Übergabe-Zustand', () => {
  it('nur „von dir gesetzt" ist freigebbar', () => {
    expect(istHandgesetzt('von dir gesetzt')).toBe(true)
    expect(istHandgesetzt(ZUSTAND_FOLGT_PLAN)).toBe(false)
    expect(istHandgesetzt(ZUSTAND_CO2_STEUERUNG)).toBe(false)
  })
})
