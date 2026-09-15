import { useEffect, useMemo, useRef, useState, type PointerEvent } from 'react'
import { apiFetch, formatApiError } from '../../api'
import { classNames } from '../../utils'
import { V1Alert, V1Badge, V1Button } from '../../components/v1'
import {
  GRUPPEN,
  aenderungen,
  alsText,
  aufPlan,
  feldText,
  fehler,
  punkt,
  schluessel,
  startIndex,
  weichtAb,
  type Entwurf,
  type Wochenwerte,
} from './wochenwerte-bearbeiten'

type Gespeichert = { werte: Wochenwerte; uebergeben: number; hinweis: string | null }

/** Ab wie viel Pixel waagerechter Bewegung ein Wischen als Blättern zählt. */
const WISCH_SCHWELLE = 50

/**
 * Fork AI (F-004): Wochenwerte bearbeiten, Mockup-Variante F.
 *
 * Immer genau eine Woche im Bild — dann passen alle neun Werte gleichzeitig
 * aufs Handy, ohne Auf- und Zuklappen. Blättern mit Pfeilen oder Wischen; die
 * Punktreihe zeigt, wo man steht und wo etwas vom Plan abweicht. Änderungen
 * sammelt der Balken unten über alle Wochen und speichert sie zusammen.
 */
