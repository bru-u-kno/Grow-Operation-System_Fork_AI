import { useEffect, useState } from 'react'
import { sortenText, zuechterPasst } from '../features/grows/sorten-text'
import { Link, useNavigate } from 'react-router-dom'
import { apiFetch, ApiRequestError } from '../api'
import type { GrowSummary } from '../types'
import { balkenText, buildPhaseTimeline, currentPhaseLabel, flipLabel } from '../features/grows/phase-timeline'
import { V1Alert, V1Page, V1Skeleton } from '../components/v1'
import '../features/grows/grows.css'

/**
 * Die Grow-Übersicht nach dem Entwurf: eine Karte je laufendem oder geplantem
 * Grow, dazu die gestrichelte Geisterkarte zum Anlegen. Beendete Grows stehen
 * nicht hier, sondern unter Ernte & Archiv — zwei Listen auf einer Seite haben
 * die Übersicht nur verwässert. Beenden/Löschen wohnt in der Verwaltung des
 * Grow-Details.
 */
function GrowsPage() {
  const navigate = useNavigate()
  const [grows, setGrows] = useState<GrowSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    async function load() {
      try {
        const active = await apiFetch<GrowSummary[]>('/api/grows?archived=false', { signal: controller.signal })
        if (!controller.signal.aborted) setGrows(sortGrows(active))
      } catch (caught) {
        if (!controller.signal.aborted) setError(caught instanceof ApiRequestError ? caught.message : 'Grows konnten nicht geladen werden.')
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }
    void load()
    return () => controller.abort()
  }, [])

  return (
    <V1Page
      eyebrow="Pflanzen / Alle"
      title="Grows"
      action={<Link className="ls-btn is-primary" to="/grows/new">+ Grow anlegen</Link>}
    >
      {error && <V1Alert message={error} tone="critical" />}

      {loading ? (
        <V1Skeleton tiles={3} label="Lade Grows" />
      ) : (
        <div className="co-grid" data-audit="grows-overview">
          {grows.map((grow) => <GrowCard key={grow.id} grow={grow} />)}
          <button type="button" className="co-ghost" data-audit="grows-empty-state" onClick={() => navigate('/grows/new')}>
            + Grow anlegen
            <small>Sorte, Zelt &amp; System wählen — 1 Seite</small>
          </button>
        </div>
      )}
    </V1Page>
  )
}

function GrowCard({ grow }: { grow: GrowSummary }) {
  const running = grow.status === 'Running'
  const timeline = buildPhaseTimeline(grow)
  return (
    <article className={`gc-card${running ? ' is-running' : ''}`}>
      <div className="gc-head">
        <strong>{grow.name}</strong>
        <span className={`ls-pill${running ? '' : ' is-plan'}`}>{statusLabel(grow.status)}</span>
        <span className="gc-day">{dayLabel(grow, timeline)}</span>
      </div>
      <div className="gc-body">
        {running && timeline.phases.length > 0 && (
          <>
            {/* Derselbe Strahl wie auf Live und im Grow-Detail — mit der
                Beschriftung IM Balken, nicht darunter. Hier stand vorher eine
                zweite, eigene Fassung: ein fest verdrahteter Keim-Balken plus
                ein erfundener Blüte-Rest. */}
            {/* Wischbar wie auf Live und im Detail — die dritte Stelle mit
                derselben Achse, beim Handy-Zuschnitt zuerst uebersehen. */}
            <div className="ls-timeline-wrap">
            <div className="ls-timeline is-compact">
              {timeline.phases.map((phase) => (
                <div
                  key={phase.label}
                  className={`ls-phase is-${phase.state}${phase.days === 0 ? ' is-unknown' : ''}`}
                  style={phase.days === 0 ? undefined : { flexGrow: phase.days }}
                  title={phase.label}
                >
                  {phase.progress != null && (
                    <i className="ls-phase-fill" style={{ width: `${Math.round(phase.progress * 100)}%` }} aria-hidden="true" />
                  )}
                  <span>{balkenText(phase.short, phase.days)}</span>
                </div>
              ))}
            </div>
            </div>
            <div className="gc-phasetext">
              {flipLabel(timeline.flipIsPlanned, timeline.daysToFlip, timeline.dates.flip)}
              {timeline.dates.harvest !== '—' && ` · Ernte ~${timeline.dates.harvest}`}
            </div>
          </>
        )}
        <div className="gc-facts">{factsLine(grow)}</div>
        <div className="co-actions" data-audit="grow-list-actions">
          <Link className="ls-btn is-small is-primary" to={`/grows/${grow.id}`}>Öffnen</Link>
          {running && <Link className="ls-btn is-small" to={`/addback?growId=${grow.id}`}>Addback</Link>}
          <Link className="ls-btn is-small" to={`/grows/${grow.id}/setup`}>Bearbeiten</Link>
        </div>
      </div>
    </article>
  )
}

/** „Fast Buds · 6 Pflanzen · RDWC Test Setup · AC Infinity" — nur, was belegt ist. */
function factsLine(grow: GrowSummary): string {
  return [
    /* Bei EINER Sorte steht hier der Zuechter — er sagt mehr als der Name,
       der schon in der Ueberschrift steht. Bei MEHREREN ist er eine
       Falschaussage: „Royal Queen Seeds" fuer ein Becken, in dem auch eine
       Gorilla Glue von GG Strains steht. Dann gewinnt die Mischung.

       Gefunden am laufenden Stand: die erste Fassung schrieb
       "grow.breeder ?? sortenText(grow)" — der Zuechter gewann IMMER, und die
       Sorte kam nie zum Zug. Die Aenderung zu pruefen haette das nicht
       gezeigt; die Karte anzusehen schon. */
    zuechterPasst(grow) ? grow.breeder ?? sortenText(grow) : sortenText(grow),
    grow.plantCount != null ? `${grow.plantCount} Pflanzen` : null,
    grow.hydroSetupName,
    grow.tentName,
  ].filter(Boolean).join(' · ') || 'Noch keine Angaben'
}

/**
 * „Veg Tag 26" bzw. „Blüte Tag 12" — geplante Grows zeigen ihr Startdatum.
 *
 * Kommt aus dem Zeitstrahl. Vorher rechnete diese Karte selbst: ab Startdatum
 * statt ab Bewurzelung (die Keimzeit wurde also mitgezählt) und mit „Veg" als
 * Namen für jede laufende Phase.
 */
function dayLabel(grow: GrowSummary, timeline: ReturnType<typeof buildPhaseTimeline>): string {
  if (grow.status === 'Running') {
    const laufend = currentPhaseLabel(timeline)
    if (laufend) return laufend
  }
  return grow.startDate ? `Start ${timeline.dates.start}` : ''
}

function statusLabel(status: GrowSummary['status']) {
  return status === 'Running' ? 'Läuft' : status === 'Planning' ? 'Geplant' : status === 'Completed' ? 'Beendet' : 'Abgebrochen'
}

function sortGrows(items: GrowSummary[]) {
  const rank = (status: GrowSummary['status']) => (status === 'Running' ? 0 : status === 'Planning' ? 1 : 2)
  return [...items].sort((a, b) => rank(a.status) - rank(b.status) || a.name.localeCompare(b.name))
}

export default GrowsPage
