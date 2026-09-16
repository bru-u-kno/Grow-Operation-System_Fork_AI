import type { NutrientProgramDto } from '../../types'

/**
 * Fork AI (Grow-Plan, Schritt 5b): wie viel ein Programm für den Plan des
 * Grows mitbringt — für den Balken auf der Programmkarte.
 *
 * Was fehlt, füllt der Plan beim Anlegen einmalig aus dem mitgelieferten
 * Standard; die Karte sagt das, damit niemand ein vollständiges Programm
 * erwartet, wo nur EC und pH stehen.
 */
export type Deckung = { stufe: 'voll' | 'teil' | 'keine'; text: string }

export function deckung(programm: Pick<NutrientProgramDto, 'feedChart'>): Deckung {
  const spalten = programm.feedChart?.columns ?? []
  if (spalten.length === 0) {
    return { stufe: 'keine', text: 'keine Wochenwerte · Raster und Ziele aus dem Standard' }
  }
  const wochen = `${spalten.length} Wochen`
  if (spalten.some((s) => s.klimaJeWoche)) {
    return { stufe: 'voll', text: `${wochen} · alle Werte je Woche · Dosierung` }
  }
  return { stufe: 'teil', text: `${wochen} · EC und pH je Woche · Klima aus dem Standard` }
}

export const istEigenesProgramm = (key: string) => key.startsWith('eigen-')

/**
 * Muss vor dem Speichern gefragt werden, was mit den eigenen Änderungen
 * passiert? Nur beim Bearbeiten, nur bei echtem Wechsel auf ein Programm aus
 * der Liste, nur wenn der Grow einen Plan hat.
 */
export function wechselNoetig(vorher: string | null, nachher: string | null, hatPlan: boolean): boolean {
  return hatPlan && nachher != null && vorher != null && vorher !== nachher
}
