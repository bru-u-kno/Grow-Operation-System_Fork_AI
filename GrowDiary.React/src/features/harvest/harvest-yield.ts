/** Ableitungen aus den Erntegewichten. */
import { zahlOderNull } from '../../zahlenfeld'

/** Trockenausbeute in Prozent — üblich sind 20–25 %. */
export function summariseYield(wetWeightG: string, dryWeightG: string): { text: string } | null {
  const wet = zahlOderNull(wetWeightG)
  const dry = zahlOderNull(dryWeightG)
  if (wet == null || dry == null || wet <= 0 || dry <= 0) return null
  const percent = (dry / wet) * 100
  // Über 100 % ist keine Ausbeute, sondern ein Zahlendreher — das zu sagen ist
  // hilfreicher, als eine unmögliche Zahl auszugeben.
  if (dry > wet) return { text: 'Trockengewicht über Frischgewicht — vermutlich vertauscht.' }
  return { text: `Trockenausbeute ${percent.toFixed(1)} % (${dry} g von ${wet} g)` }
}
