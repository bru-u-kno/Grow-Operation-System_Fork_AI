import { zahl } from './entfeuchter-band'
import type { EntfeuchterAbweichung } from './steuerung-typen'

/**
 * Neuling-Test 17.10.2026: Die Seite zeigte 60 %, Home Assistant regelte mit 30 % — und niemand sah es.
 * Der Satz für den Hinweis „Home Assistant regelt mit anderen Werten": je Helfer „hier X, in Home Assistant Y",
 * höchstens `max` Einträge, der Rest als „und N weitere" (der Hinweis soll kurz bleiben).
 */
export function abweichungsText(liste: EntfeuchterAbweichung[], max = 3): string {
  const wert = (v: number, einheit: string) => {
    const text = zahl(v, Number.isInteger(v) ? 0 : 1)
    return einheit ? `${text} ${einheit}` : text
  }
  const teile = liste.slice(0, max).map((a) => `${a.name}: hier ${wert(a.forkWert, a.einheit)}, in Home Assistant ${wert(a.haWert, a.einheit)}`)
  const mehr = liste.length - max
  if (mehr > 0) teile.push(`und ${mehr} weitere`)
  return teile.join(' · ')
}
