/* src/components/ErfassenSheet.tsx — Fork AI
   Ein Weg für alle Eingaben, statt Reiter neben Knöpfen.

   Vorher stand „Messen“ als Reiter in der Navigation UND „Messung erfassen“ als
   Knopf auf der Live-Seite; dasselbe bei Addback. Zwei Wege zum selben
   Formular kosten Platz in einer Leiste, in der Platz das Knappe ist — und wer
   den Reiter nimmt, landet an derselben Stelle wie über den Knopf. Deshalb
   liegen die Eintragswege jetzt hinter EINEM Plus in der Titelzeile, auf jeder
   Seite erreichbar und nicht nur auf der Startseite. */

import { useEffect, useRef } from 'react'
import { useNavigate } from 'react-router-dom'
import { erfassenZiele } from './erfassen-ziele'

type Props = {
  open: boolean
  onClose: () => void
}

export function ErfassenSheet({ open, onClose }: Props) {
  const navigate = useNavigate()
  const panelRef = useRef<HTMLDivElement>(null)

  // Escape schliesst. Ohne das bleibt am Notebook nur der Klick daneben — und
  // wer mit der Tastatur arbeitet, sitzt in einem Dialog fest, den er nicht
  // wegbekommt.
  useEffect(() => {
    if (!open) return
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open, onClose])

  // Der Fokus muss ins Blatt. Sonst liest ein Screenreader nach dem Öffnen
  // weiter die Seite dahinter vor, als wäre nichts passiert.
  useEffect(() => {
    if (open) panelRef.current?.focus()
  }, [open])

  if (!open) return null

  return (
    <>
      <div className="forkai-sheet-dim" onClick={onClose} data-audit="erfassen-dim" />
      <div
        className="forkai-sheet"
        role="dialog"
        aria-modal="true"
        aria-label="Erfassen"
        tabIndex={-1}
        ref={panelRef}
        data-audit="erfassen-sheet"
      >
        <div className="forkai-sheet-grip" aria-hidden="true" />
        <h2 className="forkai-sheet-title">Erfassen</h2>
        {erfassenZiele.map((ziel) => (
          <button
            key={ziel.to}
            type="button"
            className="forkai-sheet-item"
            onClick={() => {
              onClose()
              navigate(ziel.to)
            }}
          >
            <span className="forkai-sheet-icon" aria-hidden="true">{ziel.icon}</span>
            <span className="forkai-sheet-text">
              <strong>{ziel.label}</strong>
              <span>{ziel.hint}</span>
            </span>
          </button>
        ))}
        <button type="button" className="forkai-sheet-cancel" onClick={onClose}>
          Abbrechen
        </button>
      </div>
    </>
  )
}
