import { useEffect, useState } from 'react'
import { aktiveRegeln, kannPlan, speicherbareRegeln, vertauschteGrenzen } from '../features/alerts/grenzwerte-modell'
import type { Grenzwertquelle } from '../features/alerts/grenzwerte-modell'
import { useSearchParams } from 'react-router-dom'
import { apiFetch } from '../api'
import type { GrowSummary, MetricPayload, TentDto, TentLivePayload } from '../types'
import type { AutoMeasurementConfigDto } from '../types/automation'
import type { AlertRuleDto, TentAlertRulesDto } from '../types/alert'
import type { NotificationSettingsDto } from '../types/notification'
import { V1Alert, V1Empty, V1Skeleton, V1Tabs } from '../components/v1'
import { decimalsForMetric } from '../features/live/metric-tile-model'
import { feldText, zahlOderNull } from '../zahlenfeld'

type MetricDef = { key: string; label: string; unit: string; min: string; max: string }

/* Die Vorgabewerte stehen als PLATZHALTER in den Feldern — also mit Komma.
   Bis zum 01.09.2026 stand dort "5.5"/"6.5"/"1.2", und der Platzhalter bringt
   dem Nutzer die Schreibweise bei, die er gleich tippen soll. */
const ALERT_METRICS: MetricDef[] = [
  { key: 'reservoir-ph', label: 'pH', unit: '', min: '5,5', max: '6,5' },
  { key: 'reservoir-ec', label: 'EC', unit: 'mS/cm', min: '1,2', max: '2,4' },
  { key: 'reservoir-temp', label: 'Wassertemp', unit: '°C', min: '18', max: '22' },
  { key: 'orp', label: 'ORP', unit: 'mV', min: '200', max: '400' },
  { key: 'dissolved-oxygen', label: 'Sauerstoff (DO)', unit: 'mg/L', min: '6', max: '' },
  { key: 'reservoir-level', label: 'Wasserstand', unit: 'L', min: '20', max: '' },
  { key: 'reservoir-level-cm', label: 'Wasserstand', unit: 'cm', min: '', max: '' },
  { key: 'temperature', label: 'Lufttemp', unit: '°C', min: '20', max: '28' },
  { key: 'humidity', label: 'Luftfeuchte', unit: '%', min: '40', max: '65' },
  { key: 'vpd', label: 'VPD', unit: 'kPa', min: '0,8', max: '1,5' },
  { key: 'co2', label: 'CO₂', unit: 'ppm', min: '', max: '1500' },
]

type Row = { min: string; max: string; cooldown: string; enabled: boolean; quelle: Grenzwertquelle; toleranz: string }
type Rows = Record<string, Row>

function emptyRows(): Rows {
  return Object.fromEntries(ALERT_METRICS.map((metric) => [
    metric.key,
    { min: '', max: '', cooldown: '30', enabled: false, quelle: 'Fest' as Grenzwertquelle, toleranz: '' },
  ]))
}

/**
 * Eine Zahl für ein Eingabefeld.
 *
 * Die Umwandlung steht in <code>zahlenfeld.ts</code> und nur dort — sie stand
 * am 01.09.2026 fünfmal in der Oberfläche, jedes Mal mit
 * <code>String(value)</code> und damit mit englischem Punkt.
 */
function numberToInput(value: number | null): string {
  return feldText(value)
}

function errorMessage(caught: unknown, fallback: string): string {
  return caught instanceof Error ? caught.message : fallback
}

/**
 * Grenzwerte als Tabelle, wie im Entwurf: eine Zeile je Metrik mit Unter-,
 * Obergrenze und Karenz. Darunter die Kurzfassung von Auto-Messungen und
 * Benachrichtigungen — Bearbeiten wechselt in den jeweiligen Tab.
 *
 * Karenz gilt je Regel (der Entwurf zeigt sie je Zeile); solange der Wert
 * außerhalb liegt, erinnert Grow OS in diesem Takt erneut.
 */
