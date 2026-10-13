import type { ReactNode } from 'react'
import { V1LinkButton } from '../../components/v1'
import type { EntfeuchterHauptModell } from './useEntfeuchterHaupt'
import type { EntfeuchterZusatzModell } from './useEntfeuchterZusatz'
import './steuerung.css'

/**
 * Fork AI (A-015): Die gemeinsamen Teile der Reiter auf der Seite „Entfeuchtung".
 */

/** Was jeder Reiter braucht: beide Modelle (der Zusatz fehlt, wenn keiner da ist) und die Anzeigenamen. */
export type Ctx = {
  h: EntfeuchterHauptModell
  z: EntfeuchterZusatzModell | null
  haupt: string
  zusatz: string
}

/** Eine Zeile zum Lesen — kein Eingabefeld. */
export function Lesen({ label, hinweis, wert }: { label: string; hinweis?: ReactNode; wert: string }) {
  return (
    <div className="st-feldzeile">
      <span className="st-etikett">
        {label}
        {hinweis && <small>{hinweis}</small>}
      </span>
      <span className="st-nurlesen">{wert}</span>
    </div>
  )
}

/** Link auf die Geräte-Zuordnung eines Moduls, mit Zählung „x / y zugeordnet". */
export function GeraeteZeile({ zugeordnet, gesamt, pfad, label }: { zugeordnet: number; gesamt: number; pfad: string; label: string }) {
  return (
    <div className="st-geraete-zeile">
      <span>
        {label}{' '}
        <b className={zugeordnet < gesamt ? 'is-offen' : undefined}>{zugeordnet} / {gesamt}</b> zugeordnet
      </span>
      <V1LinkButton to={pfad} variant="ghost">Namen &amp; Rollen ›</V1LinkButton>
    </div>
  )
}

