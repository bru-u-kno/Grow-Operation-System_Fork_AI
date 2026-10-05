import { useState, type FormEvent, type KeyboardEvent } from 'react'
import { apiFetch, formatApiError } from '../../api'
import { V1Button } from '../../components/v1'
import type { JournalEntryType, TagebuchNotizDto } from '../../types'
import { EintragFelderFormular } from '../grow-detail/JournalStreamSection'
import { MEILENSTEIN_HINWEIS, aenderungsAnfrage, alsFelder, eintragFehler, istMeilenstein, type EintragFelder } from '../grow-detail/journal-bearbeiten'

/**
 * Einen Journaleintrag im Tagebuch korrigieren — dasselbe Formular und dieselben
 * Regeln wie im Journal (forkai.170): Meilensteine behalten ihr Datum, ein
 * unveränderter Zeitpunkt geht nicht mit.
 */
export function NotizBearbeiten({ notiz, onFertig, onAbbrechen }: {
  notiz: TagebuchNotizDto
  onFertig: () => void | Promise<void>
  onAbbrechen: () => void
}) {
  const [ausgang] = useState<EintragFelder>(() => alsFelder({
    title: notiz.titel,
    body: notiz.text,
    entryType: notiz.entryType as JournalEntryType,
    occurredAtUtc: notiz.occurredAtUtc,
  }))
  const [felder, setFelder] = useState<EintragFelder>(ausgang)
  const [speichert, setSpeichert] = useState(false)
  const [fehler, setFehler] = useState<string | null>(null)

  async function speichern(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const leer = eintragFehler(felder)
    if (leer) { setFehler(leer); return }
    setSpeichert(true)
    setFehler(null)
    try {
      await apiFetch(`/api/journal/${notiz.id}`, { method: 'PUT', body: JSON.stringify(aenderungsAnfrage(felder, ausgang)) })
      await onFertig()
    } catch (caught) {
      setFehler(formatApiError(caught, 'Eintrag konnte nicht gespeichert werden.'))
    } finally {
      setSpeichert(false)
    }
  }

  function escape(event: KeyboardEvent<HTMLFormElement>) {
    if (event.key === 'Escape') {
      event.preventDefault()
      onAbbrechen()
    }
  }

  return (
    <form
      className="js-form js-bearbeitung"
      data-audit="tagebuch-notiz-bearbeiten"
      aria-label={`Eintrag „${notiz.titel ?? ''}" bearbeiten`}
      onSubmit={(event) => void speichern(event)}
      onKeyDown={escape}
    >
      <EintragFelderFormular
        werte={felder}
        autoFocusTitel
        zeitpunktGesperrt={istMeilenstein(felder.entryType)}
        onChange={(patch) => setFelder((alt) => ({ ...alt, ...patch }))}
      />
      {istMeilenstein(felder.entryType) && <p className="js-hinweis">{MEILENSTEIN_HINWEIS}</p>}
      {fehler && <p className="js-fehler" role="alert">{fehler}</p>}
      <div className="js-knopfreihe">
        <V1Button type="submit" variant="primary" audit="tagebuch-notiz-bearbeiten-speichern" disabled={speichert}>
          {speichert ? 'Speichert…' : 'Änderungen speichern'}
        </V1Button>
        <V1Button onClick={onAbbrechen} disabled={speichert}>Abbrechen</V1Button>
      </div>
    </form>
  )
}
