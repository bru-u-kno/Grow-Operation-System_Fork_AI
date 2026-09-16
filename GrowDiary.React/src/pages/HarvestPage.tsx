import type { FormEvent } from 'react'
import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { apiFetch, ApiRequestError } from '../api'
import type { GrowDetail, HarvestDto } from '../types'
import { V1Alert, V1Badge, V1Button, V1Field, V1LinkButton, V1Page, V1Section, V1Skeleton, V1Switch } from '../components/v1'
import { summariseYield } from '../features/harvest/harvest-yield'
import { parsePlantWeights, progressLabel, serialisePlantWeights, totals, type PlantWeight } from '../features/harvest/plant-weights-model'
import { zahlOderNull, feldText } from '../zahlenfeld'
import '../features/harvest/harvest.css'

interface HarvestFormState {
  harvestedAtLocal: string
  wetWeightG: string
  dryWeightG: string
  dryDays: string
  yieldNotes: string
  rating: string
  flavorNotes: string
  effectNotes: string
  nugStructure: string
}

function HarvestPage() {
  const { growId } = useParams()
  const navigate = useNavigate()
  const [harvest, setHarvest] = useState<HarvestDto | null>(null)
  const [form, setForm] = useState<HarvestFormState | null>(null)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState<'save' | 'complete' | null>(null)
  const [error, setError] = useState<string | null>(null)
  // Fork AI (Grow-Plan): den eingefrorenen Endstand gleich als Programm ablegen.
  const [alsProgramm, setAlsProgramm] = useState(false)
  const [programmName, setProgrammName] = useState('')
  // Einzelgewichte je Pflanze. Am Trockenregal wiegt man Pflanze fuer Pflanze;
  // die Summe wandert in die Grow-Felder, damit Auswertungen unveraendert
  // weiterrechnen.
  /* Die Zeilen halten den GETIPPTEN Text neben der Zahl.
     Vorher hing das Feld direkt an `wetG`, einer Zahl — und wandelte bei jedem
     Tastendruck um. Die Zwischenform „21," wurde zu 21, das Komma verschwand
     aus dem Feld, die naechste Ziffer haengte sich an: aus „21,5" wurden 215.
     Nachkommastellen waren in dieser Tabelle ueberhaupt nicht eingebbar, und
     die Summe uebernahm den Fehler ins Grow-Feld. */
  const [plants, setPlants] = useState<ZeileMitText[]>([])

  useEffect(() => {
    if (!growId) return
    const controller = new AbortController()

    async function load() {
      setLoading(true)
      try {
        // Beides gleichzeitig: der Grow haengt nicht am Ernteeintrag, und zwei
        // Rundreisen nacheinander sind auf dem Pi ueber WLAN eine Sekunde zu viel.
        const [nextHarvest, grow] = await Promise.all([
          apiFetch<HarvestDto>(`/api/grows/${growId}/harvest`, { signal: controller.signal }),
          apiFetch<GrowDetail>(`/api/grows/${growId}`, { signal: controller.signal }).catch(() => null),
        ])
        setHarvest(nextHarvest)
        setForm({
          harvestedAtLocal: nextHarvest.harvestedAtLocal,
          wetWeightG: formatDraftNumber(nextHarvest.wetWeightG),
          dryWeightG: formatDraftNumber(nextHarvest.dryWeightG),
          dryDays: formatDraftNumber(nextHarvest.dryDays),
          yieldNotes: nextHarvest.yieldNotes ?? '',
          rating: formatDraftNumber(nextHarvest.rating),
          flavorNotes: nextHarvest.flavorNotes ?? '',
          effectNotes: nextHarvest.effectNotes ?? '',
          nugStructure: nextHarvest.nugStructure ?? '',
        })
        setPlants(parsePlantWeights(nextHarvest.plantWeightsJson, grow?.plantCount ?? 1).map(mitText))
        setError(null)
      } catch (caught) {
        if (controller.signal.aborted) return
        setError(caught instanceof ApiRequestError ? caught.message : 'Ernte-Daten konnten nicht geladen werden.')
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }

    void load()
    return () => controller.abort()
  }, [growId])

  async function save(complete: boolean) {
    if (!growId || !form) return

    setSaving(complete ? 'complete' : 'save')
    setError(null)
    try {
      await apiFetch<HarvestDto>(`/api/grows/${growId}/harvest`, {
        method: 'PUT',
        body: JSON.stringify({
          harvestedAtLocal: form.harvestedAtLocal,
          // Sind Einzelgewichte da, gewinnen ihre Summen — sonst bliebe das
          // Grow-Feld auf einem alten Wert stehen, waehrend die Tabelle daneben
          // etwas anderes zeigt.
          wetWeightG: sums.wetG ?? zahlOderNull(form.wetWeightG),
          dryWeightG: sums.dryG ?? zahlOderNull(form.dryWeightG),
          plantWeightsJson: serialisePlantWeights(plants.map(ohneText)),
          dryDays: parseNullableInteger(form.dryDays),
          yieldNotes: trimToNull(form.yieldNotes),
          rating: zahlOderNull(form.rating),
          flavorNotes: trimToNull(form.flavorNotes),
          effectNotes: trimToNull(form.effectNotes),
          nugStructure: trimToNull(form.nugStructure),
        }),
      })
      // Closing the loop: finishing the harvest completes the grow and moves it to the
      // archive (idempotent server-side — only Planning/Running grows change).
      if (complete) {
        await apiFetch(`/api/grows/${growId}/archive`, { method: 'POST' })
      }
      // Fork AI (Grow-Plan): erst nach dem Abschluss — dann ist der Plan eingefroren.
      if (alsProgramm && programmName.trim() !== '') {
        await apiFetch(`/api/grows/${growId}/plan/als-programm`, {
          method: 'POST',
          body: JSON.stringify({ name: programmName.trim() }),
        })
      }
      if (complete) {
        navigate('/archiv')
      } else {
        navigate(`/grows/${growId}`)
      }
    } catch (caught) {
      setError(caught instanceof ApiRequestError ? caught.message : 'Ernte konnte nicht gespeichert werden.')
    } finally {
      setSaving(null)
    }
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    void save(false)
  }

  function patchPlant(index: number, patch: Partial<ZeileMitText>) {
    setPlants((current) => current.map((plant, position) => (position === index ? { ...plant, ...patch } : plant)))
  }

  const sums = totals(plants.map(ohneText))
  const backTo = growId ? `/grows/${growId}` : '/'
  const yieldSummary = form ? summariseYield(form.wetWeightG, form.dryWeightG) : null

  return (
    <V1Page
      eyebrow="Abschluss"
      title="Ernte erfassen"
      subtitle={harvest?.growName ?? undefined}
      action={<V1LinkButton to={backTo}>Zurück zum Grow</V1LinkButton>}
    >
      {error && <V1Alert title="Fehler" message={error} tone="warn" />}

      {loading || !form ? (
        <V1Skeleton tiles={4} rows={3} label="Lade Ernte" />
      ) : (
        <form onSubmit={handleSubmit}>
          <V1Section title="Gewicht & Trocknung">
            <div className="v1-form-grid">
              <V1Field label="Erntedatum">
                <input type="date" value={form.harvestedAtLocal} onChange={(event) => setForm((current) => current ? { ...current, harvestedAtLocal: event.target.value } : current)} />
              </V1Field>
              <V1Field label="Trocknungsdauer" hint="Tage">
                <input inputMode="numeric" value={form.dryDays} onChange={(event) => setForm((current) => current ? { ...current, dryDays: event.target.value } : current)} />
              </V1Field>
              <V1Field label="Frischgewicht" hint="Gramm">
                <input inputMode="decimal" value={form.wetWeightG} onChange={(event) => setForm((current) => current ? { ...current, wetWeightG: event.target.value } : current)} />
              </V1Field>
              <V1Field label="Trockengewicht" hint="Gramm">
                <input inputMode="decimal" value={form.dryWeightG} onChange={(event) => setForm((current) => current ? { ...current, dryWeightG: event.target.value } : current)} />
              </V1Field>
            </div>

            {/* Das Verhältnis sagt mehr über die Trocknung als jede der beiden
                Zahlen für sich — und es auszurechnen, während man tippt, ist
                genau das, was die App abnehmen kann. */}
            {yieldSummary && (
              <div className="v1-chip-row" data-audit="harvest-ratio">
                <span>{yieldSummary.text}</span>
              </div>
            )}
          </V1Section>

          <V1Section title="Ernte pro Pflanze" action={<V1Badge tone="neutral">{progressLabel(sums)}</V1Badge>}>
            <div className="hv-table-wrap">
              <table className="hv-table" data-audit="harvest-plants">
                <thead>
                  <tr>
                    <th scope="col">Pflanze</th>
                    <th scope="col">Nass (g)</th>
                    <th scope="col">Trocken (g)</th>
                  </tr>
                </thead>
                <tbody>
                  {plants.map((plant, index) => (
                    <tr key={plant.label}>
                      <th scope="row">
                        <input
                          value={plant.label}
                          aria-label={`Kennung Pflanze ${index + 1}`}
                          onChange={(event) => patchPlant(index, { label: event.target.value })}
                        />
                      </th>
                      <td>
                        <input
                          inputMode="decimal"
                          value={plant.wetText}
                          aria-label={`Nassgewicht ${plant.label}`}
                          onChange={(event) => patchPlant(index, {
                            wetText: event.target.value,
                            wetG: zahlOderNull(event.target.value),
                          })}
                        />
                      </td>
                      <td>
                        <input
                          inputMode="decimal"
                          value={plant.dryText}
                          aria-label={`Trockengewicht ${plant.label}`}
                          onChange={(event) => patchPlant(index, {
                            dryText: event.target.value,
                            dryG: zahlOderNull(event.target.value),
                          })}
                        />
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <div className="hv-sums">
              <div><span>Nass gesamt</span><strong>{gramm(sums.wetG)}<em>g</em></strong></div>
              <div>
                <span>{sums.dryG != null ? 'Trocken gesamt' : 'Erwartet trocken'}</span>
                <strong>{sums.dryG != null ? gramm(sums.dryG) : (sums.expectedDryG != null ? `~${gramm(sums.expectedDryG)}` : '—')}<em>g</em></strong>
              </div>
            </div>

            <div className="v1-action-row">
              <V1Button type="button" onClick={() => setPlants((current) => [...current, { label: `PL-${String(current.length + 1).padStart(2, '0')}`, wetG: null, dryG: null, wetText: '', dryText: '' }])}>
                Pflanze ergänzen
              </V1Button>
            </div>
          </V1Section>

          <V1Section title="Bewertung">
            <div className="v1-form-grid">
              <V1Field label="Bewertung" hint="von 10">
                <input inputMode="numeric" value={form.rating} onChange={(event) => setForm((current) => current ? { ...current, rating: event.target.value } : current)} />
              </V1Field>
              <V1Field label="Blütenstruktur">
                <input value={form.nugStructure} onChange={(event) => setForm((current) => current ? { ...current, nugStructure: event.target.value } : current)} />
              </V1Field>
              <V1Field label="Ertrag-Notizen" wide>
                <textarea rows={3} value={form.yieldNotes} onChange={(event) => setForm((current) => current ? { ...current, yieldNotes: event.target.value } : current)} />
              </V1Field>
              <V1Field label="Geschmack / Aroma" wide>
                <textarea rows={3} value={form.flavorNotes} onChange={(event) => setForm((current) => current ? { ...current, flavorNotes: event.target.value } : current)} />
              </V1Field>
              <V1Field label="Effekt / High" wide>
                <textarea rows={3} value={form.effectNotes} onChange={(event) => setForm((current) => current ? { ...current, effectNotes: event.target.value } : current)} />
              </V1Field>
            </div>
          </V1Section>

          {/* Fork AI (Grow-Plan): was mit dem Plan passiert. */}
          <V1Section title="Plan dieses Grows">
            <div className="harvest-plan" data-audit="ernte-plan">
              <p>Mit der ersten Ernte wird der Grow abgeschlossen und sein Plan eingefroren — die Auswertung steht danach auf der Grow-Seite.</p>
              <V1Switch
                label="Endstand als Programm speichern"
                hint="Wird unter „Eigene Programme“ für den nächsten Grow angeboten."
                checked={alsProgramm}
                onChange={(an) => {
                  setAlsProgramm(an)
                  if (an && programmName === '') setProgrammName(`${harvest?.growName ?? 'Grow'} · Endstand`)
                }}
              />
              {alsProgramm && (
                <V1Field label="Name des Programms">
                  <input value={programmName} maxLength={80} onChange={(e) => setProgrammName(e.target.value)} />
                </V1Field>
              )}
            </div>
          </V1Section>

          <div className="v1-form-actions">
            <V1LinkButton to={backTo}>Abbrechen</V1LinkButton>
            <V1Button type="submit" disabled={saving !== null}>{saving === 'save' ? 'Speichert…' : 'Ernte speichern'}</V1Button>
            <V1Button type="button" variant="primary" disabled={saving !== null} onClick={() => void save(true)}>{saving === 'complete' ? 'Schließt ab…' : 'Speichern & Grow abschließen'}</V1Button>
          </div>
        </form>
      )}
    </V1Page>
  )
}


/**
 * Eine Zahl fuer ein Eingabefeld — mit Komma.
 *
 * <b>Der Anlass (01.09.2026).</b> Hier stand <code>String(value)</code>: ein
 * gespeichertes Nassgewicht von 21,5 g kam als „21.5" ins Feld zurueck,
 * direkt neben der Spalte, die „21,5" schreibt. Wer nichts aenderte und
 * speicherte, schickte den Punkt wieder los.
 */
function formatDraftNumber(value: number | null | undefined) {
  return feldText(value)
}

/** Ein Gewicht, wie es in Deutschland geschrieben wird. */
function gramm(value: number | null | undefined) {
  if (value == null || Number.isNaN(value)) return '—'
  return value.toLocaleString('de-DE', { maximumFractionDigits: 1 })
}

function parseNullableInteger(value: string) {
  const trimmed = value.trim()
  if (!trimmed) return null
  const parsed = Number.parseInt(trimmed, 10)
  return Number.isNaN(parsed) ? null : parsed
}

function trimToNull(value: string) {
  const trimmed = value.trim()
  return trimmed ? trimmed : null
}

export default HarvestPage

/** Leeres Feld heisst „noch nicht gewogen", nicht „null Gramm". */
/**
 * Eine Erntezeile, wie die Seite sie haelt: die Zahlen des Modells plus den
 * Text, den der Nutzer gerade tippt.
 *
 * Ohne den Text kann man kein Komma eingeben — das Feld haengt sonst an einer
 * Zahl und wirft die Zwischenform bei jedem Tastendruck weg.
 */
type ZeileMitText = PlantWeight & { wetText: string; dryText: string }

/** Aus einer Modellzeile eine Zeile mit Text machen. */
function mitText(zeile: PlantWeight): ZeileMitText {
  return { ...zeile, wetText: alsText(zeile.wetG), dryText: alsText(zeile.dryG) }
}

/** Und zurueck — fuer alles, was das Modell erwartet. */
function ohneText(zeile: ZeileMitText): PlantWeight {
  return { label: zeile.label, wetG: zeile.wetG, dryG: zeile.dryG }
}

function alsText(wert: number | null): string {
  return wert == null ? '' : String(wert).replace('.', ',')
}
