import { useEffect, useMemo, useState } from 'react'
import { sortenText } from '../features/grows/sorten-text'
import { useParams } from 'react-router-dom'
import { apiFetch, ApiRequestError } from '../api'
import type { WaterSource } from '../types/shared'
import type {
  AddbackDefaultsDto,
  AddbackLogDto,
  AddbackResultDto,
  CreateAddbackLogRequest,
  GrowDetail,
  KnowledgeOverviewDto,
  NutrientProgramDto,
  NutrientProgramStageDto,
} from '../types'
import { V1Alert, V1Button, V1Card, V1Empty, V1Field, V1LinkButton, V1Page, V1Section, V1Skeleton, V1Stat } from '../components/v1'
import { classNames, formatNumber } from '../utils'
import { AddbackFlow, type FlowStep } from '../features/addback/AddbackFlow'
import { feldText } from '../zahlenfeld'


/** Nur die zwei Zahlen, die der Wasser-Hinweis braucht. */
type WasserprofilKurz = { conductivityUsCm: number | null; treatedConductivityUsCm: number | null }

interface AddbackFormState {
  reservoirLiters: string
  ecIst: string
  ecZiel: string
  ecStock: string
  phBefore: string
  phTarget: string
  ecAfter: string
  phAfter: string
  /** Womit aufgefuellt wurde — leer heisst „wie am Grow hinterlegt". */
  waterUsed: string
  notes: string
}

interface ComponentDraft {
  id: string
  name: string
  amountMl: string
  done: boolean
}

const genericComponents = ['Silikat / Stabilisator', 'CalMag / Basis', 'Base A', 'Base B', 'PK / Additiv', 'pH-Korrektur']

type MischplanDto = {
  programmName: string | null
  spalteLabel: string | null
  volumenLiter: number | null
  zeilen: Array<{ komponente: string; mlProLiter: string; mlGesamtMin: number; mlGesamtMax: number }>
  ecZiel: number | null
  phMin: number | null
  phMax: number | null
  herkunft: string | null
  luecke: string | null
  zieleAktiv: boolean
}

/** „255 ml" oder „204–340 ml" — die Spanne nur, wo das Chart eine nennt. */
function mlSpanne(min: number, max: number) {
  const links = min.toLocaleString('de-DE', { maximumFractionDigits: 0 })
  if (min === max) return `${links} ml`
  return `${links}–${max.toLocaleString('de-DE', { maximumFractionDigits: 0 })} ml`
}

