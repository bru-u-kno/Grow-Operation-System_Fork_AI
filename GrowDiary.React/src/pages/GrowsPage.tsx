import { useEffect, useState } from 'react'
import { sortenText, zuechterPasst } from '../features/grows/sorten-text'
import { Link, useNavigate } from 'react-router-dom'
import { apiFetch, ApiRequestError } from '../api'
import type { GrowSummary } from '../types'
import { buildPhaseTimeline, currentPhaseLabel } from '../features/grows/phase-timeline'
import { PhasenUebersicht } from '../features/live/PhasenUebersicht'
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
        {/* Läuft die Phasen-Übersicht darunter, steht „Blüte Tag 61“ dort schon (Kopfzeile) — nicht doppelt. */}
        {!(running && timeline.phases.length > 0) && <span className="gc-day">{dayLabel(grow, timeline)}</span>}
      </div>
      <div className="gc-body">
        {running && timeline.phases.length > 0 && (
          <>
            {/* Kompakt (cc.267): Kopfzeile mit Phase, Woche und Ernte plus Balken; die Zeile je Phase steht auf der Grow-Seite. */}
            <PhasenUebersicht
              variante="kompakt"
              phases={timeline.phases}
              ernte={timeline.dates.harvest}
              flipIsPlanned={timeline.flipIsPlanned}
              daysToFlip={timeline.daysToFlip}
              flipDatum={timeline.dates.flip}
            />
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
