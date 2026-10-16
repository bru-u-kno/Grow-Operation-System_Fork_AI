import { useState } from 'react'
import { apiFetch, ApiRequestError } from '../../api'
import type {
  AcknowledgeRiskEventRequest,
  ResolveRiskEventRequest,
  RiskEventDto,
  RiskEventSopRecommendationDto,
  SopInstanceDto,
  StartRiskEventSopRequest,
} from '../../types'
import { V1Button, V1Card } from '../../components/v1'
import { formatDate, formatDateTime, formatNumber, formatSeverityLabel } from '../../utils'
import { dauerText, istWiederOkay, leseRohwert, pruefStand, seitText, vorText, wiederholungen, zielText } from './risiko-zeit'

type Props = {
  risk: RiskEventDto
  context?: string
  /** Alle Risiken des Bestands (auch geschlossene) — daraus folgt „16× aufgetreten". Ohne sie entfällt die Zeile. */
  history?: readonly RiskEventDto[]
  /** „Jetzt" in Millisekunden; die Seite reicht es durch, damit alle Karten dieselbe Uhr lesen. */
  jetzt?: number
  onChanged: (notice: string) => void
}

export function RiskActionCard({ risk, context, history, jetzt: jetztEin, onChanged }: Props) {
  const [busy, setBusy] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [recommendations, setRecommendations] = useState<RiskEventSopRecommendationDto[] | null>(null)

  async function run(key: string, fn: () => Promise<void>) {
    setBusy(key)
    setError(null)
    try {
      await fn()
    } catch (caught) {
      setError(caught instanceof ApiRequestError ? caught.message : 'Aktion fehlgeschlagen.')
    } finally {
      setBusy(null)
    }
  }

  const acknowledge = () =>
    run('ack', async () => {
      const body: AcknowledgeRiskEventRequest = {}
      await apiFetch<RiskEventDto>(`/api/risk-events/${risk.id}/acknowledge`, { method: 'POST', body: JSON.stringify(body) })
      onChanged(`„${risk.title}" bestätigt.`)
    })

  const resolve = () =>
    run('resolve', async () => {
      const body: ResolveRiskEventRequest = {}
      await apiFetch<RiskEventDto>(`/api/risk-events/${risk.id}/resolve`, { method: 'POST', body: JSON.stringify(body) })
      onChanged(`„${risk.title}" als erledigt markiert.`)
    })

  const loadRecommendations = () =>
    run('recommend', async () => {
      const recs = await apiFetch<RiskEventSopRecommendationDto[]>(`/api/risk-events/${risk.id}/sop-recommendations`)
      setRecommendations(recs)
    })

  const startSop = (sopId: string) =>
    run(`sop-${sopId}`, async () => {
      const body: StartRiskEventSopRequest = { sopId }
      await apiFetch<SopInstanceDto>(`/api/risk-events/${risk.id}/start-sop`, { method: 'POST', body: JSON.stringify(body) })
      onChanged('SOP gestartet.')
    })

  const isAcknowledged = risk.status === 'Acknowledged'
  const tone = risk.severity === 'Critical' ? 'critical' : 'warn'

  // Wann und wie aktuell: „Besteht noch" gilt nur für offene Karten; eine vom
  // Abgleich geschlossene Karte sagt „wieder okay" und hat nichts mehr zu tun.
  const [eigeneUhr] = useState(() => Date.now())
  const jetzt = jetztEin ?? eigeneUhr
  const wiederOkay = istWiederOkay(risk, jetzt)
  const wert = leseRohwert(risk.rawValue)
  const ziel = wert ? zielText(wert) : null
  const wiederholung = history ? wiederholungen(risk, history) : null
  const gesehen = risk.lastSeenAtUtc ?? risk.startedAtUtc
  // Wie frisch die Prüfung ist (nur offene Abweichungs-Karten mit Grow tragen die Angabe).
  const stand = wiederOkay ? null : pruefStand(risk, jetzt)
  const veraltet = stand?.veraltet === true

  return (
    <V1Card className={`rc-risk-action-card${wiederOkay ? ' is-wieder-okay' : ''}${veraltet ? ' is-veraltet' : ''}`} tone={wiederOkay ? undefined : tone}>
      <div className="rc-risk-action-head">
        <div>
          <strong>{risk.title}</strong>
          {context && <span>{context}</span>}
        </div>
        <em>{formatSeverityLabel(risk.severity)}</em>
      </div>
      <div className="rc-risk-state-row">
        <span className={`rc-risk-state ${wiederOkay ? 'is-off' : veraltet ? 'is-old' : 'is-on'}`} data-audit="risk-state">
          <i aria-hidden="true" />
          {wiederOkay ? 'Wieder okay' : veraltet ? 'Nicht mehr geprüft' : 'Besteht noch'}
        </span>
        {isAcknowledged && <span className="rc-risk-ack">bestätigt</span>}
      </div>
      {wert && (
        <p className="rc-risk-value" data-audit="risk-value">
          <strong>{formatNumber(wert.istwert, 2)}</strong>
          <span>{[wert.einheit, ziel].filter(Boolean).join(' · ')}</span>
        </p>
      )}
      <div className="rc-risk-when" data-audit="risk-when">
        {wiederOkay && risk.resolvedAtUtc ? (
          <>
            <p><b>Seit {dauerText(jetzt - new Date(risk.resolvedAtUtc).getTime())} im Bereich</b></p>
            <p>
              Aufgetreten: {formatDateTime(risk.startedAtUtc)} · Dauer{' '}
              {dauerText(new Date(risk.resolvedAtUtc).getTime() - new Date(risk.startedAtUtc).getTime())}
            </p>
          </>
        ) : (
          <>
            <p><b>{seitText(risk.startedAtUtc, jetzt)}</b> ({formatDateTime(risk.startedAtUtc)})</p>
            {!stand && <p>Zuletzt gesehen {vorText(gesehen, jetzt)}</p>}
          </>
        )}
        {wiederholung && (
          <p className="rc-risk-repeat">
            ↻ {wiederholung.anzahl}× seit {formatDate(wiederholung.seitIso)} aufgetreten
          </p>
        )}
      </div>
      {stand && (
        <div className={`rc-risk-stand${veraltet ? ' is-old' : ''}`} data-audit="risk-stand">
          <span className="rc-risk-stand-label">Stand der Prüfung</span>
          <p><b>{stand.zeile}</b></p>
          {stand.zusatz && <p>{stand.zusatz}</p>}
        </div>
      )}
      {risk.description && (
        <details className="rc-risk-details">
          <summary>Details &amp; Handlung</summary>
          <p className="rc-risk-action-desc">{risk.description}</p>
        </details>
      )}
      {!wiederOkay && (
        <div className="rc-risk-action-buttons">
          {!isAcknowledged && (
            <V1Button variant="secondary" disabled={busy !== null} onClick={() => void acknowledge()} audit="risk-acknowledge">
              {busy === 'ack' ? 'Speichert…' : 'Bestätigen'}
            </V1Button>
          )}
          <V1Button variant="primary" disabled={busy !== null} onClick={() => void resolve()} audit="risk-resolve">
            {busy === 'resolve' ? 'Speichert…' : 'Erledigt'}
          </V1Button>
          {/* Lose Pruefung mit Absicht: das Backend laesst null-Felder im JSON
              weg, ein Risiko ohne Grow kommt also als undefined an — und
              `!== null` haette den SOP-Knopf trotzdem gezeigt. */}
          {risk.growId != null && recommendations === null && (
            <V1Button variant="ghost" disabled={busy !== null} onClick={() => void loadRecommendations()} audit="risk-sop-recommend">
              {busy === 'recommend' ? 'Lädt…' : 'SOP vorschlagen'}
            </V1Button>
          )}
        </div>
      )}
      {recommendations !== null && (
        <div className="rc-risk-sop-list">
          {recommendations.length === 0 ? (
            <p className="rc-risk-action-desc">Keine SOP-Empfehlung für dieses Risiko.</p>
          ) : (
            recommendations.map((rec) => (
              <div key={rec.sopId} className="rc-risk-sop-row">
                <div>
                  <strong>{rec.sopName}</strong>
                  <span>{rec.reason} · {rec.confidence}</span>
                </div>
                <V1Button
                  variant="secondary"
                  disabled={busy !== null || rec.alreadyActive}
                  onClick={() => void startSop(rec.sopId)}
                  audit="risk-sop-start"
                >
                  {rec.alreadyActive ? 'Aktiv' : busy === `sop-${rec.sopId}` ? 'Startet…' : 'Starten'}
                </V1Button>
              </div>
            ))
          )}
        </div>
      )}
      {error && <p className="rc-risk-action-error">{error}</p>}
    </V1Card>
  )
}
