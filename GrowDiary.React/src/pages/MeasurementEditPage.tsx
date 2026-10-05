import type { FormEvent } from 'react'
import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { apiFetch, ApiRequestError } from '../api'
import type { GrowDetail, GrowStage, MeasurementDto, MeasurementUpsertPayload, PhotoAssetDto, PhotoTag, ValueOrigin } from '../types'
import { formatDateTime, toLocalInputValue } from '../utils'
import { V1Alert, V1Badge, V1Button, V1Empty, V1Field, V1LinkButton, V1Page, V1Section, V1Skeleton } from '../components/v1'
import '../features/measurement/measurement-edit.css'
import { FOTO_TAGS, PHASEN, fotoTagName, herkunftName, phaseName } from '../deutsche-woerter'
import { unlesbarMeldung, unlesbareFelder, zahlOderNull, feldText } from '../zahlenfeld'

interface MeasurementEditState {
  takenAtLocal: string
  stage: GrowStage
  source: ValueOrigin
  notes: string
  solutionChange: boolean
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
  /** Stufe, keine Zahl — deshalb aus den Zahlenfeldern ausgenommen. */
  waterFlow: string
}

type MeasurementNumericFieldKey = Exclude<keyof MeasurementEditState, 'takenAtLocal' | 'stage' | 'source' | 'notes' | 'solutionChange' | 'waterFlow'>

interface PhotoFormState {
  photoCaption: string
  photoTag: PhotoTag
  useAsReferenceShot: boolean
  source: ValueOrigin
  files: File[]
}


// Siehe deutsche-woerter.ts.
const sourceOptions: ValueOrigin[] = ['Manual', 'HomeAssistant', 'Imported', 'Derived']

const fieldSections: Array<{ title: string, fields: Array<{ key: MeasurementNumericFieldKey, label: string, unit: string | null }> }> = [
  {
    title: 'Klima',
    fields: [
      { key: 'airTemperatureC', label: 'Lufttemp', unit: '°C' },
      { key: 'humidityPercent', label: 'Luftfeuchte', unit: '%' },
      { key: 'co2Ppm', label: 'CO2', unit: 'ppm' },
      { key: 'ppfdMol', label: 'PPFD', unit: 'umol/m2/s' },
      { key: 'heightCm', label: 'Höhe', unit: 'cm' },
    ],
  },
  {
    title: 'Irrigation',
    fields: [
      { key: 'waterAmountMl', label: 'Giessmenge', unit: 'ml' },
      { key: 'runoffAmountMl', label: 'Runoff', unit: 'ml' },
      { key: 'irrigationPh', label: 'Giess-pH', unit: 'pH' },
      { key: 'irrigationEc', label: 'Giess-EC', unit: 'mS/cm' },
      { key: 'drainPh', label: 'Drain-pH', unit: 'pH' },
      { key: 'drainEc', label: 'Drain-EC', unit: 'mS/cm' },
    ],
  },
  {
    title: 'Reservoir',
    fields: [
      { key: 'reservoirPh', label: 'Reservoir-pH', unit: 'pH' },
      { key: 'reservoirEc', label: 'Reservoir-EC', unit: 'mS/cm' },
      { key: 'reservoirWaterTempC', label: 'Wassertemp', unit: '°C' },
      { key: 'reservoirLevelCm', label: 'Level', unit: 'cm' },
      { key: 'reservoirLevelLiters', label: 'Level', unit: 'L' },
      { key: 'topOffLiters', label: 'Top-Off', unit: 'L' },
      { key: 'addbackEc', label: 'Addback-EC', unit: 'mS/cm' },
      { key: 'dissolvedOxygenMgL', label: 'DO', unit: 'mg/L' },
      { key: 'orpMv', label: 'ORP', unit: 'mV' },
    ],
  },
]

