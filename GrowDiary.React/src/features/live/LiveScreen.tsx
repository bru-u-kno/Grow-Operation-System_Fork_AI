import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import type { GrowSummary, KuehlerLivePayload, MetricPayload, RiskEventDto, TentDto } from '../../types'
import type { HistoryPoint } from '../../components/SensorChart'
import { SensorChart } from '../../components/SensorChart'
import { V1Sheet } from '../../components/V1Sheet'
import { MetricTile } from './MetricTile'
import { decimalsForMetric } from './metric-tile-model'
import { CameraPanel } from './CameraPanel'
import { TrendWatchPanel } from './TrendWatchPanel'
import { DashboardBands } from './DashboardBands'
import { DashboardEditorBar } from './DashboardEditor'
import type { DashboardLayout, EntityValue } from './dashboard-layout'
import { buildScore, metricProvenance } from './live-model'
import { classNames } from '../../utils'
import { balkenText, flipLabel, type Phase } from '../grows/phase-timeline'

/**
 * Der Live-Bildschirm, gebaut nach dem Entwurf des Designers.
 *
 * Aufbau von oben: Kopfzeile mit Score und den zwei Handlungen, dann die
 * Messwerte in zwei beschrifteten Reihen (Klima, Nährlösung), dann Kamera neben
 * Risiko und heute fälligen Aufgaben, unten der Grow mit seiner Phasen-Timeline.
 *
 * Die Reihenfolge ist fest. Wer morgens auf diese Seite schaut, sucht denselben
 * Wert an derselben Stelle — eine Sortierung nach Dringlichkeit würde das jeden
 * Tag verschieben. Die Statusfarbe macht das Hervorheben.
 */

export type LiveTask = {
  id: string
  when: string
  title: string
  action: string
  to: string
  due?: boolean
}

export type LiveScreenProps = {
  tent: TentDto | null
  grow: GrowSummary | null
  score: ReturnType<typeof buildScore>
  scoreParts: string
  climate: MetricPayload[]
  hydro: MetricPayload[]
  /**
   * Alles, was der Server fuer dieses Zelt meldet.
   *
   * Die zwei festen Baender zeigen nur eine Auswahl: CO₂ ist in keinem von
   * beiden, PPFD faellt heraus sobald ein Licht-Zustand gemeldet wird, und von
   * den zwei Fuellstand-Varianten ueberlebt nur die gewaehlte. Wer eine solche
   * Kachel ueber „+ Kachel" hinzufuegte, bekam „—" als Wert und den rohen
   * Schluessel als Namen — obwohl der Wert direkt daneben in der Antwort steht.
   */
  alleMetriken?: MetricPayload[]
  sensorsLive: number
  lastMeasurement: string | null
  stageLine: string | null
  risks: RiskEventDto[]
  tasks: LiveTask[]
  /** Der geteilte Phasentyp — die Inline-Fassung kannte den Fortschritt nicht. */
  timeline: Phase[]
  timelineDates: { start: string; flip: string; harvest: string }
  /** Flip steht nur im Plan; die Beschriftung sagt das dann auch. */
  flipIsPlanned: boolean
  daysToFlip: number | null
  plantLine: string | null
  /** Alle Zelte zur Auswahl; erst ab zwei erscheint der Umschalter. */
  tents: TentDto[]
  /** Nur gesetzt, wenn die Überwachung SELBST ein Problem meldet (Watchdog). */
  systemWarning: { headline: string; detail: string } | null
  /** Die 24-h-Kurve je Messwert; fehlt sie, zeigt die Kachel ihr Zielband. */
  trends: Map<string, HistoryPoint[]>
  /**
   * Was der Kühler-Regler gerade tut — null, solange er für dieses Zelt aus ist.
   *
   * Absichtlich nur beim Einschalten: eine Karte, die dauerhaft „nicht
   * eingerichtet" sagt, wäre Rauschen auf dem Bildschirm, den man am
   * häufigsten ansieht.
   */
  chiller: KuehlerLivePayload | null
  /** Gesetzt, sobald der Nutzer eine eigene Anordnung hat oder gerade anpasst. */
  dashboard: DashboardPanel | null
  onTent: (tentId: number) => void
  onRefresh: () => void
}

