import { useCallback, useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { apiFetch, ApiRequestError, formatApiError } from '../api'
import type { CreateTentRequest, GrowSummary, HydroSetupDto, TentDependencyError, TentDependencySummaryDto, TentDto, TentLivePayload, TentType, UpdateTentRequest, UpdateTentSensorRequest } from '../types'
import { V1Alert, V1Button, V1Card, V1Empty, V1Field, V1LinkButton, V1Page, V1Section, V1Switch } from '../components/v1'
import { toNullableInt, toNullableString } from '../components/v1-utils'
import { classNames } from '../utils'

const tentTypes: TentType[] = ['Production', 'Mother', 'Propagation', 'Quarantine', 'MultiPurpose']

type LiveMetricKey = 'temperature' | 'humidity' | 'vpd' | 'light-cycle' | 'ppfd'

type TentDraft = {
  name: string
  kind: string
  tentType: TentType
  displayOrder: string
  widthCm: string
  depthCm: string
  tentHeightCm: string
  lightType: string
  lightWatt: string
  exhaustFanCount: string
  exhaustM3h: string
  circulationFanCount: string
  co2Available: boolean
  hasCo2Enrichment: boolean
  leafTempOffsetC: string
  leafOffsetSyncService: string
  leafOffsetSyncPort: string
  notes: string
}

function TentsPage() {
  const navigate = useNavigate()
  const location = useLocation()
  const routeCreateMode = location.pathname.endsWith('/new')

  const [tents, setTents] = useState<TentDto[]>([])
  const [hydroSetups, setHydroSetups] = useState<HydroSetupDto[]>([])
  const [grows, setGrows] = useState<GrowSummary[]>([])
  const [liveByTentId, setLiveByTentId] = useState<Record<number, TentLivePayload>>({})
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [formOpen, setFormOpen] = useState(routeCreateMode)
  const [editingId, setEditingId] = useState<number | null>(null)
  const [draft, setDraft] = useState<TentDraft>(() => createDraft())
  const [saving, setSaving] = useState<string | null>(null)
  const [selectedTentId, setSelectedTentId] = useState<number | null>(null)
  const [blockedDeleteTentId, setBlockedDeleteTentId] = useState<number | null>(null)
  const [deleteDependenciesByTentId, setDeleteDependenciesByTentId] = useState<Record<number, TentDependencySummaryDto | null>>({})

  const loadTents = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const [tentData, setupData, growData] = await Promise.all([
        apiFetch<TentDto[]>('/api/settings/tents?includeArchived=true'),
        apiFetch<HydroSetupDto[]>('/api/hydro-setups?includeArchived=true'),
        apiFetch<GrowSummary[]>('/api/grows?archived=false').catch(() => []),
      ])
      const sortedTents = sortTents(tentData)
      const livePairs = await Promise.all(sortedTents.filter((tent) => tent.status === 'Active').map(async (tent) => {
        try { return [tent.id, await apiFetch<TentLivePayload>(`/api/live/tents/${tent.id}`)] as const }
        catch { return [tent.id, null] as const }
      }))
      setTents(sortedTents)
      setHydroSetups(setupData)
      setGrows(growData)
      setLiveByTentId(Object.fromEntries(livePairs.filter((pair): pair is readonly [number, TentLivePayload] => pair[1] !== null)))
      if (routeCreateMode) setDraft(createDraft(tentData.length + 1))
    } catch (caught) {
      setError(formatApiError(caught, 'Zelte konnten nicht geladen werden.'))
    } finally {
      setLoading(false)
    }
  }, [routeCreateMode])

  useEffect(() => {
    let active = true
    queueMicrotask(() => {
      if (active) void loadTents()
    })
    return () => { active = false }
  }, [loadTents])

  const activeTents = useMemo(() => tents.filter((tent) => tent.status === 'Active'), [tents])

  function openCreate() {
    setEditingId(null)
    setDraft(createDraft(tents.length + 1))
    setFormOpen(true)
  }

  function closeForm() {
    setFormOpen(false)
    setEditingId(null)
    if (routeCreateMode) navigate('/zelte')
  }

  function openEdit(tent: TentDto) {
    setEditingId(tent.id)
    setDraft(createDraftFromTent(tent))
    setFormOpen(true)
  }

  async function saveTent(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!draft.name.trim()) {
      setError('Bitte gib einen Zeltnamen ein.')
      return
    }

    setSaving('tent')
    setError(null)
    try {
      const request = draftToRequest(draft)
      if (editingId) {
        const existing = tents.find((tent) => tent.id === editingId)
        if (!existing) throw new Error('Zelt nicht gefunden.')
        const updated = await apiFetch<TentDto>(`/api/settings/tents/${editingId}`, {
          method: 'PUT',
          body: JSON.stringify({ ...request, status: existing.status, sensors: mapSensors(existing) } satisfies UpdateTentRequest),
        })
        setTents((current) => sortTents(current.map((tent) => (tent.id === updated.id ? updated : tent))))
      } else {
        const created = await apiFetch<TentDto>('/api/settings/tents', {
          method: 'POST',
          body: JSON.stringify({ ...request, status: 'Active', sensors: [] } satisfies CreateTentRequest),
        })
        setTents((current) => sortTents([...current, created]))
      }
      closeForm()
    } catch (caught) {
      setError(formatApiError(caught, 'Zelt konnte nicht gespeichert werden.'))
    } finally {
      setSaving(null)
    }
  }

  async function deleteTent(tent: TentDto) {
    if (saving) return
    const confirmed = window.confirm(`${tent.name} endgültig löschen?`)
    if (!confirmed) return
    setSaving(`delete-${tent.id}`)
    setError(null)
    try {
      await apiFetch<void>(`/api/settings/tents/${tent.id}`, { method: 'DELETE' })
      setTents((current) => current.filter((item) => item.id !== tent.id))
      setBlockedDeleteTentId((current) => current === tent.id ? null : current)
      setDeleteDependenciesByTentId((current) => ({ ...current, [tent.id]: null }))
      await loadTents()
    } catch (caught) {
      const payload = caught instanceof ApiRequestError ? caught.payload : null
      if (caught instanceof ApiRequestError && caught.status === 409 && isTentDependencyError(payload)) {
        setBlockedDeleteTentId(tent.id)
        setDeleteDependenciesByTentId((current) => ({ ...current, [tent.id]: payload.dependencies }))
        return
      }
      if (isNotFound(caught)) {
        setTents((current) => current.filter((item) => item.id !== tent.id))
        setBlockedDeleteTentId((current) => current === tent.id ? null : current)
        setDeleteDependenciesByTentId((current) => ({ ...current, [tent.id]: null }))
        await loadTents()
        return
      }
      setError(formatApiError(caught, 'Zelt konnte nicht gelöscht werden.'))
    } finally {
      setSaving(null)
    }
  }

  async function archiveTent(tent: TentDto) {
    if (saving) return
    const confirmed = window.confirm(`${tent.name} archivieren?`)
    if (!confirmed) return
    setSaving(`archive-${tent.id}`)
    setError(null)
    try {
      const saved = await apiFetch<TentDto>(`/api/settings/tents/${tent.id}/archive`, { method: 'POST' })
      setTents((current) => sortTents(current.map((item) => item.id === saved.id ? saved : item)))
      await loadTents()
    } catch (caught) {
      if (isNotFound(caught)) {
        setTents((current) => current.filter((item) => item.id !== tent.id))
        setBlockedDeleteTentId((current) => current === tent.id ? null : current)
        setDeleteDependenciesByTentId((current) => ({ ...current, [tent.id]: null }))
        await loadTents()
        return
      }
      setError(formatApiError(caught, 'Zelt konnte nicht archiviert werden.'))
    } finally {
      setSaving(null)
    }
  }

  async function archiveLinkedGrow(grow: Pick<GrowSummary, 'id' | 'name'>) {
    if (saving) return
    const confirmed = window.confirm(`${grow.name} beenden und archivieren?`)
    if (!confirmed) return
    setSaving(`grow-archive-${grow.id}`)
    setError(null)
    try {
      await apiFetch(`/api/grows/${grow.id}/archive`, { method: 'POST' })
      setBlockedDeleteTentId(null)
      await loadTents()
    } catch (caught) {
      if (isNotFound(caught)) {
        setBlockedDeleteTentId(null)
        await loadTents()
        return
      }
      setError(formatApiError(caught, 'Grow konnte nicht beendet werden.'))
    } finally {
      setSaving(null)
    }
  }

  if (formOpen) {
    return (
      <V1Page
        eyebrow="Physischer Raum"
        title={editingId ? 'Zelt bearbeiten' : 'Zelt anlegen'}
        subtitle="Raum, Größe und verbaute Technik. Home-Assistant-Entities werden separat gemappt."
        action={<V1Button onClick={closeForm}>Schließen</V1Button>}
        className="rc2-focused-form tents-page"
      >
        {error && <V1Alert message={error} tone="warn" />}
        <div className="rc2-focused-layout">
          <V1Section title="Basis">
            <form className="v1-form-grid rc2-tent-form" onSubmit={(event) => void saveTent(event)}>
              <V1Field label="Name" wide><input value={draft.name} onChange={(event) => setDraft((current) => ({ ...current, name: event.target.value }))} placeholder="Hauptzelt" /></V1Field>
              <V1Field label="Zweck"><select value={draft.tentType} onChange={(event) => setDraft((current) => ({ ...current, tentType: event.target.value as TentType }))}>{tentTypes.map((type) => <option key={type} value={type}>{formatTentType(type)}</option>)}</select></V1Field>
              <V1Field label="Typ"><input value={draft.kind} onChange={(event) => setDraft((current) => ({ ...current, kind: event.target.value }))} placeholder="Grow Tent" /></V1Field>
              <V1Field label="Reihenfolge"><input type="number" value={draft.displayOrder} onChange={(event) => setDraft((current) => ({ ...current, displayOrder: event.target.value }))} /></V1Field>

              <div className="v1-form-divider">Größe</div>
              <V1Field label="Breite cm"><input type="number" value={draft.widthCm} onChange={(event) => setDraft((current) => ({ ...current, widthCm: event.target.value }))} /></V1Field>
              <V1Field label="Tiefe cm"><input type="number" value={draft.depthCm} onChange={(event) => setDraft((current) => ({ ...current, depthCm: event.target.value }))} /></V1Field>
              <V1Field label="Höhe cm"><input type="number" value={draft.tentHeightCm} onChange={(event) => setDraft((current) => ({ ...current, tentHeightCm: event.target.value }))} /></V1Field>

              <div className="v1-form-divider">Technik im Raum</div>
              <V1Field label="Lichttyp"><input value={draft.lightType} onChange={(event) => setDraft((current) => ({ ...current, lightType: event.target.value }))} placeholder="LED" /></V1Field>
              <V1Field label="Watt"><input type="number" value={draft.lightWatt} onChange={(event) => setDraft((current) => ({ ...current, lightWatt: event.target.value }))} /></V1Field>
              <V1Field label="Abluft Anzahl"><input type="number" value={draft.exhaustFanCount} onChange={(event) => setDraft((current) => ({ ...current, exhaustFanCount: event.target.value }))} /></V1Field>
              <V1Field label="Abluft m³/h"><input type="number" value={draft.exhaustM3h} onChange={(event) => setDraft((current) => ({ ...current, exhaustM3h: event.target.value }))} /></V1Field>
              <V1Field label="Umluft Anzahl"><input type="number" value={draft.circulationFanCount} onChange={(event) => setDraft((current) => ({ ...current, circulationFanCount: event.target.value }))} /></V1Field>
              <V1Switch label="CO₂-Sensor vorhanden" checked={draft.co2Available} onChange={(checked) => setDraft((current) => ({ ...current, co2Available: checked }))} />
              <V1Switch label="CO₂-Anreicherung (Brenner/Flasche)" checked={draft.hasCo2Enrichment} onChange={(checked) => setDraft((current) => ({ ...current, hasCo2Enrichment: checked }))} hint="Nur mit Anreicherung bekommt die CO₂-Kachel ein Ziel — ein Sensor allein misst Umgebungsluft, und die ist bei ~400–500 ppm normal." />
              <V1Field label="Blatt kühler als Luft (°C)" hint={draft.leafOffsetSyncService.trim()
                ? 'Für die VPD-Berechnung — und wird beim Speichern an den Controller geschrieben. Nur ganze Grad: die AC-Infinity-App zeigt keine halben.'
                : 'Für die VPD-Berechnung. Blätter sind durch Verdunstung meist 1–3 °C kühler als die Luft; unter LED ohne Infrarot eher 2–3. 0 = mit Lufttemperatur rechnen.'}>
                <input type="number" step={draft.leafOffsetSyncService.trim() ? 1 : 0.5} min="0" max="10" value={draft.leafTempOffsetC} onChange={(event) => setDraft((current) => ({ ...current, leafTempOffsetC: event.target.value }))} />
              </V1Field>
              <V1Field label="Offset an Controller schreiben" hint="Home-Assistant-Dienst, der den Wert oben an den Klimacontroller weiterreicht, z. B. pyscript.acinfinity_offset_setzen. Leer lassen = nur rechnen, nichts schreiben.">
                <input value={draft.leafOffsetSyncService} onChange={(event) => setDraft((current) => ({ ...current, leafOffsetSyncService: event.target.value }))} placeholder="pyscript.acinfinity_offset_setzen" />
              </V1Field>
              {draft.leafOffsetSyncService.trim() ? (
                <V1Field label="Sensor-Port am Controller" hint="Beim AC Infinity AI+ sitzt das Offset pro Sensor. Die Zeltsonde hängt üblicherweise auf Port 2.">
                  <input type="number" step="1" min="0" max="16" value={draft.leafOffsetSyncPort} onChange={(event) => setDraft((current) => ({ ...current, leafOffsetSyncPort: event.target.value }))} />
                </V1Field>
              ) : null}
              <V1Field label="Notizen" wide><textarea rows={3} value={draft.notes} onChange={(event) => setDraft((current) => ({ ...current, notes: event.target.value }))} /></V1Field>
              <div className="v1-form-actions"><V1Button variant="ghost" onClick={closeForm}>Abbrechen</V1Button><V1Button type="submit" variant="primary" disabled={saving === 'tent'}>{saving === 'tent' ? 'Speichert...' : 'Speichern'}</V1Button></div>
            </form>
          </V1Section>

          <V1Section title="Home Assistant">
            <V1Card className="rc2-info-card">
              <span className="v1-card-kicker">Mapping getrennt</span>
              <h2>Sensoren nach dem Zelt anlegen mappen</h2>
              <p>Kamera, pH, EC, VPD, Licht- und Klima-Entities gehören in das HA-Mapping. Das Zelt bleibt ein physischer Raum.</p>
              <V1LinkButton to="/home-assistant">HA-Mapping öffnen</V1LinkButton>
            </V1Card>
          </V1Section>
        </div>
      </V1Page>
    )
  }

  const selectedTent = tents.find((tent) => tent.id === selectedTentId && tent.status === 'Active') ?? activeTents[0] ?? tents[0] ?? null

  return (
    <V1Page eyebrow="Einrichtung / Zelte" title="Zelte & Räume" action={<button type="button" className="ls-btn is-primary" onClick={openCreate}>+ Zelt anlegen</button>} className="tents-page">
      {error && <V1Alert message={error} tone="warn" />}

      {loading ? <V1Empty title="Lade Zelte..." /> : tents.length === 0 ? <V1Empty title="Noch kein Zelt" action={<V1Button variant="primary" onClick={openCreate}>Erstes Zelt anlegen</V1Button>} /> : (
        <div className="tn-layout" data-audit="tents-overview">
          <div className="tn-list">
            <div className="ls-label">Zelte</div>
            {tents.map((tent) => (
              <button
                key={tent.id}
                type="button"
                className={classNames('tn-item', selectedTent?.id === tent.id && 'active', tent.status === 'Archived' && 'is-archived')}
                onClick={() => setSelectedTentId(tent.id)}
              >
                <span className="tn-item-name">
                  <span className={classNames('co-dot', tent.status === 'Active' && 'is-on')} aria-hidden="true" />
                  <strong>{tent.name}</strong>
                </span>
                <span className="tn-item-meta">{tentMetaLine(tent, countHydroForTent(hydroSetups, tent.id))}</span>
              </button>
            ))}
            <button type="button" className="tn-item is-ghost" onClick={openCreate}>+ Weiteres Zelt</button>
          </div>

          {selectedTent && (
            <TentDetail
              tent={selectedTent}
              live={liveByTentId[selectedTent.id] ?? null}
              linkedGrows={getGrowsForTent(grows, selectedTent.id)}
              linkedHydro={getHydroForTent(hydroSetups, selectedTent.id)}
              deleteBlocked={blockedDeleteTentId === selectedTent.id}
              deleteDependencies={deleteDependenciesByTentId[selectedTent.id] ?? null}
              savingKey={saving}
              onEdit={openEdit}
              onArchive={archiveTent}
              onDelete={deleteTent}
              onArchiveGrow={archiveLinkedGrow}
            />
          )}
        </div>
      )}
    </V1Page>
  )
}

