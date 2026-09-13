import { useEffect, useState } from 'react'
import { apiFetch } from '../api'
import { classNames } from '../utils'
import { V1Alert, V1Page, V1Section, V1Skeleton } from '../components/v1'
import '../features/zielwerte/zielwerte.css'

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
  quelle: string
  quelleZusatz: string | null
  lage: string
  alarm: string | null
  kette: Stufe[]
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

/** Das Herkunftsblatt — klappt unter der Karte auf. */
function Kette({ wert }: { wert: Wert }) {
  return (
    <div className="zw-kette">
      {wert.kette.map((stufe, i) => (
        <div
          key={stufe.name + i}
          className={classNames('zw-stufe', stufe.gilt && 'ist-gilt', stufe.weg && 'ist-weg')}
        >
          <span className="zw-stufe-n">{i + 1}</span>
          <span className="zw-stufe-t">
            {stufe.name}
            {stufe.hinweis && <em>{stufe.hinweis}</em>}
          </span>
          <span className="zw-stufe-w">{stufe.wert ?? '–'}</span>
        </div>
      ))}
    </div>
  )
}

function ZielwertePage() {
  const [daten, setDaten] = useState<Zielwerte | null>(null)
  const [offen, setOffen] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    async function laden() {
      try {
        setDaten(await apiFetch<Zielwerte>('/api/zielwerte'))
      } catch {
        setError('Die Zielwerte konnten nicht geladen werden.')
      } finally {
        setLoading(false)
      }
    }
    void laden()
  }, [])

  if (loading) return <V1Skeleton rows={6} label="Lade Zielwerte" />

  const kopf = daten?.growName
    ? [daten.growName, daten.phase, daten.woche].filter(Boolean).join(' · ')
    : null

  return (
    <V1Page
      eyebrow="Betrieb"
      title="Zielwerte"
      subtitle="Was gerade gilt, woher es kommt und wo man es ändert. Vier Quellen stehen hintereinander — jede spätere sticht die früheren."
    >
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
          {daten.hinweise.map((hinweis) => (
            <V1Alert key={hinweis} message={hinweis} tone="warn" />
          ))}

          <V1Section title={kopf ?? 'Werte'}>
            <div className="zw-karten" data-audit="zielwerte-karten">
              {daten.werte.map((wert) => (
                <article key={wert.key} className="zw-karte">
                  <button
                    type="button"
                    className="zw-kopf"
                    onClick={() => setOffen(offen === wert.key ? null : wert.key)}
                    aria-expanded={offen === wert.key}
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
                      {wert.min !== null || wert.max !== null
                        ? `${wert.min ?? '–'} – ${wert.max ?? '–'}${wert.einheit ? ` ${wert.einheit}` : ''}`
                        : 'kein Ziel'}
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
                  <div className="zw-alarm">
                    {wert.alarm ? `Alarm ${wert.alarm}` : 'kein Alarm hinterlegt'}
                  </div>

                  {offen === wert.key && <Kette wert={wert} />}
                </article>
              ))}
            </div>
          </V1Section>

          <V1Section title="Wo stelle ich das ein">
            {daten.gruppen.map((gruppe) => (
              <div key={gruppe.titel} className="zw-gruppe">
                <div className="zw-gruppe-k">
                  <b>{gruppe.titel}</b>
                  <a href={gruppe.route}>{gruppe.routeText}</a>
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
                      <span className={classNames('zw-zustand', u.zustand !== 'folgt dem Plan' && 'ist-hand')}>
                        {u.zustand}
                      </span>
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
    </V1Page>
  )
}

export default ZielwertePage
