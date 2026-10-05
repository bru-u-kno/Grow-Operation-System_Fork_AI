import { useEffect, useState } from 'react'
import { apiFetch, formatApiError } from '../../api'
import type { TagebuchKurvenDto } from '../../types'
import { KlappTitel } from '../live/Einklappen'
import { zahl } from '../live/verlauf-modell'
import { MiniKurve } from './MiniKurve'
import type { Marke } from './tagebuch-modell'

/**
 * „▸ Sensorkurven" eines Tages.
 *
 * Eingeklappt steht nur der Titel — und, wenn es welche gibt, die Zahl der
 * Auffälligkeiten in Warnfarbe. Keine Zahlenzeile: Bru wollte im zugeklappten
 * Tag keine „Zahlenwüste" (Mockup Runde 3). Geladen wird erst beim ersten
 * Aufklappen und dann behalten — ein zweites Aufklappen fragt nicht neu.
 */
export function Sensorkurven({ growId, datum, offen, onUmschalten, auffaellig, marken }: {
  growId: string
  datum: string
  offen: boolean
  onUmschalten: () => void
  auffaellig: number
  marken: Marke[]
}) {
  const [kurven, setKurven] = useState<TagebuchKurvenDto | null>(null)
  const [fehler, setFehler] = useState<string | null>(null)

  useEffect(() => {
    if (!offen || kurven) return
    const controller = new AbortController()
    apiFetch<TagebuchKurvenDto>(`/api/grows/${growId}/tagebuch/kurven?tag=${datum}`, { signal: controller.signal })
      .then((antwort) => { if (!controller.signal.aborted) setKurven(antwort) })
      .catch((caught) => { if (!controller.signal.aborted) setFehler(formatApiError(caught, 'Sensorkurven konnten nicht geladen werden.')) })
    return () => controller.abort()
  }, [offen, kurven, growId, datum])

  return (
    <div className={`tb-kopf${offen ? '' : ' is-zu'}`} data-audit="tagebuch-sensorkurven">
      <KlappTitel zu={!offen} onUmschalten={onUmschalten}>
        <span className="tb-kopf-titel">Sensorkurven</span>
        {auffaellig > 0 && <em className="ls-bilanz is-warn">{auffaellig === 1 ? '1 Auffälligkeit' : `${auffaellig} Auffälligkeiten`}</em>}
      </KlappTitel>
      {offen && (
        <div className="tb-kopf-inhalt">
          {fehler && <p className="tb-hinweis" role="alert">{fehler}</p>}
          {!fehler && !kurven && <p className="tb-hinweis">Lädt…</p>}
          {kurven && <KurvenKacheln kurven={kurven} marken={marken} />}
        </div>
      )}
    </div>
  )
}

function KurvenKacheln({ kurven, marken }: { kurven: TagebuchKurvenDto; marken: Marke[] }) {
  if (kurven.aufloesung === 'keine') {
    return <p className="tb-hinweis">Für diesen Tag hat das Zelt keine Sensorwerte.</p>
  }
  return (
    <>
      {kurven.aufloesung === 'tag' && (
        <p className="tb-hinweis">
          Nur Tageswerte: die Werte im 5-Minuten-Takt hebt Grow OS sieben Tage auf, danach bleiben Tiefst-, Mittel- und Höchstwert je Tag.
        </p>
      )}
      <div className="tb-kacheln">
        {kurven.kurven.map((k) => {
          const spanne = k.min != null && k.max != null ? `${zahl(k.min, k.nachkomma)}–${zahl(k.max, k.nachkomma)}` : null
          return (
            <div key={k.schluessel} className="tb-kachel" data-audit={`tagebuch-kurve-${k.schluessel}`}>
              <div className="tb-kachel-kopf">
                <span>{k.name}</span>
                {spanne ? <b>{spanne}</b> : <b className="is-leer">keine Werte</b>}
                {spanne && k.einheit && <small>{k.einheit}</small>}
              </div>
              {k.punkte.length > 0 ? (
                <MiniKurve
                  punkte={k.punkte}
                  licht={kurven.licht}
                  marken={marken}
                  beschriftung={spanne ? `${k.name} von ${spanne}${k.einheit ? ` ${k.einheit}` : ''}` : k.name}
                />
              ) : k.median != null ? (
                <p className="tb-kachel-tag">Median {zahl(k.median, k.nachkomma)}{k.einheit ? ` ${k.einheit}` : ''}</p>
              ) : null}
            </div>
          )
        })}
      </div>
      <Legende lichtQuelle={kurven.lichtQuelle} mitKurve={kurven.aufloesung === 'roh'} />
    </>
  )
}

function Legende({ lichtQuelle, mitKurve }: { lichtQuelle: TagebuchKurvenDto['lichtQuelle']; mitKurve: boolean }) {
  if (!mitKurve) return null
  return (
    <div className="tb-legende" aria-label="Legende">
      <span><i className="tb-lg-linie" /> Verlauf 0–24 Uhr</span>
      {lichtQuelle !== 'keine' && (
        <span><i className="tb-lg-licht" /> {lichtQuelle === 'sensor' ? 'Licht an (geschaltet)' : 'Licht an (laut Lichtplan)'}</span>
      )}
      <span><i className="tb-lg-strich is-blau" /> Wasserwechsel / Nachfüllen / Dosierung</span>
      <span><i className="tb-lg-strich is-gelb" /> Notiz / Auffälliges</span>
    </div>
  )
}