/** „120×120×200 · 1 Grow · 1 System" — die Kurzzeile der Zeltliste. */
function tentMetaLine(tent: TentDto, hydroCount: number): string {
  const parts: string[] = []
  if (tent.widthCm && tent.depthCm && tent.tentHeightCm) parts.push(`${tent.widthCm}×${tent.depthCm}×${tent.tentHeightCm}`)
  if (tent.tentType === 'Mother') parts.push('Mutterpflanzen')
  else if (tent.activeGrowCount > 0) parts.push(`${tent.activeGrowCount} Grow${tent.activeGrowCount > 1 ? 's' : ''}`)
  if (hydroCount > 0) parts.push(`${hydroCount} System${hydroCount > 1 ? 'e' : ''}`)
  if (tent.status === 'Archived') parts.push('Archiv')
  return parts.join(' · ') || formatTentType(tent.tentType)
}

function TentDetail({ tent, live, linkedGrows, linkedHydro, deleteBlocked, deleteDependencies, savingKey, onEdit, onArchive, onDelete, onArchiveGrow }: { tent: TentDto; live: TentLivePayload | null; linkedGrows: GrowSummary[]; linkedHydro: HydroSetupDto[]; deleteBlocked: boolean; deleteDependencies: TentDependencySummaryDto | null; savingKey: string | null; onEdit: (tent: TentDto) => void; onArchive: (tent: TentDto) => void; onDelete: (tent: TentDto) => void; onArchiveGrow: (grow: Pick<GrowSummary, 'id' | 'name'>) => void }) {
  const saving = savingKey === `delete-${tent.id}` || savingKey === `archive-${tent.id}`
  const light = live?.metrics.find((metric) => metric.key === 'light-cycle') ?? null
  const lightPill = light && (light.value === 'An' || light.value === 'Aus') ? `Licht ${light.value}` : null
  const runningGrow = linkedGrows.find((grow) => grow.status === 'Running') ?? linkedGrows[0] ?? null
  const hydro = linkedHydro[0] ?? null
  const panelDependencies = deleteDependencies ?? createClientDependencySummary(linkedGrows, linkedHydro)
  const showDependencyPanel = deleteBlocked && hasDependencies(panelDependencies)
  const mapped = tent.sensors.filter((sensor) => sensor.isActive && sensor.haEntityId)
  const missingCore = coreMetrics.filter((core) => !mapped.some((sensor) => sensor.metricType === core.metricType))
  const camera = (tent.cameras ?? []).find((entity) => entity.trim()) ?? tent.cameraEntityId

  return (
    <div className="tn-detail">
      <section className="ls-panel" data-audit="tent-detail-panel">
        <div className="tn-head">
          <strong>{tent.name}</strong>
          {lightPill && <span className="ls-pill">{lightPill}</span>}
          <span className="tn-head-meta">{headMeta(tent)}</span>
          <div className="co-actions" data-audit="tent-card-actions">
            {/* Ohne diesen Knopf war /zelte/:id von nirgendwo erreichbar — und
                damit auch die Lichtzeiten, die Zelt-Historie und die Verwaltung
                von Setups und Pflanzen, die nur dort wohnen. */}
            <V1LinkButton to={`/zelte/${tent.id}`} className="ls-btn is-small is-primary">Öffnen</V1LinkButton>
            <button type="button" className="ls-btn is-small" onClick={() => onEdit(tent)}>Bearbeiten</button>
            <V1LinkButton to="/hydro" className="ls-btn is-small">Hydro</V1LinkButton>
          </div>
        </div>
        <div className="tn-groups" data-audit="tent-metrics">
          <div className="tn-group">
            <div className="tn-group-label">Klima</div>
            <Row label="Temp" value={liveValue(live, 'temperature')} />
            <Row label="RLF" value={liveValue(live, 'humidity')} />
            <Row label="VPD" value={liveValue(live, 'vpd')} />
            <Row label="CO₂" value={tent.co2Available ? liveValue(live, 'co2' as LiveMetricKey) : 'nicht gemappt'} faint={!tent.co2Available} />
          </div>
          <div className="tn-group">
            <div className="tn-group-label">Licht</div>
            <Row label={light?.value === 'An' || light?.value === 'Aus' ? 'Status' : 'Zyklus'} value={light?.value ?? '–'} />
            <Row label="Leistung" value={tent.lightWatt ? `${tent.lightWatt} W` : '–'} />
            <Row label="PPFD" value={liveValue(live, 'ppfd')} faint={liveValue(live, 'ppfd') === '–'} />
          </div>
          <div className="tn-group">
            <div className="tn-group-label">Luft</div>
            <Row label="Abluft" value={tent.exhaustFanCount ? `${tent.exhaustFanCount} × ${tent.exhaustM3h ?? '?'} m³/h` : '–'} />
            <Row label="Umluft" value={tent.circulationFanCount ? `${tent.circulationFanCount} Ventilatoren` : '–'} />
            <Row label="Luftwechsel" value={airChanges(tent)} />
          </div>
          <div className="tn-group">
            <div className="tn-group-label">Belegung</div>
            <Row label="Grow" value={runningGrow?.name ?? '–'} />
            <Row label="System" value={hydro ? `${hydro.hydroStyle}${hydro.potCount ? ` · ${hydro.potCount} Sites` : ''}` : '–'} />
            <Row label="Fläche/Pflanze" value={areaPerPlant(tent, runningGrow)} />
          </div>
        </div>
      </section>

      <div className="co-grid is-300">
        <section className="ls-panel" data-audit="tent-camera-panel">
          <div className="ls-panel-head">
            <span className="ls-label">Kamera</span>
            {camera && <span className="ls-panel-meta">{camera}</span>}
          </div>
          {camera ? (
            <TentCam tentId={tent.id} entity={camera} name={tent.name} />
          ) : (
            <div className="ls-panel-body"><p>Keine Kamera gemappt — unter Home Assistant einrichten.</p></div>
          )}
        </section>

        <section className="ls-panel" data-audit="tent-sensors-panel">
          <div className="ls-panel-head">
            <span className="ls-label">Gemappte Sensoren · {mapped.length}</span>
          </div>
          {mapped.slice(0, 8).map((sensor) => (
            <div key={sensor.id} className="co-row">
              <span className="co-dot is-on" aria-hidden="true" />
              <span className="co-row-text">{sensor.displayLabel ?? sensor.metricType}</span>
              <span className="co-row-value is-faint" style={{ textTransform: 'none' }}>{sensor.haEntityId}</span>
            </div>
          ))}
          {missingCore.slice(0, 3).map((core) => (
            <div key={core.metricType} className="co-row">
              <span className="co-dot" aria-hidden="true" />
              <span className="co-row-text" style={{ color: 'var(--muted)' }}>{core.label} — nicht gemappt</span>
              <div className="co-row-end"><V1LinkButton to="/home-assistant" className="ls-btn is-small">Mappen</V1LinkButton></div>
            </div>
          ))}
          {mapped.length === 0 && missingCore.length === 0 && (
            <div className="ls-panel-body"><p>Keine Sensoren definiert.</p></div>
          )}
        </section>
      </div>

      <section className="ls-panel" data-audit="tent-management">
        <div className="ls-panel-head"><span className="ls-label">Verwaltung</span></div>
        <div className="co-row">
          <span className="co-row-sub">Archivieren behält die Historie; Löschen entfernt das Zelt endgültig.</span>
          <div className="co-row-end">
            <button type="button" className="ls-btn is-small" disabled={saving} onClick={() => void onArchive(tent)}>Archivieren</button>
            <button type="button" className="ls-btn is-small is-danger" data-audit="tent-delete-button" disabled={saving} onClick={() => void onDelete(tent)}>{saving ? 'Löscht…' : 'Löschen'}</button>
          </div>
        </div>
        {showDependencyPanel && (
          <div className={classNames('dependency-panel', deleteBlocked && 'active')} data-audit="tent-delete-blocked" style={{ margin: '0 14px 14px' }}>
            <strong>Löschen blockiert</strong>
            <p>Dieses Zelt ist mit aktiven Abhängigkeiten verknüpft. Verwalte sie direkt, danach ist Löschen erneut möglich.</p>
            <div className="v1-list">
              {panelDependencies.activeGrows.map((grow) => (
                <div key={`grow-${grow.id}`} className="v1-list-row dependency-row">
                  <div>
                    <strong>{grow.name}</strong>
                    <span>{grow.status ?? 'aktiv'}</span>
                  </div>
                  <div className="dependency-row-actions">
                    <V1LinkButton to={`/grows/${grow.id}`} variant="primary">Verwalten</V1LinkButton>
                    <V1LinkButton to={`/grows/${grow.id}/setup`}>Bearbeiten</V1LinkButton>
                    <V1Button disabled={savingKey === `grow-archive-${grow.id}`} onClick={() => void onArchiveGrow({ id: grow.id, name: grow.name })}>{savingKey === `grow-archive-${grow.id}` ? 'Beendet...' : 'Beenden'}</V1Button>
                  </div>
                </div>
              ))}
              {panelDependencies.hydroSetups.map((setup) => (
                <div key={`hydro-${setup.id}`} className="v1-list-row dependency-row">
                  <div>
                    <strong>{setup.name}</strong>
                    <span>{setup.status}</span>
                  </div>
                  <div className="dependency-row-actions">
                    <V1LinkButton to={`/hydro/${setup.id}`} variant="primary">Öffnen</V1LinkButton>
                  </div>
                </div>
              ))}
              {panelDependencies.sensors.map((sensor) => (
                <div key={`sensor-${sensor.id}`} className="v1-list-row dependency-row">
                  <div>
                    <strong>{sensor.name}</strong>
                    <span>{sensor.status ?? 'verknüpft'}</span>
                  </div>
                  <div className="dependency-row-actions">
                    <V1LinkButton to="/sensoren" variant="primary">Sensoren öffnen</V1LinkButton>
                  </div>
                </div>
              ))}
              {panelDependencies.other.map((item) => (
                <div key={`other-${item.type}-${item.id}`} className="v1-list-row dependency-row">
                  <div>
                    <strong>{item.name}</strong>
                    <span>{[item.type, item.status].filter(Boolean).join(' · ')}</span>
                  </div>
                  <div className="dependency-row-actions">
                    <V1LinkButton to="/hydro" variant="primary">Setups öffnen</V1LinkButton>
                  </div>
                </div>
              ))}
            </div>
          </div>
        )}
      </section>
    </div>
  )
}