function AlertsPage() {
  const [, setParams] = useSearchParams()
  const [tents, setTents] = useState<TentDto[]>([])
  const [selectedTentId, setSelectedTentId] = useState<number | null>(null)
  const [rows, setRows] = useState<Rows>(emptyRows())
  const [autoConfigs, setAutoConfigs] = useState<AutoMeasurementConfigDto[]>([])
  const [notifications, setNotifications] = useState<NotificationSettingsDto | null>(null)
  const [ziele, setZiele] = useState<Map<string, MetricPayload>>(new Map())
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    async function load() {
      setLoading(true)
      try {
        const [tentList, grows, notificationSettings] = await Promise.all([
          apiFetch<TentDto[]>('/api/settings/tents', { signal: controller.signal }),
          apiFetch<GrowSummary[]>('/api/grows?archived=false', { signal: controller.signal }).catch(() => []),
          apiFetch<NotificationSettingsDto>('/api/notifications/settings', { signal: controller.signal }).catch(() => null),
        ])
        if (controller.signal.aborted) return
        const sorted = [...tentList].sort((a, b) => a.displayOrder - b.displayOrder || a.name.localeCompare(b.name))
        setTents(sorted)
        setSelectedTentId((current) => current ?? sorted[0]?.id ?? null)
        setNotifications(notificationSettings)

        const runningGrow = grows.find((grow) => grow.status === 'Running') ?? grows[0]
        if (runningGrow) {
          const configs = await apiFetch<AutoMeasurementConfigDto[]>(`/api/auto-measurements/configs?growId=${runningGrow.id}`, { signal: controller.signal }).catch(() => [])
          if (!controller.signal.aborted) setAutoConfigs(configs)
        }
      } catch (caught) {
        if (!controller.signal.aborted) setError(errorMessage(caught, 'Zelte konnten nicht geladen werden.'))
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }
    void load()
    return () => controller.abort()
  }, [])

  useEffect(() => {
    if (selectedTentId == null) return
    const controller = new AbortController()
    async function loadRules(tentId: number) {
      setMessage(null)
      try {
        const dto = await apiFetch<TentAlertRulesDto>(`/api/alerts/tents/${tentId}`, { signal: controller.signal })
        if (controller.signal.aborted) return
        const next = emptyRows()
        for (const rule of dto.rules) {
          if (next[rule.metricKey]) {
            next[rule.metricKey] = {
              min: numberToInput(rule.minValue),
              max: numberToInput(rule.maxValue),
              quelle: rule.quelle === 'Plan' ? 'Plan' : 'Fest',
              toleranz: numberToInput(rule.toleranz),
              cooldown: String(rule.cooldownMinutes),
              enabled: rule.enabled,
            }
          }
        }
        setRows(next)
      } catch (caught) {
        if (!controller.signal.aborted) setError(errorMessage(caught, 'Grenzwerte konnten nicht geladen werden.'))
      }
    }
    void loadRules(selectedTentId)
    return () => controller.abort()
  }, [selectedTentId])

  // Die Zielwerte der aktuellen Phase kommen aus derselben Quelle wie die
  // Kacheln auf Live — damit hier keine dritte Wahrheit entsteht.
  useEffect(() => {
    if (selectedTentId == null) return
    const controller = new AbortController()
    async function loadZiele(tentId: number) {
      try {
        const live = await apiFetch<TentLivePayload>(`/api/live/tents/${tentId}`, { signal: controller.signal })
        if (controller.signal.aborted) return
        setZiele(new Map(live.metrics
          .filter((metric) => metric.targetMin != null || metric.targetMax != null)
          .map((metric) => [metric.key, metric])))
      } catch {
        /* Ohne Ziele bleibt die Spalte leer — die Grenzwerte funktionieren weiter. */
      }
    }
    void loadZiele(selectedTentId)
    return () => controller.abort()
  }, [selectedTentId])

  /** Fuellt leere Felder aus den Zielwerten; vorhandene Eingaben bleiben. */
  function zieleUebernehmen() {
    setRows((current) => {
      const next = { ...current }
      for (const metric of ALERT_METRICS) {
        const ziel = ziele.get(metric.key)
        if (!ziel) continue
        const zeile = next[metric.key]
        const min = zeile.min === '' && ziel.targetMin != null ? String(ziel.targetMin) : zeile.min
        const max = zeile.max === '' && ziel.targetMax != null ? String(ziel.targetMax) : zeile.max
        next[metric.key] = { ...zeile, min, max, enabled: zeile.enabled || min !== '' || max !== '' }
      }
      return next
    })
    setMessage('Leere Felder aus den Zielwerten gefüllt — prüfen und speichern.')
  }

  function setRow(key: string, patch: Partial<Row>) {
    setRows((current) => ({ ...current, [key]: { ...current[key], ...patch } }))
  }

  /* Was gespeichert wird — und was davon wirklich wacht.
     Vorher gingen NUR die angehakten Zeilen raus, und der Server ersetzt den
     ganzen Satz: den Haken herauszunehmen loeschte die Grenzen. */
  const speicherbar = speicherbareRegeln(ALERT_METRICS.map((m) => m.key), rows)
  const wachend = aktiveRegeln(speicherbar)

  async function save() {
    if (selectedTentId == null) return
    setSaving(true)
    setError(null)
    setMessage(null)
    // Vertauschte Grenzen vor dem Absenden — der Server lehnt sie ab, aber
    // der Nutzer soll wissen WARUM, statt eine Fehlermeldung zu lesen.
    const vertauscht = vertauschteGrenzen(speicherbar)
    if (vertauscht.length > 0) {
      const namen = vertauscht
        .map((k) => ALERT_METRICS.find((m) => m.key === k)?.label ?? k)
        .join(', ')
      setError(`Bei ${namen} liegt die Untergrenze über der Obergrenze. `
        + 'So gemeldet würde die Regel dauerhaft warnen — bitte tauschen.')
      setSaving(false)
      return
    }

    const payload: { rules: AlertRuleDto[] } = { rules: speicherbar }
    try {
      await apiFetch<TentAlertRulesDto>(`/api/alerts/tents/${selectedTentId}`, {
        method: 'PUT',
        body: JSON.stringify(payload),
      })
      setMessage(speicherbar.length === 0
        ? 'Alle Grenzwerte für dieses Zelt entfernt.'
        : wachend === speicherbar.length
          ? `${speicherbar.length} Grenzwert(e) gespeichert.`
          : `${speicherbar.length} Grenzwert(e) gespeichert, davon ${wachend} aktiv.`)
    } catch (caught) {
      setError(errorMessage(caught, 'Speichern fehlgeschlagen.'))
    } finally {
      setSaving(false)
    }
  }

  function openTab(tab: string) {
    setParams((current) => {
      const next = new URLSearchParams(current)
      next.set('tab', tab)
      return next
    })
  }

  if (loading) return <V1Skeleton rows={5} label="Lade Grenzwerte" />

  if (tents.length === 0) {
    return <V1Empty title="Noch kein Zelt" text="Lege zuerst ein Zelt an und mappe deine Sensoren, dann kannst du hier Grenzwerte setzen." />
  }

  const selectedTent = tents.find((tent) => tent.id === selectedTentId) ?? tents[0]

  return (
    <>
      {error && <V1Alert message={error} tone="critical" />}
      {message && <V1Alert message={message} tone="ok" />}

      {tents.length > 1 && (
        <V1Tabs
          label="Zelt"
          active={selectedTent.id}
          onChange={(id) => setSelectedTentId(id)}
          items={tents.map((tent) => ({ value: tent.id, label: tent.name }))}
        />
      )}

      <section className="ls-panel" data-audit="alert-rules-table">
        <div className="ls-panel-head">
          <span className="ls-label">Grenzwerte · {selectedTent.name}</span>
          <span className="ls-panel-meta">leere Felder = keine Grenze; Karenz in Minuten</span>
          {ziele.size > 0 && (
            <button type="button" className="ls-btn is-small" onClick={zieleUebernehmen}>Ziele übernehmen</button>
          )}
          <button type="button" className="ls-btn is-small is-primary" disabled={saving} onClick={() => void save()}>
            {saving ? 'Speichert…' : 'Speichern'}
          </button>
        </div>
        <div className="co-table-wrap">
        <div className="co-table" style={{ gridTemplateColumns: '1.5fr .9fr .8fr .5fr .5fr .5fr .45fr .4fr' }}>
          <div className="co-th">Metrik</div>
          <div className="co-th">Ziel dieser Phase</div>
          <div className="co-th">Quelle</div>
          <div className="co-th">Warnen unter</div>
          <div className="co-th">Warnen über</div>
          <div className="co-th">Toleranz ±</div>
          <div className="co-th">Karenz</div>
          <div className="co-th">Aktiv</div>
          {ALERT_METRICS.map((metric) => {
            const row = rows[metric.key]
            return (
              <AlertRow key={metric.key}>
                <div className="co-td is-name">{metric.label}{metric.unit ? <span className="co-unit">{metric.unit}</span> : null}</div>
                <div className="co-td is-muted">{zielText(ziele.get(metric.key), metric.key)}</div>
                <div className="co-td">
                  {kannPlan(metric.key) ? (
                    <div className="v1-tabs" role="group" aria-label={`${metric.label} Quelle`}>
                      <button type="button" className={row.quelle === 'Fest' ? 'v1-tab active' : 'v1-tab'} onClick={() => setRow(metric.key, { quelle: 'Fest' })}>Fest</button>
                      <button type="button" className={row.quelle === 'Plan' ? 'v1-tab active' : 'v1-tab'} onClick={() => setRow(metric.key, { quelle: 'Plan' })}>Plan</button>
                    </div>
                  ) : (
                    <span className="is-muted">fest</span>
                  )}
                </div>
                {/* Bei „Plan" stehen hier die Grenzen, die gerade daraus werden — gesperrt,
                    weil sie nicht eingetippt, sondern gerechnet sind. */}
                <div className="co-td"><input inputMode="decimal" disabled={row.quelle === 'Plan'} value={row.quelle === 'Plan' ? planGrenze(ziele.get(metric.key), metric.key, row.toleranz, 'min') : row.min} placeholder={metric.min || '—'} aria-label={`${metric.label} warnen unter`} onChange={(event) => setRow(metric.key, { min: event.target.value })} /></div>
                <div className="co-td"><input inputMode="decimal" disabled={row.quelle === 'Plan'} value={row.quelle === 'Plan' ? planGrenze(ziele.get(metric.key), metric.key, row.toleranz, 'max') : row.max} placeholder={metric.max || '—'} aria-label={`${metric.label} warnen über`} onChange={(event) => setRow(metric.key, { max: event.target.value })} /></div>
                <div className="co-td"><input inputMode="decimal" disabled={row.quelle !== 'Plan'} value={row.toleranz} placeholder={row.quelle === 'Plan' ? standardToleranz(metric.key) : '—'} aria-label={`${metric.label} Toleranz`} onChange={(event) => setRow(metric.key, { toleranz: event.target.value })} /></div>
                <div className="co-td"><input inputMode="numeric" value={row.cooldown} aria-label={`${metric.label} Karenz in Minuten`} onChange={(event) => setRow(metric.key, { cooldown: event.target.value })} /></div>
                <div className="co-td">
                  <label className="co-check">
                    <input type="checkbox" checked={row.enabled} aria-label={`${metric.label} aktiv`} onChange={(event) => setRow(metric.key, { enabled: event.target.checked })} />
                  </label>
                </div>
              </AlertRow>
            )
          })}
        </div>
        </div>
      </section>

      <div className="co-grid is-300">
        <section className="ls-panel" data-audit="rules-auto-summary">
          <div className="ls-panel-head"><span className="ls-label">Auto-Messungen</span></div>
          {autoConfigs.length === 0 ? (
            <div className="ls-panel-body"><p>Noch keine Auto-Messung eingerichtet.</p></div>
          ) : (
            autoConfigs.map((config) => (
              <div key={config.id} className="co-row">
                <span className={`co-dot${config.status === 'Enabled' ? ' is-on' : ''}`} aria-hidden="true" />
                <div style={{ minWidth: 0 }}>
                  <div className="co-row-title">{config.name}</div>
                  <div className="co-row-sub">{triggerLabel(config)}{config.captureSnapshot ? ' · Kamera-Snapshot' : ''}</div>
                </div>
                <div className="co-row-end">
                  <button type="button" className="ls-btn is-small" onClick={() => openTab('automatik')}>Bearbeiten</button>
                </div>
              </div>
            ))
          )}
        </section>

        <section className="ls-panel" data-audit="rules-notify-summary">
          <div className="ls-panel-head"><span className="ls-label">Benachrichtigungen</span></div>
          <div className="co-row">
            <span className="co-row-text">Push über Home Assistant App</span>
            <span className={`co-row-value${notifications?.notifyService ? ' is-good' : ' is-faint'}`}>{notifications?.notifyService ? 'Aktiv' : 'Aus'}</span>
          </div>
          <div className="co-row">
            <span className="co-row-text">{quietHoursLabel(notifications)}</span>
            <span className="co-row-value">{notifications?.quietHoursStartHour != null ? 'Nur kritisch' : '—'}</span>
          </div>
          <div className="co-row">
            <span className="co-row-text">Einstellungen &amp; Kategorien</span>
            <div className="co-row-end">
              <button type="button" className="ls-btn is-small" onClick={() => openTab('push')}>Bearbeiten</button>
            </div>
          </div>
        </section>
      </div>
    </>
  )
}

