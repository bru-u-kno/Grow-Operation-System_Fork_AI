import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { apiFetch } from '../../api'
import type { SopInstanceDto } from '../../types'
import { GrowDetailDiagnosisSection } from '../grow-detail/GrowDetailDiagnosisSection'
import { JournalStreamSection } from '../grow-detail/JournalStreamSection'
import { GrowDetailMeasurementsSection } from '../grow-detail/GrowDetailMeasurementsSection'
import { useMessBeurteilung } from '../grow-detail/useMessBeurteilung'
import { GrowDetailSopSection } from '../grow-detail/GrowDetailSopSection'
import { SopCatalog } from '../grow-detail/SopCatalog'
import { SolutionStabilityPanel } from '../grow-detail/SolutionStabilityPanel'
import { ObservationGuide } from '../grow-detail/ObservationGuide'
import { useGrowDetailBundle } from '../grow-detail/useGrowDetailBundle'
import { useGrowDetailMutations } from '../grow-detail/useGrowDetailMutations'
import { useGrowDetailResources } from '../grow-detail/useGrowDetailResources'
import type { GrowDetailSection } from '../grow-detail/grow-detail-model'
import './grow-scope.css'

// The full grow-detail wiring (all the hooks that used to power the grow's tabs),
// rendering exactly ONE section. Each top-level grow-scoped page hosts one of these,
// so a section that used to be a tab inside a grow is now its own single-purpose page.
export function GrowWorkspace({ growId, section }: { growId: string; section: GrowDetailSection }) {
  const navigate = useNavigate()
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [saving, setSaving] = useState<string | null>(null)

  const {
    bundle,
    loading,
    selectedMeasurementId,
    handleMeasurementSelection,
    loadBundle,
    loadPhotos,
  } = useGrowDetailBundle({ growId, setError })

  // Die Beurteilung des Protokolls. Eigener Abruf neben dem Buendel, weil
  // sie die Profil-Kette und die Wissensbasis kostet und nur EIN Abschnitt
  // sie braucht.
  const beurteilung = useMessBeurteilung(section === 'measurements' ? growId : null, bundle.measurements.length)
  const {
    deviationError,
    deviations,
    loadDeviations,
    loadRiskEvents,
    loadSopInstances,
    loadTreatmentRecommendations,
    riskEventError,
    riskEvents,
    setSopStepNotesById,
    sopInstanceError,
    sopInstances,
    sopStepNotesById,
    sopStepsByInstanceId,
    treatmentRecommendationError,
    treatmentRecommendations,
  } = useGrowDetailResources({ growId })

  const activeSopIds = useMemo(() => new Set(sopInstances.filter((sop) => sop.status === 'Active').map((sop) => sop.sopId)), [sopInstances])
  const selectedMeasurement = useMemo(
    () => bundle.measurements.find((measurement) => measurement.id === selectedMeasurementId) ?? null,
    [bundle.measurements, selectedMeasurementId],
  )
  // Das Messformular ist hier weg: die Protokollseite nimmt keine Messungen
  // mehr auf, sie zeigt sie. Eintragen geht auf /messung — dort steht das volle
  // Formular mit Live-Pruefung, Foto und Addback.
  const {
    journalForm,
    photoForm,
    taskForm,
    handleJournalSubmit,
    handlePhotoSubmit,
    handleTaskSubmit,
    setJournalForm,
    setPhotoForm,
    setTaskForm,
    startRecommendedSop,
    updateSopStep,
  } = useGrowDetailMutations({
    growId,
    grow: bundle.grow,
    saving,
    selectedMeasurement,
    sopStepNotesById,
    navigate,
    loadBundle,
    loadPhotos,
    loadSopInstances,
    setError,
    setNotice,
    setSaving,
  })

  useEffect(() => {
    if (!growId) return
    const controller = new AbortController()
    const handle = window.setTimeout(() => {
      void loadBundle(controller.signal)
      if (section === 'diagnosis') {
        void loadDeviations(controller.signal)
        void loadTreatmentRecommendations(controller.signal)
        void loadRiskEvents(controller.signal)
      }
      if (section === 'sops') void loadSopInstances(controller.signal)
    }, 0)
    return () => {
      window.clearTimeout(handle)
      controller.abort()
    }
  }, [growId, section, loadBundle, loadDeviations, loadTreatmentRecommendations, loadRiskEvents, loadSopInstances])

  if (loading) {
    return <div className="empty-hint">Lade Daten…</div>
  }

  if (!bundle.grow) {
    return <div className="empty-hint" style={{ color: 'var(--red)' }}>{error ?? 'Grow nicht gefunden.'}</div>
  }

  /**
   * Einen versehentlich gestarteten Ablauf abbrechen.
   *
   * Der Server nimmt die Erinnerungen in den Aufgaben mit — sie haengen an
   * einer Spalte ohne Fremdschluessel und blieben sonst stehen.
   */
  async function ablaufAbbrechen(instanz: SopInstanceDto) {
    if (!window.confirm(`Ablauf „${instanz.sopName}" wirklich abbrechen?`)) return
    try {
      await apiFetch(`/api/sop-instances/${instanz.id}`, { method: 'DELETE' })
      setNotice(`„${instanz.sopName}" abgebrochen.`)
      void loadSopInstances()
    } catch (caught) {
      setNotice(caught instanceof Error ? caught.message : 'Ablauf konnte nicht abgebrochen werden.')
    }
  }

  return (
    <>
      {error && (
        <div className="alert-bar" style={{ marginBottom: 14, borderRadius: 'var(--radius)' }}>
          <div className="alert-dot" />
          <strong>Fehler</strong>
          <span>{error}</span>
        </div>
      )}
      {notice && (
        <div className="alert-bar" style={{ marginBottom: 14, borderRadius: 'var(--radius)', background: 'var(--green-bg)', borderColor: 'var(--green)' }}>
          <div className="alert-dot" style={{ background: 'var(--green)' }} />
          <strong style={{ color: 'var(--green)' }}>Info</strong>
          <span>{notice}</span>
        </div>
      )}

      {section === 'measurements' && (
        <GrowDetailMeasurementsSection
          activeSection="measurements"
          measurements={bundle.measurements}
          selectedMeasurementId={selectedMeasurementId}
          onSelectMeasurement={(measurementId) => void handleMeasurementSelection(measurementId)}
          beurteilung={beurteilung}
        />
      )}

      {/* Der Einstieg von der Pflanze her — vor die Zahlen, weil wer
          hierherkommt meist etwas GESEHEN hat und nicht gemessen. */}
      {section === 'diagnosis' && <ObservationGuide />}

      {section === 'diagnosis' && <SolutionStabilityPanel growId={growId} />}

      {section === 'diagnosis' && (
        <GrowDetailDiagnosisSection
          activeSection="diagnosis"
          deviations={deviations}
          deviationError={deviationError}
          treatmentRecommendations={treatmentRecommendations}
          treatmentRecommendationError={treatmentRecommendationError}
          riskEvents={riskEvents}
          riskEventError={riskEventError}
          saving={saving}
          onStartRecommendedSop={(recommendation) => void startRecommendedSop(recommendation)}
          onRiskChanged={(message) => { setNotice(message); void loadRiskEvents() }}
        />
      )}

      {section === 'sops' && (
        <SopCatalog growId={growId} activeSopIds={activeSopIds} onStarted={(message) => { setNotice(message); void loadSopInstances() }} />
      )}

      {section === 'sops' && (
        <GrowDetailSopSection
          activeSection="sops"
          sopInstances={sopInstances}
          sopStepsByInstanceId={sopStepsByInstanceId}
          sopStepNotesById={sopStepNotesById}
          sopInstanceError={sopInstanceError}
          saving={saving}
          onNoteChange={(stepId, notes) => setSopStepNotesById((current) => ({ ...current, [stepId]: notes }))}
          onUpdateStep={(step, status) => void updateSopStep(step, status)}
          onAbbrechen={(instanz) => void ablaufAbbrechen(instanz)}
        />
      )}

      {section === 'journal' && (
        <JournalStreamSection
          growId={growId}
          entries={bundle.journal}
          measurements={bundle.measurements}
          journalForm={journalForm}
          photoForm={photoForm}
          taskForm={taskForm}
          saving={saving}
          selectedMeasurementId={selectedMeasurementId}
          onEntfernt={() => void loadBundle()}
          onMeasurementSelection={(measurementId) => void handleMeasurementSelection(measurementId)}
          onJournalFormChange={(patch) => setJournalForm((current) => ({ ...current, ...patch }))}
          onPhotoFormChange={(patch) => setPhotoForm((current) => ({ ...current, ...patch }))}
          onTaskFormChange={(patch) => setTaskForm((current) => ({ ...current, ...patch }))}
          onJournalSubmit={handleJournalSubmit}
          onPhotoSubmit={handlePhotoSubmit}
          onTaskSubmit={handleTaskSubmit}
        />
      )}
    </>
  )
}