/** Kamerabild mit ehrlichem Fallback — ein gerissenes Bild-Icon sagt nichts. */
function TentCam({ tentId, entity, name }: { tentId: number; entity: string; name: string }) {
  const [failed, setFailed] = useState(false)
  if (failed) return <div className="ls-panel-body"><p>Kein Bild — Home Assistant nicht erreichbar.</p></div>
  return (
    <img
      className="tn-cam"
      src={`/api/live/tents/${tentId}/camera?entity=${encodeURIComponent(entity)}&t=${tentId}`}
      alt={`Kamerabild ${name}`}
      loading="lazy"
      onError={() => setFailed(true)}
    />
  )
}

/** Kernwerte, deren Fehlen die Zelt-Ansicht anmahnt — wie im Entwurf CO₂. */
const coreMetrics: Array<{ metricType: TentDto['sensors'][number]['metricType']; label: string }> = [
  { metricType: 'AirTemperature', label: 'Lufttemperatur' },
  { metricType: 'Humidity', label: 'Luftfeuchte' },
  { metricType: 'ReservoirPh', label: 'pH Reservoir' },
]

function Row({ label, value, faint }: { label: string; value: string; faint?: boolean }) {
  return (
    <div className="tn-row">
      <span>{label}</span>
      <strong className={faint || value === '–' || value === 'nicht gemappt' ? 'is-faint' : undefined}>{value}</strong>
    </div>
  )
}