function MeasurementEditPage() {
  const { measurementId } = useParams()
  const navigate = useNavigate()
  const [measurement, setMeasurement] = useState<MeasurementDto | null>(null)
  const [grow, setGrow] = useState<GrowDetail | null>(null)
  const [photos, setPhotos] = useState<PhotoAssetDto[]>([])
  const [draft, setDraft] = useState<MeasurementEditState | null>(null)
  const [photoForm, setPhotoForm] = useState<PhotoFormState>({ photoCaption: '', photoTag: 'Overview', useAsReferenceShot: false, source: 'Manual', files: [] })
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [uploading, setUploading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!measurementId) return
    const controller = new AbortController()

    async function load() {
      setLoading(true)
      try {
        const nextMeasurement = await apiFetch<MeasurementDto>(`/api/measurements/${measurementId}`, { signal: controller.signal })
        const [nextGrow, nextPhotos] = await Promise.all([
          apiFetch<GrowDetail>(`/api/grows/${nextMeasurement.growId}`, { signal: controller.signal }),
          apiFetch<PhotoAssetDto[]>(`/api/measurements/${measurementId}/photos`, { signal: controller.signal }),
        ])

        setMeasurement(nextMeasurement)
        setGrow(nextGrow)
        setPhotos(nextPhotos)
        setDraft(createDraft(nextMeasurement))
        setError(null)
      } catch (caught) {
        if (controller.signal.aborted) return
        setError(caught instanceof ApiRequestError ? caught.message : 'Messung konnte nicht geladen werden.')
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }

    void load()
    return () => controller.abort()
  }, [measurementId])

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!measurementId || !draft) return

    // Unlesbares NICHT stillschweigend verschlucken. `zahlOderNull` macht aus
    // „leer" und aus „6,2x" dasselbe `null` — und DIESE Seite ist der einzige
    // Weg, auf dem ein vorhandener Wert verschwinden kann. Auf der Erfassungs-
    // seite war das 2026-08 behoben worden, hier stand es noch.
    const meldung = unlesbarMeldung(unlesbareFelder(ZAHLENFELDER.map(
      ([feld, beschriftung]) => [String(draft[feld] ?? ''), beschriftung] as [string, string])))
    if (meldung) { setError(meldung); return }

    setSaving(true)
    try {
      await apiFetch<MeasurementDto>(`/api/measurements/${measurementId}`, {
        method: 'PUT',
        body: JSON.stringify(toPayload(draft)),
      })
      navigate(grow ? `/grows/${grow.id}` : '/')
    } catch (caught) {
      setError(caught instanceof ApiRequestError ? caught.message : 'Messung konnte nicht gespeichert werden.')
    } finally {
      setSaving(false)
    }
  }

  async function handleDelete() {
    if (!measurementId || !window.confirm('Messung wirklich löschen?')) return

    setDeleting(true)
    try {
      await apiFetch(`/api/measurements/${measurementId}`, { method: 'DELETE' })
      navigate(grow ? `/grows/${grow.id}` : '/')
    } catch (caught) {
      setError(caught instanceof ApiRequestError ? caught.message : 'Messung konnte nicht gelöscht werden.')
    } finally {
      setDeleting(false)
    }
  }

  async function handlePhotoSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!measurementId || photoForm.files.length === 0) {
      setError('Bitte mindestens ein Foto auswählen.')
      return
    }

    setUploading(true)
    try {
      const formData = new FormData()
      formData.append('photoCaption', photoForm.photoCaption)
      formData.append('photoTag', photoForm.photoTag)
      formData.append('useAsReferenceShot', String(photoForm.useAsReferenceShot))
      formData.append('source', photoForm.source)
      for (const file of photoForm.files) {
        formData.append('photos', file)
      }

      await apiFetch(`/api/measurements/${measurementId}/photos`, { method: 'POST', body: formData })
      setPhotos(await apiFetch<PhotoAssetDto[]>(`/api/measurements/${measurementId}/photos`))
      setPhotoForm({ photoCaption: '', photoTag: 'Overview', useAsReferenceShot: false, source: 'Manual', files: [] })
      setError(null)
    } catch (caught) {
      setError(caught instanceof ApiRequestError ? caught.message : 'Fotos konnten nicht gespeichert werden.')
    } finally {
      setUploading(false)
    }
  }

  const backTo = grow ? `/grows/${grow.id}` : '/'

  return (
    <V1Page
      eyebrow="Messung bearbeiten"
      title={grow?.name ?? 'Messung'}
      subtitle={measurement ? `#${measurement.id} · ${formatDateTime(measurement.takenAt)}` : undefined}
      action={<V1LinkButton to={backTo}>Zurück zum Grow</V1LinkButton>}
    >
      {error && <V1Alert title="Fehler" message={error} tone="warn" />}

      {loading || !draft || !measurement ? (
        <V1Skeleton tiles={6} rows={2} label="Lade Messung" />
      ) : (
        <div className="meas-edit">
          <form className="meas-edit__form" onSubmit={handleSubmit}>
            <V1Section title="Basisdaten">
              <div className="v1-form-grid">
                <V1Field label="Zeitpunkt">
                  <input type="datetime-local" value={draft.takenAtLocal} onChange={(event) => setDraft((current) => current ? { ...current, takenAtLocal: event.target.value } : current)} />
                </V1Field>
                <V1Field label="Phase">
                  <select value={draft.stage} onChange={(event) => setDraft((current) => current ? { ...current, stage: event.target.value as GrowStage } : current)}>
                    {PHASEN.map((stage) => <option key={stage} value={stage}>{phaseName(stage)}</option>)}
                  </select>
                </V1Field>
                <V1Field label="Quelle">
                  <select value={draft.source} onChange={(event) => setDraft((current) => current ? { ...current, source: event.target.value as ValueOrigin } : current)}>
                    {sourceOptions.map((source) => <option key={source} value={source}>{herkunftName(source)}</option>)}
                  </select>
                </V1Field>
                <V1Field label="Notiz" wide>
                  <textarea rows={3} value={draft.notes} onChange={(event) => setDraft((current) => current ? { ...current, notes: event.target.value } : current)} />
                </V1Field>
              </div>
              {/* A-006: der Haken „Lösungswechsel" ist kein Weg mehr, einen Wechsel
                  einzutragen — das macht der Wasserwechsel-Ablauf mit Verbrauch und
                  Tagebuch. Steht der Haken an einer älteren Messung (oder an der
                  Messung „nachher" eines Vorgangs), bleibt er sichtbar und zählt weiter. */}
              {measurement?.solutionChange ? (
                <label className="meas-edit__check">
                  <input type="checkbox" checked={draft.solutionChange} onChange={(event) => setDraft((current) => current ? { ...current, solutionChange: event.target.checked } : current)} />
                  <span>Lösungswechsel dokumentiert</span>
                </label>
              ) : (
                <p className="meas-edit__hinweis" data-audit="messung-weg-zum-wasserwechsel">
                  Wasser gewechselt? Das trägst du im <Link to={`/wasserwechsel?growId=${measurement?.growId ?? ''}`}>Wasserwechsel-Ablauf</Link> ein — mit Messung vorher und nachher, Verbrauch und Tagebuch.
                </p>
              )}
            </V1Section>

            {fieldSections.map((section) => (
              <V1Section key={section.title} title={section.title}>
                <div className="v1-form-grid">
                  {section.fields.map((field) => (
                    <V1Field key={field.key} label={field.label} hint={field.unit ?? undefined}>
                      <input
                        inputMode="decimal"
                        value={draft[field.key]}
                        onChange={(event) => setDraft((current) => current ? { ...current, [field.key]: event.target.value } : current)}
                      />
                    </V1Field>
                  ))}
                </div>
              </V1Section>
            ))}

            <div className="v1-form-actions">
              {/* Löschen stand oben in der Kopfzeile, direkt neben „Zurück" — die
                  gefährlichste Schaltfläche der Seite an der Stelle, an der man
                  beim Verlassen hinklickt. Sie steht jetzt am Ende, bei den
                  anderen Entscheidungen über diese Messung. */}
              <V1Button type="button" variant="danger" disabled={deleting} onClick={() => void handleDelete()}>{deleting ? 'Löscht…' : 'Messung löschen'}</V1Button>
              <V1LinkButton to={backTo}>Abbrechen</V1LinkButton>
              <V1Button type="submit" variant="primary" disabled={saving}>{saving ? 'Speichert…' : 'Änderungen speichern'}</V1Button>
            </div>
          </form>

          <aside className="meas-edit__aside">
            <V1Section title="Fotos" action={<V1Badge tone="neutral">{photos.length}</V1Badge>}>
              {photos.length === 0 ? (
                <V1Empty title="Noch keine Fotos" text="An dieser Messung hängt bisher kein Bild." />
              ) : (
                <div className="meas-edit__photos">
                  {photos.map((photo) => (
                    <img key={photo.id} src={photo.relativePath} alt={photo.caption ?? `Foto ${photo.id}`} loading="lazy" />
                  ))}
                </div>
              )}

              <form className="meas-edit__upload" onSubmit={handlePhotoSubmit}>
                <V1Field label="Dateien">
                  <input type="file" accept="image/png,image/jpeg,image/webp" multiple onChange={(event) => setPhotoForm((current) => ({ ...current, files: Array.from(event.target.files ?? []) }))} />
                </V1Field>
                <V1Field label="Art">
                  <select value={photoForm.photoTag} onChange={(event) => setPhotoForm((current) => ({ ...current, photoTag: event.target.value as PhotoTag }))}>
                    {FOTO_TAGS.map((tag) => <option key={tag} value={tag}>{fotoTagName(tag)}</option>)}
                  </select>
                </V1Field>
                <V1Field label="Bildunterschrift">
                  <input value={photoForm.photoCaption} onChange={(event) => setPhotoForm((current) => ({ ...current, photoCaption: event.target.value }))} />
                </V1Field>
                <label className="meas-edit__check">
                  <input type="checkbox" checked={photoForm.useAsReferenceShot} onChange={(event) => setPhotoForm((current) => ({ ...current, useAsReferenceShot: event.target.checked }))} />
                  <span>Als Referenzshot markieren</span>
                </label>
                <V1Button type="submit" variant="primary" disabled={uploading || photoForm.files.length === 0}>
                  {uploading ? 'Lädt hoch…' : photoForm.files.length > 1 ? `${photoForm.files.length} Fotos hochladen` : 'Foto hochladen'}
                </V1Button>
              </form>
            </V1Section>
          </aside>
        </div>
      )}
    </V1Page>
  )
}

