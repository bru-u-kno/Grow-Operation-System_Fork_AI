import { memo, useId, useMemo, type PointerEventHandler } from 'react'
import {
  abschnitteImFenster, achsenMarken, amZeiger, grenzText, pfad, skala, statistik, textBreite, uhrzeit, yTeilung,
  type BandStueck, type Fenster, type Punkt, type Ziele,
} from './verlauf-modell'
import { FOKUS, ZEILE, ZUSAMMEN } from './verlauf-masse'

/**
 * Die Zeichenflächen des Verlaufsdiagramms: „Zusammen" (alle Kurven in einem
 * Bild), eine Zeile von „Einzeln" und der Fokus auf einen Wert.
 *
 * Alle drei zeichnen in echten Pixeln: die viewBox ist die gemessene Breite,
 * es wird nichts gestaucht. Das alte `HistoryChart` zeichnete auf 900 px und
 * liess den Browser auf die Handybreite stauchen — Schrift und Punkte wurden
 * dabei zu schmalen Ellipsen.
 *
 * Jede Fläche besteht aus einem GRUND (Nacht, Gitter, Achse, Kurven), der nur
 * neu gezeichnet wird, wenn sich Ausschnitt, Breite oder Kurven ändern, und
 * dem ZEIGER darüber. Bewegt sich der Finger, ändert sich nur der Zeiger —
 * neu aufgebaute Kurven unter dem Finger liessen die Geste abreissen.
 */

export type Reihe = {
  key: string
  label: string
  unit: string | null
  farbe: string
  stellen: number
  /** Nach Zeit sortiert. */
  punkte: Punkt[]
  /** Die Punkte, an den Lücken geteilt — einmal je Datenstand gerechnet (`abschnitte`). */
  stuecke: Punkt[][]
  ziele: Ziele | null
}

export type GestenProps = {
  onPointerDown: PointerEventHandler<SVGSVGElement>
  onPointerMove: PointerEventHandler<SVGSVGElement>
  onPointerUp: PointerEventHandler<SVGSVGElement>
  onPointerCancel: PointerEventHandler<SVGSVGElement>
  onPointerLeave: PointerEventHandler<SVGSVGElement>
}

type ZeitX = (t: number) => number

function zeitX(fenster: Fenster, links: number, breite: number): ZeitX {
  const spanne = Math.max(1, fenster.bis - fenster.von)
  return (t) => links + ((t - fenster.von) / spanne) * breite
}

function Nacht({ dunkel, x, oben, hoehe, links, rechts }: {
  dunkel: Fenster[]; x: ZeitX; oben: number; hoehe: number; links: number; rechts: number
}) {
  return (
    <>
      {dunkel.map((stueck) => {
        const x0 = Math.max(links, x(stueck.von))
        const x1 = Math.min(rechts, x(stueck.bis))
        return x1 > x0
          ? <rect key={stueck.von} x={x0} y={oben} width={x1 - x0} height={hoehe} className="vd-nacht" data-audit="verlauf-nacht"
            data-von={stueck.von} data-bis={stueck.bis} />
          : null
      })}
    </>
  )
}

function Zeitmarken({ fenster, links, breite, gesamt, y, gitterOben, gitterUnten }: {
  fenster: Fenster; links: number; breite: number; gesamt: number; y: number; gitterOben?: number; gitterUnten?: number
}) {
  const marken = achsenMarken(fenster, links, breite, gesamt)
  return (
    <>
      {marken.map((marke) => (
        <g key={marke.t}>
          {gitterOben != null && gitterUnten != null && (
            <line x1={marke.x} x2={marke.x} y1={gitterOben} y2={gitterUnten} className="vd-gitter" />
          )}
          <text x={marke.x} y={y} textAnchor="middle" className="vd-achse">{marke.text}</text>
        </g>
      ))}
    </>
  )
}