function AddbackPage() {
  const { growId } = useParams()
  const [defaults, setDefaults] = useState<AddbackDefaultsDto | null>(null)
  const [grow, setGrow] = useState<GrowDetail | null>(null)
  // Fuer den Hinweis am Wasser-Feld: der EC des Ausgangswassers.
  const [wasserprofil, setWasserprofil] = useState<WasserprofilKurz | null>(null)
  const [knowledge, setKnowledge] = useState<KnowledgeOverviewDto | null>(null)
  const [logs, setLogs] = useState<AddbackLogDto[]>([])
  const [programKey, setProgramKey] = useState<string>('custom')
  const [form, setForm] = useState<AddbackFormState>({
    reservoirLiters: '',
    ecIst: '',
    ecZiel: '',
    ecStock: '3',
    waterUsed: '',
    phBefore: '',
    // Der Vorgabewert steht in einem EINGABEFELD — also mit Komma.
    phTarget: '5,8',
    ecAfter: '',
    phAfter: '',
    notes: '',
  })
  const [components, setComponents] = useState<ComponentDraft[]>(createComponents(genericComponents))
  const [result, setResult] = useState<AddbackResultDto | null>(null)
  const [mischplan, setMischplan] = useState<MischplanDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [calculating, setCalculating] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [success, setSuccess] = useState<string | null>(null)

  /**
   * Der Schalter schreibt am Grow und bekommt den frischen Plan zurück — so
   * steht nie ein Haken, den der Server nicht bestätigt hat.
   */
  async function zieleUmschalten(use: boolean) {
    try {
      setMischplan(await apiFetch<MischplanDto>(`/api/grows/${growId}/mixing-plan/use-targets`, {
        method: 'PUT',
        body: JSON.stringify({ use }),
      }))
    } catch {
      /* Der Haken bleibt, wo er war. */
    }
  }


  useEffect(() => {
    if (!growId) return
    const controller = new AbortController()

    async function load() {
      setLoading(true)
      setError(null)
      setSuccess(null)
      try {
        const [nextDefaults, nextGrow, nextLogs, nextKnowledge, nextPlan, nextWasser] = await Promise.all([
          apiFetch<AddbackDefaultsDto>(`/api/grows/${growId}/addback`, { signal: controller.signal }),
          apiFetch<GrowDetail>(`/api/grows/${growId}`, { signal: controller.signal }),
          apiFetch<AddbackLogDto[]>(`/api/grows/${growId}/addback/logs`, { signal: controller.signal }).catch(() => []),
          apiFetch<KnowledgeOverviewDto>('/api/knowledge', { signal: controller.signal }).catch(() => null),
          apiFetch<MischplanDto>(`/api/grows/${growId}/mixing-plan`, { signal: controller.signal }).catch(() => null),
          // Ohne Profil bleibt der Wasser-Hinweis allgemein — kein Grund, die
          // Seite daran scheitern zu lassen.
          apiFetch<WasserprofilKurz>('/api/water-profile', { signal: controller.signal }).catch(() => null),
        ])

        if (controller.signal.aborted) return

        setDefaults(nextDefaults)
        setGrow(nextGrow)
        setLogs(nextLogs)
        setKnowledge(nextKnowledge)
        setMischplan(nextPlan)
        setWasserprofil(nextWasser)

        // Fork AI: Zuerst die am Grow gespeicherte Programm-Id — nur wenn die fehlt,
        // faellt die Erkennung auf den Freitext zurueck. Sonst gewinnt bei
        // „SKX Canna Aqua" der Teilstring-Treffer „Canna Aqua", und die Seite
        // zeigt ein anderes Programm an als der Mischplan tatsaechlich rechnet.
        const programById = nextGrow.feedProgramId
          ? (nextKnowledge?.programs ?? []).find((program) => program.key === nextGrow.feedProgramId) ?? null
          : null
        const matchedProgram = programById ?? matchProgram(nextKnowledge?.programs ?? [], nextGrow.nutrients)
        const initialProgramKey = matchedProgram?.key ?? 'custom'
        setProgramKey(initialProgramKey)

        setForm({
          reservoirLiters: draftNumber(nextDefaults.reservoirLiters),
          ecIst: draftNumber(nextDefaults.ecIst),
          ecZiel: draftNumber(nextDefaults.ecZiel),
          ecStock: draftNumber(nextDefaults.ecStock),
          waterUsed: '',
          phBefore: draftNumber(nextGrow.latestMeasurement?.reservoirPh),
          phTarget: derivePhTarget(matchedProgram) ?? '5,8',
          ecAfter: '',
          phAfter: '',
          notes: '',
        })

        setComponents(createComponents(componentNamesForProgram(matchedProgram)))
      } catch (caught) {
        if (!controller.signal.aborted) {
          setError(caught instanceof ApiRequestError ? caught.message : 'Addback-Daten konnten nicht geladen werden.')
        }
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }

    void load()
    return () => controller.abort()
  }, [growId])

  const programs = useMemo(() => knowledge?.programs ?? [], [knowledge?.programs])
  const selectedProgram = useMemo(
    () => programs.find((program) => program.key === programKey) ?? null,
    [programKey, programs],
  )

  const selectedStage = useMemo(
    () => findProgramStage(selectedProgram, grow?.latestMeasurement?.stage ?? null),
    [selectedProgram, grow?.latestMeasurement?.stage],
  )

  /**
   * Was am Grow als Wasserquelle steht — und was Grow OS ueber dessen EC weiss.
   *
   * Der Hinweis nennt die Zahl aus dem Wasserprofil, damit niemand sie
   * nachschlagen muss: bei Osmose zaehlt der Wert NACH der Anlage, sonst der
   * aus dem Stadtbericht.
   */
  const wasserAmGrow = grow?.waterSource === 'RO' ? 'Osmose'
    : grow?.waterSource === 'Mixed' ? 'Mischung'
      : 'Leitungswasser'

  const wasserHinweis = useMemo(() => {
    if (!wasserprofil) return 'Nur für dieses Auffüllen — der Grow behält seine Quelle.'
    const quelle = (form.waterUsed || grow?.waterSource) ?? 'Tap'
    const mikro = quelle === 'RO'
      ? wasserprofil.treatedConductivityUsCm
      : wasserprofil.treatedConductivityUsCm ?? wasserprofil.conductivityUsCm
    return mikro == null
      ? 'Nur für dieses Auffüllen — der Grow behält seine Quelle.'
      : `Startet bei ${formatNumber(mikro / 1000, 2)} mS/cm laut deinem Wasserprofil.`
  }, [wasserprofil, form.waterUsed, grow?.waterSource])

  const lastLog = logs[0] ?? null
  const hasCalculatedResult = result !== null && !result.errorMessage

  function updateForm<K extends keyof AddbackFormState>(key: K, value: AddbackFormState[K]) {
    setForm((current) => ({ ...current, [key]: value }))
    if (['reservoirLiters', 'ecIst', 'ecZiel', 'ecStock'].includes(key)) {
      setResult(null)
    }
  }

  function updateComponent(id: string, update: Partial<ComponentDraft>) {
    setComponents((current) => current.map((component) => (component.id === id ? { ...component, ...update } : component)))
  }

  function handleProgramChange(nextKey: string) {
    setProgramKey(nextKey)
    const nextProgram = programs.find((program) => program.key === nextKey) ?? null
    setComponents(createComponents(componentNamesForProgram(nextProgram)))
    const nextPhTarget = derivePhTarget(nextProgram)
    if (nextPhTarget) updateForm('phTarget', nextPhTarget)
  }

  async function calculateAddback(): Promise<AddbackResultDto | null> {
    if (!growId) return null

    const validation = validateCalculation(form)
    if (validation) {
      setError(validation)
      return null
    }

    setCalculating(true)
    setError(null)
    setSuccess(null)
    try {
      const nextResult = await apiFetch<AddbackResultDto>(`/api/grows/${growId}/addback/calculate`, {
        method: 'POST',
        body: JSON.stringify({
          reservoirLiters: parseNullableNumber(form.reservoirLiters),
          ecIst: parseNullableNumber(form.ecIst),
          ecZiel: parseNullableNumber(form.ecZiel),
          ecStock: parseNullableNumber(form.ecStock),
        }),
      })
      setResult(nextResult)
      if (nextResult.errorMessage) {
        setError(nextResult.errorMessage)
        return null
      }
      return nextResult
    } catch (caught) {
      const message = caught instanceof ApiRequestError ? caught.message : 'Addback konnte nicht berechnet werden.'
      setError(message)
      return null
    } finally {
      setCalculating(false)
    }
  }

  /**
   * Prüfen und rechnen in einem — vorher waren das zwei Wizard-Schritte, durch
   * die man sich klicken musste. Auf einer Seite genügt ein Knopf.
   */
  async function checkAndCalculate() {
    setError(null)
    const validation = validateActuals(form)
    if (validation) {
      setError(validation)
      return
    }
    await calculateAddback()
  }

  async function handleSave() {
    if (!growId) return

    let calculation = result
    if (!calculation || calculation.errorMessage) {
      calculation = await calculateAddback()
      if (!calculation || calculation.errorMessage) return
    }

    setSaving(true)
    setError(null)
    setSuccess(null)
    try {
      const payload: CreateAddbackLogRequest = {
        kind: 'Addback',
        performedAtUtc: new Date().toISOString(),
        reservoirLiters: parseNullableNumber(form.reservoirLiters),
        ecBefore: parseNullableNumber(form.ecIst),
        ecTarget: parseNullableNumber(form.ecZiel),
        ecStock: parseNullableNumber(form.ecStock),
        ecAfter: parseNullableNumber(form.ecAfter),
        phBefore: parseNullableNumber(form.phBefore),
        phAfter: parseNullableNumber(form.phAfter),
        litersAdded: calculation.litersToAdd,
        newReservoirVolumeLiters: calculation.newReservoirVolume,
        usedHydroSetupVolume: defaults?.suggestedReservoirLiters != null,
        waterUsed: form.waterUsed === '' ? null : (form.waterUsed as WaterSource),
        notes: buildNotes(selectedProgram, selectedStage, components, form.notes),
      }

      const created = await apiFetch<AddbackLogDto>(`/api/grows/${growId}/addback/logs`, {
        method: 'POST',
        body: JSON.stringify(payload),
      })

      setLogs((current) => [created, ...current])
      setSuccess('Addback gespeichert.')
      // Zurück zum Start des Assistenten statt in der Speichern-Maske zu verharren;
      // der neue Log erscheint im Kontext-Rail als "Letzter Log".
    } catch (caught) {
      setError(caught instanceof ApiRequestError ? caught.message : 'Addback konnte nicht gespeichert werden.')
    } finally {
      setSaving(false)
    }
  }

  if (!growId) {
    return (
      <V1Page eyebrow="Reservoir" title="Addback">
        <V1Alert tone="warn" message="Kein Grow ausgewählt." />
        <V1LinkButton to="/addback" variant="primary">Zur Grow-Auswahl</V1LinkButton>
      </V1Page>
    )
  }

  // Aus dem vorhandenen Zustand abgeleitet, keine zweite Datenquelle.
  const ecIst = parseNullableNumber(form.ecIst) ?? defaults?.suggestedEcIst ?? null
  const ecZiel = parseNullableNumber(form.ecZiel) ?? defaults?.suggestedEcZiel ?? null
  const ecAfter = parseNullableNumber(form.ecAfter)
  const flowSteps: FlowStep[] = [
    {
      title: 'MESSEN',
      value: ecIst != null ? `EC ${formatNumber(ecIst, 2)}` : '—',
      note: ecIst != null ? 'Istwert der Nährlösung' : 'EC eintragen',
      done: ecIst != null,
      current: ecIst == null,
    },
    {
      title: 'ZIEL',
      value: ecZiel != null ? `EC ${formatNumber(ecZiel, 2)}` : '—',
      note: selectedProgram?.name ?? grow?.nutrients ?? undefined,
      done: ecZiel != null,
      current: ecIst != null && ecZiel == null,
    },
    {
      title: 'DOSIEREN',
      value: hasCalculatedResult
        ? (result?.needsAddback ? `${formatNumber(result.litersToAdd, 2)} L` : 'nichts nötig')
        : '—',
      note: hasCalculatedResult
        ? (result?.needsAddback ? `Reservoir danach ${formatNumber(result.newReservoirVolume, 1)} L` : 'EC liegt im Ziel')
        : 'noch nicht berechnet',
      done: hasCalculatedResult,
      current: ecIst != null && ecZiel != null && !hasCalculatedResult,
    },
    {
      title: 'KONTROLLE',
      value: ecAfter != null ? `EC ${formatNumber(ecAfter, 2)}` : '—',
      note: ecAfter != null ? 'nachgemessen' : 'nach 15 min nachmessen',
      done: ecAfter != null,
      current: hasCalculatedResult && ecAfter == null,
    },
  ]

  return (
    <V1Page
      eyebrow="Reservoir"
      title="Addback"
      subtitle={grow ? `${grow.name}${grow.tentName ? ` · ${grow.tentName}` : ''}` : undefined}
      action={<V1LinkButton to="/addback" variant="ghost">Grow wechseln</V1LinkButton>}
      className="addback-assistant-page"
    >
      {error && <V1Alert title="Hinweis" message={error} tone="warn" />}
      {success && <V1Alert title="Gespeichert" message={success} tone="ok" />}

      <div data-audit="addback-flow">
        {loading ? (
          <V1Skeleton tiles={4} rows={3} label="Lade Addback" />
        ) : (
          <>
          <div className="addback-desktop-stepper" data-audit="addback-stepper">
            <AddbackFlow steps={flowSteps} />
          </div>

          <div className="addback-assistant-layout">
            <aside className="addback-context-rail">
              <V1Card>
                <span className="v1-card-kicker">System</span>
                <h2>{grow?.hydroStyle ?? 'Hydro'}</h2>
                <div className="addback-context-grid">
                  <ContextItem label="Reservoir" value={formatLiters(parseNullableNumber(form.reservoirLiters) ?? defaults?.suggestedReservoirLiters)} />
                  <ContextItem label="Programm" value={selectedProgram?.name ?? grow?.nutrients ?? 'Eigenes'} />
                  <ContextItem label="Phase" value={grow?.latestMeasurement?.stage ?? grow?.entryPoint ?? 'offen'} />
                  <ContextItem label="Letzter Log" value={formatShortDateTime(lastLog?.performedAtUtc)} />
                </div>
              </V1Card>

              <V1Card className="addback-mini-flow-card">
                <span className="v1-card-kicker">Ablauf</span>
                {/* Anzeige, keine Navigation: alle Abschnitte stehen ohnehin
                    untereinander auf derselben Seite. Ein Sprung nach vorn
                    hätte nur mit halben Daten gerechnet. */}
                {flowSteps.map((item, index) => (
                  <div
                    key={item.title}
                    className={classNames('addback-rail-step', item.current && 'active', item.done && 'done')}
                  >
                    <span>{index + 1}</span>
                    <strong>{item.title}</strong>
                    <em>{item.value}</em>
                  </div>
                ))}
              </V1Card>
            </aside>

            <section className="addback-step-panel">
              {mischplan && (mischplan.zeilen.length > 0 || mischplan.luecke) && (
                  <V1Section title={`Mischplan${mischplan.spalteLabel ? ` · ${mischplan.spalteLabel}` : ''}`}>
                    <V1Card>
                      {mischplan.luecke
                        ? <V1Alert tone="neutral" message={mischplan.luecke} />
                        : (
                          <>
                            <div className="ab-mischplan" data-audit="mixing-plan">
                              {mischplan.zeilen.map((zeile) => (
                                <div key={zeile.komponente} className="ab-mischplan-row">
                                  <span>{zeile.komponente}</span>
                                  <span className="ab-mischplan-dose">{mischplan.volumenLiter != null ? mlSpanne(zeile.mlGesamtMin, zeile.mlGesamtMax) : `${zeile.mlProLiter} ml/L`}</span>
                                  {mischplan.volumenLiter != null && <span className="ab-mischplan-per">{zeile.mlProLiter} ml/L</span>}
                                </div>
                              ))}
                              {(mischplan.ecZiel != null || mischplan.phMin != null) && (
                                <div className="ab-mischplan-row is-target">
                                  <span>Ziel</span>
                                  <span className="ab-mischplan-dose">
                                    {mischplan.ecZiel != null ? `EC ${mischplan.ecZiel.toLocaleString('de-DE')}` : ''}
                                    {mischplan.phMin != null ? ` · pH ${mischplan.phMin.toLocaleString('de-DE')}${mischplan.phMax != null && mischplan.phMax !== mischplan.phMin ? `–${mischplan.phMax.toLocaleString('de-DE')}` : ''}` : ''}
                                  </span>
                                </div>
                              )}
                            </div>
                            {(mischplan.ecZiel != null || mischplan.phMin != null) && (
                              <label className="ab-mischplan-adopt" data-audit="mixing-plan-adopt">
                                <input
                                  type="checkbox"
                                  checked={mischplan.zieleAktiv}
                                  onChange={(event) => void zieleUmschalten(event.target.checked)}
                                />
                                <span>
                                  Diese Wochen-Ziele auch auf dem Bildschirm verwenden
                                  <em>
                                    Sonst gilt weiter das Ziel deines Phasenprofils. Übernommen werden
                                    nur EC und pH — Temperatur, ORP und Licht kennt das Chart nicht.
                                  </em>
                                </span>
                              </label>
                            )}
                            {mischplan.herkunft && <p className="ab-mischplan-source">{mischplan.herkunft}</p>}
                          </>
                        )}
                    </V1Card>
                  </V1Section>
              )}

              {(
                <V1Section title="Grow & Reservoir">
                  <div className="addback-summary-grid">
                    <V1Stat label="Grow" value={grow?.name ?? defaults?.growName ?? '–'} hint={(grow ? sortenText(grow) : null) ?? 'Sorte offen'} />
                    <V1Stat label="Zelt" value={grow?.tentName ?? '–'} hint={grow?.hydroStyle ?? null} />
                    <V1Stat label="Volumen" value={formatNumber(parseNullableNumber(form.reservoirLiters) ?? defaults?.suggestedReservoirLiters, 1)} unit="L" hint="aus Hydro-Setup oder manuell" />
                    <V1Stat label="EC aktuell" value={formatNumber(parseNullableNumber(form.ecIst), 2)} unit="mS/cm" hint="letzte Messung / manuell" />
                  </div>
                  <div className="addback-action-row">
                    <V1Button variant="primary" onClick={() => void checkAndCalculate()} disabled={calculating}>{calculating ? 'Rechnet...' : 'Prüfen & Dosierung berechnen'}</V1Button>
                  </div>
                </V1Section>
              )}

              {(
                <V1Section title="Istwerte">
                  <div className="addback-form-grid">
                    <NumberField label="Aktuelles Volumen" unit="L" value={form.reservoirLiters} onChange={(value) => updateForm('reservoirLiters', value)} hint={defaults?.suggestedReservoirLiters == null ? 'Manuell eintragen' : `Hydro-Vorschlag: ${formatNumber(defaults.suggestedReservoirLiters, 1)} L`} />
                    <NumberField label="EC aktuell" unit="mS/cm" value={form.ecIst} onChange={(value) => updateForm('ecIst', value)} hint={defaults?.suggestedEcIst == null ? 'Nachmessen' : `Letzte Messung: ${formatNumber(defaults.suggestedEcIst, 2)}`} />
                    <NumberField label="pH aktuell" unit="pH" value={form.phBefore} onChange={(value) => updateForm('phBefore', value)} hint="optional, aber empfohlen" />
                    <ReadOnlyMetric label="Wasser" value={grow?.latestMeasurement?.reservoirWaterTempC} unit="°C" />
                    <ReadOnlyMetric label="ORP" value={grow?.latestMeasurement?.orpMv} unit="mV" />
                    <ReadOnlyMetric label="DO" value={grow?.latestMeasurement?.dissolvedOxygenMgL} unit="mg/L" />
                  </div>
                </V1Section>
              )}

              {(
                <V1Section title="Zielwerte">
                  <div className="addback-program-box">
                    <V1Field label="Nährstoffprogramm">
                      <select value={programKey} onChange={(event) => handleProgramChange(event.target.value)}>
                        <option value="custom">Eigenes / unbekannt</option>
                        {programs.map((program) => <option key={program.key} value={program.key}>{program.name}</option>)}
                      </select>
                    </V1Field>
                    {selectedProgram && (
                      <div className="addback-program-summary">
                        <strong>{selectedProgram.manufacturer}</strong>
                        <span>{selectedStage?.target ?? selectedProgram.ecGuidance}</span>
                      </div>
                    )}
                  </div>
                  <div className="addback-form-grid">
                    <NumberField label="Ziel-EC" unit="mS/cm" value={form.ecZiel} onChange={(value) => updateForm('ecZiel', value)} hint={defaults?.suggestedEcZiel == null ? 'Manuell festlegen' : `Sollwert: ${formatNumber(defaults.suggestedEcZiel, 2)}`} />
                    <NumberField label="Ziel-pH" unit="pH" value={form.phTarget} onChange={(value) => updateForm('phTarget', value)} hint={selectedProgram?.phGuidance ?? 'typisch 5,7–6,0'} />
                    <NumberField label="Addback-EC" unit="mS/cm" value={form.ecStock} onChange={(value) => updateForm('ecStock', value)} hint="EC der vorgemischten Addback-Lösung" />
                    {/* Der Grow traegt eine Wasserquelle fuer den ganzen Lauf.
                        Hier zaehlt der einzelne Vorgang: wer einmal mit
                        Leitungswasser auffuellt, weil der Osmose-Tank leer
                        war, erklaert damit spaeter seinen EC-Sprung. */}
                    <V1Field label="Wasser" hint={wasserHinweis}>
                      <select value={form.waterUsed} onChange={(event) => updateForm('waterUsed', event.target.value)}>
                        <option value="">wie am Grow ({wasserAmGrow})</option>
                        <option value="RO">Osmose / VE-Wasser</option>
                        <option value="Tap">Leitungswasser</option>
                        <option value="Mixed">Mischung</option>
                      </select>
                    </V1Field>
                  </div>
                </V1Section>
              )}

              {(
                <V1Section title="Dosierung">
                  <div className="addback-result-card">
                    {result?.errorMessage ? (
                      <V1Alert tone="warn" message={result.errorMessage} />
                    ) : !hasCalculatedResult ? (
                      <V1Empty title="Noch keine Berechnung" action={<V1Button variant="primary" onClick={() => void calculateAddback()} disabled={calculating}>{calculating ? 'Berechnet...' : 'Berechnen'}</V1Button>} />
                    ) : result?.needsAddback ? (
                      <>
                        <span className="v1-card-kicker">Addback-Menge</span>
                        <strong>{formatNumber(result.litersToAdd, 2)} <em>L</em></strong>
                        <p>Reservoir danach: {formatNumber(result.newReservoirVolume, 1)} L</p>
                      </>
                    ) : (
                      <>
                        <span className="v1-card-kicker">Status</span>
                        <strong>Kein Addback nötig</strong>
                        <p>EC liegt bereits im Zielbereich oder darüber.</p>
                      </>
                    )}
                  </div>

                  <div className="addback-components">
                    <header>
                      <h3>Komponenten</h3>
                      <span>Reihenfolge abarbeiten und optional Mengen eintragen.</span>
                    </header>
                    {components.map((component, index) => (
                      <div key={component.id} className={classNames('addback-component-row', component.done && 'done')}>
                        <button type="button" onClick={() => updateComponent(component.id, { done: !component.done })}>{component.done ? '✓' : index + 1}</button>
                        <input value={component.name} onChange={(event) => updateComponent(component.id, { name: event.target.value })} />
                        <div className="addback-amount-input">
                          <input inputMode="decimal" value={component.amountMl} onChange={(event) => updateComponent(component.id, { amountMl: event.target.value })} placeholder="ml" />
                          <span>ml</span>
                        </div>
                      </div>
                    ))}
                  </div>
                </V1Section>
              )}

              {(
                <V1Section title="Nachmessung">
                  <div className="addback-form-grid">
                    <NumberField label="EC nach Addback" unit="mS/cm" value={form.ecAfter} onChange={(value) => updateForm('ecAfter', value)} hint="nach Durchmischung messen" />
                    <NumberField label="pH nach Addback" unit="pH" value={form.phAfter} onChange={(value) => updateForm('phAfter', value)} hint="erst nach EC stabilisieren" />
                    <V1Field label="Notizen" wide>
                      <textarea rows={5} value={form.notes} onChange={(event) => updateForm('notes', event.target.value)} placeholder="Beobachtung, Abweichung, Reihenfolge, Reaktion der Pflanzen..." />
                    </V1Field>
                  </div>
                </V1Section>
              )}

              {(
                <V1Section title="Prüfen & Speichern">
                  <div className="addback-review-grid">
                    <Review label="Grow" value={grow?.name ?? defaults?.growName ?? '–'} />
                    <Review label="Programm" value={selectedProgram?.name ?? grow?.nutrients ?? 'Eigenes'} />
                    <Review label="Reservoir" value={`${formatNumber(parseNullableNumber(form.reservoirLiters), 1)} L`} />
                    <Review label="EC" value={`${formatNumber(parseNullableNumber(form.ecIst), 2)} → ${formatNumber(parseNullableNumber(form.ecAfter), 2)} mS/cm`} />
                    <Review label="pH" value={`${formatNumber(parseNullableNumber(form.phBefore), 2)} → ${formatNumber(parseNullableNumber(form.phAfter), 2)}`} />
                    <Review label="Addback" value={result?.needsAddback ? `${formatNumber(result.litersToAdd, 2)} L` : '0 L'} />
                  </div>
                  <div className="addback-action-row">
                    <V1Button variant="primary" onClick={handleSave} disabled={saving || calculating}>{saving ? 'Speichert...' : 'Addback speichern'}</V1Button>
                  </div>
                  {logs.length > 0 && (
                    <div className="addback-log-list" data-audit="addback-log-list">
                      <h3>Letzte Addbacks</h3>
                      {logs.slice(0, 4).map((log) => (
                        <div key={log.id}>
                          <strong>{formatShortDateTime(log.performedAtUtc)}</strong>
                          <span>{formatNumber(log.litersAdded, 2)} L · EC {formatNumber(log.ecBefore, 2)} → {formatNumber(log.ecAfter, 2)}</span>
                        </div>
                      ))}
                    </div>
                  )}
                </V1Section>
              )}
            </section>
          </div>
          </>
        )}
      </div>
    </V1Page>
  )
}

function NumberField({ label, unit, value, onChange, hint }: { label: string; unit: string; value: string; onChange: (value: string) => void; hint?: string | null }) {
  return (
    <V1Field label={label} hint={hint}>
      <div className="addback-number-field">
        <input inputMode="decimal" value={value} onChange={(event) => onChange(event.target.value)} />
        <span>{unit}</span>
      </div>
    </V1Field>
  )
}

function ReadOnlyMetric({ label, value, unit }: { label: string; value: number | null | undefined; unit: string }) {
  return <V1Stat label={label} value={formatNumber(value, unit === '°C' ? 1 : 2)} unit={unit} hint="letzte Messung" />
}

function ContextItem({ label, value }: { label: string; value: string }) {
  return <div><span>{label}</span><strong>{value}</strong></div>
}

function Review({ label, value }: { label: string; value: string }) {
  return <div className="addback-review-item"><span>{label}</span><strong>{value}</strong></div>
}

function createComponents(names: string[]): ComponentDraft[] {
  return names.map((name, index) => ({ id: `${index}-${name}`, name, amountMl: '', done: false }))
}

function componentNamesForProgram(program: NutrientProgramDto | null): string[] {
  const text = `${program?.name ?? ''} ${program?.manufacturer ?? ''}`.toLowerCase()
  if (text.includes('canna')) {
    return ['Silikat / optional', 'CANNA Aqua A', 'CANNA Aqua B', 'CalMag / Bedarf', 'Cannazym / Additiv', 'pH-Korrektur']
  }
  if (text.includes('athena')) {
    return ['Balance / pH Basis', 'Cleanse / System', 'CaMg', 'Grow/Bloom A', 'Grow/Bloom B', 'pH-Korrektur']
  }
  if (text.includes('vbx') || text.includes('hydroponic')) {
    return ['VBX', 'Shine', 'Life', 'CalMag / Bedarf', 'pH-Korrektur']
  }
  return genericComponents
}

function matchProgram(programs: NutrientProgramDto[], nutrients: string | null | undefined): NutrientProgramDto | null {
  const text = (nutrients ?? '').toLowerCase()
  if (!text) return null
  // Fork AI: exakter Namens-/Key-Treffer zuerst, damit ein laengerer Name
  // („SKX Canna Aqua …") nicht vom kuerzeren Teilstring („Canna Aqua") geschlagen wird.
  const exact = programs.find((program) => program.name.toLowerCase() === text || program.key.toLowerCase() === text)
  if (exact) return exact
  return programs.find((program) => {
    const haystack = `${program.key} ${program.name} ${program.manufacturer}`.toLowerCase()
    return haystack.includes(text) || text.includes(program.key.toLowerCase()) || text.includes(program.name.toLowerCase()) || text.includes(program.manufacturer.toLowerCase())
  }) ?? null
}

function findProgramStage(program: NutrientProgramDto | null, stage: string | null): NutrientProgramStageDto | null {
  if (!program || !stage) return null
  const normalized = stage.toLowerCase()
  return program.stages.find((item) => item.stage.toLowerCase().includes(normalized) || normalized.includes(item.stage.toLowerCase())) ?? program.stages[0] ?? null
}

function derivePhTarget(program: NutrientProgramDto | null): string | null {
  const text = program?.phGuidance ?? ''
  const matches = Array.from(text.matchAll(/\d+(?:[.,]\d+)?/g)).map((match) => Number(match[0].replace(',', '.'))).filter(Number.isFinite)
  if (matches.length === 0) return null
  const relevant = matches.filter((value) => value >= 4 && value <= 8)
  if (relevant.length === 0) return null
  const average = relevant.reduce((sum, value) => sum + value, 0) / relevant.length
  // Mit Komma: der Wert geht in ein Eingabefeld, nicht in eine Rechnung.
  return average.toFixed(1).replace('.', ',')
}

function validateActuals(form: AddbackFormState): string | null {
  const reservoir = parseNullableNumber(form.reservoirLiters)
  const ec = parseNullableNumber(form.ecIst)
  const ph = parseNullableNumber(form.phBefore)
  if (reservoir == null || reservoir <= 0) return 'Reservoir-Volumen ist erforderlich.'
  if (ec == null || ec < 0) return 'Aktueller EC ist erforderlich.'
  if (ph != null && (ph < 0 || ph > 14)) return 'pH muss zwischen 0 und 14 liegen.'
  return null
}

function validateCalculation(form: AddbackFormState): string | null {
  const actualValidation = validateActuals(form)
  if (actualValidation) return actualValidation
  const ecIst = parseNullableNumber(form.ecIst)
  const ecZiel = parseNullableNumber(form.ecZiel)
  const ecStock = parseNullableNumber(form.ecStock)
  if (ecZiel == null || ecZiel < 0) return 'Ziel-EC ist erforderlich.'
  if (ecStock == null || ecStock <= 0) return 'Addback-EC ist erforderlich.'
  if (ecIst != null && ecStock <= ecZiel) return 'Addback-EC muss höher sein als Ziel-EC.'
  return null
}

function buildNotes(program: NutrientProgramDto | null, stage: NutrientProgramStageDto | null, components: ComponentDraft[], notes: string): string {
  const componentLines = components
    .filter((component) => component.name.trim().length > 0)
    .map((component, index) => `${index + 1}. ${component.name.trim()}${component.amountMl.trim() ? ` — ${component.amountMl.trim()} ml` : ''}${component.done ? ' — erledigt' : ''}`)

  return [
    'Addback-Assistent',
    `Programm: ${program?.name ?? 'Eigenes / unbekannt'}`,
    stage ? `Stage: ${stage.stage} — ${stage.target}` : null,
    componentLines.length > 0 ? `Komponenten:\n${componentLines.join('\n')}` : null,
    notes.trim() ? `Notizen:\n${notes.trim()}` : null,
  ].filter(Boolean).join('\n\n')
}

function formatLiters(value: number | null | undefined): string {
  return value == null ? '–' : `${formatNumber(value, 1)} L`
}

function formatShortDateTime(value: string | null | undefined) {
  if (!value) return '–'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '–'
  return new Intl.DateTimeFormat('de-DE', {
    day: '2-digit',
    month: '2-digit',
    year: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  }).format(date)
}

/**
 * Eine Zahl für ein Eingabefeld.
 *
 * Die Umwandlung steht in <code>zahlenfeld.ts</code> und nur dort — sie stand
 * am 01.09.2026 fünfmal in der Oberfläche, jedes Mal mit
 * <code>String(value)</code> und damit mit englischem Punkt.
 */
function draftNumber(value: number | null | undefined): string {
  return feldText(value)
}

function parseNullableNumber(value: string): number | null {
  const trimmed = value.trim().replace(',', '.')
  if (!trimmed) return null
  const parsed = Number.parseFloat(trimmed)
  return Number.isFinite(parsed) ? parsed : null
}

export default AddbackPage
