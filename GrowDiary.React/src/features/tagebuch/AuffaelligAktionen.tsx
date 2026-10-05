import { useState, type FormEvent } from 'react'
import { apiFetch, formatApiError } from '../../api'
import { V1Button, V1Field } from '../../components/v1'
import type { CreateAddbackLogRequest, JournalEntryType, TagebuchSprungDto, WaterSource } from '../../types'
import { feldText, unlesbareFelder, unlesbarMeldung, zahlOderNull } from '../../zahlenfeld'
import { EintragFelderFormular } from '../grow-detail/JournalStreamSection'
import { eintragFehler, type EintragFelder } from '../grow-detail/journal-bearbeiten'
import { alsEingabeZeit, sprungSatz } from './tagebuch-modell'

type Modus = 'nachfuellen' | 'notiz' | null

/**
 * Die drei Antworten auf „Nachgefüllt?" an einer Auffälligkeit.
 *
 * - **Nachfüllen eintragen**: ein Nachfüll-Eintrag (Addback-Protokoll, Art
 *   „Nachfüllen"), vorbelegt mit Zeitpunkt und den Werten davor/danach.
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
        <V1Button className="tb-klein" audit="tagebuch-nachfuellen" disabled={laeuft} onClick={() => setModus(modus === 'nachfuellen' ? null : 'nachfuellen')}>
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
      {modus === 'nachfuellen' && (
        <NachfuellenFormular growId={growId} befunde={befunde} onAbbrechen={() => setModus(null)} onGespeichert={onGespeichert} />
      )}
      {modus === 'notiz' && (
        <NotizFormular growId={growId} befunde={befunde} onAbbrechen={() => setModus(null)} onGespeichert={onGespeichert} />
      )}
    </>
  )
}

/** Der Wert eines Befunds dieser Messgröße — für die Vorbelegung, mit Komma. */
function wert(befunde: TagebuchSprungDto[], messgroesse: string, seite: 'vorher' | 'nachher'): string {
  const b = befunde.find((x) => x.messgroesse === messgroesse)
  return b ? feldText(Number(b[seite].toFixed(2))) : ''
}

function literAusPegel(befunde: TagebuchSprungDto[]): string {
  const pegel = befunde.find((b) => b.messgroesse === 'reservoir-level' && b.nachher > b.vorher)
  return pegel ? feldText(Math.round((pegel.nachher - pegel.vorher) * 10) / 10) : ''
}

function NachfuellenFormular({ growId, befunde, onAbbrechen, onGespeichert }: {
  growId: string
  befunde: TagebuchSprungDto[]
  onAbbrechen: () => void
  onGespeichert: () => void | Promise<void>
}) {
  const erster = befunde[0]
  const [felder, setFelder] = useState({
    zeitpunkt: alsEingabeZeit(erster.endeUtc),
    // Hat der Wasserstand (in Litern) mitgesprungen, ist das die Menge.
    liter: literAusPegel(befunde),
    wasser: '',
    ecVorher: wert(befunde, 'reservoir-ec', 'vorher'),
    ecNachher: wert(befunde, 'reservoir-ec', 'nachher'),
    phVorher: wert(befunde, 'reservoir-ph', 'vorher'),
    phNachher: wert(befunde, 'reservoir-ph', 'nachher'),
    notiz: `Nachgetragen aus dem Tagebuch: ${befunde.map(sprungSatz).join(' · ')}.`,
  })
  const [speichert, setSpeichert] = useState(false)
  const [fehler, setFehler] = useState<string | null>(null)
  const setze = (patch: Partial<typeof felder>) => setFelder((alt) => ({ ...alt, ...patch }))

  async function speichern(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const unlesbar = unlesbarMeldung(unlesbareFelder([
      [felder.liter, 'Liter nachgefüllt'],
      [felder.ecVorher, 'EC vorher'], [felder.ecNachher, 'EC nachher'],
      [felder.phVorher, 'pH vorher'], [felder.phNachher, 'pH nachher'],
    ]))
    if (unlesbar) { setFehler(unlesbar); return }
    const zeit = new Date(felder.zeitpunkt)
    if (Number.isNaN(zeit.getTime())) { setFehler('Bitte einen Zeitpunkt angeben.'); return }

    const anfrage: CreateAddbackLogRequest = {
      kind: 'TopOff',
      performedAtUtc: zeit.toISOString(),
      reservoirLiters: null,
      ecBefore: zahlOderNull(felder.ecVorher),
      ecTarget: null,
      ecStock: null,
      ecAfter: zahlOderNull(felder.ecNachher),
      phBefore: zahlOderNull(felder.phVorher),
      phAfter: zahlOderNull(felder.phNachher),
      litersAdded: zahlOderNull(felder.liter),
      newReservoirVolumeLiters: null,
      usedHydroSetupVolume: false,
      waterUsed: felder.wasser === '' ? null : (felder.wasser as WaterSource),
      notes: felder.notiz.trim() || null,
    }
    setSpeichert(true)
    setFehler(null)
    try {
      await apiFetch(`/api/grows/${growId}/addback/logs`, { method: 'POST', body: JSON.stringify(anfrage) })
      await onGespeichert()
    } catch (caught) {
      setFehler(formatApiError(caught, 'Nachfüllen konnte nicht gespeichert werden.'))
    } finally {
      setSpeichert(false)
    }
  }

  return (
    <form className="js-form tb-formular" data-audit="tagebuch-nachfuellen-form" onSubmit={(event) => void speichern(event)}>
      <V1Field label="Zeitpunkt"><input type="datetime-local" value={felder.zeitpunkt} onChange={(e) => setze({ zeitpunkt: e.target.value })} /></V1Field>
      <V1Field label="Liter nachgefüllt" hint="Leer lassen, wenn du es nicht weißt.">
        <input inputMode="decimal" value={felder.liter} onChange={(e) => setze({ liter: e.target.value })} />
      </V1Field>
      <V1Field label="Wasser">
        <select value={felder.wasser} onChange={(e) => setze({ wasser: e.target.value })}>
          <option value="">wie am Grow</option>
          <option value="RO">Osmose / VE-Wasser</option>
          <option value="Tap">Leitungswasser</option>
          <option value="Mixed">Mischung</option>
        </select>
      </V1Field>
      <div className="tb-paar">
        <V1Field label="EC vorher"><input inputMode="decimal" value={felder.ecVorher} onChange={(e) => setze({ ecVorher: e.target.value })} /></V1Field>
        <V1Field label="EC nachher"><input inputMode="decimal" value={felder.ecNachher} onChange={(e) => setze({ ecNachher: e.target.value })} /></V1Field>
      </div>
      <div className="tb-paar">
        <V1Field label="pH vorher"><input inputMode="decimal" value={felder.phVorher} onChange={(e) => setze({ phVorher: e.target.value })} /></V1Field>
        <V1Field label="pH nachher"><input inputMode="decimal" value={felder.phNachher} onChange={(e) => setze({ phNachher: e.target.value })} /></V1Field>
      </div>
      <V1Field label="Notiz" wide><textarea rows={3} value={felder.notiz} onChange={(e) => setze({ notiz: e.target.value })} /></V1Field>
      {fehler && <p className="tb-fehler" role="alert">{fehler}</p>}
      <div className="js-knopfreihe">
        <V1Button type="submit" variant="primary" audit="tagebuch-nachfuellen-speichern" disabled={speichert}>{speichert ? 'Speichert…' : 'Nachfüllen speichern'}</V1Button>
        <V1Button onClick={onAbbrechen} disabled={speichert}>Abbrechen</V1Button>
      </div>
    </form>
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