/** Das Band als Rechtecke je Stück; ein offenes Ende reicht bis an den Rand. */
function Zielband({ stuecke, x, y, oben, unten, links, rechts, raender }: {
  stuecke: BandStueck[]; x: ZeitX; y: (v: number) => number; oben: number; unten: number; links: number; rechts: number; raender?: boolean
}) {
  return (
    <>
      {stuecke.map((stueck) => {
        const x0 = Math.max(links, x(stueck.von))
        const x1 = Math.min(rechts, x(stueck.bis))
        if (x1 <= x0) return null
        const y0 = stueck.max == null ? oben : Math.max(oben, Math.min(unten, y(stueck.max)))
        const y1 = stueck.min == null ? unten : Math.max(oben, Math.min(unten, y(stueck.min)))
        // Ein Sollwert statt eines Bands (min = max, etwa 23 °C am Tag): als
        // Fläche wäre er null Pixel hoch und unsichtbar — also eine Linie.
        if (stueck.min != null && stueck.min === stueck.max) {
          return <line key={stueck.von} x1={x0} x2={x1} y1={y0} y2={y0} className="vd-band-rand" data-audit="verlauf-zielband" />
        }
        return (
          <g key={stueck.von}>
            <rect x={x0} y={y0} width={x1 - x0} height={Math.max(0, y1 - y0)} className="vd-band" data-audit="verlauf-zielband" />
            {raender && stueck.max != null && <line x1={x0} x2={x1} y1={y0} y2={y0} className="vd-band-rand" />}
            {raender && stueck.min != null && <line x1={x0} x2={x1} y1={y1} y2={y1} className="vd-band-rand" />}
          </g>
        )
      })}
    </>
  )
}

/**
 * Die Uhrzeit am Zeiger — auf einem Schild ÜBER den Kurven. Ohne Grund lief
 * eine Kurve mitten durch die Ziffern (im Demobestand die CO₂-Linie um 22:22).
 */
function zeigerZeitText(xz: number, links: number, rechts: number, y: number, zeit: number) {
  const text = uhrzeit(zeit)
  const halb = textBreite(text) / 2 + 4
  const x = Math.min(Math.max(xz, links + halb), rechts - halb)
  return (
    <g data-audit="verlauf-zeigerschild">
      <rect x={x - halb} y={y - 11} width={2 * halb} height={15} rx={4} className="vd-schild" />
      <text x={x} y={y} textAnchor="middle" className="vd-achse vd-zeigerzeit" data-audit="verlauf-zeigerzeit">{text}</text>
    </g>
  )
}

/* ---------------------------------------------------------------------------
 * Zusammen: alle eingeschalteten Kurven, jede auf ihrer eigenen Skala
 * ------------------------------------------------------------------------- */

const ZUSAMMEN_HOEHE = ZUSAMMEN.hoehe
const PZ = ZUSAMMEN.rand

type Skaliert = { reihe: Reihe; y: (v: number) => number; d: string }

const ZusammenGrund = memo(function ZusammenGrund({ skaliert, fenster, breite, dunkel, clipId }: {
  skaliert: Skaliert[]; fenster: Fenster; breite: number; dunkel: Fenster[]; clipId: string
}) {
  const plot = breite - PZ.l - PZ.r
  const hoehe = ZUSAMMEN_HOEHE - PZ.t - PZ.b
  const x = zeitX(fenster, PZ.l, plot)
  return (
    <>
      <clipPath id={clipId}><rect x={PZ.l} y={0} width={Math.max(0, plot)} height={ZUSAMMEN_HOEHE} /></clipPath>
      <Nacht dunkel={dunkel} x={x} oben={PZ.t} hoehe={hoehe} links={PZ.l} rechts={breite - PZ.r} />
      {[0, 0.5, 1].map((a) => (
        <line key={a} x1={PZ.l} x2={breite - PZ.r} y1={PZ.t + a * hoehe} y2={PZ.t + a * hoehe} className="vd-gitter" />
      ))}
      <Zeitmarken fenster={fenster} links={PZ.l} breite={plot} gesamt={breite} y={ZUSAMMEN_HOEHE - 6} gitterOben={PZ.t} gitterUnten={PZ.t + hoehe} />
      {skaliert.map(({ reihe, d }) => (
        <path key={reihe.key} d={d} fill="none" stroke={reihe.farbe} strokeWidth={1.8} strokeLinejoin="round" strokeLinecap="round"
          clipPath={`url(#${clipId})`} data-audit={`verlauf-kurve-${reihe.key}`} />
      ))}
    </>
  )
})

