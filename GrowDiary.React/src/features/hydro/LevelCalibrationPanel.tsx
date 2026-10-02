import { useEffect, useRef, useState } from 'react'
import { apiFetch } from '../../api'
import { V1Alert, V1Button, V1Card, V1Field } from '../../components/v1'
import { zahlOderNull } from '../../zahlenfeld'
import './level-calibration.css'

/**
 * Der geführte Kalibrierlauf: Grow OS liest den Pegelsensor mit, während du füllst.
 *
 * Der Ablauf kommt aus der Praxis, nicht aus dem Formular. Wer sein System
 * kalibriert, steht mit dem Schlauch daneben und liest an der Wasseruhr ab — er
 * soll füllen, nicht Zahlen abtippen. Deshalb fragt die Seite den Sensor jede
 * Sekunde und erkennt selbst, wann der Stand steht.
 *
 * Bestätigt wird trotzdem von Hand: eine Füllpause sieht für den Sensor genauso
 * aus wie „fertig", und eine Fehlkalibrierung verzieht danach jede Dosis.
 */

type Step = 'WaitingForEmpty' | 'Filling' | 'AwaitingConfirmation' | 'Done' | 'NoSensor'

type State = {
  systemId: number
  step: Step
  currentRaw: number | null
  emptyRaw: number | null
  stableRaw: number | null
  secondsSteady: number
  secondsNeeded: number
  sampleCount: number
  message: string
}

function zahl(value: number | null | undefined, decimals = 2): string {
  return value == null ? '—' : value.toFixed(decimals).replace('.', ',')
}

