import { useId, useState } from 'react'
import type { ReactNode } from 'react'
import { KlappPfeil } from '../../components/Klapp'
import { Link } from 'react-router-dom'
import { V1Alert, V1Card } from '../../components/v1'
import '../live/metric-tile.css'
import { zahl } from './entfeuchter-band'
import { kurzeZahl } from './entfeuchtung-ziel'
import { ZONEN_WORT, zonenKlasse, zonenTon } from './entfeuchter-zonen'
import type { Zone } from './entfeuchter-zonen'
import type { EntfeuchterBremse } from './steuerung-typen'
import { Klappkachel } from './Klappkachel'
import './steuerung.css'

/**
 * Fork AI (A-014): die Bausteine der Seite „Überblick" — für Entfeuchter und
 * Zusatz-Entfeuchter gleich, damit beide gleich aufgebaut sind.
 */

/** Lagemeldung oben: Farbe, Wort und ein Satz — die Zone steht nie nur als Farbe da. */
export function Lage({ zone, titel, text }: { zone: Zone; titel: string; text: string }) {
  return <V1Alert tone={zonenTon(zone)} title={`${ZONEN_WORT[zone][0].toUpperCase()}${ZONEN_WORT[zone].slice(1)} · ${titel}`} message={text} />
}

/**
 * Fork AI (A-022): Kachel „Was bremst gerade?" — warum die Luftfeuchte nicht dort ist, wo der Plan sie will. Ohne Befund steht ein
 * ruhiger Satz da, mit Befund eine Meldung je Grund (Kapazität, Übertemperatur, Außenluft …) mit den Zahlen dahinter.
 */
export function Bremsen({ bremsen }: { bremsen: EntfeuchterBremse[] }) {
  if (bremsen.length === 0) {
    return (
      <V1Card>
        <p className="st-hinweis">
          Nichts bremst: keine Sperre, keine Wartezeit. Hier erscheint etwas, wenn ein Entfeuchter wegen Außenluft, Übertemperatur,
          CO₂-Drosselung oder fehlender Tagfreigabe nicht schaltet — oder wenn er läuft und die Feuchte trotzdem über dem Plan-Wert bleibt.
        </p>
      </V1Card>
    )
  }
  const ton = { kritisch: 'critical', warnung: 'warn', info: 'neutral' } as const
  return (
    <>
      {bremsen.map((b) => <V1Alert key={b.art} tone={ton[b.stufe]} title={b.titel} message={b.text} />)}
    </>
  )
}

/** Kopf eines Messblocks: die große Zahl links, rechts Zustand und Beiwerk. */
export function MessKopf({ wert, einheit, zone, zustand, ton, beiwerk }: {
  wert: string
  einheit: string
  zone: Zone | null
  zustand: string
  ton?: 'an' | 'warn' | 'kritisch'
  beiwerk: string
}) {
  return (
    <div className="ef-kopf">
      <span className={`ef-gross ${zonenKlasse(zone)}`}>{wert}<small> {einheit}</small></span>
      <span className="ef-zustand"><b className={ton === 'an' ? 'is-an' : ton === 'warn' ? 'is-warn' : ton === 'kritisch' ? 'is-kritisch' : undefined}>{zustand}</b>{beiwerk}</span>
    </div>
  )
}

export type WarumZeile = { frage: string; antwort: string; ok?: boolean | null }

/** „Warum ist er gerade AN/AUS?" — jede Bedingung der Regel mit dem echten Wert. */
export function Warum({ an, zeilen, fuss, titel }: { an: boolean | null; zeilen: WarumZeile[]; fuss?: ReactNode; titel?: string }) {
  const wort = an === true ? 'AN' : an === false ? 'AUS' : '…'
  return (
    <Klappkachel titel={titel ?? `Warum ist er gerade ${wort}?`}>
      <V1Card>
        {zeilen.map((z) => (
          <div key={z.frage} className="st-feldzeile">
            <span className="st-etikett">{z.frage}<small>{z.antwort}</small></span>
            <span className="st-nurlesen" aria-label={z.ok == null ? undefined : z.ok ? 'erfüllt' : 'nicht erfüllt'}>{z.ok == null ? '' : z.ok ? '✔' : '✖'}</span>
          </div>
        ))}
        {fuss && <p className="st-hinweis">{fuss}</p>}
      </V1Card>
    </Klappkachel>
  )
}

/**
 * Die Zusammenarbeit der beiden Entfeuchter in einfachen Sätzen — mit den echten
 * Zahlen, soweit die Seite sie kennt. Die Haupt-Seite kennt die Zusatz-Abstände
 * nicht (`zusatz` fehlt) und nennt dann nur die Höchsttemperatur.
 */