export function ZusammenBild({ reihen, fenster, breite, dunkel, zeiger, gesten }: {
  reihen: Reihe[]; fenster: Fenster; breite: number; dunkel: Fenster[]; zeiger: number | null; gesten: GestenProps
}) {
  const clipId = useId().replace(/:/g, '')
  const plot = breite - PZ.l - PZ.r
  const skaliert = useMemo<Skaliert[]>(() => {
    const x = zeitX(fenster, PZ.l, plot)
    return reihen.flatMap((reihe) => {
      const stuecke = abschnitteImFenster(reihe.stuecke, fenster)
      const s = statistik(reihe.punkte, fenster)
      if (!s || stuecke.length === 0) return []
      const y = skala(s.min, s.max, PZ.t, ZUSAMMEN_HOEHE - PZ.b)
      return [{ reihe, y, d: pfad(stuecke, x, y) }]
    })
  }, [reihen, fenster, plot])

  const x = zeitX(fenster, PZ.l, plot)
  const xz = zeiger == null ? null : x(zeiger)
  return (
    <svg
      viewBox={`0 0 ${breite} ${ZUSAMMEN_HOEHE}`}
      style={{ height: ZUSAMMEN_HOEHE }}
      className="vd-bild"
      data-audit="verlauf-bild"
      data-links={PZ.l}
      data-breite={plot}
      role="img"
      aria-label={`Verlauf: ${skaliert.map((s) => s.reihe.label).join(', ') || 'keine Kurve gewählt'}`}
      {...gesten}
    >
      <ZusammenGrund skaliert={skaliert} fenster={fenster} breite={breite} dunkel={dunkel} clipId={clipId} />
      {skaliert.length === 0 && (
        <text x={breite / 2} y={ZUSAMMEN_HOEHE / 2} textAnchor="middle" className="vd-achse">Keine Kurve gewählt — oben einen Wert antippen.</text>
      )}
      {xz != null && zeiger != null && (
        <g data-audit="verlauf-zeiger">
          <line x1={xz} x2={xz} y1={PZ.t} y2={ZUSAMMEN_HOEHE - PZ.b} className="vd-zeiger" />
          {skaliert.map(({ reihe, y }) => {
            const p = amZeiger(reihe.punkte, zeiger)
            return p ? <circle key={reihe.key} cx={x(p.t)} cy={y(p.v)} r={3.5} fill={reihe.farbe} /> : null
          })}
          {zeigerZeitText(xz, PZ.l, breite - PZ.r, PZ.t + 11, zeiger)}
        </g>
      )}
    </svg>
  )
}

/* ---------------------------------------------------------------------------
 * Einzeln: eine Zeile je Messgröße, mit Zielband
 * ------------------------------------------------------------------------- */

const ZEILEN_HOEHE = ZEILE.hoehe
const PE = ZEILE.rand

function bandGrenzen(stuecke: BandStueck[]): number[] {
  return stuecke.flatMap((s) => [s.min, s.max]).filter((v): v is number => v != null)
}

const ZeilenGrund = memo(function ZeilenGrund({ reihe, fenster, breite, dunkel, band, y, d, clipId }: {
  reihe: Reihe; fenster: Fenster; breite: number; dunkel: Fenster[]; band: BandStueck[]; y: (v: number) => number; d: string; clipId: string
}) {
  const plot = breite - PE.l - PE.r
  const hoehe = ZEILEN_HOEHE - PE.t - PE.b
  const x = zeitX(fenster, PE.l, plot)
  // Beschriftet wird das Band, das am rechten Rand gilt — also gerade jetzt.
  const jetzt = band[band.length - 1]
  const marken = jetzt ? [...new Set([jetzt.max, jetzt.min])].filter((v): v is number => v != null) : []
  const ys = marken.map((v) => y(v) + 4)
  // Zwei Grenzen dicht beieinander: auseinanderschieben statt übereinander schreiben.
  if (ys.length === 2 && ys[1] - ys[0] < 11) { const mitte = (ys[0] + ys[1]) / 2; ys[0] = mitte - 5.5; ys[1] = mitte + 5.5 }
  return (
    <>
      <clipPath id={clipId}><rect x={PE.l} y={0} width={Math.max(0, plot)} height={ZEILEN_HOEHE} /></clipPath>
      <Nacht dunkel={dunkel} x={x} oben={PE.t} hoehe={hoehe} links={PE.l} rechts={PE.l + plot} />
      <Zielband stuecke={band} x={x} y={y} oben={PE.t} unten={ZEILEN_HOEHE - PE.b} links={PE.l} rechts={PE.l + plot} />
      {marken.map((v, i) => (
        <text key={i} x={breite - 2} y={Math.min(ZEILEN_HOEHE - 1, Math.max(10, ys[i]))} textAnchor="end" className="vd-achse">{grenzText(v, reihe.stellen)}</text>
      ))}
      <path d={d} fill="none" stroke={reihe.farbe} strokeWidth={2} strokeLinejoin="round" strokeLinecap="round"
        clipPath={`url(#${clipId})`} data-audit={`verlauf-kurve-${reihe.key}`} />
    </>
  )
})

