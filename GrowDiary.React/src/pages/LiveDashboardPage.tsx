import { useEffect, useMemo, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch, formatApiError } from '../api'
import { wegZurRoutine } from '../features/changeouts/routine-weg'
import type { GrowSummary, RiskEventDto, TentDto, TentLivePayload } from '../types'
import { LiveScreen, type DashboardPanel, type LiveTask } from '../features/live/LiveScreen'
import { useTentDashboard } from '../features/live/useTentDashboard'
import { useTentSparklines } from '../features/live/useTentSparklines'
import { layoutIsEmpty, seedLayout, type DashboardLayout } from '../features/live/dashboard-layout'
import { buildPhaseTimeline, currentPhaseLabel } from '../features/grows/phase-timeline'
import { decimalsForMetric, kachelUrteil } from '../features/live/metric-tile-model'
import '../features/live/live-screen.css'
import { V1Skeleton } from '../components/v1'
import {
  buildScore,
  chooseInitialTent,
  climateMetricKeys,
  findMetric,
  hydroMetricKeys,
  initialLiveState,
  mapMetrics,
  riskRank,
  type LiveState,
} from '../features/live/live-model'

function LiveDashboardPage() {
  const [state, setState] = useState<LiveState>(initialLiveState)
  const [selectedTentId, setSelectedTentId] = useState<number | null>(null)
  const [loading, setLoading] = useState(true)
  const [refresh, setRefresh] = useState(0)
  // Der Watchdog meldet, wenn die Überwachung SELBST schweigt — das gehört auf
  // Live, weil frische Zahlen eines Zelts verdecken, dass ein anderes dunkel
  // ist. Nur Probleme erscheinen; „alles wach" wäre Dauerrauschen.
  const [systemWarning, setSystemWarning] = useState<{ headline: string; detail: string } | null>(null)
  // Anpassen-Modus: der Entwurf lebt hier, gespeichert wird erst auf Knopfdruck.
  // Entwurf und Modus tragen ihr Zelt bei sich, statt beim Zeltwechsel per
  // Effekt geleert zu werden — abgeleitet statt nachgezogen, das kann nicht
  // aus dem Tritt geraten.
  const [draft, setDraft] = useState<{ tentId: number; layout: DashboardLayout } | null>(null)
  const [editingTentId, setEditingTentId] = useState<number | null>(null)
  const [savingLayout, setSavingLayout] = useState(false)
  const [layoutWarning, setLayoutWarning] = useState<string | null>(null)

  // Mirror the latest committed state so a background refresh can fall back to the
  // last good data instead of blanking out when a request fails transiently.
  const stateRef = useRef(state)
  useEffect(() => { stateRef.current = state }, [state])

  useEffect(() => {
    const controller = new AbortController()
    async function load() {
      // Note: don't flip `loading` back on for background refreshes — the
      // initial useState(true) covers first paint, and keeping it false on the
      // 30s tick lets the dashboard update in place instead of blanking out.
      const previous = stateRef.current
      const issues: string[] = []
      // Report whether the call succeeded so a transient failure keeps the last
      // good value instead of overwriting it with an empty fallback (which made
      // sensor values vanish until the page was re-opened).
      const attempt = async <T,>(name: string, path: string): Promise<{ ok: boolean; value: T | null }> => {
        try { return { ok: true, value: await apiFetch<T>(path, { signal: controller.signal }) } }
        catch (caught) {
          if (!controller.signal.aborted) issues.push(`${name}: ${formatApiError(caught, 'nicht erreichbar')}`)
          return { ok: false, value: null }
        }
      }

      /**
       * Faellige Routinen aus den Ablaeufen.
       *
       * Der Block „Heute faellig" speiste sich nur aus Addback-Bedarf und
       * Risiken — die ueberfaelligen Routinen fehlten. Ein Klick auf „Alle"
       * fuehrte dann auf eine Seite, die drei weitere Punkte zeigte, die es
       * hier angeblich nicht gab. Zwei Zahlen zur selben Frage, die sich
       * widersprechen.
       */
      type FaelligeRoutine = { sopId: string; name: string; severity: string; meldung: string }

      const [tentsResult, growsResult, risksResult, watchdogResult] = await Promise.all([
        attempt<TentDto[]>('Zelte', '/api/settings/tents'),
        attempt<GrowSummary[]>('Grows', '/api/grows?archived=false'),
        attempt<RiskEventDto[]>('Risiken', '/api/risk-events?openOnly=true'),
        // Kein `attempt`: ein scheiternder Watchdog-Abruf ist kein Seitenfehler.
        apiFetch<{ headline: string; detail: string; isProblem: boolean }>('/api/notifications/watchdog', { signal: controller.signal }).catch(() => null),
      ])

      const sorted = tentsResult.ok
        ? [...(tentsResult.value ?? [])].sort((a, b) => a.displayOrder - b.displayOrder || a.name.localeCompare(b.name))
        : previous.tents
      const grows = growsResult.ok ? (growsResult.value ?? []) : previous.grows
      const risks = risksResult.ok ? (risksResult.value ?? []) : previous.risks

      // Je laufendem Grow. Scheitert einer, bleibt seine Liste leer — der
      // Rest der Seite haengt nicht daran.
      const routinenJeGrow = new Map<number, FaelligeRoutine[]>()
      await Promise.all(grows
        .filter((grow) => grow.status === 'Running')
        .map(async (grow) => {
          try {
            const liste = await apiFetch<FaelligeRoutine[]>(`/api/grows/${grow.id}/due-sops`, { signal: controller.signal })
            routinenJeGrow.set(grow.id, liste)
          } catch {
            /* Eine fehlende Routinenliste ist kein Seitenfehler. */
          }
        }))

      const livePairs = await Promise.all(sorted.map(async (tent) => {
        try { return [tent.id, await apiFetch<TentLivePayload>(`/api/live/tents/${tent.id}`, { signal: controller.signal })] as const }
        catch { return [tent.id, null] as const }
      }))

      if (controller.signal.aborted) return
      // Merge into the previous live map so a tent whose refresh failed keeps its
      // last good payload instead of dropping to empty. Only keep entries for
      // tents that still exist.
      const freshLive = Object.fromEntries(livePairs.filter((pair): pair is readonly [number, TentLivePayload] => pair[1] !== null))
      const liveByTentId: Record<number, TentLivePayload> = {}
      for (const tent of sorted) {
        const merged = freshLive[tent.id] ?? previous.liveByTentId[tent.id]
        if (merged) liveByTentId[tent.id] = merged
      }
      setState({ tents: sorted, grows, risks, liveByTentId, faelligeRoutinen: Object.fromEntries(routinenJeGrow), issues })
      setSystemWarning(watchdogResult?.isProblem ? { headline: watchdogResult.headline, detail: watchdogResult.detail } : null)
      setSelectedTentId((current) => current ?? chooseInitialTent(sorted, grows))
      setLoading(false)
    }
    void load()
    return () => controller.abort()
  }, [refresh])

  useEffect(() => {
    const id = window.setInterval(() => setRefresh((value) => value + 1), 30000)
    return () => window.clearInterval(id)
  }, [])

  const selectedTent = state.tents.find((tent) => tent.id === selectedTentId) ?? state.tents[0] ?? null
  const live = selectedTent ? state.liveByTentId[selectedTent.id] : undefined
  const activeGrows = state.grows.filter((grow) => grow.status === 'Running' || grow.status === 'Planning')
  const growsForTent = selectedTent ? activeGrows.filter((grow) => grow.tentId === selectedTent.id) : []
  const primaryGrow = growsForTent[0] ?? null
  const score = buildScore(live?.metrics ?? [], selectedTent)
  const climateMetrics = mapMetrics(live?.metrics ?? [], climateMetricKeys)
  // The cm water-level slot only renders when its sensor actually reports — most
  // setups measure either liters OR centimeters, so no permanent empty tile.
  // Fuellstand wird entweder in Litern ODER in Zentimetern gemessen — beide
  // Kacheln zu zeigen heisst, dass eine davon immer leer ist. Es gewinnt die,
  // die einen Wert hat; ohne beides bleibt die Liter-Kachel als Platzhalter.
  const hydroAlle = mapMetrics(live?.metrics ?? [], hydroMetricKeys)
  const hatWert = (key: string) => {
    const metric = hydroAlle.find((item) => item.key === key)
    return Boolean(metric && metric.value && metric.value !== '–')
  }
  const fuellstandKey = hatWert('reservoir-level') ? 'reservoir-level'
    : hatWert('reservoir-level-cm') ? 'reservoir-level-cm'
      : 'reservoir-level'
  const hydroMetrics = hydroAlle.filter((metric) =>
    !metric.key.startsWith('reservoir-level') || metric.key === fuellstandKey)
  const lightMetric = findMetric(live?.metrics ?? [], ['light-cycle', 'ppfd'])
  // Was im Klima-Band wirklich steht — Licht haengt hinten dran. Der
  // Anpassen-Modus saet daraus, damit beim Umschalten nichts erscheint oder
  // verschwindet, was vorher nicht da war.
  const climateForScreen = climateMetrics.concat(
    lightMetric ? [{ ...lightMetric, label: lightMetric.key === 'ppfd' ? 'PPFD' : 'Licht' }] : [])
  const hasHydroGrow = primaryGrow ? primaryGrow.hydroStyle === 'DWC' || primaryGrow.hydroStyle === 'RDWC' : false
  const risksForContext = state.risks
    .filter((risk) => risk.status === 'Open' || risk.status === 'Acknowledged')
    .filter((risk) => (primaryGrow ? risk.growId === primaryGrow.id : false) || (selectedTent ? risk.tentId === selectedTent.id : false))
    .sort((a, b) => riskRank(a.severity) - riskRank(b.severity) || a.startedAtUtc.localeCompare(b.startedAtUtc))

  // --- Ableitungen fuer den Bildschirm ---------------------------------------

  const sensorsLive = (live?.metrics ?? []).filter((metric) => metric.value && metric.value !== '\u2013').length

  // Die Score-Zeile des Entwurfs: "Klima 100 · Naehrloesung 64 (DO -36) · Technik 100".
  // Statt erfundener Teilscores nennt sie, was tatsaechlich danebenliegt — eine
  // Zahl, die niemand nachrechnen kann, waere schlimmer als keine.
  // Wie im Score: ein zurueckgerechnetes Ziel (Luft/Feuchte aus dem VPD-Ziel)
  // beschreibt dieselbe Lage wie VPD selbst und wird hier nicht zusaetzlich
  // aufgezaehlt — sonst stuenden drei Namen fuer ein Problem.
  const bewertbar = [...climateMetrics, ...hydroMetrics]
    .filter((metric) => metric.numericValue != null && !metric.targetDerived && (metric.targetMin != null || metric.targetMax != null))
  // Fork AI (F-041): dieselbe Lesart wie die Kachel — ein Einzelwert-Ziel gilt
  // auf die angezeigten Stellen gerundet, nicht auf die zehnte Nachkommastelle.
  const abweichungen = bewertbar
    .filter((metric) => kachelUrteil(
      metric.numericValue,
      { min: metric.targetMin, max: metric.targetMax },
      { min: metric.alarmMin ?? null, max: metric.alarmMax ?? null },
      decimalsForMetric(metric.key),
    ) !== 'ok')
  // „Alle Messwerte im Zielband" stand hier auch dann, wenn es gar kein Zielband
  // gab — die Zeile sagte „alles gut", wo sie „ich habe nichts geprueft" sagen
  // musste. Ohne bewertbaren Wert wird das jetzt benannt.
  const scoreParts = bewertbar.length === 0
    ? 'Keine Zielwerte — ohne aktiven Grow im Zelt gibt es nichts zu vergleichen'
    : abweichungen.length === 0
      ? `Alle ${bewertbar.length} bewerteten Werte im Zielband`
      : `${abweichungen.length} ${abweichungen.length === 1 ? 'Wert' : 'Werte'} daneben: ${abweichungen.map((metric) => metric.label).join(', ')}`

  const timeline = buildPhaseTimeline(primaryGrow)

  const lastMeasurement = primaryGrow?.latestMeasurementAt
    ? formatTime(primaryGrow.latestMeasurementAt)
    : null
  // Aus dem Zeitstrahl, nicht selbst gerechnet: die eigene Fassung zählte ab
  // Startdatum (Keimzeit inklusive) und nannte jede Phase so, wie die letzte
  // Messung hieß — direkt über dem Strahl, der beides richtig macht.
  const stageLine = primaryGrow ? currentPhaseLabel(timeline) : null
  const plantLine = primaryGrow?.plantCount ? `${primaryGrow.plantCount} Pflanzen` : null

  // Heute faellig: was die Watchdog-Risiken und der Addback-Bedarf hergeben.
  const tasks: LiveTask[] = []
  if (hasHydroGrow && primaryGrow) {
    const ec = hydroMetrics.find((metric) => metric.key === 'reservoir-ec')
    if (ec?.numericValue != null && ec.targetMax != null && ec.numericValue > ec.targetMax) {
      tasks.push({
        id: 'addback',
        when: 'fällig',
        title: `Addback · EC ${ec.numericValue.toFixed(2).replace('.', ',')} \u2192 ${ec.targetMax.toFixed(2).replace('.', ',')}`,
        action: 'Start',
        to: `/grows/${primaryGrow.id}/addback`,
        due: true,
      })
    }
  }
  // Ueberfaellige Routinen des angezeigten Grows. Sie standen bisher nur auf
  // /aufgaben — hier fehlten sie, obwohl derselbe Block „Heute faellig" heisst.
  for (const routine of (primaryGrow ? state.faelligeRoutinen[primaryGrow.id] ?? [] : []).slice(0, 3 - tasks.length)) {
    // Hat die Routine eine eigene Seite, fuehrt der Knopf dorthin und heisst
    // nach der Handlung. „Oeffnen → /aufgaben" war eine Sackgasse: dort steht
    // die Mahnung noch einmal, aber nicht das Formular.
    const weg = wegZurRoutine(routine.sopId)
    tasks.push({
      id: `routine-${routine.sopId}`,
      when: routine.severity === 'critical' ? 'überfällig' : 'fällig',
      title: routine.name,
      action: weg?.aktion ?? 'Öffnen',
      to: weg?.to ?? '/aufgaben',
      due: true,
    })
  }

  for (const risk of risksForContext.slice(0, 3 - tasks.length)) {
    tasks.push({
      id: `risk-${risk.id}`,
      when: risk.severity === 'Critical' ? 'jetzt' : 'offen',
      title: risk.title,
      action: 'Öffnen',
      to: '/diagnose',
      due: risk.severity === 'Critical',
    })
  }

  // --- Eigene Anordnung + Verlaufskurven ------------------------------------

  const { layout, entityValues, reload: reloadLayout } = useTentDashboard(selectedTent?.id ?? null)
  const trends = useTentSparklines(selectedTent?.id ?? null)

  // Ein Entwurf des vorigen Zelts gilt nach dem Umschalten nicht mehr.
  const activeDraft = draft && draft.tentId === selectedTent?.id ? draft.layout : null
  const editing = editingTentId != null && editingTentId === selectedTent?.id

  // Das Layout, das gerade gilt: der Entwurf im Anpassen-Modus, sonst das
  // Gespeicherte. Ohne beides zeichnet der Bildschirm seine festen Reihen.
  const shownLayout = activeDraft ?? layout

  const dashboardPanel: DashboardPanel | null = useMemo(() => {
    if (!selectedTent || !shownLayout) return null
    return {
      layout: shownLayout,
      entityValues,
      editing,
      saving: savingLayout,
      dirty: activeDraft != null,
      warning: layoutWarning,
      onChange: (next) => { setDraft({ tentId: selectedTent.id, layout: next }); setLayoutWarning(null) },
      onSave: () => void speichern(),
      onReset: () => void zuruecksetzen(),
      onToggleEditing: () => {
        if (editing) {
          // „Fertig": ein nicht gespeicherter Entwurf wird verworfen, nicht
          // stillschweigend behalten — sonst zeigt die Seite etwas an, das
          // nirgends steht.
          setDraft(null)
          setEditingTentId(null)
          setLayoutWarning(null)
          return
        }
        // Beim Einstieg genau das übernehmen, was gerade auf dem Schirm steht.
        setDraft({
          tentId: selectedTent.id,
          layout: shownLayout.isCustom
            ? shownLayout
            : seedLayout(selectedTent.id, [
              { title: 'Klima', metrics: climateForScreen },
              { title: 'Hydroponik · Nährlösung', metrics: hydroMetrics },
            ]),
        })
        setEditingTentId(selectedTent.id)
      },
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedTent, shownLayout, entityValues, editing, savingLayout, activeDraft, layoutWarning, climateForScreen, hydroMetrics])

  async function speichern() {
    if (!selectedTent || !activeDraft) return
    if (layoutIsEmpty(activeDraft)) {
      // Der Server wirft ein leeres Layout weg und liefert den Standard — das
      // sähe aus, als wäre das Speichern fehlgeschlagen. Lieber vorher sagen.
      setLayoutWarning('Mindestens eine Kachel muss stehen bleiben — sonst gibt es nichts anzuzeigen.')
      return
    }
    setSavingLayout(true)
    setLayoutWarning(null)
    try {
      await apiFetch<DashboardLayout>(`/api/tents/${selectedTent.id}/dashboard`, {
        method: 'PUT',
        body: JSON.stringify({ tentId: selectedTent.id, sections: activeDraft.sections }),
      })
      setDraft(null)
      setEditingTentId(null)
      reloadLayout()
    } catch (caught) {
      setLayoutWarning(formatApiError(caught, 'Anordnung konnte nicht gespeichert werden.'))
    } finally {
      setSavingLayout(false)
    }
  }

  async function zuruecksetzen() {
    if (!selectedTent) return
    setSavingLayout(true)
    setLayoutWarning(null)
    try {
      await apiFetch(`/api/tents/${selectedTent.id}/dashboard`, { method: 'DELETE' })
      setDraft(null)
      setEditingTentId(null)
      reloadLayout()
    } catch (caught) {
      setLayoutWarning(formatApiError(caught, 'Zurücksetzen fehlgeschlagen.'))
    } finally {
      setSavingLayout(false)
    }
  }

  if (loading) {
    return (
      <main className="ls">
        <V1Skeleton tiles={4} label="Lade Zelt" />
        <V1Skeleton tiles={5} rows={3} label="Lade Messwerte" />
      </main>
    )
  }

  // Frische Installation: ohne Zelt gibt es nichts zu zeigen. Statt eines leeren
  // Cockpits, das aussieht wie ein Fehler, steht hier der Weg — in der
  // Reihenfolge, in der es gemacht werden muss.
  if (state.tents.length === 0) {
    return (
      <main className="ls" data-audit="live-first-run">
        <header className="ls-head">
          <div className="ls-head-title">
            <div className="ls-eyebrow">Jetzt / Live</div>
            <h1>Willkommen bei Grow OS</h1>
            <div className="ls-head-parts">Drei Schritte bis zum ersten Messwert.</div>
          </div>
        </header>
        <ol className="ls-firstrun">
          <li>
            <div>
              <strong>Zelt anlegen</strong>
              <span>Der Raum: Größe, Licht, Abluft. Danach lassen sich Sensoren zuordnen.</span>
            </div>
            <Link className="ls-btn is-primary" to="/zelte/new">Zelt anlegen</Link>
          </li>
          <li>
            <div>
              <strong>Hydro-System anlegen</strong>
              <span>RDWC oder DWC: Sites, Topf- und Tankgröße. Daraus rechnet der Addback.</span>
            </div>
            <Link className="ls-btn" to="/hydro/new">Hydro anlegen</Link>
          </li>
          <li>
            <div>
              <strong>Grow starten</strong>
              <span>Sorte, Zelt und System wählen — ab dann zeigt diese Seite deine Werte.</span>
            </div>
            <Link className="ls-btn" to="/grows/new">Grow starten</Link>
          </li>
        </ol>
        <article className="ls-panel">
          <div className="ls-panel-body">
            <p>Lieber ausführlich? Die Ersten Schritte erklären Einrichtung und Home-Assistant-Anbindung.</p>
            <div className="ls-panel-actions">
              <Link className="ls-btn" to="/start">Erste Schritte</Link>
              <Link className="ls-btn" to="/home-assistant">Home Assistant verbinden</Link>
            </div>
          </div>
        </article>
      </main>
    )
  }

  return (
    <LiveScreen
      tent={selectedTent}
      grow={primaryGrow}
      score={score}
      scoreParts={scoreParts}
      climate={climateForScreen}
      hydro={hydroMetrics}
      alleMetriken={live?.metrics ?? []}
      sensorsLive={sensorsLive}
      lastMeasurement={lastMeasurement}
      stageLine={stageLine}
      risks={risksForContext}
      tasks={tasks}
      timeline={timeline.phases}
      timelineDates={timeline.dates}
      flipIsPlanned={timeline.flipIsPlanned}
      daysToFlip={timeline.daysToFlip}
      plantLine={plantLine}
      tents={state.tents}
      systemWarning={systemWarning}
      trends={trends}
      chiller={live?.chiller ?? null}
      dashboard={dashboardPanel}
      onTent={setSelectedTentId}
      onRefresh={() => setRefresh((current) => current + 1)}
    />
  )
}


/** „09:30" — die Uhrzeit reicht, das Datum steht im Journal. */
function formatTime(iso: string): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return ''
  return new Intl.DateTimeFormat('de-DE', { hour: '2-digit', minute: '2-digit' }).format(date)
}

/** „Tag 26" seit dem Start. */



export default LiveDashboardPage
