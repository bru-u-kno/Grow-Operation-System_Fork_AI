import { useCallback, useEffect, useMemo, useState } from 'react'
import { sortenText } from '../features/grows/sorten-text'
import type { FormEvent, ReactNode } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { apiFetch, ApiRequestError, formatApiError } from '../api'
import { resolveUrl } from '../base'
import type { GrowStage, GrowSummary, HydroStyle, MeasurementDto, MeasurementUpsertPayload, MetricPayload, PhotoTag, TentDto, TentLivePayload, ValueOrigin } from '../types'
import type { HomeAssistantEntity } from '../types/hardware'
import FileInput from '../components/FileInput'
import { V1Alert, V1Badge, V1Button, V1Card, V1Empty, V1Field, V1Page, V1Section, V1Skeleton, V1Switch } from '../components/v1'
import { LiveCheckPanel } from '../features/measurement/LiveCheckPanel'
import { checkDraft, type CheckSeverity } from '../features/measurement/live-check-model'
import '../features/measurement/measurement-edit.css'
import { formatNumber, toLocalInputValue } from '../utils'
import { FOTO_TAGS, PHASEN, fotoTagName, phaseName } from '../deutsche-woerter'
import { istUnlesbar, zahlOderNull } from '../zahlenfeld'

type NumericKey = Exclude<keyof MeasurementDraft, 'takenAtLocal' | 'stage' | 'source' | 'notes' | 'solutionChange'>

type MeasurementDraft = {
  takenAtLocal: string
  stage: GrowStage
  source: ValueOrigin
  notes: string
  solutionChange: boolean
  // Bewusst kein Zahlenfeld: die Quelle sagt „moderat, nicht stark" und nennt
  // keinen Durchsatz. Ein Feld in L/min wuerde eine Genauigkeit vortaeuschen,
  // die es nicht gibt.
  waterFlow: string
  airTemperatureC: string
  humidityPercent: string
  heightCm: string
  waterAmountMl: string
  runoffAmountMl: string
  irrigationPh: string
  irrigationEc: string
  drainPh: string
  drainEc: string
  reservoirPh: string
  reservoirEc: string
  reservoirWaterTempC: string
  reservoirLevelCm: string
  reservoirLevelLiters: string
  dissolvedOxygenMgL: string
  orpMv: string
  topOffLiters: string
  addbackEc: string
  ppfdMol: string
  co2Ppm: string
  airflowAtLeafMPerMin: string
}

/** unit darf null sein: pH ist dimensionslos, "pH (pH)" sagt nichts. */
type FieldDefinition = { key: NumericKey; label: string; unit: string | null; hint?: string }

type PhotoDraft = {
  files: File[]
  caption: string
  tag: PhotoTag
}

// Die Listen und ihre deutschen Namen stehen in deutsche-woerter.ts —
// vorher standen sie viermal im Quelltext, jedes Mal auf Englisch.


const climateFields: FieldDefinition[] = [
  { key: 'airTemperatureC', label: 'Temperatur', unit: '°C' },
  { key: 'humidityPercent', label: 'Luftfeuchte', unit: '%' },
  { key: 'ppfdMol', label: 'PPFD', unit: 'µmol/m²/s' },
  { key: 'co2Ppm', label: 'CO₂', unit: 'ppm' },
  {
    key: 'airflowAtLeafMPerMin',
    label: 'Luftstrom am Blatt',
    unit: 'm/min',
    hint: 'RDWC 90–120, sonst 60–90. Mit dem Anemometer im Bestand messen, nicht am Lüfter.',
  },
]

const reservoirFields: FieldDefinition[] = [
  { key: 'reservoirPh', label: 'pH', unit: null },
  { key: 'reservoirEc', label: 'EC', unit: 'mS/cm' },
  { key: 'reservoirWaterTempC', label: 'Wassertemp.', unit: '°C' },
  { key: 'reservoirLevelCm', label: 'Wasserstand', unit: 'cm' },
  { key: 'reservoirLevelLiters', label: 'Wasserstand', unit: 'L' },
  { key: 'dissolvedOxygenMgL', label: 'DO', unit: 'mg/L' },
  { key: 'orpMv', label: 'ORP', unit: 'mV' },
]

const irrigationFields: FieldDefinition[] = [
  { key: 'waterAmountMl', label: 'Gießmenge', unit: 'ml' },
  { key: 'runoffAmountMl', label: 'Runoff', unit: 'ml' },
  { key: 'irrigationPh', label: 'Input pH', unit: null },
  { key: 'irrigationEc', label: 'Input EC', unit: 'mS/cm' },
  { key: 'drainPh', label: 'Drain pH', unit: null },
  { key: 'drainEc', label: 'Drain EC', unit: 'mS/cm' },
  { key: 'topOffLiters', label: 'Top-Off', unit: 'L' },
  { key: 'addbackEc', label: 'Addback EC', unit: 'mS/cm' },
]

const soilSolutionFields: FieldDefinition[] = irrigationFields.filter((field) => field.key !== 'topOffLiters' && field.key !== 'addbackEc')

const observationFields: FieldDefinition[] = [
  { key: 'heightCm', label: 'Höhe', unit: 'cm' },
]