export function EinzelZeile({ reihe, fenster, breite, dunkel, band, zeiger, gesten }: {
  reihe: Reihe; fenster: Fenster; breite: number; dunkel: Fenster[]; band: BandStueck[]; zeiger: number | null; gesten: GestenProps
}) {
  const clipId = useId().replace(/:/g, '')
  const plot = breite - PE.l - PE.r
  const { y, d } = useMemo(() => {
    const s = statistik(reihe.punkte, fenster)
    const werte = [...(s ? [s.min, s.max] : []), ...bandGrenzen(band)]
    const lo = werte.length ? Math.min(...werte) : 0
    const hi = werte.length ? Math.max(...werte) : 1
    const rand = (hi - lo) * 0.08
    const yy = skala(lo - rand, hi + rand, PE.t, ZEILEN_HOEHE - PE.b, 0)
    return { y: yy, d: pfad(abschnitteImFenster(reihe.stuecke, fenster), zeitX(fenster, PE.l, plot), yy) }
  }, [reihe, fenster, band, plot])

  const x = zeitX(fenster, PE.l, plot)
  const p = zeiger == null ? null : amZeiger(reihe.punkte, zeiger)
  return (
    <svg
      viewBox={`0 0 ${breite} ${ZEILEN_HOEHE}`}
      style={{ height: ZEILEN_HOEHE }}
      className="vd-bild"
      data-audit="verlauf-bild"
      data-links={PE.l}
      data-breite={plot}
      role="img"
      aria-label={`${reihe.label}: Verlauf im Ausschnitt`}
      {...gesten}
    >
      <ZeilenGrund reihe={reihe} fenster={fenster} breite={breite} dunkel={dunkel} band={band} y={y} d={d} clipId={clipId} />
      {zeiger != null && (
        <g data-audit="verlauf-zeiger">
          <line x1={x(zeiger)} x2={x(zeiger)} y1={PE.t} y2={ZEILEN_HOEHE - PE.b} className="vd-zeiger" />
          {p && <circle cx={x(p.t)} cy={y(p.v)} r={3.5} fill={reihe.farbe} />}
        </g>
      )}
    </svg>
  )
}

/** Die gemeinsame Zeitachse unter den Zeilen. */
export function ZeilenAchse({ fenster, breite }: { fenster: Fenster; breite: number }) {
  const plot = breite - PE.l - PE.r
  return (
    <svg viewBox={`0 0 ${breite} 18`} style={{ height: 18 }} className="vd-achsenzeile" aria-hidden="true">
      <Zeitmarken fenster={fenster} links={PE.l} breite={plot} gesamt={breite} y={13} />
    </svg>
  )
}

/* ---------------------------------------------------------------------------
 * Fokus: ein Wert groß, mit echter y-Achse
 * ------------------------------------------------------------------------- */

const FOKUS_HOEHE = FOKUS.hoehe
const PF = FOKUS.rand

const FokusGrund = memo(function FokusGrund({ reihe, fenster, breite, dunkel, band, teilung, d, flaechen, clipId }: {
  reihe: Reihe; fenster: Fenster; breite: number; dunkel: Fenster[]; band: BandStueck[]
  teilung: ReturnType<typeof yTeilung>; d: string; flaechen: string[]; clipId: string
}) {
  const plot = breite - PF.l - PF.r
  const unten = FOKUS_HOEHE - PF.b
  const hoehe = unten - PF.t
  const x = zeitX(fenster, PF.l, plot)
  const y = (v: number) => PF.t + (1 - (v - teilung.unten) / (teilung.oben - teilung.unten)) * hoehe
  const striche = teilung.marken.length > 6 ? teilung.marken.filter((_, i) => i % 2 === 0) : teilung.marken
  return (
    <>
      <clipPath id={clipId}><rect x={PF.l} y={0} width={Math.max(0, plot)} height={FOKUS_HOEHE} /></clipPath>
      <Nacht dunkel={dunkel} x={x} oben={PF.t} hoehe={hoehe} links={PF.l} rechts={PF.l + plot} />
      <Zielband stuecke={band} x={x} y={y} oben={PF.t} unten={unten} links={PF.l} rechts={PF.l + plot} raender />
      {striche.map((v) => (
        <g key={v}>
          <line x1={PF.l} x2={breite - PF.r} y1={y(v)} y2={y(v)} className="vd-gitter" />
          <text x={PF.l - 6} y={y(v) + 4} textAnchor="end" className="vd-achse">{grenzText(v, reihe.stellen)}</text>
        </g>
      ))}
      {reihe.unit && <text x={PF.l - 6} y={10} textAnchor="end" className="vd-achse vd-einheit">{reihe.unit}</text>}
      <Zeitmarken fenster={fenster} links={PF.l} breite={plot} gesamt={breite} y={FOKUS_HOEHE - 6} />
      {flaechen.map((f, i) => <path key={i} d={f} fill={reihe.farbe} className="vd-flaeche" clipPath={`url(#${clipId})`} />)}
      <path d={d} fill="none" stroke={reihe.farbe} strokeWidth={2.2} strokeLinejoin="round" strokeLinecap="round"
        clipPath={`url(#${clipId})`} data-audit={`verlauf-kurve-${reihe.key}`} />
    </>
  )
})

