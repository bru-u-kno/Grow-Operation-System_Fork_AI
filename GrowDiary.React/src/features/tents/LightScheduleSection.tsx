import { useEffect, useState } from 'react'
import { apiFetch } from '../../api'
import type { LightScheduleDto } from '../../types'
import { V1Section, V1Card, V1Field, V1Button, V1Badge, V1Alert } from '../../components/v1'

// Makes the tent's light cycle visible and editable — the precondition the light-based
// automations ("30 min after lights on/off") trigger on, which previously only lived in
// Home Assistant. Per-tent lights-on / lights-off times with the resulting photoperiod.
function toMinutes(time: string): number | null {
  const [hours, minutes] = time.split(':').map(Number)
  return Number.isFinite(hours) && Number.isFinite(minutes) ? hours * 60 + minutes : null
}

function formatHours(minutes: number): string {
  const hours = minutes / 60
  // Deutsch: 18,5/5,5 — nicht 18.5/5.5.
  return Number.isInteger(hours) ? String(hours) : hours.toFixed(1).replace('.', ',')
}

function photoperiod(on: string, off: string): string | null {
  const onAt = toMinutes(on)
  const offAt = toMinutes(off)
  if (onAt == null || offAt == null) return null
  const onMinutes = (offAt - onAt + 1440) % 1440
  // Gleiche An- und Aus-Zeit heisst NICHT 24 Stunden Licht, sondern: der
  // Plan ist unbekannt. Vorher stand hier `onMinutes = 1440`, und die Kachel
  // behauptete daraufhin volle Dauerbeleuchtung — bei einem Zelt, an dem
  // schlicht nichts eingetragen war. Das Backend macht es an derselben
  // Stelle richtig (GrowCostService.Lichtstunden gibt dort nichts zurueck).
  if (onMinutes === 0) return null
  return `${formatHours(onMinutes)}/${formatHours(1440 - onMinutes)}`
}

