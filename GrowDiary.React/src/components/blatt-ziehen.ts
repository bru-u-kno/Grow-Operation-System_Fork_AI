import { useCallback, useRef, useState } from 'react'
import type { CSSProperties, PointerEvent as ReactPointerEvent } from 'react'

/**
 * Fork AI (forkai.88): Blätter nach unten wegziehen.
 *
 * <b>Warum.</b> Der Griff oben am Blatt (`.forkai-sheet-grip`) war reine Deko —
 * ein 40 × 4 px grosser Balken, der genau die Geste verspricht, die jedes
 * Telefon-Blatt kann, und dann nichts tut. Wer daran zieht, hält das Blatt für
 * hängend statt fuer geschlossen. Geschlossen wurde bisher nur ueber den
 * Schleier, „Abbrechen" oder Escape.
 *
 * <b>Was es tut.</b> Das Blatt folgt dem Finger nach unten, der Schleier wird
 * dabei heller. Beim Loslassen entscheidet {@link zugEntscheidung}: weit genug
 * gezogen oder schnell gewischt → schliessen, sonst federt es zurueck.
 *
 * <b>Was es NICHT tut.</b> Es hängt nicht am ganzen Blatt, sondern nur am Griff
 * und am Titel. Sonst würde jede Liste im Blatt beim Scrollen das Blatt
 * mitziehen — und die Blätter hier sind lang (Leiste anpassen, Geräte).
 */

/** Ab hier gilt der Zug als Absicht, auch ohne Schwung. */
export const ZUG_SCHWELLE_PX = 90
/** Ein schneller Wisch darf früher schliessen — aber nicht aus dem Stand. */
export const ZUG_WISCH_PX = 20
/** px je ms; ein gemütliches Ziehen liegt deutlich darunter. */
export const ZUG_WISCH_TEMPO = 0.5

/**
 * Der zurückgelegte Weg. Nach oben gibt das Blatt nur zäh nach — ein Blatt, das
 * man nach oben aus dem Bild schieben kann, sieht kaputt aus.
 */
export function zugWeg(dy: number): number {
  return dy < 0 ? dy / 4 : dy
}

export function zugEntscheidung(weg: number, dauerMs: number): 'schliessen' | 'zurueck' {
  if (weg >= ZUG_SCHWELLE_PX) return 'schliessen'
  const tempo = dauerMs > 0 ? weg / dauerMs : 0
  if (weg >= ZUG_WISCH_PX && tempo >= ZUG_WISCH_TEMPO) return 'schliessen'
  return 'zurueck'
}

/** Der Schleier wird beim Ziehen heller, aber nie ganz durchsichtig. */
export function schleierDeckung(weg: number): number {
  const gezogen = Math.min(Math.max(weg, 0), 300)
  return 1 - (gezogen / 300) * 0.6
}

export type BlattZug = {
  /** Auf Griff und Titel legen. */
  griffProps: {
    onPointerDown: (event: ReactPointerEvent<HTMLElement>) => void
    onPointerMove: (event: ReactPointerEvent<HTMLElement>) => void
    onPointerUp: (event: ReactPointerEvent<HTMLElement>) => void
    onPointerCancel: (event: ReactPointerEvent<HTMLElement>) => void
  }
  /** Auf das Blatt selbst. Setzt `--blatt-zug`, den Rest macht das CSS. */
  blattStil: CSSProperties
  /** Auf den Schleier. */
  schleierStil: CSSProperties
}

export function useBlattZiehen(schliessen: () => void): BlattZug {
  const [weg, setWeg] = useState(0)
  const [zieht, setZieht] = useState(false)
  const start = useRef<{ y: number; zeit: number } | null>(null)

  const aufRunter = useCallback((event: ReactPointerEvent<HTMLElement>) => {
    if (event.button > 0) return
    start.current = { y: event.clientY, zeit: Date.now() }
    setZieht(true)
    setWeg(0)
    try {
      event.currentTarget.setPointerCapture(event.pointerId)
    } catch {
      // Ohne Zeiger-Fang geht es auch, nur weniger zuverlässig am Rand.
    }
  }, [])

  const aufBewegung = useCallback((event: ReactPointerEvent<HTMLElement>) => {
    if (!start.current) return
    setWeg(zugWeg(event.clientY - start.current.y))
  }, [])

  const aufEnde = useCallback((event: ReactPointerEvent<HTMLElement>) => {
    const begonnen = start.current
    start.current = null
    setZieht(false)
    setWeg(0)
    try {
      event.currentTarget.releasePointerCapture(event.pointerId)
    } catch {
      // War nie gefangen — dann gibt es auch nichts freizugeben.
    }
    if (!begonnen) return
    const gezogen = zugWeg(event.clientY - begonnen.y)
    if (zugEntscheidung(gezogen, Date.now() - begonnen.zeit) === 'schliessen') schliessen()
  }, [schliessen])

  return {
    griffProps: {
      onPointerDown: aufRunter,
      onPointerMove: aufBewegung,
      onPointerUp: aufEnde,
      onPointerCancel: aufEnde,
    },
    // Am Finger darf nichts nachfedern, beim Loslassen schon — deshalb die
    // Übergangssperre nur waehrend des Zugs.
    blattStil: {
      '--blatt-zug': `${Math.max(weg, 0)}px`,
      transition: zieht ? 'none' : undefined,
    } as CSSProperties,
    schleierStil: {
      opacity: schleierDeckung(weg),
      transition: zieht ? 'none' : undefined,
    },
  }
}