export function FokusBild({ reihe, fenster, breite, dunkel, band, zeiger, gesten }: {
  reihe: Reihe; fenster: Fenster; breite: number; dunkel: Fenster[]; band: BandStueck[]; zeiger: number | null; gesten: GestenProps
}) {
  const clipId = useId().replace(/:/g, '')
  const plot = breite - PF.l - PF.r
  const unten = FOKUS_HOEHE - PF.b
  const { teilung, d, flaechen, y } = useMemo(() => {
    const s = statistik(reihe.punkte, fenster)
    const werte = [...(s ? [s.min, s.max] : []), ...bandGrenzen(band)]
    const t = yTeilung(werte.length ? Math.min(...werte) : 0, werte.length ? Math.max(...werte) : 1)
    const yy = (v: number) => PF.t + (1 - (v - t.unten) / (t.oben - t.unten)) * (unten - PF.t)
    const x = zeitX(fenster, PF.l, plot)
    const stuecke = abschnitteImFenster(reihe.stuecke, fenster)
    const boden = yy(t.unten)
    return {
      teilung: t,
      y: yy,
      d: pfad(stuecke, x, yy),
      flaechen: stuecke.filter((st) => st.length > 1).map((st) =>
        `M${x(st[0].t).toFixed(1)} ${boden.toFixed(1)} ${st.map((p) => `L${x(p.t).toFixed(1)} ${yy(p.v).toFixed(1)}`).join(' ')} L${x(st[st.length - 1].t).toFixed(1)} ${boden.toFixed(1)} Z`),
    }
  }, [reihe, fenster, band, plot, unten])

  const x = zeitX(fenster, PF.l, plot)
  const p = zeiger == null ? null : amZeiger(reihe.punkte, zeiger)
  return (
    <svg
      viewBox={`0 0 ${breite} ${FOKUS_HOEHE}`}
      style={{ height: FOKUS_HOEHE }}
      className="vd-bild"
      data-audit="verlauf-bild"
      data-links={PF.l}
      data-breite={plot}
      role="img"
      aria-label={`${reihe.label}${reihe.unit ? ` in ${reihe.unit}` : ''}: Verlauf im Ausschnitt`}
      {...gesten}
    >
      <FokusGrund reihe={reihe} fenster={fenster} breite={breite} dunkel={dunkel} band={band} teilung={teilung} d={d} flaechen={flaechen} clipId={clipId} />
      {zeiger != null && (
        <g data-audit="verlauf-zeiger">
          <line x1={x(zeiger)} x2={x(zeiger)} y1={PF.t} y2={unten} className="vd-zeiger" />
          {p && <circle cx={x(p.t)} cy={y(p.v)} r={4.5} fill={reihe.farbe} />}
          {zeigerZeitText(x(zeiger), PF.l, breite - PF.r, PF.t - 6, zeiger)}
        </g>
      )}
    </svg>
  )
}

/* ---------------------------------------------------------------------------
 * Licht-Spur und Übersichtsleiste
 * ------------------------------------------------------------------------- */

