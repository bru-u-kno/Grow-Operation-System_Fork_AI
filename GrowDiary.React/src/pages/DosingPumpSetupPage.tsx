import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { apiFetch } from '../api'
import type { TentDto } from '../types'
import { V1Alert, V1Button, V1Card, V1Field, V1Page, V1Section, V1Skeleton, V1Switch } from '../components/v1'
import '../features/dosing/dosing.css'
import { feldText, unlesbarMeldung, unlesbareFelder, zahlOderNull } from '../zahlenfeld'

/**
 * Eine Pumpe einrichten: was sie tut, was drin ist, wen sie in Home Assistant
 * schaltet — und die Anschläge, die immer gelten.
 */

type HaEntity = { entityId: string; friendlyName: string | null; domain: string }

type Form = {
  tentId: number | null
  name: string
  purpose: string
  agent: string
  concentrationPercent: string
  costPerLiterEur: string
  haEntityId: string
  maxSingleDoseMl: string
  minIntervalMinutes: string
  maxDosesPerDay: string
  maxMlPerDay: string
  hasHomeAssistantAutoOff: boolean
  automationEnabled: boolean
  maxReadingAgeMinutes: string
  simulationMode: boolean
  tubeChangedNow: boolean
  partnerPumpId: string
  partnerRatio: string
  partnerDelayMinutes: string
}

const PURPOSES = [
  { value: 'PhDown', label: 'pH senken (Säure)' },
  { value: 'PhUp', label: 'pH heben (Lauge)' },
  { value: 'Nutrient', label: 'Nährstoff' },
  { value: 'CalMag', label: 'CalMag' },
  { value: 'Custom', label: 'frei — nur von Hand' },
]

function leer(): Form {
  return {
    tentId: null, name: '', purpose: 'PhDown', agent: '', concentrationPercent: '', costPerLiterEur: '',
    haEntityId: '', maxSingleDoseMl: '5', minIntervalMinutes: '18',
    maxDosesPerDay: '6', maxMlPerDay: '25', hasHomeAssistantAutoOff: false,
    automationEnabled: false, maxReadingAgeMinutes: '10',
    simulationMode: false, tubeChangedNow: false,
    partnerPumpId: '', partnerRatio: '1', partnerDelayMinutes: '5',
  }
}

// zahlOderNull kommt aus `src/zahlenfeld.ts`. Die frühere Fassung hier hatte
// KEINE Leerprüfung: `Number('')` ist 0 und `Number.isFinite(0)` ist true. Ein
// geleertes Feld wurde damit zur Null — beim Mindestabstand heisst das keine
// Mischpause mehr, still, mit Erfolgsmeldung.

