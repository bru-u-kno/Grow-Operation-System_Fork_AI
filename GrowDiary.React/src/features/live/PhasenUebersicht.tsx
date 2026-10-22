import { classNames } from '../../utils'
import { flipLabel, type Phase } from '../grows/phase-timeline'
import { phasenKopf, phasenZeile } from './phasen-uebersicht'

/**
 * Die Phasen auf einen Blick (cc.266, Wunsch des Nutzers): Kopfzeile mit laufender Phase und Ernte, darunter ein
 * Balken im Verhältnis der Dauern und eine Zeile je Phase mit Tagen und Wochen, alles ausgeschrieben.
 *
 * Kein Wischen mehr: die frühere Zeitachse trug die Namen IM Balken und musste am Telefon gewischt werden.
 * Hier steht der Name in der Liste, der Balken zeigt nur die Verhältnisse und den Fortschritt.
 */
export function PhasenUebersicht({ phases, ernte, flipIsPlanned, daysToFlip, flipDatum }: {
  phases: Phase[]
  /** Kurzes Erntedatum („31.10.“) oder „—“. */
  ernte: string
  flipIsPlanned: boolean
  daysToFlip: number | null
  flipDatum: string
}) {
  const kopf = phasenKopf(phases)
  const zeilen = phases.map(phasenZeile)
  const erntetext = ernte === '—' ? 'Ernte offen' : `Ernte ~${ernte}${kopf?.ueberPlan ? ' · überfällig' : ''}`
  // Der Flip-Hinweis nur, solange er aussteht (oder überfällig ist); danach steht „Geflippt“ in der Phasenliste ohnehin da.
  const flip = flipIsPlanned ? flipLabel(flipIsPlanned, daysToFlip, flipDatum) : null
  return (
    <div className="ls-ph" data-audit="live-phasen">
      <div className="ls-ph-kopf">
        {kopf && <strong className={classNames(kopf.ueberPlan && 'is-ueber')}>{kopf.text}</strong>}
        <span className={classNames('ls-ph-ernte', kopf?.ueberPlan && 'is-ueber')}>{erntetext}</span>
      </div>
      <div className="ls-timeline ls-ph-balken" role="img" aria-label={phases.map((phase) => phase.label).join(', ')}>
        {phases.map((phase) => (
          <div
            key={phase.name}
            className={classNames('ls-phase', `is-${phase.state}`, phase.days === 0 && 'is-unknown')}
            style={phase.days === 0 ? undefined : { flexGrow: phase.days }}
          >
            {phase.progress != null && <i className="ls-phase-fill" style={{ width: `${Math.round(phase.progress * 100)}%` }} aria-hidden="true" />}
          </div>
        ))}
      </div>
      <ul className="ls-ph-liste">
        {zeilen.map((zeile) => (
          <li key={zeile.name} className={classNames(`is-${zeile.state}`, zeile.ueberPlan && 'is-ueber')}>
            <span className="ls-ph-name">{zeile.name}</span>
            {/* Die laufende Phase in zwei Zeilen (Tag / Woche), die übrigen in einer — so bricht nie ein Teil auseinander. */}
            <span className={classNames('ls-ph-text', zeile.state === 'current' && 'is-zweizeilig')}>
              {zeile.state === 'current' ? zeile.teile.map((teil) => <span key={teil}>{teil}</span>) : zeile.text}
            </span>
          </li>
        ))}
      </ul>
      {flip && <p className={classNames('ls-ph-flip', daysToFlip != null && daysToFlip < 0 && 'is-ueber')}>{flip}</p>}
    </div>
  )
}
