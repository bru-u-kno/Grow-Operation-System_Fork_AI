import { useEffect, useMemo, useState } from 'react'
/* `istGemischt` dazu: bei zwei Sorten von zwei Zuechtern stand hier die
   richtige Sorte neben dem Zuechter der ANDEREN — „Gorilla Glue · Royal Queen
   Seeds". Ein Zuechter je Lauf ist eine Angabe, die es bei einer Mischung
   nicht gibt. Gefunden vom Pruefer. */
import { sortenText, zuechterPasst } from '../features/grows/sorten-text'
import { Link, useParams } from 'react-router-dom'
import { apiFetch, ApiRequestError } from '../api'
import { aufstellungName, bereichsartName, bereichsstatusName, mutterzustandName, phaseName, pflanzenRolleName, pflanzenStatusName, quarantaeneErgebnisName, statusName, zeltZweckName } from '../deutsche-woerter'
import { formatDate } from '../utils'
import type { GrowSummary, HydroSetupDto, MetricPayload, PlantInstanceDto, SetupDto, TentDto, TentLivePayload } from '../types'
import { V1Alert, V1Badge, V1Button, V1Card, V1Empty, V1LinkButton, V1Page, V1Section, V1Stat } from '../components/v1'
import { PlantActions } from '../features/plants/PlantActions'
import { LightScheduleSection } from '../features/tents/LightScheduleSection'
import { TentHistorySection } from '../features/tents/TentHistorySection'
import { resolveUrl } from '../base'
import { mapMetrics, buildScore } from '../features/live/live-model'
import { MetricTile } from '../features/live/MetricTile'
import { decimalsForMetric } from '../features/live/metric-tile-model'
import '../features/live/metric-tile.css'
// Enthaelt die v1-live-/v1-camera-Regeln dieser Seite. Der Import hing bis eben
// an DesktopLiveDashboard — einer Datei, die niemand mehr laedt; Vite haette
// beide aus dem Bundle geworfen und diese Seite still entstylt.
import '../features/live/live-instrument.css'

const tentMetricDefinitions = [
  ['temperature', 'Temp', '°C'],
  ['humidity', 'RLF', '%'],
  ['vpd', 'VPD', 'kPa'],
  ['light-cycle', 'Licht', null],
  ['ppfd', 'PPFD', 'µmol/m²/s'],
  ['co2', 'CO₂', 'ppm'],
] as const

const hydroMetricDefinitions = [
  ['reservoir-ph', 'pH', null],
  ['reservoir-ec', 'EC', 'mS/cm'],
  ['reservoir-temp', 'Wasser', '°C'],
  ['reservoir-level', 'Volumen', 'L'],
  ['reservoir-level-cm', 'Pegel', 'cm'],
  ['orp', 'ORP', 'mV'],
  ['dissolved-oxygen', 'DO', 'mg/L'],
] as const

