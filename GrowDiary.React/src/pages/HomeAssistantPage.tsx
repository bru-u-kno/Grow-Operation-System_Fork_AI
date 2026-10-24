import { useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch, formatApiError } from '../api'
import type { HomeAssistantEntity, HomeAssistantSettingsDto, SensorMetricType, SettingsOverviewDto, TentDto, UpdateTentRequest, UpdateTentSensorRequest } from '../types'
import { V1Alert, V1Button, V1Empty, V1Field, V1Page, V1Skeleton, V1Switch, V1Tabs } from '../components/v1'
import { V1Select } from '../components/V1Select'
import type { V1Option } from '../components/V1Select'
import { toNullableString } from '../components/v1-utils'
import { resolveUrl } from '../base'
import { haWert } from '../utils'
import { maschinenZahl } from '../zahlenfeld'
import { definitions, entityOptionLabel, groups, messgroessenOptionen, RAUMWERTE_METRIKEN, vpdKpa } from '../features/home-assistant/messgroessen'
import type { RaumStand } from '../features/steuerung/steuerung-typen'
import { vorbereiten } from '../features/geraete/entitaets-passung'
import type { EntityDefinition } from '../features/home-assistant/messgroessen'
import '../features/home-assistant/home-assistant.css'

type GroupKey = 'tent' | 'reservoir' | 'hardware'
type SensorDraft = { metricType: SensorMetricType; haEntityId: string; displayLabel: string; isActive: boolean }
type TentMappingDraft = { cameras: string[]; sensors: SensorDraft[] }
type SavingState = 'ha' | `tent-${number}` | null
/**
 * Home Assistant nach dem Entwurf: Status-Leiste oben, dann Sensor-Mapping
 * als Zeilen (Metrik ← Entity, Live-Wert rechts) und das Koppel-Panel mit
 * QR-Code. Eine Zeile ist aktiv, sobald eine Entity eingetragen ist —
 * Feld leeren schaltet sie ab.
 */
