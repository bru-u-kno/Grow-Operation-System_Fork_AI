import { useEffect, useMemo, useState } from 'react'
import { sortenAufzaehlung, sortenText, zuechterPasst } from '../features/grows/sorten-text'
import '../features/grow-detail/growdetail-instrument.css'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { formatNumber } from '../utils'
import { useGrowDetailBundle } from '../features/grow-detail/useGrowDetailBundle'
import { useGrowDetailMutations } from '../features/grow-detail/useGrowDetailMutations'
import { formatGrowStatus } from '../features/grow-detail/grow-detail-model'
import { V1Alert, V1Badge, V1Button, V1Empty, V1LinkButton, V1Page, V1Section, V1Stat } from '../components/v1'
import { balkenText, buildPhaseTimeline, flipLabel } from '../features/grows/phase-timeline'
import { CuringSection } from '../features/curing/CuringSection'
import { PlanAuswertung } from '../features/zielwerte/PlanAuswertung'
import { GrowPlantsCard } from '../features/grow-detail/GrowPlantsCard'
import { samenName } from '../deutsche-woerter'
import type { GrowDeviationDto } from '../types'
import { resolveUrl } from '../base'
import { apiFetch } from '../api'

const noop = async () => {}

// The grow's own page does exactly one thing: show this grow's overview. The former
// tabs (measurements, diagnosis, journal, SOPs, automation) are now their own
// top-level pages with a grow switcher — reached from the nav or the quick links
// below, pre-selected to this grow. No drilling into a grow to find features.
function GrowDetailPage() {
  const { growId } = useParams()
  const navigate = useNavigate()
  const [pflanzenSorten, setPflanzenSorten] = useState<string[]>([])
  // Wie viele Pflanzen wirklich erfasst sind. Die Kachel zeigte bisher die
  // Zahl aus dem Grow-Formular; gemeldet wurde 6 bei acht erfassten Pflanzen.
  const [pflanzenAnzahl, setPflanzenAnzahl] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [saving, setSaving] = useState<string | null>(null)
  const { bundle, loading, loadBundle } = useGrowDetailBundle({ growId, setError })
  // Fuer die Diagnose-Kurzliste des Entwurfs: die zwei wichtigsten Abweichungen
  // direkt auf dem Ueberblick, der Rest hinter dem Link.
  const [deviations, setDeviations] = useState<GrowDeviationDto[]>([])
  useEffect(() => {
    if (!growId) return
    const controller = new AbortController()
    apiFetch<GrowDeviationDto[]>(`/api/grows/${growId}/deviations`, { signal: controller.signal })
      .then(setDeviations)
      .catch(() => { /* Kurzliste ist Beigabe — der Ueberblick steht auch ohne sie. */ })
    return () => controller.abort()
  }, [growId])
  const openTasks = useMemo(() => bundle.tasks.filter((task) => task.status === 'Open'), [bundle.tasks])
  const {
    archiveGrow,
    deleteGrow,
    handleGrowAction,
  } = useGrowDetailMutations({
    growId,
    grow: bundle.grow,
    saving,
    selectedMeasurement: null,
    sopStepNotesById: {},
    navigate,
    loadBundle,
    loadDeviations: noop,
    loadPhotos: noop,
    loadSopInstances: noop,
    loadTreatmentRecommendations: noop,
    setError,
    setNotice,
    setSaving,
  })

  useEffect(() => {
    const controller = new AbortController()
    const handle = window.setTimeout(() => {
      void loadBundle(controller.signal)
    }, 0)
    return () => {
      window.clearTimeout(handle)
      controller.abort()
    }
  }, [loadBundle])

  if (loading) {
    return (
      <V1Page eyebrow="Pflanzen" title="Lade Daten...">
        <V1Empty title="Einen Moment" />
      </V1Page>
    )
  }

  if (!bundle.grow) {
    return (
      <V1Page eyebrow="Pflanzen" title="Nicht gefunden" action={<V1LinkButton to="/grows">Zu den Grows</V1LinkButton>}>
        <V1Alert title="Fehler" message={error ?? 'Diesen Grow gibt es nicht (mehr).'} tone="warn" />
      </V1Page>
    )
  }

  const grow = bundle.grow
  const latest = grow.latestMeasurement
  const scope = `?growId=${grow.id}`
  const canArchiveGrow = grow.status === 'Planning' || grow.status === 'Running'
  const statusTone = grow.status === 'Running' ? 'ok' : grow.status === 'Planning' ? 'warn' : 'neutral'
  // Autoflower kennen keinen Flip — sie gehen von selbst in die Bluete. Der
  // Server lehnt den Aufruf korrekt mit 400 ab; angeboten wurde er trotzdem,
  // und ein Knopf, der immer scheitert, ist schlimmer als keiner. Der
  // Kommentar unten wusste es schon („auch bei Autoflowern, die keinen Flip
  // kennen"), die Bedingung nicht.
  const canFlip = grow.status === 'Running' && !grow.flipDate && grow.seedType !== 'Autoflower'
  // Der Übergang zur Veg hängt am Aussehen, nicht am Kalender — echte gezackte
  // Blätter statt der zwei runden Keimblätter. Also ein Knopf, solange noch
  // nichts eingetragen ist und noch nicht geflippt wurde.
  // Beide Knöpfe hängen an der Phase, die der Server ausrechnet — dieselbe
  // Quelle wie die Zielwerte. „Sämling ist durch" gibt es nur im Sämling
  // (Klone haben nie einen), „Finish beginnt" nur in der Blüte — auch bei
  // Autoflowern, die keinen Flip kennen.
  const canConfirmVeg = grow.currentStage === 'Seedling' && !grow.vegStartedAt && grow.status === 'Running'
  const canConfirmFinish = ['Transition', 'Flower'].includes(grow.currentStage)
    && !grow.finishStartedAt && grow.status === 'Running'
  // An der gerechneten Phase, wie die drei Knoepfe darueber — nicht an der
  // letzten Handmessung. Vorher verschwand der Ernte-Knopf vollstaendig,
  // sobald niemand von Hand gemessen hatte: die Seite /grows/:id/harvest gab
  // es, aber keinen Weg dorthin.
  const canHarvest = ['Flower', 'Finish', 'Dry'].includes(grow.currentStage ?? latest?.stage ?? grow.entryPoint ?? '')
  const timeline = buildPhaseTimeline(grow)
  const lastMeasurements = [...bundle.measurements]
    .sort((a, b) => b.takenAt.localeCompare(a.takenAt))
    .slice(0, 4)

  return (
    <div className="ix-growdetail">
      <V1Page
        eyebrow={`Pflanzen / ${grow.name}`}
        title={grow.name}
        action={(
          <div className="v1-action-row" data-audit="grow-management-actions">
            <V1Badge tone={statusTone}>{formatGrowStatus(grow.status)}</V1Badge>
            <V1LinkButton to={`/grows/${grow.id}/addback`}>Addback</V1LinkButton>
            {canConfirmVeg && (
              <V1Button disabled={Boolean(saving)} onClick={() => void handleGrowAction('veg')}>
                {saving === 'action-veg' ? 'Trägt ein…' : 'Sämling ist durch'}
              </V1Button>
            )}
            {canFlip && (
              <V1Button disabled={Boolean(saving)} onClick={() => void handleGrowAction('flip')}>
                {saving === 'flip' ? 'Trägt ein…' : 'Flip 12/12'}
              </V1Button>
            )}
            {canConfirmFinish && (
              <V1Button disabled={Boolean(saving)} onClick={() => void handleGrowAction('finish')}>
                {saving === 'action-finish' ? 'Trägt ein…' : 'Finish beginnt'}
              </V1Button>
            )}
            {canHarvest && <V1LinkButton to={`/grows/${grow.id}/harvest`} variant="primary">Ernte</V1LinkButton>}
          </div>
        )}
        className="grow-detail-page"
      >
        {error && <V1Alert title="Fehler" message={error} tone="warn" />}
        {notice && <V1Alert message={notice} tone="ok" />}

        {/* Die Tabs des Entwurfs führen zu den Top-Seiten — der Grow ist kein
            Behälter mehr, aber der Weg von hier zu seinen Daten bleibt einer. */}
        <nav className="gd-tabs" aria-label="Bereiche dieses Grows">
          <span className="gd-tab is-active">Überblick</span>
          <Link className="gd-tab" to={`/diagnose${scope}`}>Diagnose{deviations.length > 0 ? ` · ${deviations.length}` : ''}</Link>
          <Link className="gd-tab" to={`/messungen${scope}`}>Messungen · {bundle.measurements.length}</Link>
          <Link className="gd-tab" to={`/sops${scope}`}>SOPs</Link>
          <Link className="gd-tab" to={`/journal${scope}`}>Journal & Fotos</Link>
          <Link className="gd-tab" to={`/regeln${scope}&tab=automatik`}>Automatik</Link>
          {/* Nicht „KI-Berater": in Grow OS steckt keine KI. Der Reiter fuehrt
              zur Mappe, die man einem EIGENEN Agenten vorlegt — der Name muss
              das sagen, sonst sucht man eine Funktion, die es nicht gibt. */}
          <Link className="gd-tab" to={`/berater${scope}`}>Mappe für eigene KI</Link>
        </nav>

        {/* Phasen-Timeline — dieselbe Rechnung wie auf der Live-Seite. */}
        <section className="ls-panel" data-audit="grow-detail-timeline">
          <div className="ls-panel-body">
            {/* Wischbar auf dem Telefon — siehe live-screen.css. */}
            <div className="ls-timeline-wrap">
            <div className="ls-timeline">
              {timeline.phases.map((phase) => (
                <div key={phase.label} className={`ls-phase is-${phase.state}${phase.days === 0 ? ' is-unknown' : ''}`} style={phase.days === 0 ? undefined : { flexGrow: phase.days }} title={phase.label}>
                  {phase.progress != null && (
                    <i className="ls-phase-fill" style={{ width: `${Math.round(phase.progress * 100)}%` }} aria-hidden="true" />
                  )}
                  <span>{balkenText(phase.short, phase.days)}</span>
                </div>
              ))}
              {timeline.phases.length === 0 && <div className="ls-phase is-planned"><span>Kein Startdatum</span></div>}
            </div>
            <div className="ls-timeline-dates">
              <span>Start {timeline.dates.start}</span>
              <span className={timeline.daysToFlip != null && timeline.daysToFlip < 0 ? 'is-due' : undefined}>
                {flipLabel(timeline.flipIsPlanned, timeline.daysToFlip, timeline.dates.flip)}
              </span>
              <span>{timeline.dates.harvest === '—' ? 'Ernte offen' : `Ernte ~${timeline.dates.harvest}`}</span>
              {/* Die Frage, die der Strahl bisher offen liess: wann ist es fertig?
                  Zwischen Ernte und rauchbar liegen Wochen. */}
              {timeline.dates.ready !== '—' && <span title={timeline.readyNote}>Fertig ~{timeline.dates.ready}</span>}
            </div>
            </div>
            {timeline.dates.ready !== '—' && <p className="gd-ready-note">{timeline.readyNote}</p>}
            {/* Ohne Plan bleibt der Strahl offen — dann steht hier, wo man ihn
                setzt, statt dass drei Striche ohne Erklaerung dastehen. */}
            {timeline.dates.flip === '—' && (
              <div className="gd-plan-hint">
                <p className="gc-facts">Keine Veg-Dauer geplant — ohne sie kann der Strahl keinen Flip- und Erntetermin zeigen.</p>
                <Link className="ls-btn is-small" to={`/grows/${grow.id}/setup`}>Veg-Dauer eintragen</Link>
              </div>
            )}
          </div>
        </section>

        {/* Fakten-Leiste wie im Entwurf: die sechs Zahlen, nach denen man sucht. */}
        <section className="v1-kpi-grid" data-audit="grow-detail-summary">
          {/* Bei einem Mehrsorten-Grow ist EIN Sortenname eine Falschaussage.
              Die Pflanzen-Karte meldet ihre Sorten herauf; ab zwei steht hier
              „gemischt" mit der Liste — die Hauptsorte bleibt fuer Listen und
              Zeitstrahl, behauptet aber nicht mehr, allein im Zelt zu sein. */}
          {/* Die Regel steht in sortenText(), nicht hier.
              Diese Stelle hatte sie nachgebaut — und dabei einen Fall anders
              entschieden: bei GENAU EINER Pflanzensorte fiel sie auf
              `grow.strain` zurueck. Setzt man alle vier Toepfe auf Gorilla
              Glue, sagte die Kachel oben „White Widow", waehrend dieselbe Seite
              weiter unten „4x Gorilla Glue" schrieb. Gefunden vom Pruefer. */}
          <V1Stat
            label="Sorte"
            wortwert
            value={sortenText({ strain: grow.strain, pflanzenSorten }) ?? '—'}
            hint={sortenAufzaehlung({ pflanzenSorten })
              ?? ([zuechterPasst({ strain: grow.strain, pflanzenSorten, nurHauptsorte: grow.nurHauptsorte }) ? grow.breeder : null,
                   samenName(grow.seedType)].filter(Boolean).join(' · ') || undefined)} />
          <V1Stat
            label="Pflanzen"
            value={pflanzenAnzahl && pflanzenAnzahl > 0 ? pflanzenAnzahl : grow.plantCount ?? '—'}
            hint={pflanzenAnzahl && pflanzenAnzahl > 0 && grow.plantCount != null && grow.plantCount !== pflanzenAnzahl
              ? `einzeln erfasst · im Formular stehen ${grow.plantCount}`
              : undefined} />
          <V1Stat label="pH / EC" value={`${formatNumber(latest?.reservoirPh, 2)} · ${formatNumber(latest?.reservoirEc, 2)}`} />
          <V1Stat label="Klima" value={latest ? `${formatNumber(latest.airTemperatureC, 1)}° · ${formatNumber(latest.humidityPercent, 0)}%` : '—'} />
          <V1Stat label="Messungen" value={bundle.measurements.length} />
          <V1Stat label="Offene Tasks" value={openTasks.length} tone={openTasks.length > 0 ? 'warn' : 'neutral'} />
        </section>

        <div className="gd-lower">
          <section className="ls-panel gd-diagnose" data-audit="grow-detail-diagnose">
            <div className="ls-panel-head">
              <span className="ls-label">Diagnose</span>
              <span className="ls-panel-meta">Abweichung → Symptom → Behandlung</span>
              {deviations.length > 2 && <Link className="ls-btn is-small" to={`/diagnose${scope}`}>Alle {deviations.length}</Link>}
            </div>
            {deviations.length === 0 ? (
              <div className="ls-panel-body"><p>Keine offenen Abweichungen — alle Werte im Rahmen.</p></div>
            ) : (
              <div className="gd-devs">
                {deviations.slice(0, 2).map((deviation) => (
                  <article key={deviation.stableKey} className={`gd-dev is-${deviation.severity.toLowerCase()}`}>
                    <strong>{deviation.message}</strong>
                    {/* Die Empfehlung nur, wenn sie etwas hinzufuegt — bei
                        manchen Abweichungen ist sie woertlich die Meldung. */}
                    {deviation.recommendation && deviation.recommendation !== deviation.message && <p>{deviation.recommendation}</p>}
                    <div className="ls-panel-actions">
                      <Link className="ls-btn is-small" to={`/diagnose${scope}`}>Verlauf</Link>
                    </div>
                  </article>
                ))}
              </div>
            )}
          </section>

          <section className="ls-panel gd-meas" data-audit="grow-detail-measurements">
            <div className="ls-panel-head">
              <span className="ls-label">Letzte Messungen</span>
              <Link className="ls-btn is-small" to="/messung">Neue Messung</Link>
            </div>
            {lastMeasurements.length === 0 ? (
              <div className="ls-panel-body"><p>Noch keine Messung — die erste dauert zwei Minuten.</p></div>
            ) : (
              <div className="gd-meas-wrap">
                <table className="gd-meas-table">
                  <thead>
                    <tr><th scope="col">Zeit</th><th scope="col">pH</th><th scope="col">EC</th><th scope="col">DO</th><th scope="col">Temp</th></tr>
                  </thead>
                  <tbody>
                    {lastMeasurements.map((measurement) => (
                      <tr key={measurement.id}>
                        <td>{formatShortTime(measurement.takenAt)}</td>
                        <td>{formatNumber(measurement.reservoirPh, 2)}</td>
                        <td>{formatNumber(measurement.reservoirEc, 2)}</td>
                        <td>{formatNumber(measurement.dissolvedOxygenMgL, 1)}</td>
                        <td>{formatNumber(measurement.reservoirWaterTempC, 1)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>
        </div>

        {/* Pflanzen einzeln, jede mit ihrer Sorte — der Mischgrow aus dem
            Tester-Feedback. Vor der Nachtabsenkung: erst wer drin steht,
            dann wie gefahren wird. */}
        <GrowPlantsCard growId={grow.id} growPlantCount={grow.plantCount} systemId={grow.systemId ?? null} onSorten={setPflanzenSorten} onAnzahl={setPflanzenAnzahl} />

        {/* Fork AI (forkai.136): Die Karte „Nachtabsenkung" (Crop Steering)
            ist entfallen — die Wassertemperatur führt der Grow-Plan, geregelt
            wird unter Steuerung → Chiller. */}

        {/* Das Aushaerten steht VOR der Verwaltung: es ist der letzte Schritt
            am Lauf, nicht dessen Abwicklung. Der Abschnitt bleibt weg, solange
            nichts geerntet ist — ein Einglas-Formular an einem bluehenden Grow
            waere nur Rauschen. */}
        {/* Fork AI (Grow-Plan): Plan, Stand und Gemessenes je Woche. */}
        <PlanAuswertung growId={grow.id} />

        <CuringSection growId={grow.id} harvested={grow.status === 'Completed' || Boolean(grow.endDate)} />

        {/* Verwaltung unten — Beenden und Löschen gehören nicht neben die
            täglichen Handlungen in der Kopfzeile. Der Export ebenso wenig: er
            stand dort und kostete auf dem Telefon eine Knopfbreite in der
            Reihe, die man jeden Tag ansieht. Ein Dateidownload ist nichts,
            was man taeglich tut. */}
        <V1Section title="Verwaltung">
          <div className="v1-action-row">
            <V1LinkButton to={`/grows/${grow.id}/setup`}>Bearbeiten</V1LinkButton>
            {/* download + resolveUrl: ohne download navigierte der Knopf die
                App weg und zeigte rohes JSON — man musste zurueckgehen. Und
                ohne resolveUrl bricht der Pfad hinter dem Home-Assistant-
                Ingress, wo die App unter einem Unterpfad laeuft. */}
            <a
              className="v1-button"
              href={resolveUrl(`api/exports/grows/${grow.id}`)}
              download={`grow-${grow.id}-export.json`}
            >
              Export
            </a>
            <V1Button disabled={Boolean(saving) || !canArchiveGrow} onClick={() => void archiveGrow()}>
              {saving === 'grow-archive' ? 'Beendet...' : canArchiveGrow ? 'Beenden' : 'Beendet'}
            </V1Button>
            <V1Button variant="danger" disabled={Boolean(saving)} onClick={() => void deleteGrow()}>
              {saving === 'grow-delete' ? 'Löscht...' : 'Löschen'}
            </V1Button>
          </div>
        </V1Section>
      </V1Page>
    </div>
  )
}

/** „26.07. 09:30" — Datum und Uhrzeit, so kurz wie die Tabelle schmal ist. */
function formatShortTime(iso: string): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return '—'
  return new Intl.DateTimeFormat('de-DE', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }).format(date)
}

export default GrowDetailPage
