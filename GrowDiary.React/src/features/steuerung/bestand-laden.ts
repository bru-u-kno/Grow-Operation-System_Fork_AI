import { useCallback, useEffect, useState } from 'react'
import { apiFetch } from '../../api'
import type { Bestandsaufnahme } from './steuerung-typen'

/**
 * Den Bestand einer Steuerung laden (`GET /api/steuerung/{modul}/bestand`).
 *
 * Ein eigener Abruf, damit ein Fehler hier die Seite nicht mitnimmt — fehlende
 * Bauteile sind ein Hinweis, kein Grund, die Werte zu verbergen. Angezeigt wird
 * mit `BestandHinweis` und `BestandAbschnitt` aus `Bestand.tsx`.
 */
export function useBestand(modul: string): { bestand: Bestandsaufnahme | null; neuLaden: () => Promise<void> } {
  const [bestand, setBestand] = useState<Bestandsaufnahme | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    const pruefen = async () => {
      try {
        const geladen = await apiFetch<Bestandsaufnahme>(`/api/steuerung/${modul}/bestand`, { signal: controller.signal })
        if (!controller.signal.aborted) setBestand(geladen)
      } catch {
        if (!controller.signal.aborted) setBestand(null)
      }
    }
    void pruefen()
    return () => controller.abort()
  }, [modul])

  const neuLaden = useCallback(async () => {
    setBestand(await apiFetch<Bestandsaufnahme>(`/api/steuerung/${modul}/bestand`))
  }, [modul])

  return { bestand, neuLaden }
}
