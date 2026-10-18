import { describe, expect, it } from 'vitest'
import { istGemischt, sortenAufzaehlung, sortenText, zuechterPasst } from './sorten-text'

/**
 * Wie die Sorte eines Grows genannt wird — und wann der Züchter dazugehört.
 *
 * <b>Der Anlass (01.09.2026).</b> Der Tester hat definiert, dass ein Grow N
 * Sorten führen kann. Sechs Ansichten gaben trotzdem `strain` aus — ein Feld,
 * ein Name. Nach dem ersten Umbau blieb ein Rest: die Detailseite schrieb
 * „Gorilla Glue (Testdaten) · Royal Queen Seeds" — richtige Sorte, Züchter der
 * <i>anderen</i>. Beide Male vom Prüfer am laufenden Stand gefunden.
 *
 * <b>Warum diese Schicht.</b> Die Regel ist eine Entscheidung, keine Anzeige.
 * Sie hier zu prüfen kostet Millisekunden; sie über sechs Seiten zu prüfen
 * kostet einen E2E-Lauf je Seite — und die erste Fassung stand trotzdem an
 * fünf Stellen falsch da, weil jede sie neu gebaut hatte.
 */
describe('sortenText', () => {
  it('ohne erfasste Pflanzen gilt die Hauptsorte', () => {
    expect(sortenText({ strain: 'White Widow', pflanzenSorten: [] })).toBe('White Widow')
    expect(sortenText({ strain: 'White Widow' })).toBe('White Widow')
  })

  it('ohne alles kommt null — und keine erfundene Angabe', () => {
    expect(sortenText({ strain: null, pflanzenSorten: [] })).toBeNull()
  })

  it('eine erfasste Sorte gewinnt gegen die Hauptsorte', () => {
    /* Der Fall, den die erste Fassung der Detailseite anders entschied: sie
       fiel bei genau EINER Pflanzensorte auf `strain` zurück. Setzt man alle
       Töpfe auf Gorilla Glue, sagte die Kachel oben „White Widow", während
       dieselbe Seite unten „4x Gorilla Glue" schrieb. */
    expect(sortenText({ strain: 'White Widow', pflanzenSorten: ['Gorilla Glue'] }))
      .toBe('Gorilla Glue')
  })

  it('mehrere Sorten heissen „gemischt (N)"', () => {
    expect(sortenText({ strain: 'White Widow', pflanzenSorten: ['A', 'B', 'C'] }))
      .toBe('gemischt\u00a0(3)')
  })
})

describe('sortenAufzaehlung', () => {
  it('zaehlt erst ab zwei Sorten auf', () => {
    expect(sortenAufzaehlung({ pflanzenSorten: ['A', 'B'] })).toBe('A · B')
    expect(sortenAufzaehlung({ pflanzenSorten: ['A'] })).toBeNull()
    expect(sortenAufzaehlung({ pflanzenSorten: [] })).toBeNull()
  })
})

describe('istGemischt', () => {
  it('erst ab zwei', () => {
    expect(istGemischt({ pflanzenSorten: ['A', 'B'] })).toBe(true)
    expect(istGemischt({ pflanzenSorten: ['A'] })).toBe(false)
    expect(istGemischt({})).toBe(false)
  })
})

describe('zuechterPasst', () => {
  it('ohne erfasste Pflanzen gehoert der Zuechter zur Hauptsorte', () => {
    expect(zuechterPasst({ strain: 'White Widow', pflanzenSorten: [] })).toBe(true)
  })

  it('bei mehreren Sorten kann ein Zuechter nicht fuer alle stehen', () => {
    expect(zuechterPasst({ strain: 'White Widow', pflanzenSorten: ['A', 'B'] })).toBe(false)
  })

  it('dieselbe Sorte, auch wenn die Bibliothek sie ausfuehrlicher fuehrt', () => {
    // „White Widow" im Freitext, „White Widow (Testdaten)" in der Bibliothek.
    // Entschieden wird das NICHT hier, sondern vom Server ueber die Ids.
    expect(zuechterPasst({
      strain: 'White Widow', pflanzenSorten: ['White Widow (Testdaten)'], nurHauptsorte: true,
    })).toBe(true)
  })

  it('eine ANDERE Sorte nimmt den Zuechter mit', () => {
    // Alle Töpfe auf Gorilla Glue, Hauptsorte White Widow —
    // „Gorilla Glue · Royal Queen Seeds" wäre falsch.
    expect(zuechterPasst({
      strain: 'White Widow', pflanzenSorten: ['Gorilla Glue (Testdaten)'], nurHauptsorte: false,
    })).toBe(false)
  })

  it('ein Name, der den anderen ENTHAELT, taeuscht sie nicht mehr', () => {
    /* Der Fall, an dem die erste Fassung zerbrach: „Northern Lights" (Sensi
       Seeds) als Hauptsorte, „Northern Lights Auto" (Fast Buds) in den Töpfen.
       Der Namensvergleich sagte „passt" und schrieb den falschen Züchter
       daneben. Der Server vergleicht Ids und sagt nein. */
    expect(zuechterPasst({
      strain: 'Northern Lights', pflanzenSorten: ['Northern Lights Auto'], nurHauptsorte: false,
    })).toBe(false)
  })

  it('ohne Auskunft des Servers schweigt sie', () => {
    // Im Zweifel keine Angabe — eine fehlende ist besser als eine falsche.
    expect(zuechterPasst({ strain: 'White Widow', pflanzenSorten: ['Irgendwas'] })).toBe(false)
    expect(zuechterPasst({ strain: null, pflanzenSorten: ['Gorilla Glue'], nurHauptsorte: false }))
      .toBe(false)
  })
})
