import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch, formatApiError } from '../api'
import { V1Alert, V1Button, V1Card, V1Empty, V1Page, V1Tabs } from '../components/v1'
import { GrowScopePicker } from '../features/grow-scope/GrowScopePicker'
import { useSelectedGrow } from '../features/grow-scope/useSelectedGrow'
import { EreignisZeile } from '../features/tagebuch/EreignisZeile'
import { Sensorkurven } from '../features/tagebuch/Sensorkurven'
import { FILTER, markenFuer, passtZumFilter, tagTitel, type Filter } from '../features/tagebuch/tagebuch-modell'
import type { TagebuchSeiteDto, TagebuchTagDto } from '../types'
import '../features/grow-detail/journal-stream.css'
import '../features/live/live-screen.css'
import '../features/tagebuch/tagebuch.css'

/**
 * Das Grow-Tagebuch (A-006): alles rund um einen Grow an einer Stelle, nach
 * Tagen — Messwerte von Hand und vom Sensor, Wasserwechsel, Nachfüllen,
 * Dosierungen, Notizen, Fotos und Sprünge, zu denen nichts eingetragen ist.
 *
 * Löst „Messungen" und „Journal & Fotos" als Hauptweg im Menü ab; beide
 * Seiten bleiben erreichbar (Links oben), weil dort Tabelle, Vergleich und
 * „+ Eintrag" wohnen.
 */