/** Alles, was der Anpassen-Modus braucht — gebündelt, damit LiveScreen nicht zerfasert. */
export type DashboardPanel = {
  layout: DashboardLayout
  entityValues: Map<string, EntityValue>
  editing: boolean
  saving: boolean
  dirty: boolean
  warning: string | null
  onChange: (layout: DashboardLayout) => void
  onSave: () => void
  onReset: () => void
  /** Schaltet den Anpassen-Modus an und wieder aus — „Anpassen" oben, „Fertig" in der Leiste. */
  onToggleEditing: () => void
}

export function LiveScreen({
  tent, grow, score, scoreParts, climate, hydro, alleMetriken, sensorsLive,
  lastMeasurement, stageLine, risks, tasks, timeline, timelineDates, plantLine,
  flipIsPlanned, daysToFlip, tents, systemWarning, trends, chiller, dashboard, onTent, onRefresh,
}: LiveScreenProps) {
  const topRisk = risks[0] ?? null
  // Die eigene Anordnung zeichnet nur, wer eine hat oder gerade eine baut.
  // Sonst bleibt es bei den festen Reihen — Buchstabe fuer Buchstabe wie bisher.
  const eigeneAnordnung = dashboard && (dashboard.editing || dashboard.layout.isCustom)
  // Erst alles, was der Server kennt — dann die Baender darueber, denn deren
  // Fassungen tragen die zurueckgerechneten Ziele.
  const metricsByKey = new Map([...(alleMetriken ?? []), ...climate, ...hydro].map((metric) => [metric.key, metric]))
  const [offeneMetrik, setOffeneMetrik] = useState<string | null>(null)

  /* Fork AI: „⋯" neben dem Messen-Knopf. Die selten gebrauchten Handlungen
     (Addback, Anpassen, Zeltwechsel) liegen darunter, damit die Kopfzeile auf
     dem Telefon einzeilig bleibt statt drei vollbreite Knöpfe zu tragen. */
  const [weitereOffen, setWeitereOffen] = useState(false)
  const navigate = useNavigate()

  return (
    <main className="ls" data-audit="live-screen">
      {/* ---------- Kopfzeile ---------- */}
      <header className="ls-head">
        <ScoreRing value={score.value} tone={score.tone} />

        <div className="ls-head-title">
          <div className="ls-eyebrow">Jetzt / Live · Grow-Score</div>
          <h1 className={`ls-score-label is-${score.tone}`}>{score.label}</h1>
          <div className="ls-head-parts">{scoreParts}</div>
        </div>

        <span className="ls-pill">
          <i />{sensorsLive} Sensoren live
        </span>

        <div className="ls-head-bar">
          <span className="ls-head-meta">
            {[lastMeasurement && `Letzte Messung ${lastMeasurement}`, stageLine, grow?.name]
              .filter(Boolean).join(' · ')}
          </span>

          <div className="ls-head-actions">
            <Link className="ls-btn is-primary" to="/messung">Messen</Link>
            <button
              type="button"
              className="ls-btn ls-head-more"
              onClick={() => setWeitereOffen(true)}
              aria-label="Weitere Handlungen"
              data-audit="live-more"
            >
              ⋯
            </button>
          </div>
        </div>
      </header>

      <V1Sheet open={weitereOffen} onClose={() => setWeitereOffen(false)} title="Weitere Handlungen">
        <button
          type="button"
          className="forkai-sheet-item"
          onClick={() => { setWeitereOffen(false); navigate('/addback') }}
        >
          <span className="forkai-sheet-icon" aria-hidden="true">↻</span>
          <span className="forkai-sheet-text">
            <strong>Addback starten</strong>
            <span>Nachfüllen mit Zielwerten</span>
          </span>
        </button>
        {dashboard && !dashboard.editing && (
          <button
            type="button"
            className="forkai-sheet-item"
            onClick={() => { setWeitereOffen(false); dashboard.onToggleEditing() }}
            data-audit="dashboard-customise"
          >
            <span className="forkai-sheet-icon" aria-hidden="true">▦</span>
            <span className="forkai-sheet-text">
              <strong>Anpassen</strong>
              <span>Kacheln dieser Seite umstellen</span>
            </span>
          </button>
        )}
        {tents.length > 1 && (
          <label className="ls-sheet-zelt">
            Zelt
            <select
              className="ls-tent-select"
              aria-label="Zelt"
              value={tent?.id ?? ''}
              onChange={(event) => { onTent(Number(event.target.value)); setWeitereOffen(false) }}
            >
              {tents.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}
            </select>
          </label>
        )}
      </V1Sheet>

      {/* ---------- Systemwarnung ---------- */}
      {/* Wenn die Überwachung selbst schweigt, sind alle Kacheln darunter
          womöglich alte Zahlen — das muss ÜBER den Messwerten stehen, nicht
          in einer Benachrichtigung, die erst abends jemand liest. */}
      {systemWarning && (
        <Link className="ls-syswarn" to="/regeln?tab=push" data-audit="live-system-warning">
          <span className="ls-label">Systemüberwachung</span>
          <span className="ls-syswarn-text"><strong>{systemWarning.headline}</strong> — {systemWarning.detail}</span>
        </Link>
      )}

      {/* ---------- Messwerte ---------- */}
      {dashboard?.editing && (
        <DashboardEditorBar
          layout={dashboard.layout}
          onChange={dashboard.onChange}
          onSave={dashboard.onSave}
          onReset={dashboard.onReset}
          onClose={dashboard.onToggleEditing}
          saving={dashboard.saving}
          dirty={dashboard.dirty}
          warning={dashboard.warning}
        />
      )}

      <section className="ls-metrics">
        {eigeneAnordnung && dashboard ? (
          <DashboardBands
            layout={dashboard.layout}
            metricsByKey={metricsByKey}
            entityValues={dashboard.entityValues}
            trends={trends}
            editing={dashboard.editing}
            onChange={dashboard.onChange}
          />
        ) : (
          <>
            <MetricBand title="Klima" metrics={climate} trends={trends} offeneMetrik={offeneMetrik} setOffeneMetrik={setOffeneMetrik} />
            <MetricBand title="Hydroponik · Nährlösung" metrics={hydro} trends={trends} offeneMetrik={offeneMetrik} setOffeneMetrik={setOffeneMetrik} />
          </>
        )}
      </section>

      {/* ---------- Risiko · Aufgaben · Kamera ---------- */}
      {/* Die Handlung steht VOR dem Bild — im Quelltext, nicht auf dem Schirm.
          Sobald die zwei Spalten umbrechen (unter ~924 px, also auf jedem
          Telefon), zaehlt die Reihenfolge hier: vorher lag „kritisches Risiko“
          und „heute fällig“ hinter der Kamerabuehne, die auch ohne
          zugeordnete Kamera 260 px belegt — zusammen mit fuenf Kachelzeilen
          eine gute Bildschirmhoehe unter dem Sichtbaren. Am breiten Schirm
          holt `order` die Kamera nach links zurueck, dort aendert sich
          nichts. */}
      <section className="ls-lower">
        <div className="ls-lower-right">
          {topRisk ? (
            <article className={classNames('ls-panel', 'ls-risk', `is-${topRisk.severity.toLowerCase()}`)} data-audit="live-risk">
              <div className="ls-panel-head">
                <span className="ls-label">Risiko · {topRisk.severity === 'Critical' ? 'kritisch' : topRisk.severity === 'Warning' ? 'Warnung' : 'Hinweis'}</span>
                <span className="ls-panel-meta">{topRisk.startedAtUtc ? `seit ${sinceLabel(topRisk.startedAtUtc)}` : ''}</span>
              </div>
              <div className="ls-panel-body">
                <strong>{topRisk.title}</strong>
                {topRisk.description && <p>{topRisk.description}</p>}
                <div className="ls-panel-actions">
                  {/* Ein laufender SOP hat eine Instanz — dann fuehrt der Knopf
                      dorthin, sonst in die Diagnose, wo die Prozedur ausgewaehlt
                      wird. Kein erfundener Link auf eine SOP-Id, die es im DTO
                      nicht gibt. */}
                  {topRisk.sopInstanceId
                    ? <Link className="ls-btn is-primary" to={`/sops?instance=${topRisk.sopInstanceId}`}>SOP fortsetzen</Link>
                    : <Link className="ls-btn is-primary" to="/diagnose">Maßnahme wählen</Link>}
                  <Link className="ls-btn" to="/aufgaben">Aufgaben</Link>
                </div>
              </div>
            </article>
          ) : (
            <article className="ls-panel" data-audit="live-risk">
              <div className="ls-panel-head"><span className="ls-label">Risiken</span></div>
              <div className="ls-panel-body"><strong>Nichts Offenes</strong><p>Keine kritischen Abweichungen im gewählten Zelt.</p></div>
            </article>
          )}

          <article className="ls-panel" data-audit="live-tasks">
            <div className="ls-panel-head">
              <span className="ls-label">Heute fällig</span>
              <span className="ls-panel-meta">{tasks.length} {tasks.length === 1 ? 'Aufgabe' : 'Aufgaben'}</span>
              <Link className="ls-btn is-small" to="/aufgaben">Alle</Link>
            </div>
            {tasks.length === 0 ? (
              <div className="ls-panel-body"><p>Für heute steht nichts an.</p></div>
            ) : (
              <ul className="ls-tasks">
                {tasks.slice(0, 3).map((task) => (
                  <li key={task.id}>
                    <span className={classNames('ls-task-when', task.due && 'is-due')}>{task.when}</span>
                    <span className="ls-task-title">{task.title}</span>
                    <Link className="ls-btn is-small" to={task.to}>{task.action}</Link>
                  </li>
                ))}
              </ul>
            )}
          </article>

          {/* Der Kühler steht hier und nicht bei den Kacheln: eine Kachel zeigt
              einen Messwert, das hier ist eine laufende Steuerung. Ohne den
              Grund sieht ein stehender Kühler bei 21 °C wie ein Fehler aus,
              obwohl gerade die Mindestpause läuft. */}
          {chiller && (
            <article className="ls-panel ls-chiller" data-audit="live-chiller">
              <div className="ls-panel-head">
                <span className="ls-label">Kühler · Crop Steering</span>
                <span className="ls-panel-meta">
                  {chiller.tagbetrieb ? 'Tagwert' : 'Nachtwert'}
                  {chiller.sollC != null ? ` ${grad(chiller.sollC)}` : ' – '}
                </span>
                <Link className="ls-btn is-small" to="/cropsteering">Einstellen</Link>
              </div>
              <div className="ls-panel-body">
                <strong className={classNames('ls-chiller-state', chiller.laeuftGerade === true && 'is-an', chiller.laeuftGerade === false && 'is-aus')}>
                  {chiller.laeuftGerade == null ? 'Zustand der Steckdose unbekannt' : chiller.laeuftGerade ? 'läuft' : 'steht'}
                  {chiller.istC != null ? ` · Wasser ${grad(chiller.istC)}` : ''}
                </strong>
                <p>{chiller.grund}</p>
              </div>
            </article>
          )}

          {/* Die Watchdog-Beobachtungen (Drift, Verbrauch) blieben beim Umbau
              zunaechst auf der Strecke — sie sind ein bestehendes Feature und
              gehoeren zu „heute fällig“ dazu: was sich ueber Tage anbahnt. */}
          <TrendWatchPanel growId={grow?.id ?? null} />
        </div>

        <CameraPanel tent={tent} onReload={onRefresh} />
      </section>

      {/* ---------- Grow im Zelt ---------- */}
      {grow && (
        <section className="ls-panel ls-grow" data-audit="live-grow">
          <div className="ls-panel-head">
            <span className="ls-label">Grow im Zelt</span>
            <span className="ls-panel-meta">{[grow.name, plantLine, tent?.name].filter(Boolean).join(' · ')}</span>
            <Link className="ls-btn is-small" to={`/grows/${grow.id}`}>Grow öffnen</Link>
          </div>
          <div className="ls-panel-body">
            {/* Wischbar auf dem Telefon: die Balkenlaengen SIND die Dauer, also
                darf die Achse nicht umbrechen — sie scrollt lieber in sich. */}
            <div className="ls-timeline-wrap">
            <div className="ls-timeline">
              {timeline.map((phase) => (
                <div
                  key={phase.label}
                  className={classNames('ls-phase', `is-${phase.state}`, phase.days === 0 && 'is-unknown')}
                  /* Ohne bekannte Dauer KEIN flexGrow — ein Inline-Stil
                     schlaegt jede Regel, und `.ls-phase.is-unknown
                     { flex-grow: 0 }` blieb dadurch wirkungslos. */
                  style={phase.days === 0 ? undefined : { flexGrow: phase.days }}
                >
                  {/* Der Fuellstand zeigt, wo im Plan man heute steht — die
                      Balkenbreite allein sagt nur, wie lang die Phase ist. */}
                  {phase.progress != null && (
                    <i className="ls-phase-fill" style={{ width: `${Math.round(phase.progress * 100)}%` }} aria-hidden="true" />
                  )}
                  <span>{balkenText(phase.short, phase.days)}</span>
                </div>
              ))}
            </div>
            <div className="ls-timeline-dates">
              <span>Start {timelineDates.start}</span>
              <span className={daysToFlip != null && daysToFlip < 0 ? 'is-due' : undefined}>{flipLabel(flipIsPlanned, daysToFlip, timelineDates.flip)}</span>
              <span>{timelineDates.harvest === '\u2014' ? 'Ernte offen' : `Ernte ~${timelineDates.harvest}`}</span>
            </div>
            </div>
          </div>
        </section>
      )}
    </main>
  )
}

