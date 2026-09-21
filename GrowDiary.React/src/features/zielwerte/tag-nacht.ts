/**
 * Fork AI (forkai.130): Tag- und Nachtzeilen im Alarm-Blatt — der Rechenteil
 * ohne React. Mockup Stand 4/6b, freigegeben von Bru am 21.09.2026.
 */
import { zahlOderNull } from '../../zahlenfeld'
import type { BlattUebergabe } from './WertBlatt'

/** Welche Übergabe-Rollen zu Tag und Nacht einer Messgröße gehören. */
export const TAG_NACHT_ROLLEN: Record<string, { tag: readonly string[]; nacht: readonly string[]; abweichung: boolean }> = {
  temperature: { tag: ['luft-unten', 'luft-oben'], nacht: ['luft-nacht-unten', 'luft-nacht-oben'], abweichung: true },
  humidity: { tag: ['feuchte-oben'], nacht: ['feuchte-nacht-oben'], abweichung: false },
}

export type ZeilenStand = {
  /** Folgt die Zeile dem Plan (alle ihre Rollen)? null, wenn der Plan hier nichts übergibt. */
  folgtPlan: boolean | null
  /** Was der Plan für diese Zeile setzen würde. */
  planVon: number | null
  planBis: number | null
}

const zahl = zahlOderNull

/** Stand einer Zeile aus den Übergabe-Zeilen (Rollen „…-unten"/„…-oben"). */
export function zeilenStand(uebergabe: readonly BlattUebergabe[], rollen: readonly string[]): ZeilenStand {
  const eigene = uebergabe.filter((u) => rollen.includes(u.rolle))
  if (eigene.length === 0) return { folgtPlan: null, planVon: null, planBis: null }
  const unten = eigene.find((u) => u.rolle.endsWith('unten'))
  const oben = eigene.find((u) => u.rolle.endsWith('oben'))
  return {
    folgtPlan: eigene.every((u) => !u.zustand.toLowerCase().includes('von dir')),
    planVon: unten ? zahl(unten.wert) : null,
    planBis: oben ? zahl(oben.wert) : null,
  }
}

/** „Planwert 24 °C ± 3 K" — nur, wenn es eine Spanne um einen Wert ist. */
export function planwertText(stand: ZeilenStand, einheit: string | null): string | null {
  const e = einheit ? ` ${einheit}` : ''
  if (stand.planVon != null && stand.planBis != null) {
    const mitte = (stand.planVon + stand.planBis) / 2
    const halb = (stand.planBis - stand.planVon) / 2
    return `Planwert ${deutsch(mitte)}${e} ± ${deutsch(halb)} K`
  }
  if (stand.planBis != null) return `Planwert: höchstens ${deutsch(stand.planBis)}${e}`
  return null
}

/** „Plan wäre 17–23" für eine Zeile mit eigenem Wert. */
export function planWaereText(stand: ZeilenStand): string | null {
  if (stand.planVon != null && stand.planBis != null) return `Plan wäre ${deutsch(stand.planVon)}–${deutsch(stand.planBis)}`
  if (stand.planBis != null) return `Plan wäre höchstens ${deutsch(stand.planBis)}`
  return null
}

/**
 * Nur, wenn etwas offensichtlich nicht passen kann: ein Band von höchstens 1
 * (bei 1 °C Spielraum meldet der Fork im Takt der Pause — Bru, 21.09.2026).
 */
export function engesBand(zeile: string, von: string, bis: string, einheit: string | null): string | null {
  const a = zahl(von)
  const b = zahl(bis)
  if (a == null || b == null || b - a > 1) return null
  return `${zeile}: ${deutsch(a)}–${deutsch(b)}${einheit ? ` ${einheit}` : ''} lässt kaum Abweichung zu — dann käme ständig eine Meldung. Trotzdem speichern?`
}

/** „Flores · Woche 5" → „Blütewoche 5", „Vega · Woche 2" → „Vegiwoche 2"; sonst unverändert. */
export function deutscheWoche(label: string | null): string | null {
  if (!label) return label
  const nr = /(\d+)\s*$/.exec(label)?.[1]
  if (!nr) return label
  if (/flores|flower|blüte/i.test(label)) return `Blütewoche ${nr}`
  if (/vega|veg|vegi/i.test(label)) return `Vegiwoche ${nr}`
  return label
}

function deutsch(n: number): string {
  return n.toLocaleString('de-DE', { maximumFractionDigits: 1 })
}

/** Der Planwert einer Zeile als Zahl (Mitte des Bands bzw. Obergrenze). */
export function planwert(stand: ZeilenStand | null): number | null {
  if (!stand) return null
  if (stand.planVon != null && stand.planBis != null) return (stand.planVon + stand.planBis) / 2
  return stand.planBis
}

/** „tags 24 °C, nachts 20 °C" bzw. „tags und nachts 24 °C". */
export function planKurz(tag: ZeilenStand | null, nacht: ZeilenStand | null, wieTag: boolean | null, einheit: string | null): string | null {
  const t = planwert(tag)
  if (t == null) return null
  const e = einheit ? ` ${einheit}` : ''
  const n = planwert(nacht)
  if (wieTag || n == null || Math.abs(n - t) < 1e-9) return `tags und nachts ${deutsch(t)}${e}`
  return `tags ${deutsch(t)}${e}, nachts ${deutsch(n)}${e}`
}

/** Weicht der Entwurf einer Zeile vom Plan ab (auch vor dem Speichern)? */
export function weichtVomPlanAb(stand: ZeilenStand | null, von: string, bis: string): boolean {
  if (!stand || stand.folgtPlan == null) return false
  if (stand.folgtPlan === false) return true
  const a = zahl(von)
  const b = zahl(bis)
  const anders = (x: number | null, y: number | null) => x != null && y != null && Math.abs(x - y) > 1e-9
  return anders(a, stand.planVon) || anders(b, stand.planBis)
}