export function WochenwerteBearbeiten({ growId, onFertig }: { growId: number; onFertig: (gespeichert: boolean) => void }) {
  const [werte, setWerte] = useState<Wochenwerte | null>(null)
  const [index, setIndex] = useState(0)
  const [entwurf, setEntwurf] = useState<Entwurf>({})
  const [laden, setLaden] = useState(true)
  const [speichert, setSpeichert] = useState(false)
  const [meldung, setMeldung] = useState<{ text: string; ton: 'ok' | 'critical' | 'neutral' } | null>(null)
  const [gespeichert, setGespeichert] = useState(false)
  const wischStart = useRef<{ x: number; y: number } | null>(null)

  useEffect(() => {
    let aktiv = true
    apiFetch<Wochenwerte>(`/api/wochenplan/werte/${growId}`)
      .then((daten) => {
        if (!aktiv) return
        setWerte(daten)
        setIndex(startIndex(daten))
      })
      .catch((caught) => aktiv && setMeldung({ text: formatApiError(caught, 'Die Wochenwerte konnten nicht geladen werden.'), ton: 'critical' }))
      .finally(() => aktiv && setLaden(false))
    return () => {
      aktiv = false
    }
  }, [growId])

  const offen = useMemo(() => (werte ? aenderungen(werte, entwurf) : []), [werte, entwurf])
  const probleme = useMemo(() => (werte ? fehler(werte, entwurf) : []), [werte, entwurf])

  if (laden) return <p className="wp-leise">Lade Wochenwerte …</p>
  if (!werte || werte.spalten.length === 0) {
    return (
      <>
        {meldung && <V1Alert message={meldung.text} tone={meldung.ton} />}
        <V1Button onClick={() => onFertig(false)}>Zurück</V1Button>
      </>
    )
  }

  const spalte = werte.spalten[index]
  const letzte = werte.spalten.length - 1

  function blaettern(richtung: -1 | 1) {
    setIndex((i) => Math.min(letzte, Math.max(0, i + richtung)))
  }

  function tippen(feld: string, text: string) {
    setEntwurf((alt) => ({ ...alt, [schluessel(spalte.id, feld)]: text }))
    setMeldung(null)
  }

  function wischBeginn(event: PointerEvent<HTMLDivElement>) {
    // Knöpfe blättern selbst. Eingabefelder nur bei der Maus ausnehmen (dort
    // markiert Ziehen Text) — am Handy besteht das Blatt fast nur aus Feldern,
    // und ein Wischen, das auf einem Feld beginnt, soll trotzdem blättern.
    const ziel = event.target as HTMLElement
    if (ziel.closest('button')) return
    if (event.pointerType === 'mouse' && ziel.closest('input')) return
    wischStart.current = { x: event.clientX, y: event.clientY }
    // Ohne Capture kommt das Loslassen nicht an, sobald der Finger (oder die
    // Maus) das Blatt beim Wischen verlässt — und genau das ist die Regel.
    event.currentTarget.setPointerCapture?.(event.pointerId)
  }

  function wischEnde(event: PointerEvent<HTMLDivElement>) {
    const start = wischStart.current
    wischStart.current = null
    if (!start) return
    const dx = event.clientX - start.x
    const dy = event.clientY - start.y
    // Nur eindeutig waagerecht — sonst blättert schon das Scrollen.
    if (Math.abs(dx) < WISCH_SCHWELLE || Math.abs(dx) < Math.abs(dy) * 1.5) return
    blaettern(dx < 0 ? 1 : -1)
  }

  function fertig() {
    if (offen.length > 0 && !window.confirm(`${offen.length} ungespeicherte Änderung(en) verwerfen?`)) return
    onFertig(gespeichert)
  }

  async function speichern() {
    if (!werte || offen.length === 0 || probleme.length > 0) return
    setSpeichert(true)
    setMeldung(null)
    try {
      const antwort = await apiFetch<Gespeichert>(`/api/wochenplan/werte/${growId}`, {
        method: 'POST',
        body: JSON.stringify({ aenderungen: offen }),
      })
      setWerte(antwort.werte)
      setEntwurf({})
      setGespeichert(true)
      setMeldung(
        antwort.hinweis
          ? { text: antwort.hinweis, ton: 'neutral' }
          : {
              text: antwort.uebergeben > 0
                ? `Gespeichert. ${antwort.uebergeben} Wert(e) an Home Assistant übergeben.`
                : 'Gespeichert. In Home Assistant war nichts zu ändern.',
              ton: 'ok',
            },
      )
    } catch (caught) {
      setMeldung({ text: formatApiError(caught, 'Die Wochenwerte konnten nicht gespeichert werden.'), ton: 'critical' })
    } finally {
      setSpeichert(false)
    }
  }

  return (
    <div className="wp-bearbeiten" data-audit="wochenwerte-bearbeiten">
      {werte.andereGrows > 0 && (
        <V1Alert
          tone="neutral"
          message={`Die Werte gelten für das Programm ${werte.programmName} — also auch für ${werte.andereGrows} weitere(n) laufende(n) Grow(s).`}
        />
      )}

      <div className="wp-blatt" onPointerDown={wischBeginn} onPointerUp={wischEnde} onPointerCancel={() => (wischStart.current = null)}>
        <div className="wp-blatt-kopf">
          <button type="button" className="wp-pfeil" aria-label="Vorige Woche" disabled={index === 0} onClick={() => blaettern(-1)}>
            ‹
          </button>
          <div className="wp-blatt-titel">
            <b>{spalte.label}</b>
            {spalte.istJetzt && <V1Badge tone="accent">läuft</V1Badge>}
          </div>
          <button type="button" className="wp-pfeil" aria-label="Nächste Woche" disabled={index === letzte} onClick={() => blaettern(1)}>
            ›
          </button>
        </div>

        <div className="wp-gitter wp-gitter-edit">
          {GRUPPEN.map((gruppe) => {
            const felder = gruppe.felder
              .map((name) => spalte.felder.find((f) => f.feld === name))
              .filter((f) => f !== undefined)
            if (felder.length === 0) return null
            const eigen = felder.some((f) => weichtAb(entwurf, spalte.id, f))
            const plan = felder.map((f) => alsText(f.plan) || '–').join('–')
            return (
              <div key={gruppe.titel} className={classNames('wp-zelle', eigen && 'ist-eigen')}>
                <div className="wp-zelle-n">
                  {gruppe.titel}
                  {felder[0].einheit && <span className="wp-einheit"> {felder[0].einheit}</span>}
                </div>
                <div className="wp-eingaben">
                  {felder.map((f, i) => (
                    <span key={f.feld} className="wp-eingabe-paar">
                      {i > 0 && <span className="wp-leise">–</span>}
                      <input
                        className="wp-eingabe"
                        type="text"
                        inputMode="decimal"
                        aria-label={`${spalte.label}: ${f.bezeichnung}`}
                        data-audit={`wochenwert-${f.feld}`}
                        value={feldText(entwurf, spalte.id, f)}
                        placeholder={alsText(f.plan)}
                        onChange={(e) => tippen(f.feld, e.target.value)}
                      />
                    </span>
                  ))}
                </div>
                {eigen && (
                  <div className="wp-plan">
                    Plan {plan} ·{' '}
                    <button type="button" className="wp-plan-zurueck" onClick={() => setEntwurf((alt) => aufPlan(alt, spalte, gruppe.felder))}>
                      zurück
                    </button>
                  </div>
                )}
              </div>
            )
          })}
        </div>
      </div>

      <div className="wp-punkte" role="tablist" aria-label="Wochen">
        {werte.spalten.map((s, i) => {
          const zustand = punkt(werte, entwurf, s)
          return (
            <button
              key={s.id}
              type="button"
              role="tab"
              aria-selected={i === index}
              aria-label={s.label}
              title={s.label}
              className={classNames(
                'wp-punkt',
                i === index && 'ist-aktiv',
                s.istJetzt && 'ist-jetzt',
                zustand.eigen && 'ist-eigen',
                zustand.offen && 'ist-offen',
              )}
              onClick={() => setIndex(i)}
            />
          )
        })}
      </div>

      {probleme.length > 0 && <V1Alert tone="critical" message={probleme.join(' ')} />}
      {meldung && <V1Alert tone={meldung.ton} message={meldung.text} />}

      <div className="wp-speicherbalken" data-audit="wochenwerte-speicherbalken">
        <span className="wp-speicherbalken-zahl">
          {offen.length === 0 ? 'Keine Änderungen' : `${offen.length} Änderung${offen.length === 1 ? '' : 'en'}`}
        </span>
        <V1Button variant="ghost" onClick={offen.length > 0 ? () => setEntwurf({}) : fertig} disabled={speichert}>
          {offen.length > 0 ? 'Verwerfen' : 'Fertig'}
        </V1Button>
        <V1Button
          variant="primary"
          onClick={() => void speichern()}
          disabled={speichert || offen.length === 0 || probleme.length > 0}
          audit="wochenwerte-speichern"
        >
          {speichert ? 'Speichert …' : 'Speichern'}
        </V1Button>
      </div>
    </div>
  )
}
