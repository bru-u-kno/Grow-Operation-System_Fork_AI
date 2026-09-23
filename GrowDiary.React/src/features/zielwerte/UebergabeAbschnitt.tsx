import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch } from '../../api'
import { V1Section } from '../../components/v1'
import { istHandgesetzt, ZUSTAND_CO2_STEUERUNG } from '../wochenplan/uebergabe-zustand'
import { rollenPfad } from '../geraete/rollenPfad'
import {
  bluelabPaare, co2Stufen, gruppieren, zahl,
  type BluelabGrenze, type UebergabeRoh, type UebergabeZeile,
} from './uebergabe-anzeige'

/**
 * Fork AI (forkai.142, F-038): „Übergabe aus dem Plan" — nach Mockup
 * `mockup-grenzwerte-uebergabe.html` (412 px) freigegeben.
 *
 * Drei Gruppen nach dem Ziel der Werte: Home Assistant (Helfer der Regelungen),
 * Grenzwerte (Meldungen im Fork) und das Bluelab-Gerät. Der Zustand steht immer
 * in eigener Zeile unter dem Wert und bricht nie um; Zusätze (Tag/Nacht, wann)
 * stehen klein unter dem Namen.
 */
type Co2Live = { live: { zielPpm: number | null; zielWarm: number; zielMittel: number; zielKuehl: number } }
type BluelabStand = { eingerichtet: boolean; zuletztUtc: string | null; fehler: string | null; grenzen: BluelabGrenze[] }

const zeit = (iso: string | null) =>
  iso ? new Date(iso).toLocaleString('de-DE', { dateStyle: 'medium', timeStyle: 'short' }) : null

export function UebergabeAbschnitt({ uebergabe, letzteUebergabe, onFreigeben }: {
  uebergabe: readonly UebergabeRoh[]
  letzteUebergabe: string | null
  onFreigeben: (rolle: string) => void
}) {
  const gruppen = useMemo(() => gruppieren(uebergabe), [uebergabe])
  const [co2, setCo2] = useState<Co2Live | null>(null)
  const [bluelab, setBluelab] = useState<BluelabStand | null>(null)

  useEffect(() => {
    let aktiv = true
    if (uebergabe.some((u) => u.zustand === ZUSTAND_CO2_STEUERUNG)) {
      apiFetch<Co2Live>('/api/steuerung/co2').then((c) => { if (aktiv) setCo2(c) }).catch(() => { /* ohne Stufen */ })
    }
    apiFetch<BluelabStand>('/api/steuerung/bluelab').then((b) => { if (aktiv) setBluelab(b) }).catch(() => { /* ohne Gerät */ })
    return () => { aktiv = false }
  }, [uebergabe])

  const paare = bluelab?.eingerichtet ? bluelabPaare(bluelab.grenzen) : []
  if (uebergabe.length === 0 && paare.length === 0) return null

  const zeile = (z: UebergabeZeile) => {
    const co2Zeile = z.zustand === ZUSTAND_CO2_STEUERUNG
    const stufen = co2Zeile && co2 ? co2Stufen(co2.live.zielKuehl, co2.live.zielMittel, co2.live.zielWarm, co2.live.zielPpm) : []
    return (
      <div key={z.rolle} className={co2Zeile ? 'ueb-zeile ist-co2' : 'ueb-zeile'}>
        <span className="ueb-name">
          {z.name}
          {z.zusatz && <small>{z.zusatz}</small>}
        </span>
        <span className="ueb-rechts">
          <span className="ueb-wert">{z.wert}{z.einheit && <i>{z.einheit}</i>}</span>
          {istHandgesetzt(z.zustand) ? (
            <button type="button" className="wp-frei ueb-zustand" data-audit="uebergabe-freigeben" onClick={() => onFreigeben(z.rolle)}>
              von dir gesetzt — freigeben
            </button>
          ) : (
            <span className="ueb-zustand">{co2Zeile ? 'folgt dem Plan · gestaffelt' : z.zustand}</span>
          )}
        </span>
        {stufen.length > 0 && (
          <span className="ueb-stufen">
            <span className="ueb-stufen-titel">Stufen je Canopy-Temperatur</span>
            {stufen.map((s) => (
              <span key={s.bereich} className={s.aktiv ? 'ueb-stufe ist-aktiv' : 'ueb-stufe'}>
                <em>{s.bereich}</em>
                <b>{s.ppm}<i>ppm</i></b>
              </span>
            ))}
          </span>
        )}
      </div>
    )
  }

  return (
    <V1Section title="Übergabe aus dem Plan">
      {gruppen.ha.length > 0 && (
        <div className="ueb-gruppe">
          <h3>An Home Assistant</h3>
          <p className="ueb-wozu">Diese Helfer lesen die Regelungen in Home Assistant.</p>
          {gruppen.ha.map(zeile)}
          <p className="ueb-fuss">
            {letzteUebergabe
              ? <><b>Zuletzt übergeben:</b> {zeit(letzteUebergabe)} — beim Wochenwechsel, täglich und sofort, wenn du im Plan speicherst.</>
              : 'Noch nichts übergeben — der erste Lauf merkt sich nur den Ist-Zustand.'}
          </p>
        </div>
      )}

      {gruppen.grenzwerte.length > 0 && (
        <div className="ueb-gruppe">
          <h3>An die Grenzwerte</h3>
          <p className="ueb-wozu">Ab diesen Werten meldet der Fork aufs Handy.</p>
          {gruppen.grenzwerte.map(zeile)}
        </div>
      )}

      {paare.length > 0 && bluelab && (
        <div className="ueb-gruppe" data-audit="grenzwerte-bluelab-stand">
          <h3>Ans Bluelab-Gerät</h3>
          <p className="ueb-wozu">Alarmgrenzen am Guardian — aus den Grenzwerten, nachts und tags zusammengefasst.</p>
          {paare.map((p) => (
            <div key={p.name} className="ueb-zeile">
              <span className="ueb-name">{p.name}</span>
              <span className="ueb-rechts">
                <span className="ueb-wert">{zahl(p.unten)} · {zahl(p.oben)}{p.einheit && <i>{p.einheit}</i>}</span>
                <span className={p.stimmt ? 'ueb-zustand' : 'ueb-zustand ist-warn'}>{p.stimmt ? 'übernommen' : 'wird übertragen'}</span>
              </span>
            </div>
          ))}
          <p className="ueb-fuss">
            {bluelab.fehler
              ? <><b>Übertragung gestört:</b> {bluelab.fehler}.</>
              : <><b>Zuletzt übertragen:</b> {zeit(bluelab.zuletztUtc) ?? 'noch nie'} — alle 5 Minuten geprüft, nur Abweichendes wird geschrieben.</>}
            {' '}<Link className="ueb-link" to={rollenPfad('bluelab')}>Rollen ›</Link>
          </p>
        </div>
      )}
    </V1Section>
  )
}
