import { useCallback, useEffect, useRef, type PointerEvent as ReactPointerEvent, type RefObject } from 'react'
import { fensterUmAnker, zoomUm, type Fenster } from './verlauf-modell'

/** Was die Gesten zum Rechnen brauchen — als Ref, damit ein Handler nie einen alten Stand sieht. */
export type GestenStand = { fenster: Fenster; grenzen: Fenster }

type Finger = { x: number; svg: SVGSVGElement }
type Zange = { abstand: number; ankerZeit: number; breite: number; svg: SVGSVGElement }

/**
 * Wo im SVG die Zeitachse liegt. Steht als `data-links`/`data-breite` am
 * Element: das Diagramm ist in echten Pixeln gezeichnet (viewBox = gemessene
 * Breite), also ist ein Pixel im SVG ein Pixel auf dem Schirm.
 */
function geometrie(svg: SVGSVGElement): { links: number; breite: number; box: DOMRect } {
  return {
    links: Number(svg.dataset.links ?? 0),
    breite: Math.max(1, Number(svg.dataset.breite ?? 1)),
    box: svg.getBoundingClientRect(),
  }
}

function anteilBei(svg: SVGSVGElement, clientX: number): number {
  const { links, breite, box } = geometrie(svg)
  return (clientX - box.left - links) / breite
}

/**
 * Die Gesten auf dem Verlaufsdiagramm — Maus und Finger mit denselben
 * Pointer-Events.
 *
 * - ein Finger oder die Maus: Zeiger mit den Werten zu dieser Uhrzeit
 * - zwei Finger: Abstand zoomt, Mittelpunkt verschiebt
 * - Doppeltipp: zurück auf den gewählten Zeitraum
 * - Strg/Cmd + Mausrad (und Trackpad-Zwei-Finger): Zoom um die Mausposition
 *
 * Die Finger werden für ALLE Diagramme der Kachel gemeinsam gezählt: in der
 * Ansicht „Einzeln" darf der zweite Finger auf der Nachbarzeile landen, und
 * die Geste ist trotzdem ein Zoom.
 */
