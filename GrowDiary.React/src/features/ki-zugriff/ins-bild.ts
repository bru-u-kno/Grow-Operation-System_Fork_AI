/**
 * Fork AI (A-003, 04.10.2026): Etwas, das nur einmal zu sehen ist, in den
 * Blick holen.
 *
 * Bru legte am Handy einen Schlüssel an, die Ansicht blieb unten bei der
 * Schlüsselliste stehen, und der Kasten mit dem Klartext lag oberhalb der
 * Bildkante. Er hielt den Schlüssel für verloren und legte ihn neu an. Der
 * Klartext wird nur einmal gezeigt — wer den Kasten verpasst, verliert ihn.
 *
 * Deshalb: hinrollen (`nearest` — schon sichtbar heißt: nichts bewegt sich;
 * die feste Kopfleiste am Handy überspringt `.scroll-ziel` per
 * `scroll-margin-top`) und den Fokus hineinsetzen, damit ein Screenreader den
 * Kasten ansagt. Wer weniger Bewegung eingestellt hat, bekommt keinen
 * sanften Lauf, sondern den direkten Sprung.
 */

/** `smooth`, außer der Nutzer hat „Bewegung reduzieren" eingestellt. */
export function rollArt(fenster: Pick<Window, 'matchMedia'> | undefined = typeof window === 'undefined' ? undefined : window): ScrollBehavior {
  const weniger = fenster?.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false
  return weniger ? 'auto' : 'smooth'
}

type Ziel = Pick<HTMLElement, 'scrollIntoView' | 'focus'>

export function insBildHolen(ziel: Ziel | null | undefined, fenster?: Pick<Window, 'matchMedia'>): void {
  if (!ziel) return
  ziel.scrollIntoView?.({ block: 'nearest', behavior: rollArt(fenster) })
  // Ohne preventScroll springt der Fokus sofort hin und überholt den sanften Lauf.
  ziel.focus?.({ preventScroll: true })
}