function headMeta(tent: TentDto): string {
  const parts: string[] = []
  if (tent.widthCm && tent.depthCm && tent.tentHeightCm) parts.push(`${tent.widthCm}×${tent.depthCm}×${tent.tentHeightCm} cm`)
  if (tent.lightWatt) parts.push(`${tent.lightWatt} W`)
  return parts.join(' · ')
}

/** ~ Luftwechsel pro Stunde: Abluftleistung geteilt durch Zeltvolumen. */
function airChanges(tent: TentDto): string {
  if (!tent.exhaustM3h || !tent.widthCm || !tent.depthCm || !tent.tentHeightCm) return '–'
  const volumeM3 = (tent.widthCm / 100) * (tent.depthCm / 100) * (tent.tentHeightCm / 100)
  if (volumeM3 <= 0) return '–'
  return `~ ${Math.round(tent.exhaustM3h / volumeM3)} ×/h`
}

function areaPerPlant(tent: TentDto, grow: GrowSummary | null): string {
  if (!tent.widthCm || !tent.depthCm || !grow?.plantCount) return '–'
  const perPlant = Math.round((tent.widthCm * tent.depthCm) / grow.plantCount)
  return `${new Intl.NumberFormat('de-DE').format(perPlant)} cm²`
}

function isTentDependencyError(payload: unknown): payload is TentDependencyError {
  return Boolean(payload && typeof payload === 'object' && 'dependencies' in payload)
}

