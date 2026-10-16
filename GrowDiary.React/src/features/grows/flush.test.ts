import { describe, expect, it } from 'vitest'
import {
  bluetewochenBisFlush, blueteSpaltenZahl, finishBeginn, flushTageGueltig, flushWochenText, planHinweis, planHinweisText,
} from './flush'

describe('Flush-Dauer', () => {
  it('kennt nur 7 und 14 Tage — alles andere ist die Vorgabe zwei Wochen', () => {
    expect(flushTageGueltig(7)).toBe(7)
    expect(flushTageGueltig(14)).toBe(14)
    for (const unsinn of [0, 10, 21, -7, null, undefined]) expect(flushTageGueltig(unsinn)).toBe(14)
    expect(flushWochenText(7)).toBe('1 Woche')
    expect(flushWochenText(14)).toBe('2 Wochen')
    expect(flushWochenText(null)).toBe('2 Wochen')
  })

  it('rechnet den Finish-Beginn wie der Server: Flip + Maximum × 7 − Flush', () => {
    // Mimosa EVO: Flip 23.08.2026, Sorte 9–10 Wochen.
    expect(finishBeginn('2026-08-23', 10, 14)?.toDateString()).toBe(new Date('2026-10-18T00:00:00').toDateString())
    expect(finishBeginn('2026-08-23', 10, 7)?.toDateString()).toBe(new Date('2026-10-25T00:00:00').toDateString())
    expect(finishBeginn(null, 10, 7)).toBeNull()
    expect(finishBeginn('2026-08-23', null, 7)).toBeNull()
    expect(finishBeginn('kein Datum', 10, 7)).toBeNull()
  })

  it('zählt die Blütewochen bis zum Flush', () => {
    expect(bluetewochenBisFlush(10, 14)).toBe(8)
    expect(bluetewochenBisFlush(10, 7)).toBe(9)
    expect(bluetewochenBisFlush(12, 14)).toBe(10)
    expect(bluetewochenBisFlush(null, 14)).toBeNull()
    expect(bluetewochenBisFlush(1, 14)).toBe(1) // nie unter eine Woche
  })

  it('zählt nur nummerierte Blütewochen eines Plans', () => {
    const spalten = [
      { stage: 'Veg', week: 1 }, { stage: 'Flower', week: 1 }, { stage: 'flower', week: 2 },
      { stage: 'Finish', week: null }, { stage: 'Flower', week: null },
    ]
    expect(blueteSpaltenZahl(spalten)).toBe(2)
    expect(blueteSpaltenZahl([])).toBeNull()
    expect(blueteSpaltenZahl(null)).toBeNull()
  })
})

describe('Hinweis: Plan gegen Sorte und Flush', () => {
  it('sagt, wenn dem Plan Blütewochen fehlen — mit dem, was beim Speichern passiert', () => {
    const h = planHinweis(9, 8)
    expect(h).toEqual({ art: 'fehlt', imPlan: 8, benoetigt: 9 })
    const text = planHinweisText(h!, 'SKX Canna Aqua (eigen)', '9–10', 7)
    expect(text).toContain('hat 8 Blütewochen')
    expect(text).toContain('9–10 Wochen')
    expect(text).toContain('1 Woche Flush')
    expect(text).toContain('kommt Blütewoche 9 in den Plan')
    expect(text).toContain('Werte aus Blütewoche 8')
  })
  it('nennt bei mehreren fehlenden Wochen den Bereich', () => {
    expect(planHinweisText(planHinweis(10, 8)!, null, '11–12', 14)).toContain('Blütewoche 9 bis 10')
  })
  it('sagt bei überzähligen Wochen, dass nichts gelöscht wird', () => {
    const h = planHinweis(7, 8)
    expect(h?.art).toBe('mehr')
    expect(planHinweisText(h!, 'Plan', '8', 14)).toContain('nichts gelöscht')
  })
  it('schweigt, wenn alles stimmt oder eine Seite unbekannt ist', () => {
    expect(planHinweis(8, 8)).toBeNull()
    expect(planHinweis(null, 8)).toBeNull()
    expect(planHinweis(8, null)).toBeNull()
  })
})
