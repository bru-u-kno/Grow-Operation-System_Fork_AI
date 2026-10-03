import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch } from '../../api'
import { wegZurRoutine } from '../changeouts/routine-weg'
import { KlappTitel } from './Einklappen'
import { classNames } from '../../utils'

type TrendFinding = {
  code: string
  severity: 'Info' | 'Warning' | 'Critical'
  headline: string
  detail: string
  guidanceId: string | null
}

/**
 * What the guard noticed over days rather than right now.
 *
 * Deliberately its own panel next to the risks: a risk says a value is wrong, this says a
 * value is *heading* somewhere — often while still perfectly inside its band. Mixing the
 * two would bury the slow ones under the loud ones.
 */
export function TrendWatchPanel({ growId, zu = false, onUmschalten }: {
  growId: number | null
  /** Fork AI: eingeklappt bleibt nur der Kopf mit „N prüfen" stehen. */
  zu?: boolean
  onUmschalten?: () => void
}) {
  const [findings, setFindings] = useState<TrendFinding[]>([])

  useEffect(() => {
    const controller = new AbortController()
    async function load() {
      if (growId == null) {
        setFindings([])
        return
      }
      try {
        const data = await apiFetch<TrendFinding[]>(`/api/trends/${growId}`, { signal: controller.signal })
        if (!controller.signal.aborted) setFindings(data)
      } catch {
        if (!controller.signal.aborted) setFindings([])
      }
    }
    void load()
    return () => controller.abort()
  }, [growId])

  const actionable = findings.filter((finding) => finding.severity !== 'Info').length

  return (
    <article className={classNames('ls-panel', zu && 'is-zu')} data-audit="live-trend-card">
      <div className="ls-panel-head">
        <KlappTitel zu={zu} onUmschalten={onUmschalten}><span className="ls-label">Beobachtungen · über Tage</span></KlappTitel>
        {actionable > 0
          ? <span className="ls-panel-meta ls-trend-count">{actionable} prüfen</span>
          : zu && <span className="ls-panel-meta">nichts Auffälliges</span>}
      </div>
      {zu ? null : findings.length === 0 ? (
        <div className="ls-panel-body"><p>Nichts Auffälliges — keine Drift, kein Verbrauchssprung.</p></div>
      ) : (
        <ul className="ls-trends">
          {findings.slice(0, 5).map((finding) => {
            // Trägt die Beobachtung einen Ablauf, der eine eigene Seite hat,
            // führt ein Weg dorthin. „Wasserwechsel seit 9 Tagen offen" ohne
            // Weg zum Eintragen ist eine Sackgasse — genau das war die
            // Beschwerde vom 31.08.2026.
            const weg = finding.guidanceId ? wegZurRoutine(finding.guidanceId) : null
            return (
              <li key={finding.code} className={finding.severity === 'Info' ? '' : 'is-warn'}>
                <strong>{finding.headline}</strong>
                <span>{finding.detail}</span>
                {weg && <Link className="ls-btn is-small ls-trend-weg" to={weg.to}>{weg.aktion}</Link>}
              </li>
            )
          })}
        </ul>
      )}
    </article>
  )
}
