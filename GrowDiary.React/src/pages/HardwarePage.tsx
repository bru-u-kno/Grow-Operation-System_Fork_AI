import { useEffect, useMemo, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch, ApiRequestError, formatApiError } from '../api'
import type { CalibrationEventDto, CreateHardwareItemRequest, HardwareDeviceKind, HardwareItemCriticality, HardwareItemDto, HardwareItemStatus, HomeAssistantEntity, HydroSetupDto, MaintenanceEventDto, TentDto, UpdateHardwareItemRequest, WearTemplateDto } from '../types'
import { V1Alert, V1Badge, V1Button, V1Card, V1Empty, V1Field, V1LinkButton, V1Page, V1Section, V1Skeleton } from '../components/v1'
import { classNames, formatSeverityLabel, haWert } from '../utils'
import { geraeteStatusName } from '../deutsche-woerter'
import type { HardwareFilter, HardwareRow } from '../features/hardware/hardware-table-model'
import { buildHardwareRows, countBy, dueLabel, filterHardwareRows, statusLabel, statusTone } from '../features/hardware/hardware-table-model'
import {
  speicherbarePunkte, steilheitProzent, steilheitSatz, vorbelegung,
  type PunktZeile,
} from '../features/hardware/kalibrierpunkte'
import '../features/hardware/hardware.css'

const FILTERS: Array<{ value: HardwareFilter; label: string }> = [
  { value: 'alle', label: 'Alle' },
  { value: 'problem', label: 'Braucht Aufmerksamkeit' },
  { value: 'sensoren', label: 'Sensoren' },
  { value: 'geraete', label: 'Technik' },
  { value: 'pflege', label: 'Pflege geplant' },
]

/** Der Beleg für „hab ich gemacht" — bei Wartung bleiben die Messfelder leer. */
type CareDraft = {
  eventId: number
  kind: 'Wartung' | 'Kalibrierung'
  geraet: string
  titel: string
  datum: string
  /**
   * Die einzelnen Abgleiche — pH 4 und pH 7, oft auch 10.
   *
   * Vorher stand hier <b>ein</b> Feldpaar. Ein einzelner Abgleich gegen pH 7,00
   * sagt über die Sonde nichts: eine tote Sonde lässt sich darauf genauso
   * einstellen wie eine frische. Erst der Abstand zwischen zwei Punkten
   * verrät, ob sie noch spreizt.
   */
  punkte: PunktZeile[]
  notiz: string
  problem: boolean
}

type HardwareDraft = {
  name: string
  category: string
  deviceKind: HardwareDeviceKind
  status: HardwareItemStatus
  criticality: HardwareItemCriticality
  tentId: string
  hydroSetupId: string
  manufacturer: string
  model: string
  serialNumber: string
  calibrationIntervalDays: string
  notes: string
  wearTemplateId: string
  expectedLifespanDays: string
  inspectionIntervalDays: string
}

const criticalityOptions: HardwareItemCriticality[] = ['Low', 'Medium', 'High', 'Critical']
const statusOptions: HardwareItemStatus[] = ['Active', 'Offline', 'MaintenanceDue', 'Retired']

// Only manually creatable kinds. Fixed sensors are never created here — they appear
// automatically from the Home Assistant mapping.
const deviceKindOptions: Array<{ value: HardwareDeviceKind; label: string }> = [
  { value: 'HandheldMeter', label: 'Messgerät (mobil, ohne HA)' },
  { value: 'Equipment', label: 'Gerät (Pumpe, Chiller, USV …)' },
]

function deviceKindLabel(kind: HardwareDeviceKind | null | undefined): string | null {
  switch (kind) {
    case 'FixedSensor': return 'HA-Sensor'
    case 'HandheldMeter': return 'Messgerät'
    case 'Equipment': return 'Gerät'
    default: return null
  }
}