/** Licht an (oben) und aus (unten) nach Lichtplan, ausgerichtet auf die Zeitachse darüber. */
export function LichtSpur({ fenster, licht, links, plotBreite, breite, an, aus }: {
  fenster: Fenster; licht: Fenster[]; links: number; plotBreite: number; breite: number; an: string; aus: string
}) {
  const x = zeitX(fenster, links, plotBreite)
  const yAn = 15
  const yAus = 27
  // Stufenlinie: jede Lichtphase hebt die Linie, dazwischen liegt sie unten.
  const punkte: string[] = []
  let stand = licht.length > 0 && licht[0].von <= fenster.von ? yAn : yAus
  punkte.push(`M${x(fenster.von).toFixed(1)} ${stand}`)
  for (const stueck of licht) {
    if (stueck.von > fenster.von) { punkte.push(`L${x(stueck.von).toFixed(1)} ${stand}`, `L${x(stueck.von).toFixed(1)} ${yAn}`) }
    stand = yAn
    if (stueck.bis < fenster.bis) { punkte.push(`L${x(stueck.bis).toFixed(1)} ${yAn}`, `L${x(stueck.bis).toFixed(1)} ${yAus}`); stand = yAus }
  }
  punkte.push(`L${x(fenster.bis).toFixed(1)} ${stand}`)
  // Am schmalen Telefon passt der lange Satz nicht in die Spalte neben den Zeilen.
  const lang = `Licht · an ${an} · aus ${aus}`
  const beschriftung = textBreite(lang) <= breite - links - 4 ? lang : `Licht ${an}–${aus}`
  return (
    <svg viewBox={`0 0 ${breite} 32`} style={{ height: 32 }} className="vd-spur" data-audit="verlauf-licht" role="img"
      aria-label={`Licht nach Lichtplan: an ${an} Uhr, aus ${aus} Uhr`}>
      <path d={punkte.join(' ')} fill="none" className="vd-spurlinie" strokeWidth={1.5} />
      <text x={links + 2} y={9} className="vd-achse">{beschriftung}</text>
    </svg>
  )
}

/**
 * Die ganze verfügbare Spanne als dünne Kurve, der Ausschnitt als Rahmen.
 * Ziehen oder Tippen verschiebt den Ausschnitt; mit den Pfeiltasten geht es
 * auch ohne Zeiger.
 */
export function Uebersicht({ reihe, grenzen, fenster, breite, spanneText, onMitte, onTaste }: {
  reihe: Reihe | null; grenzen: Fenster; fenster: Fenster; breite: number; spanneText: string
  onMitte: (zeit: number, amLinkenRand: boolean) => void; onTaste: (richtung: -1 | 1 | 'anfang' | 'ende') => void
}) {
  const H = 34
  const P = 4
  const spanne = Math.max(1, grenzen.bis - grenzen.von)
  const x = (t: number) => P + ((t - grenzen.von) / spanne) * (breite - 2 * P)
  const d = useMemo(() => {
    if (!reihe || reihe.punkte.length < 2) return ''
    const s = statistik(reihe.punkte, grenzen)
    if (!s) return ''
    const y = skala(s.min, s.max, 3, H - 3, 2)
    return pfad(reihe.stuecke, (t) => P + ((t - grenzen.von) / spanne) * (breite - 2 * P), y)
  }, [reihe, grenzen, spanne, breite])

  const mitteBei = (svg: SVGSVGElement, clientX: number) => {
    const box = svg.getBoundingClientRect()
    const roh = (clientX - box.left - P) / Math.max(1, box.width - 2 * P)
    const anteil = Math.min(1, Math.max(0, roh))
    onMitte(grenzen.von + anteil * spanne, roh <= 0.02)
  }
  const x0 = Math.max(P, x(fenster.von))
  const x1 = Math.min(breite - P, x(fenster.bis))
  return (
    <svg
      viewBox={`0 0 ${breite} ${H}`}
      style={{ height: H }}
      className="vd-leiste"
      data-audit="verlauf-leiste"
      role="slider"
      tabIndex={0}
      aria-label="Ausschnitt verschieben"
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={Math.round((((fenster.von + fenster.bis) / 2 - grenzen.von) / spanne) * 100)}
      aria-valuetext={spanneText}
      onPointerDown={(event) => {
        try { event.currentTarget.setPointerCapture(event.pointerId) } catch { /* ohne Capture */ }
        mitteBei(event.currentTarget, event.clientX)
      }}
      onPointerMove={(event) => { if (event.currentTarget.hasPointerCapture?.(event.pointerId)) mitteBei(event.currentTarget, event.clientX) }}
      onKeyDown={(event) => {
        const taste = { ArrowLeft: -1, ArrowRight: 1, Home: 'anfang', End: 'ende' } as const
        const richtung = taste[event.key as keyof typeof taste]
        if (richtung == null) return
        event.preventDefault()
        onTaste(richtung)
      }}
    >
      {d && <path d={d} fill="none" stroke={reihe?.farbe} strokeWidth={1} opacity={0.6} />}
      <rect x={x0} y={1} width={Math.max(6, x1 - x0)} height={H - 2} rx={4} className="vd-fenster" />
    </svg>
  )
}
