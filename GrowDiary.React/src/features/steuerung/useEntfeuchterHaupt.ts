import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { apiFetch, formatApiError } from '../../api'
import { hystereseStufe } from './entfeuchter-band'
import { feldFehlerAus, leereZahlenfelder, ohneLuecken } from './feld-fehler'
import type { EntfeuchterEinstellungen, EntfeuchterSeite } from './steuerung-typen'

/**
 * Fork AI (A-015): Der Zustand des Hauptentfeuchters — aus der früheren Seite
 * `EntfeuchterDetail` herausgelöst, damit die Seite „Entfeuchtung" ihn mit dem
 * des Zusatz-Entfeuchters zusammen zeigen und mit EINEM Knopf speichern kann.
 *
 * Verhalten unverändert: laden, jede Minute auffrischen (ein angefangener
 * Entwurf bleibt), beim Speichern ALLE Einstellungen schreiben und leere Felder
 * vorher sperren.
 */
export function useEntfeuchterHaupt() {
  const [seite, setSeite] = useState<EntfeuchterSeite | null>(null)
  const [entwurf, setEntwurf] = useState<EntfeuchterEinstellungen | null>(null)
  const [eigeneHysterese, setEigeneHysterese] = useState(false)
  const [fehler, setFehler] = useState<string | null>(null)
  const [feldFehler, setFeldFehler] = useState<Record<string, string>>({})
  const [meldung, setMeldung] = useState<string | null>(null)
  const [laedt, setLaedt] = useState(true)
  const [arbeitet, setArbeitet] = useState(false)
  // Der zuletzt geladene Stand: Bezug für „was hat der Nutzer angefasst“.
  const geladenRef = useRef<EntfeuchterEinstellungen | null>(null)

  const auffrischen = useCallback(async () => {
    try {
      const geladen = await apiFetch<EntfeuchterSeite>('/api/steuerung/entfeuchter')
      const alt = geladenRef.current
      geladenRef.current = geladen.einstellungen
      setSeite(geladen)
      // Einen angefangenen Entwurf nicht überschreiben — aber was der Nutzer nicht angefasst hat, folgt dem
      // frischen Stand. Sonst schriebe ein späteres Speichern alte Werte zurück nach Home Assistant, etwa wenn
      // der Zusatz-Entfeuchter oder jemand in Home Assistant die Höchsttemperatur inzwischen geändert hat.
      setEntwurf((vorher) => {
        if (!vorher || !alt) return vorher ?? geladen.einstellungen
        const neu = { ...vorher } as Record<string, unknown>
        for (const [feld, wert] of Object.entries(geladen.einstellungen)) {
          if (JSON.stringify((alt as Record<string, unknown>)[feld]) === JSON.stringify((vorher as Record<string, unknown>)[feld])) neu[feld] = wert
        }
        return neu as EntfeuchterEinstellungen
      })
    } catch {
      // Ein misslungenes Auffrischen ist kein Grund, die Seite rot zu färben.
    }
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    const laden = async () => {
      setLaedt(true)
      try {
        const geladen = await apiFetch<EntfeuchterSeite>('/api/steuerung/entfeuchter', { signal: controller.signal })
        if (!controller.signal.aborted) {
          geladenRef.current = geladen.einstellungen
          setSeite(geladen)
          setEntwurf(geladen.einstellungen)
          setEigeneHysterese(hystereseStufe(geladen.einstellungen.hystereseProzent) == null)
          setFehler(null)
        }
      } catch (caught) {
        if (!controller.signal.aborted) setFehler(formatApiError(caught, 'Die Entfeuchter-Steuerung konnte nicht geladen werden.'))
      } finally {
        if (!controller.signal.aborted) setLaedt(false)
      }
    }
    void laden()
    return () => controller.abort()
  }, [])

  // Eine Minute Takt: die Regelung selbst prüft alle fünf Minuten.
  useEffect(() => {
    const uhr = window.setInterval(() => { void auffrischen() }, 60000)
    return () => window.clearInterval(uhr)
  }, [auffrischen])

  const geaendert = useMemo(
    () => Boolean(seite && entwurf) && JSON.stringify(seite?.einstellungen) !== JSON.stringify(entwurf),
    [seite, entwurf],
  )

  /** @returns false, wenn das Speichern gesperrt war oder scheiterte (dann steht die Ursache in `fehler`/`feldFehler`). */
  const speichern = async (): Promise<boolean> => {
    if (!entwurf) return true
    const leer = leereZahlenfelder(entwurf)
    if (leer) { setFeldFehler(leer); setMeldung(null); setFehler('Bitte die markierten Felder prüfen.'); return false }
    setArbeitet(true); setMeldung(null); setFeldFehler({})
    try {
      const zurueck = await apiFetch<EntfeuchterSeite>('/api/steuerung/entfeuchter', { method: 'PUT', body: JSON.stringify(entwurf) })
      geladenRef.current = zurueck.einstellungen
      setSeite(zurueck); setEntwurf(zurueck.einstellungen); setFehler(null)
      setMeldung(zurueck.haAngenommen === false
        ? 'Gespeichert — aber nicht alle Helfer haben den Wert angenommen.'
        : 'Gespeichert.')
      return true
    } catch (caught) {
      const felder = feldFehlerAus(caught)
      if (felder) setFeldFehler(felder)
      setFehler(felder ? 'Bitte die markierten Felder prüfen.' : formatApiError(caught, 'Speichern fehlgeschlagen.'))
      return false
    } finally {
      setArbeitet(false)
    }
  }

  const setz = <K extends keyof EntfeuchterEinstellungen>(feld: K, wert: EntfeuchterEinstellungen[K]) => {
    setEntwurf((alt) => (alt ? { ...alt, [feld]: wert } : alt))
  }

  const anzeige = seite && entwurf ? ohneLuecken(entwurf, seite.einstellungen) : null

  return {
    seite, entwurf, anzeige, laedt, fehler, feldFehler, meldung, arbeitet, geaendert,
    eigeneHysterese, setEigeneHysterese, setz, speichern, auffrischen,
  }
}

export type EntfeuchterHauptModell = ReturnType<typeof useEntfeuchterHaupt>
