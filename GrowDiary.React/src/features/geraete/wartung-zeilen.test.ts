import { describe, expect, it } from 'vitest'
import type { CalibrationEventDto, HardwareItemDto, MaintenanceEventDto } from '../../types'
import { wartungsZeilen } from './wartung-zeilen'

/**
 * Falsche Fälligkeit im Wartungs-Reiter (Durchsicht 02.10.2026): gerechnet
 * wurde nur Einbaudatum + Intervall. Erledigte Wartungen und Kalibrierungen,
 * abgesagte Termine und ausgemusterte Geräte spielten keine Rolle.
 */
function teil(t: Partial<HardwareItemDto> & { id: number }): HardwareItemDto {
  return {
    name: `Gerät ${t.id}`, category: 'Sensor', status: 'Active', criticality: 'Medium',
    tentId: null, setupId: null, hydroSetupId: null, growId: null, wearTemplateId: null, tentSensorId: null,
    haEntityId: null, manufacturer: null, model: null, serialNumber: null,
    installedAtUtc: '2026-06-01T00:00:00Z', retiredAtUtc: null, expectedLifespanDays: null,
    inspectionIntervalDays: null, calibrationIntervalDays: null, notes: null,
    createdAtUtc: '2026-06-01T00:00:00Z', updatedAtUtc: '2026-06-01T00:00:00Z',
    ...t,
  }
}

function wartung(e: Partial<MaintenanceEventDto> & { id: number; hardwareItemId: number }): MaintenanceEventDto {
  return {
    eventType: 'Inspection', status: 'Planned', result: 'Unknown', title: 'Prüfung', description: null,
    dueAtUtc: null, performedAtUtc: null, nextDueAtUtc: null, growTaskId: null, sopInstanceId: null, notes: null,
    createdAtUtc: '2026-06-01T00:00:00Z', updatedAtUtc: '2026-06-01T00:00:00Z',
    ...e,
  } as MaintenanceEventDto
}

function kalibrierung(e: Partial<CalibrationEventDto> & { id: number; hardwareItemId: number }): CalibrationEventDto {
  return {
    calibrationType: 'Ph', status: 'Completed', result: 'Passed', title: 'pH kalibrieren',
    referenceSolution: null, referenceValue: null, beforeValue: null, afterValue: null, temperatureC: null,
    dueAtUtc: null, performedAtUtc: null, nextDueAtUtc: null, growTaskId: null, notes: null,
    createdAtUtc: '2026-06-01T00:00:00Z', updatedAtUtc: '2026-06-01T00:00:00Z',
    ...e,
  } as CalibrationEventDto
}

const keineNamen = new Map<number, string>()
const iso = (d: Date | null) => d?.toISOString().slice(0, 10) ?? null

describe('wartungsZeilen', () => {
  it('nach einer Kalibrierung zählt ihr Folgetermin, nicht das Einbaudatum', () => {
    const zeilen = wartungsZeilen(
      [teil({ id: 1, calibrationIntervalDays: 14 })],
      [],
      [kalibrierung({ id: 1, hardwareItemId: 1, performedAtUtc: '2026-09-29T00:00:00Z', nextDueAtUtc: '2026-10-13T00:00:00Z' })],
      keineNamen,
    )
    expect(zeilen).toHaveLength(1)
    expect(iso(zeilen[0].faellig)).toBe('2026-10-13')
  })

  it('ohne Einbaudatum, aber mit Kalibrierung steht trotzdem eine Frist da', () => {
    const zeilen = wartungsZeilen(
      [teil({ id: 1, calibrationIntervalDays: 14, installedAtUtc: null })],
      [],
      [kalibrierung({ id: 1, hardwareItemId: 1, performedAtUtc: '2026-09-29T00:00:00Z' })],
      keineNamen,
    )
    expect(zeilen[0].ohneFrist).toBe(false)
    expect(iso(zeilen[0].faellig)).toBe('2026-10-13')
  })

  it('ein geplanter Termin trägt seine eigene Frist — und verdrängt die gerechnete', () => {
    const zeilen = wartungsZeilen(
      [teil({ id: 1, calibrationIntervalDays: 14 })],
      [],
      [
        kalibrierung({ id: 1, hardwareItemId: 1, performedAtUtc: '2026-09-13T00:00:00Z', nextDueAtUtc: '2026-09-27T00:00:00Z' }),
        kalibrierung({ id: 2, hardwareItemId: 1, status: 'Planned', dueAtUtc: '2026-09-27T00:00:00Z' }),
      ],
      keineNamen,
    )
    expect(zeilen).toHaveLength(1)
    expect(zeilen[0].schluessel).toBe('kalibrierung-2')
  })

  it('nach einer erledigten Prüfung zählt sie, nicht der Einbau', () => {
    const zeilen = wartungsZeilen(
      [teil({ id: 1, inspectionIntervalDays: 30 })],
      [wartung({ id: 1, hardwareItemId: 1, status: 'Completed', performedAtUtc: '2026-09-20T00:00:00Z' })],
      [],
      keineNamen,
    )
    expect(iso(zeilen[0].faellig)).toBe('2026-10-20')
  })

  it('ein abgesagter Termin ist nicht offen', () => {
    const zeilen = wartungsZeilen(
      [teil({ id: 1 })],
      [wartung({ id: 1, hardwareItemId: 1, status: 'Cancelled', dueAtUtc: '2026-08-01T00:00:00Z' })],
      [],
      keineNamen,
    )
    expect(zeilen).toHaveLength(0)
  })

  it('ausgemusterte Geräte zählen nicht mit — weder Intervall noch Termin', () => {
    const zeilen = wartungsZeilen(
      [teil({ id: 1, status: 'Retired', inspectionIntervalDays: 30 })],
      [wartung({ id: 1, hardwareItemId: 1, dueAtUtc: '2026-08-01T00:00:00Z' })],
      [],
      keineNamen,
    )
    expect(zeilen).toHaveLength(0)
  })

  it('Kalibrieren und Prüfen am selben Gerät sind zwei Zeilen', () => {
    const zeilen = wartungsZeilen(
      [teil({ id: 1, inspectionIntervalDays: 30, calibrationIntervalDays: 14 })],
      [], [], keineNamen,
    )
    expect(zeilen.map((z) => z.titel).sort()).toEqual(['Kalibrieren', 'Prüfen'])
  })

  it('ohne Einbau und ohne Abschluss: ohne Frist', () => {
    const zeilen = wartungsZeilen([teil({ id: 1, calibrationIntervalDays: 14, installedAtUtc: null })], [], [], keineNamen)
    expect(zeilen[0].ohneFrist).toBe(true)
  })
})
