import { useEffect, useMemo, useRef, useState } from 'react'
import type { FormEvent, KeyboardEvent } from 'react'
import { apiFetch } from '../../api'
import type { JournalEntryDto, MeasurementDto, PhotoAssetDto, PhotoTag } from '../../types'
import { MEILENSTEIN_HINWEIS, textZeilen, aenderungsAnfrage, alsFelder, artenFuer, eintragFehler, istMeilenstein, type EintragFelder } from './journal-bearbeiten'
import type { JournalFormState, PhotoFormState, TaskFormState } from './grow-detail-model'
import { buildJournalStream, streamTimeLabel } from './journal-stream'
import { V1Button, V1Field } from '../../components/v1'
import { SymptomZuordnung } from '../knowledge/SymptomZuordnung'
import { FOTO_TAGS, fotoTagName } from '../../deutsche-woerter'
import './journal-stream.css'

// Die Liste UND die Beschriftungen kommen aus deutsche-woerter.ts. Hier stand
// eine zweite Kopie der Aufzaehlung, und die Auswahl zeigte die englischen
// Werte roh — „Overview", „Canopy", „Leaf", „Comparison", „Other".
const photoTags: PhotoTag[] = FOTO_TAGS

const taskPriorities: Array<{ value: string; label: string }> = [
  { value: 'Low', label: 'Niedrig' },
  { value: 'Normal', label: 'Normal' },
  { value: 'High', label: 'Hoch' },
  { value: 'Critical', label: 'Kritisch' },
]

/**
 * Die vier Felder eines Journaleintrags. EIN Formular für „+ Eintrag" und
 * „Bearbeiten" — zwei Kopien laufen auseinander (EINE WAHRHEIT JE ZAHL gilt
 * auch für Felder).
 */
export function EintragFelderFormular({ werte, onChange, autoFocusTitel, zeitpunktGesperrt }: {
  werte: EintragFelder
  onChange: (patch: Partial<EintragFelder>) => void
  autoFocusTitel?: boolean
  /** Bei Meilensteinen: das Datum gehört der Phase, nicht dem Journal. */
  zeitpunktGesperrt?: boolean
}) {
  const arten = artenFuer(werte.entryType)
  return (
    <>
      <V1Field label="Art">
        <select value={werte.entryType} onChange={(event) => onChange({ entryType: event.target.value })}>
          {arten.map((type) => <option key={type.value} value={type.value}>{type.label}</option>)}
        </select>
      </V1Field>
      <V1Field label="Titel"><input value={werte.title} autoFocus={autoFocusTitel} onChange={(event) => onChange({ title: event.target.value })} placeholder="Was ist passiert?" /></V1Field>
      <V1Field label="Zeitpunkt"><input type="datetime-local" value={werte.occurredAtLocal} disabled={zeitpunktGesperrt} onChange={(event) => onChange({ occurredAtLocal: event.target.value })} /></V1Field>
      <V1Field label="Text" wide><textarea rows={textZeilen(werte.body)} value={werte.body} onChange={(event) => onChange({ body: event.target.value })} /></V1Field>
    </>
  )
}

/**
 * Journal & Fotos als ein Strom, wie im Entwurf: Zeitspalte links, getaggter
 * Eintrag rechts, Messfotos direkt beim Eintrag. „Nur Fotos" filtert den
 * Strom, „+ Eintrag" öffnet das Formular unter dem Panelkopf.
 */
