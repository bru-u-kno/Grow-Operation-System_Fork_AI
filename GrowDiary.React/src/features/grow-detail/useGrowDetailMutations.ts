import { useState } from 'react'
import type { Dispatch, FormEvent, SetStateAction } from 'react'
import type { NavigateFunction } from 'react-router-dom'
import { apiFetch, ApiRequestError } from '../../api'
import { TASKS_CHANGED_EVENT } from '../../useNavCounts'
import type {
  GrowActionResultDto,
  GrowDetail,
  MeasurementDto,
  PhotoAssetDto,
  SopInstanceDto,
  SopStepInstanceDto,
  SopStepInstanceStatus,
  StartSopInstanceRequest,
  TreatmentRecommendationDto,
  UpdateSopStepInstanceRequest,
} from '../../types'
import {
  emptyJournalForm,
  emptyPhotoForm,
  emptyTaskForm,
  isNotFound,
} from './grow-detail-model'

type UseGrowDetailMutationsArgs = {
  growId: string | undefined
  grow: GrowDetail | null
  saving: string | null
  selectedMeasurement: MeasurementDto | null
  sopStepNotesById: Record<number, string>
  navigate: NavigateFunction
  loadBundle: () => Promise<void>
  loadPhotos: (measurementId: number) => Promise<void>
  loadSopInstances: () => Promise<void>
  setError: Dispatch<SetStateAction<string | null>>
  setNotice: Dispatch<SetStateAction<string | null>>
  setSaving: Dispatch<SetStateAction<string | null>>
}

