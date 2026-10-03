import type { ReactNode } from 'react'
import { bilanzText, type Bilanz } from './eingeklappt'
import { classNames } from '../../utils'

export type BandKlappe = { zu: boolean; bilanz: Bilanz; onUmschalten: () => void }

/**
 * Die Überschrift eines Messwert-Bereichs — dieselbe für die festen Bänder und
 * für eigene Bereiche, damit beide gleich einklappen.
 *
 * Eingeklappt steht dahinter, was der Bereich verschweigt: „6 Werte · 1
 * Warnung". Ohne die Zahl wäre ein zugeklappter Bereich mit rotem pH
 * stumm — genau das, was diese Seite nicht sein darf.
 */
export function BandTitel({ title, klappe }: { title: string; klappe?: BandKlappe }) {
  if (!klappe) {
    return (
      <div className="ls-band-label">
        <span>{title}</span>
        <i />
      </div>
    )
  }
  const { zu, bilanz } = klappe
  return (
    <div className="ls-band-label">
      <KlappTitel zu={zu} onUmschalten={klappe.onUmschalten}>
        <span>{title}</span>
        {zu && (
          <em className={classNames('ls-bilanz', bilanz.kritisch > 0 ? 'is-crit' : bilanz.warnungen > 0 && 'is-warn')}>
            {bilanzText(bilanz)}
          </em>
        )}
      </KlappTitel>
      <i />
    </div>
  )
}

/**
 * Fork AI: Die zwei Bedienelemente zum Ein- und Ausklappen der Live-Seite.
 *
 * `KlappTitel` macht die Überschrift eines Bereichs oder einer Karte selbst zum
 * Knopf — Pfeil und Name zusammen, damit die Trefferfläche nicht nur das
 * kleine Dreieck ist. `KachelKlappe` sitzt in der Ecke einer Kachel; die Kachel
 * selbst bleibt der Knopf für den 24-h-Verlauf, deshalb ein eigenes Element
 * daneben und nicht darin (kein Knopf im Knopf).
 */

export function KlappTitel({ zu, onUmschalten, children }: {
  zu: boolean
  onUmschalten: () => void
  children: ReactNode
}) {
  return (
    <button type="button" className="ls-fold-titel" aria-expanded={!zu} onClick={onUmschalten}>
      <span className="ls-fold-pfeil" aria-hidden="true">{zu ? '▸' : '▾'}</span>
      {children}
    </button>
  )
}

export function KachelKlappe({ zu, name, onUmschalten }: {
  zu: boolean
  name: string
  onUmschalten: () => void
}) {
  return (
    <button
      type="button"
      className="ls-fold"
      aria-expanded={!zu}
      aria-label={`${name} ${zu ? 'ausklappen' : 'einklappen'}`}
      onClick={onUmschalten}
    >
      {zu ? '▸' : '▾'}
    </button>
  )
}