function HomeAssistantPage() {
  const [ha, setHa] = useState<HomeAssistantSettingsDto>({ baseUrl: '', accessToken: '', enabled: false })
  const [entities, setEntities] = useState<HomeAssistantEntity[]>([])
  const [entitiesLoadedAt, setEntitiesLoadedAt] = useState<Date | null>(null)
  const [tents, setTents] = useState<TentDto[]>([])
  const [raum, setRaum] = useState<RaumStand | null>(null)
  const [drafts, setDrafts] = useState<Record<number, TentMappingDraft>>({})
  const [selectedTentId, setSelectedTentId] = useState<number | null>(null)
  const [activeGroup, setActiveGroup] = useState<GroupKey>('tent')
  const [showToken, setShowToken] = useState(false)
  const [previewNonce, setPreviewNonce] = useState(0)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState<SavingState>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    async function load() {
      setLoading(true)
      setError(null)
      try {
        const overview = await apiFetch<SettingsOverviewDto>('/api/settings', { signal: controller.signal })
        if (controller.signal.aborted) return
        const sorted = [...overview.tents].sort((a, b) => scoreTent(b) - scoreTent(a) || a.displayOrder - b.displayOrder || a.name.localeCompare(b.name))
        setHa(overview.homeAssistant)
        setTents(sorted)
        setDrafts(Object.fromEntries(sorted.map((tent) => [tent.id, createTentDraft(tent)])))
        setSelectedTentId((current) => current ?? sorted[0]?.id ?? null)

        // cc.263: Steht ein Zelt im Raum, kommen dessen drei Raumluft-Werte vom Raum — die Zeilen entfallen dann am Zelt.
        const raumStand = await apiFetch<RaumStand>('/api/steuerung/raum', { signal: controller.signal }).catch(() => null)
        if (!controller.signal.aborted) setRaum(raumStand)

        // Best-effort: load live entities so the mapping can use a dropdown.
        // Returns [] when Home Assistant is unreachable or not configured.
        const entityList = await apiFetch<HomeAssistantEntity[]>('/api/home-assistant/entities', { signal: controller.signal }).catch(() => [])
        if (!controller.signal.aborted) {
          setEntities(entityList)
          if (entityList.length > 0) setEntitiesLoadedAt(new Date())
        }
      } catch (caught) {
        if (!controller.signal.aborted) setError(formatApiError(caught, 'Home Assistant konnte nicht geladen werden.'))
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }
    void load()
    return () => controller.abort()
  }, [])

  const vorbereitet = useMemo(() => entities.map(vorbereiten), [entities])
  const selectedTent = useMemo(() => tents.find((tent) => tent.id === selectedTentId) ?? tents[0] ?? null, [selectedTentId, tents])
  const selectedDraft = selectedTent ? drafts[selectedTent.id] : null
  const mappedCount = selectedDraft ? selectedDraft.sensors.filter((sensor) => sensor.haEntityId.trim()).length : 0
  // Die Raumluft-Werte des gewählten Zelts kommen vom Raum (Zelt steht darin, beide Raum-Fühler gesetzt).
  const raumWerte = selectedTent?.raumId && raum?.aussenWerte && raum.fuehler ? raum : null
  const cameraCount = selectedDraft ? selectedDraft.cameras.filter((camera) => camera.trim()).length : 0
  const connected = ha.isManagedByAddon || ha.enabled
  // Prefer camera.* entities but fall back to the full list so the picker is never
  // empty when HA exposes the camera under a different domain (e.g. image.*).
  const cameraEntities = useMemo(() => {
    const cams = entities.filter((entity) => entity.domain === 'camera' || entity.domain === 'image')
    return cams.length > 0 ? cams : entities
  }, [entities])

  function liveValue(entityId: string): string | null {
    if (!entityId.trim()) return null
    const entity = entities.find((item) => item.entityId === entityId.trim())
    if (!entity) return null
    return haWert(entity.state, entity.unitOfMeasurement)
  }

  async function saveConnection(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSaving('ha')
    setError(null)
    setMessage(null)
    try {
      const saved = await apiFetch<HomeAssistantSettingsDto>('/api/settings/home-assistant', { method: 'PUT', body: JSON.stringify({ baseUrl: toNullableString(ha.baseUrl), accessToken: toNullableString(ha.accessToken), enabled: ha.enabled }) })
      setHa(saved)
      setMessage('Home-Assistant-Verbindung gespeichert.')
    } catch (caught) {
      setError(formatApiError(caught, 'Home Assistant konnte nicht gespeichert werden.'))
    } finally {
      setSaving(null)
    }
  }

  async function saveSelectedTent() {
    if (!selectedTent || !selectedDraft) return
    setSaving(`tent-${selectedTent.id}`)
    setError(null)
    setMessage(null)
    try {
      const saved = await apiFetch<TentDto>(`/api/settings/tents/${selectedTent.id}`, { method: 'PUT', body: JSON.stringify(toUpdateTentRequest(selectedTent, selectedDraft)) })
      setTents((current) => current.map((tent) => tent.id === saved.id ? saved : tent))
      setDrafts((current) => ({ ...current, [saved.id]: createTentDraft(saved) }))
      setMessage(`${saved.name} gespeichert.`)
    } catch (caught) {
      setError(formatApiError(caught, 'Die Zuordnung der Entitäten konnte nicht gespeichert werden.'))
    } finally {
      setSaving(null)
    }
  }

  function mutateCameras(mutate: (cameras: string[]) => string[]) {
    if (!selectedTent) return
    setDrafts((current) => ({ ...current, [selectedTent.id]: { ...current[selectedTent.id], cameras: mutate(current[selectedTent.id].cameras) } }))
  }

  const addCamera = () => mutateCameras((cameras) => [...cameras, ''])
  const updateCameraAt = (index: number, value: string) => mutateCameras((cameras) => cameras.map((camera, i) => (i === index ? value : camera)))
  const removeCameraAt = (index: number) => mutateCameras((cameras) => cameras.filter((_, i) => i !== index))

  function updateSensor(metricType: SensorMetricType, patch: Partial<SensorDraft>) {
    if (!selectedTent) return
    setDrafts((current) => ({ ...current, [selectedTent.id]: { ...current[selectedTent.id], sensors: current[selectedTent.id].sensors.map((sensor) => sensor.metricType === metricType ? { ...sensor, ...patch } : sensor) } }))
  }

  return (
    <V1Page
      eyebrow="Einrichtung / Home Assistant"
      title="Home Assistant"
      subtitle="Hier legst du fest, welche Entität welchen Messwert liefert — das ist die einzige Stelle dafür. Die Geräte selbst, mit Wartung und Kalibrierung, stehen unter Sensoren &amp; Wartung."
    >
      {error && <V1Alert message={error} tone="warn" />}
      {message && <V1Alert message={message} tone="ok" />}

      {loading ? <V1Skeleton rows={4} label="Lade Home Assistant" /> : (
        <>
          <div className="co-strip" data-audit="ha-status-strip">
            <div className="co-cell">
              <div className="co-cell-label">Verbindung</div>
              <div className={`co-cell-value is-md${connected && entities.length > 0 ? ' is-good' : connected ? ' is-warn' : ''}`}>
                {ha.isManagedByAddon ? 'Add-on · verbunden' : connected ? (entities.length > 0 ? 'verbunden' : 'keine Antwort') : 'inaktiv'}
              </div>
            </div>
            <div className="co-cell">
              <div className="co-cell-label">Entitäten zugeordnet</div>
              <div className="co-cell-value is-md">{mappedCount} von {definitions.length}</div>
            </div>
            <div className="co-cell">
              <div className="co-cell-label">Kameras</div>
              <div className="co-cell-value is-md">{cameraCount > 0 ? String(cameraCount) : '—'}</div>
            </div>
            <div className="co-cell">
              <div className="co-cell-label">Entitäten geladen</div>
              <div className="co-cell-value is-md">{entitiesLoadedAt ? `${entities.length} · ${new Intl.DateTimeFormat('de-DE', { hour: '2-digit', minute: '2-digit' }).format(entitiesLoadedAt)}` : 'keine'}</div>
            </div>
          </div>

          {!ha.isManagedByAddon && (
            <section className="ls-panel">
              <div className="ls-panel-head"><span className="ls-label">Verbindung</span></div>
              <form className="ha-connect" data-audit="ha-connection-layout" onSubmit={(event) => void saveConnection(event)}>
                <V1Field label="Home Assistant URL" hint="Beispiel: http://homeassistant.local:8123">
                  <input value={ha.baseUrl ?? ''} onChange={(event) => setHa((current) => ({ ...current, baseUrl: event.target.value }))} placeholder="http://homeassistant.local:8123" />
                </V1Field>
                <V1Field label="Long-Lived Access Token">
                  <div className="v1-inline-input">
                    <input type={showToken ? 'text' : 'password'} value={ha.accessToken ?? ''} onChange={(event) => setHa((current) => ({ ...current, accessToken: event.target.value }))} autoComplete="off" />
                    <V1Button onClick={() => setShowToken((current) => !current)}>{showToken ? 'Verbergen' : 'Anzeigen'}</V1Button>
                  </div>
                </V1Field>
                <div className="co-actions" data-audit="ha-connection-actions">
                  <V1Switch label="Home Assistant aktiv" checked={ha.enabled} onChange={(checked) => setHa((current) => ({ ...current, enabled: checked }))} />
                  <V1Button type="submit" variant="primary" disabled={saving === 'ha'}>{saving === 'ha' ? 'Speichert…' : 'Verbindung speichern'}</V1Button>
                </div>
              </form>
            </section>
          )}

          {tents.length === 0 ? (
            <V1Empty title="Kein Zelt angelegt" action={<Link to="/zelte" className="ls-btn is-primary">Zelt anlegen</Link>} />
          ) : selectedTent && selectedDraft && (
            <div className="ha-layout">
              <section className="ls-panel" data-audit="ha-mapping-panel">
                <div className="ls-panel-head">
                  <span className="ls-label">Sensor-Mapping</span>
                  <span className="ls-panel-meta">Messgröße ← HA-Entität, je Zelt</span>
                  <button type="button" className="ls-btn is-small is-primary" disabled={saving === `tent-${selectedTent.id}`} onClick={() => void saveSelectedTent()}>
                    {saving === `tent-${selectedTent.id}` ? 'Speichert…' : 'Speichern'}
                  </button>
                </div>

                {tents.length > 1 && (
                  <div className="ha-tent-row">
                    <V1Tabs label="Zelt" active={selectedTent.id} onChange={(id) => setSelectedTentId(id)} items={tents.map((tent) => ({ value: tent.id, label: tent.name }))} />
                  </div>
                )}

                <div className="ha-tent-row co-chips" role="tablist" aria-label="Entitätsgruppe">
                  {groups.map((group) => (
                    <button key={group.key} type="button" role="tab" aria-selected={group.key === activeGroup} className={group.key === activeGroup ? 'co-chip active' : 'co-chip'} onClick={() => setActiveGroup(group.key)}>
                      {group.label}
                    </button>
                  ))}
                </div>

                {definitions.filter((definition) => definition.group === activeGroup && !(raumWerte && RAUMWERTE_METRIKEN.includes(definition.metricType))).map((definition) => {
                  const sensor = selectedDraft.sensors.find((item) => item.metricType === definition.metricType) ?? createSensorDraft(definition)
                  const live = liveValue(sensor.haEntityId)
                  const auswahl = messgroessenOptionen(vorbereitet, definition)
                  return (
                    <div key={definition.metricType} className="co-row" data-audit="ha-entity-row">
                      <span className="ha-metric">{definition.label}{definition.unit ? <small> {definition.unit}</small> : null}</span>
                      {/* forkai.38: Auswahl im Blatt statt Eingabefeld mit Vorschlagsliste —
                          bei dreihundert Entitaeten ist Suchen der Normalfall. */}
                      <V1Select
                        label={definition.label}
                        titel={definition.label}
                        unterzeile={definition.placeholder}
                        wert={sensor.haEntityId}
                        platzhalter={definition.placeholder}
                        onWahl={(wert) => updateSensor(definition.metricType, { haEntityId: wert })}
                        optionen={[
                          { wert: '', text: '— nicht zugeordnet —', betont: true },
                          ...auswahl.optionen,
                        ]}
                        hinweisOben={auswahl.leerHinweis}
                      />
                      {live != null
                        ? <span className="co-row-value is-good">{live}</span>
                        : sensor.haEntityId.trim() === ''
                          ? <span className="co-row-value is-faint">nicht gemappt</span>
                          : <span className="co-row-value">gemappt</span>}
                      {definition.hinweis && <span className="ha-metric-hinweis">{definition.hinweis}</span>}
                    </div>
                  )
                })}

                {activeGroup === 'tent' && raumWerte?.fuehler && (() => {
                  const temp = entities.find((item) => item.entityId === raumWerte.fuehler?.tempEntityId.trim())
                  const rh = entities.find((item) => item.entityId === raumWerte.fuehler?.rhEntityId.trim())
                  const t = maschinenZahl(temp?.state) ?? NaN
                  const f = maschinenZahl(rh?.state) ?? NaN
                  const vpd = Number.isFinite(t) && Number.isFinite(f) ? vpdKpa(t, f) : null
                  const werte = [temp ? haWert(temp.state, temp.unitOfMeasurement) : null, rh ? haWert(rh.state, rh.unitOfMeasurement) : null, vpd != null ? haWert(String(vpd), 'kPa') : null]
                  return (
                    <div className="co-row" data-audit="ha-raumwerte-row">
                      <span className="ha-metric">{raumWerte.name} Temp. / RLF / VPD</span>
                      <div className="v1-field v1-select">
                        <span>Raumwerte</span>
                        <button type="button" className="v1-select-feld" disabled aria-label={`${raumWerte.name} Temp., RLF und VPD kommen vom Raum`}>
                          <span>Vom Raum „{raumWerte.name}“ · <Link to="/geraete?reiter=raeume">Geräte › Räume</Link></span>
                        </button>
                      </div>
                      {werte.every((w) => w != null)
                        ? <span className="co-row-value is-good">{werte.join(' · ')}</span>
                        : <span className="co-row-value is-faint">kein Wert</span>}
                    </div>
                  )
                })()}

                {activeGroup === 'tent' && (
                  <>
                    {selectedDraft.cameras.map((camera, index) => (
                      <div key={index} className="co-row" data-audit="ha-camera-field-action">
                        <span className="ha-metric">Kamera {index + 1}</span>
                        <V1Select
                          label={`Kamera ${index + 1}`}
                          titel={`Kamera ${index + 1}`}
                          wert={camera}
                          platzhalter="camera.hauptzelt"
                          onWahl={(wert) => updateCameraAt(index, wert)}
                          optionen={cameraEntities.map((entity): V1Option => ({
                            wert: entity.entityId,
                            text: entity.friendlyName ?? entity.entityId,
                            hinweis: entityOptionLabel(entity),
                          }))}
                        />
                        <button type="button" className="ls-btn is-small" onClick={() => removeCameraAt(index)}>Entfernen</button>
                      </div>
                    ))}
                    <div className="co-row">
                      <div className="co-actions">
                        <button type="button" className="ls-btn is-small" onClick={addCamera}>+ Kamera</button>
                        <button type="button" className="ls-btn is-small" onClick={() => setPreviewNonce(Date.now())}>Snapshot-Test</button>
                      </div>
                    </div>
                    {previewNonce > 0 && (() => {
                      const saved = (selectedTent.cameras ?? []).filter((camera) => camera.trim())
                      const previewCameras = saved.length > 0 ? saved : selectedDraft.cameras.map((camera) => camera.trim()).filter(Boolean)
                      return previewCameras.length === 0 ? (
                        <div className="ls-panel-body"><p>Trag zuerst mindestens eine Kamera ein.</p></div>
                      ) : (
                        <div className="ha-previews">
                          {previewCameras.map((camera, index) => (
                            <CameraPreview key={`${camera}-${previewNonce}`} tentId={selectedTent.id} entity={camera} index={index} nonce={previewNonce} />
                          ))}
                          {saved.length === 0 && <p className="gc-facts">Neu hinzugefügte Kameras erst nach „Speichern" testbar.</p>}
                        </div>
                      )
                    })()}
                  </>
                )}
              </section>

              {/* Der QR-Code wohnt jetzt unter „Handy › App einrichten". Hier stand
                  eine zweite Fassung, die auf die Ingress-Adresse mit Token
                  zeigte — die stirbt beim naechsten Aufruf. */}
              <section className="ls-panel">
                <div className="ls-panel-head"><span className="ls-label">Aufs Handy</span></div>
                <p className="ha-pair-text" style={{ margin: '0 0 10px' }}>
                  Cultivate Control als Kachel auf den Startbildschirm des Handys — mit QR-Code und Anleitung.
                </p>
                <Link to="/handy?tab=app" className="ls-btn is-small">Handy › App einrichten</Link>
              </section>
            </div>
          )}
        </>
      )}
    </V1Page>
  )
}

