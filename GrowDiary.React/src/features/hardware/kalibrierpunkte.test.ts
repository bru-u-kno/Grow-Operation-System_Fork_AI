import { describe, expect, it } from 'vitest'
import {
  speicherbarePunkte,
  steilheitProzent,
  steilheitSatz,
  unlesbarePunktFelder,
  vorbelegung,
  STEILHEIT_FAELLIG_UNTER,
} from './kalibrierpunkte'

/**
 * Dieselben Fälle wie `SteilheitAusZweiPunktenTests.cs`.
 *
 * Die Rechnung steht auf beiden Seiten — im Backend, weil die Zahl im Vertrag
 * mitgeht, und hier, damit der Nutzer sie schon beim Tippen sieht. Damit sie
 * nicht auseinanderlaufen, prüfen beide denselben ausgerechneten Fall.
 */
describe('Steilheit aus den Kalibrierpunkten', () => {
  it('rechnet den Beispielfall wie das Backend', () => {
    // (6,82 − 4,15) / (7,00 − 4,01) = 2,67 / 2,99 = 89,3 %
    const steil = steilheitProzent([
      { loesung: 'pH 4,01', sollwert: 4.01, vorher: 4.15, nachher: 4.01 },
      { loesung: 'pH 7,00', sollwert: 7.0, vorher: 6.82, nachher: 7.0 },
    ])

    expect(steil).not.toBeNull()
    expect(Math.abs((steil as number) - 89.3)).toBeLessThan(0.15)
  })

  it('nimmt bei drei Punkten die äussersten', () => {
    // (9,70 − 4,10) / (10,01 − 4,01) = 5,60 / 6,00 = 93,3 %
    const steil = steilheitProzent([
      { loesung: 'pH 7,00', sollwert: 7.0, vorher: 6.9, nachher: 7.0 },
      { loesung: 'pH 4,01', sollwert: 4.01, vorher: 4.1, nachher: 4.01 },
      { loesung: 'pH 10,01', sollwert: 10.01, vorher: 9.7, nachher: 10.01 },
    ])

    expect(steil).not.toBeNull()
    expect(Math.abs((steil as number) - 93.3)).toBeLessThan(0.15)
  })

  it('behauptet aus einem Punkt nichts', () => {
    expect(steilheitProzent([])).toBeNull()
    expect(steilheitProzent([
      { loesung: 'pH 7,00', sollwert: 7.0, vorher: 6.9, nachher: 7.0 },
    ])).toBeNull()
  })

  it('behauptet aus zwei gleichen Lösungen nichts', () => {
    expect(steilheitProzent([
      { loesung: 'pH 7,00', sollwert: 7.0, vorher: 6.9, nachher: 7.0 },
      { loesung: 'pH 7,00', sollwert: 7.0, vorher: 7.1, nachher: 7.0 },
    ])).toBeNull()
  })

  it('nennt im Satz die Faustregel und ihre Herkunft', () => {
    const satz = steilheitSatz(72)
    expect(satz).toContain('Faustregel')
    expect(satz).toContain('fällig')
    expect(satz).toContain(String(STEILHEIT_FAELLIG_UNTER))
  })

  it.each([
    [72, 'fällig'],
    [89.3, 'unter dem üblichen Bereich'],
    [99, 'im üblichen Bereich'],
    [118, 'ungewöhnlich hoch'],
  ])('stuft %s %% als „%s" ein', (prozent, erwartet) => {
    // Eine erste Fassung nannte alles ueber 85 % „im ueblichen Bereich" —
    // auch 89, das nun einmal nicht in 95–105 liegt.
    expect(steilheitSatz(prozent as number)).toContain(erwartet as string)
  })

  it('schreibt die Zahl im Satz mit Komma', () => {
    // „89,3 %" — nicht „89.3 %". Die Zahlen-Pruefung sieht nur den DOM;
    // hier faellt es schon beim Tippen auf.
    expect(steilheitSatz(89.3)).toContain('89,3')
    expect(steilheitSatz(89.3)).not.toContain('89.3')
  })
})

describe('Was gespeichert wird', () => {
  const zeile = (teil: Partial<Record<string, string>> = {}) =>
    ({ loesung: '', sollText: '', vorherText: '', nachherText: '', ...teil })

  it('lässt leere Zeilen weg', () => {
    expect(speicherbarePunkte([zeile(), zeile({ loesung: 'pH 7,00' })])).toHaveLength(0)
  })

  it('nimmt eine Zeile mit, sobald eine Zahl darin steht', () => {
    expect(speicherbarePunkte([zeile({ vorherText: '6,82' })])).toHaveLength(1)
  })

  it('liest das Komma', () => {
    const punkte = speicherbarePunkte([zeile({ sollText: '4,01', vorherText: '4,15' })])
    expect(punkte[0].sollwert).toBeCloseTo(4.01, 3)
    expect(punkte[0].vorher).toBeCloseTo(4.15, 3)
  })
})

describe('Vorbelegung', () => {
  it('schlägt für pH zwei Punkte vor', () => {
    const zeilen = vorbelegung('pH-Sonde')
    expect(zeilen).toHaveLength(2)
    expect(zeilen.map((z) => z.loesung)).toEqual(['pH 4,01', 'pH 7,00'])
  })

  it('schreibt die Sollwerte mit Komma', () => {
    for (const zeile of vorbelegung('pH-Sonde')) {
      expect(zeile.sollText).not.toContain('.')
    }
  })

  it('schlägt für EC einen Punkt vor', () => {
    expect(vorbelegung('EC-Sonde')).toHaveLength(1)
  })
})

describe('Unlesbare Kalibrierpunkte (B9)', () => {
  const zeile = (soll: string, vorher: string, nachher = '') =>
    ({ loesung: '', sollText: soll, vorherText: vorher, nachherText: nachher })

  it('nennt das Feld so, wie es am Eingabefeld steht', () => {
    expect(unlesbarePunktFelder([zeile('4,01', '4,15'), zeile('7,00', '6,8x')]),
      '„6,8x" machte den Punkt still zu null — und die Steilheit verschwand').toEqual(['Vorher 2'])
  })

  it('meldet nichts bei lesbaren und leeren Feldern', () => {
    expect(unlesbarePunktFelder([zeile('4,01', '4,15'), zeile('', '')])).toEqual([])
  })
})
