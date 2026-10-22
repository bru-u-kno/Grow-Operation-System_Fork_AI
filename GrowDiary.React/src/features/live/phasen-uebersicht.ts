import { formatNumber } from '../../utils'
import { PHASEN_ANZEIGE, type Phase } from '../grows/phase-timeline'

/**
 * Texte der Phasen-Übersicht auf der Live-Seite (cc.266): eine Zeile je Phase, alles ausgeschrieben
 * („58 Tage · 8,3 Wochen“). Die Rechnung der Phasen steht in `phase-timeline.ts`; hier nur Wortlaut.
 */

export function tageText(tage: number): string {
  return tage === 1 ? '1 Tag' : `${tage} Tage`
}

/** Wochen mit höchstens einer Nachkommastelle: 14 Tage → „2 Wochen“, 58 Tage → „8,3 Wochen“. */
export function wochenText(tage: number): string {
  const wochen = Math.round((tage / 7) * 10) / 10
  return `${formatNumber(wochen, 1)} ${wochen === 1 ? 'Woche' : 'Wochen'}`
}

export type PhasenZeile = {
  name: string
  state: Phase['state']
  text: string
  /** Dieselbe Angabe in ihre Teile zerlegt (laufende Phase: Tag, Woche) — damit ein Umbruch nie mitten in einem Teil fällt. */
  teile: string[]
  /** Die laufende Phase dauert schon länger als geplant. */
  ueberPlan: boolean
}

function zeile(name: string, state: Phase['state'], teile: string[], ueberPlan = false): PhasenZeile {
  return { name, state, teile, text: teile.join(' · '), ueberPlan }
}

export function phasenZeile(phase: Phase): PhasenZeile {
  const name = PHASEN_ANZEIGE[phase.name]
  const tag = phase.dayInPhase
  if (phase.state === 'current' && tag != null) {
    // Mit Plan („Tag 61 von 70“) oder ohne (Anzucht, Wachstum ohne geplante Dauer: nur „Tag 12“).
    if (phase.progress != null && phase.days > 0) {
      if (tag > phase.days) {
        const drueber = tag - phase.days
        return zeile(name, phase.state, [`Tag ${tag} von ${phase.days}`, `${tageText(drueber)} über Plan`], true)
      }
      return zeile(name, phase.state, [`Tag ${tag} von ${phase.days}`, `Woche ${Math.ceil(tag / 7)} von ${formatNumber(phase.days / 7, 1)}`])
    }
    return zeile(name, phase.state, [`Tag ${tag}`, `Woche ${Math.ceil(tag / 7)}`])
  }
  if (phase.days === 0) return zeile(name, phase.state, ['Dauer offen'])
  return zeile(name, phase.state, [tageText(phase.days), wochenText(phase.days)])
}

/** Die Kopfzeile: laufende Phase und ihre Woche; null, wenn gerade keine läuft. */
export function phasenKopf(phases: Phase[]): { text: string; ueberPlan: boolean } | null {
  const aktuell = phases.find((phase) => phase.state === 'current')
  if (!aktuell) return null
  const name = PHASEN_ANZEIGE[aktuell.name]
  const tag = aktuell.dayInPhase
  if (tag == null) return { text: name, ueberPlan: false }
  const ueberPlan = aktuell.progress != null && aktuell.days > 0 && tag > aktuell.days
  const woche = Math.ceil(tag / 7)
  const von = aktuell.progress != null && aktuell.days > 0 && !ueberPlan ? ` von ${formatNumber(aktuell.days / 7, 1)}` : ''
  return { text: `${name} · Woche ${woche}${von}${ueberPlan ? ' · über Plan' : ''}`, ueberPlan }
}
