import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch, ApiRequestError } from '../api'
import type { CuringJar } from '../features/curing/curing-typen'
import { faelligText, feuchteTon } from '../features/curing/curing-typen'
import { V1Alert, V1Card, V1Empty, V1Page, V1Section, V1Skeleton } from '../components/v1'
import { istUnlesbar, zahlOderNull } from '../zahlenfeld'
import '../features/curing/curing.css'

/**
 * Das Aushärten im Glas.
 *
 * Bis beta.42 endete die Begleitung beim Trockengewicht. Schlimmer: das
 * Speichern der Ernte setzt den Grow auf „beendet" — er verschwand aus der
 * Übersicht in genau dem Moment, in dem das Aushärten begann. Die 30 bis 60
 * Tage, die über die Qualität von Monaten Arbeit entscheiden, liefen ohne die
 * App.
 *
 * Diese Seite fragt deshalb nicht nach dem Grow-Status, sondern nach offenen
 * Gläsern. Ein Glas ist erst durch, wenn man es abschließt.
 */
function CuringPage() {
  const [jars, setJars] = useState<CuringJar[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [refresh, setRefresh] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    async function load() {
      try {
        const geladen = await apiFetch<CuringJar[]>('/api/curing/jars?auchKuerzlichFertige=true', { signal: controller.signal })
        if (!controller.signal.aborted) setJars(geladen)
      } catch (caught) {
        if (!controller.signal.aborted) {
          setError(caught instanceof ApiRequestError ? caught.message : 'Gläser konnten nicht geladen werden.')
        }
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }
    void load()
    return () => controller.abort()
  }, [refresh])

  /** Was ansteht, zuerst — überfällig vor heute vor dem Rest. */
  const sortiert = useMemo(() => {
    const rang = { Overdue: 0, Due: 1, Ok: 2, Finished: 3 } as const
    return [...jars].sort((a, b) => rang[a.duty.level] - rang[b.duty.level] || a.label.localeCompare(b.label))
  }, [jars])

  const faellig = sortiert.filter((jar) => jar.duty.level === 'Due' || jar.duty.level === 'Overdue').length

  return (
    <V1Page
      eyebrow="Pflanzen / Aushärten"
      title="Aushärten"
      subtitle="Was im Glas liegt und wann es gelüftet werden will. Gläser laufen weiter, auch wenn der Grow längst als beendet gilt — das ist der Sinn der Sache."
    >
      {error && <V1Alert message={error} tone="critical" />}
      {notice && <V1Alert message={notice} tone="ok" />}

      {loading ? (
        <V1Skeleton tiles={2} label="Lade Gläser" />
      ) : jars.length === 0 ? (
        <V1Section title="Noch kein Glas">
          <V1Card>
            <V1Empty
              title="Hier steht, was im Schrank aushärtet."
              text="Ein Glas legst du beim Grow an, dessen Ernte darin liegt — im Grow unter „Aushärten“."
            />
            <p className="cu-hint">
              Warum es diese Seite gibt: nach der Ernte gilt ein Grow als beendet, aber
              das Aushärten fängt genau dann erst an und dauert 30 bis 60 Tage. Ohne
              eigene Liste vergisst man die Gläser, die im Schrank stehen.
            </p>
          </V1Card>
        </V1Section>
      ) : (
        <V1Section title={faellig > 0 ? `${faellig} von ${jars.length} Gläsern sind dran` : `${jars.length} Gläser härten aus`}>
          <div className="co-grid" data-audit="curing-jars">
            {sortiert.map((jar) => (
              <JarCard
                key={jar.id}
                jar={jar}
                onDone={(text) => { setNotice(text); setRefresh((n) => n + 1) }}
                onError={setError}
              />
            ))}
          </div>
        </V1Section>
      )}
    </V1Page>
  )
}

/**
 * Ein Glas.
 *
 * Feuchte und Lüften stehen getrennt, weil sie es sind: wer lüftet, ohne
 * abzulesen, hat den Rhythmus gehalten und nichts gelernt; wer abliest, ohne zu
 * lüften, weiß Bescheid und hat nichts getan.
 */