/**
 * „6,00–6,10" bzw. „≥ 7,0" — das Zielband der aktuellen Phase aus der
 * Wissensbasis. Es steht neben den Grenzwerten, damit sichtbar ist, worauf
 * sich die Warnung eigentlich bezieht.
 */
function zielText(metric: MetricPayload | undefined, key: string): string {
  if (!metric) return '—'
  const stellen = decimalsForMetric(key)
  const zahl = (wert: number) => wert.toFixed(stellen).replace('.', ',')
  if (metric.targetMin != null && metric.targetMax != null) return `${zahl(metric.targetMin)}–${zahl(metric.targetMax)}`
  if (metric.targetMin != null) return `≥ ${zahl(metric.targetMin)}`
  if (metric.targetMax != null) return `≤ ${zahl(metric.targetMax)}`
  return '—'
}

/**
 * Die Standard-Toleranz einer Messgröße, als Platzhalter im Feld.
 *
 * Dieselben Zahlen wie im Backend (<code>Planzielgrenzen.StandardToleranz</code>).
 * Sie stehen hier nur als Anzeige — gerechnet wird auf dem Server.
 */
function standardToleranz(key: string): string {
  switch (key) {
    case 'reservoir-ph': return '0,20'
    case 'reservoir-ec': return '0,20'
    case 'reservoir-temp': return '2'
    case 'orp': return '50'
    case 'vpd': return '0,20'
    case 'co2': return '200'
    case 'ppfd': return '100'
    default: return '—'
  }
}

