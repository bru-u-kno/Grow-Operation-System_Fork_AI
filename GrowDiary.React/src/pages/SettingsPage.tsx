import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch, formatApiError } from '../api'
import type { GrowSummary, SettingsOverviewDto } from '../types'
import FileInput from '../components/FileInput'
import { useTheme } from '../useTheme'
import { useNavBar } from '../useNavBar'
import { V1Alert, V1Page, V1Skeleton } from '../components/v1'
import { unlesbarMeldung, unlesbareFelder, zahlOderNull } from '../zahlenfeld'

type ImportPreview = { ok: boolean; title: string; details: string[] }
type BackupManifest = { fileName?: string; downloadUrl?: string }
type BackendHealth = { appName: string; backendSchema: string }

/**
 * Einstellungen nach dem Entwurf: drei Panels — Darstellung, Daten, System.
 * Hinter jedem Knopf steckt die vorhandene Funktion (Vollbackup, JSON-Exporte,
 * Import-Prüfung); neu ist nur die Anordnung.
 */
function SettingsPage() {
  const { theme, toggle } = useTheme()
  const { dashboardPath, saveDashboardPath } = useNavBar()
  // Eigener Zustand fuers Feld: waehrend des Tippens darf der gespeicherte
  // Wert nicht zurueckspringen.
  const [haZiel, setHaZiel] = useState('')
  const [haZielSaving, setHaZielSaving] = useState(false)
  useEffect(() => { setHaZiel(dashboardPath) }, [dashboardPath])
  const saveHaZiel = async () => {
    setHaZielSaving(true)
    try { await saveDashboardPath(haZiel.trim()) } finally { setHaZielSaving(false) }
  }
  const [settings, setSettings] = useState<SettingsOverviewDto | null>(null)
  const [grows, setGrows] = useState<GrowSummary[]>([])
  const [health, setHealth] = useState<BackendHealth | null>(null)
  const [importFileName, setImportFileName] = useState('')
  const [strompreis, setStrompreis] = useState('')
  const [begleitung, setBegleitung] = useState<'full' | 'important' | 'expert'>('full')
  const [strompreisSaving, setStrompreisSaving] = useState(false)
  const [pumpSchonfrist, setPumpSchonfrist] = useState('')
  const [pumpSaving, setPumpSaving] = useState(false)
  const [importText, setImportText] = useState('')
  const [preview, setPreview] = useState<ImportPreview | null>(null)
  const [loading, setLoading] = useState(true)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    async function load() {
      setLoading(true)
      setError(null)
      try {
        const [overview, activeGrows, backendHealth] = await Promise.all([
          apiFetch<SettingsOverviewDto>('/api/settings', { signal: controller.signal }),
          apiFetch<GrowSummary[]>('/api/grows?archived=false', { signal: controller.signal }),
          apiFetch<BackendHealth>('/api/system/backend-health', { signal: controller.signal }).catch(() => null),
        ])
        const kosten = await apiFetch<{ strompreisCentProKwh: number | null }>('/api/costs/settings', { signal: controller.signal }).catch(() => null)
        if (!controller.signal.aborted && kosten?.strompreisCentProKwh != null) {
          setStrompreis(String(kosten.strompreisCentProKwh).replace('.', ','))
        }
        const companion = await apiFetch<{ level: 'full' | 'important' | 'expert' }>('/api/companion/settings', { signal: controller.signal }).catch(() => null)
        if (!controller.signal.aborted && companion) setBegleitung(companion.level)
        const pumpe = await apiFetch<{ minutes: number }>('/api/pump-watch/grace', { signal: controller.signal }).catch(() => null)
        if (!controller.signal.aborted && pumpe) setPumpSchonfrist(String(pumpe.minutes))
        if (controller.signal.aborted) return
        setSettings(overview)
        setGrows(activeGrows)
        setHealth(backendHealth)
      } catch (caught) {
        if (!controller.signal.aborted) setError(formatApiError(caught, 'Einstellungen konnten nicht geladen werden.'))
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }
    void load()
    return () => controller.abort()
  }, [])

  async function createFullBackup() {
    setError(null)
    setMessage(null)
    try {
      const initialResponse = await fetch('/api/system/backup', { method: 'POST' })
      if (!initialResponse.ok) throw new Error(`Backup konnte nicht erstellt werden (${initialResponse.status})`)

      const initialContentType = initialResponse.headers.get('content-type') ?? ''
      if (!initialContentType.includes('application/json')) {
        const blob = await initialResponse.blob()
        downloadBlob(getFileNameFromDisposition(initialResponse.headers.get('content-disposition')) ?? defaultBackupFileName(), blob)
        setMessage('Vollbackup wurde erstellt und heruntergeladen.')
        return
      }

      const manifest = await initialResponse.json() as BackupManifest
      if (!manifest.downloadUrl) throw new Error('Backup wurde erstellt, aber es fehlt die Download-URL.')

      const response = await fetch(manifest.downloadUrl)
      if (!response.ok) throw new Error(`Backup-Download fehlgeschlagen (${response.status})`)

      const blob = await response.blob()
      downloadBlob(getFileNameFromDisposition(response.headers.get('content-disposition')) ?? manifest.fileName ?? defaultBackupFileName(), blob)
      setMessage('Vollbackup wurde erstellt und heruntergeladen.')
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Vollbackup konnte nicht erstellt werden.')
    }
  }

  function exportGrowIndex() {
    downloadJson(`grow-os-grows-${new Date().toISOString().slice(0, 10)}.json`, {
      schema: 'grow-os.grow-index.v1',
      exportedAtUtc: new Date().toISOString(),
      grows,
    })
    setMessage('Grow-Index exportiert.')
  }

  /** System-Index: Konfiguration + HA-Mapping in einer Datei — der Diagnose-Export. */
  function exportSystemIndex() {
    if (!settings) return
    downloadJson(`grow-os-system-index-${new Date().toISOString().slice(0, 10)}.json`, {
      schema: 'grow-os.system-config.v1',
      exportedAtUtc: new Date().toISOString(),
      homeAssistant: { enabled: settings.homeAssistant.enabled, baseUrl: settings.homeAssistant.baseUrl, tokenStored: Boolean(settings.homeAssistant.accessToken) },
      tents: settings.tents,
      haMapping: settings.tents.map((tent) => ({
        name: tent.name,
        cameras: tent.cameras,
        sensors: tent.sensors.filter((sensor) => sensor.isActive || sensor.haEntityId),
      })),
    })
    setMessage('System-Index exportiert.')
  }

  async function handleFile(file: File | null) {
    setPreview(null)
    setImportText('')
    setImportFileName('')
    if (!file) return
    setImportFileName(file.name)
    const text = await file.text()
    setImportText(text)
    inspectImport(text, file.name)
  }

  async function saveBegleitung(level: 'full' | 'important' | 'expert') {
    setBegleitung(level)
    try {
      await apiFetch('/api/companion/settings', { method: 'PUT', body: JSON.stringify({ level }) })
      setMessage(level === 'expert'
        ? 'Expertenmodus: keine unaufgeforderten Erinnerungen mehr — nur deine eigenen Alarme.'
        : level === 'important'
          ? 'Nur Wichtiges: Erinnerungen kommen erst, wenn etwas kritisch überfällig ist.'
          : 'Volle Begleitung: die App erinnert an alles, was ihre Abläufe kennen.')
    } catch (caught) {
      setError(formatApiError(caught, 'Begleitungsstufe konnte nicht gespeichert werden.'))
    }
  }

  async function savePumpSchonfrist() {
    setPumpSaving(true)
    setError(null)
    setMessage(null)
    try {
      // Aus „20x" wurden vorher stillschweigend 15 Minuten — der Nutzer sah
      // eine Erfolgsmeldung mit einer Zahl, die er nie eingegeben hat. Jetzt
      // sagt die App, dass sie es nicht lesen kann.
      const meldung = unlesbarMeldung(unlesbareFelder([[pumpSchonfrist, 'Schonfrist']]))
      if (meldung) { setError(meldung); setPumpSaving(false); return }

      const minutes = zahlOderNull(pumpSchonfrist)
      const gespeichert = await apiFetch<{ minutes: number }>('/api/pump-watch/grace', {
        method: 'PUT',
        body: JSON.stringify({ minutes: minutes == null ? 15 : Math.round(minutes) }),
      })
      setPumpSchonfrist(String(gespeichert.minutes))
      setMessage(`Schonfrist gespeichert: erst nach ${gespeichert.minutes} Minuten Stillstand wird gewarnt.`)
    } catch (caught) {
      setError(formatApiError(caught, 'Schonfrist konnte nicht gespeichert werden.'))
    } finally {
      setPumpSaving(false)
    }
  }

  async function saveStrompreis() {
    setStrompreisSaving(true)
    setError(null)
    setMessage(null)
    try {
      // Aus „32,5x" wurde still `null`: der Strompreis war weg, das Archiv
      // rechnete ohne ihn, und die Meldung sagte „gespeichert".
      const meldung = unlesbarMeldung(unlesbareFelder([[strompreis, 'Strompreis']]))
      if (meldung) { setError(meldung); setStrompreisSaving(false); return }

      const wert = zahlOderNull(strompreis)
      await apiFetch('/api/costs/settings', {
        method: 'PUT',
        body: JSON.stringify({ strompreisCentProKwh: wert }),
      })
      setMessage('Strompreis gespeichert — das Archiv rechnet damit die Kosten je Grow.')
    } catch (caught) {
      setError(formatApiError(caught, 'Strompreis konnte nicht gespeichert werden.'))
    } finally {
      setStrompreisSaving(false)
    }
  }

  function inspectImport(text = importText, name = importFileName) {
    try {
      const parsed = JSON.parse(text) as Record<string, unknown>
      const schema = typeof parsed.schema === 'string' ? parsed.schema : 'unbekannt'
      setPreview({
        ok: true,
        title: schema,
        details: [
          `Datei: ${name || 'unbekannt'}`,
          Array.isArray(parsed.tents) ? `${parsed.tents.length} Zelt-/Mapping-Einträge` : 'keine Zeltliste',
          Array.isArray(parsed.grows) ? `${parsed.grows.length} Grow-Einträge` : 'keine Growliste',
          'Schreibender Import wird erst nach expliziter Restore-Bestätigung aktiviert.',
        ],
      })
    } catch {
      setPreview({ ok: false, title: 'Ungültiges JSON', details: [`Datei: ${name || 'unbekannt'}`, 'Syntaxprüfung fehlgeschlagen.'] })
    }
  }

  return (
    <V1Page eyebrow="System" title="Einstellungen">
      {error && <V1Alert title="Fehler" message={error} tone="warn" />}
      {message && <V1Alert message={message} tone="ok" />}

      {loading ? <V1Skeleton rows={4} label="Lade Einstellungen" /> : (
        <div className="co-grid is-300" data-audit="settings-panels">
          <section className="ls-panel" data-audit="settings-appearance">
            <div className="ls-panel-head"><span className="ls-label">Darstellung</span></div>
            <div className="co-row">
              <span className="co-row-text">Theme</span>
              <div className="co-row-end st-theme" role="group" aria-label="Theme">
                <button type="button" className={`ls-btn is-small${theme === 'dark' ? ' is-primary' : ''}`} onClick={() => theme !== 'dark' && toggle()}>Dunkel</button>
                <button type="button" className={`ls-btn is-small${theme === 'light' ? ' is-primary' : ''}`} onClick={() => theme !== 'light' && toggle()}>Hell</button>
              </div>
            </div>
            <div className="co-row">
              <span className="co-row-text">Sprache</span>
              <span className="co-row-value">Deutsch</span>
            </div>
            <div className="co-row">
              <div style={{ minWidth: 0 }}>
                <div className="co-row-title">Begleitung</div>
                <div className="co-row-sub">Wie eng die App erinnert — Experten bekommen nur ihre eigenen Alarme</div>
              </div>
              <div className="co-row-end st-theme" role="group" aria-label="Begleitungsstufe">
                <button type="button" className={`ls-btn is-small${begleitung === 'full' ? ' is-primary' : ''}`} onClick={() => void saveBegleitung('full')}>Voll</button>
                <button type="button" className={`ls-btn is-small${begleitung === 'important' ? ' is-primary' : ''}`} onClick={() => void saveBegleitung('important')}>Wichtiges</button>
                <button type="button" className={`ls-btn is-small${begleitung === 'expert' ? ' is-primary' : ''}`} onClick={() => void saveBegleitung('expert')}>Experte</button>
              </div>
            </div>
            {/* Strompreis und Schonfrist stehen hier, nicht bei „Daten":
                dort war die Spalte 705 px hoch, waehrend die beiden anderen
                bei 267 und 264 px endeten und zur Haelfte leer aussahen. */}
            <div className="co-row">
              <div style={{ minWidth: 0 }}>
                <div className="co-row-title">Strompreis</div>
                <div className="co-row-sub">ct/kWh — für die berechneten Kosten je Grow im Archiv</div>
              </div>
              <div className="co-row-end st-strompreis">
                <input inputMode="decimal" value={strompreis} onChange={(event) => setStrompreis(event.target.value)} placeholder="z. B. 32,5" aria-label="Strompreis in Cent je kWh" style={{ width: 90 }} />
                <button type="button" className="ls-btn is-small" disabled={strompreisSaving} onClick={() => void saveStrompreis()}>{strompreisSaving ? 'Speichert…' : 'Speichern'}</button>
              </div>
            </div>
            <div className="co-row">
              <div style={{ minWidth: 0 }}>
                <div className="co-row-title">Pumpen-Schonfrist</div>
                <div className="co-row-sub">Minuten Stillstand, bevor gewarnt wird. Wer die Umwälzung im Intervall fährt, stellt sie höher. Faustregel: 15.</div>
              </div>
              <div className="co-row-end st-strompreis">
                <input inputMode="numeric" value={pumpSchonfrist} onChange={(event) => setPumpSchonfrist(event.target.value)} placeholder="15" aria-label="Pumpen-Schonfrist in Minuten" style={{ width: 90 }} />
                <button type="button" className="ls-btn is-small" disabled={pumpSaving} onClick={() => void savePumpSchonfrist()}>{pumpSaving ? 'Speichert…' : 'Speichern'}</button>
              </div>
            </div>
            {/* Fork AI: Ziel des Haus-Zeichens in der Titelzeile. Steht bei
                „Darstellung", weil es die Titelzeile betrifft — und weil es
                jeder anders braucht: das Lieblings-Dashboard heisst nirgends
                gleich. */}
            <div className="co-row">
              <div style={{ minWidth: 0 }}>
                <div className="co-row-title">Zurück zu Home Assistant</div>
                <div className="co-row-sub">Wohin das ⌂ in der Titelzeile springt, z. B. /dashboard-grow/0</div>
              </div>
              <div className="co-row-end st-strompreis">
                <input
                  value={haZiel}
                  onChange={(event) => setHaZiel(event.target.value)}
                  placeholder="/lovelace/0"
                  aria-label="Ziel des Rücksprungs nach Home Assistant"
                  style={{ width: 160 }}
                />
                <button type="button" className="ls-btn is-small" disabled={haZielSaving} onClick={() => void saveHaZiel()}>
                  {haZielSaving ? 'Speichert…' : 'Speichern'}
                </button>
              </div>
            </div>
          </section>

          <section className="ls-panel" data-audit="settings-data">
            <div className="ls-panel-head"><span className="ls-label">Daten</span></div>
            <div className="co-row">
              <div style={{ minWidth: 0 }}>
                <div className="co-row-title">Backup</div>
                <div className="co-row-sub">läuft über Home-Assistant-Backups mit</div>
              </div>
              <div className="co-row-end"><button type="button" className="ls-btn is-small" onClick={() => void createFullBackup()}>Jetzt sichern</button></div>
            </div>
            <div className="co-row">
              <div style={{ minWidth: 0 }}>
                <div className="co-row-title">Grow exportieren / importieren</div>
                <div className="co-row-sub">Grow-Index als JSON; Import unten prüfen</div>
              </div>
              <div className="co-row-end"><button type="button" className="ls-btn is-small" onClick={exportGrowIndex}>Export</button></div>
            </div>
            <div className="co-row">
              <div style={{ minWidth: 0 }}>
                <div className="co-row-title">System-Index</div>
                <div className="co-row-sub">Diagnose-Export für Fehlerberichte</div>
              </div>
              <div className="co-row-end"><button type="button" className="ls-btn is-small" onClick={exportSystemIndex}>Erzeugen</button></div>
            </div>
            <div className="co-row st-import">
              <div style={{ minWidth: 0, flex: 1 }}>
                <div className="co-row-title">Import prüfen</div>
                <div className="co-row-sub" style={{ marginBottom: 8 }}>Erst Datei wählen, dann Syntax und Schema prüfen — kein manuelles JSON-Feld.</div>
                <FileInput accept="application/json,.json" fileNames={importFileName ? [importFileName] : []} onFiles={(files) => void handleFile(files[0] ?? null)} />
                {preview && (
                  <div className={`st-preview${preview.ok ? '' : ' is-bad'}`} data-audit="settings-import-preview">
                    <strong>{preview.title}</strong>
                    {preview.details.map((detail) => <span key={detail}>{detail}</span>)}
                  </div>
                )}
              </div>
            </div>
          </section>

          <section className="ls-panel" data-audit="settings-system">
            <div className="ls-panel-head"><span className="ls-label">System</span></div>
            <div className="co-row">
              <span className="co-row-text">Version</span>
              <span className="co-row-value">{health ? `${health.appName} · Add-on` : 'Grow OS · Add-on'}</span>
            </div>
            {health && (
              <div className="co-row">
                <span className="co-row-text">Backend-Schema</span>
                <span className="co-row-value">{health.backendSchema}</span>
              </div>
            )}
            <div className="co-row">
              <span className="co-row-text">Erste Schritte erneut zeigen</span>
              <div className="co-row-end"><Link className="ls-btn is-small" to="/start">Onboarding</Link><Link className="ls-btn is-small" to="/release">Release &amp; Daten</Link></div>
            </div>
            <div className="co-row">
              <span className="co-row-text">Zelte / Grows</span>
              <span className="co-row-value">{settings?.tents.length ?? 0} · {grows.length}</span>
            </div>
          </section>
        </div>
      )}
    </V1Page>
  )
}

function downloadJson(fileName: string, value: unknown) {
  const blob = new Blob([JSON.stringify(value, null, 2)], { type: 'application/json' })
  downloadBlob(fileName, blob)
}

function downloadBlob(fileName: string, blob: Blob) {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}

function defaultBackupFileName() {
  return `grow-os-backup-${new Date().toISOString().slice(0, 10)}.zip`
}

function getFileNameFromDisposition(value: string | null) {
  if (!value) return null
  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(value)
  if (encoded) return decodeURIComponent(encoded[1].replace(/"/g, ''))
  const plain = /filename="?([^";]+)"?/i.exec(value)
  return plain?.[1] ?? null
}


export default SettingsPage
