import { useEffect, useMemo, useState } from 'react'
import { apiFetch, formatApiError } from '../api'
import type { GrowSummary } from '../types'
import FileInput from '../components/FileInput'
import { V1Alert, V1Badge, V1Button, V1Card, V1Empty, V1Field, V1Page, V1Section, V1Stat, V1Switch } from '../components/v1'

type ImportPlan = {
  exportValid?: boolean
  importSupported?: boolean
  wouldModifyDatabase?: boolean
  exportId?: string | null
  blockers?: string[]
  warnings?: string[]
  conflicts?: Array<{ severity?: string; message?: string }>
  plannedItems?: Array<{ section?: string; action?: string; count?: number; notes?: string | null }>
  source?: { growName?: string | null; tentName?: string | null; hydroSetupName?: string | null; exportedAtUtc?: string | null }
}

type ImportResult = {
  success?: boolean
  importedGrowId?: number
  importedGrowName?: string | null
  safetyBackupFileName?: string | null
  safetyBackupDownloadUrl?: string | null
  warnings?: string[]
}

function ReleasePage() {
  const [grows, setGrows] = useState<GrowSummary[]>([])
  const [selectedGrowId, setSelectedGrowId] = useState<number | null>(null)
  const [anonymize, setAnonymize] = useState(false)
  const [includePhotoMetadata, setIncludePhotoMetadata] = useState(true)
  const [importFileName, setImportFileName] = useState('')
  const [importText, setImportText] = useState('')
  const [plan, setPlan] = useState<ImportPlan | null>(null)
  const [result, setResult] = useState<ImportResult | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState<'load' | 'plan' | 'import' | null>('load')

  useEffect(() => {
    const controller = new AbortController()
    async function load() {
      setBusy('load')
      try {
        const loaded = await apiFetch<GrowSummary[]>('/api/grows?archived=false', { signal: controller.signal })
        if (controller.signal.aborted) return
        setGrows(loaded)
        setSelectedGrowId((current) => current ?? loaded[0]?.id ?? null)
      } catch (caught) {
        if (!controller.signal.aborted) setError(formatApiError(caught, 'Grows konnten nicht geladen werden.'))
      } finally {
        if (!controller.signal.aborted) setBusy(null)
      }
    }
    void load()
    return () => controller.abort()
  }, [])

  const selectedGrow = useMemo(() => grows.find((grow) => grow.id === selectedGrowId) ?? null, [grows, selectedGrowId])
  const exportUrl = selectedGrowId == null ? null : `/api/exports/grows/${selectedGrowId}?anonymize=${anonymize}&includePhotoMetadata=${includePhotoMetadata}`

  async function handleFile(file: File | null) {
    if (!file) return
    setError(null)
    setMessage(null)
    setPlan(null)
    setResult(null)
    const text = await file.text()
    setImportFileName(file.name)
    setImportText(text)
    setMessage(`${file.name} geladen.`)
  }

  function readExportJson() {
    try {
      return JSON.parse(importText) as Record<string, unknown>
    } catch {
      throw new Error('Export-JSON konnte nicht gelesen werden.')
    }
  }

  async function createImportPlan() {
    setBusy('plan')
    setError(null)
    setMessage(null)
    setPlan(null)
    setResult(null)
    try {
      const exportJson = readExportJson()
      const nextPlan = await apiFetch<ImportPlan>('/api/exports/grows/import-plan', { method: 'POST', body: JSON.stringify(exportJson) })
      setPlan(nextPlan)
      setMessage(nextPlan.blockers?.length ? 'Import-Plan erstellt, aber blockiert.' : 'Import-Plan erstellt.')
    } catch (caught) {
      setError(formatApiError(caught, 'Import-Plan konnte nicht erstellt werden.'))
    } finally {
      setBusy(null)
    }
  }

  async function executeImport() {
    setBusy('import')
    setError(null)
    setMessage(null)
    setResult(null)
    try {
      const exportJson = readExportJson()
      const importResult = await apiFetch<ImportResult>('/api/exports/grows/import', { method: 'POST', body: JSON.stringify(exportJson) })
      setResult(importResult)
      setMessage(importResult.success ? 'Import abgeschlossen.' : 'Import beendet, Ergebnis prüfen.')
    } catch (caught) {
      setError(formatApiError(caught, 'Import konnte nicht ausgeführt werden.'))
    } finally {
      setBusy(null)
    }
  }

  return (
    <V1Page eyebrow="Version" title="Version & Daten" subtitle="Export, Import-Plan und Import sind bewusst getrennt. Import schreibt erst nach Preflight und Safety-Backup.">
      {error && <V1Alert title="Fehler" message={error} tone="warn" />}
      {message && <V1Alert message={message} tone="ok" />}

      <section className="v1-kpi-grid">
        <V1Stat label="Grows" value={grows.length} hint="für Export verfügbar" />
        <V1Stat label="Import" value={plan ? (plan.blockers?.length ? 'blockiert' : 'bereit') : 'Plan'} hint="Prüfung vor dem Schreiben" />
        <V1Stat label="Sicherung" value="Backup" hint="Der Import legt vorher selbst eine Sicherung an" />
        <V1Stat label="Momentaufnahmen" value="Grow" hint="Zelt- und Hydro-Stand bleiben vergleichbar" />
      </section>

      <V1Section title="Grow exportieren">
        {busy === 'load' ? <V1Empty title="Lade Grows..." /> : grows.length === 0 ? <V1Empty title="Keine Grows vorhanden" text="Starte zuerst einen Grow, damit ein Export erzeugt werden kann." /> : (
          <div className="v1-card-grid v1-card-grid-compact">
            <V1Card>
              <span className="v1-card-kicker">Quelle</span>
              <h2>{selectedGrow?.name ?? 'Grow wählen'}</h2>
              <V1Field label="Grow">
                <select value={selectedGrowId ?? ''} onChange={(event) => setSelectedGrowId(Number(event.target.value))}>
                  {grows.map((grow) => <option key={grow.id} value={grow.id}>{grow.name} · {grow.tentName ?? 'ohne Zelt'}</option>)}
                </select>
              </V1Field>
              <V1Switch label="Anonymisieren" checked={anonymize} onChange={setAnonymize} hint="Name, Sorte, HA-Entitäten und freie Notizen werden reduziert." />
              <V1Switch label="Foto-Metadaten" checked={includePhotoMetadata} onChange={setIncludePhotoMetadata} hint="JSON enthält keine Bilddateien, nur Metadaten." />
              {exportUrl && <a className="v1-button is-primary" href={exportUrl} download>Export JSON öffnen</a>}
            </V1Card>
            <V1Card>
              <span className="v1-card-kicker">Export enthält</span>
              <h2>Vergleichsdaten</h2>
              <p>Grow, Zelt-Snapshot, Hydro-Snapshot, Messungen, Journal, Tasks, Addback, Changeouts, Harvest und optionale Foto-Metadaten.</p>
              <p>Für echte Backups bleiben Datenbank-Backups und App_Data-Dateien weiterhin separat wichtig.</p>
            </V1Card>
          </div>
        )}
      </V1Section>

      <V1Section title="Grow importieren">
        <div className="v1-card-grid v1-card-grid-compact">
          <V1Card>
            <span className="v1-card-kicker">1. Datei wählen</span>
            <h2>Export JSON</h2>
            <V1Field label="Datei">
              <FileInput accept="application/json,.json" fileNames={importFileName ? [importFileName] : []} onFiles={(files) => void handleFile(files[0] ?? null)} />
            </V1Field>
            <V1Field label="Oder JSON einfügen">
              <textarea value={importText} onChange={(event) => setImportText(event.target.value)} rows={8} placeholder="{ ... grow-os.grow-export.v1 ... }" />
            </V1Field>
            <div className="v1-action-row">
              <V1Button variant="primary" disabled={!importText.trim() || busy === 'plan'} onClick={() => void createImportPlan()}>{busy === 'plan' ? 'Prüft...' : 'Import-Plan erstellen'}</V1Button>
              <V1Button variant="danger" disabled={!plan || Boolean(plan.blockers?.length) || busy === 'import'} onClick={() => void executeImport()}>{busy === 'import' ? 'Importiert...' : 'Import ausführen'}</V1Button>
            </div>
          </V1Card>

          <V1Card tone={plan?.blockers?.length ? 'warn' : plan ? 'ok' : 'neutral'}>
            <span className="v1-card-kicker">2. Preflight</span>
            <h2>{plan ? (plan.blockers?.length ? 'Blockiert' : 'Import bereit') : 'Noch kein Plan'}</h2>
            {plan ? <ImportPlanView plan={plan} /> : <p>Erstelle zuerst einen Import-Plan. Dabei wird geprüft, ob ExportId, Schema, Hash, SectionCounts und Secrets passen.</p>}
          </V1Card>
        </div>

        {result && (
          <V1Card tone={result.success ? 'ok' : 'warn'}>
            <span className="v1-card-kicker">Import-Ergebnis</span>
            <h2>{result.importedGrowName ?? `Grow #${result.importedGrowId ?? 'neu'}`}</h2>
            <p>Safety-Backup: {result.safetyBackupFileName ?? 'offen'}</p>
            {result.safetyBackupDownloadUrl && <a className="v1-button is-secondary" href={result.safetyBackupDownloadUrl}>Safety-Backup herunterladen</a>}
            {(result.warnings ?? []).map((warning) => <p key={warning}>{warning}</p>)}
          </V1Card>
        )}
      </V1Section>

      <V1Section title="Backup / Restore Hinweis">
        <div className="v1-card-grid">
          <V1Card>
            <span className="v1-card-kicker">Backup</span>
            <h2>Datenbank + App_Data</h2>
            <p>Für einen vollständigen Systemstand reicht ein Grow-Export nicht. Sichere zusätzlich die SQLite-Datenbank und relevante App_Data-Dateien.</p>
          </V1Card>
          <V1Card>
            <span className="v1-card-kicker">Restore</span>
            <h2>Safety zuerst</h2>
            <p>Restore gehört nicht in operative Screens. Vor Importen erzeugt das Backend ein Safety-Backup. Vollständige Restore-UI bleibt ein kontrollierter Systemworkflow.</p>
          </V1Card>
          <V1Card>
            <span className="v1-card-kicker">Git</span>
            <h2>Keine Runtime-Daten</h2>
            <p>Keine Datenbank, HA-Tokens, Logs, App_Data oder generierten wwwroot-Artefakte committen.</p>
          </V1Card>
        </div>
      </V1Section>
    </V1Page>
  )
}

