import { describe, expect, it } from 'vitest'
import {
  aenderung,
  alleAufVorschlag,
  anteilPlanDosis,
  ecMitDeinenMengen,
  ecTankDanach,
  istGeaendert,
  meineWiederEinsetzen,
  meinsVerfuegbar,
  tagebuchZeile,
  teileText,
  vorbelegungAusLink,
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
const BOOST = zeile('Cannaboost', 160, 'ZusatzImPlan')
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
    // Die Summe bleibt die angesetzte Menge (Befund: 68,8 + 68,8 = 137,6 bei 137,5 L).
    const teile = wasserZeilen(137.5, 'Mixed', 0.5)
    expect(teile.reduce((summe, t) => summe + t.menge, 0)).toBeCloseTo(137.5, 5)
  })

  it('zeigt Änderungen mit Vorzeichen auf deutsch', () => {
    expect(aenderung(1.63, 1.15, 2)).toBe('−0,48')
    expect(aenderung(6.12, 6.15, 2)).toBe('+0,03')
    expect(aenderung(6.1, 6.1, 2)).toBe('±0,00')
    expect(aenderung(null, 6.1, 2)).toBeNull()
  })

  it('baut die Tagebuchzeile, die auch gespeichert wird', () => {
    const { titel, text } = tagebuchZeile({
      titel: 'Wasserwechsel',
      liter: 160,
      wasserName: 'Leitungswasser',
      vorher: { ec: 1.63, ph: 6.12, wt: 20, do: null, orp: null },
      nachher: { ec: 1.15, ph: 6.15, wt: 18.1, do: null, orp: 450 },
      zugaben: [{ name: 'Leitungswasser', menge: 160, einheit: 'L' }, { name: 'Aqua Flores A', menge: 180, einheit: 'ml' }, { name: 'Cannaboost', menge: 0, einheit: 'ml' }],
      notiz: 'Bewusst unter Plan.',
    })
    expect(titel).toBe('Wasserwechsel 160 L Leitungswasser')
    expect(text).toBe('EC 1,63 → 1,15 · pH 6,12 → 6,15 · Wasser 20,0 → 18,1\u00a0°C · ORP — → 450\u00a0mV\nZugaben: Leitungswasser 160\u00a0L · Aqua Flores A 180\u00a0ml\nBewusst unter Plan.')
  })
})

describe('Nachfüllen (A-006, Etappe 3)', () => {
  it('rechnet den Tank danach als Mischung nach Volumen', () => {
    // 180 L Rest bei EC 1,75, dazu 20 L Lösung mit EC 0,5 → (315 + 10) / 200 = 1,625
    expect(ecTankDanach(180, 1.75, 20, 0.5)).toBeCloseTo(1.625, 6)
    // Leerer Tank: die Lösung allein.
    expect(ecTankDanach(0, 1.75, 20, 0.5)).toBeCloseTo(0.5, 6)
    expect(ecTankDanach(null, 1.75, 20, 0.5)).toBeNull()
    expect(ecTankDanach(180, 1.75, 0, 0.5)).toBeNull()
  })

  it('liest die Vorbelegung aus dem Link des Tagebuchs', () => {
    const v = vorbelegungAusLink(new URLSearchParams('growId=1&zeitpunkt=2026-10-03T16:55&ecVorher=1.75&ecNachher=1,61&phVorher=6.02&liter=20&wasser=RO&notiz=Sprung'))
    expect(v).not.toBeNull()
    expect(v!.zeitpunkt).toBe('2026-10-03T16:55')
    expect(v!.liter).toBe('20')
    expect(v!.wasser).toBe('RO')
    expect(v!.vorher).toEqual({ ec: 1.75, ph: 6.02, wt: null })
    expect(v!.nachher).toEqual({ ec: 1.61, ph: null, wt: null })
    expect(v!.quelle).toBe('Sensor')
    expect(v!.notiz).toBe('Sprung')
  })

  it('nimmt einen Zeitpunkt mit Zone und rechnet ihn in Ortszeit um', () => {
    const utc = '2026-10-03T14:55:00Z'
    const v = vorbelegungAusLink(new URLSearchParams(`zeitpunkt=${encodeURIComponent(utc)}`))
    const ort = new Date(utc)
    const zwei = (n: number) => String(n).padStart(2, '0')
    expect(v!.zeitpunkt).toBe(`${ort.getFullYear()}-${zwei(ort.getMonth() + 1)}-${zwei(ort.getDate())}T${zwei(ort.getHours())}:${zwei(ort.getMinutes())}`)
  })

  it('belegt nichts vor, wenn der Link nur den Grow nennt — und verwirft Unsinn', () => {
    expect(vorbelegungAusLink(new URLSearchParams('growId=1'))).toBeNull()
    const v = vorbelegungAusLink(new URLSearchParams('liter=-3&wasser=Bier&zeitpunkt=gestern&quelle=hand'))
    expect(v!.liter).toBeNull()
    expect(v!.wasser).toBeNull()
    expect(v!.zeitpunkt).toBeNull()
    expect(v!.quelle).toBe('Hand')
  })

  it('nennt, was am Nachfüllen hängt — dieselbe Zählung wie beim Wechsel', () => {
    expect(teileText({ vorher: {}, nachher: {}, buchungen: [{ id: 1, artikelId: 1, artikelName: 'A', einheit: 'ml', menge: 3 }], tagebuch: {} }))
      .toBe('2 Messwerten, 1 Buchung und der Tagebuchzeile')
    expect(teileText({ vorher: null, nachher: null, buchungen: [], tagebuch: null })).toBe('nichts weiter')
  })
})
