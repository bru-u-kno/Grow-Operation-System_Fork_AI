import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { apiFetch, formatApiError } from '../../api'
import { V1Button } from '../../components/v1'
import type { JournalEntryType, TagebuchSprungDto } from '../../types'
import { EintragFelderFormular } from '../grow-detail/JournalStreamSection'
import { eintragFehler, type EintragFelder } from '../grow-detail/journal-bearbeiten'
import { nachfuellenVorbelegung, nachfuellenWeg } from './nachfuellen-weg'
import { sprungSatz } from './tagebuch-modell'

type Modus = 'notiz' | null

/**
 * Die drei Antworten auf „Nachgefüllt?" an einer Auffälligkeit.
 *
 * - **Nachfüllen eintragen**: führt in den Nachfüll-Ablauf (`/addback`,
 *   ein Vorgang), vorbelegt mit Zeitpunkt und den Werten davor/danach.
 * - **Notiz dazu**: ein Journaleintrag, vorbelegt — dasselbe Formular wie im
 *   Journal (`EintragFelderFormular`).
 * - **War nichts**: blendet die Zeile aus; gemerkt auf dem Server, und bis zum
 *   Neuladen lässt es sich zurücknehmen.
 *
 * Beide Einträge erklären den Sprung — nach dem Speichern verschwindet die
 * Zeile, und an ihrer Stelle steht der neue Eintrag.
 */
export function AuffaelligAktionen({ growId, befunde, onGespeichert }: {
  growId: string
  befunde: TagebuchSprungDto[]
  onGespeichert: () => void | Promise<void>
}) {
  const navigate = useNavigate()
  const [modus, setModus] = useState<Modus>(null)
  const [verworfen, setVerworfen] = useState(false)
  const [laeuft, setLaeuft] = useState(false)
  const [fehler, setFehler] = useState<string | null>(null)

  async function verwerfen(ja: boolean) {
    setLaeuft(true)
    setFehler(null)
    try {
      for (const b of befunde) {
        await apiFetch(`/api/tagebuch/auffaelligkeiten/${b.id}`, { method: 'PUT', body: JSON.stringify({ verworfen: ja }) })
      }
      setVerworfen(ja)
    } catch (caught) {
      setFehler(formatApiError(caught, 'Konnte nicht gespeichert werden.'))
    } finally {
      setLaeuft(false)
    }
  }

  /** Der Weg kommt aus `nachfuellen-weg.ts` — dort, und nur dort, wird er umgestellt. */
  function nachfuellenOeffnen() {
    navigate(nachfuellenWeg(growId, nachfuellenVorbelegung(befunde)).to)
  }

  if (verworfen) {
    return (
      <p className="tb-verworfen" role="status">
        Ausgeblendet — beim nächsten Laden steht die Zeile nicht mehr hier.{' '}
        <button type="button" className="tb-link" disabled={laeuft} onClick={() => void verwerfen(false)} data-audit="tagebuch-war-nichts-zurueck">
          Rückgängig
        </button>
      </p>
    )
  }

  return (
    <>
      <div className="tb-knoepfe">
        <V1Button className="tb-klein" audit="tagebuch-nachfuellen" disabled={laeuft} onClick={nachfuellenOeffnen}>
          Nachfüllen eintragen
        </V1Button>
        <V1Button className="tb-klein" audit="tagebuch-notiz-dazu" disabled={laeuft} onClick={() => setModus(modus === 'notiz' ? null : 'notiz')}>
          Notiz dazu
        </V1Button>
        <V1Button variant="ghost" className="tb-klein" audit="tagebuch-war-nichts" disabled={laeuft} onClick={() => void verwerfen(true)}>
          War nichts
        </V1Button>
      </div>
      {fehler && <p className="tb-fehler" role="alert">{fehler}</p>}
      {modus === 'notiz' && (
        <NotizFormular growId={growId} befunde={befunde} onAbbrechen={() => setModus(null)} onGespeichert={onGespeichert} />
      )}
    </>
  )
}

function NotizFormular({ growId, befunde, onAbbrechen, onGespeichert }: {
  growId: string
  befunde: TagebuchSprungDto[]
  onAbbrechen: () => void
  onGespeichert: () => void | Promise<void>
}) {
  const erster = befunde[0]
  const [felder, setFelder] = useState<EintragFelder>({
    entryType: 'Observation',
    title: `${erster.name}-Sprung ${erster.beginnUhrzeit}–${erster.endeUhrzeit} Uhr`,
    // Ortszeit der Anlage vom Server: das Journal liest `occurredAtLocal` als
    // solche. Aus dem Browser gerechnet stand bei anderer Zeitzone die falsche
    // Stunde im Feld (Prüfer 05.10.2026).
    occurredAtLocal: erster.beginnOrtszeit,
    body: `Vom Sensor erkannt: ${befunde.map(sprungSatz).join(' · ')}. `,
  })
  const [speichert, setSpeichert] = useState(false)
  const [fehler, setFehler] = useState<string | null>(null)

  async function speichern(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const leer = eintragFehler(felder)
    if (leer) { setFehler(leer); return }
    setSpeichert(true)
    setFehler(null)
    try {
      await apiFetch(`/api/grows/${growId}/journal`, {
        method: 'POST',
        body: JSON.stringify({
          title: felder.title,
          body: felder.body,
          entryType: felder.entryType as JournalEntryType,
          occurredAtLocal: felder.occurredAtLocal,
        }),
      })
      await onGespeichert()
    } catch (caught) {
      setFehler(formatApiError(caught, 'Notiz konnte nicht gespeichert werden.'))
    } finally {
      setSpeichert(false)
    }
  }

  return (
    <form className="js-form tb-formular" data-audit="tagebuch-notiz-form" onSubmit={(event) => void speichern(event)}>
      <EintragFelderFormular werte={felder} autoFocusTitel onChange={(patch) => setFelder((alt) => ({ ...alt, ...patch }))} />
      {fehler && <p className="tb-fehler" role="alert">{fehler}</p>}
      <div className="js-knopfreihe">
        <V1Button type="submit" variant="primary" audit="tagebuch-notiz-speichern" disabled={speichert}>{speichert ? 'Speichert…' : 'Notiz speichern'}</V1Button>
        <V1Button onClick={onAbbrechen} disabled={speichert}>Abbrechen</V1Button>
      </div>
    </form>
  )
}
