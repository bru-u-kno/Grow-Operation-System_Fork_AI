import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import type { MetricPayload } from '../../types'
import type { HistoryPoint } from '../../components/SensorChart'
import { classNames } from '../../utils'
import { KNOWN_METRICS } from './dashboard-layout'
import { decimalsForMetric } from './metric-tile-model'
import { VERLAUFS_METRIKEN } from './useTentSparklines'
import { useWochenverlauf } from './useWochenverlauf'
import { useVerlaufGesten, type GestenStand } from './useVerlaufGesten'
import { EinzelZeile, FokusBild, LichtSpur, Uebersicht, ZeilenAchse, ZusammenBild, type Reihe } from './verlauf-flaechen'
import { FOKUS, ZEILE, ZUSAMMEN } from './verlauf-masse'
import {
  ZEITRAEUME, amZeiger, begrenzeFenster, blaettern, datenGrenzen, dunkelphasen, fensterFuerZeitraum,
  grenzText, kannBlaettern, kurvenFarbe, letzterPunkt, lichtPhasen, quelleWaehlen,
  spannenTeile, statistik, uhrzeit, zahl, zeitraumBeiBreite, zielbandStuecke, zusammenfuehren,
  type BandStueck, type Fenster, type Punkt, type ZeitraumId,
} from './verlauf-modell'
import './verlauf.css'

type Darstellung = 'zusammen' | 'einzeln'
type Ansicht = { art: 'zeitraum'; id: ZeitraumId } | { art: 'frei'; fenster: Fenster }
type Gemerkt = { an?: string[]; darstellung?: Darstellung }

const speicherSchluessel = (tileId: string) => `growos.verlauf.${tileId}`

/** Was die Kachel sich gemerkt hat — ohne Speicher (Privatmodus) eben nichts. */
function gemerktLesen(tileId: string): Gemerkt {
  try {
    const roh = localStorage.getItem(speicherSchluessel(tileId))
    if (!roh) return {}
    const wert = JSON.parse(roh) as Gemerkt
    return {
      an: Array.isArray(wert.an) ? wert.an.filter((k): k is string => typeof k === 'string') : undefined,
      darstellung: wert.darstellung === 'einzeln' || wert.darstellung === 'zusammen' ? wert.darstellung : undefined,
    }
  } catch {
    return {}
  }
}

function fensterAus(ansicht: Ansicht, grenzen: Fenster): Fenster {
  return ansicht.art === 'zeitraum' ? fensterFuerZeitraum(ansicht.id, grenzen) : begrenzeFenster(ansicht.fenster, grenzen)
}

/** Das Band, das zur Zeit `t` gilt. */
function bandBei(stuecke: BandStueck[], t: number): BandStueck | null {
  return stuecke.find((s) => t >= s.von && t <= s.bis) ?? null
}

/**
 * Das Verlaufsdiagramm der Diagramm-Kachel auf der Live-Seite.
 *
 * Nach dem Vorbild der AC-Infinity-App, vom Nutzer am 03.10.2026 gewählt
 * („Variante D"): Wertekarten oben zum Ein- und Ausblenden, Zeitraum und
 * Blättern, ein Diagramm in echten Pixeln mit Zeiger und Zwei-Finger-Zoom, die
 * Licht-Spur, eine Übersichtsleiste und Max/Min/Ø des Ausschnitts.
 *
 * Dazu der Umschalter „Zusammen | Einzeln": „Einzeln" ist eine Zeile je Wert
 * mit Zielband (Variante B), und ein Tipp auf eine Zeile zeigt diesen einen
 * Wert groß mit echter Achse (Variante C) — „nur einen Wert ansehen" war der
 * eigentliche Wunsch.
 *
 * Die Rechnung steht in `verlauf-modell.ts`, die Zeichenflächen in
 * `verlauf-flaechen.tsx`, die Gesten in `useVerlaufGesten.ts`.
 */
