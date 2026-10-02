/**
 * Fork AI (forkai.125): Lesart der Wochenzeile über den Reitern von
 * „Ziele & Meldungen“. Die Daten kommen unverändert aus `/api/wochenplan` —
 * derselben Spaltenauswahl, die Kacheln, Alarme und die Übergabe benutzen.
 */

export type PlanWoche = {
  id: string
  label: string
  stage: string
  woche?: number | null
  istJetzt: boolean
  wirdGehalten: boolean
  ec: string | null
  ph: string | null
  wasser: string | null
  vpd: string | null
  rh: string | null
  luft: string | null
  co2: string | null
  ppfd: string | null
  dosierung: string | null
  /** Fork AI (02.10.2026): angehängt, weil die Phase länger läuft als das Programm. */
  verlaengert?: boolean
}

import type { PhasenerinnerungDto } from '../../types'

export type WochenPlan = {
  growId: number
  growName: string
  sorte: string | null
  programmName: string
  wochenZieleAktiv: boolean
  vegiStart?: string | null
  flip?: string | null
  erntefenster?: string | null
  jetztLabel: string | null
  haltehinweis?: string | null
  wochen: PlanWoche[]
  /** Offene Phasen-Erinnerung des Grows (Phasenanker) — fehlt, wenn keine offen ist. */
  erinnerung?: PhasenerinnerungDto | null
}

/** Der Plan des Grows, den auch die Reiter zeigen — sonst der erste. */
export function waehlePlan(plaene: WochenPlan[], growId: number | null): WochenPlan | null {
  if (plaene.length === 0) return null
  return plaene.find((p) => p.growId === growId) ?? plaene[0]
}

/** Die Anker, aus denen sich die Woche ergibt — nur die, die gesetzt sind. */
export function anker(plan: WochenPlan): Array<{ name: string; wert: string }> {
  return [
    { name: 'Vegi-Start', wert: plan.vegiStart },
    // Der Blütebeginn: Flip, bei der Autoflower „Blüte beginnt" (Phasenanker).
    { name: 'Blüte', wert: plan.flip },
    { name: 'Ernte', wert: plan.erntefenster },
  ].filter((a): a is { name: string; wert: string } => !!a.wert)
}

/** Ist die laufende Woche eine angehängte? */
export function laufendVerlaengert(plan: WochenPlan): boolean {
  return plan.wochen.some((w) => w.istJetzt && w.verlaengert)
}

/** Kurzfassung einer Woche für die Liste im Wochen-Blatt. */
export function wochenKurz(woche: PlanWoche): string {
  return [woche.ec && `EC ${woche.ec}`, woche.wasser, woche.rh].filter(Boolean).join(' · ')
}

/**
 * Welche Woche der Plan-Reiter zeigen soll, wenn er über das Wochen-Blatt
 * geöffnet wurde. `null` = Adresszeile nennt keine (oder eine unbekannte) Woche.
 */
export function wochenIndex(spalten: ReadonlyArray<{ id: string }>, wocheId: string | null): number | null {
  if (!wocheId) return null
  const i = spalten.findIndex((s) => s.id === wocheId)
  return i < 0 ? null : i
}
