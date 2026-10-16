/**
 * Züchterwochen eines Grows: eigener Wert oder die Sorte?
 *
 * Bis forkai.226 kopierte das Formular die Wochen der Sorte ins Grow und zog sie
 * nie nach (Mimosa EVO: Sorte 9–10, Grow 8–9, Finish eine Woche zu früh). Jetzt
 * folgt das Grow der Sorte, solange es keinen bewusst gesetzten Wert hat. Der
 * Server setzt den wirksamen Wert beim Lesen ein; hier steht nur, was die
 * Oberfläche daraus macht.
 */
export interface Wochen {
  min: number | null
  max: number | null
}

/** „9–10", „9" oder „–" — mit Halbgeviertstrich, wie überall in der App. */
export function wochenText({ min, max }: Wochen): string {
  if (min == null && max == null) return '–'
  if (min == null || max == null || min === max) return String(max ?? min)
  return `${min}–${max}`
}

export function gleicheWochen(a: Wochen, b: Wochen): boolean {
  return (a.min ?? null) === (b.min ?? null) && (a.max ?? null) === (b.max ?? null)
}

/**
 * Um wie viele Tage rückt der Finish-Beginn, wenn statt der Sorte der eigene
 * Wert gilt? Negativ = früher. Gerechnet wird wie im Server (`Phasenanker`):
 * Flip + Maximum × 7 − 14 Tage, ohne Maximum das Minimum.
 */
export function finishVerschiebungTage(eigen: Wochen, sorte: Wochen): number | null {
  const e = eigen.max ?? eigen.min
  const s = sorte.max ?? sorte.min
  if (e == null || s == null) return null
  return (e - s) * 7
}

/** Eingabe „9" → 9; leer oder Unsinn → null. */
export function wochenZahl(text: string): number | null {
  const zahl = Number.parseInt(text, 10)
  return Number.isFinite(zahl) && zahl > 0 ? zahl : null
}

/**
 * Ob im Formular ein eigener Wert gilt. Ohne Sorte immer; sonst nur, wenn er
 * ausdrücklich gesetzt wurde — ein leeres Feld heißt „folge der Sorte".
 */
export function istEigen(strainId: number | null, breederWeeksEigen: boolean | null | undefined, sorteBekannt: boolean): boolean {
  if (strainId == null || !sorteBekannt) return true
  return breederWeeksEigen === true
}

/** Was eine Grow-Zeile für die Wirkung einer Sortenänderung wissen muss. */
export interface GrowFuerWirkung {
  id: number
  name: string
  status: string
  seedType: string
  strainId: number | null
  breederWeeksEigen: boolean
  breederFlowerWeeksMin: number | null
  breederFlowerWeeksMax: number | null
  phasenanker?: { finishAb?: string | null; finishBestaetigt?: boolean } | null
}

export interface WirkungFolgt {
  name: string
  /** Verschiebung des Finish-Beginns in Tagen; null, wenn sich nichts rechnen lässt. */
  tage: number | null
  von: Date | null
  nach: Date | null
  /** Finish ist schon beobachtet — die Sorte ändert daran nichts mehr. */
  bestaetigt: boolean
}

export interface Wirkung {
  folgt: WirkungFolgt[]
  bleibt: { name: string; wochen: Wochen }[]
}

const TAG_MS = 86_400_000
/** Ab dieser Wochenzahl greift der Mindestabstand des Servers nicht mehr (Flip + n × 7 − 14 > Flip + 10). */
const MINDESTWOCHEN_FUER_RECHNUNG = 4

/**
 * Was eine geänderte Sorte bei den laufenden Grows bewirkt: wer ihr folgt und
 * um wie viel sich dort der Finish-Beginn verschiebt — und wer bei seinem
 * eigenen Wert bleibt. Gezeigt VOR dem Speichern, damit eine Änderung nie wieder
 * unbemerkt an einem Grow vorbeigeht.
 */
export function wirkungAufGrows(grows: GrowFuerWirkung[], strainId: number, alt: Wochen, neu: Wochen): Wirkung {
  const wirkung: Wirkung = { folgt: [], bleibt: [] }
  if (gleicheWochen(alt, neu)) return wirkung
  for (const grow of grows) {
    if (grow.strainId !== strainId) continue
    if (grow.status !== 'Planning' && grow.status !== 'Running') continue
    // Die Autoflower rechnet nicht mit Züchterwochen.
    if (grow.seedType === 'Autoflower') continue
    if (grow.breederWeeksEigen) {
      wirkung.bleibt.push({ name: grow.name, wochen: { min: grow.breederFlowerWeeksMin, max: grow.breederFlowerWeeksMax } })
      continue
    }
    const bestaetigt = grow.phasenanker?.finishBestaetigt === true
    // Der Server hält den Finish-Beginn bei kurzen Wochenzahlen am Mindestabstand
    // zum Flip fest (Phasenanker: 10 Tage Übergang, 14 Tage vor der Ernte — ab
    // etwa 4 Wochen greift die Klemme nicht mehr). Darunter stimmt „Tage × 7"
    // nicht; dort wird keine Verschiebung behauptet.
    const rechenbar = [alt, neu].every((w) => (w.max ?? w.min ?? 0) >= MINDESTWOCHEN_FUER_RECHNUNG)
    const tage = rechenbar ? finishVerschiebungTage(neu, alt) : null
    const finishAb = grow.phasenanker?.finishAb ? new Date(grow.phasenanker.finishAb) : null
    const hatDatum = finishAb != null && !Number.isNaN(finishAb.getTime()) && tage != null && !bestaetigt
    wirkung.folgt.push({
      name: grow.name,
      tage,
      von: hatDatum ? finishAb : null,
      nach: hatDatum ? new Date(finishAb.getTime() + (tage as number) * TAG_MS) : null,
      bestaetigt,
    })
  }
  return wirkung
}