export function Verlaufsdiagramm({
  tileId, tentId, metricKeys, metricsByKey, tag,
}: {
  tileId: string
  tentId: number | null
  /** Die in der Kachel eingestellten Werte — anfangs eingeschaltet. */
  metricKeys: string[]
  metricsByKey: Map<string, MetricPayload>
  /** Die 24 h Rohwerte, die die Live-Seite ohnehin lädt (`useTentSparklines`). */
  tag: Map<string, HistoryPoint[]>
}) {
  const [gemerkt] = useState(() => gemerktLesen(tileId))
  const [an, setAn] = useState<string[]>(() => gemerkt.an ?? metricKeys)
  const [darstellung, setDarstellung] = useState<Darstellung>(gemerkt.darstellung ?? 'zusammen')
  const [fokus, setFokus] = useState<string | null>(null)
  const [ansicht, setAnsicht] = useState<Ansicht>({ art: 'zeitraum', id: '24h' })
  const [gewaehlt, setGewaehlt] = useState<ZeitraumId>('24h')
  const [zeiger, setZeiger] = useState<number | null>(null)
  const [breite, setBreite] = useState(0)

  useEffect(() => {
    try {
      localStorage.setItem(speicherSchluessel(tileId), JSON.stringify({ an, darstellung } satisfies Gemerkt))
    } catch { /* ohne Speicher geht alles, nur gemerkt wird nichts */ }
  }, [tileId, an, darstellung])

  /* ---------- Daten ---------- */

  const tagPunkte = useMemo(() => new Map([...tag].map(([key, punkte]) => [
    key,
    punkte.map((p): Punkt => ({ t: new Date(p.t).getTime(), v: p.v })).filter((p) => Number.isFinite(p.t) && Number.isFinite(p.v)).sort((a, b) => a.t - b.t),
  ])), [tag])

  const tagVon = useMemo(() => {
    const anfaenge = [...tagPunkte.values()].filter((p) => p.length).map((p) => p[0].t)
    return anfaenge.length ? Math.min(...anfaenge) : null
  }, [tagPunkte])

  // Ob die 7 Tage gebraucht werden, entscheidet der Ausschnitt — gerechnet an
  // den 24-h-Grenzen, denn die 7-Tage-Grenzen gibt es erst nach dem Laden.
  const tagGrenzen = useMemo(() => datenGrenzen([...tagPunkte.values()], false), [tagPunkte])
  // Ohne 24-h-Daten wird NICHT von selbst nachgeladen: beim ersten Zeichnen
  // sind die 24 h schlicht noch unterwegs, und jede Live-Seite holte sonst
  // einmal die ganze Woche. Dann entscheidet ein Knopf im Leerzustand.
  const [wocheAngefordert, setWocheAngefordert] = useState(false)
  const benoetigt = wocheAngefordert || (tagGrenzen != null && quelleWaehlen(fensterAus(ansicht, tagGrenzen), tagVon) === '7t')
  const woche = useWochenverlauf(tentId, benoetigt)

  const reihen = useMemo<Reihe[]>(() => VERLAUFS_METRIKEN.flatMap((key, index) => {
    const punkte = zusammenfuehren(woche.daten?.get(key), tagPunkte.get(key))
    if (punkte.length < 2) return []
    const metric = metricsByKey.get(key)
    return [{
      key,
      label: metric?.label ?? KNOWN_METRICS.find((m) => m.key === key)?.label ?? key,
      unit: metric?.unit ?? null,
      farbe: kurvenFarbe(key, index),
      stellen: decimalsForMetric(key),
      punkte,
      ziele: metric ?? null,
    }]
  }), [woche.daten, tagPunkte, metricsByKey])

  const grenzen = useMemo(() => datenGrenzen(reihen.map((r) => r.punkte), woche.daten != null), [reihen, woche.daten])
  const fenster = useMemo(() => (grenzen ? fensterAus(ansicht, grenzen) : null), [ansicht, grenzen])

  const licht = metricsByKey.get('light-cycle')
  const lichtAn = licht?.lightOnAt ?? null
  const lichtAus = licht?.lightOffAt ?? null
  const dunkel = useMemo(() => (fenster ? dunkelphasen(fenster, lichtAn, lichtAus) : []), [fenster, lichtAn, lichtAus])
  const hell = useMemo(() => (fenster ? lichtPhasen(fenster, lichtAn, lichtAus) : null), [fenster, lichtAn, lichtAus])
  const baender = useMemo(() => new Map(reihen.map((r) => [r.key, fenster ? zielbandStuecke(fenster, r.ziele, hell) : []])), [reihen, fenster, hell])

  const eingeschaltet = useMemo(() => reihen.filter((r) => an.includes(r.key)), [reihen, an])
  const fokusReihe = fokus ? reihen.find((r) => r.key === fokus) ?? null : null
  const imFokus = darstellung === 'einzeln' && fokusReihe != null

  /* ---------- Gesten ---------- */

  const stand = useRef<GestenStand | null>(null)
  useLayoutEffect(() => {
    stand.current = fenster && grenzen ? { fenster, grenzen } : null
  }, [fenster, grenzen])

  const setFensterFrei = useCallback((f: Fenster) => setAnsicht({ art: 'frei', fenster: f }), [])
  const zuruecksetzen = useCallback(() => {
    setAnsicht({ art: 'zeitraum', id: gewaehlt })
    setZeiger(null)
  }, [gewaehlt])
  const gesten = useVerlaufGesten({ stand, setZeiger, setFenster: setFensterFrei, zuruecksetzen })

  const beobachter = useRef<ResizeObserver | null>(null)
  const { huelleRef } = gesten
  const flaecheRef = useCallback((element: HTMLDivElement | null) => {
    huelleRef(element)
    beobachter.current?.disconnect()
    beobachter.current = null
    if (!element) return
    const ro = new ResizeObserver((eintraege) => {
      const b = Math.floor(eintraege[0]?.contentRect.width ?? 0)
      setBreite((alt) => (alt === b ? alt : b))
    })
    ro.observe(element)
    beobachter.current = ro
  }, [huelleRef])

  /* ---------- Leerzustand ---------- */

  if (reihen.length === 0 || !fenster || !grenzen) {
    return (
      <div className="vd" data-audit="verlauf">
        <p className="vd-leer">
          {woche.laedt ? 'Lade die letzten 7 Tage …'
            : wocheAngefordert ? 'Auch in den letzten 7 Tagen keine Messwerte.'
              : 'Noch kein Verlauf der letzten 24 Stunden — sobald Messwerte ankommen, steht die Kurve hier.'}
        </p>
        {!wocheAngefordert && tentId != null && (
          <button type="button" className="vd-zurueck vd-mitte" onClick={() => setWocheAngefordert(true)}>Ältere Werte laden (7 Tage)</button>
        )}
      </div>
    )
  }

  /* ---------- Ansicht ---------- */

  const kann = kannBlaettern(fenster, grenzen)
  // Gewählt heißt gewählt — auch wenn die Daten keine vollen 7 Tage hergeben
  // und das Fenster deshalb ein paar Minuten schmaler ist. Nach Blättern oder
  // Zoomen entscheidet die Breite.
  const aktiverZeitraum = ansicht.art === 'zeitraum' ? ansicht.id : zeitraumBeiBreite(fenster)
  const teile = spannenTeile(fenster)

  const wertBei = (reihe: Reihe): number | null => {
    if (zeiger != null) return amZeiger(reihe.punkte, zeiger)?.v ?? null
    return metricsByKey.get(reihe.key)?.numericValue ?? letzterPunkt(reihe.punkte)?.v ?? null
  }
  const zeitText = zeiger != null ? uhrzeit(zeiger) : `jetzt · ${uhrzeit(grenzen.bis)}`

  const karteTippen = (key: string) => {
    if (imFokus) { setFokus(key); return }
    setAn((alt) => (alt.includes(key) ? alt.filter((k) => k !== key) : [...alt, key]))
  }

  const zeilenBreite = Math.max(0, breite - ZEILE.kopf - ZEILE.luecke)
  const leistenReihe = fokusReihe && imFokus ? fokusReihe : eingeschaltet[0] ?? reihen[0]
  const tabellenReihen = imFokus && fokusReihe ? [fokusReihe] : eingeschaltet

  return (
    <div className="vd" data-audit="verlauf" data-darstellung={imFokus ? 'fokus' : darstellung}>
      <div className="vd-werte" role="group" aria-label={imFokus ? 'Wert für die große Ansicht wählen' : 'Kurven ein- und ausblenden'}>
        {reihen.map((reihe) => {
          const aktiv = imFokus ? fokus === reihe.key : an.includes(reihe.key)
          return (
            <button
              key={reihe.key}
              type="button"
              className="vd-wert"
              aria-pressed={aktiv}
              data-audit={`verlauf-karte-${reihe.key}`}
              onClick={() => karteTippen(reihe.key)}
            >
              <span className="vd-wert-name">
                <i className="vd-ring" style={{ borderColor: reihe.farbe, background: aktiv ? reihe.farbe : 'transparent' }} aria-hidden="true" />
                {reihe.label}
              </span>
              <span className="vd-wert-zahl" data-audit="verlauf-kartenwert">
                {zahl(wertBei(reihe), reihe.stellen)}
                {reihe.unit && <small> {reihe.unit}</small>}
              </span>
            </button>
          )
        })}
      </div>

      <div className="vd-zeitleiste">
        <button type="button" className="vd-pfeil" aria-label="Früher" disabled={!kann.zurueck}
          onClick={() => { setZeiger(null); setAnsicht({ art: 'frei', fenster: blaettern(fenster, -1, grenzen) }) }}>
          <span aria-hidden="true">◀</span>
        </button>
        <p className="vd-spanne" data-audit="verlauf-spanne">
          {/* Fliesstext mit Leerzeichen zwischen den Teilen: ohne `nowrap` an den
              Teilen bräche der Browser an JEDEM Leerzeichen, etwa „… – Sa" / „03.10. …". */}
          {teile.map((teil, i) => <span key={teil}>{i > 0 && ' '}<span className="vd-teil">{teil}</span></span>)}
        </p>
        <button type="button" className="vd-pfeil" aria-label="Später" disabled={!kann.vor}
          onClick={() => { setZeiger(null); setAnsicht({ art: 'frei', fenster: blaettern(fenster, 1, grenzen) }) }}>
          <span aria-hidden="true">▶</span>
        </button>
      </div>

      <div className="vd-segment" role="group" aria-label="Zeitraum">
        {ZEITRAEUME.map((z) => (
          <button key={z.id} type="button" className="vd-seg" aria-pressed={aktiverZeitraum === z.id} data-audit={`verlauf-zeitraum-${z.id}`}
            onClick={() => { setGewaehlt(z.id); setZeiger(null); setAnsicht({ art: 'zeitraum', id: z.id }) }}>
            {z.name}
          </button>
        ))}
      </div>

      <div className="vd-segment is-zwei" role="group" aria-label="Darstellung">
        {(['zusammen', 'einzeln'] as const).map((d) => (
          <button key={d} type="button" className="vd-seg" aria-pressed={darstellung === d} data-audit={`verlauf-darstellung-${d}`}
            onClick={() => { setDarstellung(d); setFokus(null) }}>
            {d === 'zusammen' ? 'Zusammen' : 'Einzeln'}
          </button>
        ))}
      </div>

      {(woche.laedt || woche.fehler) && (
        <p className="vd-status" role="status">
          {woche.laedt ? 'Lade die letzten 7 Tage …' : 'Die letzten 7 Tage konnten nicht geladen werden — gezeigt wird, was da ist.'}
        </p>
      )}

      <div ref={flaecheRef} className="vd-flaeche-huelle">
        {breite > 0 && darstellung === 'zusammen' && (
          <>
            <ZusammenBild reihen={eingeschaltet} fenster={fenster} breite={breite} dunkel={dunkel} zeiger={zeiger} gesten={gesten.svgProps} />
            {hell && lichtAn && lichtAus && (
              <LichtSpur fenster={fenster} licht={hell} links={ZUSAMMEN.rand.l} plotBreite={breite - ZUSAMMEN.rand.l - ZUSAMMEN.rand.r}
                breite={breite} an={lichtAn} aus={lichtAus} />
            )}
          </>
        )}

        {breite > 0 && darstellung === 'einzeln' && !imFokus && (
          <div className="vd-zeilen" data-audit="verlauf-zeilen">
            <p className="vd-zeilen-zeit" data-audit="verlauf-zeilen-zeit">{zeitText}</p>
            {eingeschaltet.length === 0 && <p className="vd-leer">Keine Kurve gewählt — oben einen Wert antippen.</p>}
            {eingeschaltet.map((reihe) => {
              const s = statistik(reihe.punkte, fenster)
              return (
                <div key={reihe.key} className="vd-zeile" data-audit={`verlauf-zeile-${reihe.key}`}>
                  <button type="button" className="vd-zeile-kopf" onClick={() => { setFokus(reihe.key); setZeiger(null) }}>
                    <span className="vd-zeile-name">
                      <i className="vd-punkt" style={{ background: reihe.farbe }} aria-hidden="true" />
                      {reihe.label}
                    </span>
                    <span className="vd-zeile-zahl" data-audit="verlauf-zeilenwert">
                      {zahl(wertBei(reihe), reihe.stellen)}
                      {reihe.unit && <small> {reihe.unit}</small>}
                    </span>
                    <span className="vd-zeile-mm">{s ? `${zahl(s.min, reihe.stellen)}–${zahl(s.max, reihe.stellen)}` : '–'}</span>
                    <span className="sr-only"> — groß ansehen</span>
                  </button>
                  <EinzelZeile reihe={reihe} fenster={fenster} breite={zeilenBreite} dunkel={dunkel}
                    band={baender.get(reihe.key) ?? []} zeiger={zeiger} gesten={gesten.svgProps} />
                </div>
              )
            })}
            {eingeschaltet.length > 0 && (
              <div className="vd-zeile is-fuss">
                <span />
                <div>
                  <ZeilenAchse fenster={fenster} breite={zeilenBreite} />
                  {hell && lichtAn && lichtAus && (
                    <LichtSpur fenster={fenster} licht={hell} links={ZEILE.rand.l} plotBreite={zeilenBreite - ZEILE.rand.l - ZEILE.rand.r}
                      breite={zeilenBreite} an={lichtAn} aus={lichtAus} />
                  )}
                </div>
              </div>
            )}
          </div>
        )}

        {breite > 0 && imFokus && fokusReihe && (() => {
          const band = baender.get(fokusReihe.key) ?? []
          const wert = wertBei(fokusReihe)
          const gilt = bandBei(band, zeiger ?? grenzen.bis)
          // Ein Sollwert (min = max) ist kein Band: „im Ziel" träfe nie zu, und
          // „außerhalb 19–19" liest sich wie ein Fehler. Dort steht nur der Sollwert.
          const sollwert = gilt != null && gilt.min != null && gilt.min === gilt.max
          const imZiel = wert != null && gilt != null && !sollwert && (gilt.min == null || wert >= gilt.min) && (gilt.max == null || wert <= gilt.max)
          const zielText = gilt == null ? null
            : sollwert ? `${grenzText(gilt.min as number, fokusReihe.stellen)}`
            : gilt.min != null && gilt.max != null ? `${grenzText(gilt.min, fokusReihe.stellen)}–${grenzText(gilt.max, fokusReihe.stellen)}`
              : gilt.min != null ? `ab ${grenzText(gilt.min, fokusReihe.stellen)}`
                : `bis ${grenzText(gilt.max as number, fokusReihe.stellen)}`
          return (
            <div className="vd-fokus" data-audit="verlauf-fokus">
              <div className="vd-fokus-kopf">
                <button type="button" className="vd-zurueck" data-audit="verlauf-alle-werte" onClick={() => { setFokus(null); setZeiger(null) }}>
                  ← Alle Werte
                </button>
                <span className="vd-fokus-name"><i className="vd-punkt" style={{ background: fokusReihe.farbe }} aria-hidden="true" />{fokusReihe.label}</span>
              </div>
              <div className="vd-gross">
                <span className="vd-gross-zahl" data-audit="verlauf-fokuswert">
                  {zahl(wert, fokusReihe.stellen)}
                  {fokusReihe.unit && <small> {fokusReihe.unit}</small>}
                </span>
                <span className="vd-gross-text">
                  {zeitText}
                  {zielText && wert != null && (
                    <> · <span className={classNames(imZiel && 'vd-im-ziel')} data-audit="verlauf-zielstatus">{sollwert ? 'Soll' : imZiel ? 'im Ziel' : 'außerhalb'} {zielText}{fokusReihe.unit ? ` ${fokusReihe.unit}` : ''}</span></>
                  )}
                </span>
              </div>
              <FokusBild reihe={fokusReihe} fenster={fenster} breite={breite} dunkel={dunkel} band={band} zeiger={zeiger} gesten={gesten.svgProps} />
              {hell && lichtAn && lichtAus && (
                <LichtSpur fenster={fenster} licht={hell} links={FOKUS.rand.l} plotBreite={breite - FOKUS.rand.l - FOKUS.rand.r}
                  breite={breite} an={lichtAn} aus={lichtAus} />
              )}
            </div>
          )
        })()}

        {breite > 0 && (
          <Uebersicht
            reihe={leistenReihe ?? null}
            grenzen={grenzen}
            fenster={fenster}
            breite={breite}
            spanneText={teile.join(' ')}
            onMitte={(mitte) => {
              const w = fenster.bis - fenster.von
              setAnsicht({ art: 'frei', fenster: begrenzeFenster({ von: mitte - w / 2, bis: mitte + w / 2 }, grenzen) })
            }}
            onTaste={(richtung) => {
              const w = fenster.bis - fenster.von
              const ziel = richtung === 'anfang' ? { von: grenzen.von, bis: grenzen.von + w }
                : richtung === 'ende' ? { von: grenzen.bis - w, bis: grenzen.bis }
                  : { von: fenster.von + richtung * w / 4, bis: fenster.bis + richtung * w / 4 }
              setAnsicht({ art: 'frei', fenster: begrenzeFenster(ziel, grenzen) })
            }}
          />
        )}
      </div>

      <div className="vd-tabelle-huelle">
        <table className="vd-tabelle" data-audit="verlauf-tabelle">
          <thead>
            <tr><th scope="col">im Ausschnitt</th><th scope="col">Max</th><th scope="col">Min</th><th scope="col">Ø</th></tr>
          </thead>
          <tbody>
            {tabellenReihen.length === 0 && <tr><td colSpan={4}>Keine Kurve gewählt.</td></tr>}
            {tabellenReihen.map((reihe) => {
              const s = statistik(reihe.punkte, fenster)
              return (
                <tr key={reihe.key}>
                  <th scope="row">
                    <i className="vd-punkt" style={{ background: reihe.farbe }} aria-hidden="true" />
                    {reihe.label}{reihe.unit ? <small> {reihe.unit}</small> : null}
                  </th>
                  <td>{zahl(s?.max, reihe.stellen)}</td>
                  <td>{zahl(s?.min, reihe.stellen)}</td>
                  <td>{zahl(s?.mittel, reihe.stellen)}</td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
    </div>
  )
}

