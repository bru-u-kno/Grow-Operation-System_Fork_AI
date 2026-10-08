import type { ReactNode } from 'react'
import { V1Alert, V1Card } from '../../components/v1'
import { zahl } from './entfeuchter-band'
import { ZONEN_WORT, markenZeilen, zonenKlasse, zonenStrecken, zonenTon } from './entfeuchter-zonen'
import type { Zone } from './entfeuchter-zonen'
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

export type SkalaMarke = { wert: number; label: string }


/**
 * Eine Skala mit drei Farbstrecken, Messpunkt und Marken. Der Messpunkt und die
 * Zahl darüber tragen die Farbe der Zone, in der der Istwert liegt.
 */
export function ZonenSkala({ von, bis, zielBis, knappBis, marken, ist, zone, stellen = 1, einheit }: {
  von: number
  bis: number
  zielBis: number
  knappBis: number
  marken: SkalaMarke[]
  ist: number | null
  zone: Zone | null
  stellen?: number
  einheit: string
}) {
  const pos = (w: number) => Math.round(Math.max(0, Math.min(100, ((w - von) / (bis - von)) * 100)) * 10) / 10
  const zeilen = markenZeilen(marken.map((m) => pos(m.wert)))
  return (
    <>
      <div className="ef-skala" aria-hidden="true">
        <div className="ef-bahn hat-zonen">
          {zonenStrecken(von, bis, zielBis, knappBis).map((s) => (
            <i key={s.zone} className={`ez-farbe is-${s.zone}`} style={{ left: `${s.links}%`, width: `${s.breite}%` }} />
          ))}
        </div>
        {ist != null && <div className={`ef-ist ${zonenKlasse(zone)}`} style={{ left: `${pos(ist)}%` }}><em>{zahl(ist, stellen)}</em></div>}
      </div>
      <div className={zeilen.length > 1 ? 'ef-marken ist-gestaffelt' : 'ef-marken'}>
        {marken.map((m, i) => (
          <span key={m.label + m.wert} className={zeilen[i] === 1 ? 'is-tief' : undefined} style={{ left: `${pos(m.wert)}%` }}>{zahl(m.wert, stellen)}<b>{m.label}</b></span>
        ))}
      </div>
      <div className="ef-rand"><span>{zahl(von, 0)} {einheit}</span><span>{zahl(bis, 0)} {einheit}</span></div>
    </>
  )
}

export function ZonenLegende() {
  return (
    <div className="ef-legende">
      <span><i className="is-ziel" />{ZONEN_WORT.ziel}</span>
      <span><i className="is-knapp" />{ZONEN_WORT.knapp}</span>
      <span><i className="is-kritisch" />{ZONEN_WORT.kritisch}</span>
    </div>
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
          <p><b>{haupt} führt.</b> Er schaltet nach Luftfeuchte oder VPD; die Schwellen kommen aus dem Plan.</p>
          <p><b>{zusatz} hilft.</b> Er springt zu, wenn {haupt} eine Weile läuft, und geht früher wieder aus. Er steht im Zelt und gibt Wärme ab.</p>
          <p>
            <b>Zu warm: beide aus.</b> Über {zahl(tempMax)} °C geht {haupt} aus
            {abstaende ? `, ${zusatz} schon bei ${zahl(abstaende.folgeAus)} °C.` : `, ${zusatz} etwas früher.`}
          </p>
          <p>
            <b>Wieder an erst, wenn es kühler ist.</b> {zusatz} kommt erst
            {abstaende ? ` unter ${zahl(abstaende.wiederEin)} °C zurück, frühestens nach ${abstaende.mindestpauseMin} min Pause.` : ' nach einem weiteren Abstand zurück, frühestens nach seiner Mindestpause.'}
            {' '}Das verhindert das Takten an der Grenze.
          </p>
          <p><b>Wo du es einstellst.</b> Schwellen unter „Regel", Temperaturen und Pausen unter „Schutz", Automatik und Wartezeiten unter „Betrieb".</p>
        </div>
      </V1Card>
    </Klappkachel>
  )
}