function createDraft(measurement: MeasurementDto): MeasurementEditState {
  return {
    takenAtLocal: toLocalInputValue(new Date(measurement.takenAt)),
    stage: measurement.stage,
    source: measurement.source,
    notes: measurement.notes ?? '',
    solutionChange: measurement.solutionChange,
    airTemperatureC: formatDraftNumber(measurement.airTemperatureC),
    humidityPercent: formatDraftNumber(measurement.humidityPercent),
    heightCm: formatDraftNumber(measurement.heightCm),
    waterAmountMl: formatDraftNumber(measurement.waterAmountMl),
    runoffAmountMl: formatDraftNumber(measurement.runoffAmountMl),
    irrigationPh: formatDraftNumber(measurement.irrigationPh),
    irrigationEc: formatDraftNumber(measurement.irrigationEc),
    drainPh: formatDraftNumber(measurement.drainPh),
    drainEc: formatDraftNumber(measurement.drainEc),
    reservoirPh: formatDraftNumber(measurement.reservoirPh),
    reservoirEc: formatDraftNumber(measurement.reservoirEc),
    reservoirWaterTempC: formatDraftNumber(measurement.reservoirWaterTempC),
    reservoirLevelCm: formatDraftNumber(measurement.reservoirLevelCm),
    reservoirLevelLiters: formatDraftNumber(measurement.reservoirLevelLiters),
    dissolvedOxygenMgL: formatDraftNumber(measurement.dissolvedOxygenMgL),
    orpMv: formatDraftNumber(measurement.orpMv),
    topOffLiters: formatDraftNumber(measurement.topOffLiters),
    addbackEc: formatDraftNumber(measurement.addbackEc),
    ppfdMol: formatDraftNumber(measurement.ppfdMol),
    co2Ppm: formatDraftNumber(measurement.co2Ppm),
    airflowAtLeafMPerMin: formatDraftNumber(measurement.airflowAtLeafMPerMin),
    waterFlow: measurement.waterFlow ?? '',
  }
}


