import { useCallback, useEffect, useState } from 'react'
import { apiFetch, formatApiError } from '../../api'
import type { EntfeuchtungEinrichtung } from './steuerung-typen'

/**
 * Fork AI (A-015): Wie viele Entfeuchter es gibt — was der Nutzer in der „Einrichtung" gesagt hat.
 *
 * Geschrieben wird sofort beim Umschalten (kein Speichern-Knopf): es ist eine Angabe über die Anlage,
 * keine Regel. Die Regelung in Home Assistant bleibt unberührt.
 */
export function useEntfeuchtungEinrichtung() {
  const [einrichtung, setEinrichtung] = useState<EntfeuchtungEinrichtung | null>(null)
  const [fehler, setFehler] = useState<string | null>(null)
  const [arbeitet, setArbeitet] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    void apiFetch<EntfeuchtungEinrichtung>('/api/steuerung/entfeuchtung-einrichtung', { signal: controller.signal })
      .then((e) => { if (!controller.signal.aborted) setEinrichtung(e) })
      .catch(() => undefined)
    return () => controller.abort()
  }, [])

  /** true/false: ausdrücklich; null: der Fork entscheidet nach der Zuordnung. */
  const zusatzSetzen = useCallback(async (zusatzVorhanden: boolean | null) => {
    setArbeitet(true)
    // Sofort zeigen, was gewählt wurde; scheitert das Speichern, springt es zurück.
    const vorher = einrichtung
    setEinrichtung({ zusatzVorhanden })
    try {
      const neu = await apiFetch<EntfeuchtungEinrichtung>('/api/steuerung/entfeuchtung-einrichtung', {
        method: 'PUT',
        body: JSON.stringify({ zusatzVorhanden }),
      })
      setEinrichtung(neu)
      setFehler(null)
    } catch (caught) {
      setEinrichtung(vorher)
      setFehler(formatApiError(caught, 'Die Einrichtung konnte nicht gespeichert werden.'))
    } finally {
      setArbeitet(false)
    }
  }, [einrichtung])

  return { einrichtung, fehler, arbeitet, zusatzSetzen }
}
