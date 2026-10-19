import { useState } from 'react'
import { apiFetch, formatApiError } from '../../api'
import FileInput from '../../components/FileInput'
import { V1Alert, V1Button } from '../../components/v1'
import { V1Sheet } from '../../components/V1Sheet'
import {
  pruefzeilen,
  sperrGruende,
  zurueckspielenFrei,
  type RestorePlan,
  type RestoreResult,
} from './sicherung-logik'

type Manifest = { fileName: string }

/**
 * Eine Sicherung hochladen, prüfen und zurückspielen — der Weg für den Umzug auf
 * eine neue Installation (Entwurf `sicherung-hochladen`, 19.10.2026).
 *
 * Drei Schritte in einem Blatt: Datei wählen (lädt hoch, ändert noch nichts),
 * Prüfung lesen, dann bewusst zurückspielen. Vor dem Zurückspielen legt der
 * Server selbst eine Sicherung des jetzigen Stands an; danach startet die App
 * neu.
 */
export function SicherungZurueckspielen({ open, onClose }: { open: boolean; onClose: () => void }) {
  const [dateiName, setDateiName] = useState<string | null>(null)
  const [plan, setPlan] = useState<RestorePlan | null>(null)
  const [ergebnis, setErgebnis] = useState<RestoreResult | null>(null)
  const [arbeitet, setArbeitet] = useState(false)
  const [fehler, setFehler] = useState<string | null>(null)

  function schliessen() {
    setDateiName(null)
    setPlan(null)
    setErgebnis(null)
    setFehler(null)
    onClose()
  }

  async function dateiGewaehlt(datei: File | null) {
    if (!datei) return
    setFehler(null)
    setPlan(null)
    setDateiName(datei.name)
    setArbeitet(true)
    try {
      const formular = new FormData()
      formular.append('File', datei)
      const manifest = await apiFetch<Manifest>('/api/system/backup/upload', { method: 'POST', body: formular })
      const pruefung = await apiFetch<RestorePlan>(
        `/api/system/backup/${encodeURIComponent(manifest.fileName)}/restore-plan`,
        { method: 'POST' },
      )
      setPlan(pruefung)
    } catch (caught) {
      setFehler(formatApiError(caught, 'Die Sicherung konnte nicht geprüft werden.'))
    } finally {
      setArbeitet(false)
    }
  }

  async function zurueckspielen() {
    if (!plan) return
    setFehler(null)
    setArbeitet(true)
    try {
      const antwort = await apiFetch<RestoreResult>(
        `/api/system/backup/${encodeURIComponent(plan.fileName)}/restore`,
        { method: 'POST' },
      )
      setErgebnis(antwort)
    } catch (caught) {
      setFehler(formatApiError(caught, 'Die Sicherung konnte nicht zurückgespielt werden.'))
    } finally {
      setArbeitet(false)
    }
  }

  const fuss = ergebnis ? (
    <V1Button variant="primary" onClick={() => window.location.reload()}>Seite neu laden</V1Button>
  ) : (
    <>
      <V1Button variant="ghost" onClick={schliessen} disabled={arbeitet}>Abbrechen</V1Button>
      <V1Button variant="danger" onClick={() => void zurueckspielen()} disabled={arbeitet || !zurueckspielenFrei(plan)} audit="sicherung-zurueckspielen">
        Jetzt zurückspielen
      </V1Button>
    </>
  )

  return (
    <V1Sheet
      open={open}
      onClose={schliessen}
      title="Sicherung zurückspielen"
      subtitle="Eine Sicherung hochladen, prüfen und den jetzigen Stand durch sie ersetzen."
      footer={fuss}
    >
      {ergebnis ? (
        <div data-audit="sicherung-ergebnis">
          <V1Alert
            tone="ok"
            title="Zurückgespielt."
            message={ergebnis.neustartGeplant
              ? 'Die App startet in wenigen Sekunden neu. Danach sind Grows, Pläne, Messwerte und Einstellungen aus der Sicherung da.'
              : 'Bitte starte die App neu, damit alles aus der Sicherung gilt.'}
          />
          <p className="v1-muted">Der Stand davor liegt als Sicherung bereit: <b>{ergebnis.safetyBackupFileName}</b></p>
          {ergebnis.warnings.map((warnung) => <p key={warnung} className="v1-muted">{warnung}</p>)}
        </div>
      ) : (
        <div data-audit="sicherung-wahl">
          <p className="v1-muted">
            Wähle die Sicherungsdatei (<b>grow-os-backup-….zip</b>), die du auf der alten Installation mit „Jetzt sichern“ heruntergeladen hast.
            Beim Auswählen wird sie hochgeladen und geprüft. Es wird noch nichts geändert.
          </p>
          <FileInput accept=".zip,application/zip" disabled={arbeitet} fileNames={dateiName ? [dateiName] : []} onFiles={(dateien) => void dateiGewaehlt(dateien[0] ?? null)} />
          {arbeitet && !plan && <p className="v1-muted">Wird hochgeladen und geprüft …</p>}
          {fehler && <V1Alert tone="critical" message={fehler} />}
          {plan && (
            <div data-audit="sicherung-pruefung">
              <ul className="st-pruefung">
                {pruefzeilen(plan).map((zeile) => (
                  <li key={zeile.text}>
                    <span>{zeile.text}</span>
                    <b className={zeile.gut ? 'is-gut' : 'is-schlecht'}>{zeile.wert}</b>
                  </li>
                ))}
                <li><span>Der jetzige Stand wird ersetzt</span><b className="is-schlecht">{plan.wouldOverwriteExistingDatabase ? 'ja' : 'nein'}</b></li>
              </ul>
              {sperrGruende(plan).map((grund) => <V1Alert key={grund} tone="critical" message={grund} />)}
              {zurueckspielenFrei(plan) && (
                <V1Alert tone="warn" message="Vor dem Zurückspielen sichert die App den jetzigen Stand automatisch. So kannst du zurück." />
              )}
            </div>
          )}
        </div>
      )}
    </V1Sheet>
  )
}
