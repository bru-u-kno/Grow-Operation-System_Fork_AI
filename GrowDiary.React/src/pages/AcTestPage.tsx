import { useCallback, useEffect, useRef, useState } from 'react'
import { apiFetch, formatApiError } from '../api'
import {
  V1Alert, V1Badge, V1Button, V1Card, V1Empty, V1Field, V1Page, V1Section, V1Skeleton,
} from '../components/v1'
import type { TentDto } from '../types'
import '../features/actest/actest.css'

/**
 * Zelt (AC-Test) — der Versuchsaufbau.
 *
 * **Die Frage dahinter:** kann Grow OS die Zentrale sein, von der aus der ganze
 * Grow läuft? Beantworten kann das kein Entwurf, sondern jemand, der es
 * benutzt. Deshalb steht es als eigener Menüpunkt da und nicht versteckt in den
 * Einstellungen — dort probiert es niemand aus und niemand gibt Rückmeldung.
 *
 * **Was hier absichtlich fehlt:** jede Automatik. Gestellt wird genau dann,
 * wenn jemand klickt. Der Controller behält sein eigenes Gehirn; zwei Systeme,
 * die dasselbe Gerät regeln, sind die Falle, die beim Kühler schon eine eigene
 * Regel bekommen hat.
 */

type Geraet = {
  name: string
  leistungEntityId: string
  modusEntityId: string | null
  einZeitEntityId: string | null
  ausZeitEntityId: string | null
}

type GeraetStand = {
  geraet: Geraet
  stufe: number | null
  /** Die Kennung, wie Home Assistant sie meldet („Schedule") — nicht zum Anzeigen. */
  modus: string | null
  /** Der Modus auf Deutsch („Zeitplan") — kommt aus `AcModi.Name` im Backend. */
  modusName: string | null
  /** Was der Controller MELDET — nicht, was jemand wollte. */
  einZeit: string | null
  ausZeit: string | null
  fehler: string | null
}

type Stand = {
  zeltId: number
  geraete: GeraetStand[]
  haVerbunden: boolean
  testbetrieb: boolean
  /** Der Lichtplan des Zelts, falls es einen gibt — die Quelle des Vorschlags. */
  lichtplan: { name: string; ein: string; aus: string } | null
}

/** Die Antwort auf einen Stellbefehl — drei Ausgaenge, nicht zwei. */
type StellErgebnis = {
  ok: boolean
  meldungen: string[]
}

const LEERES_GERAET: Geraet = {
  name: '',
  leistungEntityId: '',
  modusEntityId: null,
  einZeitEntityId: null,
  ausZeitEntityId: null,
}

const STUFEN = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10]

/**
 * Die Eingabefelder mit dem fuellen, was der Controller MELDET.
 *
 * Nicht mit dem Lichtplan: sonst staende in den Feldern ein Wunsch, und wer
 * sie anschaut, hielte ihn fuer den Zustand des Geraets. Der Lichtplan wird
 * angeboten — mit einem Knopf, den jemand druecken muss.
 */
function ausStand(stand: Stand): Record<string, { ein: string; aus: string }> {
  const gefuellt: Record<string, { ein: string; aus: string }> = {}
  for (const g of stand.geraete) {
    if (!g.geraet.einZeitEntityId || !g.geraet.ausZeitEntityId) continue
    gefuellt[g.geraet.leistungEntityId] = { ein: g.einZeit ?? '', aus: g.ausZeit ?? '' }
  }
  return gefuellt
}