function scoreTent(tent: TentDto) {
  return (tent.activeGrowCount > 0 ? 100 : 0) + (tent.tentType === 'Production' ? 10 : 0)
}

function createTentDraft(tent: TentDto): TentMappingDraft {
  return { cameras: tent.cameras && tent.cameras.length > 0 ? [...tent.cameras] : (tent.cameraEntityId ? [tent.cameraEntityId] : []), sensors: definitions.map((definition) => {
    const existing = tent.sensors.find((sensor) => sensor.metricType === definition.metricType)
    return { metricType: definition.metricType, haEntityId: existing?.haEntityId ?? '', displayLabel: existing?.displayLabel ?? definition.label, isActive: existing?.isActive ?? false }
  }) }
}

function createSensorDraft(definition: EntityDefinition): SensorDraft {
  return { metricType: definition.metricType, haEntityId: '', displayLabel: definition.label, isActive: false }
}

function toUpdateTentRequest(tent: TentDto, draft: TentMappingDraft): UpdateTentRequest {
  // Aktiv ist, was eine Entity hat — der Entwurf kennt keinen separaten Schalter.
  const sensors: UpdateTentSensorRequest[] = draft.sensors.map((sensor) => ({ id: tent.sensors.find((existing) => existing.metricType === sensor.metricType)?.id ?? 0, metricType: sensor.metricType, haEntityId: toNullableString(sensor.haEntityId), displayLabel: toNullableString(sensor.displayLabel), isActive: sensor.haEntityId.trim().length > 0 }))
  return { name: tent.name, status: tent.status, kind: tent.kind, tentType: tent.tentType, notes: tent.notes, displayOrder: tent.displayOrder, accentColor: tent.accentColor, widthCm: tent.widthCm, depthCm: tent.depthCm, tentHeightCm: tent.tentHeightCm, lightType: tent.lightType, lightWatt: tent.lightWatt, lightController: tent.lightController, lightControllerEntityId: tent.lightControllerEntityId, exhaustFanCount: tent.exhaustFanCount, exhaustM3h: tent.exhaustM3h, circulationFanCount: tent.circulationFanCount, hvacController: tent.hvacController, hvacControllerEntityId: tent.hvacControllerEntityId, co2Available: tent.co2Available, hasCo2Enrichment: tent.hasCo2Enrichment, raumId: tent.raumId, cameraEntityId: null, cameras: draft.cameras.map((camera) => camera.trim()).filter((camera) => camera.length > 0), sensors }
}


