/* src/features/steuerung/fehler-reiter.ts */
import { useCallback, useEffect, useState } from 'react'
import { insBildRollen } from '../../components/reiter-ins-bild'

/**
 * Nach einem gesperrten Speichern das markierte Feld zeigen — auch auf einem anderen Reiter.
 *
 * <b>Der Befund (02.10.2026).</b> Ein leeres Feld sperrt das Speichern; oben
 * steht dann „Bitte die markierten Felder prüfen.". Lag das Feld auf einem
 * anderen Reiter, war nichts markiert zu sehen — der Nutzer suchte einen
 * Fehler, den die Seite ihm nicht zeigte.
 *
 * <b>Warum über den gerenderten Baum statt über eine Zuordnung Feld → Reiter.</b>
 * Eine solche Liste müsste neben jedem Formular gepflegt werden und kann nur an
 * dem scheitern, was schon draufsteht. Gesucht wird stattdessen, was wirklich
 * markiert auf dem Schirm steht (`.st-fehler`): erst auf dem offenen Reiter,
 * dann der Reihe nach auf den anderen. Wer ein Feld neu anlegt und `fehler`
 * durchreicht, ist damit automatisch dabei.
 *
 * Gerollt wird mit `insBildRollen` — es überspringt die feste Kopfleiste am
 * Handy (`.scroll-ziel`, `--mobil-kopf`).
 *
 * @returns `zeigen()`: nach jedem gesperrten Speichern aufrufen, im selben Zug
 *   wie `setFeldFehler`. Jeder Aufruf sucht neu — auch beim zweiten Mal mit
 *   denselben Fehlern.
 */
export function useFehlerZeigen<R extends string>(
  reiter: R,
  setReiter: (reiter: R) => void,
  alleReiter: ReadonlyArray<{ value: R }>,
): () => void {
  const [suche, setSuche] = useState<{ rest: R[] } | null>(null)

  const zeigen = useCallback(() => {
    setSuche({ rest: alleReiter.map((eintrag) => eintrag.value).filter((wert) => wert !== reiter) })
  }, [alleReiter, reiter])

  useEffect(() => {
    if (!suche) return undefined
    // Erst im nächsten Bild nachsehen: dann steht der Reiter samt Markierung
    // sicher im Baum — auch wenn die Feldfehler erst nach einer Antwort des
    // Backends gesetzt wurden.
    const bild = requestAnimationFrame(() => {
      const markiert = ersteMarkierung()
      if (markiert) {
        setSuche(null)
        insBildRollen(markiert.closest<HTMLElement>('.st-feldzeile') ?? markiert)
        return
      }
      const [naechster, ...rest] = suche.rest
      if (naechster === undefined) {
        // Kein Reiter zeigt eine Markierung. Die Meldung oben bleibt stehen.
        // Ein Feld, das der gewählte Modus ausblendet („Plan +"/„Fest"), landet
        // hier nicht: seine Zeile bleibt markiert und nennt Feld und Modus
        // (`modusFehler` in feld-fehler.ts).
        setSuche(null)
        return
      }
      setReiter(naechster)
      setSuche({ rest })
    })
    return () => cancelAnimationFrame(bild)
  }, [suche, reiter, setReiter])

  return zeigen
}

/** Das erste markierte Feld im Inhalt der Seite. */
function ersteMarkierung(): HTMLElement | null {
  const wurzel = document.querySelector('main') ?? document.body
  return wurzel.querySelector<HTMLElement>('.st-fehler')
}
