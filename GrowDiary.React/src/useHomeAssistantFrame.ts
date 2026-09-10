/* src/useHomeAssistantFrame.ts — Fork AI
   Zwei Fragen an den Rahmen, in dem Grow OS gerade läuft:
   zeichnet Home Assistant seine eigene Kopfleiste über uns, und kommen wir
   von hier aus dorthin zurück?

   Hintergrund: Grow OS ist ein Add-on und läuft im Ingress von Home Assistant,
   also in einem iframe. Darüber liegt normalerweise die HA-Kopfleiste mit dem
   Menü-Zeichen. Ein Add-on kann die nicht entfernen — das kann nur die
   HACS-Integration „Ingress“ (lovelylain/hass_ingress) mit `ui_mode: normal`,
   die einen eigenen Seitenleisten-Eintrag ohne Kopfleiste anlegt. Siehe FORK.md.

   Deshalb erkennt der Fork den Zustand, statt ihn vorauszusetzen:
   - Kopfleiste vorhanden  -> unsere Titelzeile bliebe der zweite Namenszug
                              untereinander; sie zeigt dann nur die Knöpfe.
   - Kopfleiste weg        -> unsere Titelzeile trägt den Namen, damit man
                              sieht, wo man ist.

   Ohne die Integration funktioniert also alles, es sieht nur etwas anders aus.
   Das ist Absicht: ein Add-on kann keine Integration mitinstallieren, und wer
   den Fork von GitHub holt, soll ihn ohne Zusatzschritt benutzen können. */

import { useEffect, useState } from 'react'

export type HomeAssistantFrame = {
  /** true, wenn KEINE HA-Kopfleiste über uns liegt — wir also den Namen tragen. */
  vollbild: boolean
  /** true, wenn wir im HA-Frame stecken und dorthin zurückspringen können. */
  ruecksprungMoeglich: boolean
}

/**
 * Liegt über dem iframe die HA-Kopfleiste?
 *
 * Messbar ist das nur indirekt: das iframe darf das Elternfenster nur lesen,
 * wenn beide dieselbe Herkunft haben. Beim Ingress ist das so (beides läuft
 * unter der HA-Adresse), bei einem fremden Reverse-Proxy nicht — dann wirft der
 * Zugriff, und wir behandeln den Fall wie „kein Rücksprung“.
 *
 * Der Vergleich der Höhen ist die verlässlichste Auskunft: die Kopfleiste ist
 * die einzige Sache, die zwischen Fensterhöhe und iframe-Höhe liegt. Ein
 * Unterschied von mehr als 24 px heißt: da ist etwas. Kleinere Abweichungen
 * kommen von Rundung und Rändern.
 */
function messen(): HomeAssistantFrame {
  // Nicht eingebettet: eigener Tab oder eigene Adresse. Dann tragen wir den
  // Namen und es gibt nichts, wohin man zurückspringen könnte.
  if (window.self === window.top) {
    return { vollbild: true, ruecksprungMoeglich: false }
  }

  try {
    const aussen = window.top!.innerHeight
    const innen = window.innerHeight
    const kopfleiste = aussen - innen > 24
    return { vollbild: !kopfleiste, ruecksprungMoeglich: true }
  } catch {
    // Fremde Herkunft: wir stecken in einem iframe, dürfen aber nicht
    // hinaussehen. Den Namen zeigen wir dann lieber — im Zweifel doppelt
    // benannt statt gar nicht.
    return { vollbild: true, ruecksprungMoeglich: false }
  }
}

export function useHomeAssistantFrame(): HomeAssistantFrame {
  const [frame, setFrame] = useState<HomeAssistantFrame>(messen)

  useEffect(() => {
    // Beim Drehen des Telefons ändert sich die Höhe, und mit ihr die Rechnung.
    const neu = () => setFrame(messen())
    window.addEventListener('resize', neu)
    window.addEventListener('orientationchange', neu)
    return () => {
      window.removeEventListener('resize', neu)
      window.removeEventListener('orientationchange', neu)
    }
  }, [])

  return frame
}

/**
 * Zurück in die normale Home-Assistant-Ansicht.
 *
 * Zielt auf das Dashboard, nicht auf die Startseite: wer hier arbeitet, will in
 * aller Regel zu seinen Grow-Karten und nicht zur Übersicht aller Räume. Das
 * Ziel steht in den Einstellungen, damit es nicht auf Bru's Pfad festgenagelt
 * ist — bei jemand anderem heißt das Dashboard anders.
 */
export function zurueckZuHomeAssistant(pfad: string): void {
  const ziel = pfad.startsWith('/') ? pfad : `/${pfad}`
  try {
    if (window.top && window.top !== window.self) {
      window.top.location.href = ziel
      return
    }
  } catch {
    // Fremde Herkunft — dann bleibt nur der eigene Rahmen.
  }
  window.location.href = ziel
}
