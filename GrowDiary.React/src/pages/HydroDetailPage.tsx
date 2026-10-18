import { useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { apiFetch } from '../api'
import type { GrowSummary, HydroSetupDto } from '../types'
import { V1Alert, V1Badge, V1Card, V1Empty, V1LinkButton, V1Page, V1Section } from '../components/v1'
import { LevelCalibrationPanel } from '../features/hydro/LevelCalibrationPanel'
import { formatNumber } from '../utils'
import { aufstellungName, phaseName, statusName, tankplatzName } from '../deutsche-woerter'

function HydroDetailPage() {
  const { setupId } = useParams()
  const [setup, setSetup] = useState<HydroSetupDto | null>(null)
  const [grows, setGrows] = useState<GrowSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  // Nach einer Kalibrierung neu laden — sonst stuenden die alten Werte da.
  const [refresh, setRefresh] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    async function load() {
      if (!setupId) return
      setLoading(true)
      setError(null)
      try {
        const [loadedSetup, loadedGrows] = await Promise.all([
          apiFetch<HydroSetupDto>(`/api/hydro-setups/${setupId}`, { signal: controller.signal }),
          apiFetch<GrowSummary[]>('/api/grows?archived=false', { signal: controller.signal }).catch(() => []),
        ])
        if (controller.signal.aborted) return
        setSetup(loadedSetup)
        setGrows(loadedGrows)
      } catch (caught) {
        if (!controller.signal.aborted) setError(caught instanceof Error ? caught.message : 'Hydro-Setup konnte nicht geladen werden.')
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }
    void load()
    return () => controller.abort()
  }, [setupId, refresh])

  // `systemId` ist die Verknuepfung zum Hydro-System; `setupId` ist etwas
  // anderes und beim laufenden Grow leer. Diese Seite fragte NUR nach
  // `setupId` und meldete deshalb bei jedem System dauerhaft "Keine aktiven
  // Grows verknuepft" — auch wenn ein Grow daran haengt. HydroPage und
  // useHydroSetups fragen beide Felder ab; nur hier fehlte das erste.
  const linkedGrows = useMemo(
    () => (setup ? grows.filter((grow) => grow.systemId === setup.id || grow.setupId === setup.id) : []),
    [grows, setup])

  if (loading) return <V1Page eyebrow="Hydro" title="Lade Setup..."><V1Empty title="Hydro-Setup wird geladen." /></V1Page>
  if (!setup) return <V1Page eyebrow="Hydro" title="Nicht gefunden" action={<V1LinkButton to="/hydro">Zurück</V1LinkButton>}><V1Alert message={error ?? 'Hydro-Setup nicht gefunden.'} tone="warn" /></V1Page>

  return (
    <V1Page eyebrow={setup.hydroStyle} title={setup.name} action={<div className="v1-action-row"><V1LinkButton to="/hydro" variant="ghost">Hydro</V1LinkButton><V1LinkButton to="/hydro/new">Neu</V1LinkButton></div>}>
      {error && <V1Alert message={error} tone="warn" />}
      <section className="v1-kpi-grid">
        <V1Card><span className="v1-card-kicker">Zelt</span><h2>{setup.tentName ?? 'offen'}</h2><p>{setup.status === 'Active' ? 'aktiv' : 'archiviert'}</p></V1Card>
        {/* Gemessen schlaegt gerechnet. Die Summe aus Topf- und Tankgroesse ist
            eine Nennweite; sie unterstellt randvolle Toepfe und vergisst
            Schlaeuche. Wer einmal mit der Wasseruhr gefuellt hat, hat die
            bessere Zahl — und soll nicht raten muessen, welche der beiden gilt. */}
        <V1Card>
          <span className="v1-card-kicker">Volumen</span>
          <h2>{formatNumber(setup.levelSensorFullLiters ?? setup.totalVolumeLiters, 0)}{'\u00a0'}L</h2>
          <p>{setup.levelSensorFullLiters == null
            ? 'Gesamt'
            : weichtAb(setup.levelSensorFullLiters, setup.totalVolumeLiters)
              ? `Gemessen · gerechnet ${formatNumber(setup.totalVolumeLiters, 0)}\u00a0L`
              : 'Gemessen'}</p>
        </V1Card>
        <V1Card><span className="v1-card-kicker">Sites</span><h2>{setup.potCount ?? '–'}</h2><p>{aufstellungName(setup.layoutType)}</p></V1Card>
        <V1Card tone={setup.status === 'Active' ? 'ok' : 'neutral'}><span className="v1-card-kicker">Status</span><h2>{setup.status === 'Active' ? 'aktiv' : 'Archiv'}</h2><p>Tank {tankplatzName(setup.reservoirPosition)}</p></V1Card>
      </section>

      <V1Section title="Details">
        {/* Keine Karte und kein zweiter Koerper um das Raster: `.v1-info-grid`
            bringt Rahmen und Ecken selbst mit, V1Section den Innenabstand. Mit
            der Karte waren es drei Rahmen ineinander, und die Werte fingen erst
            47 px vom Bildschirmrand an. */}
          <div className="v1-info-grid compact">
            <Info label="Topf" value={formatLiters(setup.potSizeLiters)} />
            <Info label="Tank" value={formatLiters(setup.reservoirLiters)} />
            <Info label="Umwälzung" value={setup.hasCirculationPump ? 'ja' : 'nein'} />
            <Info label="Luft" value={setup.hasAirPump ? `${setup.airStoneCount ?? '–'} Steine` : 'nein'} />
            <Info label="Chiller" value={setup.hasChiller ? 'ja' : 'nein'} />
            <Info label="UV-C" value={setup.hasUvSterilizer ? 'ja' : 'nein'} />
          </div>
          {setup.notes && <p>{setup.notes}</p>}
      </V1Section>

      <V1Section title="Volumen-Kalibrierung">
        {setup.levelCalibratedAtUtc ? (
          <V1Card>
            <div className="v1-info-grid compact">
              <Info label="Leer bei" value={formatRaw(setup.levelSensorEmptyRaw)} />
              <Info label="Voll bei" value={formatRaw(setup.levelSensorFullRaw)} />
              <Info label="Das sind" value={formatLiters(setup.levelSensorFullLiters)} />
              <Info label="Kalibriert" value={new Date(setup.levelCalibratedAtUtc).toLocaleDateString('de-DE')} />
            </div>
            <p className="lk-intro" style={{ marginTop: 10 }}>
              Der Wasserstand steht damit überall in Litern — Kachel, Verlauf, Grenzwerte, und die
              Dosierung rechnet mit dem echten Volumen: halb leeres Becken heißt halbe Dosis.
            </p>
            <LevelCalibrationPanel
              systemId={setup.id}
              bereitsKalibriert
              onDone={() => setRefresh((value) => value + 1)} />
          </V1Card>
        ) : (
          <LevelCalibrationPanel systemId={setup.id} onDone={() => setRefresh((value) => value + 1)} />
        )}
      </V1Section>

      <V1Section title="Verknüpfte Grows">
        {linkedGrows.length === 0 ? <V1Empty title="Keine aktiven Grows verknüpft" /> : <div className="v1-list">{linkedGrows.map((grow) => <Link key={grow.id} to={`/grows/${grow.id}`} className="v1-list-row"><strong>{grow.name}</strong><span>{grow.tentName ?? 'ohne Zelt'} · {statusName(grow.status)}</span><V1Badge>{grow.currentStage ? phaseName(grow.currentStage) : statusName(grow.status)}</V1Badge></Link>)}</div>}
      </V1Section>
    </V1Page>
  )
}

function Info({ label, value }: { label: string; value: string }) { return <div className="v1-info"><span>{label}</span><strong>{value}</strong></div> }
function formatLiters(value: number | null | undefined) { return value == null ? '–' : `${value.toLocaleString('de-DE', { maximumFractionDigits: 1 })}\u00a0L` }
/**
 * Lohnt es sich, beide Zahlen zu nennen?
 *
 * Ein paar Liter Unterschied sind normal — Schlaeuche, Restwasser, ein nicht
 * ganz randvoller Topf. Erst ab einem Zehntel wird daraus eine Aussage, die
 * jemanden interessiert.
 */
function weichtAb(gemessen: number, gerechnet: number | null | undefined) {
  if (gerechnet == null || gerechnet <= 0) return false
  return Math.abs(gemessen - gerechnet) / gerechnet > 0.1
}

/** Der rohe Sensorwert — Einheit bewusst offen: meist cm, aber nicht zwingend. */
function formatRaw(value: number | null | undefined) { return value == null ? '–' : value.toLocaleString('de-DE', { maximumFractionDigits: 2 }) }

export default HydroDetailPage
