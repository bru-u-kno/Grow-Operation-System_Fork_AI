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
   ohnehin vorher in „404: Not Found", weil der Link auf /<slug> zeigte.

   Zweiter Weg (05.10.2026): Wer Grow OS über ein Panel der HACS-Integration
   „Ingress" ohne HA-Kopfleiste öffnet, bekommt den Link /<panel>?index=<seite>
   (IngressPanelService). Beim ersten Öffnen lädt hass_ingress die Seite selbst
   in den iframe. Ist das Panel schon offen, lädt es nichts neu — dann liest
   die App ?index= aus der HA-Adresse (gleiche Herkunft) und öffnet die Seite. */

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
 * Liest aus der Abfrage der HA-Adresse die Seite, die ein Ingress-Panel öffnen
 * soll (`?index=live/3` → „/live/3") — oder null ohne gültige Seite.
 */
export function indexSeiteAus(suche: string): string | null {
  const index = new URLSearchParams(suche).get('index')
  if (!index) return null
  const pfad = `/${index.replace(/^\/+|\/+$/g, '')}`
  return APP_PFAD.test(pfad) ? pfad : null
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

  // Ingress-Panel (hass_ingress): die Seite steht als ?index= in der HA-Adresse.
  useEffect(() => {
    if (window.parent === window) return
    const eltern = window.parent
    try {
      void eltern.location.search
    } catch {
      return // fremde Herkunft — nicht Home Assistant über uns
    }

    // Nur die Adresse des eigenen Panels: wechselt HA zu einem anderen Panel mit
    // ?index=, lebt dieser iframe noch einen Augenblick — sein index gehört dem
    // anderen Panel und darf weder gelesen noch gelöscht werden (Prüfer 05.10.2026).
    const panelPfad = eltern.location.pathname

    const pruefen = () => {
      if (eltern.location.pathname !== panelPfad) return
      const seite = indexSeiteAus(eltern.location.search)
      if (!seite) return
      navigateRef.current(seite, { replace: true })
      // ?index= wieder entfernen: sonst öffnet ein Neuladen die alte Seite, und
      // ein zweiter Tipp auf dieselbe Art Meldung änderte die Adresse nicht.
      const adresse = new URL(eltern.location.href)
      adresse.searchParams.delete('index')
      eltern.history.replaceState(eltern.history.state, '', adresse.pathname + adresse.search + adresse.hash)
    }

    pruefen()
    // Das HA-Frontend meldet jeden Seitenwechsel als „location-changed".
    eltern.addEventListener('location-changed', pruefen)
    eltern.addEventListener('popstate', pruefen)
    return () => {
      eltern.removeEventListener('location-changed', pruefen)
      eltern.removeEventListener('popstate', pruefen)
    }
  }, [])
}
