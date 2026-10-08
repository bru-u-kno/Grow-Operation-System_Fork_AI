import { useId, useState } from 'react'
import type { ReactNode } from 'react'
import './steuerung.css'

/**
 * Fork AI (A-014): eine Kachel, die sich ein- und ausklappen lässt.
 *
 * <b>Regel (Bru, 07.10.2026).</b> Jede Kachel ist einklappbar, damit nur
 * aufgeklappt ist, was gerade gebraucht wird. Zugeklappt zeigt die Kachel in
 * der Kopfzeile ihren aktuellen Wert (`zusammenfassung`).
 *
 * <b>Inhalt bleibt im Baum.</b> Zugeklappt wird mit `hidden`, nicht durch
 * Weglassen: ein markiertes Feld (`.st-fehler`) in einer zugeklappten Kachel
 * muss gefunden werden (`useFehlerZeigen`). Die Kachel klappt dann per CSS
 * von selbst auf (`.st-kk:has(.st-fehler)`).
 */
export function Klappkachel({ titel, zusammenfassung, offen = true, children }: {
  titel: string
  /** Was die zugeklappte Kachel in der Kopfzeile zeigt. */
  zusammenfassung?: ReactNode
  /** Zustand beim ersten Anzeigen. */
  offen?: boolean
  children: ReactNode
}) {
  const [auf, setAuf] = useState(offen)
  const koerper = useId()
  return (
    <section className="v1-section st-kk">
      <header className="v1-section-head">
        <h2>
          <button type="button" className="st-kk-kopf" aria-expanded={auf} aria-controls={koerper} onClick={() => setAuf(!auf)}>
            <span className="st-kk-titel">{titel}</span>
            {!auf && zusammenfassung != null && <span className="st-kk-zus">{zusammenfassung}</span>}
            <span className="ef-klapp-zeichen">{auf ? 'zuklappen ▴' : 'aufklappen ▾'}</span>
          </button>
        </h2>
      </header>
      <div className="v1-section-body st-kk-body" id={koerper} hidden={!auf}>{children}</div>
    </section>
  )
}
