import { useCallback, useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { apiFetch, ApiRequestError, formatApiError } from '../api'
import { resolveUrl } from '../base'
import type { GrowSummary, HydroSetupDto, HydroSetupLayoutType, ReservoirPosition } from '../types'
import { SystemPlan } from '../features/hydro/SystemPlan'
import { rowsFromLayoutType } from '../features/hydro/system-plan-model'
import { V1Alert, V1Badge, V1Button, V1Empty, V1LinkButton, V1Page, V1Section, V1Stat } from '../components/v1'
import { formatLiters } from '../components/v1-utils'
import { classNames, formatNumber } from '../utils'

function HydroPage() {
  const navigate = useNavigate()
  const [setups, setSetups] = useState<HydroSetupDto[]>([])
  const [grows, setGrows] = useState<GrowSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState<string | null>(null)
  const [selectedSetupId, setSelectedSetupId] = useState<number | null>(null)
  const [blockedDeleteSetupId, setBlockedDeleteSetupId] = useState<number | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const setupData = await apiFetch<HydroSetupDto[]>('/api/hydro-setups?includeArchived=true')
      const growData = await apiFetch<GrowSummary[]>('/api/grows?archived=false').catch(() => [])
      setGrows(growData)
      const sorted = sortSetups(setupData)
      setSetups(sorted)
      setSelectedSetupId((current) => sorted.some((setup) => setup.id === current) ? current : sorted.find((setup) => setup.status === 'Active')?.id ?? sorted[0]?.id ?? null)
    } catch (caught) {
      setError(formatApiError(caught, 'Hydro-Daten konnten nicht geladen werden.'))
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    let active = true
    queueMicrotask(() => {
      if (active) void load()
    })
    return () => { active = false }
  }, [load])

  const activeSetups = useMemo(() => setups.filter((setup) => setup.status === 'Active'), [setups])
  const selectedSetup = useMemo(() => setups.find((setup) => setup.id === selectedSetupId) ?? activeSetups[0] ?? setups[0] ?? null, [activeSetups, selectedSetupId, setups])

  // Anlegen und Bearbeiten sind eigene Seiten geworden.
  function openCreate() {
    navigate('/hydro/new')
  }

  function openEdit(setup: HydroSetupDto) {
    navigate(`/hydro/${setup.id}/edit`)
  }

  async function deleteSetup(setup: HydroSetupDto) {
    if (saving) return
    const linkedGrows = getGrowsForSetup(grows, setup)
    // Verknüpfte Grows blockieren das Löschen. Die Begründung steht im Panel neben
    // den betroffenen Grows, nicht in einer Fehlerzeile am Seitenkopf — dort stand
    // sie früher, und der zugehörige Aufruf war hinter einem übrig gebliebenen
    // Block unerreichbar geworden.
    if (linkedGrows.length > 0) {
      setError(null)
      setBlockedDeleteSetupId(setup.id)
      return
    }
    const confirmed = window.confirm(`${setup.name} endgültig löschen?`)
    if (!confirmed) return
    setSaving(`delete-${setup.id}`)
    setError(null)
    try {
      const response = await fetch(resolveUrl(`/api/hydro-setups/${setup.id}`), { method: 'DELETE' })
      if (response.status === 204) {
        setSetups((current) => current.filter((item) => item.id !== setup.id))
        setSelectedSetupId((current) => current === setup.id ? null : current)
        setBlockedDeleteSetupId((current) => current === setup.id ? null : current)
        await load()
        return
      }
      if (response.status === 404) {
        setSetups((current) => current.filter((item) => item.id !== setup.id))
        setSelectedSetupId((current) => current === setup.id ? null : current)
        setBlockedDeleteSetupId((current) => current === setup.id ? null : current)
        await load()
        return
      }
      if (response.status === 409) {
        setBlockedDeleteSetupId(setup.id)
        await load()
        return
      }
      if (!response.ok) throw new Error(`Hydro-Setup konnte nicht gelöscht werden (${response.status})`)
      const saved = await response.json() as HydroSetupDto
      setSetups((current) => sortSetups(current.map((item) => item.id === saved.id ? saved : item)))
      setSelectedSetupId(saved.id)
      await load()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Hydro-Setup konnte nicht gelöscht werden.')
    } finally {
      setSaving(null)
    }
  }

  async function archiveSetup(setup: HydroSetupDto) {
    if (saving) return
    const confirmed = window.confirm(`${setup.name} archivieren?`)
    if (!confirmed) return
    setSaving(`archive-${setup.id}`)
    setError(null)
    try {
      const saved = await apiFetch<HydroSetupDto>(`/api/hydro-setups/${setup.id}/archive`, { method: 'POST' })
      setSetups((current) => sortSetups(current.map((item) => item.id === saved.id ? saved : item)))
      setSelectedSetupId(saved.id)
      await load()
    } catch (caught) {
      if (isNotFound(caught)) {
        setSetups((current) => current.filter((item) => item.id !== setup.id))
        setSelectedSetupId((current) => current === setup.id ? null : current)
        setBlockedDeleteSetupId((current) => current === setup.id ? null : current)
        await load()
        return
      }
      setError(formatApiError(caught, 'Hydro-Setup konnte nicht archiviert werden.'))
    } finally {
      setSaving(null)
    }
  }

  async function archiveLinkedGrow(grow: GrowSummary) {
    if (saving) return
    const confirmed = window.confirm(`${grow.name} beenden und archivieren?`)
    if (!confirmed) return
    setSaving(`grow-archive-${grow.id}`)
    setError(null)
    try {
      await apiFetch(`/api/grows/${grow.id}/archive`, { method: 'POST' })
      setBlockedDeleteSetupId(null)
      await load()
    } catch (caught) {
      if (isNotFound(caught)) {
        setBlockedDeleteSetupId(null)
        await load()
        return
      }
      setError(formatApiError(caught, 'Grow konnte nicht beendet werden.'))
    } finally {
      setSaving(null)
    }
  }

  // Der fuenfschrittige Wizard ist einer Seite gewichen: /hydro/new und
  // /hydro/:id/edit rendern HydroEditorPage.

  return (
    <V1Page eyebrow="Einrichtung / Hydro" title="Hydro-Systeme" subtitle="Deine DWC- und RDWC-Systeme: Behälter, Volumen und was daran hängt." action={<V1Button variant="primary" onClick={openCreate}>Hydro-Setup anlegen</V1Button>} className="hydro-page">
      {error && <V1Alert message={error} tone="warn" />}
      <section className="v1-kpi-grid">
        <V1Stat label="Aktive Setups" value={activeSetups.length} />
        <V1Stat label="Sites" value={activeSetups.reduce((sum, setup) => sum + (setup.potCount ?? 0), 0)} />
        <V1Stat label="Gesamtvolumen" value={formatNumber(activeSetups.reduce((sum, setup) => sum + (setup.totalVolumeLiters ?? 0), 0), 0)} unit="L" />
        <V1Stat label="Ohne Zelt" value={activeSetups.filter((setup) => setup.tentId == null).length} />
      </section>

      {loading ? <V1Empty title="Lade Hydro-Setups..." /> : setups.length === 0 ? <V1Empty title="Noch kein Hydro-Setup" action={<V1Button variant="primary" onClick={openCreate}>Erstes Setup anlegen</V1Button>} /> : (
        <section className="v1-hydro-layout">
          <V1Section title="Setups" className="v1-hydro-list-section">
            <div className="v1-hydro-list">
              {setups.map((setup) => <button key={setup.id} type="button" className={classNames('v1-hydro-list-item', selectedSetup?.id === setup.id && 'active')} onClick={() => setSelectedSetupId(setup.id)}><strong>{setup.name}</strong><span>{setup.hydroStyle} · {setup.tentName ?? 'ohne Zelt'} · {formatLiters(setup.totalVolumeLiters)}</span></button>)}
            </div>
          </V1Section>
          {selectedSetup && <HydroDetail setup={selectedSetup} linkedGrows={getGrowsForSetup(grows, selectedSetup)} deleteBlocked={blockedDeleteSetupId === selectedSetup.id} saving={saving === `delete-${selectedSetup.id}` || saving === `archive-${selectedSetup.id}`} savingKey={saving} onEdit={openEdit} onDelete={deleteSetup} onArchive={archiveSetup} onArchiveGrow={archiveLinkedGrow} />}
        </section>
      )}
    </V1Page>
  )
}