/** Eine beschriftete Reihe Messwerte mit Haarlinie bis zum Rand. */
/**
 * Ein Band aus Kacheln — anklickbar, mit aufklappbarem Verlauf.
 *
 * <b>Der Fehler, der hier steckte.</b> Der Kachel-Klick kam in beta.38 und
 * wurde nur in `DashboardBands` eingebaut — also in der Ansicht mit eigener
 * Anordnung. Wer keine gespeichert hat, sieht dieses Band hier, und es reichte
 * `onOpen` nie durch: die Kacheln sahen gleich aus und taten nichts. Das ist
 * der Standardfall, also traf es die Mehrheit.
 *
 * Beide Ansichten zeigen jetzt denselben Verlauf an derselben Stelle: unter
 * der Zeile, nicht als Fenster darüber — man will die Nachbarkacheln zum
 * Vergleich weiter sehen.
 */
function MetricBand({ title, metrics, trends, offeneMetrik, setOffeneMetrik }: {
  title: string
  metrics: MetricPayload[]
  trends: Map<string, HistoryPoint[]>
  /**
   * Welcher Verlauf offen ist — geteilt über BEIDE Bänder.
   *
   * Lag der Zustand in jedem Band für sich, konnten zwei Verläufe gleichzeitig
   * aufgeklappt sein: einer im Klima, einer in der Nährlösung. Gemeint ist
   * aber „der eine, den ich gerade ansehe".
   */
  offeneMetrik: string | null
  setOffeneMetrik: (key: string | null) => void
}) {
  if (metrics.length === 0) return null

  const offene = offeneMetrik ? metrics.find((m) => m.key === offeneMetrik) : null
  const punkte = offeneMetrik ? (trends.get(offeneMetrik) ?? []) : []

  return (
    <>
      <div className="ls-band-label">
        <span>{title}</span>
        <i />
      </div>
      <div className="gos-metric-row">
        {metrics.map((metric) => {
          const herkunft = metricProvenance(metric)
          // Klickbar nur mit Verlauf: eine Kachel, die sich als Knopf anbietet
          // und dann nichts zeigt, ist schlimmer als eine, die stumm bleibt.
          const hatVerlauf = (trends.get(metric.key)?.length ?? 0) > 1
          return (
            <MetricTile
              key={metric.key}
              label={metric.label}
              value={metric.numericValue}
              unit={metric.unit}
              targetMin={metric.targetMin}
              targetMax={metric.targetMax}
              decimals={decimalsForMetric(metric.key)}
              display={metric.numericValue == null && metric.value !== '–' ? metric.value : undefined}
              footer={metric.targetMin == null && metric.targetMax == null ? (metric.hint ?? undefined) : undefined}
              trend={trends.get(metric.key)}
              targetNote={metric.targetNote}
              sourceNote={herkunft.sourceNote}
              stale={herkunft.stale}
              onOpen={hatVerlauf ? () => setOffeneMetrik(offeneMetrik === metric.key ? null : metric.key) : undefined}
              open={offeneMetrik === metric.key}
            />
          )
        })}
      </div>

      {offene && punkte.length > 1 && (
        <div className="ls-metric-detail" data-audit="metric-detail">
          <SensorChart
            series={{ metricKey: offene.key, label: offene.label, unit: offene.unit, points: punkte }}
            target={{ min: offene.targetMin, max: offene.targetMax }}
          />
        </div>
      )}
    </>
  )
}