export function LevelCalibrationPanel(
  { systemId, onDone, bereitsKalibriert = false }:
  { systemId: number; onDone: () => void; bereitsKalibriert?: boolean },
) {
  const [state, setState] = useState<State | null>(null)
  const [liters, setLiters] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const laeuft = useRef(false)

  // Beim Öffnen einmal nachsehen, ob schon ein Lauf offen ist.
  //
  // Kalibrieren dauert Minuten, und man steht dabei mit dem Schlauch am Becken —
  // das Handy sperrt, die Seite lädt neu. Ohne dieses Anknüpfen wäre der Lauf
  // dann unsichtbar, und der nächste Klick auf „starten" würfe den bereits
  // gefundenen Nullpunkt weg. Der Lauf lebt im Server, nicht in dieser Seite.
  useEffect(() => {
    let abgemeldet = false
    void apiFetch<State>(`/api/hydro-setups/${systemId}/level-calibration`)
      .then((offen) => {
        if (abgemeldet) return
        if (offen.step === 'WaitingForEmpty' || offen.step === 'Filling' || offen.step === 'AwaitingConfirmation') {
          setState(offen)
        }
      })
      .catch(() => { /* kein Lauf offen ist der Normalfall */ })
    return () => { abgemeldet = true }
  }, [systemId])

  // Der Takt: einmal pro Sekunde ablesen. Nur solange ein Lauf offen ist —
  // sonst fragte die Seite Home Assistant im Sekundentakt, ohne dass jemand
  // davorsteht.
  useEffect(() => {
    if (state === null || state.step === 'Done') return

    const id = window.setInterval(() => {
      if (laeuft.current) return
      laeuft.current = true
      void apiFetch<State>(`/api/hydro-setups/${systemId}/level-calibration`)
        .then((next) => setState(next))
        .catch(() => { /* ein verpasster Takt ist harmlos, der naechste kommt */ })
        .finally(() => { laeuft.current = false })
    }, 1000)

    return () => window.clearInterval(id)
  }, [state, systemId])

  async function starten() {
    setBusy(true)
    setError(null)
    try {
      setState(await apiFetch<State>(`/api/hydro-setups/${systemId}/level-calibration/start`, { method: 'POST' }))
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Der Lauf liess sich nicht starten.')
    } finally {
      setBusy(false)
    }
  }

  async function abbrechen() {
    await apiFetch(`/api/hydro-setups/${systemId}/level-calibration/cancel`, { method: 'POST' }).catch(() => {})
    setState(null)
    setLiters('')
    setError(null)
  }

  async function bestaetigen() {
    // Leer, unlesbar und „0" landen alle in derselben Meldung — wie bisher.
    const menge = zahlOderNull(liters)
    if (menge == null || menge <= 0) {
      setError('Trag ein, wie viel Wasser wirklich hineingegangen ist.')
      return
    }

    setBusy(true)
    setError(null)
    try {
      await apiFetch(`/api/hydro-setups/${systemId}/level-calibration/finish`, {
        method: 'POST',
        body: JSON.stringify({ liters: menge }),
      })
      setState(null)
      setLiters('')
      onDone()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Speichern fehlgeschlagen.')
    } finally {
      setBusy(false)
    }
  }

  // Wer schon kalibriert hat, hat die Erklaerung daneben stehen — sie ein
  // zweites Mal zu bringen, macht die Seite nur laenger. Hier zaehlt nur noch
  // der Weg zurueck: nach einem Umbau oder einem neuen Sensor neu messen.
  if (state === null && bereitsKalibriert) {
    return (
      <div className="lk-again">
        {error && <V1Alert message={error} tone="warn" />}
        <V1Button onClick={() => void starten()} disabled={busy}>
          {busy ? 'Startet…' : 'Neu kalibrieren'}
        </V1Button>
        <span className="lk-hint">Nach Umbau, neuem Sensor oder verrutschtem eTape.</span>
      </div>
    )
  }

  if (state === null) {
    return (
      <V1Card>
        <p className="lk-intro">
          Grow OS liest den Wasserstand-Sensor mit, während du füllst, und erkennt Null- und Vollpunkt
          selbst. Danach steht der Wasserstand überall in Litern — auf der Kachel, im Verlauf, in den
          Grenzwerten, und die Dosierung rechnet mit dem echten Volumen.
        </p>
        <ul className="lk-list">
          <li><b>System muss leer sein</b>, bevor du startest.</li>
          <li><b>Umwälzpumpe an lassen</b> — so wird später auch gemessen.</li>
          <li>Eine Wasseruhr bereithalten (Gardena o. ä.), um die Litermenge abzulesen.</li>
        </ul>
        {error && <V1Alert message={error} tone="warn" />}
        <V1Button variant="primary" onClick={() => void starten()} disabled={busy}>
          {busy ? 'Startet…' : 'Kalibrierung starten'}
        </V1Button>
      </V1Card>
    )
  }

  const fortschritt = state.secondsNeeded > 0
    ? Math.min(100, Math.round((state.secondsSteady / state.secondsNeeded) * 100))
    : 0

  return (
    <V1Card>
      <div className="lk-live">
        <div className="lk-value">
          <span className="lk-label">Sensor jetzt</span>
          <strong>{zahl(state.currentRaw)}</strong>
        </div>
        <div className="lk-value">
          <span className="lk-label">Nullpunkt</span>
          <strong>{zahl(state.emptyRaw)}</strong>
        </div>
        <div className="lk-value">
          <span className="lk-label">Ruhig seit</span>
          <strong>{state.secondsSteady} s</strong>
        </div>
      </div>

      {state.step !== 'AwaitingConfirmation' && state.step !== 'NoSensor' && (
        <div className="lk-bar" aria-label={`${fortschritt} % der nötigen Ruhezeit`}>
          <span style={{ width: `${fortschritt}%` }} />
        </div>
      )}

      <p className="lk-message">{state.message}</p>

      {state.step === 'AwaitingConfirmation' && (
        <div className="lk-confirm">
          <V1Field
            label="Wie viel Wasser ist hineingegangen?"
            hint="Der Wert von deiner Wasseruhr, in Litern.">
            <input
              value={liters}
              onChange={(event) => setLiters(event.target.value)}
              inputMode="decimal"
              placeholder="z. B. 100"
              aria-label="Eingefüllte Liter" />
          </V1Field>
          <div className="lk-actions">
            <V1Button variant="primary" onClick={() => void bestaetigen()} disabled={busy}>
              Voll — übernehmen
            </V1Button>
            <span className="lk-hint">Noch nicht voll? Einfach weiterfüllen, ich melde mich wieder.</span>
          </div>
        </div>
      )}

      {error && <V1Alert message={error} tone="warn" />}

      <div className="lk-actions">
        <V1Button onClick={() => void abbrechen()}>Abbrechen</V1Button>
      </div>
    </V1Card>
  )
}
