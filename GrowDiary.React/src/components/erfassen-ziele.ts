/* src/components/erfassen-ziele.ts — Fork AI
   Die Ziele des Erfassen-Blatts, getrennt von der Komponente.

   Warum eine eigene Datei: eine Datei, die eine Komponente UND eine Konstante
   ausfuehrt, bricht das schnelle Neuladen im Entwicklungsbetrieb (Vite kann
   dann nicht mehr nur die Komponente tauschen). Der Linter meldet genau das —
   `react-refresh/only-export-components`. */

export type ErfassenZiel = {
  to: string
  icon: string
  label: string
  hint: string
}

/** Nach Häufigkeit, nicht alphabetisch: was man täglich tippt, steht oben. */
export const erfassenZiele: ErfassenZiel[] = [
  { to: '/messung', icon: '◎', label: 'Messung', hint: 'pH, EC, Wassertemperatur, ORP' },
  { to: '/addback', icon: '⤓', label: 'Addback', hint: 'Nachfüllen und Dünger nach Feed-Chart' },
  { to: '/wasserwechsel', icon: '⟳', label: 'Wasserwechsel', hint: 'Reservoir neu ansetzen' },
  { to: '/journal', icon: '✎', label: 'Notiz & Foto', hint: 'Beobachtung fürs Journal' },
  { to: '/kosten', icon: '€', label: 'Kosten', hint: 'Nachfüllung oder Anschaffung' },
]