export default function TagebuchPage() {
  const { grows, growId, setGrowId, loading, error } = useSelectedGrow()
  const [tage, setTage] = useState<TagebuchTagDto[]>([])
  const [aeltereAb, setAeltereAb] = useState<string | null>(null)
  const [laedt, setLaedt] = useState(true)
  const [fehler, setFehler] = useState<string | null>(null)
  const [filter, setFilter] = useState<Filter>('alles')
  // Beim Öffnen ist jeder Tag zu (Bru, 05.10.2026) — das Aufklappen wird
  // bewusst NICHT gespeichert, anders als auf der Live-Seite.
  const [offen, setOffen] = useState<Set<string>>(() => new Set())

  /** Ab heute so viele Tage laden, wie schon da sind — nach einer Änderung bleibt die Liste so lang. */
  const holen = useCallback((anzahl: number, signal?: AbortSignal) => {
    if (!growId) return Promise.resolve()
    return apiFetch<TagebuchSeiteDto>(`/api/grows/${growId}/tagebuch?tage=${Math.min(31, Math.max(7, anzahl))}`, { signal })
      .then((seite) => {
        if (signal?.aborted) return
        setTage(seite.tage)
        setAeltereAb(seite.aeltereAb)
        setFehler(null)
      })
      .catch((caught) => { if (!signal?.aborted) setFehler(formatApiError(caught, 'Das Tagebuch konnte nicht geladen werden.')) })
      .finally(() => { if (!signal?.aborted) setLaedt(false) })
  }, [growId])

  useEffect(() => {
    const controller = new AbortController()
    void holen(7, controller.signal)
    return () => controller.abort()
  }, [holen])

  async function aeltereLaden() {
    if (!growId || !aeltereAb) return
    setLaedt(true)
    try {
      const seite = await apiFetch<TagebuchSeiteDto>(`/api/grows/${growId}/tagebuch?bis=${aeltereAb}&tage=7`)
      setTage((alt) => [...alt, ...seite.tage.filter((t) => !alt.some((a) => a.datum === t.datum))])
      setAeltereAb(seite.aeltereAb)
    } catch (caught) {
      setFehler(formatApiError(caught, 'Ältere Tage konnten nicht geladen werden.'))
    } finally {
      setLaedt(false)
    }
  }

  const neuLaden = useCallback(() => holen(tage.length), [holen, tage.length])
  const alleOffen = tage.length > 0 && tage.every((t) => offen.has(t.datum))
  const umschalten = (datum: string) => setOffen((alt) => {
    const neu = new Set(alt)
    if (neu.has(datum)) neu.delete(datum)
    else neu.add(datum)
    return neu
  })

  /** Ein anderer Grow: alles von vorn, alle Tage wieder zu. */
  function growWechseln(id: string) {
    setTage([])
    setOffen(new Set())
    setAeltereAb(null)
    setLaedt(true)
    setGrowId(id)
  }

  const scope = growId ? `?growId=${growId}` : ''
  const sichtbareTage = useMemo(() => tage.map((tag) => ({ tag, sichtbar: tag.ereignisse.filter((e) => passtZumFilter(e, filter)) })), [tage, filter])

  return (
    <V1Page
      eyebrow="Pflanzen"
      title="Tagebuch"
      subtitle="Alles rund um den Grow an einer Stelle: Messwerte von Hand und vom Sensor, Wasserwechsel, Nachfüllen, Notizen und Fotos. Neue Einträge über das + oben."
      action={<GrowScopePicker grows={grows} growId={growId} onChange={growWechseln} />}
    >
      {error && <V1Alert message={error} tone="critical" />}
      {loading ? (
        <V1Card>Lädt…</V1Card>
      ) : grows.length === 0 ? (
        <V1Empty title="Kein aktiver Grow" text="Lege zuerst einen Grow an, dann erscheint sein Tagebuch hier." />
      ) : (
        <>
          <nav className="tb-wege" aria-label="Weitere Ansichten">
            <Link className="ls-btn is-small" to={`/messungen${scope}`}>Messungen als Tabelle</Link>
            <Link className="ls-btn is-small" to={`/journal${scope}`}>Journal &amp; Fotos</Link>
          </nav>
          <div className="tb-leiste">
            <V1Tabs label="Filter" active={filter} onChange={setFilter} items={FILTER.map((f) => ({ value: f.value, label: f.label, audit: `tagebuch-filter-${f.value}` }))} />
            <button
              type="button"
              role="switch"
              aria-checked={alleOffen}
              className="tb-schalter"
              data-audit="tagebuch-alle-kurven"
              disabled={tage.length === 0}
              onClick={() => setOffen(alleOffen ? new Set() : new Set(tage.map((t) => t.datum)))}
            >
              <span className="tb-schalter-bahn" aria-hidden="true"><span /></span>
              Alle Kurven
            </button>
          </div>

          {fehler && <V1Alert message={fehler} tone="critical" />}
          {!laedt && !fehler && tage.length === 0 && (
            <V1Empty title="Noch nichts eingetragen" text="Sobald gemessen, gewechselt oder notiert wird, steht es hier — nach Tagen." />
          )}

          {growId && sichtbareTage.map(({ tag, sichtbar }) => (
            <section key={tag.datum} className="tb-tag" data-audit="tagebuch-tag" data-datum={tag.datum}>
              <div className="tb-tag-kopf">
                <strong>{tagTitel(tag.datum, tag.wochentag)}</strong>
                {tag.phase && <span>{tag.phase}</span>}
              </div>
              {filter !== 'notizen' && (
                <Sensorkurven
                  growId={growId}
                  datum={tag.datum}
                  offen={offen.has(tag.datum)}
                  onUmschalten={() => umschalten(tag.datum)}
                  auffaellig={tag.auffaellig}
                  marken={markenFuer(tag.ereignisse)}
                />
              )}
              <div className="js-stream">
                {sichtbar.length === 0
                  ? <div className="js-row"><div className="js-when" /><div className="js-content"><p>Nichts in diesem Filter.</p></div></div>
                  : sichtbar.map((e) => <EreignisZeile key={e.schluessel} growId={growId} e={e} onGeaendert={neuLaden} />)}
              </div>
            </section>
          ))}

          {laedt && <V1Card>Lädt…</V1Card>}
          {aeltereAb && !laedt && (
            <div className="tb-mehr">
              <V1Button audit="tagebuch-aeltere" onClick={() => void aeltereLaden()}>Ältere Tage laden</V1Button>
            </div>
          )}
        </>
      )}
    </V1Page>
  )
}
