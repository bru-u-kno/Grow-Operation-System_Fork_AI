import { describe, expect, it } from 'vitest'
import { MARKEN_MINDESTABSTAND, RF_KNAPP_PUNKTE, feuchteZone, markenZeilen, schlechtereZone, temperaturZone, zonenKlasse, zonenStrecken, zonenTon } from './entfeuchter-zonen'

describe('Zonen der Entfeuchter-Seiten (A-014)', () => {
  it('Luftfeuchte: bis zum Ziel im Ziel, vier Punkte darüber knapp, danach deutlich daneben', () => {
    expect(feuchteZone(51, 51)).toBe('ziel')
    expect(feuchteZone(48, 51)).toBe('ziel')
    expect(feuchteZone(51.1, 51)).toBe('knapp')
    expect(feuchteZone(51 + RF_KNAPP_PUNKTE, 51)).toBe('knapp')
    expect(feuchteZone(55.1, 51)).toBe('kritisch')
    // Der Screenshot von Bru vom 07.10.2026, 18:14: 57,9 % bei Ziel 51 %.
    expect(feuchteZone(57.9, 51)).toBe('kritisch')
  })

  it('Temperatur: eine Stufe unter der Höchsttemperatur beginnt „knapp", darüber „deutlich daneben"', () => {
    expect(temperaturZone(22.7, 25.5)).toBe('ziel')
    expect(temperaturZone(24.5, 25.5)).toBe('ziel')
    expect(temperaturZone(24.6, 25.5)).toBe('knapp')
    expect(temperaturZone(25.5, 25.5)).toBe('knapp')
    expect(temperaturZone(25.6, 25.5)).toBe('kritisch')
  })

  it('Temperatur: ein eigener Zielrand (Zusatz geht früher aus) ersetzt die eine Stufe', () => {
    expect(temperaturZone(24.0, 25.5, 24.5)).toBe('ziel')
    expect(temperaturZone(24.7, 25.5, 24.5)).toBe('knapp')
  })

  it('ohne Messwert oder ohne Ziel gibt es keine Aussage statt einer erfundenen Zone', () => {
    expect(feuchteZone(null, 51)).toBeNull()
    expect(feuchteZone(55, null)).toBeNull()
    expect(feuchteZone(Number.NaN, 51)).toBeNull()
    expect(temperaturZone(undefined, 25)).toBeNull()
  })

  it('die schlechtere Zone gewinnt, „keine Aussage" zählt nicht', () => {
    expect(schlechtereZone('ziel', 'knapp')).toBe('knapp')
    expect(schlechtereZone('kritisch', 'ziel')).toBe('kritisch')
    expect(schlechtereZone(null, 'ziel')).toBe('ziel')
    expect(schlechtereZone(null, null)).toBeNull()
  })

  it('Klasse und Ton: im Ziel bleibt die Grundfarbe', () => {
    expect(zonenKlasse('ziel')).toBe('')
    expect(zonenKlasse(null)).toBe('')
    expect(zonenKlasse('knapp')).toBe('is-knapp')
    expect(zonenKlasse('kritisch')).toBe('is-kritisch')
    expect([zonenTon('ziel'), zonenTon('knapp'), zonenTon('kritisch')]).toEqual(['ok', 'warn', 'critical'])
  })

  it('Farbstrecken füllen die Skala lückenlos und ragen nie über den Rand', () => {
    const s = zonenStrecken(40, 65, 51, 55)
    expect(s.map((x) => x.zone)).toEqual(['ziel', 'knapp', 'kritisch'])
    expect(s.reduce((summe, x) => summe + x.breite, 0)).toBeCloseTo(100, 5)
    expect(s[1].links).toBeCloseTo(s[0].breite, 5)
    // Liegt das Ziel außerhalb des Ausschnitts, bleibt die grüne Strecke leer statt negativ.
    const rand = zonenStrecken(52, 60, 51, 55)
    expect(rand.every((x) => x.breite > 0 && x.links >= 0)).toBe(true)
    expect(rand[0].zone).toBe('knapp')
  })

  it('Marken: weit auseinander bleiben alle oben, dicht beieinander wandert die zweite nach unten', () => {
    expect(markenZeilen([10, 50, 90])).toEqual([0, 0, 0])
    expect(markenZeilen([10, 10 + MARKEN_MINDESTABSTAND - 1, 90])).toEqual([0, 1, 0])
    // „50,0 EIN" und „51,0 Ziel" (Handy, 07.10.2026): nebeneinander ginge nicht.
    expect(markenZeilen([40, 44, 80])).toEqual([0, 1, 0])
    // Drei dicht beieinander: höchstens zwei Zeilen — die dritte teilt sich eine, rutscht aber nach der Reihenfolge.
    expect(markenZeilen([40, 42, 44]).every((z) => z === 0 || z === 1)).toBe(true)
    expect(markenZeilen([])).toEqual([])
  })
})
