import { describe, expect, it } from 'vitest'
import { bandGeometrie, kachelUrteil, kurzeZahl, urteilText } from './metric-tile-model'

// Fork AI (F-041): Ziel (Plan) und Grenze (Meldung) getrennt auf der Kachel.
describe('kachelUrteil', () => {
  const luftZiel = { min: 25, max: 25 }
  const luftGrenze = { min: 22, max: 28 }

  it('Einzelwert: daneben, solange die Grenze nicht überschritten ist', () => {
    expect(kachelUrteil(26.8, luftZiel, luftGrenze, 1)).toBe('warn')
  })

  it('Einzelwert: im Ziel nur, wenn gerundet genau getroffen', () => {
    expect(kachelUrteil(25.04, luftZiel, luftGrenze, 1)).toBe('ok')
    expect(kachelUrteil(25.1, luftZiel, luftGrenze, 1)).toBe('warn')
  })

  it('rot nur jenseits der Grenze', () => {
    expect(kachelUrteil(28.2, luftZiel, luftGrenze, 1)).toBe('crit')
    expect(kachelUrteil(21.9, luftZiel, luftGrenze, 1)).toBe('crit')
  })

  it('höchstens: RLF 52 bei ≤ 55 ist im Ziel', () => {
    expect(kachelUrteil(52, { min: null, max: 55 }, { min: 45, max: 55 }, 0)).toBe('ok')
  })

  it('ohne Ziel und ohne Grenze kein Urteil', () => {
    expect(kachelUrteil(5, { min: null, max: null }, { min: null, max: null })).toBe('unknown')
  })
})

describe('urteilText', () => {
  it('nennt bei Einzelwerten die Abweichung, Temperatur in Kelvin', () => {
    expect(urteilText('warn', 26.8, { min: 25, max: 25 }, '°C', 1)).toBe('+1,8 K')
    expect(urteilText('warn', 1.32, { min: 1.4, max: 1.4 }, 'kPa', 2)).toBe('−0,08 kPa')
  })

  it('bei Spannen „daneben", jenseits der Grenze „Grenze"', () => {
    expect(urteilText('warn', 1.62, { min: 1.4, max: 1.6 }, 'mS/cm', 2)).toBe('daneben')
    expect(urteilText('crit', 30, { min: 25, max: 25 }, '°C', 1)).toBe('Grenze')
  })
})

describe('bandGeometrie', () => {
  const kurz = (x: number) => kurzeZahl(x, 1)

  it('Einzelwert: Zielmarke statt Zone, Grenzen gelb', () => {
    const b = bandGeometrie(26.8, { min: 25, max: 25 }, { min: 22, max: 28 }, kurz)!
    expect(b.zone).toBeNull()
    expect(b.zielMarke).not.toBeNull()
    expect(b.grenzen).toHaveLength(2)
    expect(b.skala.map((s) => s.text)).toEqual(['22', '25', '28'])
  })

  it('Ziel gleich Grenze: gelbe Striche bleiben, Zahlen gelb (RLF 45–55)', () => {
    const b = bandGeometrie(52, { min: null, max: 55 }, { min: 45, max: 55 }, kurz)!
    expect(b.zone).not.toBeNull()
    expect(b.grenzen).toHaveLength(2)
    expect(b.skala.every((s) => s.art === 'grenze')).toBe(true)
  })

  it('Spanne mit Grenzen: vier Zahlen, Zone zwischen den Zielzahlen', () => {
    const b = bandGeometrie(1.62, { min: 1.4, max: 1.6 }, { min: 1.2, max: 1.8 }, kurz)!
    expect(b.skala.map((s) => [s.text, s.art])).toEqual([['1,2', 'grenze'], ['1,4', 'ziel'], ['1,6', 'ziel'], ['1,8', 'grenze']])
    expect(b.zone!.links).toBeGreaterThan(0)
  })
})