export function useGrowDetailMutations({
  growId,
  grow,
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
}: UseGrowDetailMutationsArgs) {
  const [taskForm, setTaskForm] = useState(emptyTaskForm)
  const [journalForm, setJournalForm] = useState(emptyJournalForm)
  const [photoForm, setPhotoForm] = useState(emptyPhotoForm)

  async function handleTaskSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!growId) return

    setSaving('task')
    try {
      await apiFetch(`/api/grows/${growId}/tasks`, {
        method: 'POST',
        body: JSON.stringify({
          title: taskForm.title,
          notes: taskForm.notes || null,
          dueAtLocal: taskForm.dueAtLocal || null,
          priority: taskForm.priority,
        }),
      })
      setTaskForm(emptyTaskForm())
      setNotice('Task gespeichert.')
      window.dispatchEvent(new Event(TASKS_CHANGED_EVENT))
      await loadBundle()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Aufgabe konnte nicht gespeichert werden.')
    } finally {
      setSaving(null)
    }
  }

  async function handleJournalSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!growId) return

    setSaving('journal')
    try {
      await apiFetch(`/api/grows/${growId}/journal`, {
        method: 'POST',
        body: JSON.stringify({
          title: journalForm.title || null,
          body: journalForm.body || null,
          entryType: journalForm.entryType,
          source: journalForm.source,
          occurredAtLocal: journalForm.occurredAtLocal,
        }),
      })
      setJournalForm(emptyJournalForm())
      setNotice('Journal gespeichert.')
      await loadBundle()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Journal konnte nicht gespeichert werden.')
    } finally {
      setSaving(null)
    }
  }

  async function handlePhotoSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!selectedMeasurement || photoForm.files.length === 0) {
      setError('Bitte wähle eine Messung und mindestens ein Foto aus.')
      return
    }

    setSaving('photo')
    try {
      const formData = new FormData()
      formData.append('photoCaption', photoForm.photoCaption)
      formData.append('photoTag', photoForm.photoTag)
      formData.append('useAsReferenceShot', String(photoForm.useAsReferenceShot))
      formData.append('source', photoForm.source)
      for (const file of photoForm.files) {
        formData.append('photos', file)
      }

      await apiFetch<PhotoAssetDto[]>(`/api/measurements/${selectedMeasurement.id}/photos`, { method: 'POST', body: formData })
      setPhotoForm(emptyPhotoForm())
      setNotice('Fotos hochgeladen.')
      await Promise.all([loadBundle(), loadPhotos(selectedMeasurement.id)])
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Fotos konnten nicht gespeichert werden.')
    } finally {
      setSaving(null)
    }
  }

  async function updateTaskStatus(taskId: number, status: 'Open' | 'Done' | 'Skipped') {
    setSaving(`task-status-${taskId}`)
    try {
      await apiFetch(`/api/tasks/${taskId}/status`, { method: 'PATCH', body: JSON.stringify({ status }) })
      await loadBundle()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Task-Status konnte nicht geändert werden.')
    } finally {
      setSaving(null)
    }
  }

  async function startRecommendedSop(recommendation: TreatmentRecommendationDto) {
    if (!growId || !recommendation.sopId) return
    const savingKey = `start-sop-${recommendation.stableKey}`
    setSaving(savingKey)
    setNotice(null)
    try {
      const payload: StartSopInstanceRequest = {
        growId: parseInt(growId, 10),
        sopId: recommendation.sopId,
        source: 'Recommendation',
        sourceRecommendationKey: recommendation.stableKey,
        treatmentRecommendationStableKey: recommendation.stableKey,
        notes: 'Gestartet aus Diagnoseempfehlung',
      }
      await apiFetch<SopInstanceDto>('/api/sop-instances/start', {
        method: 'POST',
        body: JSON.stringify(payload),
      })
      setNotice('SOP gestartet.')
      setError(null)
      await loadSopInstances()
    } catch (caught) {
      if (caught instanceof ApiRequestError && caught.payload?.code === 'active_sop_exists') {
        setNotice('SOP ist bereits aktiv.')
        setError(null)
      } else {
        setError(caught instanceof ApiRequestError ? caught.message : 'SOP konnte nicht gestartet werden.')
      }
    } finally {
      setSaving(null)
    }
  }

  async function updateSopStep(step: SopStepInstanceDto, status: SopStepInstanceStatus) {
    const savingKey = `sop-step-${step.id}-${status}`
    setSaving(savingKey)
    setNotice(null)
    try {
      const payload: UpdateSopStepInstanceRequest = {
        status,
        notes: sopStepNotesById[step.id]?.trim() || null,
        measurementId: null,
        journalEntryId: null,
        photoAssetId: null,
      }
      await apiFetch<SopStepInstanceDto>(`/api/sop-instances/steps/${step.id}`, {
        method: 'PUT',
        body: JSON.stringify(payload),
      })
      setNotice('SOP-Step aktualisiert.')
      setError(null)
      await loadSopInstances()
    } catch (caught) {
      setError(caught instanceof ApiRequestError ? caught.message : 'SOP-Step konnte nicht aktualisiert werden.')
    } finally {
      setSaving(null)
    }
  }

  async function handleGrowAction(action: 'germination' | 'rooting' | 'veg' | 'finish' | 'flip') {
    if (!growId) return

    const route = action === 'germination'
      ? 'confirm-germination'
      : action === 'rooting'
        ? 'confirm-rooting'
        : action === 'veg'
          ? 'confirm-veg'
          : action === 'finish'
            ? 'confirm-finish'
            : 'flip-to-flower'

    setSaving(`action-${action}`)
    try {
      const result = await apiFetch<GrowActionResultDto>(`/api/grows/${growId}/actions/${route}`, { method: 'POST' })
      setNotice(result.message)
      setError(null)
      await loadBundle()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Grow-Aktion konnte nicht ausgeführt werden.')
    } finally {
      setSaving(null)
    }
  }

  async function archiveGrow() {
    if (!growId || !grow) return
    if (saving) return
    const confirmed = window.confirm(`${grow.name} beenden und archivieren?`)
    if (!confirmed) return

    setSaving('grow-archive')
    setError(null)
    setNotice(null)
    try {
      await apiFetch<GrowDetail>(`/api/grows/${growId}/archive`, { method: 'POST' })
      setNotice('Grow beendet und archiviert.')
      await loadBundle()
    } catch (caught) {
      if (isNotFound(caught)) {
        navigate('/grows')
        return
      }
      setError(caught instanceof Error ? caught.message : 'Grow konnte nicht beendet werden.')
    } finally {
      setSaving(null)
    }
  }

  async function deleteGrow() {
    if (!growId || !grow) return
    if (saving) return
    const confirmed = window.confirm(`${grow.name} endgültig löschen?`)
    if (!confirmed) return

    setSaving('grow-delete')
    setError(null)
    setNotice(null)
    try {
      await apiFetch(`/api/grows/${growId}`, { method: 'DELETE' })
      navigate('/grows')
    } catch (caught) {
      if (isNotFound(caught)) {
        navigate('/grows')
        return
      }
      setError(caught instanceof Error ? caught.message : 'Grow konnte nicht gelöscht werden.')
    } finally {
      setSaving(null)
    }
  }

  return {
    journalForm,
    photoForm,
    taskForm,
    archiveGrow,
    deleteGrow,
    handleGrowAction,
    handleJournalSubmit,
    handlePhotoSubmit,
    handleTaskSubmit,
    setJournalForm,
    setPhotoForm,
    setTaskForm,
    startRecommendedSop,
    updateSopStep,
    updateTaskStatus,
  }
}
