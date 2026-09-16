import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch } from '../api'
import type { NotificationSettingsDto } from '../types/notification'
import { V1Section, V1Card, V1Field, V1Switch, V1Button, V1Alert } from '../components/v1'

function errorMessage(caught: unknown, fallback: string): string {
  return caught instanceof Error ? caught.message : fallback
}

/** Der Watchdog-Status samt Puls je Zelt — „stale" heißt: seit >20 min nichts. */
type WatchdogStatus = {
  headline: string
  detail: string
  isProblem: boolean
  tents?: Array<{ name: string; minutesSinceData: number | null; stale: boolean }>
}

function pulseLabel(minutes: number | null): string {
  if (minutes == null) return 'noch nie Daten'
  if (minutes <= 1) return 'gerade eben'
  return `vor ${minutes} min`
}

function hourToInput(value: number | null): string {
  return value == null ? '' : String(value)
}

function parseHour(value: string): number | null {
  const trimmed = value.trim()
  if (trimmed === '') return null
  const parsed = Number(trimmed)
  return Number.isInteger(parsed) && parsed >= 0 && parsed <= 23 ? parsed : null
}

const DEFAULT_SETTINGS: NotificationSettingsDto = {
  notifyService: '',
  quietHoursStartHour: null,
  quietHoursEndHour: null,
  thresholds: true,
  calibration: true,
  maintenance: true,
  sensorOffline: true,
  risks: true,
  systemWatch: true,
  dailyDigest: false,
  digestHour: 6,
  digestMinute: 0,
  digestDetailed: false,
}

function pad2(value: number): string {
  return value.toString().padStart(2, '0')
}