export function JournalStreamSection({ growId, entries, measurements, journalForm, photoForm, taskForm, saving, selectedMeasurementId, onMeasurementSelection, onJournalFormChange, onPhotoFormChange, onTaskFormChange, onJournalSubmit, onPhotoSubmit, onTaskSubmit, onEntfernt, onGeaendert }: {
  growId: string
  entries: JournalEntryDto[]
  measurements: MeasurementDto[]
  journalForm: JournalFormState
  photoForm: PhotoFormState
  taskForm: TaskFormState
  saving: string | null
  selectedMeasurementId: number | null
  onMeasurementSelection: (measurementId: number | null) => void
  onJournalFormChange: (patch: Partial<JournalFormState>) => void
  onPhotoFormChange: (patch: Partial<PhotoFormState>) => void
  onTaskFormChange: (patch: Partial<TaskFormState>) => void
  onJournalSubmit: (event: FormEvent<HTMLFormElement>) => void
  onPhotoSubmit: (event: FormEvent<HTMLFormElement>) => void
  onTaskSubmit: (event: FormEvent<HTMLFormElement>) => void
  /** Nach dem Entfernen neu laden — die Seite haelt die Eintraege. */
  onEntfernt?: () => void
  /** Nach dem Bearbeiten neu laden — wie nach dem Entfernen. */
  onGeaendert?: () => void | Promise<void>
}) {
  const [photos, setPhotos] = useState<PhotoAssetDto[]>([])
  const [photosOnly, setPhotosOnly] = useState(false)
  const [composerOpen, setComposerOpen] = useState(false)
  // Welcher Eintrag gerade bearbeitet wird — immer nur einer. Der Ausgangsstand
  // bleibt daneben, damit ein unveränderter Zeitpunkt nicht mitgeschickt wird
  // (die Umrechnung über die Minute verlöre sonst die Sekunden).
  const [bearbeitung, setBearbeitung] = useState<{ id: number; felder: EintragFelder; ausgang: EintragFelder } | null>(null)
  const [bearbeitungSpeichert, setBearbeitungSpeichert] = useState(false)
  const [bearbeitungFehler, setBearbeitungFehler] = useState<string | null>(null)
  const streamRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const controller = new AbortController()
    async function load() {
      try {
        const list = await apiFetch<PhotoAssetDto[]>(`/api/grows/${growId}/photos`, { signal: controller.signal })
        if (!controller.signal.aborted) setPhotos(list)
      } catch {
        if (!controller.signal.aborted) setPhotos([])
      }
    }
    void load()
    return () => controller.abort()
    // Auf die Identität des Arrays hören, nicht auf die Länge: nach einem
    // Foto-Upload lädt der Bundle neu (neues Array), die ANZAHL der Einträge
    // bleibt aber gleich — mit entries.length blieb der Strom stehen und das
    // frisch hochgeladene Foto erschien erst nach einem Seitenwechsel.
  }, [growId, entries])

  const stream = useMemo(() => buildJournalStream(entries, photos), [entries, photos])

  /** Einen Journaleintrag entfernen — mit Rueckfrage, danach neu laden. */
  async function eintragEntfernen(eintragId: number, titel: string) {
    if (!window.confirm(`Eintrag „${titel}" wirklich entfernen?`)) return
    try {
      await apiFetch(`/api/journal/${eintragId}`, { method: 'DELETE' })
      onEntfernt?.()
    } catch (caught) {
      window.alert(caught instanceof Error ? caught.message : 'Eintrag konnte nicht entfernt werden.')
    }
  }
  const eintragNachId = useMemo(() => new Map(entries.map((entry) => [entry.id, entry])), [entries])

  /** Den Fokus zurück auf „Bearbeiten" des Eintrags — nach Speichern und Abbrechen. */
  function fokusZurueck(eintragId: number) {
    requestAnimationFrame(() => {
      streamRef.current?.querySelector<HTMLButtonElement>(`[data-bearbeiten-id="${eintragId}"]`)?.focus()
    })
  }

  function bearbeitungOeffnen(eintragId: number) {
    const entry = eintragNachId.get(eintragId)
    if (!entry) return
    const felder = alsFelder(entry)
    setBearbeitung({ id: eintragId, felder, ausgang: felder })
    setBearbeitungFehler(null)
  }

  function bearbeitungAbbrechen() {
    if (!bearbeitung) return
    const id = bearbeitung.id
    setBearbeitung(null)
    setBearbeitungFehler(null)
    fokusZurueck(id)
  }

  async function bearbeitungSpeichern(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!bearbeitung) return
    const { id, felder, ausgang } = bearbeitung
    const fehler = eintragFehler(felder)
    if (fehler) {
      setBearbeitungFehler(fehler)
      return
    }
    setBearbeitungSpeichert(true)
    setBearbeitungFehler(null)
    try {
      await apiFetch(`/api/journal/${id}`, {
        method: 'PUT',
        body: JSON.stringify(aenderungsAnfrage(felder, ausgang)),
      })
      setBearbeitung(null)
      await onGeaendert?.()
      fokusZurueck(id)
    } catch (caught) {
      setBearbeitungFehler(caught instanceof Error ? caught.message : 'Eintrag konnte nicht gespeichert werden.')
    } finally {
      setBearbeitungSpeichert(false)
    }
  }

  function escapeBricktAb(event: KeyboardEvent<HTMLFormElement>) {
    if (event.key === 'Escape') {
      event.preventDefault()
      bearbeitungAbbrechen()
    }
  }

  const visible = photosOnly ? stream.filter((item) => item.photos.length > 0) : stream

  return (
    <section className="ls-panel" data-audit="journal-stream">
      <div className="ls-panel-head">
        <span className="ls-label">Journal &amp; Fotos</span>
        <span className="ls-panel-meta">{stream.length} Einträge</span>
        <div className="co-row co-row-end">
          <button type="button" className={`ls-btn is-small${photosOnly ? ' is-primary' : ''}`} onClick={() => setPhotosOnly((current) => !current)}>
            Nur Fotos
          </button>
          <button type="button" className="ls-btn is-small is-primary" data-audit="journal-add-entry" onClick={() => setComposerOpen((current) => !current)}>
            {composerOpen ? 'Schließen' : '+ Eintrag'}
          </button>
        </div>
      </div>

      {composerOpen && (
        <div className="js-composer">
          <form onSubmit={(event) => { onJournalSubmit(event); setComposerOpen(false) }} className="js-form" data-audit="journal-entry-form">
            <EintragFelderFormular werte={journalForm} onChange={onJournalFormChange} />
            <V1Button type="submit" variant="primary" disabled={saving === 'journal'}>{saving === 'journal' ? 'Speichert…' : 'Eintrag speichern'}</V1Button>
          </form>

          <form onSubmit={onPhotoSubmit} className="js-form" data-audit="journal-photo-form">
            <V1Field label="Foto zu Messung" hint={measurements.length === 0 ? 'Fotos hängen an Messungen — erst eine Messung erfassen.' : null}>
              <select value={selectedMeasurementId ?? ''} onChange={(event) => onMeasurementSelection(event.target.value ? parseInt(event.target.value, 10) : null)} disabled={measurements.length === 0}>
                {measurements.length === 0 ? <option value="">Keine Messungen</option> : null}
                {measurements.map((measurement) => (
                  <option key={measurement.id} value={measurement.id}>#{measurement.id} · {streamTimeLabel(measurement.takenAt).day} {streamTimeLabel(measurement.takenAt).clock}</option>
                ))}
              </select>
            </V1Field>
            <V1Field label="Art">
              <select value={photoForm.photoTag} onChange={(event) => onPhotoFormChange({ photoTag: event.target.value as PhotoTag })}>
                {photoTags.map((tag) => <option key={tag} value={tag}>{fotoTagName(tag)}</option>)}
              </select>
            </V1Field>
            <V1Field label="Bildunterschrift"><input value={photoForm.photoCaption} onChange={(event) => onPhotoFormChange({ photoCaption: event.target.value })} /></V1Field>
            <V1Field label="Dateien">
              <input type="file" accept="image/png,image/jpeg,image/webp" multiple onChange={(event) => onPhotoFormChange({ files: Array.from(event.target.files ?? []) })} />
            </V1Field>
            <V1Button type="submit" disabled={saving === 'photo' || measurements.length === 0}>{saving === 'photo' ? 'Lädt hoch…' : 'Fotos hochladen'}</V1Button>
          </form>

          <form onSubmit={onTaskSubmit} className="js-form" data-audit="journal-task-form">
            <V1Field label="Aufgabe" hint={'Erscheint unter „Aufgaben“ bei den Terminen.'}>
              <input value={taskForm.title} onChange={(event) => onTaskFormChange({ title: event.target.value })} placeholder="z. B. pH-Sonde kalibrieren" />
            </V1Field>
            <V1Field label="Fällig am"><input type="datetime-local" value={taskForm.dueAtLocal} onChange={(event) => onTaskFormChange({ dueAtLocal: event.target.value })} /></V1Field>
            {/* Die Priorität entscheidet, wie weit oben die Aufgabe unter
                „Aufgaben" steht — ohne sie landet alles auf „Normal". */}
            <V1Field label="Priorität">
              <select value={taskForm.priority} onChange={(event) => onTaskFormChange({ priority: event.target.value })}>
                {taskPriorities.map((item) => <option key={item.value} value={item.value}>{item.label}</option>)}
              </select>
            </V1Field>
            <V1Button type="submit" disabled={saving === 'task'}>{saving === 'task' ? 'Speichert…' : 'Aufgabe anlegen'}</V1Button>
          </form>
        </div>
      )}

      {visible.length === 0 ? (
        <div className="ls-panel-body"><p>{photosOnly ? 'Noch keine Fotos in diesem Grow.' : 'Noch keine Journal-Einträge.'}</p></div>
      ) : (
        <div className="js-stream" ref={streamRef}>
          {visible.map((item) => {
            const time = streamTimeLabel(item.at)
            return (
              <div key={item.key} className="js-row">
                <div className="js-when">{time.day}<br />{time.clock}</div>
                <div className="js-content">
                  <div className="js-headline">
                    <span className={`js-tag is-${item.tone}`}>{item.tag}</span>
                    <strong>{item.title}</strong>
                    {/* Ein Journal ist ein Tagebuch, kein Gesetzblatt: wer den
                        falschen Grow erwischt oder sich vertippt, muss den
                        Eintrag loswerden. Bis zum 25.08.2026 ging das nirgends. */}
                    {item.eintragId != null && (
                      <span className="js-aktionen">
                        {/* Korrigieren statt löschen und neu tippen (04.10.2026:
                            „pH- Menge nicht notiert" ließ sich nicht nachtragen). */}
                        <button
                          type="button"
                          className="js-weg js-bearbeiten"
                          data-bearbeiten-id={item.eintragId}
                          aria-label={`Eintrag „${item.title}" bearbeiten`}
                          aria-expanded={bearbeitung?.id === item.eintragId}
                          disabled={bearbeitung?.id === item.eintragId}
                          onClick={() => bearbeitungOeffnen(item.eintragId!)}
                        >
                          Bearbeiten
                        </button>
                        <button
                          type="button"
                          className="js-weg"
                          title="Eintrag entfernen"
                          aria-label={`Eintrag „${item.title}" entfernen`}
                          onClick={() => void eintragEntfernen(item.eintragId!, item.title)}
                        >
                          Entfernen
                        </button>
                      </span>
                    )}
                  </div>
                  {item.body && !(bearbeitung != null && bearbeitung.id === item.eintragId) && <p>{item.body}</p>}
                  {item.photos.length > 0 && (
                    <div className="js-photos">
                      {item.photos.map((photo) => (
                        <figure key={photo.id} className="js-photo">
                          <img src={photo.relativePath} alt={photo.caption ?? `Foto ${photo.id}`} loading="lazy" />
                          {/* Ein Klick, und das Bild steht kuenftig im Wissen
                              beim passenden Symptom. Ohne diese Stelle waere
                              das Feld tot — gespeichert, aber nie gefuellt. */}
                          <SymptomZuordnung photoId={photo.id} current={photo.symptomId} />
                        </figure>
                      ))}
                    </div>
                  )}
                </div>
                {/* Das Formular liegt ÜBER BEIDE Spalten — neben der 88-px-Zeitspalte
                    blieben bei 320 px nur 162 px, die Uhrzeit war abgeschnitten (Prüfer 05.10.2026). */}
                {bearbeitung != null && bearbeitung.id === item.eintragId && (
                    <form
                      className="js-form js-bearbeitung"
                      data-audit="journal-edit-form"
                      aria-label={`Eintrag „${item.title}" bearbeiten`}
                      onSubmit={(event) => void bearbeitungSpeichern(event)}
                      onKeyDown={escapeBricktAb}
                    >
                      <EintragFelderFormular
                        werte={bearbeitung.felder}
                        zeitpunktGesperrt={istMeilenstein(bearbeitung.felder.entryType)}
                        autoFocusTitel
                        onChange={(patch) => setBearbeitung((current) => current && { ...current, felder: { ...current.felder, ...patch } })}
                      />
                      {istMeilenstein(bearbeitung.felder.entryType) && (
                        <p className="js-hinweis">{MEILENSTEIN_HINWEIS}</p>
                      )}
                      {bearbeitungFehler && <p className="js-fehler" role="alert">{bearbeitungFehler}</p>}
                      <div className="js-knopfreihe">
                        <V1Button type="submit" variant="primary" audit="journal-edit-save" disabled={bearbeitungSpeichert}>
                          {bearbeitungSpeichert ? 'Speichert…' : 'Änderungen speichern'}
                        </V1Button>
                        <V1Button audit="journal-edit-cancel" onClick={bearbeitungAbbrechen} disabled={bearbeitungSpeichert}>Abbrechen</V1Button>
                      </div>
                    </form>
                )}
              </div>
            )
          })}
        </div>
      )}
    </section>
  )
}
