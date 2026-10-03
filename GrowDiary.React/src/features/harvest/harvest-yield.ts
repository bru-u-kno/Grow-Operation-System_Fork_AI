/** Ableitungen aus den Erntegewichten. */
import { zahlOderNull } from '../../zahlenfeld'
import { formatNumber } from '../../utils'

/** Trockenausbeute in Prozent — üblich sind 20–25 %. */
export function summariseYield(wetWeightG: string, dryWeightG: string): { text: string } | null {
  const wet = zahlOderNull(wetWeightG)
  const dry = zahlOderNull(dryWeightG)
  if (wet == null || dry == null || wet <= 0 || dry <= 0) return null
  const percent = (dry / wet) * 100
  // Über 100 % ist keine Ausbeute, sondern ein Zahlendreher — das zu sagen ist
  // hilfreicher, als eine unmögliche Zahl auszugeben.
  if (dry > wet) return { text: 'Trockengewicht über Frischgewicht — vermutlich vertauscht.' }
  // Deutsch geschrieben, wie jede Zahl auf dem Schirm: bis 03.10.2026 stand
  // hier `toFixed(1)` — „Trockenausbeute 22.0 %" mit englischem Punkt
  // (offene Punkte D5; e2e/deutsche-zahlen.spec.ts sah die Zeile nicht, weil
  // sie erst nach dem Eintippen beider Gewichte erscheint).
  return { text: `Trockenausbeute ${formatNumber(percent, 1)} % (${formatNumber(dry, 1)} g von ${formatNumber(wet, 1)} g)` }
}
