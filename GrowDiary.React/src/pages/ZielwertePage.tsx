import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch } from '../api'
import { classNames } from '../utils'
import { istHandgesetzt } from '../features/wochenplan/uebergabe-zustand'
import { V1Alert, V1Section, V1Skeleton } from '../components/v1'
import { WertBlatt } from '../features/zielwerte/WertBlatt'
import type { AlarmRegel, PlanFeld } from '../features/zielwerte/wert-blatt'
import '../features/zielwerte/zielwerte.css'
import '../features/wochenplan/wochenplan.css'

/**
 * Fork AI: die Seite „Zielwerte" — was gilt, woher es kommt, wo man es ändert.
 *
 * Ein Ziel entsteht aus vier Quellen, und jede spätere sticht die früheren. Auf
 * der Kachel steht davon nur das Ergebnis; „dein Wert" sagt weder, WER die Zahl
 * gesetzt hat, noch dass die Wochenspalte daneben gerade wirkungslos ist. Wer
 * etwas ändern wollte, suchte an drei Stellen.
 *
 * Drei Ebenen, absichtlich in dieser Reihenfolge:
 *   1. Karten mit Band  — wie steht es gerade (und WIE WEIT ist es daneben)
 *   2. Gruppen          — wo stelle ich das ein
 *   3. Herkunftsblatt   — warum gilt ausgerechnet das
 *
 * Kein eigener Rechenweg: alles aus `/api/zielwerte`, das dieselbe Auflösung
 * liest wie Kacheln und Alarme.
 */

type Stufe = {
  name: string
  hinweis: string | null
  wert: string | null
  gilt: boolean
  weg: boolean
}

type Wert = {
  key: string
  name: string
  einheit: string | null
  ist: string
  istZahl: number | null
  min: number | null
  max: number | null
  band: string | null
  quelle: string
  quelleZusatz: string | null
  lage: string
  alarm: string | null
  kette: Stufe[]
  // Fork AI (Grow-Plan, Schritt 2)
  regel: AlarmRegel | null
  alarmVon: number | null
  alarmBis: number | null
  meldet: boolean
  planFelder: PlanFeld[] | null
}

type Gruppe = {
  titel: string
  route: string
  routeText: string
  zeilen: { links: string; rechts: string }[]
  hinweis: string
}

type Uebergabe = { rolle: string; name: string; wert: string; zustand: string }

type Zielwerte = {
  growId: number | null
  growName: string | null
  phase: string | null
  woche: string | null
  programm: string | null
  hinweise: string[]
  werte: Wert[]
  gruppen: Gruppe[]
  uebergabe: Uebergabe[]
  letzteUebergabe: string | null
  zeltId: number | null
  spalteId: string | null
  eigenerPlan: boolean
}

/**
 * Der Balken: Zielband als Zone, Ist-Wert als Nadel.
 *
 * Die Skala ist das Band plus die Hälfte seiner Breite als Rand — so bleibt ein
 * Ausreißer sichtbar, statt am Rand zu kleben, und ein Wert weit außerhalb
 * (CO₂ 595 gegen 1200–1400) sieht auch weit außerhalb aus.
 */
function Balken({ wert }: { wert: Wert }) {
  const { min, max, istZahl } = wert
  if (min === null || max === null || istZahl === null) return null

  const breite = max - min
  const rand = breite > 0 ? breite / 2 : Math.abs(max) * 0.1 || 1
  const von = Math.min(min - rand, istZahl)
  const bis = Math.max(max + rand, istZahl)
  const spanne = bis - von
  if (spanne <= 0) return null

  const prozent = (x: number) => `${((x - von) / spanne) * 100}%`

  return (
    <div className="zw-balken" aria-hidden="true">
      <div className="zw-zone" style={{ left: prozent(min), right: `${100 - ((max - von) / spanne) * 100}%` }} />
      <div className={classNames('zw-nadel', `ist-${wert.lage.replace('ü', 'ue')}`)} style={{ left: prozent(istZahl) }} />
    </div>
  )
}