export function Zusammenspiel({ haupt, zusatz, tempMax, abstaende }: {
  haupt: string
  zusatz: string
  tempMax: number
  abstaende?: { folgeAus: number; wiederEin: number; mindestpauseMin: number }
}) {
  return (
    <Klappkachel titel="So arbeiten die beiden Entfeuchter zusammen" offen={false}>
      <V1Card>
        <div className="ef-regelart">
          <p><b>{haupt} führt.</b> Beide regeln nach derselben Größe (Luftfeuchte oder VPD, einstellbar unter Einstellungen › Erweitert); die Schwellen kommen aus dem Plan.</p>
          <p><b>{zusatz} hilft.</b> Er springt zu, wenn {haupt} eine Weile läuft, und geht früher wieder aus. Er steht im Zelt und gibt Wärme ab.</p>
          <p>
            <b>Zu warm: beide aus.</b> Über {zahl(tempMax)}{'\u00a0'}°C geht {haupt} aus
            {abstaende ? `, ${zusatz} schon bei ${zahl(abstaende.folgeAus)}\u00a0°C.` : `, ${zusatz} etwas früher.`}
          </p>
          <p>
            <b>Wieder an erst, wenn es kühler ist.</b> {zusatz} kommt erst
            {abstaende ? ` unter ${zahl(abstaende.wiederEin)}\u00a0°C zurück, frühestens nach ${abstaende.mindestpauseMin} min Pause.` : ' nach einem weiteren Abstand zurück, frühestens nach seiner Mindestpause.'}
            {' '}Das verhindert das Takten an der Grenze.
          </p>
          <p><b>Wo du es einstellst.</b> Alles unter „Einstellungen“: Betrieb, Ziel, Höchsttemperatur, Stufe und Zusatz-Stärke.</p>
        </div>
      </V1Card>
    </Klappkachel>
  )
}

/** Die Plaketten oben im Überblick: Lichtphase und Woche — gilt für Luftfeuchte und Temperatur zugleich. */
export function Kontext({ plaketten }: { plaketten: string[] }) {
  if (plaketten.length === 0) return null
  return <div className="ef-kontext">{plaketten.map((p) => <span key={p}>{p}</span>)}</div>
}

/** Die ZIEL-Zeile wie bei den Live-Kacheln: Wort „ZIEL" darüber, darunter die Zahl und ihre Einheit. */
export function ZielZeile({ text, einheit, zusatz }: { text: string; einheit: string; zusatz?: string }) {
  return (
    <div className="gos-metric-ziel">
      <div className="gos-metric-ziel-n">ZIEL</div>
      <div className="gos-metric-zv">{text}<span className="u"> {einheit}{zusatz ? ` ${zusatz}` : ''}</span></div>
    </div>
  )
}

export type ZielBandProps = {
  von: number
  bis: number
  /** Das grüne Ziel; eine Seite darf fehlen (dann reicht der Streifen bis zum Rand). */
  zielVon: number | null
  zielBis: number | null
  /** Gelbe Striche (die Grenzen); ihre Zahlen stehen unter dem Band. */
  grenzen: number[]
  ist: number | null
  urteil: 'ok' | 'warn' | 'crit' | null
}

/**
 * Das Band im Stil der Live-Kacheln (`metric-tile.css`): grüner Streifen für das Ziel in der Mitte,
 * gelbe Striche für die Grenzen, Nadel für den Messwert. Unter dem Band stehen nur die Grenzen —
 * das Ziel steht schon darüber bei „ZIEL" (11.10.2026).
 */
export function ZielBandAnzeige({ von, bis, zielVon, zielBis, grenzen, ist, urteil }: ZielBandProps) {
  const pos = (w: number) => Math.round(Math.max(0, Math.min(100, ((w - von) / (bis - von)) * 100)) * 10) / 10
  const links = zielVon == null ? 0 : pos(zielVon)
  const rechts = zielBis == null ? 100 : pos(zielBis)
  return (
    <div className="gos-metric-band" aria-hidden="true">
      <div className="balken">
        <div className="spur" />
        {rechts > links && <div className="zone" style={{ left: `${links}%`, width: `${rechts - links}%` }} />}
        {grenzen.map((g) => <div key={g} className="grenze" style={{ left: `${pos(g)}%` }} />)}
        {ist != null && <div className={`nadel ist-${urteil ?? 'ok'}`} style={{ left: `${pos(ist)}%` }} />}
      </div>
      <div className="skala">
        {grenzen.map((g) => <span key={g} className="ist-grenze" style={{ left: `${pos(g)}%` }}>{kurzeZahl(g)}</span>)}
      </div>
    </div>
  )
}

export type HerkunftLink = { text: string; to?: string; springeZu?: string }

/**
 * „Woher kommt das?" — eine Zeile zum Aufklappen; aufgeklappt eine Zeile je Quelle (Wert links, Herkunft
 * rechts) und darunter die Wege zum Ändern. Zugeklappt kostet es nur eine Zeile (13.10.2026).
 */
export function Herkunft({ titel = 'Woher kommt das?', zeilen, links, onSpringen }: {
  titel?: string
  zeilen: Array<{ wert: string; quelle: string }>
  links?: HerkunftLink[]
  onSpringen?: (kachel: string) => void
}) {
  const [auf, setAuf] = useState(false)
  const koerper = useId()
  return (
    <div className="ef-herkunft-box">
      <button type="button" className="ef-hk-kopf" aria-expanded={auf} aria-controls={koerper} onClick={() => setAuf(!auf)}>
        <KlappPfeil zu={!auf} />
        <span className="ef-hk-titel">{titel}</span>
      </button>
      <div id={koerper} className="ef-hk-koerper" hidden={!auf}>
        {zeilen.map((z) => (
          <div key={z.wert} className="ef-hk-zeile"><b>{z.wert}</b><span>{z.quelle}</span></div>
        ))}
        {links && links.length > 0 && (
          <div className="ef-hk-links">
            {links.map((l) => (
              l.to
                ? <Link key={l.text} to={l.to}>{l.text}</Link>
                : <button key={l.text} type="button" onClick={() => l.springeZu && onSpringen?.(l.springeZu)}>{l.text}</button>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
