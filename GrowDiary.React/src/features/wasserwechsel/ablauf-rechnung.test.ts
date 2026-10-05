import { describe, expect, it } from 'vitest'
import {
  aenderung,
  alleAufVorschlag,
  anteilPlanDosis,
  ecMitDeinenMengen,
  istGeaendert,
  meineWiederEinsetzen,
  meinsVerfuegbar,
  tagebuchZeile,
  wasserZeilen,
  wertDerZeile,
  type AblaufZeile,
} from './ablauf-rechnung'

/** Brus Wechsel vom 04.10.2026: 160 L, Plan Blütewoche 7. */
const zeile = (name: string, vorschlagMl: number | null, rolle: AblaufZeile['rolle'] = 'Grundduenger', art: AblaufZeile['art'] = 'plan'): AblaufZeile => ({
  schluessel: `${art}:${name}`, name, art, rolle, vorschlagMl, hinweis: null, artikelId: 1, einheit: 'ml',
})
const A = zeile('Aqua Flores A', 320)
const B = zeile('Aqua Flores B', 320)
const PK = zeile('PK 13/14', 160)
const BOOST = zeile('Cannaboost', 160, 'Zusatz')
const PURO = zeile('Purolyt', null, null, 'extra')
const ZEILEN = [A, B, PK, BOOST, PURO]
const BRU = { 'plan:Aqua Flores A': '180', 'plan:Aqua Flores B': '180', 'plan:PK 13/14': '80', 'plan:Cannaboost': '0', 'extra:Purolyt': '200' }

describe('Vorschlag und eigene Werte', () => {
  it('steht auf dem Vorschlag, bis jemand ändert', () => {
    expect(wertDerZeile(A, {})).toBe('320')
    expect(istGeaendert(A, {})).toBe(false)
    expect(istGeaendert(A, BRU)).toBe(true)
    // Derselbe Wert wie der Vorschlag ist keine Änderung.
    expect(istGeaendert(A, { 'plan:Aqua Flores A': '320' })).toBe(false)
    // Zeilen „nicht im Plan" haben keinen Vorschlag, also nichts zu ändern.
    expect(istGeaendert(PURO, BRU)).toBe(false)
  })

  it('„Alles auf Vorschlag" lässt die Zeilen nicht im Plan stehen', () => {
    const neu = alleAufVorschlag(ZEILEN, BRU)
    expect(neu).toEqual({ 'extra:Purolyt': '200' })
  })

  it('merkt die eigenen Werte und setzt sie zurück — einzeln und alle', () => {
    const nachZurueck = alleAufVorschlag(ZEILEN, BRU)
    expect(meinsVerfuegbar(A, nachZurueck, BRU)).toBe(true)
    expect(meinsVerfuegbar(PURO, nachZurueck, BRU)).toBe(false)
    expect(meineWiederEinsetzen(ZEILEN, nachZurueck, BRU)).toEqual(BRU)
    // Ein gemerkter Wert gleich dem Vorschlag wird nicht angeboten.
    expect(meinsVerfuegbar(A, {}, { 'plan:Aqua Flores A': '320' })).toBe(false)
  })
})

describe('Mit deinen Mengen', () => {
  it('rechnet den Anteil der Plan-Dosis nur über die Grunddünger', () => {
    // (180 + 180 + 80) / (320 + 320 + 160) = 0,55 — Cannaboost 0 zählt nicht mit.
    expect(anteilPlanDosis(ZEILEN, BRU)).toBeCloseTo(0.55, 5)
    expect(anteilPlanDosis(ZEILEN, {})).toBe(1)
    expect(anteilPlanDosis([BOOST], {})).toBeNull()
  })

  it('schätzt den EC als Wasser + Dünger × Anteil', () => {
    expect(ecMitDeinenMengen(0.5, 1.2, 0.55)).toBeCloseTo(1.16, 5)
    expect(ecMitDeinenMengen(null, 1.2, 1)).toBeNull()
  })
})

describe('Wasser und Tagebuch', () => {
  it('bucht das Wasser nach Wasserart, bei der Mischung zweimal', () => {
    expect(wasserZeilen(160, 'Tap', 0)).toEqual([{ wasser: 'Tap', name: 'Leitungswasser', menge: 160 }])
    expect(wasserZeilen(160, 'RO', 1)).toEqual([{ wasser: 'RO', name: 'Osmosewasser', menge: 160 }])
    expect(wasserZeilen(160, 'Mixed', 0.3)).toEqual([
      { wasser: 'RO', name: 'Osmosewasser', menge: 48 },
      { wasser: 'Tap', name: 'Leitungswasser', menge: 112 },
    ])
  })

  it('zeigt Änderungen mit Vorzeichen auf deutsch', () => {
    expect(aenderung(1.63, 1.15, 2)).toBe('−0,48')
    expect(aenderung(6.12, 6.15, 2)).toBe('+0,03')
    expect(aenderung(6.1, 6.1, 2)).toBe('±0,00')
    expect(aenderung(null, 6.1, 2)).toBeNull()
  })

  it('baut die Tagebuchzeile, die auch gespeichert wird', () => {
    const { titel, text } = tagebuchZeile({
      liter: 160,
      wasserName: 'Leitungswasser',
      vorher: { ec: 1.63, ph: 6.12, wt: 20, do: null, orp: null },
      nachher: { ec: 1.15, ph: 6.15, wt: 18.1, do: null, orp: 450 },
      zugaben: [{ name: 'Leitungswasser', menge: 160, einheit: 'L' }, { name: 'Aqua Flores A', menge: 180, einheit: 'ml' }, { name: 'Cannaboost', menge: 0, einheit: 'ml' }],
      notiz: 'Bewusst unter Plan.',
    })
    expect(titel).toBe('Wasserwechsel 160 L Leitungswasser')
    expect(text).toBe('EC 1,63 → 1,15 · pH 6,12 → 6,15 · Wasser 20,0 → 18,1 °C · ORP — → 450 mV\nZugaben: Leitungswasser 160 L · Aqua Flores A 180 ml\nBewusst unter Plan.')
  })
})
