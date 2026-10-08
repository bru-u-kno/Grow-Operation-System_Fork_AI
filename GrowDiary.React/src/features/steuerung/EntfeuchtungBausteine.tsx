import type { ReactNode } from 'react'
import { V1Card, V1LinkButton } from '../../components/v1'
import { Klappkachel } from './Klappkachel'
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

/** Eine Regel = eine Kachel: erst die Erklärung, darunter dieselben Felder für jedes Gerät. */
export function RegelKachel({ titel, zusammenfassung, erklaerung, offen = true, children }: {
  titel: string
  zusammenfassung?: ReactNode
  erklaerung?: ReactNode
  offen?: boolean
  children: ReactNode
}) {
  return (
    <Klappkachel titel={titel} zusammenfassung={zusammenfassung} offen={offen}>
      <V1Card>
        {erklaerung && <p className="st-hinweis">{erklaerung}</p>}
        {children}
      </V1Card>
    </Klappkachel>
  )
}

/** Überschrift einer Gruppe von Kacheln, die nur ein Gerät betreffen. */
export function GruppenKopf({ children }: { children: ReactNode }) {
  return <div className="v1-eyebrow ef-gruppe">{children}</div>
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

