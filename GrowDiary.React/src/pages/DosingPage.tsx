import { useCallback, useEffect, useState } from 'react'
import { apiFetch } from '../api'
import type { TentDto } from '../types'
import { V1Alert, V1Button, V1Card, V1Empty, V1LinkButton, V1Page, V1Section, V1Skeleton } from '../components/v1'
import { PumpGraphic } from '../features/dosing/PumpGraphic'
import { MAX_CALIBRATION_SECONDS, runSecondsForPump, secondsForTarget, targetForPump } from '../features/dosing/calibration'
import '../features/dosing/dosing.css'
import { classNames } from '../utils'
import { zahlOderNull } from '../zahlenfeld'

/**
 * Dosierpumpen — Stufe 1: nichts läuft von allein.
 *
 * Jede Dosis wird hier ausgelöst, weil jemand gedrückt hat. Die Automatik
 * kommt erst, wenn Rechnung und Anschläge sich an echten Zelten bewährt haben.
 * Bis dahin ist das hier die ehrlichste Fassung: Grow OS rechnet und schaltet,
 * die Entscheidung bleibt beim Menschen.
 */

type Pump = {
  id: number
  tentId: number
  name: string
  purpose: string
  agent: string | null
  concentrationPercent: number | null
  haEntityId: string
  mlPerMinute: number | null
  calibratedAtUtc: string | null
  tubeChangedAtUtc: string | null
  maxSingleDoseMl: number
  minIntervalMinutes: number
  maxDosesPerDay: number
  maxMlPerDay: number
  automationEnabled: boolean
  hasHomeAssistantAutoOff: boolean
  simulationMode: boolean
  metricKey: string | null
  learnedChangePerMl: number | null
  learnedFromDoses: number
  blockedReason: string | null
}

type Suggestion = {
  allowed: boolean
  ml: number
  seconds: number
  reason: string
  reading: number | null
  readingFrom: 'sensor' | 'manual' | 'none'
  readingAgeMinutes: number | null
  target: number | null
  targetFrom: 'user' | 'profile' | 'none'
  learnedChangePerMl: number | null
  learnedFromDoses: number
}

type DoseEvent = {
  id: number
  pumpName: string
  occurredAtUtc: string
  trigger: string
  outcome: string
  requestedMl: number
  dosedMl: number
  secondsRun: number
  valueBefore: number | null
  valueAfter: number | null
  reason: string | null
  simulated: boolean
}

const PURPOSE_LABEL: Record<string, string> = {
  PhDown: 'pH senken',
  PhUp: 'pH heben',
  Nutrient: 'Nährstoff',
  CalMag: 'CalMag',
  Custom: 'frei',
}

function toneFor(purpose: string): 'danger' | 'accent' | 'info' {
  if (purpose === 'PhDown' || purpose === 'PhUp') return 'danger'
  if (purpose === 'Nutrient' || purpose === 'CalMag') return 'accent'
  return 'info'
}

function tageSeit(iso: string | null): string {
  if (!iso) return 'nie'
  const tage = Math.floor((Date.now() - new Date(iso).getTime()) / 86_400_000)
  if (Number.isNaN(tage)) return 'nie'
  return tage <= 0 ? 'heute' : `vor ${tage} T`
}

function zeit(iso: string): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return '—'
  return new Intl.DateTimeFormat('de-DE', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }).format(date)
}

function zahl(value: number | null | undefined, decimals = 2): string {
  return value == null ? '—' : value.toFixed(decimals).replace('.', ',')
}

