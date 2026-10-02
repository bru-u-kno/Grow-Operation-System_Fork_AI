/**
 * Wann die letzte Messung war — so, wie der Kopf der Live-Seite es zeigt.
 *
 * Bis zum 02.10.2026 stand dort nur die Uhrzeit („Letzte Messung 09:30"). Eine
 * Messung von vor drei Tagen sah damit genauso frisch aus wie eine von heute
 * früh. Jetzt: heute nur die Uhrzeit, gestern „gestern 09:30", sonst mit Datum
 * (und Jahr, wenn es nicht das laufende ist).
 */
export function messZeitpunkt(iso: string, jetzt: Date = new Date()): string {
  const datum = new Date(iso)
  if (Number.isNaN(datum.getTime())) return ''
  const uhr = new Intl.DateTimeFormat('de-DE', { hour: '2-digit', minute: '2-digit' }).format(datum)
  const tag = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime()
  const abstand = Math.round((tag(jetzt) - tag(datum)) / 86_400_000)
  if (abstand === 0) return uhr
  if (abstand === 1) return `gestern ${uhr}`
  const mitJahr = datum.getFullYear() !== jetzt.getFullYear()
  const kalender = new Intl.DateTimeFormat('de-DE', mitJahr
    ? { day: '2-digit', month: '2-digit', year: 'numeric' }
    : { day: '2-digit', month: '2-digit' }).format(datum)
  return `${kalender} ${uhr}`
}
