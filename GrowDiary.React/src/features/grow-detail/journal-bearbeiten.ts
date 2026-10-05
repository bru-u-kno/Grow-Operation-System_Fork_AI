import type { JournalEntryDto, JournalEntryType } from '../../types'
import { toLocalInputValue } from '../../utils'

/**
 * Einen Journaleintrag bearbeiten — die Regeln ohne Oberfläche.
 *
 * Anlass (04.10.2026): ein Wasserwechsel stand mit „pH- Menge nicht notiert"
 * im Journal und ließ sich nicht korrigieren. Seitdem hat jeder Eintrag
 * „Bearbeiten" — dasselbe Formular wie „+ Eintrag", vorbefüllt.
 */

/** Was das Eintragsformular hält — beim Anlegen wie beim Bearbeiten. */
export type EintragFelder = { title: string; body: string; entryType: string; occurredAtLocal: string }

/** Die Arten, die „+ Eintrag" anbietet. */
export const EINTRAG_ARTEN: ReadonlyArray<{ value: JournalEntryType; label: string }> = [
  { value: 'Observation', label: 'Beobachtung' },
  { value: 'Note', label: 'Notiz' },
  { value: 'Action', label: 'Aktion' },
  { value: 'Problem', label: 'Problem' },
  { value: 'Solution', label: 'Lösung' },
  { value: 'Training', label: 'Training' },
  { value: 'Transplant', label: 'Umtopfen' },
  { value: 'Feeding', label: 'Fütterung' },
  { value: 'ReservoirChange', label: 'Wasserwechsel' },
]

/**
 * Meilensteine legt die App selbst an (Keimung, Flip …) — „+ Eintrag" bietet
 * sie nicht an. Beim Bearbeiten muss die Auswahl sie trotzdem kennen: sonst
 * zeigt sie die erste Option, und „Änderungen speichern" machte aus dem
 * Meilenstein still eine Beobachtung.
 */
export const MEILENSTEIN_ARTEN: Readonly<Record<string, string>> = {
  GerminationConfirmed: 'Keimung bestätigt',
  CloneRooted: 'Steckling bewurzelt',
  FlipToFlower: 'Blüte eingeleitet',
  VegStarted: 'Wachstum begonnen',
  FinishStarted: 'Reife begonnen',
}

/** Ob die Art ein Meilenstein ist, den die App selbst anlegt. */
export function istMeilenstein(entryType: string): boolean {
  return entryType in MEILENSTEIN_ARTEN
}

/**
 * Ein Meilenstein erzählt nur nach: die Phase rechnet mit dem Datum am Grow
 * (Flip, Finish …), nicht mit dem Journal. Ein hier verschobener Zeitpunkt
 * wäre eine zweite Wahrheit neben der Phase — deshalb ist er gesperrt.
 */
export const MEILENSTEIN_HINWEIS =
  'Meilenstein: Das Datum gehört zur Phase des Grows und wird dort geändert („Phase"). Hier lassen sich Titel und Text korrigieren.'

/** Höhe des Textfelds: nach Zeilen UND Länge (ein langer Einzeiler bricht um), 2 bis 8 Zeilen. */
export function textZeilen(text: string): number {
  const zeilen = text.split('\n').reduce((summe, zeile) => summe + Math.max(1, Math.ceil(zeile.length / 40)), 0)
  return Math.min(8, Math.max(2, zeilen + 1))
}

/** Die Auswahl für eine Art — ergänzt um sie selbst, wenn es ein Meilenstein ist. */
export function artenFuer(entryType: string): ReadonlyArray<{ value: string; label: string }> {
  if (EINTRAG_ARTEN.some((art) => art.value === entryType)) return EINTRAG_ARTEN
  const meilenstein = MEILENSTEIN_ARTEN[entryType]
  return meilenstein ? [...EINTRAG_ARTEN, { value: entryType, label: meilenstein }] : EINTRAG_ARTEN
}

/**
 * Ein gespeicherter Eintrag als vorbefülltes Formular.
 *
 * Nimmt nur die vier Felder, die es liest — das Grow-Tagebuch (A-006) hat den
 * Eintrag in seiner eigenen Form und bearbeitet ihn mit demselben Formular.
 */
export function alsFelder(entry: Pick<JournalEntryDto, 'title' | 'body' | 'entryType' | 'occurredAtUtc'>): EintragFelder {
  return {
    title: entry.title ?? '',
    body: entry.body ?? '',
    entryType: entry.entryType,
    occurredAtLocal: toLocalInputValue(new Date(entry.occurredAtUtc)),
  }
}

/** Titel oder Text muss stehen — dieselbe Regel wie im Backend. */
export function eintragFehler(felder: EintragFelder): string | null {
  return !felder.title.trim() && !felder.body.trim() ? 'Bitte gib mindestens einen Titel oder Text ein.' : null
}

/**
 * Der Körper für `PUT /api/journal/{id}`.
 *
 * Titel und Text gehen immer mit — ein leerer Text heißt „leeren", `null`
 * hieße beim Backend „unverändert lassen". Der Zeitpunkt geht nur mit, wenn
 * er geändert wurde: das Formular kennt nur Minuten, und ein unveränderter
 * Eintrag soll seine Sekunden behalten.
 */
export function aenderungsAnfrage(felder: EintragFelder, ausgang: EintragFelder) {
  return {
    title: felder.title,
    body: felder.body,
    entryType: felder.entryType as JournalEntryType,
    occurredAtLocal: felder.occurredAtLocal !== ausgang.occurredAtLocal ? felder.occurredAtLocal : null,
  }
}
