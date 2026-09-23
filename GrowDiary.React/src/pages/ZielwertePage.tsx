import { Fragment, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch } from '../api'
import { classNames } from '../utils'
import { V1Alert, V1Section, V1Skeleton } from '../components/v1'
import { WertBlatt } from '../features/zielwerte/WertBlatt'
import type { AlarmRegel, PlanFeld } from '../features/zielwerte/wert-blatt'
import '../features/zielwerte/zielwerte.css'
import { UebergabeAbschnitt } from '../features/zielwerte/UebergabeAbschnitt'
import { warnZeile } from '../features/zielwerte/warn-zeilen'
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
  meldetSeitUtc?: string | null
}

type Gruppe = {
  titel: string
  route: string
  routeText: string
  zeilen: { links: string; rechts: string; zusatz?: string | null; bereich?: string | null }[]
  hinweis: string
}

type Uebergabe = { rolle: string; name: string; entityId?: string | null; wert: string; zustand: string }

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
  // Fork AI (forkai.143, F-039, Mockup-Variante A): schmaler Balken, Ziel kräftig
  // grün, Meldegrenzen als kleine gelbe Striche, Messwert als farbiger Zeiger —
  // und die Zahlen direkt darunter.
  const { min, max, istZahl } = wert
  if (min === null || max === null || istZahl === null) return null
  const gv = wert.alarmVon
  const gb = wert.alarmBis

  const werte = [min, max, istZahl, gv, gb].filter((x): x is number => x != null)
  const lo = Math.min(...werte)
  const hi = Math.max(...werte)
  const rand = (hi - lo) * 0.18 || Math.abs(hi) * 0.1 || 1
  const von = lo - rand
  const spanne = hi + rand - von
  if (spanne <= 0) return null
  const pos = (x: number) => ((x - von) / spanne) * 100
  const p = (x: number) => `${pos(x)}%`

  const gleich = gv === min && gb === max
  const zielDicht = pos(max) - pos(min) < 16
  // lage kommt als „im Ziel" / „darunter" / „darüber".
  const lage = wert.meldet ? 'ist-meldet' : wert.lage.startsWith('im') ? 'ist-im' : 'ist-rand'

  return (
    <div className="zw-band" aria-hidden="true">
      <div className="zw-balken">
        <div className="zw-spur" />
        {/* Fork AI (F-041): Einzelwert-Ziel als Marke statt einer Zone ohne Breite. */}
        <div className={classNames('zw-zone', min === max && 'ist-marke')} style={{ left: p(min), width: `${pos(max) - pos(min)}%` }} />
        {!gleich && gv != null && <div className="zw-grenze" style={{ left: p(gv) }} />}
        {!gleich && gb != null && <div className="zw-grenze" style={{ left: p(gb) }} />}
        <div className={classNames('zw-nadel', lage)} style={{ left: p(istZahl) }} title="Messwert jetzt" />
      </div>
      <div className="zw-skala">
        {!gleich && gv != null && <span className="ist-grenze" style={{ left: p(gv) }}>{kurz(gv)}</span>}
        {min === max
          // Fork AI (F-041): Einzelwert-Ziel als eine Zahl, nicht „25–25".
          ? <span className="ist-ziel" style={{ left: p(min) }}>{kurz(min)}</span>
          : zielDicht
          ? <span className="ist-ziel" style={{ left: `${(pos(min) + pos(max)) / 2}%` }}>{kurz(min)}–{kurz(max)}</span>
          : <>
              <span className="ist-ziel" style={{ left: p(min) }}>{kurz(min)}</span>
              <span className="ist-ziel" style={{ left: p(max) }}>{kurz(max)}</span>
            </>}
        {!gleich && gb != null && <span className="ist-grenze" style={{ left: p(gb) }}>{kurz(gb)}</span>}
      </div>
    </div>
  )
}

