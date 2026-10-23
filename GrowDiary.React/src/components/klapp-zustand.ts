import { useCallback, useState } from 'react'

/**
 * Cultivate Control (cc.270): Was der Nutzer an einem Bereich auf- oder zugeklappt hat, am Gerät gemerkt.
 *
 * Anders als `useEingeklappt` der Live-Seite (dort ist alles offen und nur „eingeklappt" wird gemerkt) kennt
 * dieser Speicher beide Richtungen: Die Bereiche der übrigen Seiten starten teils zu, teils je nach Stand
 * offen. Gemerkt wird deshalb nur, was der Nutzer selbst entschieden hat; ohne Eintrag gilt der Standard der
 * Stelle. Im Browser abgelegt (Eigenschaft des Bildschirms, kein Datenmodell, keine Wanderung beim Update).
 *
 * Kennungen: `<seite>:<name>`, bei mehreren gleichen Bereichen mit Nummer (`verbrauch:12`).
 */
const KEY = 'growos.klapp'

function lese(): Record<string, boolean> {
  try {
    const gelesen: unknown = JSON.parse(localStorage.getItem(KEY) ?? 'null')
    if (!gelesen || typeof gelesen !== 'object' || Array.isArray(gelesen)) return {}
    return Object.fromEntries(Object.entries(gelesen).filter(([, wert]) => typeof wert === 'boolean'))
  } catch {
    return {}
  }
}

function schreibe(id: string, offen: boolean): void {
  try { localStorage.setItem(KEY, JSON.stringify({ ...lese(), [id]: offen })) } catch { /* Privatmodus */ }
}

/** `[gemerkt, setze]`: `gemerkt` ist `undefined`, solange der Nutzer nichts gewählt hat. Ohne Kennung wird nichts gemerkt. */
export function useKlappGemerkt(id?: string): [boolean | undefined, (offen: boolean) => void] {
  const [wert, setWert] = useState<boolean | undefined>(() => (id ? lese()[id] : undefined))
  const setze = useCallback((offen: boolean) => {
    setWert(offen)
    if (id) schreibe(id, offen)
  }, [id])
  return [wert, setze]
}
