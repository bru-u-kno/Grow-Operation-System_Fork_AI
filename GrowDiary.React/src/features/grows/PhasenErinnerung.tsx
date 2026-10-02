import { useState } from 'react'
import { apiFetch } from '../../api'
import type { PhasenerinnerungDto } from '../../types'
import './phasen-erinnerung.css'

/**
 * Die offene Erinnerung des Phasenankers mit ihrem Knopf.
 *
 * Seit dem 02.10.2026 schaltet keine Phase mehr nach Tagen um: die Vegi (beim
 * Steckling „Bewurzelung abgeschlossen") und die Blüte einer Autoflower
 * beginnen mit der Bestätigung. Ab dem alten Schätzwert fragt der Server
 * danach — hier steht die Frage, und der Knopf ist dieselbe Aktion wie im
 * Grow-Detail und im Messformular (`/api/grows/{id}/actions/{aktion}`).
 */
export function PhasenErinnerung({ growId, erinnerung, onErledigt }: {
  growId: number
  erinnerung: PhasenerinnerungDto | null | undefined
  onErledigt?: () => void | Promise<void>
}) {
  const [laeuft, setLaeuft] = useState(false)
  const [fehler, setFehler] = useState<string | null>(null)
  // Die Rückmeldung gehört zu der Erinnerung, die bestätigt wurde. Taucht
  // danach eine andere auf (Autoflower: erst Vegi, später Blüte), gilt die neue.
  const [meldung, setMeldung] = useState<{ art: string, text: string } | null>(null)

  if (meldung && (!erinnerung || erinnerung.art === meldung.art)) {
    return <p className="pe-meldung" role="status" data-audit="phasen-erinnerung-erledigt">{meldung.text}</p>
  }
  if (!erinnerung) return null

  async function bestaetigen() {
    if (!erinnerung || laeuft) return
    setLaeuft(true)
    setFehler(null)
    try {
      const antwort = await apiFetch<{ message: string }>(`/api/grows/${growId}/actions/${erinnerung.aktion}`, { method: 'POST' })
      setMeldung({ art: erinnerung.art, text: antwort.message })
      await onErledigt?.()
    } catch (caught) {
      setFehler(caught instanceof Error ? caught.message : 'Konnte nicht gespeichert werden.')
    } finally {
      setLaeuft(false)
    }
  }

  return (
    <div className="pe-box" role="note" data-audit="phasen-erinnerung" data-art={erinnerung.art}>
      <p className="pe-text">{erinnerung.text}</p>
      <button type="button" className="v1-button is-primary pe-knopf" disabled={laeuft} onClick={() => void bestaetigen()}>
        {laeuft ? 'Wird gespeichert…' : erinnerung.knopf}
      </button>
      {fehler && <p className="pe-fehler" role="alert">{fehler}</p>}
    </div>
  )
}