function HydroDetail({ setup, linkedGrows, deleteBlocked, saving, savingKey, onEdit, onArchive, onDelete, onArchiveGrow }: { setup: HydroSetupDto; linkedGrows: GrowSummary[]; deleteBlocked: boolean; saving: boolean; savingKey: string | null; onEdit: (setup: HydroSetupDto) => void; onArchive: (setup: HydroSetupDto) => void; onDelete: (setup: HydroSetupDto) => void; onArchiveGrow: (grow: GrowSummary) => void }) {
  const facts = [
    ['Zelt', setup.tentName ?? '–'],
    ['Sites', String(setup.potCount ?? '–')],
    ['Topf', formatLiters(setup.potSizeLiters)],
    ['Tank', formatLiters(setup.reservoirLiters)],
    ['Gesamt', formatLiters(setup.totalVolumeLiters)],
    ['Layout', formatLayout(setup.layoutType)],
    ['Tankposition', formatReservoirPosition(setup.reservoirPosition)],
    ['Luftsteine', String(setup.airStoneCount ?? '–')],
  ]

  return (
    <V1Section title={setup.name} action={<V1Badge tone={setup.status === 'Active' ? 'ok' : 'neutral'}>{setup.status === 'Active' ? 'aktiv' : 'Archiv'}</V1Badge>} className="v1-hydro-detail-section">
      {/* Kopf über die volle Breite, Plan und Werte darunter nebeneinander. Vorher
          stand der Name in einer .95fr-Spalte und brach bei längeren Setup-Namen
          auf drei Zeilen um. Er steht jetzt nur noch in der Section-Überschrift —
          zweimal derselbe Name war ohnehin einer zu viel. */}
      <div className="hydro-detail">
        <div className="hydro-detail__head">
          <span className="v1-card-kicker">{setup.hydroStyle}</span>
          <V1Stat label="Volumen" value={formatNumber(setup.totalVolumeLiters, 0)} unit="L" />
          <div className="actions">
            <V1LinkButton to={`/hydro/${setup.id}`} variant="primary">Öffnen</V1LinkButton>
            <V1Button onClick={() => onEdit(setup)}>Bearbeiten</V1Button>
            <V1Button disabled={saving} onClick={() => void onArchive(setup)}>Archivieren</V1Button>
            <V1Button variant="danger" disabled={saving} onClick={() => void onDelete(setup)}>{saving ? 'Löscht...' : 'Löschen'}</V1Button>
          </div>
        </div>

        <div className="hydro-detail__body">
          <div className="hydro-detail__plan">
            <SystemPlan
              compact
              hydroStyle={setup.hydroStyle === 'DWC' ? 'DWC' : 'RDWC'}
              siteCount={setup.potCount ?? 1}
              rows={rowsFromLayoutType(setup.layoutType, setup.potCount ?? 1)}
              potLiters={setup.potSizeLiters ?? 0}
              tankLiters={setup.reservoirLiters ?? 0}
              reservoirPosition={setup.reservoirPosition}
              tentWidthCm={null}
              tentDepthCm={null}
            />
          </div>
          <div className="hydro-detail__facts">
            {facts.map(([label, value]) => <Fact key={label} label={label} value={value} />)}
          </div>
        </div>

        <div className="hydro-detail__tech">
          <div className="v1-chip-row">
            {setup.hasCirculationPump && <span>Umwälzpumpe</span>}
            {setup.hasAirPump && <span>Luftpumpe{setup.airPumpLitersPerHour ? ` · ${formatNumber(setup.airPumpLitersPerHour, 0)} L/h` : ''}</span>}
            {setup.hasChiller && <span>Chiller</span>}
            {setup.hasUvSterilizer && <span>UV-C</span>}
            {!setup.hasCirculationPump && !setup.hasAirPump && !setup.hasChiller && !setup.hasUvSterilizer && <span>Technik offen</span>}
          </div>
          {/* Die Einschätzung ist gerechnet, nicht gemessen — und sagt das dazu.
              Wer ein DO-Messgerät hat, glaubt dem Messwert, nicht dieser Zeile. */}
          {setup.aeration && (
            <V1Alert
              tone={setup.aeration.stufe === 'gut' ? 'ok' : setup.aeration.stufe === 'zu_wenig' ? 'warn' : 'neutral'}
              title={`Belüftung: ${setup.aeration.stufe === 'gut' ? 'im grünen Bereich' : setup.aeration.stufe === 'knapp' ? 'knapp' : setup.aeration.stufe === 'sehr_hoch' ? 'sehr hoch' : 'zu wenig'}`}
              message={`${setup.aeration.satz} (berechnet, Faustregel)`}
            />
          )}
        </div>

        {/* Hier stand eine zweite Liste der verknüpften Grows, die rc2-overrides per
            `display: none` wieder ausgeblendet hat. Markup, das nur existiert, um
            versteckt zu werden, ist einfacher zu löschen als zu pflegen. */}
        {deleteBlocked && linkedGrows.length > 0 && (
          <div className={classNames('dependency-panel', 'active')} data-audit="hydro-delete-blocked">
            <strong>Löschen blockiert</strong>
            <p>Dieses Hydro-Setup ist mit aktiven oder geplanten Grows verknüpft. Beende oder verwalte die betroffenen Grows, danach ist Löschen erneut möglich.</p>
            <div className="v1-list">
              {linkedGrows.map((grow) => (
                <div key={grow.id} className="v1-list-row dependency-row">
                  <div>
                    <strong>{grow.name}</strong>
                    <span>{grow.status ?? 'aktiv'}</span>
                  </div>
                  <div className="dependency-row-actions">
                    <V1LinkButton to={`/grows/${grow.id}`} variant="primary">Verwalten</V1LinkButton>
                    <V1LinkButton to={`/grows/${grow.id}/setup`}>Bearbeiten</V1LinkButton>
                    <V1Button disabled={savingKey === `grow-archive-${grow.id}`} onClick={() => void onArchiveGrow(grow)}>{savingKey === `grow-archive-${grow.id}` ? 'Beendet...' : 'Beenden'}</V1Button>
                  </div>
                </div>
              ))}
            </div>
          </div>
        )}
      </div>
    </V1Section>
  )
}

