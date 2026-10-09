import { useCallback, useEffect, useRef, useState } from 'react'
import { apiFetch, formatApiError } from '../../api'
import type { SteuerungAuswahl } from './steuerung-typen'

/**
 * Fork AI (A-016): Welche Steuerungen der Nutzer hat.
 *
 * Geschrieben wird sofort beim Umschalten (kein Speichern-Knopf): es ist eine Angabe über die Anlage,
 * keine Regel. Abwählen blendet aus — Home Assistant und seine Automationen bleiben unberührt.
 *
 * <b>Mehrere Tipps hintereinander gehen alle durch.</b> Jeder Schreibvorgang liest vorher den aktuellen Stand und
 * schreibt die ganze Liste; zwei gleichzeitig würden sich überholen, einer ginge verloren (gemessen: Zuluft an,
 * gleich danach Lampe aus — die Lampe blieb an). Deshalb laufen sie hintereinander in einer Kette; der Schalter
 * springt sofort, die Kette holt den Server nach.
 *
 * @param nachAenderung wird nach jedem erfolgreichen Speichern gerufen (die Übersicht lädt neu)
 */
export function useSteuerungAuswahl(nachAenderung?: () => void) {
  const [auswahl, setAuswahl] = useState<SteuerungAuswahl | null>(null)
  const [fehler, setFehler] = useState<string | null>(null)
  const [arbeitet, setArbeitet] = useState(false)
  const kette = useRef<Promise<unknown>>(Promise.resolve())
  const offen = useRef(0)

  useEffect(() => {
    const controller = new AbortController()
    void apiFetch<SteuerungAuswahl>('/api/steuerung/auswahl', { signal: controller.signal })
      .then((a) => { if (!controller.signal.aborted) setAuswahl(a) })
      .catch(() => undefined)
    return () => controller.abort()
  }, [])

  const umschalten = useCallback((kennung: string, gewaehlt: boolean): Promise<boolean> => {
    setFehler(null)
    // Sofort zeigen, was gewählt wurde — sonst springt der Schalter bis zur Antwort zurück.
    setAuswahl((a) => a && { ...a, eintraege: a.eintraege.map((e) => e.kennung === kennung ? { ...e, gewaehlt } : e) })
    offen.current += 1
    setArbeitet(true)

    const lauf = kette.current.then(async () => {
      try {
        const aktuell = await apiFetch<SteuerungAuswahl>('/api/steuerung/auswahl')
        const neu = new Set(aktuell.eintraege.filter((e) => e.gewaehlt).map((e) => e.kennung))
        if (gewaehlt) neu.add(kennung)
        else neu.delete(kennung)
        const gespeichert = await apiFetch<SteuerungAuswahl>('/api/steuerung/auswahl', {
          method: 'PUT',
          body: JSON.stringify({ gewaehlt: [...neu] }),
        })
        // Nur der letzte Schreibvorgang der Kette setzt den Stand: ein früherer würde spätere Tipps kurz zurückdrehen.
        if (offen.current === 1) setAuswahl(gespeichert)
        nachAenderung?.()
        return true
      } catch (caught) {
        setFehler(formatApiError(caught, 'Die Auswahl konnte nicht gespeichert werden.'))
        void apiFetch<SteuerungAuswahl>('/api/steuerung/auswahl').then(setAuswahl).catch(() => undefined)
        return false
      } finally {
        offen.current -= 1
        if (offen.current === 0) setArbeitet(false)
      }
    })
    kette.current = lauf
    return lauf
  }, [nachAenderung])

  return { auswahl, fehler, arbeitet, umschalten }
}
