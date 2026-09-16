import { describe, expect, it } from 'vitest'
import { dosierungZeilen, zeilen, zeitleiste, type Auswertung, type AuswertungWoche } from './plan-auswertung'
import { buchText } from './plan-reiter'

const leer = { ecTarget: null, ecMin: null, ecMax: null, phMin: null, phMax: null, waterTempDayC: null, waterTempNightC: null, rhMax: null, airTempC: null, orpMin: null, orpMax: null }

function woche(teil: Partial<AuswertungWoche>): AuswertungWoche {
  return {
    id: 'flower-w4', label: 'Flores · Woche 4', stage: 'Flower', week: 4, von: null, bis: null,
    start: { ...leer, ecTarget: 1.4, ecMin: 1.3, ecMax: 1.5, rhMax: 60 },
    ende: { ...leer, ecTarget: 1.4, ecMin: 1.3, ecMax: 1.5, rhMax: 60 },
    gemessen: { ec: 1.63, ph: null, wasser: null, rh: 65, luft: null, orp: null },
    messungen: 3, dosierungStart: [], dosierungEnde: [],
    ...teil,
  }
}

const basis = (wochen: AuswertungWoche[]): Auswertung => ({
  growId: 1, growName: '2026-01', programmName: 'SKX', startProgrammName: 'SKX', eingefroren: false,
  eingefrorenUtc: null, startVermerk: null, wochen, buch: [],
})

describe('Auswertung', () => {
  it('EC 1,63 liegt innerhalb Band ± 0,2 — keine Abweichung', () => {
    const [z] = zeilen(basis([woche({})]), 'ec')
    expect(z).toMatchObject({ start: '1,4', ende: '1,4', geaendert: false, gemessen: '1,63', abweichung: false })
  })

  it('Feuchte 65 über max 60 ist eine Abweichung', () => {
    const [z] = zeilen(basis([woche({})]), 'rh')
    expect(z).toMatchObject({ ende: '≤ 60', gemessen: '65', abweichung: true })
  })

  it('geänderte Woche wird erkannt', () => {
    const [z] = zeilen(basis([woche({ ende: { ...leer, ecTarget: 1.3, ecMin: 1.2, ecMax: 1.4 } })]), 'ec')
    expect(z.geaendert).toBe(true)
    expect(z.abweichung).toBe(true)   // 1,63 > 1,4 + 0,2
  })

  it('Dosierung zeigt Start und Ende', () => {
    const w = woche({
      dosierungStart: [{ component: 'Cannaboost', minMlPerLiter: 1, maxMlPerLiter: 1 }],
      dosierungEnde: [{ component: 'Pro-Silicate', minMlPerLiter: 0.6, maxMlPerLiter: 0.6 }],
    })
    expect(dosierungZeilen(basis([w]))[0]).toMatchObject({ start: 'Cannaboost 1', ende: 'Pro-Silicate 0,6', geaendert: true })
  })

  it('Zeitleiste ohne „angelegt“, mit lesbaren Texten für die neuen Arten', () => {
    const a = basis([])
    const e = { id: 1, zeitUtc: '', spalteId: null, feld: null, ziel: null, grund: null }
    a.buch = [
      { ...e, art: 'angelegt', alt: null, neu: 'SKX' },
      { ...e, art: 'programmwechsel', alt: 'SKX', neu: 'Athena' },
      { ...e, art: 'eingefroren', alt: null, neu: 'abgeschlossen' },
    ]
    const texte = zeitleiste(a).map((x) => buchText(x, (f) => f))
    expect(texte).toEqual(['Programmwechsel SKX → Athena', 'Plan eingefroren · abgeschlossen'])
  })
})
