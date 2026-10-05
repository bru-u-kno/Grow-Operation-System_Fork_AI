import { describe, expect, it } from 'vitest'
import type { TagebuchSprungDto } from '../../types'
import { toLocalInputValue } from '../../utils'
import { vorbelegungAusLink } from '../vorgang/ablauf-rechnung'
import { nachfuellenVorbelegung, nachfuellenWeg } from './nachfuellen-weg'

function sprung(teil: Partial<TagebuchSprungDto>): TagebuchSprungDto {
  return {
    id: 1, messgroesse: 'reservoir-ec', name: 'EC', einheit: 'mS/cm', nachkomma: 2, vorher: 1.75, nachher: 1.61,
    beginnUtc: '2026-10-03T14:50:00Z', endeUtc: '2026-10-03T14:55:00Z', beginnUhrzeit: '16:50', endeUhrzeit: '16:55',
    beginnOrtszeit: '2026-10-03T16:50', endeOrtszeit: '2026-10-03T16:55', dauerMinuten: 5, regel: 'Testregel',
    ...teil,
  }
}

/**
 * „Nachfüllen eintragen" im Tagebuch führt in den Nachfüll-Ablauf — und was
 * der Knopf in die Adresse schreibt, muss der Ablauf auch so lesen. Geprüft
 * wird deshalb der ganze Weg: Befund → Adresse → `vorbelegungAusLink`.
 */
describe('Nachfüllen eintragen → Nachfüll-Ablauf (A-006, Etappe 3)', () => {
  const befunde = [
    sprung({}),
    sprung({ id: 2, messgroesse: 'reservoir-ph', name: 'pH', einheit: null, vorher: 6.02, nachher: 5.9 }),
    sprung({ id: 3, messgroesse: 'reservoir-level', name: 'Wasserstand', einheit: 'L', vorher: 180, nachher: 192.5 }),
  ]

  it('führt auf /addback mit dem Grow', () => {
    const weg = nachfuellenWeg('7', nachfuellenVorbelegung(befunde))
    expect(weg.art).toBe('adresse')
    expect(weg.to.startsWith('/addback?')).toBe(true)
    expect(new URLSearchParams(weg.to.split('?')[1]).get('growId')).toBe('7')
  })

  it('der Ablauf liest genau die Werte, die der Befund hatte — Komma wird zum Punkt, nicht zum Tausender', () => {
    const weg = nachfuellenWeg('7', nachfuellenVorbelegung(befunde))
    const v = vorbelegungAusLink(new URLSearchParams(weg.to.split('?')[1]))!
    expect(v.vorher).toEqual({ ec: 1.75, ph: 6.02, wt: null })
    expect(v.nachher).toEqual({ ec: 1.61, ph: 5.9, wt: null })
    expect(v.liter).toBe('12,5')
    expect(v.quelle).toBe('Sensor')
    expect(v.zeitpunkt).toBe(toLocalInputValue(new Date('2026-10-03T14:55:00Z')))
    expect(v.notiz).toContain('Nachgetragen aus dem Tagebuch')
  })

  it('lässt leere Werte weg statt 0 zu schreiben', () => {
    const weg = nachfuellenWeg('7', nachfuellenVorbelegung([sprung({})]))
    const suche = new URLSearchParams(weg.to.split('?')[1])
    expect(suche.has('liter')).toBe(false)
    expect(suche.has('phVorher')).toBe(false)
    expect(vorbelegungAusLink(suche)!.liter).toBeNull()
  })
})