// Live Home Assistant metric keys → measurement draft fields, so a new measurement
// starts pre-filled from the sensors that are already mapped.
const LIVE_TO_DRAFT: Partial<Record<string, NumericKey>> = {
  'reservoir-ph': 'reservoirPh',
  'reservoir-ec': 'reservoirEc',
  'reservoir-temp': 'reservoirWaterTempC',
  'reservoir-level': 'reservoirLevelLiters',
  'reservoir-level-cm': 'reservoirLevelCm',
  'orp': 'orpMv',
  'dissolved-oxygen': 'dissolvedOxygenMgL',
  'temperature': 'airTemperatureC',
  'humidity': 'humidityPercent',
  'co2': 'co2Ppm',
  'ppfd': 'ppfdMol',
}

/**
 * Ein Live-Wert, wie er in ein Eingabefeld gehoert.
 *
 * <b>Der Anlass (01.09.2026).</b> Hier wurde das Komma zum Punkt gemacht und
 * der Punkt ins Feld geschrieben. Unter der Zeile „Aus Home Assistant
 * vorbefuellt" standen dann „5.82" und „19.2" — direkt neben Feldern, in die
 * der Nutzer „5,82" tippt. Gerechnet wird beim Absenden ohnehin mit
 * <code>parseNullableNumber</code>, das beides liest; der Punkt war nur auf
 * dem Schirm.
 */
function normalizeLiveValue(value: string): string | null {
  const cleaned = value.trim().replace(',', '.')
  if (cleaned === '' || cleaned === '–' || cleaned === '-') return null
  return Number.isFinite(Number(cleaned)) ? cleaned.replace('.', ',') : null
}

/** forkai.99: Ein Verbrauchsartikel, so weit ihn das Messformular braucht. */
type KostenArtikelOption = { id: number; name: string; einheit: string; aktiv: boolean }

/** Eine Zeile im Gaben-Block. Menge bleibt Text, damit „0,5" waehrend des Tippens nicht zerfaellt. */
type GabeZeile = { schluessel: number; artikelId: number | null; menge: string }