function TentDetailPage() {
  const { tentId } = useParams()
  const [tent, setTent] = useState<TentDto | null>(null)
  const [live, setLive] = useState<TentLivePayload | null>(null)
  const [grows, setGrows] = useState<GrowSummary[]>([])
  const [setups, setSetups] = useState<SetupDto[]>([])
  const [hydroSetups, setHydroSetups] = useState<HydroSetupDto[]>([])
  const [plantsBySetupId, setPlantsBySetupId] = useState<Record<number, PlantInstanceDto[]>>({})
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [refresh, setRefresh] = useState(0)
  const [notice, setNotice] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()

    async function load() {
      if (!tentId) return
      setLoading(true)
      setError(null)

      try {
        const tentIdNumber = Number(tentId)
        const [tents, livePayload, activeGrows, setupList, hydroSetupList] = await Promise.all([
          apiFetch<TentDto[]>('/api/settings/tents?includeArchived=true', { signal: controller.signal }),
          apiFetch<TentLivePayload>(`/api/live/tents/${tentId}`, { signal: controller.signal }).catch(() => null),
          apiFetch<GrowSummary[]>('/api/grows?archived=false', { signal: controller.signal }),
          apiFetch<SetupDto[]>('/api/setups', { signal: controller.signal }),
          apiFetch<HydroSetupDto[]>(`/api/hydro-setups?tentId=${tentIdNumber}&includeArchived=true`, { signal: controller.signal }).catch(() => []),
        ])

        if (controller.signal.aborted) return

        const selectedTent = tents.find((item) => item.id === tentIdNumber) ?? null
        const activeSetups = setupList.filter((setup) => setup.tentId === tentIdNumber && isActiveSetup(setup))
        const plantEntries = await fetchPlantsForSetups(activeSetups, controller.signal)

        if (controller.signal.aborted) return

        setTent(selectedTent)
        setLive(livePayload)
        setGrows(activeGrows.filter((grow) => grow.tentId === tentIdNumber))
        setSetups(activeSetups)
        setHydroSetups(hydroSetupList)
        setPlantsBySetupId(Object.fromEntries(plantEntries))
      } catch (caught) {
        if (controller.signal.aborted) return
        setError(caught instanceof ApiRequestError ? caught.message : 'Zelt-Details konnten nicht geladen werden.')
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }

    void load()
    return () => controller.abort()
  }, [tentId, refresh])

  const activeHydroSetups = useMemo(() => hydroSetups.filter((setup) => setup.status === 'Active'), [hydroSetups])
  const quarantineSetups = useMemo(() => setups.filter((setup) => setup.setupType === 'Quarantine'), [setups])
  const productionSetups = useMemo(() => setups.filter((setup) => setup.setupType === 'Production'), [setups])
  const score = buildScore(live?.metrics ?? [], tent)

  if (loading) return <V1Page eyebrow="Zelt" title="Lade Zelt..."><V1Empty title="Live-Daten werden geladen..." /></V1Page>
  if (!tent) return <V1Page eyebrow="Zelt" title="Nicht gefunden" action={<V1LinkButton to="/zelte">Zurück</V1LinkButton>}><V1Empty title="Zelt nicht gefunden." /></V1Page>

  return (
    <V1Page
      eyebrow={formatTentType(tent.tentType)}
      title={tent.name}
      className="tent-detail-v2"
      action={<div className="v1-action-row"><V1LinkButton to="/zelte" variant="ghost">Zelte</V1LinkButton><V1LinkButton to="/home-assistant">HA</V1LinkButton><V1LinkButton to="/hydro">Hydro</V1LinkButton></div>}
    >
      {error && <V1Alert title="Fehler" message={error} tone="warn" />}
      {notice && <V1Alert title="Erledigt" message={notice} tone="ok" />}

      <section className="v1-live-hero-grid">
        <V1Card tone={score.tone} className="v1-live-now-card">
          <div className="v1-card-title-row"><div><span className="v1-card-kicker">Live</span><h2>{score.label}</h2></div><V1Badge tone={score.tone}>{score.value}%</V1Badge></div>
          <div className="v1-info-grid compact">
            {mapMetrics(live?.metrics ?? [], tentMetricDefinitions.slice(0, 5)).map((metric) => <Info key={metric.key} label={metric.label} value={formatMetricValue(metric)} />)}
          </div>
          <div className="v1-action-row">{grows[0] ? <V1LinkButton to={`/grows/${grows[0].id}/addback`} variant="primary">Addback</V1LinkButton> : <V1LinkButton to="/grows/new" variant="primary">Grow starten</V1LinkButton>}<V1LinkButton to="/messung">Messung</V1LinkButton></div>
        </V1Card>

        <V1Card className="v1-live-now-card">
          <div className="v1-card-title-row"><div><span className="v1-card-kicker">Raum</span><h2>{formatSize(tent)}</h2></div><V1Badge tone={tent.status === 'Active' ? 'ok' : 'neutral'}>{tent.status === 'Active' ? 'aktiv' : 'Archiv'}</V1Badge></div>
          <div className="v1-info-grid compact tent-detail-room-grid tent-detail-fact-list">
            <Info label="Grows" value={String(grows.length)} />
            <Info label="Hydro" value={String(activeHydroSetups.length)} />
            <Info label="Setups" value={String(setups.length)} />
            <Info label="Sensoren" value={String(tent.sensors.filter((sensor) => sensor.isActive).length)} />
            <Info label="Licht" value={tent.lightWatt ? `${tent.lightWatt} W` : tent.lightType ?? 'offen'} />
            <Info label="Klima" value={`${tent.exhaustFanCount ?? 0} Abluft · ${tent.circulationFanCount ?? 0} Umluft`} />
          </div>
        </V1Card>

        {live?.cameraUrl ? (
          <div className="v1-camera-card rc2-camera-card"><img src={resolveUrl(live.cameraUrl)} alt={`Livebild ${tent.name}`} className="ready" /><div className="v1-camera-label"><strong>{tent.name}</strong><span>Kamera</span></div></div>
        ) : (
          <V1Card className="v1-camera-empty is-compact"><span className="v1-card-kicker">Kamera</span><h2>Nicht eingerichtet</h2><p>{tent.cameraEntityId ?? 'Kamera-Entity fehlt im HA-Mapping.'}</p><V1LinkButton to="/home-assistant">HA-Mapping</V1LinkButton></V1Card>
        )}
      </section>

      <div className="v1-live-metrics-pair">
        <V1Section title="Zeltwerte"><div className="v1-metric-grid compact">{mapMetrics(live?.metrics ?? [], tentMetricDefinitions).map((metric) => <MetricCard key={metric.key} metric={metric} />)}</div></V1Section>
        {/* Die Reservoirwerte haben als einzige einen hinterlegten Zielbereich —
            hier zeigt die Kachel ihn als Band mit Marker, statt die Zahl allein
            zu lassen. */}
        <V1Section title="Reservoir">
          <div className="gos-metric-row">
            {mapMetrics(live?.metrics ?? [], hydroMetricDefinitions).map((metric) => (
              <MetricTile
                key={metric.key}
                label={metric.label}
                value={metric.numericValue}
                unit={metric.unit}
                targetMin={metric.targetMin}
                targetMax={metric.targetMax}
                decimals={decimalsForMetric(metric.key)}
              />
            ))}
          </div>
        </V1Section>
      </div>

      <TentHistorySection tentId={tent.id} />

      <LightScheduleSection tentId={tent.id} />

      <V1Section title="Hydro-Systeme" action={<V1LinkButton to="/hydro/new">Hydro anlegen</V1LinkButton>}>
        {activeHydroSetups.length === 0 ? <V1Empty title="Kein aktives Hydro-Setup" text="DWC/RDWC-Systeme werden separat angelegt und dann dem Zelt zugeordnet." /> : <div className="v1-card-grid v1-card-grid-compact">{activeHydroSetups.map((setup) => <HydroSetupCard key={setup.id} setup={setup} />)}</div>}
      </V1Section>

      <V1Section title="Aktive Grows" action={<V1LinkButton to="/grows/new">Grow starten</V1LinkButton>}>
        {grows.length === 0 ? <V1Empty title="Kein Grow in diesem Zelt" /> : <div className="v1-list">{grows.map((grow) => <Link key={grow.id} to={`/grows/${grow.id}`} className="v1-list-row"><strong>{grow.name}</strong><span>{sortenText(grow) ?? 'Sorte offen'}{zuechterPasst(grow) && grow.breeder ? ` · ${grow.breeder}` : ''}</span><em>{grow.currentStage ? phaseName(grow.currentStage) : statusName(grow.status)}</em></Link>)}</div>}
      </V1Section>

      <V1Section title="Setups & Pflanzen">
        {setups.length === 0 ? <V1Empty title="Keine aktiven Plant-Setups" /> : <div className="v1-card-grid">{setups.map((setup) => <SetupCard key={setup.id} setup={setup} plants={plantsBySetupId[setup.id] ?? []} quarantineSetups={quarantineSetups} productionSetups={productionSetups} grows={grows} onChanged={(message) => { setNotice(message); setRefresh((value) => value + 1) }} />)}</div>}
      </V1Section>
    </V1Page>
  )
}

