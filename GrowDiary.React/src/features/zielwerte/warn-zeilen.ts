/**
 * Fork AI (forkai.145, F-040): Zeilen der Warnung oben auf „Grenzwerte" —
 * je Wert Richtung, überschrittene Grenze und seit wann.
 */
export type WarnWert = {
  key: string
  name: string
  ist: string
  einheit: string | null
  lage: string
  alarmVon: number | null
  alarmBis: number | null
  istZahl: number | null
  meldetSeitUtc?: string | null
}

export type WarnZeile = { key: string; name: string; ist: string; einheit: string | null; richtung: string; grenze: string | null; seit: string | null }

const zahl = (x: number) => x.toLocaleString('de-DE', { maximumFractionDigits: 2, useGrouping: false })

/** „09:02", „gestern 18:10" oder „21.09. 18:10". */
export function seitText(iso: string | null | undefined, jetzt: Date = new Date()): string | null {
  if (!iso) return null
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return null
  const uhr = d.toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })
  const tag = (x: Date) => new Date(x.getFullYear(), x.getMonth(), x.getDate()).getTime()
  const diff = Math.round((tag(jetzt) - tag(d)) / 86_400_000)
  if (diff <= 0) return uhr
  if (diff === 1) return `gestern ${uhr}`
  return `${d.toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit' })} ${uhr}`
}

export function warnZeile(w: WarnWert, jetzt?: Date): WarnZeile {
  const unten = w.lage.startsWith('darunter') || (w.istZahl != null && w.alarmVon != null && w.istZahl < w.alarmVon)
  const e = w.einheit ? ` ${w.einheit}` : ''
  const grenze = unten
    ? (w.alarmVon != null ? `unter ${zahl(w.alarmVon)}${e}` : null)
    : (w.alarmBis != null ? `über ${zahl(w.alarmBis)}${e}` : null)
  return {
    key: w.key, name: w.name, ist: w.ist, einheit: w.einheit,
    richtung: unten ? 'zu niedrig' : 'zu hoch',
    grenze,
    seit: seitText(w.meldetSeitUtc, jetzt),
  }
}