function DosingPumpSetupPage() {
  const { pumpId } = useParams<{ pumpId: string }>()
  const navigate = useNavigate()
  const bearbeiten = Boolean(pumpId)
  const [form, setForm] = useState<Form>(leer())
  const [tents, setTents] = useState<TentDto[]>([])
  const [entities, setEntities] = useState<HaEntity[]>([])
  // Die uebrigen Pumpen desselben Zelts — nur die kommen als Partner infrage.
  const [alle, setAlle] = useState<Array<{ id: number; name: string; tentId: number }>>([])
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    async function laden() {
      try {
        const [tentData, entityData, pumps, pump] = await Promise.all([
          apiFetch<TentDto[]>('/api/settings/tents', { signal: controller.signal }),
          apiFetch<HaEntity[]>('/api/home-assistant/entities', { signal: controller.signal }).catch(() => []),
          apiFetch<Array<{ id: number; name: string; tentId: number }>>('/api/dosing/pumps', { signal: controller.signal }).catch(() => []),
          bearbeiten ? apiFetch<Record<string, unknown>>(`/api/dosing/pumps/${pumpId}`, { signal: controller.signal }) : Promise.resolve(null),
        ])
        if (controller.signal.aborted) return
        setTents(tentData)
        setEntities(entityData)
        setAlle(pumps)
        if (pump) {
          setForm({
            tentId: pump.tentId as number,
            name: (pump.name as string) ?? '',
            purpose: (pump.purpose as string) ?? 'Custom',
            agent: (pump.agent as string) ?? '',
            concentrationPercent: feldText(pump.concentrationPercent as number | null | undefined),
            costPerLiterEur: pump.costPerLiterEur != null ? String(pump.costPerLiterEur).replace('.', ',') : '',
            haEntityId: (pump.haEntityId as string) ?? '',
            maxSingleDoseMl: feldText((pump.maxSingleDoseMl as number | null | undefined) ?? 5),
            minIntervalMinutes: String(pump.minIntervalMinutes ?? 18),
            maxDosesPerDay: String(pump.maxDosesPerDay ?? 6),
            maxMlPerDay: feldText((pump.maxMlPerDay as number | null | undefined) ?? 25),
            hasHomeAssistantAutoOff: Boolean(pump.hasHomeAssistantAutoOff),
            automationEnabled: Boolean(pump.automationEnabled),
            maxReadingAgeMinutes: String(pump.maxReadingAgeMinutes ?? 10),
            partnerPumpId: pump.partnerPumpId ? String(pump.partnerPumpId) : '',
            partnerRatio: String(pump.partnerRatio ?? 1).replace('.', ','),
            partnerDelayMinutes: String(pump.partnerDelayMinutes ?? 5),
            simulationMode: Boolean(pump.simulationMode),
            tubeChangedNow: false,
          })
        } else {
          setForm((current) => ({ ...current, tentId: tentData[0]?.id ?? null }))
        }
      } catch (caught) {
        if (!controller.signal.aborted) setError(caught instanceof Error ? caught.message : 'Konnte nicht laden.')
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }
    void laden()
    return () => controller.abort()
  }, [pumpId, bearbeiten])

  function patch(next: Partial<Form>) {
    setForm((current) => ({ ...current, ...next }))
  }

  async function speichern() {
    if (!form.name.trim()) { setError('Die Pumpe braucht einen Namen.'); return }
    if (!form.simulationMode && !form.haEntityId.trim()) {
      setError('Ohne Home-Assistant-Entität lässt sich nichts schalten — oder schalte den Testbetrieb ein.')
      return
    }
    if (form.tentId == null) { setError('Wähle ein Zelt.'); return }

    // Dieselbe Sperre wie im Messformular. Hier wiegt sie schwerer: ein
    // unlesbarer Mindestabstand wurde zu `null`, das Backend liess ihn durch,
    // und `DosingService` prüft `seit < TimeSpan.FromMinutes(0)` — nie wahr.
    // Die Pumpe fuhr dann ohne Mischpause, still, mit Erfolgsmeldung.
    const meldung = unlesbarMeldung(unlesbareFelder([
      [form.concentrationPercent, 'Konzentration'],
      [form.costPerLiterEur, 'Preis je Liter'],
      [form.maxSingleDoseMl, 'Höchstmenge je Gabe'],
      [form.minIntervalMinutes, 'Mindestabstand'],
      [form.maxDosesPerDay, 'Gaben je Tag'],
      [form.maxMlPerDay, 'Milliliter je Tag'],
      [form.maxReadingAgeMinutes, 'Messwert höchstens alt'],
      [form.partnerRatio, 'Verhältnis zum Partner'],
      [form.partnerDelayMinutes, 'Abstand zum Partner'],
    ]))
    if (meldung) { setError(meldung); return }

    setSaving(true)
    setError(null)
    const payload = {
      tentId: form.tentId,
      name: form.name.trim(),
      purpose: form.purpose,
      agent: form.agent.trim() || null,
      concentrationPercent: zahlOderNull(form.concentrationPercent),
      costPerLiterEur: zahlOderNull(form.costPerLiterEur),
      haEntityId: form.haEntityId.trim(),
      maxSingleDoseMl: zahlOderNull(form.maxSingleDoseMl),
      minIntervalMinutes: zahlOderNull(form.minIntervalMinutes),
      maxDosesPerDay: zahlOderNull(form.maxDosesPerDay),
      maxMlPerDay: zahlOderNull(form.maxMlPerDay),
      automationEnabled: form.automationEnabled,
      // Ersatzwerte wie bisher, wenn leer oder 0 — aber mit derselben Leseregel
      // wie die Pruefung oben: `Number('7,5')` war NaN und wurde still zum
      // Ersatzwert, waehrend `unlesbareFelder` das Feld fuer lesbar hielt.
      maxReadingAgeMinutes: zahlOderNull(form.maxReadingAgeMinutes) || 10,
      partnerPumpId: form.partnerPumpId ? Number(form.partnerPumpId) : null,
      partnerRatio: zahlOderNull(form.partnerRatio) || 1,
      partnerDelayMinutes: zahlOderNull(form.partnerDelayMinutes) || 5,
      hasHomeAssistantAutoOff: form.hasHomeAssistantAutoOff,
      simulationMode: form.simulationMode,
      tubeChangedNow: form.tubeChangedNow,
    }
    try {
      if (bearbeiten) await apiFetch(`/api/dosing/pumps/${pumpId}`, { method: 'PUT', body: JSON.stringify(payload) })
      else await apiFetch('/api/dosing/pumps', { method: 'POST', body: JSON.stringify(payload) })
      navigate('/dosierung')
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Speichern fehlgeschlagen.')
      setSaving(false)
    }
  }

  async function loeschen() {
    if (!bearbeiten) return
    setSaving(true)
    try {
      await apiFetch(`/api/dosing/pumps/${pumpId}`, { method: 'DELETE' })
      navigate('/dosierung')
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Löschen fehlgeschlagen.')
      setSaving(false)
    }
  }

  if (loading) return <V1Skeleton rows={6} label="Lade Pumpe" />

  const schaltbar = entities.filter((entity) =>
    entity.domain === 'switch' || entity.domain === 'input_boolean' || entity.domain === 'light')

  // Ein Paar ueber zwei Zelte hinweg liesse B in ein anderes Becken laufen —
  // solche Pumpen stehen hier gar nicht erst zur Wahl.
  const andere = alle.filter((other) => other.id !== Number(pumpId) && other.tentId === form.tentId)

  return (
    <V1Page
      eyebrow="Dosierung"
      title={bearbeiten ? 'Pumpe einstellen' : 'Pumpe einrichten'}
      action={
        <>
          <V1Button variant="primary" onClick={() => void speichern()} disabled={saving}>
            {saving ? 'Speichert…' : 'Speichern'}
          </V1Button>
          <V1Button onClick={() => navigate('/dosierung')}>Zurück</V1Button>
        </>
      }
    >
      {error && <V1Alert message={error} tone="critical" />}

      <V1Section title="Was die Pumpe tut">
        <V1Card>
          <div className="v1-form-grid">
            <V1Field label="Name">
              <input value={form.name} onChange={(event) => patch({ name: event.target.value })} placeholder="pH Minus" />
            </V1Field>
            <V1Field label="Zelt">
              <select value={form.tentId ?? ''} onChange={(event) => patch({ tentId: Number(event.target.value) })}>
                {tents.map((tent) => <option key={tent.id} value={tent.id}>{tent.name}</option>)}
              </select>
            </V1Field>
            <V1Field label="Aufgabe" hint="Bestimmt, gegen welchen Messwert gerechnet wird.">
              <select value={form.purpose} onChange={(event) => patch({ purpose: event.target.value })}>
                {PURPOSES.map((purpose) => <option key={purpose.value} value={purpose.value}>{purpose.label}</option>)}
              </select>
            </V1Field>
            <V1Field label="Mittel" hint="Was im Kanister ist.">
              <input value={form.agent} onChange={(event) => patch({ agent: event.target.value })} placeholder="Phosphorsäure" />
            </V1Field>
            <V1Field label="Konzentration in %" hint="Steht auf dem Kanister.">
              <input inputMode="decimal" value={form.concentrationPercent}
                onChange={(event) => patch({ concentrationPercent: event.target.value })} placeholder="59" />
            </V1Field>
            <V1Field label="Preis (€ je Liter)" hint="Flaschenpreis geteilt durch Liter — daraus rechnet das Archiv die Düngerkosten je Grow.">
              <input inputMode="decimal" value={form.costPerLiterEur}
                onChange={(event) => patch({ costPerLiterEur: event.target.value })} placeholder="z. B. 18,50" />
            </V1Field>
            <V1Field
              label="Schaltet in Home Assistant"
              hint={schaltbar.length === 0 ? 'Keine Entitäten erreichbar — ist Home Assistant verbunden?' : 'Die Entität, die den Pumpenstrom schaltet.'}
            >
              <input value={form.haEntityId} onChange={(event) => patch({ haEntityId: event.target.value })}
                placeholder="switch.dosier_ph_minus" list="dosing-entities" />
              {schaltbar.length > 0 && (
                <datalist id="dosing-entities">
                  {schaltbar.map((entity) => (
                    <option key={entity.entityId} value={entity.entityId}>{entity.friendlyName ?? entity.entityId}</option>
                  ))}
                </datalist>
              )}
            </V1Field>
          </div>
        </V1Card>
      </V1Section>

      <V1Section title="Anschläge">
        <V1Card>
          <div className="v1-form-grid">
            <V1Field label="Größte Einzeldosis in ml" hint="Macht aus einem Rechenfehler eine Unannehmlichkeit statt eines Schadens.">
              <input inputMode="decimal" value={form.maxSingleDoseMl} onChange={(event) => patch({ maxSingleDoseMl: event.target.value })} />
            </V1Field>
            <V1Field label="Pause danach in Minuten" hint="Erst mischen und neu messen, dann urteilen.">
              <input inputMode="numeric" value={form.minIntervalMinutes} onChange={(event) => patch({ minIntervalMinutes: event.target.value })} />
            </V1Field>
            <V1Field label="Höchstens Dosen am Tag">
              <input inputMode="numeric" value={form.maxDosesPerDay} onChange={(event) => patch({ maxDosesPerDay: event.target.value })} />
            </V1Field>
            <V1Field label="Höchstens ml am Tag">
              <input inputMode="decimal" value={form.maxMlPerDay} onChange={(event) => patch({ maxMlPerDay: event.target.value })} />
            </V1Field>
          </div>
          {bearbeiten && (
            <div style={{ marginTop: 12 }}>
              <V1Switch
                label="Schlauch heute gewechselt"
                checked={form.tubeChangedNow}
                onChange={(checked) => patch({ tubeChangedNow: checked })}
                hint="Setzt das Schlauchdatum auf heute. Nach einem Wechsel neu kalibrieren."
              />
            </div>
          )}
        </V1Card>
      </V1Section>

      <V1Section title="Testbetrieb">
        <V1Card tone={form.simulationMode ? 'warn' : 'neutral'}>
          <V1Switch
            label="Testbetrieb — schaltet nichts, es fließt nichts"
            checked={form.simulationMode}
            onChange={(checked) => patch({ simulationMode: checked })}
            hint="Zum Durchspielen ohne Hardware: Grow OS rechnet, wartet die echte Laufzeit ab und protokolliert — schaltet aber keine Entität."
          />
          {form.simulationMode && (
            <p className="rc2-measurement-note" style={{ margin: '10px 0 0' }}>
              Testdosen sind im Protokoll als solche markiert und fließen <strong>nicht</strong> in das
              Gelernte ein. Sonst stünde dort später eine Zahl, hinter der nie ein Tropfen war.
              Eine Home-Assistant-Entität brauchst du im Testbetrieb nicht.
            </p>
          )}
        </V1Card>
      </V1Section>

      <V1Section title="Abschaltung in Home Assistant">
        <V1Card>
          <div className="dz-warn">
            <div>
              <b>Grow OS schaltet ein und Sekunden später wieder aus.</b> Stürzt es genau dazwischen ab
              oder fällt der Rechner aus, läuft die Pumpe weiter — niemand ist da, der sie stoppt.
              Richte in Home Assistant eine Abschaltung ein, die diese Entität nach spätestens
              30 Sekunden von sich aus abwirft, und hake es hier ab.
            </div>
          </div>
          <div style={{ marginTop: 12 }}>
            <V1Switch
              label="Abschaltung ist in Home Assistant eingerichtet"
              checked={form.hasHomeAssistantAutoOff}
              onChange={(checked) => patch({ hasHomeAssistantAutoOff: checked })}
              hint="Ohne diesen Haken bleibt die spätere Automatik gesperrt. Von Hand dosieren geht trotzdem."
            />
          </div>
        </V1Card>
      </V1Section>

      <V1Section title="Zweikomponenten-Dünger (A und B)">
        <V1Card>
          <div className="dz-warn">
            <div>
              <b>A und B dürfen sich nicht konzentriert begegnen.</b> Das Calcium aus A fällt mit den
              Sulfaten und Phosphaten aus B als Gips aus. Was ausgeflockt ist, kommt bei der Pflanze
              nie an — im Becken schwimmen weiße Flocken, und der EC steigt trotz Dünger kaum.
              Deshalb läuft hier nie beides gleichzeitig: diese Pumpe gibt, dann vergeht die
              Trennzeit, dann gibt der Partner nach. Auch über einen Neustart hinweg.
            </div>
          </div>
          <div style={{ marginTop: 12, display: 'grid', gap: 12 }}>
            <V1Field label="Partnerpumpe" hint="Leer lassen, wenn diese Pumpe allein arbeitet.">
              <select value={form.partnerPumpId} onChange={(event) => patch({ partnerPumpId: event.target.value })}>
                <option value="">— keine —</option>
                {andere.map((other) => <option key={other.id} value={other.id}>{other.name}</option>)}
              </select>
            </V1Field>
            {form.partnerPumpId && (
              <>
                <V1Field label="Verhältnis" hint="Wie viel der Partner je Milliliter dieser Pumpe bekommt. 1 heißt 1:1.">
                  <input inputMode="decimal" value={form.partnerRatio}
                    onChange={(event) => patch({ partnerRatio: event.target.value })} />
                </V1Field>
                <V1Field label="Trennzeit (Minuten)" hint="So lange verteilt sich A, bevor B nachkommt.">
                  <input inputMode="numeric" value={form.partnerDelayMinutes}
                    onChange={(event) => patch({ partnerDelayMinutes: event.target.value })} />
                </V1Field>
              </>
            )}
          </div>
        </V1Card>
      </V1Section>

      <V1Section title="Automatik">
        <V1Card>
          <div className="dz-warn">
            <div>
              <b>Eingeschaltet dosiert Grow OS ohne Rückfrage.</b> Es rechnet aus dem, was diese Pumpe
              an früheren Dosen gelernt hat, und gibt jeweils den halben Weg zum Ziel. Alle Anschläge
              von oben gelten weiter — dazu drei, die nur hier greifen: ohne Abschaltung in Home
              Assistant bleibt sie gesperrt, gegen einen alten Messwert wird nicht dosiert, und eine
              nie oder überfällig kalibrierte Sonde sperrt ebenfalls. Eine driftende Sonde meldet 6,0,
              während 5,4 im Becken steht — die Automatik dosierte dann überzeugt in die falsche Richtung.
            </div>
          </div>
          <div style={{ marginTop: 12, display: 'grid', gap: 12 }}>
            <V1Switch
              label="Automatik einschalten"
              checked={form.automationEnabled}
              onChange={(checked) => patch({ automationEnabled: checked })}
              hint="Jede automatische Dosis geht zusätzlich als Nachricht raus."
            />
            <V1Field label="Messwert höchstens (Minuten) alt" hint="Älter heißt: es wird nicht dosiert.">
              <input inputMode="numeric" value={form.maxReadingAgeMinutes}
                onChange={(event) => patch({ maxReadingAgeMinutes: event.target.value })} />
            </V1Field>
          </div>
        </V1Card>
      </V1Section>

      {bearbeiten && (
        <V1Section title="Entfernen">
          <V1Card>
            <V1Button variant="danger" onClick={() => void loeschen()} disabled={saving}>Pumpe löschen</V1Button>
          </V1Card>
        </V1Section>
      )}
    </V1Page>
  )
}

export default DosingPumpSetupPage
