/* src/useHaTiefenlink.ts — Fork AI
   Ein Tipp auf eine Push-Meldung soll dort landen, wo die Sache steht.

   Die Meldung trägt einen Pfad wie /app/<slug>/aufgaben (siehe
   SupervisorInfoService.PanelPath). Home Assistant öffnet damit sein
   App-Panel, lädt im iframe aber immer die Startseite der App — den Rest
   hinter dem Slug gibt es nur auf Nachfrage: Wer sich mit
   `home-assistant/subscribe-properties` anmeldet, bekommt
   `home-assistant/properties` mit `route.path` (z. B. „/aufgaben") — beim
   Anmelden und bei jeder Änderung von Route, Breite oder Fenstergröße
   (frontend: src/panels/app/ha-panel-app.ts).

   Bis forkai.167 hat sich die App nicht angemeldet; jeder Tipp endete
   ohnehin vorher in „404: Not Found", weil der Link auf /<slug> zeigte. */

import { useEffect, useRef } from 'react'
import { useNavigate } from 'react-router-dom'

/** Nur schlichte App-Pfade: „/aufgaben", „/zelte/3". Kein Schema, kein „//", keine Abfrage. */
const APP_PFAD = /^(\/[A-Za-z0-9_-]+)+$/

export type HaTiefenlink = {
  /** Die Seite in der App, z. B. „/aufgaben". */
  pfad: string
  /** Der HA-Pfad ohne den Rest, z. B. „/app/<slug>" — dorthin wird die Adresse zurückgesetzt. */
  praefix: string
}

/**
 * Liest aus einer Nachricht von Home Assistant die Seite, auf die der Link
 * zeigt — oder null, wenn die Nachricht keine ist oder auf die Startseite zeigt.
 */
export function tiefenlinkAus(nachricht: unknown): HaTiefenlink | null {
  if (typeof nachricht !== 'object' || nachricht === null) return null
  const { type, route } = nachricht as { type?: unknown; route?: unknown }
  if (type !== 'home-assistant/properties') return null
  if (typeof route !== 'object' || route === null) return null
  const { path, prefix } = route as { path?: unknown; prefix?: unknown }
  if (typeof path !== 'string' || typeof prefix !== 'string') return null
  if (!APP_PFAD.test(path) || !APP_PFAD.test(prefix)) return null
  return { pfad: path, praefix: prefix }
}

/**
 * Meldet die App bei Home Assistant an und öffnet die Seite aus dem Link.
 *
 * Danach wird die Adresse in HA auf das Präfix zurückgesetzt. Sonst stünde
 * dort weiter „…/aufgaben", und ein zweiter Tipp auf dieselbe Art Meldung
 * änderte nichts an der Route — HA schickte zwar wieder Eigenschaften, aber
 * dieselben wie bei jeder Größenänderung, und die App bliebe, wo man
 * inzwischen hingeblättert hat.
 */
export function useHaTiefenlink(): void {
  // navigate wechselt bei jedem Seitenwechsel seine Identität. Hinge der
  // Effekt daran, meldete sich die App bei jedem Klick ab und wieder an.
  const navigate = useNavigate()
  const navigateRef = useRef(navigate)
  useEffect(() => {
    navigateRef.current = navigate
  }, [navigate])

  useEffect(() => {
    // Nicht eingebettet (eigener Tab, Entwicklung): niemand, der etwas schickt.
    if (window.parent === window) return
    const eltern = window.parent
    const herkunft = window.location.origin

    const empfangen = (ereignis: MessageEvent) => {
      // Nur das HA-Frontend direkt über uns, und nur unter derselben Herkunft
      // — der Ingress läuft immer unter der HA-Adresse.
      if (ereignis.source !== eltern || ereignis.origin !== herkunft) return
      const link = tiefenlinkAus(ereignis.data)
      if (!link) return
      navigateRef.current(link.pfad, { replace: true })
      eltern.postMessage(
        { type: 'home-assistant/navigate', path: link.praefix, options: { replace: true } },
        herkunft,
      )
    }

    window.addEventListener('message', empfangen)
    eltern.postMessage({ type: 'home-assistant/subscribe-properties' }, herkunft)
    return () => {
      window.removeEventListener('message', empfangen)
      eltern.postMessage({ type: 'home-assistant/unsubscribe-properties' }, herkunft)
    }
  }, [])
}