function HydroSetupCard({ setup }: { setup: HydroSetupDto }) {
  return <V1Card><div className="v1-card-title-row"><div><span className="v1-card-kicker">{setup.hydroStyle}</span><h2>{setup.name}</h2></div><V1Badge tone="accent">{aufstellungName(setup.layoutType)}</V1Badge></div><div className="v1-info-grid compact"><Info label="Sites" value={String(setup.potCount ?? '–')} /><Info label="Topf" value={formatLiters(setup.potSizeLiters)} /><Info label="Tank" value={formatLiters(setup.reservoirLiters)} /><Info label="Gesamt" value={formatLiters(setup.totalVolumeLiters)} /><Info label="Chiller" value={setup.hasChiller ? 'ja' : 'nein'} /><Info label="Luft" value={setup.hasAirPump ? `${setup.airStoneCount ?? '–'} Steine` : 'offen'} /></div></V1Card>
}

function SetupCard({ setup, plants, quarantineSetups, productionSetups, grows, onChanged }: { setup: SetupDto; plants: PlantInstanceDto[]; quarantineSetups: SetupDto[]; productionSetups: SetupDto[]; grows: GrowSummary[]; onChanged: (notice: string) => void }) {
  /**
   * Einen Bereich entfernen.
   *
   * Der Server lehnt ab, solange Pflanzen, Geräte oder Grows daran hängen —
   * und sagt auch welche. Bis zum 25.08.2026 gab es diesen Weg nur in der API.
   */
  async function entfernen() {
    if (!window.confirm(`Bereich „${setup.name}" wirklich entfernen?`)) return
    try {
      await apiFetch(`/api/setups/${setup.id}`, { method: 'DELETE' })
      onChanged(`„${setup.name}" entfernt.`)
    } catch (caught) {
      onChanged(caught instanceof Error ? caught.message : 'Bereich konnte nicht entfernt werden.')
    }
  }

  return <V1Card><div className="v1-card-title-row"><div><span className="v1-card-kicker">{bereichsartName(setup.setupType)}</span><h2>{setup.name}</h2></div><V1Badge tone={setup.status === 'Active' ? 'ok' : 'neutral'}>{bereichsstatusName(setup.status)}</V1Badge><V1Button variant="ghost" onClick={() => void entfernen()}>Entfernen</V1Button></div><div className="v1-info-grid compact">{formatSetupDetails(setup).map((detail) => <Info key={detail.label} label={detail.label} value={detail.value} />)}</div>{plants.length === 0 ? <V1Empty title="Keine Pflanzen in diesem Setup" /> : <div className="plant-list">{plants.map((plant) => <div key={plant.id} className="plant-entry"><div className="plant-row"><strong>{plant.label}</strong><span>{formatPlantLine(plant)}</span><em>{pflanzenStatusName(plant.plantStatus)}</em></div><PlantActions plant={plant} setup={setup} quarantineSetups={quarantineSetups} productionSetups={productionSetups} grows={grows} onChanged={onChanged} /></div>)}</div>}</V1Card>
}