function JarCard({ jar, onDone, onError }: {
  jar: CuringJar
  onDone: (text: string) => void
  onError: (text: string) => void
}) {
  const [feuchte, setFeuchte] = useState('')
  const [minuten, setMinuten] = useState('')
  const [busy, setBusy] = useState(false)

  async function eintragen() {
    // Bisher wurde Unlesbares still zu NaN und im JSON zu `null` — mit
    // Erfolgsmeldung „eingetragen".
    if (istUnlesbar(feuchte) || istUnlesbar(minuten)) {
      onError(`${jar.label}: Feuchte oder Minuten sind keine Zahl. Bitte korrigieren oder das Feld leeren.`)
      return
    }
    setBusy(true)
    try {
      await apiFetch(`/api/curing/jars/${jar.id}/readings`, {
        method: 'POST',
        body: JSON.stringify({
          humidityPercent: zahlOderNull(feuchte),
          burpedMinutes: zahlOderNull(minuten),
        }),
      })
      setFeuchte('')
      setMinuten('')
      onDone(`${jar.label}: eingetragen.`)
    } catch (caught) {
      onError(caught instanceof ApiRequestError ? caught.message : 'Eintrag fehlgeschlagen.')
    } finally {
      setBusy(false)
    }
  }

  async function abschliessen() {
    setBusy(true)
    try {
      await apiFetch(`/api/curing/jars/${jar.id}/finish`, { method: 'POST' })
      onDone(`${jar.label} ist fertig ausgehärtet.`)
    } catch (caught) {
      onError(caught instanceof ApiRequestError ? caught.message : 'Abschließen fehlgeschlagen.')
    } finally {
      setBusy(false)
    }
  }

  /**
   * „Fertig" war die einzige Handlung ohne Rückweg — ein Fehlgriff und das Glas
   * war aus der Liste, mitsamt seinem Lüft-Rhythmus. Aushärten dauert Wochen;
   * in der Zeit klickt man sich auch mal vertan.
   */
  async function wiederOeffnen() {
    setBusy(true)
    try {
      await apiFetch(`/api/curing/jars/${jar.id}/reopen`, { method: 'POST' })
      onDone(`${jar.label} härtet wieder aus.`)
    } catch (caught) {
      onError(caught instanceof ApiRequestError ? caught.message : 'Wieder öffnen fehlgeschlagen.')
    } finally {
      setBusy(false)
    }
  }

  async function loeschen() {
    // Ein Glas, das man versehentlich angelegt hat, muss auch wieder weg
    // koennen — sonst steht es fuer immer in der Liste und meldet Termine.
    if (!window.confirm(`„${jar.label}" wirklich löschen? Die Ablesungen dazu gehen mit verloren.`)) return
    setBusy(true)
    try {
      await apiFetch(`/api/curing/jars/${jar.id}`, { method: 'DELETE' })
      onDone(`${jar.label} gelöscht.`)
    } catch (caught) {
      onError(caught instanceof ApiRequestError ? caught.message : 'Löschen fehlgeschlagen.')
    } finally {
      setBusy(false)
    }
  }

  const ampel = jar.latestHumidity ? feuchteTon(jar.latestHumidity.level) : null
  const dringend = jar.duty.level === 'Overdue' || jar.duty.level === 'Due'

  return (
    <article className={`cu-jar${dringend ? ' is-due' : ''}`} data-audit="curing-jar">
      <div className="cu-head">
        <strong>{jar.label}</strong>
        <span className={`ls-pill${dringend ? '' : ' is-plan'}`}>{faelligText(jar.duty)}</span>
      </div>

      <div className="cu-facts">
        <Link to={`/grows/${jar.growId}`}>{jar.growName}</Link>
        {jar.strainName && <> · {jar.strainName}</>}
        {jar.weightG != null && <> · {jar.weightG.toLocaleString('de-DE')} g</>}
        {jar.hasHumidityPack && <> · mit Feuchtigkeitsregler</>}
        <> · Tag {jar.duty.dayInCure}</>
      </div>

      {jar.latestHumidity ? (
        <div className={`cu-ampel is-${ampel}`}>
          {/* Das Alter steht in einer eigenen Zeile ueber dem Befund. Neben ihm
              rutschte es bei laengeren Saetzen mitten in den Text — und ein
              Zeitstempel, der wie ein Satzteil aussieht, liest sich als einer. */}
          <span className="cu-alter">Abgelesen {alterText(jar.latestHumidity.readAtUtc)}</span>
          <strong className="cu-befund">{jar.latestHumidity.summary}</strong>
          <p>{jar.latestHumidity.action}</p>
          <small>Quelle: {jar.latestHumidity.ratingSource}</small>
        </div>
      ) : (
        // Kein „0 %": nie gemessen ist etwas anderes als trocken.
        <p className="cu-hint">Noch keine Feuchte abgelesen.</p>
      )}

      <p className="cu-rhythmus">{jar.duty.text}</p>

      <div className="cu-form">
        <label>
          <span>Feuchte %</span>
          <input
            type="text"
            inputMode="decimal"
            value={feuchte}
            onChange={(event) => setFeuchte(event.target.value)}
            placeholder="61"
            aria-label={`Feuchte im ${jar.label}`}
          />
        </label>
        <label>
          <span>Gelüftet min</span>
          <input
            type="text"
            inputMode="numeric"
            value={minuten}
            onChange={(event) => setMinuten(event.target.value)}
            placeholder="5"
            aria-label={`Lüftdauer für ${jar.label}`}
          />
        </label>
        <button
          type="button"
          className="ls-btn is-small is-primary"
          disabled={busy || (!feuchte.trim() && !minuten.trim())}
          onClick={() => void eintragen()}
        >
          Eintragen
        </button>
        {/* `== null` statt `=== null`: leere Felder fehlen im JSON ganz, es
            kommt `undefined` an. Sonst haette jedes offene Glas den
            Knopf „Wieder öffnen" getragen statt „Fertig". */}
        {jar.finishedAtUtc == null ? (
          <button type="button" className="ls-btn is-small" disabled={busy} onClick={() => void abschliessen()}>
            Fertig
          </button>
        ) : (
          <button type="button" className="ls-btn is-small" disabled={busy} onClick={() => void wiederOeffnen()}>
            Wieder öffnen
          </button>
        )}
        <button type="button" className="ls-btn is-small is-ghost" disabled={busy} onClick={() => void loeschen()}>
          Löschen
        </button>
      </div>
    </article>
  )
}

/** „vor 2 Stunden", „gestern" — wie alt der Wert ist, gehört an den Wert. */
function alterText(iso: string): string {
  const stunden = Math.floor((Date.now() - new Date(iso).getTime()) / 3_600_000)
  if (stunden < 1) return 'gerade eben'
  if (stunden < 24) return `vor ${stunden} h`
  const tage = Math.floor(stunden / 24)
  return tage === 1 ? 'gestern' : `vor ${tage} Tagen`
}

export default CuringPage
