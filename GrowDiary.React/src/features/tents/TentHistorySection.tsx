import { useEffect, useState } from 'react'
import { apiFetch } from '../../api'
import { SensorChart, type TentHistory } from '../../components/SensorChart'
import { V1Section, V1Card, V1Alert } from '../../components/v1'

// The metrics worth charting, in the order a grower reads them.
const METRICS = ['reservoir-ph', 'reservoir-ec', 'reservoir-temp', 'temperature', 'humidity', 'vpd'] as const
const RANGES = [
  { days: 7, label: '7 Tage' },
  { days: 14, label: '14 Tage' },
  { days: 30, label: '30 Tage' },
] as const

type AlertRule = { metricKey: string; minValue: number | null; maxValue: number | null; enabled: boolean }

/**
 * Shows the sensor history Cultivate Control records anyway — one curve per metric, with the tent's
 * own threshold range drawn behind it so "am I inside my limits?" is answered at a glance.
 */
export function TentHistorySection({ tentId }: { tentId: number }) {
  const [history, setHistory] = useState<TentHistory | null>(null)
  const [targets, setTargets] = useState<Map<string, { min: number | null; max: number | null }>>(new Map())
  const [days, setDays] = useState<number>(14)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    async function load() {
      setLoading(true)
      setError(null)
      try {
        const [data, rules] = await Promise.all([
          apiFetch<TentHistory>(`/api/tents/${tentId}/history?metrics=${METRICS.join(',')}&days=${days}`, { signal: controller.signal }),
          apiFetch<{ rules: AlertRule[] }>(`/api/alerts/tents/${tentId}`, { signal: controller.signal }).catch(() => ({ rules: [] })),
        ])
        if (controller.signal.aborted) return
        setHistory(data)
        setTargets(new Map(rules.rules.filter((rule) => rule.enabled).map((rule) => [rule.metricKey, { min: rule.minValue, max: rule.maxValue }])))
      } catch (caught) {
        if (!controller.signal.aborted) setError(caught instanceof Error ? caught.message : 'Verlauf konnte nicht geladen werden.')
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }
    void load()
    return () => controller.abort()
  }, [tentId, days])

  const withData = (history?.series ?? []).filter((series) => series.points.length > 0)

  return (
    <V1Section
      title="Verlauf"
      action={(
        <div className="v1-tabs" role="tablist" aria-label="Zeitraum">
          {RANGES.map((range) => (
            <button
              key={range.days}
              type="button"
              role="tab"
              aria-selected={range.days === days}
              className={range.days === days ? 'v1-tab is-active' : 'v1-tab'}
              onClick={() => setDays(range.days)}
            >
              {range.label}
            </button>
          ))}
        </div>
      )}
    >
      {error && <V1Alert message={error} tone="warn" />}
      {loading ? (
        <V1Card>Lädt Verlauf…</V1Card>
      ) : withData.length === 0 ? (
        <V1Card>
          <span className="v1-card-kicker">Noch nichts aufgezeichnet</span>
          <h2>Der Verlauf füllt sich von selbst</h2>
          <p>Cultivate Control speichert alle 5 Minuten die Werte deiner zugeordneten Sensoren und verdichtet sie nachts zu Tageswerten. Ab dem ersten vollen Tag erscheinen hier Kurven.</p>
        </V1Card>
      ) : (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 300px), 1fr))', gap: 12 }}>
          {withData.map((series) => (
            <V1Card key={series.metricKey}>
              {/* Die Aufloesung steht im Verlauf selbst: bei Tageswerten braucht der
                  angetippte Punkt keine Uhrzeit, sonst stuende unter jedem
                  Punkt „00:00". */}
              <SensorChart
                series={series}
                target={targets.get(series.metricKey)}
                resolution={history?.resolution}
              />
            </V1Card>
          ))}
        </div>
      )}
    </V1Section>
  )
}
