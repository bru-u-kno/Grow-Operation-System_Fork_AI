import { describe, expect, it } from 'vitest'
import type { RiskEventDto } from '../../types'
import { dauerText, istOffen, istWiederOkay, leseRohwert, seitText, vorText, wiederholungen, zielText } from './risiko-zeit'

const JETZT = Date.parse('2026-10-15T04:05:00Z')

function risiko(teil: Partial<RiskEventDto>): RiskEventDto {
  return {
    id: 1, eventType: 'Other', severity: 'Warning', status: 'Open', source: 'Deviation', title: 't', description: null,
    hardwareItemId: null, tentId: 1, growId: 1, tentSensorId: null, haEntityId: null, sopInstanceId: null, growTaskId: null,
    startedAtUtc: '2026-10-15T03:33:48Z', lastSeenAtUtc: null, resolvedAtUtc: null, acknowledgedAtUtc: null,
    dedupeKey: 'deviation:grow:1:hydro.vpd', rawValue: null, notes: null,
    createdAtUtc: '2026-10-15T03:33:48Z', updatedAtUtc: '2026-10-15T03:33:48Z', ...teil,
  }
}

describe('Dauer und Zeitpunkt', () => {
  it('nennt Minuten, Stunden und Tage mit der richtigen Mehrzahl', () => {
    expect(dauerText(20_000)).toBe('unter 1 Min.')
    expect(dauerText(32 * 60_000)).toBe('32 Min.')
    expect(dauerText(3 * 3_600_000 + 59 * 60_000)).toBe('3 Std.')
    expect(dauerText(24 * 3_600_000)).toBe('1 Tag')
    expect(dauerText(2.6 * 24 * 3_600_000)).toBe('2 Tagen')
  })
  it('baut „Seit …" und „vor …" daraus', () => {
    expect(seitText('2026-10-12T15:32:29Z', JETZT)).toBe('Seit 2 Tagen')
    expect(vorText('2026-10-15T03:33:48Z', JETZT)).toBe('vor 31 Min.')
    expect(vorText('2026-10-15T04:04:50Z', JETZT)).toBe('gerade eben')
  })
  it('lässt eine Zeit aus der Zukunft nicht negativ werden', () => {
    expect(seitText('2026-10-15T05:00:00Z', JETZT)).toBe('Seit unter 1 Min.')
  })
})

describe('Rohwert der Abgleich-Karte', () => {
  it('liest Istwert, Band und Einheit', () => {
    expect(leseRohwert('actual=6.22;target=5.6-6.2;unit=pH')).toEqual({ istwert: 6.22, min: 5.6, max: 6.2, einheit: 'pH' })
  })
  it('zeigt die Temperatur mit Gradzeichen', () => {
    expect(leseRohwert('actual=14.81;target=15-22;unit=C')).toMatchObject({ einheit: '°C' })
  })
  it('liest einseitige Ziele', () => {
    expect(leseRohwert('actual=2;target=>=3;unit=x')).toMatchObject({ min: 3, max: null })
    expect(leseRohwert('actual=9;target=<=7;unit=x')).toMatchObject({ min: null, max: 7 })
  })
  it('gibt null statt einer erfundenen Zahl zurück', () => {
    expect(leseRohwert(null)).toBeNull()
    expect(leseRohwert('actual=-;target=-;unit=')).toBeNull()
    expect(leseRohwert('kaputt')).toBeNull()
  })
  it('behält den Istwert, wenn kein Ziel lesbar ist', () => {
    const wert = leseRohwert('actual=1.5;target=-;unit=kPa')
    expect(wert).toEqual({ istwert: 1.5, min: null, max: null, einheit: 'kPa' })
    expect(zielText(wert!)).toBeNull()
  })
  it('schreibt das Ziel deutsch — auch ein Band ohne Breite', () => {
    expect(zielText(leseRohwert('actual=6.22;target=5.6-6.2;unit=pH')!)).toBe('Ziel 5,6 – 6,2')
    expect(zielText(leseRohwert('actual=1.01;target=1.6-1.6;unit=kPa')!)).toBe('Ziel 1,6')
    expect(zielText(leseRohwert('actual=2;target=>=3;unit=x')!)).toBe('Ziel ≥ 3')
  })
})

describe('Wiederholungen', () => {
  const alle = [
    risiko({ id: 73, startedAtUtc: '2026-10-12T15:32:29Z' }),
    risiko({ id: 69, status: 'Resolved', startedAtUtc: '2026-10-10T15:33:16Z' }),
    risiko({ id: 41, status: 'Resolved', startedAtUtc: '2026-09-12T15:32:27Z' }),
    risiko({ id: 5, dedupeKey: 'deviation:grow:1:hydro.ph', startedAtUtc: '2026-08-01T00:00:00Z' }),
  ]
  it('zählt nur dieselbe Abweichung und nennt den ersten Tag', () => {
    expect(wiederholungen(alle[0], alle)).toEqual({ anzahl: 3, seitIso: '2026-09-12T15:32:27Z' })
  })
  it('sagt beim ersten Mal nichts', () => {
    expect(wiederholungen(alle[3], alle)).toBeNull()
    expect(wiederholungen(risiko({ dedupeKey: null }), alle)).toBeNull()
  })
  it('zählt das Risiko selbst mit, auch wenn die Liste es nicht enthält', () => {
    expect(wiederholungen(risiko({ id: 99, startedAtUtc: '2026-10-15T00:00:00Z' }), [alle[1]])?.anzahl).toBe(2)
  })
})

describe('Zustand', () => {
  it('offen oder bestätigt gilt als „besteht noch"', () => {
    expect(istOffen(risiko({ status: 'Open' }))).toBe(true)
    expect(istOffen(risiko({ status: 'Acknowledged' }))).toBe(true)
    expect(istOffen(risiko({ status: 'Resolved' }))).toBe(false)
  })
  it('„wieder okay" nur vom Abgleich geschlossen und höchstens 24 Stunden her', () => {
    const zu = { status: 'Resolved' as const, resolvedAutomatically: true }
    expect(istWiederOkay(risiko({ ...zu, resolvedAtUtc: '2026-10-15T01:00:00Z' }), JETZT)).toBe(true)
    expect(istWiederOkay(risiko({ ...zu, resolvedAtUtc: '2026-10-14T04:06:00Z' }), JETZT)).toBe(true)
    expect(istWiederOkay(risiko({ ...zu, resolvedAtUtc: '2026-10-14T04:04:00Z' }), JETZT)).toBe(false)
    expect(istWiederOkay(risiko({ status: 'Resolved', resolvedAutomatically: false, resolvedAtUtc: '2026-10-15T01:00:00Z' }), JETZT)).toBe(false)
    expect(istWiederOkay(risiko({ status: 'Resolved', resolvedAtUtc: '2026-10-15T01:00:00Z' }), JETZT)).toBe(false)
  })
})
