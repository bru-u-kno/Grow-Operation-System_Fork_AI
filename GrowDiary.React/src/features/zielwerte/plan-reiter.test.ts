import { describe, expect, it } from 'vitest'
import {
  aenderungsZeilen, anfragen, buchText, dosisFehler, dosisGeaendert, mengeFuerVolumen, zeilenAus,
  type PlanSpalte,
} from './plan-reiter'
import type { WochenwertSpalte } from '../wochenplan/wochenwerte-bearbeiten'

const w5: PlanSpalte = {
  id: 'flower-w5', label: 'Flores · Woche 5', stage: 'Flower', week: 5,
  items: [
    { component: 'Aqua Flores A', minMlPerLiter: 2.5, maxMlPerLiter: 2.5 },
    { component: 'Cannaboost', minMlPerLiter: 1, maxMlPerLiter: 1 },
  ],
}

const wochen: WochenwertSpalte[] = [{
  id: 'flower-w5', label: 'Flores · Woche 5', stage: 'Flower', woche: 5, istJetzt: false,
  felder: [{ feld: 'ecTarget', bezeichnung: 'EC', einheit: 'mS/cm', min: 0, max: 5, schritt: 0.1, wert: 1.5, plan: 1.5, geaendert: false }],
}]

describe('Dosierung', () => {
  it('unberührt ist nicht geändert', () => {
    expect(dosisGeaendert(w5.items, zeilenAus(w5.items))).toBe(false)
  })

  it('entfernen und neu hinzufügen wird erkannt und als Anfrage gebaut', () => {
    const zeilen = zeilenAus(w5.items)
    zeilen[1].entfernt = true
    zeilen.push({ name: 'Pro-Silicate', ml: '0,6', entfernt: false, neu: true })
    expect(dosisGeaendert(w5.items, zeilen)).toBe(true)

    const [anfrage] = anfragen([], { 'flower-w5': zeilen }, [w5], { auchInsProgramm: false, programmName: '', grund: ' Organik raus ' })
    expect(anfrage.dosierung).toEqual([
      { komponente: 'Aqua Flores A', mlProLiter: 2.5 },
      { komponente: 'Pro-Silicate', mlProLiter: 0.6 },
    ])
    expect(anfrage.grund).toBe('Organik raus')
    expect(anfrage.programmName).toBeNull()

    const texte = aenderungsZeilen([], { 'flower-w5': zeilen }, [w5], wochen).map((z) => z.text)
    expect(texte).toEqual(['Cannaboost 1 → entfernt', 'Pro-Silicate neu · 0,6 ml/l'])
  })

  it('eine leere neue Zeile zählt nicht', () => {
    const zeilen = [...zeilenAus(w5.items), { name: '', ml: '', entfernt: false, neu: true }]
    expect(dosisGeaendert(w5.items, zeilen)).toBe(false)
  })

  it('meldet doppelte und namenlose Zutaten und falsche Mengen', () => {
    const zeilen = [
      { name: 'CalMag', ml: '0,5', entfernt: false, neu: false },
      { name: 'calmag', ml: '1', entfernt: false, neu: true },
      { name: '', ml: '1', entfernt: false, neu: true },
      { name: 'PK', ml: '99', entfernt: false, neu: true },
    ]
    const saetze = dosisFehler('W5', zeilen)
    expect(saetze).toHaveLength(3)
  })
})

describe('Anfragen und Texte', () => {
  it('Werte und Dosierung einer Woche gehen in EINE Anfrage, mit Programmwahl', () => {
    const zeilen = zeilenAus(w5.items)
    zeilen[0].ml = '2,4'
    const liste = anfragen([{ spalteId: 'flower-w5', feld: 'ecTarget', wert: 1.4 }], { 'flower-w5': zeilen }, [w5],
      { auchInsProgramm: true, programmName: 'Mimosa', grund: '' })
    expect(liste).toHaveLength(1)
    expect(liste[0]).toMatchObject({ werte: [{ feld: 'ecTarget', wert: 1.4 }], auchInsProgramm: true, programmName: 'Mimosa', grund: null })
  })

  it('Wertzeile nennt alt und neu, zurück auf Start heißt so', () => {
    const texte = aenderungsZeilen([{ spalteId: 'flower-w5', feld: 'ecTarget', wert: null }], {}, [w5], wochen)
    expect(texte[0].text).toBe('EC 1,5 → 1,5 (Start)')
  })

  it('rechnet Mengen aufs Volumen', () => {
    expect(mengeFuerVolumen('0,6', 160)).toBe('96 ml')
    expect(mengeFuerVolumen('0,6', null)).toBeNull()
  })

  it('Buchtexte', () => {
    const basis = { id: 1, zeitUtc: '', spalteId: 'flower-w5', ziel: 'grow', grund: null }
    expect(buchText({ ...basis, art: 'dosierung', feld: 'Cannaboost', alt: '1', neu: null }, (f) => f)).toBe('Cannaboost 1 → entfernt')
    expect(buchText({ ...basis, art: 'wert', feld: 'ecTarget', alt: '1.5', neu: '1.4' }, () => 'EC')).toBe('EC 1,5 → 1,4')
  })
})
