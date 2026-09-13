import { useEffect, useRef } from 'react'
import type { ReactNode } from 'react'
import { useBlattZiehen } from './blatt-ziehen'

/**
 * Fork AI (forkai.35): Das Blatt von unten — jetzt für alle, nicht nur fürs
 * Erfassen.
 *
 * <b>Warum herausgelöst.</b> Das Muster stand fertig in `ErfassenSheet`: Schleier,
 * Griff, Titel, Escape schließt, Fokus wandert hinein, am Schreibtisch ein
 * mittiger Dialog statt voller Breite. Formulare anderswo im Fork griffen
 * trotzdem zu Inline-Kästen mitten in der Karte, weil es das Blatt nur als
 * fertige Seite gab. Dieselbe Gestaltung, dasselbe CSS (`.forkai-sheet`) — nur
 * mit freiem Inhalt.
 *
 * <b>forkai.88.</b> Der Griff ist nicht mehr nur Deko: Ziehen am Griff oder am
 * Titel schiebt das Blatt nach unten weg (siehe `blatt-ziehen.ts`).
 *
 * <b>Was es NICHT tut.</b> Es fängt den Tabulator nicht ein. Escape und der Klick
 * auf den Schleier schließen; mehr hatte das Erfassen-Blatt auch nicht, und ein
 * halber Fokus-Käfig ist schlechter als keiner.
 */
export function V1Sheet({ open, onClose, title, subtitle, children, footer, label }: {
  open: boolean
  onClose: () => void
  title: string
  subtitle?: ReactNode
  children: ReactNode
  /** Knopfzeile unten — ohne sie steht nur „Abbrechen" da. */
  footer?: ReactNode
  /** Vorlesbarer Name, wenn der Titel zu knapp ist. */
  label?: string
}) {
  const blatt = useRef<HTMLDivElement>(null)
  const zug = useBlattZiehen(onClose)

  useEffect(() => {
    if (!open) return
    const aufTaste = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', aufTaste)
    return () => window.removeEventListener('keydown', aufTaste)
  }, [open, onClose])

  // Der Fokus muss ins Blatt. Sonst liest ein Screenreader nach dem Öffnen
  // weiter die Seite dahinter vor, als wäre nichts passiert.
  useEffect(() => {
    if (open) blatt.current?.focus()
  }, [open])

  if (!open) return null

  return (
    <>
      <div className="forkai-sheet-dim" onClick={onClose} style={zug.schleierStil} data-audit="sheet-dim" />
      <div
        className="forkai-sheet"
        style={zug.blattStil}
        role="dialog"
        aria-modal="true"
        aria-label={label ?? title}
        tabIndex={-1}
        ref={blatt}
        data-audit="v1-sheet"
      >
        <div className="forkai-sheet-griff" aria-hidden="true" data-audit="sheet-griff" {...zug.griffProps}>
          <div className="forkai-sheet-grip" />
        </div>
        <h2 className="forkai-sheet-title" {...zug.griffProps}>{title}</h2>
        {subtitle && <p className="forkai-sheet-unter">{subtitle}</p>}
        {children}
        {footer ?? (
          <button type="button" className="forkai-sheet-cancel" onClick={onClose}>
            Abbrechen
          </button>
        )}
      </div>
    </>
  )
}
