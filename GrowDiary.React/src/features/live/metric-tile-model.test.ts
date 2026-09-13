import { describe, expect, it } from 'vitest'
import { bandText, metricScale, metricStatus, statusLabel, targetLabel } from './metric-tile-model'

describe('metricScale', () => {
  it('setzt den Marker mittig, wenn der Wert in der Mitte des Ziels liegt', () => {
    const scale = metricScale(6.0, 5.8, 6.2)!
    expect(scale.marker).toBe(50)
  })

  it('zeichnet das Zielband in der Mitte der Skala', () => {
    // Skala = Ziel plus halbe Zielbreite je Seite, das Band liegt also mittig
    // und nimmt die Hälfte ein.
    const scale = metricScale(6.0, 5.8, 6.2)!
    expect(scale.bandLeft).toBe(25)
    expect(scale.bandWidth).toBe(50)
  })

  it('schiebt den Marker an den Rand, wenn der Wert knapp daneben liegt', () => {
    const scale = metricScale(6.3, 5.8, 6.2)!
    expect(scale.marker).toBeGreaterThan(75)
    expect(scale.marker).toBeLessThan(100)
    expect(scale.clamped).toBe(false)
  })

  it('meldet, wenn der Wert ausserhalb der gezeichneten Skala liegt', () => {
    // Ein Wert weit weg soll nicht die ganze Skala verzerren — er klebt am Rand
    // und die Kachel weiss das.
    const scale = metricScale(9.0, 5.8, 6.2)!
    expect(scale.clamped).toBe(true)
    expect(scale.marker).toBeGreaterThan(90)
  })

  it('kommt mit einem halboffenen Zielbereich klar', () => {
    // Gelöster Sauerstoff hat nur eine Untergrenze.
    const scale = metricScale(7.4, 7.0, null)!
    expect(scale.bandWidth).toBeGreaterThan(0)
    expect(scale.marker).toBeGreaterThan(0)
  })

  it('zeichnet nichts ohne Wert oder ohne Zielbereich', () => {
    expect(metricScale(null, 5.8, 6.2)).toBeNull()
    expect(metricScale(6.0, null, null)).toBeNull()
    expect(metricScale(Number.NaN, 5.8, 6.2)).toBeNull()
  })

  it('bleibt in den Grenzen 0 bis 100', () => {
    for (const value of [-50, 0, 6, 1000]) {
      const scale = metricScale(value, 5.8, 6.2)
      if (!scale) continue
      for (const n of [scale.bandLeft, scale.marker]) {
        expect(n).toBeGreaterThanOrEqual(0)
        expect(n).toBeLessThanOrEqual(100)
      }
    }
  })
})

describe('metricStatus', () => {
  it('nennt einen Wert im Zielbereich in Ordnung', () => {
    expect(metricStatus(6.0, 5.8, 6.2)).toBe('ok')
  })

  it('zählt die Grenzen selbst noch zum Ziel', () => {
    expect(metricStatus(5.8, 5.8, 6.2)).toBe('ok')
    expect(metricStatus(6.2, 5.8, 6.2)).toBe('ok')
  })

  it('nennt knapp daneben auffällig, nicht kritisch', () => {
    // pH 6,3 statt 6,2 ist kein Notfall.
    expect(metricStatus(6.3, 5.8, 6.2)).toBe('warn')
  })

  it('unterscheidet auffällig von kritisch, wenn eine zweite Grenze da ist', () => {
    const critical = { min: 5.5, max: 6.5 }
    expect(metricStatus(6.3, 5.8, 6.2, critical)).toBe('warn')
    expect(metricStatus(6.8, 5.8, 6.2, critical)).toBe('crit')
  })

  it('weiss nichts ohne Wert oder ohne Ziel', () => {
    expect(metricStatus(null, 5.8, 6.2)).toBe('unknown')
    expect(metricStatus(6.0, null, null)).toBe('unknown')
  })

  it('behandelt eine halboffene Grenze richtig', () => {
    expect(metricStatus(7.4, 7.0, null)).toBe('ok')
    expect(metricStatus(6.4, 7.0, null)).toBe('warn')
  })
})

describe('Beschriftungen', () => {
  it('schreibt den Zielbereich mit Komma', () => {
    expect(targetLabel(5.8, 6.2)).toBe('Ziel 5,8–6,2')
  })

  it('schreibt halboffene Bereiche als Ungleichung', () => {
    expect(targetLabel(7, null, 'mg/l')).toBe('Ziel ≥ 7 mg/l')
    expect(targetLabel(null, 21, '°C')).toBe('Ziel ≤ 21 °C')
  })

  it('nimmt die Nachkommastellen von aussen, weil 7.0 und 7 dieselbe Zahl sind', () => {
    expect(targetLabel(7, null, 'mg/l', 1)).toBe('Ziel ≥ 7,0 mg/l')
    expect(targetLabel(55, 70, '%', 0)).toBe('Ziel 55–70 %')
  })

  it('schreibt einen Punktwert, wo Ober- und Untergrenze zusammenfallen', () => {
    // Die Wassertemperatur kommt als Tag/Nacht-Paar; sind beide gleich, waere
    // „Ziel 20,0–20,0" eine Spanne, die keine ist.
    expect(targetLabel(20, 20, '°C', 1)).toBe('Ziel 20,0 °C')
  })

  it('schweigt ohne Zielbereich', () => {
    expect(targetLabel(null, null)).toBeNull()
  })

  it('schreibt ein Band nur so genau, wie es ist', () => {
    // Eingetragen wurde 22 und 28 — „22,0–28,0" liest sich genauer als die Zahl.
    expect(bandText(22, 28, 1)).toBe('22–28')
    // Eine eingetippte Stelle bleibt dagegen stehen.
    expect(bandText(22.5, 28, 1)).toBe('22,5–28')
    // Rechenrauschen faellt weg.
    expect(bandText(22.700000000000003, 28, 1)).toBe('22,7–28')
  })

  it('schreibt halbe Baender und schweigt ohne', () => {
    expect(bandText(3, null, 1)).toBe('≥ 3')
    expect(bandText(null, 60, 0)).toBe('≤ 60')
    expect(bandText(null, null)).toBeNull()
  })

  it('sagt den Status in Worten', () => {
    expect(statusLabel('ok')).toBe('im Ziel')
    expect(statusLabel('crit')).toBe('kritisch')
  })
})