/** Was aus Zielband und Toleranz gerade als Grenze wird — reine Vorschau. */
function planGrenze(
  metric: MetricPayload | undefined,
  key: string,
  toleranz: string,
  seite: 'min' | 'max',
): string {
  const grenze = seite === 'min' ? metric?.targetMin : metric?.targetMax
  if (grenze == null) return ''
  const tol = zahlOderNull(toleranz) ?? zahlOderNull(standardToleranz(key)) ?? 0
  const wert = seite === 'min' ? grenze - tol : grenze + tol
  return wert.toFixed(decimalsForMetric(key)).replace('.', ',')
}

/** Nur ein Fragment — die Zellen müssen direkte Grid-Kinder bleiben. */
function AlertRow({ children }: { children: React.ReactNode }) {
  return <>{children}</>
}

function triggerLabel(config: AutoMeasurementConfigDto): string {
  const delay = config.delayMinutes != null && config.delayMinutes > 0 ? `${config.delayMinutes} min nach ` : ''
  const kind = config.triggerKind === 'LightOnDelay' ? 'Licht-an' : config.triggerKind === 'LightOffDelay' ? 'Licht-aus' : 'manuell'
  return `${delay}${kind} · täglich`
}

function quietHoursLabel(settings: NotificationSettingsDto | null): string {
  if (!settings || settings.quietHoursStartHour == null || settings.quietHoursEndHour == null) return 'Keine Ruhezeit gesetzt'
  const pad = (value: number) => String(value).padStart(2, '0')
  return `Ruhezeit ${pad(settings.quietHoursStartHour)}:00 – ${pad(settings.quietHoursEndHour)}:00`
}

export default AlertsPage
