import { describe, expect, it } from 'vitest'
import { zielwerteKurz } from './useZielwerteKurz'

describe('zielwerteKurz', () => {
  it('holt Grow, Programm, Woche und die Regeln für Luftfeuchte und Lufttemperatur', () => {
    const kurz = zielwerteKurz({
      growName: '2026-01', programm: 'SKX Canna Aqua', woche: 'Flush', phase: 'Finish',
      werte: [
        { key: 'ph', regel: null },
        { key: 'humidity', regel: { min: 45, max: 54, nachtMax: 60 }, quelleZusatz: 'von dir gesetzt — der Plan lässt sie in Ruhe' },
        { key: 'temperature', regel: { min: 14, max: 22, nachtMin: 12, nachtMax: 20, toleranz: 4 }, quelleZusatz: 'Flush · ±4 K' },
      ],
    })
    expect(kurz.growName).toBe('2026-01')
    expect(kurz.programm).toBe('SKX Canna Aqua')
    expect(kurz.woche).toBe('Flush')
    expect(kurz.feuchteRegel?.min).toBe(45)
    expect(kurz.temperaturRegel?.nachtMin).toBe(12)
    expect(kurz.feuchteQuelle).toBe('von dir gesetzt — der Plan lässt sie in Ruhe')
    expect(kurz.temperaturQuelle).toBe('Flush · ±4 K')
  })

  it('fehlt etwas, bleibt es leer — nichts wird erfunden', () => {
    const kurz = zielwerteKurz({})
    expect(kurz).toEqual({ growName: null, programm: null, woche: null, feuchteRegel: null, temperaturRegel: null, feuchteQuelle: null, temperaturQuelle: null })
  })

  it('ein Grenzwert, der die Messgröße gar nicht hat (kein „humidity" in der Liste), ergibt keine Regel', () => {
    expect(zielwerteKurz({ werte: [{ key: 'ph', regel: { min: 5, max: 6 } }] }).feuchteRegel).toBeNull()
  })
})