/**
 * Die Zahlenfelder dieser Seite mit ihrer Beschriftung.
 *
 * Nur dafür da, ein unlesbares Feld beim Namen nennen zu können — „Reservoir-pH"
 * statt `reservoirPh`. Kommt ein Feld dazu, gehört es hier hinein; fehlt es,
 * wird sein Inhalt weiterhin stillschweigend verworfen. Genau dagegen zählt
 * `src/zahlen-verlust.node.test.ts`.
 */
const ZAHLENFELDER: Array<[keyof MeasurementEditState, string]> = [
  ['airTemperatureC', 'Lufttemperatur'],
  ['humidityPercent', 'Luftfeuchte'],
  ['heightCm', 'Höhe'],
  ['waterAmountMl', 'Gießmenge'],
  ['runoffAmountMl', 'Ablauf'],
  ['irrigationPh', 'pH (Gießwasser)'],
  ['irrigationEc', 'EC (Gießwasser)'],
  ['drainPh', 'pH (Ablauf)'],
  ['drainEc', 'EC (Ablauf)'],
  ['reservoirPh', 'Reservoir-pH'],
  ['reservoirEc', 'Reservoir-EC'],
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
function toPayload(draft: MeasurementEditState): MeasurementUpsertPayload {
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

/**
 * Eine Zahl für ein Eingabefeld.
 *
 * Die Umwandlung steht in <code>zahlenfeld.ts</code> und nur dort — sie stand
 * am 01.09.2026 fünfmal in der Oberfläche, jedes Mal mit
 * <code>String(value)</code> und damit mit englischem Punkt.
 */
function formatDraftNumber(value: number | null | undefined) {
  return feldText(value)
}

// Diese Fassung prüfte mit `Number.isNaN` statt `Number.isFinite` — damit galt
// `Infinity` als gültige Zahl. Und aus „6,2x" wurde stillschweigend `null`,
// obwohl gerade DIESE Seite der einzige Weg ist, auf dem ein vorhandener Wert
// verschwinden kann. Beides erledigt jetzt `src/zahlenfeld.ts`.
function parseNullableNumber(value: string) {
  return zahlOderNull(value)
}

function trimToNull(value: string) {
  const trimmed = value.trim()
  return trimmed ? trimmed : null
}

export default MeasurementEditPage