export function LightScheduleSection({ tentId }: { tentId: number }) {
  const [schedules, setSchedules] = useState<LightScheduleDto[]>([])
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [reloadKey, setReloadKey] = useState(0)
  const [addDraft, setAddDraft] = useState({ name: 'Lichtzyklus', lightsOnTime: '06:00', lightsOffTime: '00:00' })

  useEffect(() => {
    const controller = new AbortController()
    async function load() {
      try {
        const list = await apiFetch<LightScheduleDto[]>(`/api/light-schedules?tentId=${tentId}`, { signal: controller.signal })
        if (!controller.signal.aborted) setSchedules(list)
      } catch (caught) {
        if (!controller.signal.aborted) setError(caught instanceof Error ? caught.message : 'Lichtzyklus konnte nicht geladen werden.')
      }
    }
    void load()
    return () => controller.abort()
  }, [tentId, reloadKey])

  const reload = () => setReloadKey((key) => key + 1)
  const patchSchedule = (id: number, patch: Partial<LightScheduleDto>) =>
    setSchedules((current) => current.map((schedule) => (schedule.id === id ? { ...schedule, ...patch } : schedule)))

  async function saveExisting(schedule: LightScheduleDto) {
    setBusy(`save-${schedule.id}`)
    setError(null)
    try {
      await apiFetch(`/api/light-schedules/${schedule.id}`, {
        method: 'PUT',
        body: JSON.stringify({
          name: schedule.name.trim() || 'Lichtzyklus',
          isActive: schedule.isActive,
          lightsOnTime: schedule.lightsOnTime,
          lightsOffTime: schedule.lightsOffTime,
          source: schedule.source,
        }),
      })
      reload()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Lichtzyklus konnte nicht gespeichert werden.')
    } finally {
      setBusy(null)
    }
  }

  /**
   * Einen Lichtplan entfernen.
   *
   * Der Server lehnt den letzten Plan eines Zelts ab und sagt auch warum — an
   * ihm haengen Nachtabsenkung, Lichteinbruch-Waechter und Auto-Messungen.
   * Ohne diesen Knopf konnte diese Meldung niemand je zu sehen bekommen.
   */
  async function entfernen(schedule: LightScheduleDto) {
    if (!window.confirm(`Lichtzyklus „${schedule.name}" wirklich entfernen?`)) return
    setBusy(`del-${schedule.id}`)
    setError(null)
    try {
      await apiFetch(`/api/light-schedules/${schedule.id}`, { method: 'DELETE' })
      reload()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Lichtzyklus konnte nicht entfernt werden.')
    } finally {
      setBusy(null)
    }
  }

  async function add() {
    setBusy('add')
    setError(null)
    try {
      await apiFetch('/api/light-schedules', {
        method: 'POST',
        body: JSON.stringify({
          tentId,
          name: addDraft.name.trim() || 'Lichtzyklus',
          isActive: true,
          lightsOnTime: addDraft.lightsOnTime,
          lightsOffTime: addDraft.lightsOffTime,
          source: 'Manual',
        }),
      })
      setAddDraft({ name: 'Lichtzyklus', lightsOnTime: '06:00', lightsOffTime: '00:00' })
      reload()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Lichtzyklus konnte nicht angelegt werden.')
    } finally {
      setBusy(null)
    }
  }

  return (
    <V1Section title="Lichtzyklus">
      {error && <V1Alert message={error} tone="warn" />}
      <div style={{ display: 'grid', gap: 12 }}>
        {schedules.map((schedule) => {
          const cycle = photoperiod(schedule.lightsOnTime, schedule.lightsOffTime)
          return (
            <V1Card key={schedule.id}>
              <div className="v1-card-title-row">
                <div><span className="v1-card-kicker">{schedule.source === 'HomeAssistant' ? 'aus Home Assistant' : 'manuell'}</span><h2>{schedule.name || 'Lichtzyklus'}</h2></div>
                {cycle && <V1Badge tone="accent">{cycle}</V1Badge>}
              </div>
              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 140px), 1fr))', gap: 12 }}>
                <V1Field label="Name"><input value={schedule.name} onChange={(event) => patchSchedule(schedule.id, { name: event.target.value })} /></V1Field>
                <V1Field label="Licht AN"><input type="time" value={schedule.lightsOnTime} onChange={(event) => patchSchedule(schedule.id, { lightsOnTime: event.target.value })} /></V1Field>
                <V1Field label="Licht AUS"><input type="time" value={schedule.lightsOffTime} onChange={(event) => patchSchedule(schedule.id, { lightsOffTime: event.target.value })} /></V1Field>
              </div>
              <div className="v1-action-row" style={{ marginTop: 10 }}>
                <V1Button variant="primary" disabled={busy === `save-${schedule.id}`} onClick={() => void saveExisting(schedule)}>{busy === `save-${schedule.id}` ? 'Speichert…' : 'Speichern'}</V1Button>
                <V1Button variant="ghost" disabled={busy === `del-${schedule.id}`} onClick={() => void entfernen(schedule)}>{busy === `del-${schedule.id}` ? 'Entfernt…' : 'Entfernen'}</V1Button>
              </div>
            </V1Card>
          )
        })}

        <V1Card>
          <span className="v1-card-kicker">Neu</span>
          <h2>Lichtzyklus hinzufügen</h2>
          <p className="rc2-measurement-note">Trag ein, wann das Licht angeht und ausgeht. Die Lichtzyklus-Automatiken lösen daran aus.</p>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 140px), 1fr))', gap: 12 }}>
            <V1Field label="Name"><input value={addDraft.name} onChange={(event) => setAddDraft((current) => ({ ...current, name: event.target.value }))} /></V1Field>
            <V1Field label="Licht AN"><input type="time" value={addDraft.lightsOnTime} onChange={(event) => setAddDraft((current) => ({ ...current, lightsOnTime: event.target.value }))} /></V1Field>
            <V1Field label="Licht AUS"><input type="time" value={addDraft.lightsOffTime} onChange={(event) => setAddDraft((current) => ({ ...current, lightsOffTime: event.target.value }))} /></V1Field>
          </div>
          <div className="v1-action-row" style={{ marginTop: 10 }}>
            {photoperiod(addDraft.lightsOnTime, addDraft.lightsOffTime) && <V1Badge tone="neutral">{photoperiod(addDraft.lightsOnTime, addDraft.lightsOffTime)}</V1Badge>}
            <V1Button variant="secondary" disabled={busy === 'add'} onClick={() => void add()}>{busy === 'add' ? 'Legt an…' : 'Hinzufügen'}</V1Button>
          </div>
        </V1Card>
      </div>
    </V1Section>
  )
}