function cameraLabel(entity: string, index: number): string {
  const short = entity.replace(/^(camera|image)\./i, '').replace(/[_-]+/g, ' ').trim()
  return short ? `Kamera ${index + 1} · ${short}` : `Kamera ${index + 1}`
}

// A live snapshot for one specific camera entity, with its own loading/error state so a
// broken camera doesn't hide the others. Used to test every mapped camera at once.
function CameraPreview({ tentId, entity, index, nonce }: { tentId: number; entity: string; index: number; nonce: number }) {
  const [state, setState] = useState<'loading' | 'ok' | 'error'>('loading')
  const label = cameraLabel(entity, index)
  return (
    <figure className="ha-preview">
      <div className="ha-preview-frame">
        <img
          src={resolveUrl(`/api/live/tents/${tentId}/camera?entity=${encodeURIComponent(entity)}&t=${nonce}`)}
          alt={label}
          style={{ display: state === 'ok' ? 'block' : 'none' }}
          onLoad={() => setState('ok')}
          onError={() => setState('error')}
        />
        {state === 'loading' && <span>Lädt…</span>}
        {state === 'error' && <span>Kein Bild — in Home Assistant erreichbar?</span>}
      </div>
      <figcaption>{label}</figcaption>
    </figure>
  )
}

export default HomeAssistantPage
