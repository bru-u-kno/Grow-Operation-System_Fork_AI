/* src/ki-aktiv.ts — Fork AI (A-011)
   Der globale Schalter „KI-Funktionen": ein Zustand für die ganze Oberfläche.

   Bis die Antwort des Servers da ist, gilt „aus" — wer keine KI will, sieht sie
   nie aufblitzen. `geladen` sagt, ob die Antwort schon da ist; wer eine Seite
   wegschickt (NurMitKi), wartet darauf, statt bei jedem Neuladen umzuleiten. */

import { createContext, useContext } from 'react'

export type KiAktivZustand = {
  aktiv: boolean
  /** true, sobald der Server geantwortet hat (oder nicht erreichbar war). */
  geladen: boolean
  /** Setzt den Schalter am Server; wirft bei einem Fehler. */
  setzen: (aktiv: boolean) => Promise<void>
}

export const KiAktivKontext = createContext<KiAktivZustand>({
  aktiv: false,
  geladen: false,
  setzen: async () => {},
})

export function useKiAktiv(): KiAktivZustand {
  return useContext(KiAktivKontext)
}
