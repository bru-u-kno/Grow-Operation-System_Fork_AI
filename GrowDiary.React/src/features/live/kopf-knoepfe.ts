/**
 * Fork AI: Welche Handlungen oben in der Kopfzeile stehen.
 *
 * <b>Warum am Gerät und nicht am Grow.</b> Es ist keine Eigenschaft des Zeltes,
 * sondern eine des Bildschirms: am Telefon ist neben der Statuszeile Platz für
 * zwei Knöpfe, am Schreibtisch stört auch ein dritter nicht. Dieselbe Person
 * will an beiden Geräten etwas anderes — deshalb wie beim Farbschema im
 * Browser abgelegt und nicht im Backend. Kein Datenmodell, keine Wanderung
 * beim Update, nichts, was der Entwickler des Originals mitziehen müsste.
 */

const KEY = 'growos.live.kopfknoepfe'

export type KopfKnopf = 'messen' | 'addback' | 'grow' | 'anpassen'

/** Reihenfolge hier = Reihenfolge im Blatt. */
export const KOPF_KNOEPFE: ReadonlyArray<{
  id: KopfKnopf; icon: string; titel: string; hinweis: string
}> = [
  { id: 'messen', icon: '＋', titel: 'Messen', hinweis: 'Messung erfassen' },
  { id: 'addback', icon: '↻', titel: 'Addback starten', hinweis: 'Nachfüllen mit Zielwerten' },
  { id: 'grow', icon: '✿', titel: 'Grow bearbeiten', hinweis: 'Stammblatt des laufenden Grows' },
  { id: 'anpassen', icon: '▦', titel: 'Anpassen', hinweis: 'Kacheln dieser Seite umstellen' },
]

/** Mehr passt neben der Statuszeile nicht, ohne dass die Zeile umbricht. */
export const MAX_ANGEHEFTET = 2

/**
 * Ohne eigene Wahl steht nur „Messen\" oben. Das „+\" der Kopfleiste führt
 * ohnehin zu allem; wer Addback täglich braucht, heftet es in zwei Tipps an.
 */
const STANDARD: KopfKnopf[] = ['messen']

const gueltig = (wert: unknown): wert is KopfKnopf =>
  KOPF_KNOEPFE.some((knopf) => knopf.id === wert)

export function ladeKopfKnoepfe(): KopfKnopf[] {
  try {
    const roh = localStorage.getItem(KEY)
    if (!roh) return [...STANDARD]
    const gelesen: unknown = JSON.parse(roh)
    if (!Array.isArray(gelesen)) return [...STANDARD]
    // Unbekanntes fällt raus: eine spätere Version kann Namen ändern, ohne dass
    // hier ein Knopf erscheint, den es nicht mehr gibt.
    return gelesen.filter(gueltig).slice(0, MAX_ANGEHEFTET)
  } catch {
    return [...STANDARD]
  }
}

export function speichereKopfKnoepfe(knoepfe: KopfKnopf[]): void {
  try { localStorage.setItem(KEY, JSON.stringify(knoepfe)) } catch { /* Privatmodus */ }
}

/**
 * An- und abheften. Ist die Grenze erreicht, rückt der älteste Knopf ins
 * „⋯\" — ein Tipp, der nichts tut, wäre schlechter als einer, der sichtbar
 * tauscht.
 */
export function umschalten(aktuell: KopfKnopf[], id: KopfKnopf): KopfKnopf[] {
  if (aktuell.includes(id)) return aktuell.filter((eintrag) => eintrag !== id)
  const platz = [...aktuell, id]
  return platz.slice(Math.max(0, platz.length - MAX_ANGEHEFTET))
}
