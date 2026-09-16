import { describe, expect, it } from 'vitest'
import type { AlertRuleDto } from '../../types/alert'
import {
  alarmGeaendert, entwurfAus, planAenderungen, pruefen, regelnMitAenderung,
  type AlarmRegel, type PlanFeld,
} from './wert-blatt'

const ph: PlanFeld[] = [
  { feld: 'phMin', bezeichnung: 'pH von', einheit: '', min: 3, max: 9, schritt: 0.1, wert: 5.9, startwert: 5.9, herkunft: 'programm' },
  { feld: 'phMax', bezeichnung: 'pH bis', einheit: '', min: 3, max: 9, schritt: 0.1, wert: 6.1, startwert: 5.9, herkunft: 'eigen' },
]

const festeRegel: AlarmRegel = {
  quelle: 'Fest', min: 22, max: 28, nachtMin: 18, nachtMax: 24, toleranz: null,
  standardToleranz: 3, karenzMinuten: 10, aktiv: true, planMoeglich: false,
}

describe('planAenderungen', () => {
  it('schickt nur, was sich geändert hat', () => {
    const e = entwurfAus(ph, null)
    expect(planAenderungen(ph, e, 'flower-w4')).toEqual([])
    e.plan.phMin = '5,8'
    expect(planAenderungen(ph, e, 'flower-w4')).toEqual([{ spalteId: 'flower-w4', feld: 'phMin', wert: 5.8 }])
  })

  it('der Startwert geht als null raus — zurück auf den Plan', () => {
    const e = entwurfAus(ph, null)
    e.plan.phMax = '5,9'
    expect(planAenderungen(ph, e, 'w')).toEqual([{ spalteId: 'w', feld: 'phMax', wert: null }])
  })
})

describe('pruefen', () => {
  it('lehnt vertauschte Plan-Paare ab', () => {
    const e = entwurfAus(ph, festeRegel)
    e.plan.phMin = '6,5'
    expect(pruefen(ph, e)).toMatch(/über dem Bis-Wert/)
  })

  it('lehnt Werte außerhalb des Bereichs ab', () => {
    const e = entwurfAus(ph, festeRegel)
    e.plan.phMax = '12'
    expect(pruefen(ph, e)).toMatch(/zwischen 3 und 9/)
  })

  it('lehnt vertauschte feste Grenzen ab', () => {
    const e = entwurfAus([], festeRegel)
    e.min = '30'
    expect(pruefen([], e)).toMatch(/untere Alarmgrenze/)
  })

  it('ein gültiger Entwurf geht durch', () => {
    expect(pruefen(ph, entwurfAus(ph, festeRegel))).toBeNull()
  })
})

describe('alarmGeaendert', () => {
  it('erkennt keine Änderung am unberührten Entwurf', () => {
    expect(alarmGeaendert(festeRegel, entwurfAus([], festeRegel))).toBe(false)
  })

  it('erkennt eine geänderte Karenz', () => {
    const e = entwurfAus([], festeRegel)
    e.karenz = '30'
    expect(alarmGeaendert(festeRegel, e)).toBe(true)
  })
})

describe('regelnMitAenderung', () => {
  const alle: AlertRuleDto[] = [
    { metricKey: 'temperature', minValue: 22, maxValue: 28, notifyService: '', enabled: true, cooldownMinutes: 10, quelle: 'Fest', toleranz: null, nightMinValue: 18, nightMaxValue: 24 },
    { metricKey: 'co2', minValue: null, maxValue: null, notifyService: '', enabled: true, cooldownMinutes: 60, quelle: 'Plan', toleranz: 600 },
  ]

  it('ersetzt nur die eine Regel und behält das Nachtband (F-016)', () => {
    const e = entwurfAus([], festeRegel)
    e.max = '29'
    const neu = regelnMitAenderung(alle, 'temperature', e)
    expect(neu).toHaveLength(2)
    expect(neu[0]).toMatchObject({ maxValue: 29, nightMinValue: 18, nightMaxValue: 24 })
    expect(neu[1]).toBe(alle[1])
  })

  it('hängt eine neue Regel an', () => {
    const e = entwurfAus([], null)
    e.aktiv = true
    e.min = '400'
    const neu = regelnMitAenderung(alle, 'orp', e)
    expect(neu).toHaveLength(3)
    expect(neu[2]).toMatchObject({ metricKey: 'orp', minValue: 400, enabled: true })
  })
})
