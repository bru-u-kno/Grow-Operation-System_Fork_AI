/* src/KiAktivProvider.tsx — Fork AI (A-011)
   Lädt den Schalter „KI-Funktionen" einmal und gibt ihn an die ganze App. */

import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { apiFetch } from './api'
import { KiAktivKontext, type KiAktivZustand } from './ki-aktiv'

type KiHauptschalterDto = { aktiv: boolean }

const ENDPUNKT = '/api/settings/ki'

export function KiAktivProvider({ children }: { children: ReactNode }) {
  const [aktiv, setAktiv] = useState(false)
  const [geladen, setGeladen] = useState(false)

  useEffect(() => {
    let abgebrochen = false
    apiFetch<KiHauptschalterDto>(ENDPUNKT)
      .then((dto) => {
        if (!abgebrochen) setAktiv(dto?.aktiv === true)
      })
      .catch(() => {
        // Ohne Antwort bleibt die KI aus: lieber eine KI-Seite zu wenig als eine,
        // die der Anwender abgeschaltet hat.
      })
      .finally(() => {
        if (!abgebrochen) setGeladen(true)
      })
    return () => {
      abgebrochen = true
    }
  }, [])

  const setzen = useCallback(async (neu: boolean) => {
    const dto = await apiFetch<KiHauptschalterDto>(ENDPUNKT, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ aktiv: neu }),
    })
    setAktiv(dto?.aktiv === true)
  }, [])

  const wert = useMemo<KiAktivZustand>(() => ({ aktiv, geladen, setzen }), [aktiv, geladen, setzen])
  return <KiAktivKontext.Provider value={wert}>{children}</KiAktivKontext.Provider>
}