function createClientDependencySummary(linkedGrows: GrowSummary[], linkedHydro: HydroSetupDto[]): TentDependencySummaryDto {
  return {
    activeGrows: linkedGrows.map((grow) => ({ id: grow.id, name: grow.name, status: grow.status, type: 'Grow' })),
    archivedGrows: [],
    hydroSetups: linkedHydro.map((setup) => ({ id: setup.id, name: setup.name, status: setup.status, type: 'Hydro' })),
    sensors: [],
    measurements: [],
    other: [],
  }
}

function hasDependencies(dependencies: TentDependencySummaryDto) {
  return dependencies.activeGrows.length > 0
    || dependencies.hydroSetups.length > 0
    || dependencies.sensors.length > 0
    || dependencies.other.length > 0
}

function sortTents(items: TentDto[]) { return [...items].sort((a, b) => a.status.localeCompare(b.status) || a.displayOrder - b.displayOrder || a.name.localeCompare(b.name)) }
function countHydroForTent(items: HydroSetupDto[], tentId: number) { return items.filter((setup) => setup.tentId === tentId && setup.status === 'Active').length }
function getHydroForTent(items: HydroSetupDto[], tentId: number) { return items.filter((setup) => setup.tentId === tentId && setup.status === 'Active') }
function getGrowsForTent(items: GrowSummary[], tentId: number) { return items.filter((grow) => grow.tentId === tentId && (grow.status === 'Running' || grow.status === 'Planning')) }
function mapSensors(tent: TentDto): UpdateTentSensorRequest[] { return tent.sensors.map((sensor) => ({ id: sensor.id, metricType: sensor.metricType, haEntityId: sensor.haEntityId, displayLabel: sensor.displayLabel, isActive: sensor.isActive })) }
function createDraft(displayOrder = 1): TentDraft { return { name: '', kind: 'Grow Tent', tentType: 'Production', notes: '', displayOrder: String(displayOrder), widthCm: '', depthCm: '', tentHeightCm: '', lightType: '', lightWatt: '', exhaustFanCount: '', exhaustM3h: '', circulationFanCount: '', co2Available: false, hasCo2Enrichment: false, leafTempOffsetC: '2', leafOffsetSyncService: '', leafOffsetSyncPort: '2' } }
function createDraftFromTent(tent: TentDto): TentDraft { return { name: tent.name, kind: tent.kind, tentType: tent.tentType, notes: tent.notes ?? '', displayOrder: String(tent.displayOrder), widthCm: String(tent.widthCm ?? ''), depthCm: String(tent.depthCm ?? ''), tentHeightCm: String(tent.tentHeightCm ?? ''), lightType: tent.lightType ?? '', lightWatt: String(tent.lightWatt ?? ''), exhaustFanCount: String(tent.exhaustFanCount ?? ''), exhaustM3h: String(tent.exhaustM3h ?? ''), circulationFanCount: String(tent.circulationFanCount ?? ''), co2Available: tent.co2Available, hasCo2Enrichment: tent.hasCo2Enrichment, leafTempOffsetC: String(tent.leafTempOffsetC ?? 0), leafOffsetSyncService: tent.leafOffsetSyncService ?? '', leafOffsetSyncPort: String(tent.leafOffsetSyncPort ?? 2) } }
// The leaf offset is a decimal (e.g. 2.5 °C), so it must not go through the int helper.
function parseOffset(value: string): number {
  const parsed = Number.parseFloat(value.replace(',', '.'))
  return Number.isFinite(parsed) ? Math.min(10, Math.max(0, parsed)) : 0
}

