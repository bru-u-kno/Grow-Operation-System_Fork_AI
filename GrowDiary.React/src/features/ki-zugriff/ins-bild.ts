import { spaeterInsBild } from '../../components/reiter-ins-bild'

/**
 * Fork AI (A-003, 04.10.2026): Etwas, das nur einmal zu sehen ist, in den
 * Blick holen.
 *
 * Bru legte am Handy einen Schlüssel an, die Ansicht blieb unten bei der
 * Schlüsselliste stehen, und der Kasten mit dem Klartext lag oberhalb der
 * Bildkante. Er hielt den Schlüssel für verloren und legte ihn neu an. Der
 * Klartext wird nur einmal gezeigt — wer den Kasten verpasst, verliert ihn.
 *
 * Deshalb: den Kasten OBEN unter die feste Kopfleiste rollen und den Fokus
 * hineinsetzen, damit ein Screenreader ihn ansagt. Wer weniger Bewegung
 * eingestellt hat, bekommt keinen sanften Lauf, sondern den direkten Sprung.
 *
 * Gerollt wird mit `spaeterInsBild` und NICHT mit `scrollIntoView`: forkai.166
 * nahm `scrollIntoView` — in Brus HA-App lag der Kasten danach wieder ein Stück
 * unter der Kopfleiste, im Chromium hier richtig. Dieselbe Falle wie F-049: die
 * App beachtet `scroll-margin-top` dabei nicht. `insBildRollen` misst die echte
 * Kopfleiste (auch bei größerer Schrift) und rollt selbst.
 */

/** `smooth`, außer der Nutzer hat „Bewegung reduzieren" eingestellt. */
export function rollArt(fenster: Pick<Window, 'matchMedia'> | undefined = typeof window === 'undefined' ? undefined : window): ScrollBehavior {
  const weniger = fenster?.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false
  return weniger ? 'auto' : 'smooth'
}

type Rollen = (ziel: () => HTMLElement | null, art: ScrollBehavior) => () => void

/** Fokus sofort (ohne Sprung), gerollt wird nach dem Zeichnen. Gibt das Abbrechen zurück. */
export function insBildHolen(
  ziel: HTMLElement | null | undefined,
  fenster?: Pick<Window, 'matchMedia'>,
  rollen: Rollen = spaeterInsBild,
): () => void {
  if (!ziel) return () => {}
  // Ohne preventScroll springt der Fokus sofort hin — und zwar mit demselben Fehler wie scrollIntoView.
  ziel.focus?.({ preventScroll: true })
  return rollen(() => ziel, rollArt(fenster))
}