/**
 * Der Score als Ring. Bewusst klein: er ist die Zusammenfassung, nicht die
 * Nachricht — die steht als Wort daneben.
 */
/**
 * Der Score-Ring — in der Farbe seiner Bewertung.
 *
 * Der Ring war fest auf Akzentgrün gesetzt: bei 40 Punkten stand daneben
 * „Kritisch“ und der Kreis leuchtete trotzdem grün. Die Bewertung kommt schon
 * immer aus `buildScore`, sie wurde hier nur nicht benutzt.
 */
function ScoreRing({ value, tone }: { value: number | null; tone: 'ok' | 'warn' | 'critical' | 'neutral' }) {
  // null heisst „nicht bewertet" — der Ring bleibt dann leer, statt eine Null
  // als Note auszugeben.
  const clamped = value == null ? 0 : Math.max(0, Math.min(100, value))
  const farbe = tone === 'critical' ? 'var(--danger)'
    : tone === 'warn' ? 'var(--warn)'
      : tone === 'ok' ? 'var(--accent)'
        : 'var(--hair-2)'
  return (
    <div
      className={classNames('ls-ring', `is-${tone}`)}
      style={{ background: `conic-gradient(${farbe} 0 ${clamped}%, var(--sunk) ${clamped}% 100%)` }}
      role="img"
      aria-label={value == null ? 'Grow-Score: nicht bewertet' : `Grow-Score ${clamped} von 100`}
    >
      <div className="ls-ring-inner">
        {/* Ein Strich statt einer Null: „0 /100" sah aus wie die schlechteste
            aller Noten, während daneben „Nicht bewertet" stand. */}
        <span>{value == null ? '—' : clamped}</span>
        {value != null && <span className="ls-ring-max">/100</span>}
      </div>
    </div>
  )
}

/** „19,4 °C" — eine Nachkommastelle, deutsches Komma. */
function grad(wert: number): string {
  return `${wert.toLocaleString('de-DE', { minimumFractionDigits: 1, maximumFractionDigits: 1 })} °C`
}

function sinceLabel(iso: string): string {
  const started = new Date(iso).getTime()
  if (Number.isNaN(started)) return ''
  const minutes = Math.max(0, Math.round((Date.now() - started) / 60000))
  if (minutes < 60) return `${minutes} min`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours} h ${minutes % 60} min`
  return `${Math.floor(hours / 24)} Tagen`
}

