import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { apiFetch, formatApiError } from '../../api'
import {
  VPD_STUFEN,
  aenderungBilden,
  entwurfNachfuehren,
  gleich,
  hilfeNachEinzelwert,
  istGeaendert,
  leereFelder,
  ohneLueckenZusatz,
  unbekannteFehler,
} from './entfeuchter-zusatz'
import { feldFehlerAus } from './feld-fehler'
import type { EntfeuchterZusatzEinstellungen, EntfeuchterZusatzSeite, ZusatzMeldung } from './steuerung-typen'

type Einstellungen = EntfeuchterZusatzEinstellungen

/**
 * Fork AI (A-015): Der Zustand des Zusatz-Entfeuchters — aus der früheren Seite
 * `EntfeuchterZusatzDetail` herausgelöst (siehe `useEntfeuchterHaupt`).
 *
 * <b>Speichern schreibt nur, was geändert wurde</b> (A-009): `aenderungBilden`
 * vergleicht mit dem zuletzt geladenen Stand. Verhalten unverändert.
 */
export function useEntfeuchterZusatz(aktiv = true) {
  const [seite, setSeite] = useState<EntfeuchterZusatzSeite | null>(null)
  const [entwurf, setEntwurf] = useState<Einstellungen | null>(null)
  const [eigeneHysterese, setEigeneHysterese] = useState(false)
  const [fehler, setFehler] = useState<string | null>(null)
  const [feldFehler, setFeldFehler] = useState<Record<string, string>>({})
  const [meldung, setMeldung] = useState<string | null>(null)
  const [laedt, setLaedt] = useState(aktiv)
  const [arbeitet, setArbeitet] = useState(false)
  // Der zuletzt geladene Stand: Bezug für „was hat sich geändert". Als Ref,
  // damit das Auffrischen ihn lesen kann, ohne selbst davon abzuhängen.
  const geladenRef = useRef<Einstellungen | null>(null)

  const auffrischen = useCallback(async () => {
    try {
      const neu = await apiFetch<EntfeuchterZusatzSeite>('/api/steuerung/entfeuchter-zusatz')
      const alt = geladenRef.current
      geladenRef.current = neu.einstellungen
      setSeite(neu)
      // Einen angefangenen Entwurf nicht überschreiben — aber was der Nutzer
      // nicht angefasst hat, folgt dem frischen Stand.
      setEntwurf((vorher) => (vorher && alt ? entwurfNachfuehren(alt, neu.einstellungen, vorher) : neu.einstellungen))
    } catch {
      // Ein misslungenes Auffrischen ist kein Grund, die Seite rot zu färben.
    }
  }, [])

  useEffect(() => {
    if (!aktiv) return undefined
    const controller = new AbortController()
    const laden = async () => {
      setLaedt(true)
      try {
        const geladen = await apiFetch<EntfeuchterZusatzSeite>('/api/steuerung/entfeuchter-zusatz', { signal: controller.signal })
        if (!controller.signal.aborted) {
          geladenRef.current = geladen.einstellungen
          setSeite(geladen)
          setEntwurf(geladen.einstellungen)
          setEigeneHysterese(!VPD_STUFEN.some((s) => gleich(s.wert, geladen.einstellungen.vpdHystereseKpa)))
          setFehler(null)
        }
      } catch (caught) {
        if (!controller.signal.aborted) setFehler(formatApiError(caught, 'Der Zusatz-Entfeuchter konnte nicht geladen werden.'))
      } finally {
        if (!controller.signal.aborted) setLaedt(false)
      }
    }
    void laden()
    return () => controller.abort()
  }, [aktiv])

  // Eine Minute Takt: die Regelung selbst prüft alle fünf Minuten.
  useEffect(() => {
    if (!aktiv) return undefined
    const uhr = window.setInterval(() => { void auffrischen() }, 60000)
    return () => window.clearInterval(uhr)
  }, [aktiv, auffrischen])

  const geladen = seite?.einstellungen ?? null
  const geaendert = useMemo(() => Boolean(geladen && entwurf) && istGeaendert(geladen!, entwurf!), [geladen, entwurf])

  /** @returns false, wenn das Speichern gesperrt war oder scheiterte. */
  const speichern = async (): Promise<boolean> => {
    if (!entwurf || !geladen) return true
    const leer = leereFelder(entwurf)
    if (leer) { setFeldFehler(leer); setMeldung(null); setFehler('Bitte die markierten Felder prüfen.'); return false }
    const koerper = aenderungBilden(geladen, entwurf)
    if (Object.keys(koerper).length === 0) return true
    setArbeitet(true); setMeldung(null); setFeldFehler({})
    try {
      const zurueck = await apiFetch<EntfeuchterZusatzSeite>('/api/steuerung/entfeuchter-zusatz', { method: 'PUT', body: JSON.stringify(koerper) })
      geladenRef.current = zurueck.einstellungen
      setSeite(zurueck); setEntwurf(zurueck.einstellungen); setFehler(null)
      setEigeneHysterese(!VPD_STUFEN.some((s) => gleich(s.wert, zurueck.einstellungen.vpdHystereseKpa)))
      setMeldung(zurueck.haAngenommen === false
        ? 'Gespeichert — aber nicht alle Helfer haben den Wert angenommen.'
        : 'Gespeichert.')
      return true
    } catch (caught) {
      const felder = feldFehlerAus(caught)
      if (felder) {
        setFeldFehler(felder)
        const rest = unbekannteFehler(felder)
        setFehler(rest.length > 0 ? `Bitte die markierten Felder prüfen. Außerdem: ${rest.join(' ')}` : 'Bitte die markierten Felder prüfen.')
      } else {
        setFehler(formatApiError(caught, 'Speichern fehlgeschlagen.'))
      }
      return false
    } finally {
      setArbeitet(false)
    }
  }

  const setz = <K extends keyof Einstellungen>(feld: K, wert: Einstellungen[K]) => {
    setEntwurf((alt) => (alt ? { ...alt, [feld]: wert } : alt))
  }
  const setzMeldung = <K extends keyof ZusatzMeldung>(feld: K, wert: ZusatzMeldung[K]) => {
    setEntwurf((alt) => (alt ? { ...alt, meldung: { ...alt.meldung, [feld]: wert } } : alt))
  }
  /** Einen Einzelwert ändern: die Hilfsstärke folgt (passt er zu einer Stufe, heißt sie so, sonst „eigene Werte"). */
  const setzEinzel = <K extends 'folgeAbstandK' | 'wiederEinAbstandK' | 'vpdHystereseKpa' | 'zuschaltVerzoegerungMin' | 'mindestpauseMin'>(feld: K, wert: number) => {
    setEntwurf((alt) => (alt ? hilfeNachEinzelwert({ ...alt, [feld]: wert }) : alt))
  }

  const anzeige = geladen && entwurf ? ohneLueckenZusatz(entwurf, geladen) : null

  return {
    seite, entwurf, geladen, anzeige, laedt, fehler, feldFehler, meldung, arbeitet, geaendert,
    eigeneHysterese, setEigeneHysterese, setEntwurf, setz, setzMeldung, setzEinzel, speichern, auffrischen,
  }
}

export type EntfeuchterZusatzModell = ReturnType<typeof useEntfeuchterZusatz>