function HardwarePage() {
  const [hardware, setHardware] = useState<HardwareItemDto[]>([])
  const [tents, setTents] = useState<TentDto[]>([])
  const [hydroSetups, setHydroSetups] = useState<HydroSetupDto[]>([])
  const [maintenance, setMaintenance] = useState<MaintenanceEventDto[]>([])
  const [entities, setEntities] = useState<HomeAssistantEntity[]>([])
  const [calibration, setCalibration] = useState<CalibrationEventDto[]>([])
  const [wearTemplates, setWearTemplates] = useState<WearTemplateDto[]>([])
  const [filter, setFilter] = useState<HardwareFilter>('alle')
  const [formOpen, setFormOpen] = useState(false)

  // Das Formular steht UNTER der Geraeteliste. Bei zwei Geraeten faellt das
  // nicht auf, bei sieben schon: der Knopf „Bearbeiten" oeffnet es dann
  // ausserhalb des Sichtbaren, der Scrollstand bleibt 0 — und fuer den
  // Nutzer „reagiert der Knopf nicht". Gemessen am laufenden Stand: Formular
  // bei y = 721 in einem 600 px hohen Fenster, nichts scrollte.
  //
  // Deshalb hinscrollen, sobald es aufgeht. `block: start` und nicht
  // `center`: die Ueberschrift „Sensor oder Geraet bearbeiten" ist die
  // Rueckmeldung, dass der Klick angekommen ist.
  //
  // Nur `formOpen` in der Abhaengigkeitsliste: `editingId` wird weiter unten
  // deklariert, und ein Wechsel von einem Geraet zum naechsten scrollt ohnehin
  // nicht weg — das Formular steht dann schon da.
  // Ein ZAEHLER, nicht `formOpen`.
  //
  // Die erste Fassung hing an `formOpen` — und beim zweiten Klick ist das schon
  // `true`, der Effekt lief also nicht mehr. Wer eine Zeile bearbeitet, hoch
  // scrollt und die naechste anklickt, bekam den Inhalt gewechselt und sonst
  // nichts: das Formular blieb unten ausserhalb des Bildes stehen.
  //
  // Der Tester hat genau das als Video geschickt, NACHDEM der erste Klick
  // repariert war. Mein Kommentar an dieser Stelle behauptete sogar, ein
  // Wechsel scrolle „ohnehin nicht weg" — geprueft hatte ich nur den ersten
  // Klick.
  //
  // Ein Zaehler feuert bei jedem Aufruf, unabhaengig davon, ob das Formular
  // schon offen war.
  const formularRef = useRef<HTMLDivElement | null>(null)
  const [hinScrollen, setHinScrollen] = useState(0)

  useEffect(() => {
    if (hinScrollen === 0) return
    formularRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }, [hinScrollen])
  const [draft, setDraft] = useState<HardwareDraft>(() => createDraft())
  const [editingId, setEditingId] = useState<number | null>(null)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  // Der offene „hab ich gemacht"-Beleg: null = keiner.
  const [careDraft, setCareDraft] = useState<CareDraft | null>(null)

  // Das Formular steht an anderer Stelle als der Knopf, der es oeffnet. Bei
  // wenigen Zeilen faellt das nicht auf, bei vielen schon: dann geht es
  // ausserhalb des Sichtbaren auf, nichts scrollt hin, und fuer den Nutzer
  // „reagiert der Knopf nicht". Genau so kam die Meldung zu /sensoren.
  //
  // `block: start`, nicht `center`: die Ueberschrift ist die Rueckmeldung,
  // dass der Klick angekommen ist.
  const pflegeRef = useRef<HTMLDivElement | null>(null)

  useEffect(() => {
    if (!careDraft) return
    pflegeRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }, [careDraft])

  useEffect(() => { void load() }, [])

  /**
   * Einen falsch eingetragenen Termin entfernen.
   *
   * Ein Protokoll ist erst eines, wenn es stimmt: bleibt ein Vertipper stehen,
   * rechnet der Faelligkeits-Waechter mit einem Datum, das nie stattgefunden
   * hat, und meldet die naechste Kalibrierung zu spaet. Bis zum 25.08.2026 gab
   * es diesen Weg nur in der API.
   */
  async function terminEntfernen(row: HardwareRow) {
    if (!row.nextCare) return
    if (!window.confirm(`Termin „${row.nextCare.title}" wirklich entfernen?`)) return
    setMessage(null)
    setError(null)
    try {
      // Beide Wege ausgeschrieben, nicht zusammengesetzt: ein Pfad aus
      // Bausteinen ist fuer die Zaehlung loeschwege-erreichbar.node.test.ts
      // unsichtbar — und ein Leser sieht so auch, welche Endpunkte es gibt.
      if (row.nextCare.kind === 'Kalibrierung') {
        await apiFetch(`/api/calibration-events/${row.nextCare.eventId}`, { method: 'DELETE' })
      } else {
        await apiFetch(`/api/maintenance-events/${row.nextCare.eventId}`, { method: 'DELETE' })
      }
      setMessage('Termin entfernt.')
      await load()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Termin konnte nicht entfernt werden.')
    }
  }

  function openCare(row: HardwareRow) {
    if (!row.nextCare) return
    setMessage(null)
    setError(null)
    setCareDraft({
      eventId: row.nextCare.eventId,
      kind: row.nextCare.kind,
      geraet: row.item.name,
      titel: row.nextCare.title,
      datum: new Date().toISOString().slice(0, 10),
      punkte: vorbelegung(row.nextCare.title + ' ' + row.item.name),
      notiz: '',
      problem: false,
    })
  }

  /**
   * Den Termin abschliessen — und damit den naechsten planen.
   *
   * Der Folgetermin entsteht im Backend (`/complete`), nicht hier: nur dort
   * ist bekannt, welches Intervall am Geraet haengt, und nur dort laesst sich
   * verhindern, dass zwei Erinnerungen fuer denselben Termin entstehen.
   */
  /* Die Steilheit schon beim Tippen: der Nutzer soll nicht erst nach dem
     Speichern erfahren, dass seine Sonde faellig ist. Gerechnet wird sie hier
     UND im Backend; beide Seiten pruefen denselben ausgerechneten Fall
     (89,3 %), damit sie nicht auseinanderlaufen. */
  const steilheit = careDraft ? steilheitProzent(speicherbarePunkte(careDraft.punkte)) : null

  /** Eine Zeile der Punkte-Tabelle ändern. */
  function patchPunkt(index: number, teil: Partial<PunktZeile>) {
    setCareDraft((current) => current && ({
      ...current,
      punkte: current.punkte.map((p, i) => (i === index ? { ...p, ...teil } : p)),
    }))
  }

  async function saveCare() {
    if (!careDraft) return
    setSaving('care')
    setError(null)
    try {
      const pfad = careDraft.kind === 'Kalibrierung' ? 'calibration-events' : 'maintenance-events'
      const koerper = careDraft.kind === 'Kalibrierung'
        ? {
            performedAtUtc: new Date(careDraft.datum).toISOString(),
            /* Die Punkte gehen als JSON mit; der Server zieht daraus die
               Zusammenfassung (Referenz, vorher, nachher) und rechnet die
               Steilheit. Zwei Wege zu derselben Zahl waeren zwei Wahrheiten. */
            pointsJson: JSON.stringify(speicherbarePunkte(careDraft.punkte)),
            notes: careDraft.notiz.trim() || null,
            failed: careDraft.problem,
          }
        : {
            performedAtUtc: new Date(careDraft.datum).toISOString(),
            notes: careDraft.notiz.trim() || null,
            actionNeeded: careDraft.problem,
          }

      await apiFetch(`/api/${pfad}/${careDraft.eventId}/complete`, { method: 'POST', body: JSON.stringify(koerper) })
      setCareDraft(null)
      setMessage(`${careDraft.kind} für „${careDraft.geraet}“ eingetragen — der nächste Termin steht.`)
      await load()
    } catch (caught) {
      setError(formatApiError(caught, 'Konnte nicht eingetragen werden.'))
    } finally {
      setSaving(null)
    }
  }

  /**
   * Eine Vorlage wählen füllt Lebensdauer und Prüfintervall.
   *
   * Warum das nötig war: die zwölf Verschleiß-Vorlagen gab es seit jeher im
   * Wissen — nur bot das Formular sie nie an. Also blieb bei jedem angelegten
   * Gerät die Lebensdauer leer, und die Wartungserinnerung, die daran hängt,
   * meldete sich nie. Eine UV-C-Lampe leuchtet nach 9000 Stunden weiter und
   * klärt trotzdem nicht mehr; genau so ein Fall fällt ohne Erinnerung niemandem
   * auf.
   *
   * Der Name wird nur übernommen, wenn das Feld leer ist — wer seiner Sonde
   * schon einen Namen gegeben hat, soll ihn nicht durch die Auswahl verlieren.
   */
  function waehleVorlage(templateId: string) {
    const template = wearTemplates.find((item) => item.id === templateId)
    setDraft((current) => ({
      ...current,
      wearTemplateId: templateId,
      name: current.name.trim() === '' && template ? template.name : current.name,
      category: template ? template.category : current.category,
      expectedLifespanDays: template ? String(template.expectedLifespanDays) : current.expectedLifespanDays,
      inspectionIntervalDays: template?.inspectionIntervalDays != null
        ? String(template.inspectionIntervalDays)
        : current.inspectionIntervalDays,
    }))
  }

  async function load() {
    setLoading(true)
    setError(null)
    try {
      const dueBeforeUtc = new Date(Date.now() + 14 * 24 * 60 * 60 * 1000).toISOString()
      const [items, tentData, hydroData, maintenanceData, calibrationData, entityData, wearData] = await Promise.all([
        apiFetch<HardwareItemDto[]>('/api/hardware-items'),
        apiFetch<TentDto[]>('/api/settings/tents'),
        apiFetch<HydroSetupDto[]>('/api/hydro-setups?includeArchived=true'),
        apiFetch<MaintenanceEventDto[]>(`/api/maintenance-events?dueBeforeUtc=${encodeURIComponent(dueBeforeUtc)}`).catch(() => []),
        apiFetch<CalibrationEventDto[]>(`/api/calibration-events?dueBeforeUtc=${encodeURIComponent(dueBeforeUtc)}`).catch(() => []),
        apiFetch<HomeAssistantEntity[]>('/api/home-assistant/entities').catch(() => [] as HomeAssistantEntity[]),
        apiFetch<WearTemplateDto[]>('/api/knowledge/wear').catch(() => [] as WearTemplateDto[]),
      ])
      setHardware(items)
      setTents(tentData)
      setHydroSetups(hydroData)
      setMaintenance(maintenanceData)
      setCalibration(calibrationData)
      setEntities(entityData)
      setWearTemplates(wearData)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Sensoren konnten nicht geladen werden.')
    } finally {
      setLoading(false)
    }
  }

  const sensors = useMemo(() => hardware.filter((item) => isSensorLike(item)), [hardware])
  const offline = sensors.filter((item) => item.status === 'Offline' || item.status === 'Retired').length
  const plannedMaintenance = maintenance.filter((event) => event.status === 'Planned')
  const plannedCalibration = calibration.filter((event) => event.status === 'Planned')

  const rows = useMemo(
    () => buildHardwareRows(hardware, tents, maintenance, calibration),
    [hardware, tents, maintenance, calibration],
  )
  const counts = useMemo(() => countBy(rows), [rows])
  const visibleRows = useMemo(() => filterHardwareRows(rows, filter), [rows, filter])

  function startEdit(item: HardwareItemDto) {
    setEditingId(item.id)
    setDraft(createDraft(item))
    setFormOpen(true)
    setHinScrollen((n) => n + 1)
    setError(null)
    setMessage(null)
  }

  function openCreate() {
    setEditingId(null)
    setDraft(createDraft())
    setFormOpen(true)
    setHinScrollen((n) => n + 1)
    setError(null)
    setMessage(null)
  }

  function closeForm() {
    setEditingId(null)
    setDraft(createDraft())
    setFormOpen(false)
    setError(null)
  }

  async function saveHardware(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!draft.name.trim()) {
      setError('Bitte Gerätename eingeben.')
      return
    }

    const existing = editingId ? hardware.find((item) => item.id === editingId) ?? null : null
    const request: CreateHardwareItemRequest | UpdateHardwareItemRequest = {
      name: draft.name.trim(),
      category: draft.category.trim() || 'Sensor',
      status: draft.status,
      criticality: draft.criticality,
      tentId: toIntOrNull(draft.tentId),
      setupId: null,
      hydroSetupId: toIntOrNull(draft.hydroSetupId),
      // Entity mapping lives on the Home Assistant page; the sync writes it onto the
      // item. Preserve the synced value here — a form edit must not clear it.
      haEntityId: existing?.haEntityId ?? null,
      deviceKind: draft.deviceKind,
      manufacturer: nullable(draft.manufacturer),
      model: nullable(draft.model),
      serialNumber: nullable(draft.serialNumber),
      notes: nullable(draft.notes),
      installedAtUtc: existing?.installedAtUtc ?? new Date().toISOString(),
      retiredAtUtc: existing?.retiredAtUtc ?? null,
      wearTemplateId: draft.wearTemplateId || null,
      tentSensorId: existing?.tentSensorId ?? null,
      growId: existing?.growId ?? null,
      expectedLifespanDays: toIntOrNull(draft.expectedLifespanDays),
      inspectionIntervalDays: toIntOrNull(draft.inspectionIntervalDays),
      calibrationIntervalDays: toIntOrNull(draft.calibrationIntervalDays),
    }

    setSaving('hardware')
    setError(null)
    setMessage(null)
    try {
      if (editingId) {
        await apiFetch<HardwareItemDto>(`/api/hardware-items/${editingId}`, { method: 'PUT', body: JSON.stringify(request) })
        setMessage('Sensor gespeichert.')
      } else {
        await apiFetch<HardwareItemDto>('/api/hardware-items', { method: 'POST', body: JSON.stringify(request) })
        setMessage('Hardware angelegt. Live-Werte verbindest du im Tab „Home Assistant" am Zelt.')
      }
      setEditingId(null)
      setDraft(createDraft())
      await load()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : editingId ? 'Sensor konnte nicht gespeichert werden.' : 'Hardware konnte nicht angelegt werden.')
    } finally {
      setSaving(null)
    }
  }

  async function updateHardwareStatus(item: HardwareItemDto, status: HardwareItemStatus) {
    setSaving(`hardware-${item.id}`)
    setError(null)
    const request: UpdateHardwareItemRequest = {
      name: item.name,
      category: item.category,
      status,
      criticality: item.criticality,
      tentId: item.tentId,
      setupId: item.setupId,
      hydroSetupId: item.hydroSetupId,
      haEntityId: item.haEntityId,
      manufacturer: item.manufacturer,
      model: item.model,
      serialNumber: item.serialNumber,
      notes: item.notes,
      installedAtUtc: item.installedAtUtc,
      retiredAtUtc: item.retiredAtUtc,
      wearTemplateId: item.wearTemplateId,
      tentSensorId: item.tentSensorId,
      growId: item.growId,
      expectedLifespanDays: item.expectedLifespanDays,
      inspectionIntervalDays: item.inspectionIntervalDays,
      calibrationIntervalDays: item.calibrationIntervalDays,
    }
    try {
      await apiFetch<HardwareItemDto>(`/api/hardware-items/${item.id}`, { method: 'PUT', body: JSON.stringify(request) })
      await load()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Hardwarestatus konnte nicht geändert werden.')
    } finally {
      setSaving(null)
    }
  }

  async function deleteHardware(item: HardwareItemDto) {
    if (saving) return
    const confirmed = window.confirm(`${item.name} endgültig löschen?`)
    if (!confirmed) return

    setSaving(`hardware-${item.id}`)
    setError(null)
    setMessage(null)
    try {
      await apiFetch(`/api/hardware-items/${item.id}`, { method: 'DELETE' })
      setHardware((current) => current.filter((hardwareItem) => hardwareItem.id !== item.id))
      setMessage('Sensor gelöscht.')
      if (editingId === item.id) {
        setEditingId(null)
        setDraft(createDraft())
      }
      await load()
    } catch (caught) {
      if (isNotFound(caught)) {
        setHardware((current) => current.filter((hardwareItem) => hardwareItem.id !== item.id))
        setMessage('Eintrag existiert bereits nicht mehr.')
        if (editingId === item.id) {
          setEditingId(null)
          setDraft(createDraft())
        }
        await load()
        return
      }
      setError(caught instanceof Error ? caught.message : 'Sensor konnte nicht gelöscht werden.')
    } finally {
      setSaving(null)
    }
  }

  return (
    <V1Page
      eyebrow="Einrichtung / Sensoren"
      title="Sensoren & Wartung"
      subtitle="Deine Geräte: was verbaut ist, wann es geprüft, kalibriert oder getauscht gehört. Welche Home-Assistant-Entität welchen Messwert liefert, stellst du unter Home Assistant ein — gemessene Sensoren erscheinen dann hier von selbst."
      action={<button type="button" className="ls-btn is-primary" onClick={openCreate}>+ Gerät anlegen</button>}
    >
      {error && <V1Alert title="Fehler" message={error} tone="warn" />}
      {message && <V1Alert message={message} tone="ok" />}

      {careDraft && (
      <div ref={pflegeRef} className="scroll-ziel" data-audit="pflege-formular">
        <V1Section title={`${careDraft.kind} eintragen · ${careDraft.geraet}`}>
          <V1Card>
            <div className="hw-care-form" data-audit="care-form">
              <p className="gc-facts">
                {careDraft.titel}
                {careDraft.kind === 'Kalibrierung'
                  ? ' — die Werte sind freiwillig. Wer sie einträgt, sieht später am Vorher-Wert, wann die Sonde müde wird.'
                  : ' — Datum genügt; der nächste Termin wird daraus gerechnet.'}
              </p>
              <div className="hw-care-grid">
                <V1Field label="Wann">
                  <input type="date" value={careDraft.datum} onChange={(event) => setCareDraft((current) => current && ({ ...current, datum: event.target.value }))} />
                </V1Field>
                {careDraft.kind === 'Kalibrierung' && (
                  <div className="hw-punkte">
                    <p className="hw-punkte-hinweis">
                      Eine Sonde wird gegen mehrere Lösungen abgeglichen — üblich sind
                      pH&nbsp;4,01 und 7,00, manche nehmen zusätzlich 10,01. Trag ein, was
                      die Sonde <b>vorher</b> anzeigte: daraus ergibt sich, ob sie noch
                      spreizt.
                    </p>
                    <table className="hw-punkte-tabelle">
                      <thead>
                        <tr>
                          <th>Lösung</th>
                          <th>Sollwert</th>
                          <th>Vorher</th>
                          <th>Nachher</th>
                        </tr>
                      </thead>
                      <tbody>
                        {careDraft.punkte.map((punkt, index) => (
                          <tr key={index}>
                            <td>
                              <input
                                value={punkt.loesung}
                                aria-label={`Lösung ${index + 1}`}
                                placeholder="pH 7,00"
                                onChange={(event) => patchPunkt(index, { loesung: event.target.value })}
                              />
                            </td>
                            <td>
                              <input
                                inputMode="decimal"
                                value={punkt.sollText}
                                aria-label={`Sollwert ${index + 1}`}
                                placeholder="7,00"
                                onChange={(event) => patchPunkt(index, { sollText: event.target.value })}
                              />
                            </td>
                            <td>
                              <input
                                inputMode="decimal"
                                value={punkt.vorherText}
                                aria-label={`Vorher ${index + 1}`}
                                placeholder="6,82"
                                onChange={(event) => patchPunkt(index, { vorherText: event.target.value })}
                              />
                            </td>
                            <td>
                              <input
                                inputMode="decimal"
                                value={punkt.nachherText}
                                aria-label={`Nachher ${index + 1}`}
                                placeholder="7,00"
                                onChange={(event) => patchPunkt(index, { nachherText: event.target.value })}
                              />
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>

                    <div className="v1-action-row">
                      <V1Button
                        type="button"
                        onClick={() => setCareDraft((current) => current && ({
                          ...current,
                          punkte: [...current.punkte, { loesung: '', sollText: '', vorherText: '', nachherText: '' }],
                        }))}
                      >Punkt ergänzen</V1Button>
                    </div>

                    {steilheit != null && (
                      <p className={steilheit < 85 ? 'hw-steilheit is-warn' : 'hw-steilheit'}>
                        {steilheitSatz(steilheit)}
                      </p>
                    )}
                  </div>
                )}
                <V1Field label="Notiz">
                  <input value={careDraft.notiz} onChange={(event) => setCareDraft((current) => current && ({ ...current, notiz: event.target.value }))} placeholder="optional" />
                </V1Field>
              </div>
              <label className="hw-care-check">
                <input type="checkbox" checked={careDraft.problem} onChange={(event) => setCareDraft((current) => current && ({ ...current, problem: event.target.checked }))} />
                <span>
                  {careDraft.kind === 'Kalibrierung'
                    ? 'Sonde nimmt den Referenzwert nicht mehr an — Austausch prüfen'
                    : 'Dabei etwas gefunden — Ersatz oder Reparatur nötig'}
                </span>
              </label>
              <div className="co-actions">
                <V1Button variant="primary" disabled={saving === 'care'} audit="care-save" onClick={() => void saveCare()}>
                  {saving === 'care' ? 'Speichert…' : 'Eintragen'}
                </V1Button>
                <V1Button onClick={() => setCareDraft(null)}>Abbrechen</V1Button>
              </div>
            </div>
          </V1Card>
        </V1Section>
      </div>
      )}

      <div className="co-strip" data-audit="hardware-kpis">
        <div className="co-cell"><div className="co-cell-label">Geräte</div><div className="co-cell-value is-lg">{hardware.length}</div></div>
        <div className="co-cell"><div className="co-cell-label">Live über HA</div><div className="co-cell-value is-lg">{hardware.filter((item) => item.haEntityId).length}</div></div>
        <div className="co-cell"><div className="co-cell-label">Kalibrierung fällig</div><div className={`co-cell-value is-lg${plannedCalibration.length + plannedMaintenance.length > 0 ? ' is-warn' : ''}`}>{plannedCalibration.length + plannedMaintenance.length}</div></div>
        <div className="co-cell"><div className="co-cell-label">Störung</div><div className="co-cell-value is-lg" style={offline > 0 ? { color: 'var(--danger)' } : undefined}>{offline}</div></div>
      </div>

      {loading ? <V1Skeleton tiles={4} rows={4} label="Lade Sensoren" /> : (
        <>
          <V1Section title="Geräte">
            {/* Filter statt Tabs: die Tabs zeigten Teilmengen derselben Liste auf
                getrennten Seiten, sodass dieselbe Sonde in dreien davon stand und
                man Status und Kalibriertermin nicht zusammen sehen konnte. */}
            <div className="hw-filters" role="group" aria-label="Liste einschränken">
              {FILTERS.map(({ value, label }) => (
                <button
                  key={value}
                  type="button"
                  className={classNames('hw-filter', filter === value && 'active')}
                  aria-pressed={filter === value}
                  onClick={() => setFilter(value)}
                  data-audit={`hardware-filter-${value}`}
                >
                  {label}<span className="hw-filter-count">{counts[value]}</span>
                </button>
              ))}
            </div>

            {visibleRows.length === 0 ? (
              <V1Empty
                title={rows.length === 0 ? 'Noch keine Hardware angelegt' : 'Nichts in dieser Auswahl'}
                text={rows.length === 0 ? 'Feste Sensoren erscheinen automatisch, sobald du sie unter Home Assistant zuordnest. Messgeräte und Technik legst du hier an.' : undefined}
                action={rows.length === 0 ? <V1Button variant="primary" onClick={openCreate}>Gerät anlegen</V1Button> : <V1Button onClick={() => setFilter('alle')}>Alle zeigen</V1Button>}
              />
            ) : (
              <div className="hw-table-wrap">
                <table className="hw-table" data-audit="hardware-table">
                  <thead>
                    <tr>
                      <th scope="col">Gerät</th>
                      <th scope="col">Art</th>
                      <th scope="col">Zelt</th>
                      <th scope="col">HA-Entity</th>
                      <th scope="col">Wert</th>
                      <th scope="col">Kalibrierung</th>
                      <th scope="col"><span className="sr-only">Aktionen</span></th>
                    </tr>
                  </thead>
                  <tbody>
                    {visibleRows.map((row) => (
                      <HardwareRowView
                        key={row.item.id}
                        row={row}
                        liveState={liveStateFor(row.item, entities)}
                        saving={saving === `hardware-${row.item.id}`}
                        onStatus={updateHardwareStatus}
                        onEdit={startEdit}
                        onDelete={deleteHardware}
                        onCare={openCare}
                        onCareEntfernen={(row) => void terminEntfernen(row)}
                      />
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            <p className="hw-note">
              Feste Sensoren legt Grow OS selbst an, sobald du unter{' '}
              <V1LinkButton to="/home-assistant" variant="ghost">Home Assistant</V1LinkButton>{' '}
              eine Entity zuordnest — samt Kalibrier-Intervall, das du hier je Sensor ändern kannst.
            </p>
          </V1Section>

          {formOpen && (
          <div ref={formularRef} className="scroll-ziel">
          <V1Section title={editingId ? 'Sensor oder Gerät bearbeiten' : 'Sensor oder Gerät anlegen'}>
            <form className="ops1b-form" data-audit="hardware-edit-form" onSubmit={(event) => void saveHardware(event)}>
              <div className="ops1b-form-grid">
                <V1Field label="Name" wide><input value={draft.name} onChange={(event) => setDraft((current) => ({ ...current, name: event.target.value }))} placeholder="pH Sonde Hauptzelt" /></V1Field>
                {draft.deviceKind === 'FixedSensor' ? (
                  <V1Field label="Art" hint="Kommt aus dem HA-Mapping.">
                    <input value="Fester Sensor" readOnly disabled />
                  </V1Field>
                ) : (
                  <V1Field label="Art" hint="Messgeräte werden kalibriert, Geräte gewartet.">
                    <select value={draft.deviceKind} onChange={(event) => setDraft((current) => ({ ...current, deviceKind: event.target.value as HardwareDeviceKind }))}>
                      {deviceKindOptions.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
                    </select>
                  </V1Field>
                )}
                <V1Field label="Kategorie"><input value={draft.category} onChange={(event) => setDraft((current) => ({ ...current, category: event.target.value }))} placeholder="Sensor / Pumpe / Chiller" /></V1Field>
                <V1Field label="Status"><select value={draft.status} onChange={(event) => setDraft((current) => ({ ...current, status: event.target.value as HardwareItemStatus }))}>{statusOptions.map((item) => <option key={item} value={item}>{geraeteStatusName(item)}</option>)}</select></V1Field>
                <V1Field label="Kritikalität"><select value={draft.criticality} onChange={(event) => setDraft((current) => ({ ...current, criticality: event.target.value as HardwareItemCriticality }))}>{criticalityOptions.map((item) => <option key={item} value={item}>{formatSeverityLabel(item)}</option>)}</select></V1Field>
                <V1Field label="Zelt"><select value={draft.tentId} onChange={(event) => setDraft((current) => ({ ...current, tentId: event.target.value, hydroSetupId: '' }))}><option value="">Kein Zelt</option>{tents.map((tent) => <option key={tent.id} value={tent.id}>{tent.name}</option>)}</select></V1Field>
                <V1Field label="Hydro-Setup"><select value={draft.hydroSetupId} onChange={(event) => setDraft((current) => ({ ...current, hydroSetupId: event.target.value }))}><option value="">Kein Hydro-Setup</option>{hydroSetups.filter((setup) => !draft.tentId || String(setup.tentId) === draft.tentId).map((setup) => <option key={setup.id} value={setup.id}>{setup.name}</option>)}</select></V1Field>
                <V1Field label="Hersteller"><input value={draft.manufacturer} onChange={(event) => setDraft((current) => ({ ...current, manufacturer: event.target.value }))} /></V1Field>
                <V1Field label="Modell"><input value={draft.model} onChange={(event) => setDraft((current) => ({ ...current, model: event.target.value }))} /></V1Field>
                <V1Field label="Seriennummer"><input value={draft.serialNumber} onChange={(event) => setDraft((current) => ({ ...current, serialNumber: event.target.value }))} /></V1Field>
                <V1Field label="Kalibrieren alle (Tage)" hint="Erinnerung nach jeder Kalibrierung; leer = Standard je Typ"><input type="number" min="1" value={draft.calibrationIntervalDays} onChange={(event) => setDraft((current) => ({ ...current, calibrationIntervalDays: event.target.value }))} placeholder="z. B. 14" /></V1Field>
                <V1Field
                  label="Verschleißteil"
                  wide
                  hint="Füllt Lebensdauer und Prüfintervall aus unserem Wissen — beides bleibt danach änderbar.">
                  <select value={draft.wearTemplateId} onChange={(event) => waehleVorlage(event.target.value)}>
                    <option value="">— keine Vorlage —</option>
                    {wearTemplates.map((template) => (
                      <option key={template.id} value={template.id}>
                        {template.name} · {template.expectedLifespanDays} Tage
                      </option>
                    ))}
                  </select>
                </V1Field>
                <V1Field label="Lebensdauer (Tage)" hint="Danach meldet sich die Wartung. Leer = keine Erinnerung."><input type="number" min="1" value={draft.expectedLifespanDays} onChange={(event) => setDraft((current) => ({ ...current, expectedLifespanDays: event.target.value }))} placeholder="z. B. 375" /></V1Field>
                <V1Field label="Prüfen alle (Tage)"><input type="number" min="1" value={draft.inspectionIntervalDays} onChange={(event) => setDraft((current) => ({ ...current, inspectionIntervalDays: event.target.value }))} placeholder="z. B. 90" /></V1Field>
                <V1Field label="Notizen" wide><textarea value={draft.notes} onChange={(event) => setDraft((current) => ({ ...current, notes: event.target.value }))} rows={3} /></V1Field>
              </div>
              <div className="ops1b-sticky-actions">
                <V1Button type="button" variant="ghost" onClick={closeForm}>Abbrechen</V1Button>
                <V1Button type="submit" variant="primary" disabled={saving === 'hardware'}>{saving === 'hardware' ? 'Speichert...' : editingId ? 'Speichern' : 'Hardware anlegen'}</V1Button>
              </div>
            </form>
          </V1Section>
          </div>
          )}
        </>
      )}
    </V1Page>
  )
}

/** Eine Gerätezeile. Bei schmalem Rahmen legt hardware.css sie zu einer Karte um. */
function HardwareRowView({ row, liveState, saving, onStatus, onEdit, onDelete, onCare, onCareEntfernen }: { row: HardwareRow; liveState: string | null; saving: boolean; onStatus: (item: HardwareItemDto, status: HardwareItemStatus) => void; onEdit: (item: HardwareItemDto) => void; onDelete: (item: HardwareItemDto) => void; onCare: (row: HardwareRow) => void; onCareEntfernen: (row: HardwareRow) => void }) {
  const { item } = row
  return (
    <tr className={classNames(row.overdue && 'overdue')}>
      <td data-label="Gerät">
        <strong>{item.name}</strong>
        <span className="hw-sub">{item.manufacturer ?? item.category}{item.status !== 'Active' ? <> · <V1Badge tone={statusTone(item.status)}>{statusLabel(item.status)}</V1Badge></> : null}</span>
      </td>
      <td data-label="Art">{deviceKindLabel(item.deviceKind) ?? item.category}</td>
      <td data-label="Zelt">{row.tentName ?? <span className="hw-empty">—</span>}</td>
      {/* Die Zuordnung wird hier NICHT gesetzt, sie kommt von der HA-Seite. Ein
          blosses „nicht gemappt" laedt zum Ausfuellen ein, wo es nichts auszufuellen
          gibt — deshalb der Weg dorthin statt einer Sackgasse. */}
      {/* Ein Handmessgeraet hat keine Entitaet — ein Stift zum Reinhalten
          liefert nichts an Home Assistant. „zuordnen →" schickte den Nutzer
          dort auf eine Suche, die nicht enden kann. Die App weiss das
          ohnehin: `isMappingExpected` warnt bei Handmessgeraeten bewusst
          NICHT ueber eine fehlende Zuordnung. */}
      <td data-label="HA-Entity">{item.haEntityId
        ? <code className="hw-entity">{item.haEntityId}</code>
        : inferDeviceKind(item) === 'HandheldMeter'
          ? <span className="hw-empty">von Hand</span>
          : <Link className="hw-empty" to="/home-assistant">zuordnen →</Link>}</td>
      <td data-label="Wert">{liveState ?? <span className="hw-empty">—</span>}</td>
      <td data-label="Kalibrierung">
        {row.nextCare ? (
          <>
            <strong className={classNames(row.overdue && 'hw-overdue')}>{dueLabel(row.dueInDays)}</strong>
            <span className="hw-sub">{row.nextCare.kind}: {row.nextCare.title}</span>
          </>
        ) : <span className="hw-empty">—</span>}
      </td>
      <td data-label="Aktionen">
        <div className="hw-actions">
          {/* Der Termin stand hier immer nur da. Wer kalibriert hat, brauchte
              eine Stelle, an der er das sagen kann — sonst mahnt die App ewig
              etwas an, das längst erledigt ist. */}
          {row.nextCare && (
            <V1Button variant="primary" audit="care-complete" onClick={() => onCare(row)}>
              {row.nextCare.kind === 'Kalibrierung' ? 'Kalibriert' : 'Gemacht'}
            </V1Button>
          )}
          {/* Und der Weg zurueck, wenn der Termin falsch eingetragen war. */}
          {row.nextCare && (
            <V1Button audit="care-remove" onClick={() => onCareEntfernen(row)}>Termin weg</V1Button>
          )}
          <V1Button onClick={() => onEdit(item)}>Bearbeiten</V1Button>
          <V1Button disabled={saving} onClick={() => void onStatus(item, item.status === 'Offline' ? 'Active' : 'Offline')}>{item.status === 'Offline' ? 'Aktivieren' : 'Offline'}</V1Button>
          <V1Button variant="danger" disabled={saving} audit="hardware-delete-button" onClick={() => void onDelete(item)}>{saving ? 'Löscht...' : 'Löschen'}</V1Button>
        </div>
      </td>
    </tr>
  )
}

/** Live-Wert der gemappten Entity — die WERT-Spalte des Entwurfs. */
function liveStateFor(item: HardwareItemDto, entities: HomeAssistantEntity[]): string | null {
  if (!item.haEntityId) return null
  const entity = entities.find((candidate) => candidate.entityId === item.haEntityId)
  if (!entity) return null

  // Die Umschrift steht in `haWert` — sie wird an drei Stellen gebraucht.
  return haWert(entity.state, entity.unitOfMeasurement)
}

function inferDeviceKind(item?: HardwareItemDto): HardwareDeviceKind {
  if (item?.deviceKind) return item.deviceKind
  if (item?.haEntityId || item?.metricType) return 'FixedSensor'
  if (item && isSensorLike(item)) return 'HandheldMeter'
  return item ? 'Equipment' : 'HandheldMeter'
}

function createDraft(item?: HardwareItemDto): HardwareDraft {
  return {
    name: item?.name ?? '',
    category: item?.category ?? 'Sensor',
    deviceKind: inferDeviceKind(item),
    status: item?.status ?? 'Active',
    criticality: item?.criticality ?? 'High',
    tentId: item?.tentId ? String(item.tentId) : '',
    hydroSetupId: item?.hydroSetupId ? String(item.hydroSetupId) : '',
    manufacturer: item?.manufacturer ?? '',
    model: item?.model ?? '',
    serialNumber: item?.serialNumber ?? '',
    calibrationIntervalDays: item?.calibrationIntervalDays != null ? String(item.calibrationIntervalDays) : '',
    notes: item?.notes ?? '',
    wearTemplateId: item?.wearTemplateId ?? '',
    expectedLifespanDays: item?.expectedLifespanDays != null ? String(item.expectedLifespanDays) : '',
    inspectionIntervalDays: item?.inspectionIntervalDays != null ? String(item.inspectionIntervalDays) : '',
  }
}

function isSensorLike(item: HardwareItemDto) {
  // Explicit device kind wins; keyword matching is only the legacy fallback.
  if (item.deviceKind === 'FixedSensor' || item.deviceKind === 'HandheldMeter') return true
  if (item.deviceKind === 'Equipment') return false
  const text = `${item.name} ${item.category}`.toLowerCase()
  return ['sensor', 'sonde', 'probe', 'ph', 'ec', 'orp', 'do', 'temperatur', 'level'].some((term) => text.includes(term))
}

function nullable(value: string) {
  const trimmed = value.trim()
  return trimmed.length > 0 ? trimmed : null
}

function toIntOrNull(value: string) {
  const parsed = Number.parseInt(value, 10)
  return Number.isFinite(parsed) ? parsed : null
}



function isNotFound(caught: unknown) {
  return caught instanceof ApiRequestError && caught.status === 404
}

export default HardwarePage