function ImportPlanView({ plan }: { plan: ImportPlan }) {
  return (
    <div style={{ display: 'grid', gap: 10 }}>
      <p>{plan.source?.growName ?? plan.exportId ?? 'Export'}</p>
      <div className="v1-action-row">
        <V1Badge tone={plan.exportValid ? 'ok' : 'warn'}>{plan.exportValid ? 'Export gültig' : 'Export prüfen'}</V1Badge>
        <V1Badge tone={plan.importSupported ? 'ok' : 'warn'}>{plan.importSupported ? 'Import unterstützt' : 'Import blockiert'}</V1Badge>
        <V1Badge tone={plan.wouldModifyDatabase ? 'warn' : 'neutral'}>{plan.wouldModifyDatabase ? 'würde schreiben' : 'Dry Run'}</V1Badge>
      </div>
      {(plan.blockers ?? []).map((blocker) => <V1Alert key={blocker} message={blocker} tone="warn" />)}
      {(plan.warnings ?? []).slice(0, 4).map((warning) => <p key={warning}>{warning}</p>)}
      {(plan.conflicts ?? []).slice(0, 4).map((conflict, index) => <p key={index}><strong>{conflict.severity ?? 'Konflikt'}:</strong> {conflict.message}</p>)}
      {(plan.plannedItems ?? []).length > 0 && (
        <div>
          <strong>Geplante Schritte</strong>
          {(plan.plannedItems ?? []).slice(0, 8).map((item, index) => <p key={index}>{item.section ?? 'item'} · {item.action ?? 'plan'} · {item.count ?? 0}</p>)}
        </div>
      )}
    </div>
  )
}


export default ReleasePage