/** Zahl kurz, deutsch, ohne Tausenderpunkt: 1200, 1,4, 21. */
function kurz(x: number): string {
  return x.toLocaleString('de-DE', { maximumFractionDigits: 2, useGrouping: false })
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
          {/* Fork AI (forkai.145, F-040): eine Zeile je Wert — Richtung, Grenze, seit wann. */}
          {melden.length > 0 && (
            <section className="zw-warn" role="alert" data-audit="grenzwerte-warnung">
              <div className="zw-warn-kopf">
                {melden.length === 1 ? '1 Wert wird gemeldet' : `${melden.length} Werte werden gemeldet`}
              </div>
              {melden.map((w) => warnZeile(w)).map((z) => (
                <button
                  key={z.key}
                  type="button"
                  className="zw-warn-zeile"
                  onClick={() => document.getElementById(`zw-karte-${z.key}`)?.scrollIntoView({ behavior: 'smooth', block: 'center' })}
                >
                  <span className="zw-warn-name">{z.name}</span>
                  <span className="zw-warn-ist">{z.ist}{z.einheit && <i>{z.einheit}</i>}</span>
                  <span className="zw-warn-grenze">{z.grenze ? <>Grenze <b>{z.grenze}</b></> : 'Grenze überschritten'}</span>
                  <span className="zw-warn-richtung">{z.richtung}{z.seit && ` · seit ${z.seit}`}</span>
                </button>
              ))}
            </section>
          )}

          {daten.hinweise.map((hinweis) => (
            <V1Alert key={hinweis} message={hinweis} tone="warn" />
          ))}

          <V1Section title={kopf ?? 'Werte'}>
            <div className="zw-karten" data-audit="zielwerte-karten">
              {daten.werte.map((wert) => (
                <article key={wert.key} id={`zw-karte-${wert.key}`} className={classNames('zw-karte', wert.meldet && 'ist-meldet')}>
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

                  <div className="zw-fuss zw-ziel">
                    <span className="zw-etikett">Ziel</span>
                    <b className="zw-zahl">
                      {wert.band ? `${wert.band}${wert.einheit ? ` ${wert.einheit}` : ''}` : 'kein Ziel'}
                    </b>
                    <QuellePill wert={wert} />
                  </div>
                  {wert.quelleZusatz && <p className="zw-herkunft">{wert.quelleZusatz}</p>}

                  {/* Der Alarm steht bewusst AUF der Karte und nicht auf einer
                      eigenen Seite: Ziel und Meldeschwelle sind zwei Zahlen zu
                      einer Sache, und sie auseinanderzuziehen war der Anfang
                      der Verwirrung. */}
                  <div className="zw-alarm">
                    <div className="zw-fuss">
                      <span className="zw-etikett">Meldet</span>
                      <span className="zw-meldet-text">
                        {wert.alarmVon == null && wert.alarmBis == null
                          ? 'keine Grenzwerte'
                          : wert.alarmVon === wert.min && wert.alarmBis === wert.max ? 'außerhalb des Ziels' : ''}
                      </span>
                      <Glocke wert={wert} />
                    </div>
                    {(wert.alarmVon != null || wert.alarmBis != null)
                      && !(wert.alarmVon === wert.min && wert.alarmBis === wert.max) && (
                      <div className="zw-chips">
                        {wert.alarmVon != null && <span className="zw-chip">unter <b>{kurz(wert.alarmVon)}</b> {wert.einheit}</span>}
                        {wert.alarmBis != null && <span className="zw-chip">über <b>{kurz(wert.alarmBis)}</b> {wert.einheit}</span>}
                      </div>
                    )}
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
                {gruppe.zeilen.map((zeile, i) => (
                  <Fragment key={zeile.links}>
                    {/* Fork AI (forkai.145, F-040): Untergruppen Klima / Nährlösung. */}
                    {zeile.bereich && zeile.bereich !== gruppe.zeilen[i - 1]?.bereich && (
                      <div className="zw-bereich">{zeile.bereich}</div>
                    )}
                    <div className="zw-zeile">
                      <span>
                        {zeile.links}
                        {zeile.zusatz && <small className="zw-zeile-zusatz">{zeile.zusatz}</small>}
                      </span>
                      <span className="zw-zeile-r">{zeile.rechts}</span>
                    </div>
                  </Fragment>
                ))}
                <p className="zw-gruppe-h">{gruppe.hinweis}</p>
              </div>
            ))}
          </V1Section>

          {/* Fork AI (forkai.142, F-038): Übergabe aus dem Plan in drei Gruppen. */}
          <UebergabeAbschnitt
            uebergabe={daten.uebergabe}
            letzteUebergabe={daten.letzteUebergabe}
            onFreigeben={(rolle) => void freigeben(rolle)}
          />
        </>
      )}
    </>
  )
}

export default ZielwertePage
