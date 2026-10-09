import { useCallback, useEffect, useRef, useState } from 'react'
import { apiFetch, formatApiError } from '../../api'
import type { SteuerungAuswahl } from './steuerung-typen'

/**
 * Fork AI (A-016): Welche Steuerungen der Nutzer hat.
 *
 * Geschrieben wird sofort beim Umschalten (kein Speichern-Knopf): es ist eine Angabe über die Anlage,
 * keine Regel. Abwählen blendet aus — Home Assistant und seine Automationen bleiben unberührt.
 *
 * @param nachAenderung wird nach jedem erfolgreichen Speichern gerufen (die Übersicht lädt neu)
 */
export function useSteuerungAuswahl(nachAenderung?: () => void) {
  const [auswahl, setAuswahl] = useState<SteuerungAuswahl | null>(null)
  const [fehler, setFehler] = useState<string | null>(null)
  const [arbeitet, setArbeitet] = useState(false)
  // Ein Schreibvorgang zur Zeit: ein zweiter Tipp währenddessen würde den ersten überholen.
  const laeuft = useRef(false)

  useEffect(() => {
    const controller = new AbortController()
    void apiFetch<SteuerungAuswahl>('/api/steuerung/auswahl', { signal: controller.signal })
      .then((a) => { if (!controller.signal.aborted) setAuswahl(a) })
      .catch(() => undefined)
    return () => controller.abort()
  }, [])

  /**
   * Eine Steuerung ein- oder ausschalten. Liest vorher den aktuellen Stand, damit ein zweiter
   * Tab oder ein veralteter Bildschirm keine fremde Auswahl überschreibt.
   */
  const umschalten = useCallback(async (kennung: string, gewaehlt: boolean): Promise<boolean> => {
    if (laeuft.current) return false
    laeuft.current = true
    setArbeitet(true)
    setFehler(null)
    // Sofort zeigen, was gewählt wurde — sonst springt der Schalter bis zur Antwort zurück.
    // Scheitert das Speichern, wird der Stand neu gelesen (unten, im catch).
    setAuswahl((a) => a && { ...a, eintraege: a.eintraege.map((e) => e.kennung === kennung ? { ...e, gewaehlt } : e) })
    try {
      const aktuell = await apiFetch<SteuerungAuswahl>('/api/steuerung/auswahl')
      const neu = new Set(aktuell.eintraege.filter((e) => e.gewaehlt).map((e) => e.kennung))
      if (gewaehlt) neu.add(kennung)
      else neu.delete(kennung)
      const gespeichert = await apiFetch<SteuerungAuswahl>('/api/steuerung/auswahl', {
        method: 'PUT',
        body: JSON.stringify({ gewaehlt: [...neu] }),
      })
      setAuswahl(gespeichert)
      nachAenderung?.()
      return true
    } catch (caught) {
      setFehler(formatApiError(caught, 'Die Auswahl konnte nicht gespeichert werden.'))
      void apiFetch<SteuerungAuswahl>('/api/steuerung/auswahl').then(setAuswahl).catch(() => undefined)
      return false
    } finally {
      laeuft.current = false
      setArbeitet(false)
    }
  }, [nachAenderung])

  return { auswahl, fehler, arbeitet, umschalten }
}