export function AcTestPage() {
  const [zelte, setZelte] = useState<TentDto[]>([])
  const [zeltId, setZeltId] = useState<number | null>(null)
  const [stand, setStand] = useState<Stand | null>(null)
  const [laedt, setLaedt] = useState(true)
  const [fehler, setFehler] = useState<string | null>(null)
  const [meldung, setMeldung] = useState<string | null>(null)

  // Der Schwebezustand: gesendet, die Wolke hat den Wert noch nicht
  // zurueckgemeldet. GELB, nicht rot — der Tester hatte recht: "manchmal kommt
  // 502, aber das Schalten funktioniert". Es hatte funktioniert.
  const [schwebt, setSchwebt] = useState<string | null>(null)
  const [arbeitet, setArbeitet] = useState<string | null>(null)

  // Laufende Nachlese-Timer, damit ein Seitenwechsel sie nicht verwaist.
  const nachleseRef = useRef<number[]>([])
  useEffect(() => () => { nachleseRef.current.forEach((t) => window.clearTimeout(t)) }, [])


  // Der Entwurf der Geräteliste — getrennt vom Stand, damit ein halb getipptes
  // Feld nichts an einem laufenden Gerät ändert.
  const [entwurf, setEntwurf] = useState<Geraet[]>([])

  // Die gewuenschten Zeiten je Geraet — getrennt vom Gemeldeten, damit die
  // Anzeige nie behauptet, etwas sei gestellt, weil jemand getippt hat.
  const [zeiten, setZeiten] = useState<Record<string, { ein: string; aus: string }>>({})

  useEffect(() => {
    const abbruch = new AbortController()
    void (async () => {
      try {
        const liste = await apiFetch<TentDto[]>('/api/settings/tents', { signal: abbruch.signal })
        if (abbruch.signal.aborted) return
        setZelte(liste)
        setZeltId((aktuell) => aktuell ?? liste[0]?.id ?? null)
      } catch (caught) {
        if (!abbruch.signal.aborted) setFehler(formatApiError(caught, 'Zelte nicht ladbar.'))
      } finally {
        if (!abbruch.signal.aborted) setLaedt(false)
      }
    })()
    return () => abbruch.abort()
  }, [])

  // `useState`-Aufrufe hängen alle am `await`, laufen also erst nach dem
  // Rendern. Ein setState im Effektrumpf löst eine zweite Renderrunde aus,
  // bevor die erste fertig ist — `react-hooks` macht daraus zu Recht einen
  // Fehler, und derselbe Lint fährt in ci.yml.
  const laden = useCallback(async (id: number) => {
    try {
      const geholt = await apiFetch<Stand>(`/api/ac-test/${id}`)
      setStand(geholt)
      setEntwurf(geholt.geraete.map((g) => g.geraet))
      setZeiten(ausStand(geholt))
    } catch (caught) {
      setFehler(formatApiError(caught, 'Der Versuchsaufbau liess sich nicht laden.'))
    }
  }, [])

  /**
   * Nach einem Schwebezustand den Stand spaeter von selbst nachlesen.
   *
   * Die AC-Infinity-Integration meldet neue Werte oft erst nach ihrer
   * naechsten Wolken-Abfrage. Zweimal nachsehen deckt die uebliche Spanne ab;
   * wer will, laedt frueher von Hand.
   */
  function nachleseAnstossen(id: number) {
    nachleseRef.current.forEach((t) => window.clearTimeout(t))
    nachleseRef.current = [15_000, 45_000].map((wartezeit) =>
      window.setTimeout(() => {
        void (async () => {
          await laden(id)
          // Aufgeloest? Dann den gelben Hinweis wegraeumen — der neue Stand
          // steht ohnehin an den Karten.
          setSchwebt((aktuell) => aktuell === null ? null : aktuell)
        })()
      }, wartezeit))
  }

  useEffect(() => {
    if (zeltId == null) return

    // Der AbortController ist nicht nur Aufräumen: ohne ihn schreibt beim
    // schnellen Zeltwechsel die Antwort zum ALTEN Zelt in den Zustand.
    const abbruch = new AbortController()
    void (async () => {
      try {
        const geholt = await apiFetch<Stand>(`/api/ac-test/${zeltId}`, { signal: abbruch.signal })
        if (abbruch.signal.aborted) return
        setStand(geholt)
        setEntwurf(geholt.geraete.map((g) => g.geraet))
        setZeiten(ausStand(geholt))
      } catch (caught) {
        if (!abbruch.signal.aborted) {
          setFehler(formatApiError(caught, 'Der Versuchsaufbau liess sich nicht laden.'))
        }
      }
    })()

    return () => abbruch.abort()
  }, [zeltId])

  async function speichern() {
    if (zeltId == null) return
    setFehler(null)
    setMeldung(null)
    try {
      await apiFetch(`/api/ac-test/${zeltId}`, { method: 'PUT', body: JSON.stringify(entwurf) })
      setMeldung('Gespeichert.')
      await laden(zeltId)
    } catch (caught) {
      setFehler(formatApiError(caught, 'Konnte nicht gespeichert werden.'))
    }
  }

  async function stellen(entityId: string, stufe: number) {
    if (zeltId == null) return
    setFehler(null)
    setMeldung(null)
    setSchwebt(null)
    setArbeitet(`${entityId}:${stufe}`)
    try {
      const antwort = await apiFetch<StellErgebnis>(`/api/ac-test/${zeltId}/stufe`, {
        method: 'POST',
        body: JSON.stringify({ entityId, stufe }),
      })

      if (antwort.ok) {
        // Der Controller MELDET den Wert — deshalb darf hier „angekommen" stehen.
        setMeldung(`Stufe ${stufe} ist am Gerät angekommen.`)
      } else {
        // Gesendet, aber noch nicht zurueckgemeldet. Kein Fehler: das Geraet
        // hat oft laengst geschaltet, nur die Wolke traegt nach.
        setSchwebt(
          `${antwort.meldungen.join(' ')} Das Gerät hat oft trotzdem geschaltet — `
          + 'AC Infinity meldet neue Werte manchmal erst nach ein paar Minuten zurück. '
          + 'Die Anzeige liest den Stand gleich von selbst nach.')
        nachleseAnstossen(zeltId)
      }
      await laden(zeltId)
    } catch (caught) {
      setFehler(formatApiError(caught, 'Die Stufe liess sich nicht stellen.'))
    } finally {
      setArbeitet(null)
    }
  }

  async function zeitplanStellen(g: Geraet) {
    if (zeltId == null) return
    const wunsch = zeiten[g.leistungEntityId]
    if (!wunsch) return

    setFehler(null)
    setMeldung(null)
    setSchwebt(null)
    setArbeitet(`${g.leistungEntityId}:zeit`)
    try {
      const antwort = await apiFetch<StellErgebnis>(`/api/ac-test/${zeltId}/zeitplan`, {
        method: 'POST',
        body: JSON.stringify({ entityId: g.leistungEntityId, ein: wunsch.ein, aus: wunsch.aus }),
      })

      if (antwort.ok) {
        setMeldung(`Zeitplan ${wunsch.ein}–${wunsch.aus} ist am Gerät angekommen.`)
      } else {
        setSchwebt(
          `${antwort.meldungen.join(' ')} Das Gerät hat oft trotzdem übernommen — `
          + 'AC Infinity meldet neue Werte manchmal erst nach ein paar Minuten zurück. '
          + 'Die Anzeige liest den Stand gleich von selbst nach.')
        nachleseAnstossen(zeltId)
      }
      await laden(zeltId)
    } catch (caught) {
      setFehler(formatApiError(caught, 'Der Zeitplan liess sich nicht stellen.'))
    } finally {
      setArbeitet(null)
    }
  }

  const untertitel = 'Geräte am AC-Infinity-Controller direkt aus Grow OS stellen.'

  if (laedt) {
    return (
      <V1Page eyebrow="Versuch" title="Zelt (AC-Test)" subtitle={untertitel}>
        <V1Skeleton rows={5} label="Lade Versuchsaufbau" />
      </V1Page>
    )
  }

  const zeltWahl = zelte.length > 1 ? (
    <label className="v1-scope-picker">
      <span>Zelt</span>
      <select
        value={zeltId ?? ''}
        aria-label="Zelt wählen"
        onChange={(e) => setZeltId(Number(e.target.value))}
      >
        {zelte.map((z) => <option key={z.id} value={z.id}>{z.name}</option>)}
      </select>
    </label>
  ) : undefined

  return (
    <V1Page eyebrow="Versuch" title="Zelt (AC-Test)" subtitle={untertitel} action={zeltWahl}>
      {/* Der Hinweis steht GANZ oben und vor allem anderen — wer hier etwas
          stellt, soll wissen, worauf er sich einlässt. */}
      <V1Alert
        tone="warn"
        title="Das ist ein Test"
        message={
          'Dieser Bereich ist ein Versuch und noch nicht fertig. Er schreibt echte Werte an '
          + 'deine Geräte, sobald du eine Stufe anklickst — sonst passiert nichts von selbst. '
          + 'Sag uns, was fehlt und was hakt: davon hängt ab, wie die Gerätesteuerung in '
          + 'Grow OS am Ende aussieht.'
        } />

      {fehler && <V1Alert title="Fehler" message={fehler} tone="critical" />}
      {meldung && <V1Alert message={meldung} tone="ok" />}
      {schwebt && <V1Alert title="Gesendet — Bestätigung steht aus" message={schwebt} tone="warn" />}

      {stand?.testbetrieb && (
        <V1Alert
          tone="neutral"
          message={
            'Testbetrieb: die Messwerte sind erfunden und es geht nichts an ein echtes '
            + 'Gerät. Was du hier stellst, merkt sich der Testbestand — so lässt sich der '
            + 'ganze Weg samt Nachkontrolle durchspielen. Ohne GROW_OS_DEMO=1 gilt die '
            + 'echte Verbindung.'
          } />
      )}

      {stand && !stand.haVerbunden && !stand.testbetrieb && (
        <V1Alert
          tone="warn"
          message="Home Assistant ist nicht verbunden — Einrichtung → Home Assistant." />
      )}

      {/* ---------- Stellen ---------- */}
      <V1Section title="Stufe stellen">
        {stand == null || stand.geraete.length === 0 ? (
          <V1Empty
            title="Noch kein Gerät eingetragen"
            text={'Trag unten die Entität deines Geräts ein — bei AC Infinity heisst sie '
              + '„… Einschaltleistung" und beginnt mit number.'} />
        ) : (
          <div className="ac-geraete">
            {stand.geraete.map((g) => (
              <V1Card key={g.geraet.leistungEntityId}>
                <div className="ac-kopf">
                  <strong>{g.geraet.name}</strong>
                  {g.stufe != null && <V1Badge tone="accent">Stufe {g.stufe}</V1Badge>}
                  {g.modusName && <V1Badge>Modus {g.modusName}</V1Badge>}
                </div>
                <p className="ac-entity">{g.geraet.leistungEntityId}</p>

                {g.fehler ? (
                  <V1Alert tone="warn" message={g.fehler} />
                ) : (
                  <div className="ac-stufen" role="group" aria-label={`Stufe für ${g.geraet.name}`}>
                    {STUFEN.map((stufe) => (
                      <button
                        key={stufe}
                        type="button"
                        className={`ac-stufe${g.stufe === stufe ? ' is-jetzt' : ''}`}
                        disabled={arbeitet != null}
                        aria-pressed={g.stufe === stufe}
                        onClick={() => void stellen(g.geraet.leistungEntityId, stufe)}
                      >
                        {stufe}
                      </button>
                    ))}
                  </div>
                )}
                <p className="ac-hinweis">
                  {/* 0 heisst aus — das steht dabei, damit niemand es ausprobieren muss. */}
                  Stufe 0 schaltet das Gerät aus. Es passiert nur, was du hier anklickst.
                </p>

                {/* ---------- Zeitplan ---------- */}
                {g.geraet.einZeitEntityId && g.geraet.ausZeitEntityId && (
                  <div className="ac-zeitplan" data-audit="ac-zeitplan">
                    <div className="ac-kopf">
                      <strong>Zeitplan</strong>
                      {g.einZeit && g.ausZeit
                        ? <V1Badge>Gerät meldet {g.einZeit}–{g.ausZeit}</V1Badge>
                        : <V1Badge tone="warn">Gerät meldet keine Zeiten</V1Badge>}
                    </div>

                    <div className="ac-zeitfelder">
                      <V1Field label="An um">
                        <input
                          type="time"
                          value={zeiten[g.geraet.leistungEntityId]?.ein ?? ''}
                          aria-label={`Ein-Zeit für ${g.geraet.name}`}
                          onChange={(e) => setZeiten((alt) => ({
                            ...alt,
                            [g.geraet.leistungEntityId]: {
                              ein: e.target.value,
                              aus: alt[g.geraet.leistungEntityId]?.aus ?? '',
                            },
                          }))} />
                      </V1Field>
                      <V1Field label="Aus um">
                        <input
                          type="time"
                          value={zeiten[g.geraet.leistungEntityId]?.aus ?? ''}
                          aria-label={`Aus-Zeit für ${g.geraet.name}`}
                          onChange={(e) => setZeiten((alt) => ({
                            ...alt,
                            [g.geraet.leistungEntityId]: {
                              ein: alt[g.geraet.leistungEntityId]?.ein ?? '',
                              aus: e.target.value,
                            },
                          }))} />
                      </V1Field>
                    </div>

                    {/* Der Vorschlag kommt aus dem Lichtplan des Zelts, nicht aus
                        einer Faustregel — und er wird angeboten, nicht gesetzt. */}
                    {stand.lichtplan && (
                      <p className="ac-hinweis ac-vorschlag">
                        Grow OS kennt für dieses Zelt den Lichtplan
                        {' '}<strong>{stand.lichtplan.name}</strong>:
                        {' '}{stand.lichtplan.ein}–{stand.lichtplan.aus}.
                        {' '}
                        <button
                          type="button"
                          className="ac-uebernehmen"
                          onClick={() => setZeiten((alt) => ({
                            ...alt,
                            [g.geraet.leistungEntityId]: {
                              ein: stand.lichtplan!.ein,
                              aus: stand.lichtplan!.aus,
                            },
                          }))}
                        >
                          Übernehmen
                        </button>
                      </p>
                    )}

                    <div className="v1-form-actions">
                      <V1Button
                        variant="primary"
                        disabled={arbeitet != null}
                        onClick={() => void zeitplanStellen(g.geraet)}
                        audit="ac-zeitplan-stellen"
                      >
                        {arbeitet === `${g.geraet.leistungEntityId}:zeit`
                          ? 'Stellt und prüft nach…'
                          : 'Zeitplan stellen'}
                      </V1Button>
                    </div>

                    <p className="ac-hinweis">
                      Es wird nacheinander geschrieben und jedes Mal nachgelesen —
                      die AC-Infinity-Cloud verwirft gleichzeitige Aufträge. Meist
                      ein paar Sekunden; hängt die Cloud, bis zu einer Minute.
                    </p>
                  </div>
                )}
              </V1Card>
            ))}
          </div>
        )}
      </V1Section>

      {/* ---------- Einrichten ---------- */}
      <V1Section
        title="Geräte eintragen"
        action={(
          <V1Button
            onClick={() => setEntwurf([...entwurf, { ...LEERES_GERAET }])}
          >
            + Gerät
          </V1Button>
        )}
      >
        <V1Card>
          <div data-audit="ac-test-form">
            {entwurf.length === 0 && (
              <p className="ac-hinweis">
                Die Entitäten findest du in Home Assistant unter deinem AC-Infinity-Gerät.
                Für die Stufe brauchst du „… Einschaltleistung" (beginnt mit <code>number.</code>);
                der aktive Modus (<code>select.</code>) ist freiwillig und wird nur angezeigt.
                Trägst du zusätzlich beide <code>time.</code>-Entitäten ein, kann Grow OS
                auch den Zeitplan stellen.
              </p>
            )}

            {entwurf.map((g, i) => (
              <div key={i} className="v1-form-grid ac-zeile">
                <V1Field label="Name" hint={'Frei wählbar — „LED Top", „Abluft".'}>
                  <input
                    value={g.name}
                    placeholder="LED Top"
                    onChange={(e) => setEntwurf(entwurf.map((x, k) => k === i ? { ...x, name: e.target.value } : x))} />
                </V1Field>
                <V1Field label="Stufe 0–10 (number)" hint={'Bei AC Infinity: „… Einschaltleistung".'}>
                  <input
                    value={g.leistungEntityId}
                    placeholder="number.led_top_eingeschaltete_leistung"
                    onChange={(e) => setEntwurf(entwurf.map((x, k) => k === i ? { ...x, leistungEntityId: e.target.value } : x))} />
                </V1Field>
                <V1Field label="Modus (select, freiwillig)" hint="Wird nur angezeigt, nicht gestellt.">
                  <input
                    value={g.modusEntityId ?? ''}
                    placeholder="select.led_top_aktiver_modus"
                    onChange={(e) => setEntwurf(entwurf.map((x, k) => k === i ? { ...x, modusEntityId: e.target.value || null } : x))} />
                </V1Field>
                <V1Field label="Ein-Zeit (time, freiwillig)" hint={'Bei AC Infinity: Geplante Ein-Zeit.'}>
                  <input
                    value={g.einZeitEntityId ?? ''}
                    placeholder="time.led_top_geplante_ein_zeit"
                    onChange={(e) => setEntwurf(entwurf.map((x, k) => k === i ? { ...x, einZeitEntityId: e.target.value || null } : x))} />
                </V1Field>
                <V1Field label="Aus-Zeit (time, freiwillig)" hint="Nur zusammen mit der Ein-Zeit.">
                  <input
                    value={g.ausZeitEntityId ?? ''}
                    placeholder="time.led_top_geplante_aus_zeit"
                    onChange={(e) => setEntwurf(entwurf.map((x, k) => k === i ? { ...x, ausZeitEntityId: e.target.value || null } : x))} />
                </V1Field>
                <div className="ac-weg">
                  <V1Button variant="danger" onClick={() => setEntwurf(entwurf.filter((_, k) => k !== i))}>
                    Entfernen
                  </V1Button>
                </div>
              </div>
            ))}

            <div className="v1-form-actions">
              <V1Button variant="primary" onClick={() => void speichern()} audit="ac-test-speichern">
                Speichern
              </V1Button>
            </div>
          </div>
        </V1Card>
      </V1Section>
    </V1Page>
  )
}
