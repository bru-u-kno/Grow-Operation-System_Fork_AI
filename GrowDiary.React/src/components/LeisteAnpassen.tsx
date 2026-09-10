/* src/components/LeisteAnpassen.tsx — Fork AI
   Reihenfolge und Auswahl der fünf Ziele in der Leiste.

   Warum ein eigener Modus und kein langer Druck auf das Zeichen selbst:
   Grow OS läuft am Telefon im WebView der Companion-App. Ein langer Druck ist
   dort schon vergeben — er startet die Textauswahl und, je nach Android-Version,
   das Kontextmenü. Beides lässt sich von innen nicht zuverlässig abschalten, und
   ein Ziehen, das mal die Leiste sortiert und mal die Seite scrollt, ist
   schlimmer als gar keins. Deshalb ein sichtbarer Modus mit Pfeilen: er
   funktioniert mit dem Daumen, mit der Maus und mit der Tastatur gleich. */

import { useEffect, useState } from 'react'
import { barCandidates, defaultBarRoutes, type NavLeaf } from '../navigation'
import { formatApiError } from '../api'

type Props = {
  open: boolean
  aktuell: NavLeaf[]
  onClose: () => void
  onSave: (routes: string[]) => Promise<void>
  onReset: () => Promise<void>
}

const MAX = 5

export function LeisteAnpassen({ open, aktuell, onClose, onSave, onReset }: Props) {
  const [gewaehlt, setGewaehlt] = useState<string[]>([])
  const [fehler, setFehler] = useState<string | null>(null)
  const [speichert, setSpeichert] = useState(false)

  // Beim Öffnen den echten Stand übernehmen — nicht den von vorhin. Wer
  // abbricht, ändert nichts; wer erneut öffnet, sieht das Gespeicherte.
  useEffect(() => {
    if (open) {
      setGewaehlt(aktuell.map((item) => item.to))
      setFehler(null)
    }
  }, [open, aktuell])

  if (!open) return null

  const verschieben = (index: number, richtung: -1 | 1) => {
    const ziel = index + richtung
    if (ziel < 0 || ziel >= gewaehlt.length) return
    const kopie = [...gewaehlt]
    ;[kopie[index], kopie[ziel]] = [kopie[ziel], kopie[index]]
    setGewaehlt(kopie)
  }

  const umschalten = (route: string) => {
    setFehler(null)
    if (gewaehlt.includes(route)) {
      // Die Leiste darf nicht leer werden — sonst bliebe nur „Mehr“, und der
      // Weg zurück wäre ausgerechnet über das Menü, das man gerade leert.
      if (gewaehlt.length === 1) {
        setFehler('Mindestens ein Ziel muss in der Leiste bleiben.')
        return
      }
      setGewaehlt(gewaehlt.filter((item) => item !== route))
      return
    }
    if (gewaehlt.length >= MAX) {
      setFehler(`In die Leiste passen ${MAX} Ziele. Nimm erst eines heraus.`)
      return
    }
    setGewaehlt([...gewaehlt, route])
  }

  const speichern = async () => {
    setSpeichert(true)
    setFehler(null)
    try {
      await onSave(gewaehlt)
      onClose()
    } catch (caught) {
      setFehler(formatApiError(caught, 'Die Reihenfolge konnte nicht gespeichert werden.'))
    } finally {
      setSpeichert(false)
    }
  }

  const zuruecksetzen = async () => {
    setSpeichert(true)
    setFehler(null)
    try {
      await onReset()
      onClose()
    } catch (caught) {
      setFehler(formatApiError(caught, 'Die Werkseinstellung konnte nicht gesetzt werden.'))
    } finally {
      setSpeichert(false)
    }
  }

  const nichtGewaehlt = barCandidates.filter((item) => !gewaehlt.includes(item.to))
  const istWerkseinstellung =
    gewaehlt.length === defaultBarRoutes.length &&
    gewaehlt.every((route, index) => route === defaultBarRoutes[index])

  return (
    <>
      <div className="forkai-sheet-dim" onClick={onClose} />
      <div className="forkai-anpassen" role="dialog" aria-modal="true" aria-label="Leiste anpassen" data-audit="leiste-anpassen">
        <div className="forkai-sheet-grip" aria-hidden="true" />
        <h2 className="forkai-sheet-title">Leiste anpassen</h2>
        <p className="forkai-anpassen-hinweis">
          Bis zu {MAX} Ziele stehen oben in der Leiste, alles Übrige bleibt unter „Mehr“ erreichbar.
        </p>

        <div className="forkai-anpassen-gruppe">In der Leiste</div>
        <ol className="forkai-anpassen-liste">
          {gewaehlt.map((route, index) => {
            const leaf = barCandidates.find((item) => item.to === route)
            if (!leaf) return null
            return (
              <li key={route} className="forkai-anpassen-zeile">
                <span className="forkai-anpassen-icon" aria-hidden="true">{leaf.icon}</span>
                <span className="forkai-anpassen-name">{leaf.label}</span>
                <button
                  type="button"
                  className="forkai-anpassen-knopf"
                  onClick={() => verschieben(index, -1)}
                  disabled={index === 0}
                  aria-label={`${leaf.label} nach links`}
                >
                  ←
                </button>
                <button
                  type="button"
                  className="forkai-anpassen-knopf"
                  onClick={() => verschieben(index, 1)}
                  disabled={index === gewaehlt.length - 1}
                  aria-label={`${leaf.label} nach rechts`}
                >
                  →
                </button>
                <button
                  type="button"
                  className="forkai-anpassen-knopf entfernen"
                  onClick={() => umschalten(route)}
                  aria-label={`${leaf.label} aus der Leiste nehmen`}
                >
                  ✕
                </button>
              </li>
            )
          })}
        </ol>

        {nichtGewaehlt.length > 0 && (
          <>
            <div className="forkai-anpassen-gruppe">Verfügbar</div>
            <div className="forkai-anpassen-vorrat">
              {nichtGewaehlt.map((leaf) => (
                <button
                  key={leaf.to}
                  type="button"
                  className="forkai-anpassen-chip"
                  onClick={() => umschalten(leaf.to)}
                >
                  <span aria-hidden="true">{leaf.icon}</span> {leaf.label}
                </button>
              ))}
            </div>
          </>
        )}

        {fehler && <p className="forkai-anpassen-fehler" role="alert">{fehler}</p>}

        <div className="forkai-anpassen-aktionen">
          <button type="button" className="forkai-anpassen-sekundaer" onClick={onClose} disabled={speichert}>
            Abbrechen
          </button>
          <button
            type="button"
            className="forkai-anpassen-sekundaer"
            onClick={zuruecksetzen}
            disabled={speichert || istWerkseinstellung}
          >
            Standard
          </button>
          <button type="button" className="forkai-anpassen-primaer" onClick={speichern} disabled={speichert}>
            {speichert ? 'Speichert …' : 'Speichern'}
          </button>
        </div>
      </div>
    </>
  )
}
