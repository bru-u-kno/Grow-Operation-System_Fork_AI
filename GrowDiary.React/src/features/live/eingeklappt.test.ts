import { describe, expect, it } from 'vitest'
import type { MetricPayload } from '../../types'
import { bereichsBilanz, bilanzText, leseEingeklappt, umschaltenEingeklappt } from './eingeklappt'

function metrik(teil: Partial<MetricPayload> & { key: string }): MetricPayload {
  return { label: teil.key, value: '–', tone: 'default', hint: null, ...teil } as MetricPayload
}

describe('Eingeklappt einlesen', () => {
  it('startet ohne Eintrag mit allem offen', () => {
    expect(leseEingeklappt(null).size).toBe(0)
  })

  it('liest, was gespeichert wurde', () => {
    const roh = JSON.stringify(['bereich:seed-1', 'kachel:vpd-0'])
    expect([...leseEingeklappt(roh)].sort()).toEqual(['bereich:seed-1', 'kachel:vpd-0'])
  })

  it('verwirft Unlesbares statt die Seite zu brechen', () => {
    expect(leseEingeklappt('{kaputt').size).toBe(0)
    expect(leseEingeklappt(JSON.stringify({ nicht: 'liste' })).size).toBe(0)
    expect([...leseEingeklappt(JSON.stringify(['panel:kamera', 7, null]))]).toEqual(['panel:kamera'])
  })
})

describe('Umschalten', () => {
  it('schaltet hin und zurück, ohne die alte Menge zu verändern', () => {
    const vorher = new Set(['panel:kamera'])
    const zu = umschaltenEingeklappt(vorher, 'kachel:co2')
    expect([...zu].sort()).toEqual(['kachel:co2', 'panel:kamera'])
    expect([...vorher]).toEqual(['panel:kamera'])
    const wiederOffen = umschaltenEingeklappt(zu, 'kachel:co2')
    expect([...wiederOffen]).toEqual(['panel:kamera'])
  })
})

describe('Bilanz eines eingeklappten Bereichs', () => {
  // Werte wie in der Anlage am 03.10.2026: EC knapp über dem Ziel, pH an der
  // Obergrenze des Ziels, CO₂ unter der Meldegrenze, Wasserstand ohne Ziel.
  const ph = metrik({ key: 'reservoir-ph', numericValue: 6.2, targetMin: 5.8, targetMax: 6.2, alarmMin: 5.6, alarmMax: 6.4 })
  const ec = metrik({ key: 'reservoir-ec', numericValue: 1.73, targetMin: 1.5, targetMax: 1.7, alarmMin: 1.3, alarmMax: 1.9 })
  const co2 = metrik({ key: 'co2', numericValue: 545, targetMin: 1200, targetMax: 1400, alarmMin: 600, alarmMax: 2000 })
  const ohneZiel = metrik({ key: 'reservoir-level', numericValue: 145 })

  it('zählt Warnungen und Grenzen mit derselben Lesart wie die Kachel', () => {
    expect(bereichsBilanz([ph, ec, co2, ohneZiel])).toEqual({ werte: 4, warnungen: 1, kritisch: 1 })
  })

  it('nennt im Text nur, was es gibt', () => {
    expect(bilanzText({ werte: 4, warnungen: 0, kritisch: 0 })).toBe('4 Werte')
    expect(bilanzText({ werte: 1, warnungen: 1, kritisch: 0 })).toBe('1 Wert · 1 daneben')
    expect(bilanzText({ werte: 6, warnungen: 2, kritisch: 1 })).toBe('6 Werte · 3 daneben, davon 1 an der Grenze')
    expect(bilanzText({ werte: 2, warnungen: 0, kritisch: 2 })).toBe('2 Werte · 2 daneben, davon 2 an der Grenze')
  })
})