function QuellePill({ wert }: { wert: Wert }) {
  const ton = wert.quelle === 'Fest' ? 'ist-fest' : wert.quelle === 'Plan' ? 'ist-plan' : 'ist-profil'
  return (
    <span className={classNames('zw-pill', ton)}>{wert.quelle}</span>
  )
}

/** Was die Glocke unten auf der Karte sagt. */
function Glocke({ wert }: { wert: Wert }) {
  // Fork AI (forkai.133): „außerhalb/überwacht" statt „meldet/scharf" — Grenzwerte, nicht Alarm.
  if (wert.meldet) return <span className="zw-glocke ist-meldet">● außerhalb</span>
  if (wert.regel?.aktiv) return <span className="zw-glocke ist-scharf">● überwacht</span>
  return <span className="zw-glocke">○ nicht überwacht</span>
}

function ZielwertePage() {
  const [daten, setDaten] = useState<Zielwerte | null>(null)
  const [offen, setOffen] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [meldung, setMeldung] = useState<string | null>(null)
  const [runde, setRunde] = useState(0)

  useEffect(() => {
    async function laden() {
      try {
        setDaten(await apiFetch<Zielwerte>('/api/zielwerte'))
        setError(null)
      } catch {
        setError('Die Zielwerte konnten nicht geladen werden.')
      } finally {
        setLoading(false)
      }
    }
    void laden()
  }, [runde])

  async function freigeben(rolle: string) {
    try {
      await apiFetch(`/api/wochenplan/freigeben/${encodeURIComponent(rolle)}`, { method: 'POST' })
      setMeldung('Freigegeben — der Plan führt den Wert wieder.')
    } catch {
      setMeldung('Der Helfer konnte nicht freigegeben werden.')
    }
    setRunde((r) => r + 1)
  }

  if (loading) return <V1Skeleton rows={6} label="Lade Zielwerte" />

  const kopf = daten?.growName
    ? [daten.growName, daten.phase, daten.woche].filter(Boolean).join(' · ')
    : null

  const melden = daten?.werte.filter((w) => w.meldet) ?? []
  const offenerWert = daten?.werte.find((w) => w.key === offen) ?? null

  return (
    <>
      {error && <V1Alert message={error} tone="critical" />}

      {!error && (!daten || daten.werte.length === 0) && (
        <V1Alert
          message="Kein laufender Durchgang mit Zelt. Zielwerte gehören zu einem Grow — ohne ihn gibt es nichts aufzulösen."
          tone="neutral"
        />
      )}

      {daten && daten.werte.length > 0 && (
        <>
          {/* Zuerst die Stellen, an denen etwas den Plan aussticht. Wer sie
              kennt, liest die Karten darunter richtig. */}
          {meldung && <V1Alert message={meldung} tone="neutral" />}

          {/* Fork AI (Schritt 2): was gerade außerhalb der Alarmgrenzen liegt —
              die Frage, mit der man die Seite meist öffnet. */}
          {melden.length > 0 && (
            <V1Alert
              tone="critical"
              message={`${melden.length === 1 ? '1 Wert' : `${melden.length} Werte`} gerade außerhalb: ${melden
                .map((w) => `${w.name} ${w.ist}${w.einheit ? ` ${w.einheit}` : ''}`)
                .join(' · ')}`}
            />
          )}

          {daten.hinweise.map((hinweis) => (
            <V1Alert key={hinweis} message={hinweis} tone="warn" />
          ))}

          <V1Section title={kopf ?? 'Werte'}>
            <div className="zw-karten" data-audit="zielwerte-karten">
              {daten.werte.map((wert) => (
                <article key={wert.key} className={classNames('zw-karte', wert.meldet && 'ist-meldet')}>
                  <button
                    type="button"
                    className="zw-kopf"
                    onClick={() => { setMeldung(null); setOffen(wert.key) }}
                    aria-haspopup="dialog"
                    data-audit="zielwert-karte"
                  >
                    <span className="zw-name">{wert.name}</span>
                    <span className={classNames('zw-ist', `ist-${wert.lage.replace('ü', 'ue')}`)}>
                      {wert.ist}
                      {wert.einheit && <small> {wert.einheit}</small>}
                    </span>
                  </button>

                  <Balken wert={wert} />

                  <div className="zw-fuss">
                    <span>
                      {wert.band ? `${wert.band}${wert.einheit ? ` ${wert.einheit}` : ''}` : 'kein Ziel'}
                    </span>
                    <span className="zw-herkunft">
                      <QuellePill wert={wert} />
                      {wert.quelleZusatz}
                    </span>
                  </div>

                  {/* Der Alarm steht bewusst AUF der Karte und nicht auf einer
                      eigenen Seite: Ziel und Meldeschwelle sind zwei Zahlen zu
                      einer Sache, und sie auseinanderzuziehen war der Anfang
                      der Verwirrung. */}
                  <div className="zw-alarm zw-fuss">
                    <span>{wert.alarm ? `Grenzwerte ${wert.alarm}` : 'keine Grenzwerte'}</span>
                    <Glocke wert={wert} />
                  </div>
                </article>
              ))}
            </div>
          </V1Section>

          {offenerWert && daten.growId != null && daten.zeltId != null && (
            <WertBlatt
              key={offenerWert.key}
              wert={offenerWert}
              growId={daten.growId}
              zeltId={daten.zeltId}
              spalteId={daten.spalteId}
              woche={daten.woche}
              uebergabe={daten.uebergabe}
              onClose={() => setOffen(null)}
              onGespeichert={(text) => { setOffen(null); setMeldung(text); setRunde((r) => r + 1) }}
            />
          )}

          <V1Section title="Wo stelle ich das ein">
            {daten.gruppen.map((gruppe) => (
              <div key={gruppe.titel} className="zw-gruppe">
                <div className="zw-gruppe-k">
                  <b>{gruppe.titel}</b>
                  {/* Link, nicht <a href>: die App laeuft unter dem
                      Ingress-Praefix von Home Assistant, und ein roher href
                      verlaesst diesen Grundpfad — die Seite endete in einer
                      Fehlermeldung statt bei den Sollwerten. */}
                  <Link to={gruppe.route}>{gruppe.routeText}</Link>
                </div>
                {gruppe.zeilen.map((zeile) => (
                  <div key={zeile.links} className="zw-zeile">
                    <span>{zeile.links}</span>
                    <span className="zw-zeile-r">{zeile.rechts}</span>
                  </div>
                ))}
                <p className="zw-gruppe-h">{gruppe.hinweis}</p>
              </div>
            ))}
          </V1Section>

          {daten.uebergabe.length > 0 && (
            <V1Section title="Übergabe an Home Assistant">
              <div className="zw-gruppe">
                {daten.uebergabe.map((u) => (
                  <div key={u.rolle} className="zw-zeile">
                    <span>{u.name}</span>
                    <span className="zw-zeile-r">
                      {u.wert}
                      {istHandgesetzt(u.zustand) ? (
                        /* Fork AI (forkai.125): kam aus dem früheren Wochenplan.
                           Von Hand verstellt lässt der Plan den Helfer in Ruhe,
                           bis er hier freigegeben wird. */
                        <span className="zw-zustand">
                          <button type="button" className="wp-frei" data-audit="uebergabe-freigeben" onClick={() => void freigeben(u.rolle)}>
                            von dir gesetzt — freigeben
                          </button>
                        </span>
                      ) : (
                        <span className="zw-zustand">{u.zustand}</span>
                      )}
                    </span>
                  </div>
                ))}
                <p className="zw-gruppe-h">
                  {daten.letzteUebergabe
                    ? `Zuletzt übergeben: ${new Date(daten.letzteUebergabe).toLocaleString('de-DE')}`
                    : 'Noch nichts übergeben — der erste Lauf merkt sich nur den Ist-Zustand.'}
                </p>
              </div>
            </V1Section>
          )}
        </>
      )}
    </>
  )
}

export default ZielwertePage
