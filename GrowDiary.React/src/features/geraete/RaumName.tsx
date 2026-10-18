import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch, formatApiError } from '../../api'
import { V1Alert, V1Button, V1Section, V1Skeleton } from '../../components/v1'
import type { RaumStand } from '../steuerung/steuerung-typen'
import '../steuerung/steuerung.css'

/**
 * forkai.241: Name und Zelte des Raums — der Ort, in dem ein Zelt steht und aus dem es seine Luft zieht.
 *
 * <b>Eine Stelle für den Namen.</b> Zuluft, Entfeuchter und die Sätze der Kacheln benutzen diesen Namen; kein „Keller"
 * fest im Text. Leer lassen heißt: die Vorgabe „Aufstellraum". Die Fühler des Raums stehen darunter (Rollen des Moduls
 * `raum`), gezeigt vom Rollen-Reiter.
 */
export function RaumName() {
  const [stand, setStand] = useState<RaumStand | null>(null)
  const [eingabe, setEingabe] = useState('')
  const [fehler, setFehler] = useState<string | null>(null)
  const [hinweis, setHinweis] = useState<string | null>(null)
  const [laedt, setLaedt] = useState(true)
  const [speichert, setSpeichert] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    const laden = async () => {
      try {
        const geladen = await apiFetch<RaumStand>('/api/steuerung/raum', { signal: controller.signal })
        if (controller.signal.aborted) return
        setStand(geladen)
        setEingabe(geladen.name)
        setFehler(null)
      } catch (caught) {
        if (!controller.signal.aborted) setFehler(formatApiError(caught, 'Der Raum konnte nicht geladen werden.'))
      } finally {
        if (!controller.signal.aborted) setLaedt(false)
      }
    }
    void laden()
    return () => controller.abort()
  }, [])

  const geaendert = stand !== null && (eingabe.trim() || stand.vorgabe) !== stand.name

  const speichern = async () => {
    setSpeichert(true); setHinweis(null)
    try {
      const zurueck = await apiFetch<RaumStand>('/api/steuerung/raum', { method: 'PUT', body: JSON.stringify({ name: eingabe }) })
      setStand(zurueck)
      setEingabe(zurueck.name)
      setFehler(null)
      setHinweis('Gespeichert.')
    } catch (caught) {
      setFehler(formatApiError(caught, 'Der Name konnte nicht gespeichert werden.'))
    } finally {
      setSpeichert(false)
    }
  }

  if (laedt) return <V1Skeleton label="Raum wird geladen" rows={2} />

  return (
    <>
      <p className="st-hinweis">
        Ein Raum ist der Ort, in dem ein Zelt steht und aus dem es seine Luft zieht. Die Außenluft ist davon getrennt.
        Steuerungen, die den Raum betreffen (Zuluft, Entfeuchter), hängen am Raum — nicht am einzelnen Zelt.
      </p>
      <V1Section title="Name des Raums">
        <div className="ez-namen">
          {fehler && <V1Alert tone="critical" message={fehler} />}
          {hinweis && <V1Alert tone="ok" message={hinweis} />}
          {stand && (
            <>
              <div className="st-feldzeile is-gestapelt">
                <span className="st-etikett">
                  Name des Raums
                  <small>Vorgabe für einen neuen Raum: {stand.vorgabe}</small>
                </span>
                <div className="ez-namen-zeile">
                  <input
                    value={eingabe}
                    placeholder={stand.vorgabe}
                    aria-label="Name des Raums"
                    onChange={(event) => setEingabe(event.target.value)}
                  />
                  {eingabe.trim() !== '' && eingabe.trim() !== stand.vorgabe && (
                    <V1Button onClick={() => setEingabe('')}>Vorgabe</V1Button>
                  )}
                </div>
              </div>
              <div className="st-knopfleiste">
                <V1Button variant="primary" disabled={!geaendert || speichert} onClick={() => void speichern()}>
                  {speichert ? 'Speichert…' : 'Namen speichern'}
                </V1Button>
              </div>
            </>
          )}
        </div>
      </V1Section>
      {stand && (
        <V1Section title="Zelte in diesem Raum">
          {stand.zelte.length === 0 ? (
            <p className="st-hinweis">
              Noch kein Zelt. Beim Zelt unter <Link to="/zelte">Zelte &amp; Räume</Link> das Feld „Aufstellraum" wählen.
            </p>
          ) : (
            <ul className="st-liste">
              {stand.zelte.map((zelt) => <li key={zelt.id}>{zelt.name}</li>)}
            </ul>
          )}
        </V1Section>
      )}
    </>
  )
}