function ManualMeasurementPage() {
  const navigate = useNavigate()
  // forkai.99: Gaben zur Messung. Eine Gabe am Becken ist selten ein Mittel —
  // Purolyt und pH-Minus am selben Abend, vier Naehrstoffe beim Addback.
  // Deshalb eine Liste und kein einzelnes Feld.
  const [artikel, setArtikel] = useState<KostenArtikelOption[]>([])
  const [gaben, setGaben] = useState<GabeZeile[]>([])
  const [grows, setGrows] = useState<GrowSummary[]>([])
  const [selectedGrowId, setSelectedGrowId] = useState<number | null>(null)
  const [draft, setDraft] = useState<MeasurementDraft>(() => createDraft())
  const [photoDraft, setPhotoDraft] = useState<PhotoDraft>({ files: [], caption: '', tag: 'Overview' })
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [prefilled, setPrefilled] = useState(false)
  const [livePulling, setLivePulling] = useState(false)
  const [cameras, setCameras] = useState<string[]>([])
  // forkai.126: Anzeigenamen der Kameras aus HA. Ohne sie stand im Auswahlfeld
  // die Entitäts-Id („Rdwc overview standardauflosung“).
  const [kameraNamen, setKameraNamen] = useState<Record<string, string>>({})
  const [snapshotCam, setSnapshotCam] = useState('')
  const [snapshotting, setSnapshotting] = useState(false)
  const [growActionSaving, setGrowActionSaving] = useState<string | null>(null)
  // How many °C the leaf sits below air temperature — configured on the tent, used for VPD.
  const [leafOffset, setLeafOffset] = useState(0)
  // Die Live-Metriken bringen die Zielbereiche der aktuellen Phase mit. Bisher
  // wurden sie nur zum Vorbefuellen gelesen und danach weggeworfen — dieselben
  // Zahlen tragen jetzt die Pruefung neben dem Formular.
  const [liveMetrics, setLiveMetrics] = useState<MetricPayload[]>([])

  // Fetches the tent's current live values and writes the mappable ones into the draft.
  // Returns whether any live value was available at all.
  const pullLive = useCallback(async (tentId: number, overwrite: boolean, signal?: AbortSignal): Promise<boolean> => {
    const live = await apiFetch<TentLivePayload>(`/api/live/tents/${tentId}`, signal ? { signal } : undefined)
    setLiveMetrics(live.metrics)
    const mappable = live.metrics.some((metric) => LIVE_TO_DRAFT[metric.key] && normalizeLiveValue(metric.value) != null)
    setDraft((current) => {
      const next = { ...current }
      for (const metric of live.metrics) {
        const field = LIVE_TO_DRAFT[metric.key]
        if (!field) continue
        const value = normalizeLiveValue(metric.value)
        if (value == null) continue
        if (!overwrite && next[field].trim() !== '') continue
        next[field] = value
      }
      return next
    })
    return mappable
  }, [])

  useEffect(() => {
    const controller = new AbortController()

    async function load() {
      setLoading(true)
      setError(null)
      try {
        const artikelListe = await apiFetch<KostenArtikelOption[]>('/api/kosten/artikel', { signal: controller.signal })
          .catch(() => [] as KostenArtikelOption[])
        setArtikel(artikelListe.filter((a) => a.aktiv))
        const data = await apiFetch<GrowSummary[]>('/api/grows?archived=false', { signal: controller.signal })
        if (controller.signal.aborted) return
        const active = data.filter((grow) => grow.status === 'Running' || grow.status === 'Planning')
        setGrows(active)
        setSelectedGrowId((current) => current ?? active[0]?.id ?? null)
        // Die Phase von HEUTE (currentStage), nicht die Aufschrift der letzten
        // Messung. Vorher schlug das Formular die alte Aufschrift vor, und die
        // naechste Messung schrieb sie fort — waehrend die App dieselbe Zeile
        // gleich darauf mit „≠ Bluete" als falsch markierte.
        const stage = active[0]?.currentStage ?? 'Veg'
        setDraft((current) => ({ ...current, stage }))
      } catch (caught) {
        if (!controller.signal.aborted) setError(formatApiError(caught, 'Grows konnten nicht geladen werden.'))
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }

    void load()
    return () => controller.abort()
  }, [])

  const selectedGrow = useMemo(() => grows.find((grow) => grow.id === selectedGrowId) ?? null, [grows, selectedGrowId])
  const filledCount = useMemo(() => countFilled(draft), [draft])
  const vpd = useMemo(() => calculateVpd(draft.airTemperatureC, draft.humidityPercent, leafOffset), [draft.airTemperatureC, draft.humidityPercent, leafOffset])

  // Der Rand des Eingabefelds sagt beim Tippen, ob der Wert im Ziel liegt —
  // dieselbe Pruefung wie im Panel rechts, nur direkt am Feld. Gruen heisst im
  // Zielband, gelb knapp daneben, rot deutlich — wie im Entwurf.
  const fieldStatus = useMemo(() => {
    const map: Record<string, CheckSeverity> = {}
    const strings = Object.fromEntries(
      Object.entries(draft).filter((entry): entry is [string, string] => typeof entry[1] === 'string'),
    )
    for (const finding of checkDraft(strings, liveMetrics)) map[finding.field] = finding.severity
    return map
  }, [draft, liveMetrics])
  const isHydroGrow = isHydroStyle(selectedGrow?.hydroStyle)
  const solutionFields = isHydroGrow ? reservoirFields : soilSolutionFields
  const tentId = selectedGrow?.tentId ?? null

  // Lifecycle confirmations belong here, at measurement time — confirming germination,
  // rooting, or the flip to 12/12 is an observation you make when you check the plant.
  const canConfirmGermination = selectedGrow?.startMaterial === 'Seed' && !selectedGrow?.germinatedAt
  const canConfirmRooting = selectedGrow?.startMaterial === 'Clone' && !selectedGrow?.rootedAt
  const canFlipToFlower = selectedGrow != null && selectedGrow.seedType !== 'Autoflower' && !selectedGrow.flipDate

  // Pre-fill the mappable fields from Home Assistant when the tent context appears or
  // changes. Best-effort: silently skipped if HA is unreachable.
  useEffect(() => {
    if (tentId == null) return
    const controller = new AbortController()
    void (async () => {
      try {
        const any = await pullLive(tentId, true, controller.signal)
        if (!controller.signal.aborted && any) setPrefilled(true)
      } catch { /* HA offline or no live values — leave fields empty */ }
    })()
    return () => controller.abort()
  }, [tentId, pullLive])

  // Load the tent's cameras so a snapshot can be attached to the measurement.
  useEffect(() => {
    const controller = new AbortController()
    void (async () => {
      if (tentId == null) {
        setCameras([])
        setSnapshotCam('')
        setLeafOffset(0)
        return
      }
      try {
        const tent = await apiFetch<TentDto>(`/api/settings/tents/${tentId}`, { signal: controller.signal })
        if (controller.signal.aborted) return
        const list = tent.cameras ?? []
        setCameras(list)
        setSnapshotCam(list[0] ?? '')
        setLeafOffset(tent.leafTempOffsetC ?? 0)
        if (list.length > 0) {
          try {
            const entities = await apiFetch<HomeAssistantEntity[]>('/api/home-assistant/entities', { signal: controller.signal })
            if (controller.signal.aborted) return
            const namen: Record<string, string> = {}
            for (const entity of entities) {
              if (list.includes(entity.entityId) && entity.friendlyName) namen[entity.entityId] = entity.friendlyName
            }
            setKameraNamen(namen)
          } catch { /* HA offline — dann bleibt die Id als Name */ }
        }
      } catch { /* ignore */ }
    })()
    return () => controller.abort()
  }, [tentId])

  async function captureSnapshot() {
    if (tentId == null || snapshotCam === '') return
    setSnapshotting(true)
    setError(null)
    setMessage(null)
    try {
      const response = await fetch(resolveUrl(`/api/live/tents/${tentId}/camera?entity=${encodeURIComponent(snapshotCam)}&t=${Date.now()}`))
      if (!response.ok) throw new Error('Kamera nicht erreichbar')
      const blob = await response.blob()
      const file = new File([blob], `snapshot-${Date.now()}.jpg`, { type: blob.type || 'image/jpeg' })
      setPhotoDraft((current) => ({ ...current, files: [...current.files, file] }))
      setMessage('Kamera-Snapshot hinzugefügt.')
    } catch {
      setError('Snapshot konnte nicht aufgenommen werden — ist die Kamera in Home Assistant erreichbar?')
    } finally {
      setSnapshotting(false)
    }
  }

  async function confirmGrowAction(action: 'germination' | 'rooting' | 'flip') {
    if (!selectedGrowId) return
    const route = action === 'germination' ? 'confirm-germination' : action === 'rooting' ? 'confirm-rooting' : 'flip-to-flower'
    setGrowActionSaving(action)
    setError(null)
    setMessage(null)
    try {
      const result = await apiFetch<{ message: string }>(`/api/grows/${selectedGrowId}/actions/${route}`, { method: 'POST' })
      setMessage(result.message)
      // Re-pull grows so the just-confirmed step drops off (germinatedAt/rootedAt/flipDate set).
      const data = await apiFetch<GrowSummary[]>('/api/grows?archived=false')
      setGrows(data.filter((grow) => grow.status === 'Running' || grow.status === 'Planning'))
    } catch (caught) {
      setError(formatApiError(caught, 'Aktion konnte nicht ausgeführt werden.'))
    } finally {
      setGrowActionSaving(null)
    }
  }

  async function refreshFromLive() {
    if (tentId == null) return
    setLivePulling(true)
    setError(null)
    try {
      const any = await pullLive(tentId, true)
      setPrefilled(any)
      if (!any) setMessage('Keine Live-Werte in Home Assistant gefunden.')
    } catch {
      setError('Live-Werte konnten nicht geladen werden.')
    } finally {
      setLivePulling(false)
    }
  }

  function patch(patchValue: Partial<MeasurementDraft>) {
    setDraft((current) => ({ ...current, ...patchValue }))
  }

  function selectGrow(growId: number) {
    const grow = grows.find((item) => item.id === growId)
    setSelectedGrowId(growId)
    if (grow?.currentStage) patch({ stage: grow.currentStage })
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    await save('grow')
  }

  /**
   * Speichert die Messung. „Speichern & Addback“ springt danach direkt in den
   * Addback — der häufigste nächste Schritt, wenn der EC daneben liegt: erst
   * messen, dann nachdosieren, ohne Umweg über die Grow-Seite.
   */
  async function save(after: 'grow' | 'addback') {
    if (!selectedGrowId) {
      setError('Bitte Grow auswählen.')
      return
    }

    // Lieber gar nicht speichern als eine Messung, der die Haelfte fehlt.
    const unlesbar = unlesbareFelder(draft)
    if (unlesbar.length > 0) {
      setError(unlesbar.length === 1
        ? `„${unlesbar[0]}" ist keine Zahl. Bitte korrigieren oder das Feld leeren — sonst geht der Wert verloren, ohne dass es jemand merkt.`
        : `Diese Felder enthalten keine Zahl: ${unlesbar.join(', ')}. Bitte korrigieren oder leeren — sonst gehen die Werte verloren, ohne dass es jemand merkt.`)
      return
    }

    setSaving(true)
    setError(null)
    setMessage(null)

    try {
      const payload = toPayload(draft)
      const measurement = await createMeasurement(selectedGrowId, payload)

      if (photoDraft.files.length > 0) {
        await uploadPhotos(measurement.id, photoDraft)
      }

      // Die Buchung haengt an der gespeicherten Messung, laeuft aber als
      // eigener Aufruf: die Messung ist dann schon sicher, und ein Fehler beim
      // Buchen kostet sie nicht. Der Hinweis sagt, was durchkam und was nicht.
      // forkai.104: zahlOderNull statt eigener Umwandlung. Number('') ist 0 und
      // Number.isFinite(0) ist true — eine geleerte Menge waere als 0 gebucht
      // worden, still und mit Erfolgsmeldung. Genau der Fehler, den die
      // Dosierpumpe schon einmal hatte.
      const unlesbar = gaben.find((g) => istUnlesbar(g.menge))
      if (unlesbar) {
        setError('Eine Menge im Abschnitt Zugaben ist nicht lesbar. Bitte korrigieren oder die Zeile entfernen.')
        setSaving(false)
        return
      }
      const zuBuchen = gaben
        .map((g) => ({ artikelId: g.artikelId, menge: zahlOderNull(g.menge) }))
        .filter((g): g is { artikelId: number; menge: number } => g.artikelId != null && g.menge != null && g.menge > 0)
      let gabenHinweis = ''
      if (zuBuchen.length > 0) {
        try {
          await apiFetch('/api/kosten/verbrauch', {
            method: 'POST',
            body: JSON.stringify({
              growId: selectedGrowId,
              messungId: measurement.id,
              zeitpunkt: draft.takenAtLocal || null,
              quelle: 'messung',
              zeilen: zuBuchen,
            }),
          })
          gabenHinweis = ` ${zuBuchen.length} ${zuBuchen.length === 1 ? 'Zugabe' : 'Zugaben'} gebucht.`
        } catch (caught) {
          setError(formatApiError(caught, 'Die Messung ist gespeichert, die Zugaben konnten nicht gebucht werden.'))
          setSaving(false)
          return
        }
      }

      setMessage('Messung gespeichert.' + gabenHinweis)
      navigate(after === 'addback' ? `/grows/${selectedGrowId}/addback` : `/grows/${selectedGrowId}`)
    } catch (caught) {
      setError(formatApiError(caught, 'Messung konnte nicht gespeichert werden.'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <V1Page
      eyebrow="Jetzt / Messen"
      title="Messung erfassen"
      subtitle="Werte, Foto, speichern."
      action={<Link className="v1-button is-ghost" to="/">Zurück</Link>}
      className="rc2-measurement-page"
    >
      {error && <V1Alert title="Fehler" message={error} tone="warn" />}
      {message && <V1Alert message={message} tone="ok" />}

      {loading ? <V1Skeleton tiles={6} label="Lade Messfelder" /> : grows.length === 0 ? (
        <div data-audit="measurement-empty-state">
          <V1Empty
            title="Noch kein Grow für Messungen"
            action={(
              <div className="measurement-empty-actions">
                <Link to="/grows/new" className="v1-button is-primary">Grow anlegen</Link>
                <Link to="/zelte/new" className="v1-button is-secondary">Zelt anlegen</Link>
              </div>
            )}
          />
        </div>
      ) : (
        <form className="ms-layout" data-audit="measurement-form" onSubmit={(event) => void submit(event)}>
          <div className="ms-form">
          <V1Card className="rc2-sticky-card rc2-measurement-context" data-audit="measurement-section-context">
            <span className="v1-card-kicker">Kontext</span>
            <h2>{selectedGrow?.name ?? 'Grow wählen'}</h2>
            <p>{(selectedGrow ? sortenText(selectedGrow) : null) ?? 'Sorte offen'} · {selectedGrow?.tentName ?? 'ohne Zelt'}</p>
            {selectedGrow && <p className="rc2-measurement-note">Hydro: {formatGrowHydroMedium(selectedGrow)}</p>}
            <V1Field label="Grow">
              <select value={selectedGrowId ?? ''} onChange={(event) => selectGrow(Number(event.target.value))}>
                {grows.map((grow) => <option key={grow.id} value={grow.id}>{grow.name}</option>)}
              </select>
            </V1Field>
            {grows.length === 1 && <small className="rc2-measurement-note">Eindeutig vorausgewählt, Wechsel bleibt möglich.</small>}
            <V1Field label="Zeitpunkt">
              <input type="datetime-local" value={draft.takenAtLocal} onChange={(event) => patch({ takenAtLocal: event.target.value })} />
            </V1Field>
            <V1Field label="Phase">
              <select value={draft.stage} onChange={(event) => patch({ stage: event.target.value as GrowStage })}>
                {PHASEN.map((stage) => <option key={stage} value={stage}>{phaseName(stage)}</option>)}
              </select>
            </V1Field>
            <V1Badge tone={filledCount > 0 ? 'ok' : 'neutral'}>{filledCount} Werte</V1Badge>
            {(canConfirmGermination || canConfirmRooting || canFlipToFlower) && (
              <div className="rc2-measurement-live" style={{ display: 'grid', gap: 8 }}>
                <span className="v1-card-kicker">Phase bestätigen</span>
                {canConfirmGermination && (
                  <V1Button variant="secondary" onClick={() => void confirmGrowAction('germination')} disabled={growActionSaving !== null}>
                    {growActionSaving === 'germination' ? 'Bestätigt…' : 'Keimung bestätigen'}
                  </V1Button>
                )}
                {canConfirmRooting && (
                  <V1Button variant="secondary" onClick={() => void confirmGrowAction('rooting')} disabled={growActionSaving !== null}>
                    {growActionSaving === 'rooting' ? 'Bestätigt…' : 'Bewurzelung bestätigen'}
                  </V1Button>
                )}
                {canFlipToFlower && (
                  <V1Button variant="secondary" onClick={() => void confirmGrowAction('flip')} disabled={growActionSaving !== null}>
                    {growActionSaving === 'flip' ? 'Trägt ein…' : 'Flip zu 12/12'}
                  </V1Button>
                )}
              </div>
            )}
            {tentId != null && (
              <div className="rc2-measurement-live" style={{ display: 'grid', gap: 8 }}>
                {prefilled && <p className="rc2-measurement-note">Aus Home Assistant vorbefüllt — anpassbar.</p>}
                <V1Button variant="secondary" onClick={() => void refreshFromLive()} disabled={livePulling}>
                  {livePulling ? 'Lädt…' : 'Aus Home Assistant übernehmen'}
                </V1Button>
              </div>
            )}
          </V1Card>
            <div data-audit="measurement-section-climate">
              <V1Section title="Klima">
                <FieldGrid fields={climateFields} draft={draft} patch={patch} status={fieldStatus}>
                  <div className="rc2-measurement-derived" data-audit="measurement-vpd">
                    <span>VPD{leafOffset < 0 ? ` · Blatt ${leafOffset} °C` : ''}</span>
                    <strong>{vpd ?? '–'}<em>kPa</em></strong>
                  </div>
                </FieldGrid>
              </V1Section>
            </div>

            <div data-audit="measurement-section-hydro">
              <V1Section title={isHydroGrow ? 'Hydro / Nährlösung' : 'Gießen / Drain'}>
                <FieldGrid fields={solutionFields} draft={draft} patch={patch} status={fieldStatus} />
              </V1Section>
            </div>

            {isHydroGrow && (
              <div data-audit="measurement-section-addback">
                <V1Section title="Addback">
                  <FieldGrid fields={irrigationFields} draft={draft} patch={patch} />
                </V1Section>
              </div>
            )}

            <div data-audit="measurement-section-observation">
              <V1Section title="Beobachtung">
                <div className="rc2-measurement-extra">
                  <FieldGrid fields={observationFields} draft={draft} patch={patch} />
                  {isHydroGrow && (
                    <V1Field
                      label="Wasserfluss"
                      hint="Moderat ist das Ziel. Starker Flow zerrt an den Wurzeln — mehr Umwälzung verteilt nicht besser.">
                      <select value={draft.waterFlow} onChange={(event) => patch({ waterFlow: event.target.value })}>
                        <option value="">nicht beurteilt</option>
                        <option value="Weak">schwach</option>
                        <option value="Moderate">moderat</option>
                        <option value="Strong">stark</option>
                      </select>
                    </V1Field>
                  )}
                  <V1Switch label="Lösungswechsel" checked={draft.solutionChange} onChange={(checked) => patch({ solutionChange: checked })} hint="Reservoir oder Nährlösung vollständig gewechselt." />
                  <V1Field label="Notiz" wide>
                    <textarea rows={4} value={draft.notes} onChange={(event) => patch({ notes: event.target.value })} placeholder="Blattbild, Wurzeln, Geruch, Korrektur..." />
                  </V1Field>
                </div>
              </V1Section>
            </div>

          </div>

          <aside className="ms-side">
            <div data-audit="measurement-section-check">
              <LiveCheckPanel draft={draft} metrics={liveMetrics} />
            </div>

            <div data-audit="measurement-section-photo">
              <V1Section title="Zugaben" action={
                <V1Button
                  onClick={() => setGaben((v) => [...v, { schluessel: Date.now(), artikelId: null, menge: '' }])}
                  audit="messung-gabe-hinzufuegen"
                >
                  + Zugabe
                </V1Button>
              }>
                <div className="rc2-measurement-extra ms-zugaben">
                  {artikel.length === 0 ? (
                    <V1Empty title="Keine Produkte" text="Unter Kosten → Artikel anlegen, dann erscheinen sie hier." />
                  ) : gaben.length === 0 ? (
                    <V1Empty title="Nichts zugegeben" text="Nährstoffe, Purolyt, pH-Minus — wird als Verbrauch gebucht und an diese Messung gehängt." />
                  ) : (
                    gaben.map((zeile) => {
                      const gewaehlt = artikel.find((a) => a.id === zeile.artikelId)
                      // forkai.126: eigene Zeile über die volle Breite. Vorher saß die
                      // Flex-Zeile in EINER Spalte des Zwei-Spalten-Rasters — am Handy
                      // schrumpfte das leere Auswahlfeld auf nichts und „Entfernen“
                      // brach Buchstabe für Buchstabe um.
                      return (
                        <div key={zeile.schluessel} className="ms-zugabe-zeile">
                          <V1Field label="Produkt">
                            <select
                              value={zeile.artikelId ?? ''}
                              onChange={(e) => setGaben((v) => v.map((z) => z.schluessel === zeile.schluessel
                                ? { ...z, artikelId: e.target.value === '' ? null : Number(e.target.value) }
                                : z))}
                              aria-label="Produkt wählen"
                            >
                              <option value="">Produkt wählen…</option>
                              {artikel.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
                            </select>
                          </V1Field>
                          <V1Field label={gewaehlt?.einheit ? `Menge (${gewaehlt.einheit})` : 'Menge'}>
                            <input
                              type="text"
                              inputMode="decimal"
                              value={zeile.menge}
                              onChange={(e) => setGaben((v) => v.map((z) => z.schluessel === zeile.schluessel
                                ? { ...z, menge: e.target.value }
                                : z))}
                              placeholder="–"
                              aria-label="Menge"
                            />
                          </V1Field>
                          <button
                            type="button"
                            className="v1-button is-ghost ms-zugabe-weg"
                            data-audit="messung-gabe-entfernen"
                            onClick={() => setGaben((v) => v.filter((z) => z.schluessel !== zeile.schluessel))}
                            aria-label="Zugabe entfernen"
                            title="Zugabe entfernen"
                          >
                            ✕
                          </button>
                        </div>
                      )
                    })
                  )}
                </div>
              </V1Section>
              <V1Section title="Foto">
              <div className="rc2-measurement-extra rc2-measurement-photo">
                {cameras.length > 0 && (
                  <V1Field label="Kamera-Snapshot" wide hint="Wähle die Kamera und nimm ein Foto vom aktuellen Kamerabild — es wird angehängt.">
                    <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
                      {cameras.length > 1 ? (
                        <select value={snapshotCam} onChange={(event) => setSnapshotCam(event.target.value)} aria-label="Kamera wählen" style={{ minWidth: 180 }}>
                          {cameras.map((camera, index) => <option key={camera} value={camera}>{kameraNamen[camera] ?? cameraLabel(camera, index)}</option>)}
                        </select>
                      ) : (
                        <span className="rc2-measurement-note">Kamera: {kameraNamen[cameras[0]] ?? cameraLabel(cameras[0], 0)}</span>
                      )}
                      <V1Button variant="secondary" onClick={() => void captureSnapshot()} disabled={snapshotting}>
                        {snapshotting ? 'Nimmt auf…' : 'Snapshot aufnehmen'}
                      </V1Button>
                    </div>
                  </V1Field>
                )}
                <V1Field label="Foto-Tag">
                  <select value={photoDraft.tag} onChange={(event) => setPhotoDraft((current) => ({ ...current, tag: event.target.value as PhotoTag }))}>
                    {FOTO_TAGS.map((tag) => <option key={tag} value={tag}>{fotoTagName(tag)}</option>)}
                  </select>
                </V1Field>
                <V1Field label="Beschriftung">
                  <input value={photoDraft.caption} onChange={(event) => setPhotoDraft((current) => ({ ...current, caption: event.target.value }))} />
                </V1Field>
                <V1Field label="Fotos" wide>
                  <FileInput accept="image/png,image/jpeg,image/webp" label="Foto auswählen" multiple fileNames={photoDraft.files.map((file) => file.name)} onFiles={(files) => setPhotoDraft((current) => ({ ...current, files }))} />
                  <small>Optional, ein oder mehrere Bilder.</small>
                  <PhotoThumbs
                    files={photoDraft.files}
                    onRemove={(index) => setPhotoDraft((current) => ({ ...current, files: current.files.filter((_, i) => i !== index) }))}
                  />
                </V1Field>
              </div>
              </V1Section>
            </div>

            <div className="v1-form-actions ms-actions" data-audit="measurement-form-actions">
              <V1Button type="submit" variant="primary" disabled={saving}>{saving ? 'Speichert...' : 'Messung speichern'}</V1Button>
              {isHydroGrow && selectedGrowId != null && (
                <V1Button variant="secondary" disabled={saving} onClick={() => void save('addback')}>
                  Speichern & Addback
                </V1Button>
              )}
              <Link className="v1-button is-ghost" to="/">Abbrechen</Link>
            </div>
          </aside>
        </form>
      )}
    </V1Page>
  )
}

// Thumbnails of the photos/snapshots attached to this measurement, so a captured
// snapshot is actually visible (and removable) before saving.
function PhotoThumbs({ files, onRemove }: { files: File[]; onRemove: (index: number) => void }) {
  const urls = useMemo(() => files.map((file) => URL.createObjectURL(file)), [files])
  useEffect(() => () => urls.forEach((url) => URL.revokeObjectURL(url)), [urls])
  if (files.length === 0) return null
  return (
    <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginTop: 10 }}>
      {urls.map((url, index) => (
        <div key={index} style={{ position: 'relative', width: 84, height: 84 }}>
          <img src={url} alt={files[index]?.name ?? 'Foto'} style={{ width: '100%', height: '100%', objectFit: 'cover', borderRadius: 8, border: '1px solid var(--v1-line)' }} />
          <button
            type="button"
            onClick={() => onRemove(index)}
            aria-label="Foto entfernen"
            style={{ position: 'absolute', top: -7, right: -7, width: 22, height: 22, borderRadius: '50%', border: 'none', background: 'rgba(0,0,0,0.72)', color: 'white', cursor: 'pointer', fontSize: 'var(--fs-text)', lineHeight: '22px', padding: 0 }}
          >
            ×
          </button>
        </div>
      ))}
    </div>
  )
}

function FieldGrid({ children, fields, draft, patch, status }: { children?: ReactNode; fields: FieldDefinition[]; draft: MeasurementDraft; patch: (patchValue: Partial<MeasurementDraft>) => void; status?: Record<string, CheckSeverity> }) {
  return (
    <div className="rc2-measurement-grid">
      {fields.map((field) => (
        <V1Field key={field.key} label={`${field.label} ${field.unit ? `(${field.unit})` : ''}`} hint={field.hint}>
          <input
            inputMode="decimal"
            className={status?.[field.key] ? `is-${status[field.key]}` : undefined}
            value={draft[field.key]}
            onChange={(event) => patch({ [field.key]: event.target.value } as Partial<MeasurementDraft>)}
            placeholder="–"
          />
        </V1Field>
      ))}
      {children}
    </div>
  )
}

async function createMeasurement(growId: number, payload: MeasurementUpsertPayload) {
  try {
    return await apiFetch<MeasurementDto>(`/api/grows/${growId}/measurements`, {
      method: 'POST',
      body: JSON.stringify(payload),
    })
  } catch (caught) {
    if (caught instanceof ApiRequestError && caught.status === 404) {
      return await apiFetch<MeasurementDto>(`/api/measurements?growId=${growId}`, {
        method: 'POST',
        body: JSON.stringify(payload),
      })
    }
    throw caught
  }
}

async function uploadPhotos(measurementId: number, draft: PhotoDraft) {
  const form = new FormData()
  form.append('photoCaption', draft.caption)
  form.append('photoTag', draft.tag)
  form.append('useAsReferenceShot', 'false')
  form.append('source', 'Manual')
  for (const file of draft.files) form.append('photos', file)
  await apiFetch(`/api/measurements/${measurementId}/photos`, { method: 'POST', body: form })
}

function createDraft(): MeasurementDraft {
  return {
    takenAtLocal: toLocalInputValue(),
    stage: 'Veg',
    source: 'Manual',
    notes: '',
    solutionChange: false,
    waterFlow: '',
    airflowAtLeafMPerMin: '',
    airTemperatureC: '',
    humidityPercent: '',
    heightCm: '',
    waterAmountMl: '',
    runoffAmountMl: '',
    irrigationPh: '',
    irrigationEc: '',
    drainPh: '',
    drainEc: '',
    reservoirPh: '',
    reservoirEc: '',
    reservoirWaterTempC: '',
    reservoirLevelCm: '',
    reservoirLevelLiters: '',
    dissolvedOxygenMgL: '',
    orpMv: '',
    topOffLiters: '',
    addbackEc: '',
    ppfdMol: '',
    co2Ppm: '',
  }
}

function toPayload(draft: MeasurementDraft): MeasurementUpsertPayload {
  return {
    takenAtLocal: draft.takenAtLocal,
    stage: draft.stage,
    source: draft.source,
    notes: trimToNull(draft.notes),
    airTemperatureC: parseNullableNumber(draft.airTemperatureC),
    humidityPercent: parseNullableNumber(draft.humidityPercent),
    heightCm: parseNullableNumber(draft.heightCm),
    waterAmountMl: parseNullableNumber(draft.waterAmountMl),
    runoffAmountMl: parseNullableNumber(draft.runoffAmountMl),
    irrigationPh: parseNullableNumber(draft.irrigationPh),
    irrigationEc: parseNullableNumber(draft.irrigationEc),
    drainPh: parseNullableNumber(draft.drainPh),
    drainEc: parseNullableNumber(draft.drainEc),
    reservoirPh: parseNullableNumber(draft.reservoirPh),
    reservoirEc: parseNullableNumber(draft.reservoirEc),
    reservoirWaterTempC: parseNullableNumber(draft.reservoirWaterTempC),
    reservoirLevelCm: parseNullableNumber(draft.reservoirLevelCm),
    reservoirLevelLiters: parseNullableNumber(draft.reservoirLevelLiters),
    dissolvedOxygenMgL: parseNullableNumber(draft.dissolvedOxygenMgL),
    orpMv: parseNullableNumber(draft.orpMv),
    topOffLiters: parseNullableNumber(draft.topOffLiters),
    addbackEc: parseNullableNumber(draft.addbackEc),
    solutionChange: draft.solutionChange,
    ppfdMol: parseNullableNumber(draft.ppfdMol),
    co2Ppm: parseNullableNumber(draft.co2Ppm),
    airflowAtLeafMPerMin: parseNullableNumber(draft.airflowAtLeafMPerMin),
    waterFlow: draft.waterFlow || null,
  }
}

function countFilled(draft: MeasurementDraft) {
  const ignored = new Set(['takenAtLocal', 'stage', 'source', 'notes', 'solutionChange'])
  return Object.entries(draft).filter(([key, value]) => !ignored.has(key) && String(value).trim().length > 0).length
}

function isHydroStyle(style: HydroStyle | null | undefined) {
  return style != null && style !== 'None'
}

function formatGrowHydroMedium(grow: GrowSummary) {
  return grow.hydroSetupName ?? (grow.hydroStyle === 'None' ? 'kein Hydro-Setup' : grow.hydroStyle)
}

// A readable name for a camera entity, e.g. "camera.hauptzelt" → "Hauptzelt".
function cameraLabel(entity: string, index: number): string {
  const short = entity.replace(/^(camera|image)\./i, '').replace(/[_-]+/g, ' ').trim()
  if (!short) return `Kamera ${index + 1}`
  return short.charAt(0).toUpperCase() + short.slice(1)
}

// Leaf VPD: the deficit is measured against the (cooler) leaf surface, while the actual
// vapour pressure comes from the air. leafOffsetC = 0 gives plain air VPD.
function saturationKpa(temperatureC: number) {
  return 0.6108 * Math.exp((17.27 * temperatureC) / (temperatureC + 237.3))
}

function calculateVpd(temperatureValue: string, humidityValue: string, leafOffsetC = 0) {
  const temperature = parseNullableNumber(temperatureValue)
  const humidity = parseNullableNumber(humidityValue)
  if (temperature == null || humidity == null || humidity < 0 || humidity > 100) return null
  const actual = saturationKpa(temperature) * (humidity / 100)
  const leaf = saturationKpa(temperature + leafOffsetC)

  // `toFixed` schreibt IMMER mit Punkt — im Formular stand deshalb „1.00 kPa"
  // mitten in einer deutschen Oberflaeche. Dieselbe Falle wie an den
  // Diagramm-Achsen; gefunden, weil /messung in keiner Zahlen-Pruefung stand.
  return formatNumber(Math.max(0, leaf - actual), 2)
}

/**
 * Die Zahlenfelder des Formulars mit ihrer Beschriftung.
 *
 * Nur dafür da, ein unlesbares Feld beim Namen nennen zu können — „pH
 * (Reservoir)" statt „reservoirPh". Kommt ein Feld dazu, gehört es hier hinein;
 * fehlt es, wird sein Inhalt weiterhin stillschweigend verworfen.
 */
const ZAHLENFELDER: Array<[keyof MeasurementDraft, string]> = [
  ['airTemperatureC', 'Lufttemperatur'],
  ['humidityPercent', 'Luftfeuchte'],
  ['heightCm', 'Höhe'],
  ['waterAmountMl', 'Gießmenge'],
  ['runoffAmountMl', 'Ablauf'],
  ['irrigationPh', 'pH (Gießwasser)'],
  ['irrigationEc', 'EC (Gießwasser)'],
  ['drainPh', 'pH (Ablauf)'],
  ['drainEc', 'EC (Ablauf)'],
  ['reservoirPh', 'pH (Reservoir)'],
  ['reservoirEc', 'EC (Reservoir)'],
  ['reservoirWaterTempC', 'Wassertemperatur'],
  ['reservoirLevelCm', 'Füllstand (cm)'],
  ['reservoirLevelLiters', 'Füllstand (L)'],
  ['dissolvedOxygenMgL', 'Sauerstoff'],
  ['orpMv', 'ORP'],
  ['topOffLiters', 'Nachgefüllt'],
  ['addbackEc', 'EC nach Addback'],
  ['ppfdMol', 'PPFD'],
  ['co2Ppm', 'CO₂'],
  ['airflowAtLeafMPerMin', 'Luftstrom am Blatt'],
]

/**
 * Felder, in denen etwas steht, das keine Zahl ist.
 *
 * <b>Warum es diese Prüfung braucht.</b> `parseNullableNumber` macht aus „leer"
 * und aus „unlesbar" dasselbe Ergebnis: `null`. Wer sich beim pH vertippt und
 * „6,2x" stehen lässt, speichert eine Messung ohne pH — und die App meldet
 * Erfolg. Der Wert ist weg, niemand hat es gesagt, und beim nächsten Blick auf
 * die Kurve fehlt einfach ein Punkt.
 */
function unlesbareFelder(draft: MeasurementDraft): string[] {
  return ZAHLENFELDER
    .filter(([feld]) => {
      const roh = String(draft[feld] ?? '').trim()
      return roh !== '' && parseNullableNumber(roh) === null
    })
    .map(([, label]) => label)
}

function parseNullableNumber(value: string) {
  const trimmed = value.trim().replace(',', '.')
  if (!trimmed) return null
  const parsed = Number(trimmed)
  return Number.isFinite(parsed) ? parsed : null
}

function trimToNull(value: string) {
  const trimmed = value.trim()
  return trimmed ? trimmed : null
}


export default ManualMeasurementPage