export function useVerlaufGesten({
  stand, setZeiger, setFenster, zuruecksetzen,
}: {
  stand: RefObject<GestenStand | null>
  setZeiger: (zeit: number | null) => void
  setFenster: (fenster: Fenster) => void
  zuruecksetzen: () => void
}) {
  const finger = useRef(new Map<number, Finger>())
  const zange = useRef<Zange | null>(null)
  const letzterTipp = useRef<{ zeit: number; x: number }>({ zeit: 0, x: 0 })
  const huelle = useRef<HTMLElement | null>(null)

  const zeitBei = (svg: SVGSVGElement, clientX: number): number | null => {
    const s = stand.current
    if (!s) return null
    const anteil = Math.min(1, Math.max(0, anteilBei(svg, clientX)))
    return s.fenster.von + anteil * (s.fenster.bis - s.fenster.von)
  }

  const zangeBeginnen = () => {
    const s = stand.current
    const [a, b] = [...finger.current.values()]
    if (!s || !a || !b) return
    const mitte = (a.x + b.x) / 2
    const anteil = anteilBei(a.svg, mitte)
    // Der erste Finger einer Zwei-Finger-Geste war kein Tipp. Sonst hielte der
    // nächste Fingerdruck kurz danach beide für einen Doppeltipp und setzte
    // den Ausschnitt zurück, statt den Zeiger zu zeigen (gefunden beim
    // wiederholten Lauf von e2e/verlaufsdiagramm.spec.ts).
    letzterTipp.current = { zeit: 0, x: 0 }
    zange.current = {
      abstand: Math.max(1, Math.abs(a.x - b.x)),
      ankerZeit: s.fenster.von + anteil * (s.fenster.bis - s.fenster.von),
      breite: s.fenster.bis - s.fenster.von,
      svg: a.svg,
    }
    setZeiger(null)
  }

  const onPointerDown = (event: ReactPointerEvent<SVGSVGElement>) => {
    const svg = event.currentTarget
    // Ohne Capture verliert das SVG den Finger, sobald er über den Rand rutscht.
    try { svg.setPointerCapture(event.pointerId) } catch { /* Testumgebung ohne Capture */ }
    finger.current.set(event.pointerId, { x: event.clientX, svg })

    if (finger.current.size === 2) { zangeBeginnen(); return }
    if (finger.current.size > 2) return

    const jetzt = event.timeStamp
    const vorher = letzterTipp.current
    if (jetzt - vorher.zeit < 300 && Math.abs(event.clientX - vorher.x) < 30) {
      letzterTipp.current = { zeit: 0, x: 0 }
      zuruecksetzen()
      return
    }
    letzterTipp.current = { zeit: jetzt, x: event.clientX }
    setZeiger(zeitBei(svg, event.clientX))
  }

  const onPointerMove = (event: ReactPointerEvent<SVGSVGElement>) => {
    const eintrag = finger.current.get(event.pointerId)
    if (eintrag) eintrag.x = event.clientX

    const z = zange.current
    const s = stand.current
    if (finger.current.size === 2 && z && s) {
      const [a, b] = [...finger.current.values()]
      const abstand = Math.max(1, Math.abs(a.x - b.x))
      const breite = z.breite * (z.abstand / abstand)
      const anteil = anteilBei(z.svg, (a.x + b.x) / 2)
      setFenster(fensterUmAnker(z.ankerZeit, anteil, breite, s.grenzen))
      return
    }
    // Die Maus zeigt schon beim Darüberfahren; ein Finger nur, solange er liegt.
    if (finger.current.size === 1 || (event.pointerType === 'mouse' && finger.current.size === 0)) {
      setZeiger(zeitBei(event.currentTarget, event.clientX))
    }
  }

  const ende = (event: ReactPointerEvent<SVGSVGElement>) => {
    // Nur ein kurzer Tipp an Ort und Stelle zählt für den Doppeltipp — wer den
    // Zeiger über das Bild gezogen hat, hat nicht getippt.
    const tipp = letzterTipp.current
    if (tipp.zeit && (event.timeStamp - tipp.zeit > 300 || Math.abs(event.clientX - tipp.x) > 10)) letzterTipp.current = { zeit: 0, x: 0 }
    finger.current.delete(event.pointerId)
    if (finger.current.size < 2) zange.current = null
    if (finger.current.size === 0 && event.pointerType !== 'mouse') setZeiger(null)
  }

  const onPointerLeave = (event: ReactPointerEvent<SVGSVGElement>) => {
    if (event.pointerType === 'mouse' && finger.current.size === 0) setZeiger(null)
  }

  // Zoom nur mit Strg/Cmd + Rad: das einfache Rad gehört der Seite, sonst
  // bleibt jeder, der am Rechner nach unten scrollt, im Diagramm hängen.
  // Die Zwei-Finger-Geste auf dem Trackpad schickt Chrome als Strg+Rad — sie
  // zoomt also weiter. Der Listener ist NICHT passiv, damit er mit Strg das
  // Zoomen der ganzen Seite verhindern kann; React hängt `onWheel` passiv an.
  const raeder = (event: WheelEvent) => {
    if (!event.ctrlKey && !event.metaKey) return
    const svg = (event.target as Element | null)?.closest?.('svg[data-links]') as SVGSVGElement | null
    const s = stand.current
    if (!svg || !s) return
    event.preventDefault()
    const zeit = zeitBei(svg, event.clientX)
    if (zeit == null) return
    setFenster(zoomUm(s.fenster, zeit, event.deltaY > 0 ? 1.25 : 0.8, s.grenzen))
  }

  const raederAktuell = useRef(raeder)
  useEffect(() => { raederAktuell.current = raeder })

  // Ein fester Listener für die ganze Lebenszeit, der immer die aktuelle
  // Fassung ruft — so muss beim Neuzeichnen nichts ab- und angehängt werden.
  const weiter = useRef((event: WheelEvent) => raederAktuell.current(event))
  const huelleRef = useCallback((element: HTMLElement | null) => {
    huelle.current?.removeEventListener('wheel', weiter.current)
    huelle.current = element
    element?.addEventListener('wheel', weiter.current, { passive: false })
  }, [])

  return {
    svgProps: { onPointerDown, onPointerMove, onPointerUp: ende, onPointerCancel: ende, onPointerLeave },
    huelleRef,
  }
}