function draftToRequest(draft: TentDraft) { return { name: draft.name.trim(), kind: draft.kind.trim() || 'Grow Tent', tentType: draft.tentType, notes: toNullableString(draft.notes), displayOrder: toNullableInt(draft.displayOrder) ?? 0, accentColor: '#22c55e', widthCm: toNullableInt(draft.widthCm), depthCm: toNullableInt(draft.depthCm), tentHeightCm: toNullableInt(draft.tentHeightCm), lightType: toNullableString(draft.lightType), lightWatt: toNullableInt(draft.lightWatt), lightController: null, lightControllerEntityId: null, exhaustFanCount: toNullableInt(draft.exhaustFanCount), exhaustM3h: toNullableInt(draft.exhaustM3h), circulationFanCount: toNullableInt(draft.circulationFanCount), hvacController: null, hvacControllerEntityId: null, co2Available: draft.co2Available, hasCo2Enrichment: draft.hasCo2Enrichment, cameraEntityId: null, leafTempOffsetC: parseOffset(draft.leafTempOffsetC), leafOffsetSyncService: draft.leafOffsetSyncService.trim(), leafOffsetSyncPort: toNullableInt(draft.leafOffsetSyncPort) ?? 2 } }
function formatTentType(value: TentType) { return value === 'Production' ? 'Blüte / Run' : value === 'Mother' ? 'Mutter' : value === 'Propagation' ? 'Anzucht' : value === 'Quarantine' ? 'Quarantäne' : 'Mehrzweck' }
function liveValue(live: TentLivePayload | null, key: LiveMetricKey) {
  const metric = live?.metrics.find((item) => item.key === key)
  return metric ? `${metric.value}${metric.unit && metric.value !== '–' ? ` ${metric.unit}` : ''}` : '–'
}
function isNotFound(caught: unknown) { return caught instanceof ApiRequestError && caught.status === 404 }

export default TentsPage