function MetricCard({ metric }: { metric: MetricPayload }) { return <V1Stat label={metric.label} value={metric.value} unit={metric.unit} hint={metric.hint ?? undefined} tone={metricTone(metric)} /> }
function Info({ label, value }: { label: string; value: string }) { return <div className="v1-info"><span>{label}</span><strong>{value}</strong></div> }
function formatMetricValue(metric: MetricPayload) { return metric.unit && metric.value !== '–' ? `${metric.value} ${metric.unit}` : metric.value }
function metricTone(metric: MetricPayload) { return metric.tone === 'danger' ? 'critical' : metric.tone === 'warning' ? 'warn' : metric.tone === 'success' ? 'ok' : 'neutral' }
function formatSetupDetails(setup: SetupDto): Array<{ label: string; value: string }> { const base = [{ label: 'Status', value: bereichsstatusName(setup.status) }]; if (setup.setupType === 'Mother') return [...base, { label: 'Stecklinge', value: setup.cloneCounterTotal != null ? String(setup.cloneCounterTotal) : '–' }, { label: 'Schnitt', value: setup.lastCloneCutAt ? formatDate(setup.lastCloneCutAt) : '–' }, { label: 'Zustand', value: setup.motherHealthStatus ? mutterzustandName(setup.motherHealthStatus) : '–' }]; if (setup.setupType === 'Quarantine') return [...base, { label: 'Start', value: setup.quarantineStartedAt ? formatDate(setup.quarantineStartedAt) : '–' }, { label: 'Ende', value: setup.quarantinePlannedEndAt ? formatDate(setup.quarantinePlannedEndAt) : '–' }, { label: 'Ergebnis', value: setup.quarantineResult ? quarantaeneErgebnisName(setup.quarantineResult) : '–' }]; return [...base, { label: 'Notiz', value: setup.notes ?? '–' }] }
function formatPlantLine(plant: PlantInstanceDto): string { const strain = plant.strainName ?? (plant.strainId ? `Sorte #${plant.strainId}` : 'ohne Sorte'); const pheno = plant.phenoLabel ? ` · ${plant.phenoLabel}` : ''; return `${pflanzenRolleName(plant.plantRole)} · ${strain}${pheno}` }
function isActiveSetup(setup: SetupDto): boolean { return setup.status === 'Planning' || setup.status === 'Active' }
async function fetchPlantsForSetups(setups: SetupDto[], signal?: AbortSignal): Promise<Array<readonly [number, PlantInstanceDto[]]>> { return Promise.all(setups.map(async (setup) => { const plants = await apiFetch<PlantInstanceDto[]>(`/api/plants?setupId=${setup.id}`, { signal }); return [setup.id, plants] as const })) }
// Die Tabelle steht in deutsche-woerter.ts — sie stand vorher hier UND in
// live-model.ts, und an einer dritten Stelle gar nicht.
function formatTentType(value: string) { return zeltZweckName(value) }
function formatSize(tent: TentDto) { return !tent.widthCm && !tent.depthCm && !tent.tentHeightCm ? 'Größe offen' : `${tent.widthCm ?? '–'}×${tent.depthCm ?? '–'}×${tent.tentHeightCm ?? '–'} cm` }
function formatLiters(value: number | null | undefined) { return value == null ? '–' : `${value.toLocaleString('de-DE', { maximumFractionDigits: 1 })} L` }

export default TentDetailPage

