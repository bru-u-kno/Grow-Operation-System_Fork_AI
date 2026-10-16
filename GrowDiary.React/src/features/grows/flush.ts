/**
 * Flush vor der Ernte: eine oder zwei Wochen, je Grow gewählt.
 *
 * Der Server rechnet den Finish-Beginn als Ernte laut Sorte (Maximum der
 * Züchterwochen) minus Flush-Tage (`Phasenanker`). Hier steht, was die
 * Oberfläche daraus anzeigt — und wie viele Blütewochen der Plan dafür braucht.
 */
export const FLUSH_KURZ = 7
export const FLUSH_STANDARD = 14

/** Jeder andere Wert als 7 gilt als die Vorgabe 14 — wie im Server (`GrowRun.FlushTageGueltig`). */
export function flushTageGueltig(tage: number | null | undefined): number {
  return tage === FLUSH_KURZ ? FLUSH_KURZ : FLUSH_STANDARD
}

export function flushWochenText(tage: number | null | undefined): string {
  return flushTageGueltig(tage) === FLUSH_KURZ ? '1 Woche' : '2 Wochen'
}

/**
 * Wie viele volle Blütewochen vor dem Flush liegen: Ernte (Maximum der Sorte)
 * minus Flush, in Wochen. `null` ohne Züchterwochen.
 */
export function bluetewochenBisFlush(maxWochen: number | null | undefined, flushTage: number | null | undefined): number | null {
  if (maxWochen == null || maxWochen <= 0) return null
  return Math.max(1, Math.ceil((maxWochen * 7 - flushTageGueltig(flushTage)) / 7))
}

const TAG_MS = 86_400_000

/** Finish-Beginn: Flip + Maximum × 7 − Flush-Tage; `null`, wenn Flip oder Wochen fehlen. */
export function finishBeginn(flipIso: string | null | undefined, maxWochen: number | null | undefined, flushTage: number | null | undefined): Date | null {
  if (!flipIso || maxWochen == null || maxWochen <= 0) return null
  const flip = new Date(`${flipIso.slice(0, 10)}T00:00:00`)
  if (Number.isNaN(flip.getTime())) return null
  return new Date(flip.getTime() + (maxWochen * 7 - flushTageGueltig(flushTage)) * TAG_MS)
}

export function kurzDatum(datum: Date): string {
  return datum.toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric' })
}

/** Wie viele nummerierte Blütewochen führt ein Plan (oder ein Programm)? */
export function blueteSpaltenZahl(spalten: ReadonlyArray<{ stage: string; week?: number | null }> | null | undefined): number | null {
  if (!spalten) return null
  const n = spalten.filter((s) => s.stage.toLowerCase() === 'flower' && s.week != null).length
  return n > 0 ? n : null
}

export type PlanHinweis =
  | { art: 'fehlt'; imPlan: number; benoetigt: number }
  | { art: 'mehr'; imPlan: number; benoetigt: number }

/**
 * Vergleicht die Blütewochen, die Sorte und Flush verlangen, mit denen des Plans.
 * `null`, wenn eine Seite unbekannt ist oder beides stimmt.
 */
export function planHinweis(benoetigt: number | null, imPlan: number | null): PlanHinweis | null {
  if (benoetigt == null || imPlan == null) return null
  if (benoetigt > imPlan) return { art: 'fehlt', imPlan, benoetigt }
  if (benoetigt < imPlan) return { art: 'mehr', imPlan, benoetigt }
  return null
}

export function planHinweisText(h: PlanHinweis, planName: string | null, spanne: string | null, flushTage: number): string {
  const plan = planName ? `Dein Düngeplan „${planName}“` : 'Dein Düngeplan'
  const flush = `${flushWochenText(flushTage)} Flush`
  if (h.art === 'fehlt') {
    const neue = h.benoetigt - h.imPlan
    const bis = neue === 1 ? `Blütewoche ${h.benoetigt}` : `Blütewoche ${h.imPlan + 1} bis ${h.benoetigt}`
    return `${plan} hat ${h.imPlan} Blütewochen. Die Sorte braucht ${spanne ?? 'mehr'} Wochen — mit ${flush} sind das ${h.benoetigt} Blütewochen. `
      + `Beim Speichern kommt ${bis} in den Plan (Werte aus Blütewoche ${h.imPlan}) — bitte danach prüfen.`
  }
  return `${plan} führt ${h.imPlan} Blütewochen, nötig sind ${h.benoetigt}. Die überzähligen bleiben stehen; es wird nichts gelöscht.`
}