function Fact({ label, value }: { label: string; value: string }) { return <div className="v1-fact"><span>{label}</span><strong>{value}</strong></div> }
function sortSetups(items: HydroSetupDto[]) { return [...items].sort((a, b) => a.status.localeCompare(b.status) || a.displayOrder - b.displayOrder || a.name.localeCompare(b.name)) }
function formatLayout(value: HydroSetupLayoutType) { return value === 'SingleBucket' ? 'Einzeleimer' : value === 'Row' ? 'Reihe' : value === 'Grid2x2' ? '2×2' : value === 'Grid2x3' ? '2×3' : value === 'Grid2x4' ? '2×4' : 'Flexibel' }
function getGrowsForSetup(grows: GrowSummary[], setup: HydroSetupDto) {
  const activeGrows = grows.filter((grow) => grow.status === 'Running' || grow.status === 'Planning')
  const direct = activeGrows.filter((grow) => grow.systemId === setup.id || grow.setupId === setup.id)
  if (direct.length > 0 || !setup.activeGrowCount) return direct
  return activeGrows.filter((grow) => grow.tentId === setup.tentId)
}
function formatReservoirPosition(value: ReservoirPosition) { return value === 'None' ? 'keiner' : value === 'Left' ? 'links' : value === 'Right' ? 'rechts' : value === 'Top' ? 'oben' : value === 'Bottom' ? 'unten' : 'extern' }
function isNotFound(caught: unknown) { return caught instanceof ApiRequestError && caught.status === 404 }

export default HydroPage