function DosingPage() {
  const [tents, setTents] = useState<TentDto[]>([])
  const [pumps, setPumps] = useState<Pump[]>([])
  const [log, setLog] = useState<DoseEvent[]>([])
  const [loading, setLoading] = useState(true)
  const [busyPumpId, setBusyPumpId] = useState<number | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [doseMl, setDoseMl] = useState<Record<number, string>>({})
  const [suggestions, setSuggestions] = useState<Record<number, Suggestion>>({})
  const [calibrating, setCalibrating] = useState<number | null>(null)
  const [calibMl, setCalibMl] = useState('')
  // Die Sekunden des letzten Laufs — die Foerdermenge rechnet sich daraus.
  const [calibSeconds, setCalibSeconds] = useState(30)
  // Restzeit des laufenden Kalibrierlaufs. Zwei Minuten „Läuft…" ohne Zahl
  // sehen aus wie ein Absturz — also zählt der Knopf herunter.
  const [restSekunden, setRestSekunden] = useState<number | null>(null)

  const laeuft = restSekunden != null
  useEffect(() => {
    if (!laeuft) return
    // Bei 1 auf null: das stoppt den Takt von selbst, ohne zweiten Zustand.
    const id = window.setInterval(
      () => setRestSekunden((value) => (value == null || value <= 1 ? null : value - 1)), 1000)
    return () => window.clearInterval(id)
  }, [laeuft])

  // Nach jeder Aktion einmal hochzählen — laden passiert nur im Effekt, damit
  // kein setState direkt aus dem Effektkörper heraus Kaskaden auslöst.
  const [refresh, setRefresh] = useState(0)
  const laden = useCallback(() => setRefresh((value) => value + 1), [])

  useEffect(() => {
    const controller = new AbortController()
    async function load() {
      try {
        const [tentData, pumpData, logData] = await Promise.all([
          apiFetch<TentDto[]>('/api/settings/tents', { signal: controller.signal }),
          apiFetch<Pump[]>('/api/dosing/pumps', { signal: controller.signal }),
          apiFetch<DoseEvent[]>('/api/dosing/log?limit=25', { signal: controller.signal }).catch(() => []),
        ])
        if (controller.signal.aborted) return
        setTents(tentData)
        setPumps(pumpData)
        setLog(logData)
      } catch (caught) {
        if (!controller.signal.aborted) setError(caught instanceof Error ? caught.message : 'Pumpen konnten nicht geladen werden.')
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }
    void load()
    return () => controller.abort()
  }, [refresh])

  async function dosieren(pump: Pump) {
    // Leer, unlesbar und 0 landen in derselben Meldung — wie bisher.
    const ml = zahlOderNull(doseMl[pump.id] ?? '')
    if (ml == null || ml <= 0) {
      setError('Trag eine Menge in Millilitern ein.')
      return
    }
    setBusyPumpId(pump.id)
    setError(null)
    setMessage(null)
    try {
      const result = await apiFetch<{ dosed: boolean; ml: number; reason: string }>(
        `/api/dosing/pumps/${pump.id}/dose`, { method: 'POST', body: JSON.stringify({ ml }) })
      if (result.dosed) setMessage(result.reason)
      else setError(result.reason)
      laden()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Dosieren fehlgeschlagen.')
    } finally {
      setBusyPumpId(null)
    }
  }

  /**
   * Stufe 2: rechnen, nicht dosieren.
   *
   * Der Vorschlag geht durch dieselben Anschläge wie eine echte Dosis — hier
   * steht also nie eine Menge, die beim Druck auf „Dosieren" abgelehnt würde.
   * Übernommen wird sie trotzdem nur, wenn jemand drückt.
   */
  async function vorschlagen(pump: Pump) {
    setBusyPumpId(pump.id)
    setError(null)
    try {
      const result = await apiFetch<Suggestion>(`/api/dosing/pumps/${pump.id}/suggestion`)
      setSuggestions((current) => ({ ...current, [pump.id]: result }))
      if (result.allowed) setDoseMl((current) => ({ ...current, [pump.id]: String(result.ml).replace('.', ',') }))
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Der Vorschlag liess sich nicht berechnen.')
    } finally {
      setBusyPumpId(null)
    }
  }

  /**
   * Kalibrierlauf — entweder auf eine Zielmenge oder auf eine feste Zeit.
   *
   * Die Zielmenge ist genauer: 1 ml Ablesefehler wiegt bei 100 ml viermal
   * weniger als bei 23 ml. Sie setzt aber eine grobe Foerdermenge voraus, die
   * es beim allerersten Mal noch nicht gibt — dann laeuft es ueber die Zeit.
   */
  async function kalibrierlauf(pump: Pump, options: { seconds?: number; targetMl?: number }) {
    setBusyPumpId(pump.id)
    setError(null)
    setMessage(null)
    // Die Sekunden merken, mit denen wirklich gelaufen wurde — daraus rechnet
    // der Server spaeter die Foerdermenge.
    // Gedeckelt wie auf dem Server: bei einer sehr langsamen Pumpe passt selbst
    // die kleinste Zielmenge nicht in die Zeit, und dann zaehlt der Knopf sonst
    // an einem Lauf herunter, der laengst vorbei ist.
    const geschaetzt = Math.min(
      options.targetMl != null
        ? secondsForTarget(options.targetMl, pump.mlPerMinute) ?? options.seconds ?? 30
        : options.seconds ?? 30,
      MAX_CALIBRATION_SECONDS)
    setCalibSeconds(geschaetzt)
    setRestSekunden(Math.ceil(geschaetzt))
    try {
      const result = await apiFetch<{ dosed: boolean; seconds: number; reason: string }>(
        `/api/dosing/pumps/${pump.id}/calibration/run`,
        { method: 'POST', body: JSON.stringify({ seconds: options.seconds ?? 30, targetMl: options.targetMl ?? null }) })
      if (result.dosed) { setCalibSeconds(result.seconds); setMessage(result.reason); setCalibrating(pump.id) }
      else setError(result.reason)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Kalibrierlauf fehlgeschlagen.')
    } finally {
      setBusyPumpId(null)
      setRestSekunden(null)
    }
  }

  async function kalibrierungSpeichern(pump: Pump, seconds: number) {
    const measuredMl = zahlOderNull(calibMl)
    if (measuredMl == null || measuredMl <= 0) {
      setError('Trag ein, was im Becher steht.')
      return
    }
    setBusyPumpId(pump.id)
    try {
      await apiFetch(`/api/dosing/pumps/${pump.id}/calibration`, {
        method: 'POST', body: JSON.stringify({ seconds, measuredMl }),
      })
      setCalibrating(null)
      setCalibMl('')
      setMessage('Fördermenge gespeichert.')
      laden()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Speichern fehlgeschlagen.')
    } finally {
      setBusyPumpId(null)
    }
  }

  async function allesStoppen() {
    setError(null)
    try {
      await Promise.all(pumps.map((pump) =>
        apiFetch(`/api/dosing/pumps/${pump.id}/stop`, { method: 'POST' }).catch(() => null)))
      setMessage('Stopp an alle Pumpen geschickt.')
    } catch {
      setError('Stopp konnte nicht an alle Pumpen gehen — in Home Assistant nachsehen.')
    }
  }

  if (loading) return <V1Skeleton tiles={2} rows={3} label="Lade Pumpen" />

  return (
    <V1Page
      eyebrow="Betrieb"
      title="Dosierung"
      subtitle="Grow OS schaltet deine Peristaltikpumpen über Home Assistant. Noch dosiert nichts von allein — jede Dosis löst du hier aus."
      action={pumps.length > 0
        ? <V1Button variant="danger" onClick={() => void allesStoppen()}>Alles stoppen</V1Button>
        : undefined}
    >
      {error && <V1Alert message={error} tone="critical" />}
      {message && <V1Alert message={message} tone="ok" />}

      {pumps.length === 0 ? (
        <V1Empty
          title="Noch keine Pumpe eingerichtet"
          text={tents.length === 0
            ? 'Lege zuerst ein Zelt an — eine Pumpe gehört immer zu einem.'
            : 'Eine Pumpe braucht einen Namen, ihre Aufgabe und die Home-Assistant-Entität, die sie schaltet.'}
          action={tents.length === 0
            ? <V1LinkButton to="/zelte/new">Zelt anlegen</V1LinkButton>
            : <V1LinkButton to="/dosierung/neu" variant="primary">Pumpe einrichten</V1LinkButton>}
        />
      ) : (
        <>
          <V1Section title="Pumpen" action={<V1LinkButton to="/dosierung/neu">+ Pumpe</V1LinkButton>}>
            <div className="dz-pumps">
              {pumps.map((pump) => {
                const dosing = busyPumpId === pump.id
                const zelt = tents.find((tent) => tent.id === pump.tentId)
                return (
                  <article
                    key={pump.id}
                    className={classNames('dz-pump', dosing && 'is-dosing')}
                    data-audit={`dosing-pump-${pump.id}`}
                  >
                    <div className="dz-pump-head">
                      <span className="name">{pump.name}</span>
                      {pump.simulationMode && <span className="dz-badge">Testbetrieb</span>}
                      <span className={classNames('dz-pump-state', !dosing && pump.blockedReason && 'is-blocked')}>
                        {dosing ? '● dosiert' : pump.blockedReason ? 'gesperrt' : 'bereit'}
                      </span>
                    </div>

                    <PumpGraphic dosing={dosing} tone={toneFor(pump.purpose)} />

                    <div className="dz-facts">
                      <div className="row"><span>Aufgabe</span><b>{PURPOSE_LABEL[pump.purpose] ?? pump.purpose}</b></div>
                      <div className="row">
                        <span>Mittel</span>
                        <b>{pump.agent ?? '—'}{pump.concentrationPercent != null ? ` ${zahl(pump.concentrationPercent, 0)} %` : ''}</b>
                      </div>
                      <div className="row">
                        <span>Fördermenge</span>
                        {pump.mlPerMinute != null
                          ? <b>{zahl(pump.mlPerMinute, 1)} ml/min</b>
                          : <span className="is-blocked">nicht kalibriert</span>}
                      </div>
                      <div className="row"><span>Schlauch</span><span>seit {tageSeit(pump.tubeChangedAtUtc)}</span></div>
                      <div className="row"><span>Zelt</span><span>{zelt?.name ?? `#${pump.tentId}`}</span></div>
                      {pump.learnedChangePerMl != null ? (
                        <div className="row">
                          <span>Gelernt</span>
                          <span className="is-learned">
                            {zahl(pump.learnedChangePerMl, 3)} je ml · aus {pump.learnedFromDoses} Dosen
                          </span>
                        </div>
                      ) : (
                        <div className="row"><span>Gelernt</span><span>noch nichts — die ersten Dosen gibst du selbst</span></div>
                      )}
                      {pump.blockedReason && (
                        <div className="row"><span>Gesperrt</span><span className="is-blocked">{pump.blockedReason}</span></div>
                      )}
                    </div>

                    <div className="dz-actions">
                      {pump.mlPerMinute == null ? (
                        <>
                          <V1Button onClick={() => void kalibrierlauf(pump, { seconds: 30 })} disabled={dosing}>
                            {dosing ? `Läuft… noch ${restSekunden ?? 0} s` : '30 s Kalibrierlauf'}
                          </V1Button>
                          <span className="dz-cal-note" style={{ font: '400 12px/1.4 var(--font-mono)', color: 'var(--faint)' }}>
                            Schlauchende in den Messbecher — beim ersten Mal über die Zeit
                          </span>
                        </>
                      ) : (
                        <>
                          <input
                            className="dz-dose-input"
                            inputMode="decimal"
                            placeholder="ml"
                            value={doseMl[pump.id] ?? ''}
                            onChange={(event) => setDoseMl((current) => ({ ...current, [pump.id]: event.target.value }))}
                            aria-label={`Menge für ${pump.name} in Millilitern`}
                          />
                          <V1Button variant="primary" onClick={() => void dosieren(pump)} disabled={dosing}>
                            {dosing ? 'Dosiert…' : 'Dosieren'}
                          </V1Button>
                          <V1Button onClick={() => void vorschlagen(pump)} disabled={dosing}>
                            Was wäre jetzt nötig?
                          </V1Button>
                          <V1Button onClick={() => void kalibrierlauf(pump, { targetMl: targetForPump(pump.mlPerMinute) })} disabled={dosing}>
                            {dosing
                              ? `Läuft… noch ${restSekunden ?? 0} s`
                              : `Genau kalibrieren · ${targetForPump(pump.mlPerMinute)} ml`}
                          </V1Button>
                          <span style={{ font: '400 11px/1.4 var(--font-mono)', color: 'var(--faint)' }}>
                            läuft {zahl(runSecondsForPump(pump.mlPerMinute), 0)} s in den Messbecher
                          </span>
                        </>
                      )}
                      <V1LinkButton to={`/dosierung/${pump.id}`} variant="ghost">Einstellen</V1LinkButton>
                    </div>

                    {suggestions[pump.id] && <SuggestionPanel suggestion={suggestions[pump.id]} />}

                    {calibrating === pump.id && (
                      <div className="dz-actions" style={{ borderTop: '1px solid var(--hair)', paddingTop: 10 }}>
                        <span style={{ font: '400 11.5px/1.4 var(--font-mono)', color: 'var(--muted)' }}>
                          Was steht wirklich im Becher? ({calibSeconds.toFixed(1).replace('.', ',')} s gelaufen)
                        </span>
                        <input
                          className="dz-dose-input"
                          inputMode="decimal"
                          placeholder="ml"
                          value={calibMl}
                          onChange={(event) => setCalibMl(event.target.value)}
                          aria-label="Gemessene Menge in Millilitern"
                        />
                        <V1Button variant="primary" onClick={() => void kalibrierungSpeichern(pump, calibSeconds)} disabled={dosing}>
                          Übernehmen
                        </V1Button>
                        <V1Button onClick={() => { setCalibrating(null); setCalibMl('') }}>Abbrechen</V1Button>
                      </div>
                    )}
                  </article>
                )
              })}
            </div>
          </V1Section>

          <V1Section title="Sicherheit">
            <V1Card>
              <div className="dz-warn">
                <div>
                  <b>Konzentrierte Mittel sind ätzend.</b> Beim Wechseln von Kanister oder Schlauch:
                  Handschuhe und Schutzbrille, Säure ins Wasser und nie umgekehrt, und pH-Plus und
                  pH-Minus niemals im selben Behälter.
                  <br /><br />
                  <b>Grow OS schaltet ein und wieder aus.</b> Stürzt es genau dazwischen ab, läuft die
                  Pumpe weiter. Richte deshalb in Home Assistant eine Abschaltung ein, die die Pumpe
                  nach spätestens 30 Sekunden von sich aus abwirft. Beim Start wirft Grow OS jede
                  eingerichtete Pumpe einmal aus — falls sie vom letzten Mal noch lief.
                </div>
              </div>
            </V1Card>
          </V1Section>
        </>
      )}

      {log.length > 0 && (
        <V1Section title="Protokoll">
          <div className="co-table-wrap dz-log-wrap">
            {/* Mindestens 700 px: bei den 560 px aus `collection.css` bekamen die
                Zahlenspalten je 49 px, und darin brach „6,31" mitten in der Zahl
                um — am Telefon stand „6,3" ueber „1". Die Spalten sind Anteile,
                also wird die ganze Tabelle breiter statt einzelner Spalten; der
                Wischkasten drumherum kann das. */}
            <div
              className="co-table"
              style={{ gridTemplateColumns: '.8fr 1.15fr .6fr .6fr .6fr 1.6fr', minWidth: 760 }}
            >
              <div className="co-th">Wann</div>
              <div className="co-th">Pumpe</div>
              <div className="co-th">Menge</div>
              <div className="co-th">Vorher</div>
              <div className="co-th">Nachher</div>
              <div className="co-th">Grund</div>
              {log.map((dose) => (
                <LogRow key={dose.id}>
                  <div className="co-td">{zeit(dose.occurredAtUtc)}</div>
                  <div className="co-td is-name">
                    {dose.pumpName}
                    {dose.simulated && <span className="dz-badge is-small">Test</span>}
                  </div>
                  <div className="co-td">{dose.outcome === 'Done' ? `${zahl(dose.dosedMl, 1)} ml` : '—'}</div>
                  <div className="co-td">{zahl(dose.valueBefore)}</div>
                  <div className="co-td">{zahl(dose.valueAfter)}</div>
                  <div className={classNames('co-td', dose.outcome !== 'Done' && 'is-muted')}>{dose.reason ?? '—'}</div>
                </LogRow>
              ))}
            </div>
          </div>
        </V1Section>
      )}
    </V1Page>
  )
}

/** Nur ein Fragment — die Zellen müssen direkte Grid-Kinder bleiben. */
function LogRow({ children }: { children: React.ReactNode }) {
  return <>{children}</>
}

/**
 * Der Vorschlag, aufgeschlüsselt.
 *
 * Bewusst nicht nur die Menge: eine Zahl, die eine Pumpe Säure ins Becken
 * drücken lässt, muss nachrechenbar sein. Wer sie für falsch hält, soll auf
 * einen Blick sehen, an welcher Stelle — am Messwert, am Ziel oder am Gelernten.
 */
function SuggestionPanel({ suggestion }: { suggestion: Suggestion }) {
  const messwertHerkunft = suggestion.readingFrom === 'sensor' ? 'Sensor'
    : suggestion.readingFrom === 'manual' ? 'von Hand' : null
  const zielHerkunft = suggestion.targetFrom === 'user' ? 'dein Grenzwert'
    : suggestion.targetFrom === 'profile' ? 'Sollwert-Profil' : null

  return (
    <div className={classNames('dz-suggestion', suggestion.allowed && 'is-allowed')}>
      <div className="dz-suggestion-head">
        {suggestion.allowed
          ? <b>{zahl(suggestion.ml, 2)} ml · {zahl(suggestion.seconds, 1)} s</b>
          : <b>Kein Vorschlag</b>}
        <span>{suggestion.reason}</span>
      </div>
      <div className="dz-suggestion-facts">
        <span>
          Ist{' '}
          {suggestion.reading != null ? zahl(suggestion.reading, 2) : '—'}
          {messwertHerkunft && ` · ${messwertHerkunft}`}
          {suggestion.readingAgeMinutes != null && ` · vor ${suggestion.readingAgeMinutes} min`}
        </span>
        <span>
          Ziel {suggestion.target != null ? zahl(suggestion.target, 2) : '—'}
          {zielHerkunft && ` · ${zielHerkunft}`}
        </span>
        <span>
          {suggestion.learnedChangePerMl != null
            ? `Gelernt ${zahl(suggestion.learnedChangePerMl, 3)} je ml · aus ${suggestion.learnedFromDoses} Dosen`
            : 'Noch nichts gelernt'}
        </span>
      </div>
    </div>
  )
}

export default DosingPage
