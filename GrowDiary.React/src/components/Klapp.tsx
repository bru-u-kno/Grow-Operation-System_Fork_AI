import { useId, useState } from 'react'
import type { ReactNode } from 'react'
import { classNames } from '../utils'
import './klapp.css'

/**
 * Cultivate Control (cc.268): Das Ein- und Ausklappen der Live-Seite als Baustein für alle übrigen Seiten.
 *
 * <b>Das Muster.</b> Kleiner Pfeilknopf links vor dem Titel (▾ offen, ▸ zu), der Titel in
 * gesperrten Großbuchstaben, zugeklappt eine Kurzauskunft in Normalschrift dahinter — wie `KlappTitel` und
 * `.ls-bilanz` auf der Live-Seite. Dort wohnen sie im Live-CSS, das nur mit der Live-Seite geladen wird; auf
 * den anderen Seiten fehlen die Regeln. Deshalb hat dieser Baustein sein eigenes, immer mitgeladenes CSS.
 */

/** Nur das Zeichen im Rahmen — für Zeilen, die ihr eigenes Kopf-Element behalten (Gerätezeile). */
export function KlappPfeil({ zu }: { zu: boolean }) {
  return <span className="kk-pfeil" aria-hidden="true">{zu ? '▸' : '▾'}</span>
}

export type KlappTon = 'warn' | 'ok'

/**
 * Der Knopf selbst: Pfeil, Titel, bei `zu` die Kurzauskunft. `titelNormal` lässt den Titel in Normalschrift
 * stehen — für Einträge, deren Name ein Symptom oder ein Gerätename ist und kein Abschnittstitel.
 */
export function KlappKopf({ zu, onUmschalten, titel, kurz, ton, titelNormal, className, controls, audit }: {
  zu: boolean
  onUmschalten: () => void
  titel: ReactNode
  /** Was der zugeklappte Bereich verschweigt; offen steht sie nicht da. */
  kurz?: ReactNode
  ton?: KlappTon
  titelNormal?: boolean
  className?: string
  controls?: string
  audit?: string
}) {
  return (
    <button
      type="button"
      className={classNames('kk-kopf', className)}
      aria-expanded={!zu}
      aria-controls={controls}
      data-audit={audit}
      onClick={onUmschalten}
    >
      <KlappPfeil zu={zu} />
      <span className="kk-text">
        <span className={classNames('kk-titel', titelNormal && 'is-normal')}>{titel}</span>
        {zu && kurz != null && kurz !== '' && <em className={classNames('kk-kurz', ton && `is-${ton}`)}>{kurz}</em>}
      </span>
    </button>
  )
}

/**
 * Ein Bereich zum Aufklappen an Stelle von `<details>`: derselbe Kopf, der Inhalt bleibt mit `hidden` im
 * Baum (Eingabefelder darin behalten ihren Wert, Suche und Tests finden ihn). Beim ersten Anzeigen zu.
 */
export function KlappBereich({ titel, kurz, ton, offen = false, className, audit, children }: {
  titel: ReactNode
  kurz?: ReactNode
  ton?: KlappTon
  offen?: boolean
  className?: string
  audit?: string
  children: ReactNode
}) {
  const [auf, setAuf] = useState(offen)
  const koerper = useId()
  return (
    <div className={classNames('kk-bereich', className)} data-audit={audit}>
      <KlappKopf zu={!auf} onUmschalten={() => setAuf(!auf)} titel={titel} kurz={kurz} ton={ton} controls={koerper} />
      <div id={koerper} className="kk-inhalt" hidden={!auf}>{children}</div>
    </div>
  )
}