function NotificationsPage() {
  const [settings, setSettings] = useState<NotificationSettingsDto>(DEFAULT_SETTINGS)
  const [notifyOptions, setNotifyOptions] = useState<string[]>([])
  const [quietStart, setQuietStart] = useState('')
  const [quietEnd, setQuietEnd] = useState('')
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [testing, setTesting] = useState(false)
  const [testingCal, setTestingCal] = useState(false)
  const [testingWatchdog, setTestingWatchdog] = useState(false)
  const [watchdog, setWatchdog] = useState<WatchdogStatus | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    async function load() {
      setLoading(true)
      try {
        const dto = await apiFetch<NotificationSettingsDto>('/api/notifications/settings', { signal: controller.signal })
        if (controller.signal.aborted) return
        setSettings({ ...dto, notifyService: dto.notifyService ?? '' })
        setQuietStart(hourToInput(dto.quietHoursStartHour))
        setQuietEnd(hourToInput(dto.quietHoursEndHour))
        const services = await apiFetch<string[]>('/api/notifications/notify-services', { signal: controller.signal }).catch(() => [])
        if (!controller.signal.aborted) setNotifyOptions(services)
        const status = await apiFetch<WatchdogStatus>('/api/notifications/watchdog', { signal: controller.signal }).catch(() => null)
        if (!controller.signal.aborted && status) setWatchdog(status)
      } catch (caught) {
        if (!controller.signal.aborted) setError(errorMessage(caught, 'Einstellungen konnten nicht geladen werden.'))
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }
    void load()
    return () => controller.abort()
  }, [])

  function patch(next: Partial<NotificationSettingsDto>) {
    setSettings((current) => ({ ...current, ...next }))
  }

  async function persist(): Promise<boolean> {
    const payload: NotificationSettingsDto = {
      ...settings,
      notifyService: settings.notifyService?.trim() || null,
      quietHoursStartHour: parseHour(quietStart),
      quietHoursEndHour: parseHour(quietEnd),
    }
    const saved = await apiFetch<NotificationSettingsDto>('/api/notifications/settings', { method: 'PUT', body: JSON.stringify(payload) })
    setSettings({ ...saved, notifyService: saved.notifyService ?? '' })
    return true
  }

  async function save() {
    setSaving(true)
    setError(null)
    setMessage(null)
    try {
      await persist()
      setMessage('Gespeichert.')
    } catch (caught) {
      setError(errorMessage(caught, 'Speichern fehlgeschlagen.'))
    } finally {
      setSaving(false)
    }
  }

  async function sendTest() {
    if ((settings.notifyService ?? '').trim() === '') {
      setError('Bitte zuerst ein Push-Handy wählen.')
      return
    }
    setTesting(true)
    setError(null)
    setMessage(null)
    try {
      // Save first, so testing the phone also stores it — otherwise a user who only
      // enters a phone and taps "Test" would leave without it ever being saved.
      await persist()
      const result = await apiFetch<{ ok: boolean }>('/api/notifications/test', {
        method: 'POST',
        body: JSON.stringify({ notifyService: settings.notifyService?.trim() }),
      })
      setMessage(result.ok ? 'Gespeichert. Test-Benachrichtigung gesendet — schau auf dein Handy.' : 'Gespeichert. Home Assistant hat die Test-Nachricht aber nicht angenommen — stimmt der Dienstname?')
    } catch (caught) {
      setError(errorMessage(caught, 'Test fehlgeschlagen.'))
    } finally {
      setTesting(false)
    }
  }

  async function testCalibration() {
    setTestingCal(true)
    setError(null)
    setMessage(null)
    try {
      await persist()
      const result = await apiFetch<{ ok: boolean; configured: boolean; categoryEnabled: boolean; message: string }>(
        '/api/notifications/test-calibration',
        { method: 'POST', body: JSON.stringify({}) },
      )
      if (result.configured && result.categoryEnabled) setMessage(result.message)
      else setError(result.message)
    } catch (caught) {
      setError(errorMessage(caught, 'Test der Kalibrierungs-Erinnerung fehlgeschlagen.'))
    } finally {
      setTestingCal(false)
    }
  }

  async function testWatchdog() {
    setTestingWatchdog(true)
    setError(null)
    setMessage(null)
    try {
      await persist()
      const result = await apiFetch<{ ok: boolean; message: string }>('/api/notifications/watchdog/test', { method: 'POST', body: JSON.stringify({}) })
      if (result.ok) setMessage(result.message)
      else setError(result.message)
      const status = await apiFetch<WatchdogStatus>('/api/notifications/watchdog').catch(() => null)
      if (status) setWatchdog(status)
    } catch (caught) {
      setError(errorMessage(caught, 'Systemtest fehlgeschlagen.'))
    } finally {
      setTestingWatchdog(false)
    }
  }

  if (loading) {
    return <V1Card>Lädt…</V1Card>
  }

  return (
    <>
      <div className="co-tabhead">
        <p className="gc-facts">Wähle einmal dein Handy, lege Ruhezeiten fest und entscheide, worüber Grow OS dich über Home Assistant erinnert.</p>
        <V1Button variant="primary" onClick={() => void save()} disabled={saving}>{saving ? 'Speichert…' : 'Speichern'}</V1Button>
      </div>
      {error && <V1Alert message={error} tone="critical" />}
      {message && <V1Alert message={message} tone="ok" />}

      <V1Section title="Dein Handy">
        <V1Card>
          <V1Field label="Push-Dienst" hint="Deine Home-Assistant-App am Handy (z. B. notify.mobile_app_pixel). Erscheint automatisch, wenn die App in HA angemeldet ist.">
            <input
              list="notify-services"
              value={settings.notifyService ?? ''}
              onChange={(event) => patch({ notifyService: event.target.value })}
              placeholder="notify.mobile_app_dein_handy"
            />
            <datalist id="notify-services">
              {notifyOptions.map((service) => <option key={service} value={service} />)}
            </datalist>
          </V1Field>
          <div style={{ marginTop: 12 }}>
            <V1Button variant="secondary" onClick={() => void sendTest()} disabled={testing}>
              {testing ? 'Sende…' : 'Test-Push senden'}
            </V1Button>
          </div>
        </V1Card>
      </V1Section>

      <V1Section title="Ruhezeiten">
        <V1Card>
          <p style={{ marginTop: 0, color: 'var(--ix-muted, #8a988e)', fontSize: 'var(--fs-text)' }}>In diesem Zeitfenster (Stunde 0–23) schickt Grow OS keine Push-Nachrichten. Beide Felder leer = immer erlaubt.</p>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
            <V1Field label="Von (Uhr)"><input inputMode="numeric" value={quietStart} onChange={(event) => setQuietStart(event.target.value)} placeholder="z. B. 22" /></V1Field>
            <V1Field label="Bis (Uhr)"><input inputMode="numeric" value={quietEnd} onChange={(event) => setQuietEnd(event.target.value)} placeholder="z. B. 7" /></V1Field>
          </div>
        </V1Card>
      </V1Section>

      <V1Section title="Täglicher Überblick">
        <V1Card>
          <V1Switch label="Täglicher Überblick" hint="Eine Push-Nachricht pro Tag: System läuft, hier die Werte." checked={settings.dailyDigest} onChange={(checked) => patch({ dailyDigest: checked })} />
          {settings.dailyDigest && (
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12, marginTop: 12 }}>
              <V1Field label="Uhrzeit">
                <input
                  type="time"
                  value={`${pad2(settings.digestHour)}:${pad2(settings.digestMinute)}`}
                  onChange={(event) => {
                    const [hour, minute] = event.target.value.split(':').map(Number)
                    if (Number.isInteger(hour) && Number.isInteger(minute)) patch({ digestHour: hour, digestMinute: minute })
                  }}
                />
              </V1Field>
              <V1Field label="Format">
                <select value={settings.digestDetailed ? 'detailed' : 'summary'} onChange={(event) => patch({ digestDetailed: event.target.value === 'detailed' })}>
                  <option value="summary">Kurz (Alles OK / Hinweise)</option>
                  <option value="detailed">Ausführlich (alle Werte)</option>
                </select>
              </V1Field>
            </div>
          )}
        </V1Card>
      </V1Section>

      <V1Section title="Wofür wirst du benachrichtigt?">
        <div style={{ display: 'grid', gap: 12 }}>
          <V1Card>
            <V1Switch label="Grenzwerte" hint="Wenn ein Messwert über oder unter deine Grenze läuft (pH, EC …)." checked={settings.thresholds} onChange={(checked) => patch({ thresholds: checked })} />
            {/* Fork AI (Schritt 4): Grenzen stellt man jetzt an der Werte-Karte ein. */}
            <p style={{ margin: '8px 0 0' }}><Link to="/zielwerte?tab=jetzt">Grenzen je Wert unter Zielwerte → Werte einstellen →</Link></p>
          </V1Card>
          <V1Card>
            <V1Switch label="Kalibrierung fällig" hint="Erinnerung, wenn eine Sensor-Kalibrierung ansteht." checked={settings.calibration} onChange={(checked) => patch({ calibration: checked })} />
            <div style={{ marginTop: 10 }}>
              <V1Button variant="secondary" onClick={() => void testCalibration()} disabled={testingCal}>
                {testingCal ? 'Testet…' : 'Test-Erinnerung senden'}
              </V1Button>
              <p className="rc2-measurement-note" style={{ margin: '8px 0 0' }}>Prüft jetzt die echte Kalibrier-Erinnerung und sagt dir, ob (und warum nicht) sie ankommt.</p>
            </div>
          </V1Card>
          <V1Card tone={watchdog?.isProblem ? 'warn' : 'neutral'}>
            <V1Switch label="Systemüberwachung" hint="Meldet, wenn die Überwachung selbst schweigt — keine neuen Messwerte, Home Assistant nicht erreichbar oder Grow OS steht. Dazu gehört auch der Pumpen-Wächter (Pumpe steht oder sollte geprüft werden, samt Entwarnung)." checked={settings.systemWatch} onChange={(checked) => patch({ systemWatch: checked })} />
            {watchdog && (
              <p className="rc2-measurement-note" style={{ margin: '10px 0 0' }}>
                <strong style={{ color: watchdog.isProblem ? 'var(--v1-text)' : 'var(--v1-green)' }}>{watchdog.headline}</strong> — {watchdog.detail}
              </p>
            )}
            {watchdog?.tents && watchdog.tents.length > 0 && (
              // Der Puls je Zelt: EIN Zelt kann dunkel sein, während die anderen
              // melden — die eine Gesamtzeile würde das verdecken.
              <p className="rc2-measurement-note" style={{ margin: '6px 0 0' }}>
                {watchdog.tents.map((tent, index) => (
                  <span key={tent.name}>
                    {index > 0 && ' · '}
                    {tent.name}:{' '}
                    <strong style={{ color: tent.stale ? 'var(--v1-warn)' : 'var(--v1-green)' }}>{pulseLabel(tent.minutesSinceData)}</strong>
                  </span>
                ))}
              </p>
            )}
            <div style={{ marginTop: 10 }}>
              <V1Button variant="secondary" onClick={() => void testWatchdog()} disabled={testingWatchdog}>
                {testingWatchdog ? 'Testet…' : 'Systemtest senden'}
              </V1Button>
            </div>
          </V1Card>
          <V1Card>
            <V1Switch label="Sensor ausgefallen" hint="Wenn ein gemappter Sensor keine Werte mehr liefert." checked={settings.sensorOffline} onChange={(checked) => patch({ sensorOffline: checked })} />
          </V1Card>
          {/* Fork AI (F-014): diese Kategorie sendete immer — ohne Schalter und ohne
              dass die Seite sie nannte. Zwei Absender teilen sie sich. */}
          <V1Card>
            <V1Switch
              label="Trends & Risiken"
              hint="Wenn ein Wert über Tage wegdriftet, obwohl er noch im Band liegt (Trendwächter), und wenn in der Dunkelphase Licht ins Zelt fällt (Licht-Wächter)."
              checked={settings.risks}
              onChange={(checked) => patch({ risks: checked })}
            />
            <p className="rc2-measurement-note" style={{ margin: '8px 0 0' }}>
              Licht in der Dunkelphase kommt auch während der Ruhezeit — nachts kann die Meldung die Blüte noch retten.
            </p>
          </V1Card>
        </div>
      </V1Section>
    </>
  )
}

export default NotificationsPage
