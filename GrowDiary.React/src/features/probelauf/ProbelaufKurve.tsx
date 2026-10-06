import { useState } from 'react'
import type { ProbelaufGrenzen, ProbelaufLauf } from '../../types'
import { formatNumber } from '../../utils'
import { kurvenPunkte, type KurvenGroesse, type KurvenPunkt } from './probelauf-anzeige'

/**
 * Fork AI (A-010): Eine Kurve mit den drei Abschnitten — Vorlauf, Gerät aus, Nachlauf —
 * und der Grenze, an der der Lauf abbricht.
 *
 * Eine einzige Kurve: Wer drei Größen übereinanderlegt, liest keine mehr. Die Auswahl darüber
 * wechselt die Größe.
 */

const GROESSEN: Array<{ id: KurvenGroesse; titel: string; einheit: string }> = [
  { id: 'feuchte', titel: 'Luftfeuchte', einheit: '%' },
  { id: 'temp', titel: 'Temperatur', einheit: '°C' },
  { id: 'vpd', titel: 'VPD', einheit: 'kPa' },
]

/** Die Grenzen, die zu einer Größe gehören — bei VPD ein Band. */
function grenzenFuer(g: KurvenGroesse, grenzen: ProbelaufGrenzen): Array<{ wert: number; text: string }> {
  const liste: Array<{ wert: number | null; text: string }> = g === 'feuchte'
    ? [{ wert: grenzen.feuchteMax, text: 'Grenze' }]
    : g === 'temp'
      ? [{ wert: grenzen.tempMax, text: 'Grenze' }]
      : [{ wert: grenzen.vpdMin, text: 'unten' }, { wert: grenzen.vpdMax, text: 'oben' }]
  return liste.filter((x): x is { wert: number; text: string } => x.wert != null)
}

export function ProbelaufKurve({ lauf }: { lauf: ProbelaufLauf }) {
  const [groesse, setGroesse] = useState<KurvenGroesse>('feuchte')
  if (!lauf.messreihe) return null
  const info = GROESSEN.find((x) => x.id === groesse)!
  const p = kurvenPunkte(lauf.messreihe, groesse, lauf.startUtc)
  const alle: KurvenPunkt[] = [...p.vorlauf, ...p.waehrend, ...p.nachlauf]
  const grenzen = grenzenFuer(groesse, lauf.grenzen)

  const endeMin = lauf.eingriffEndeUtc
    ? (new Date(lauf.eingriffEndeUtc).getTime() - new Date(lauf.startUtc).getTime()) / 60000
    : (new Date(lauf.geplantesEndeUtc).getTime() - new Date(lauf.startUtc).getTime()) / 60000

  const B = 640
  const H = 220
  const l = 44
  const r = 10
  const t = 10
  const b = 26
  const x0 = Math.min(-10, ...alle.map((a) => a.minute))
  const x1 = Math.max(endeMin + 10, ...alle.map((a) => a.minute))
  const ys = [...alle.map((a) => a.wert), ...grenzen.map((g) => g.wert)]
  const rohMin = ys.length ? Math.min(...ys) : 0
  const rohMax = ys.length ? Math.max(...ys) : 1
  const rand = Math.max((rohMax - rohMin) * 0.1, groesse === 'vpd' ? 0.05 : 0.5)
  const y0 = rohMin - rand
  const y1 = rohMax + rand

  const X = (m: number) => l + ((m - x0) / (x1 - x0)) * (B - l - r)
  const Y = (v: number) => t + ((y1 - v) / (y1 - y0)) * (H - t - b)
  const linie = (pts: KurvenPunkt[]) => pts.map((a) => `${X(a.minute).toFixed(1)},${Y(a.wert).toFixed(1)}`).join(' ')
  const stellen = groesse === 'vpd' ? 2 : 1

  const yMarken = [0, 1, 2, 3, 4].map((i) => y0 + ((y1 - y0) * i) / 4)
  const xMarken = [x0, 0, endeMin, x1].filter((m, i, arr) => arr.indexOf(m) === i)

  return (
    <div className="pl-karte" data-audit="probelauf-kurve">
      <h2>Verlauf im Zelt</h2>
      <div className="st-wechsel" role="group" aria-label="Größe der Kurve">
        {GROESSEN.map((x) => (
          <button key={x.id} type="button" className="st-chip" aria-current={x.id === groesse} onClick={() => setGroesse(x.id)}>{x.titel}</button>
        ))}
      </div>
      {alle.length < 2 ? (
        <p className="co-row-sub">Für {info.titel} liegen noch zu wenige Messpunkte vor.</p>
      ) : (
        <svg className="pl-kurve" viewBox={`0 0 ${B} ${H}`} role="img" aria-label={`Verlauf der ${info.titel} vor, während und nach dem Probelauf`}>
          <rect x={X(x0)} y={t} width={X(0) - X(x0)} height={H - t - b} fill="currentColor" fillOpacity=".06" />
          <rect x={X(0)} y={t} width={X(endeMin) - X(0)} height={H - t - b} fill="var(--warn, #ffc24d)" fillOpacity=".2" />
          <rect x={X(endeMin)} y={t} width={X(x1) - X(endeMin)} height={H - t - b} fill="var(--accent, #52e98c)" fillOpacity=".14" />
          {yMarken.map((v) => (
            <g key={v}>
              <line x1={l} x2={B - r} y1={Y(v)} y2={Y(v)} stroke="currentColor" strokeOpacity=".12" />
              <text x={l - 6} y={Y(v) + 4} fontSize="10" textAnchor="end" fill="currentColor" fillOpacity=".6">{formatNumber(v, stellen)}</text>
            </g>
          ))}
          {xMarken.map((m) => (
            <text key={m} x={X(m)} y={H - 8} fontSize="10" textAnchor="middle" fill="currentColor" fillOpacity=".6">{`${m > 0 ? '+' : ''}${formatNumber(m, 0)}′`}</text>
          ))}
          {grenzen.map((g) => (
            <g key={g.text}>
              <line x1={l} x2={B - r} y1={Y(g.wert)} y2={Y(g.wert)} stroke="currentColor" strokeWidth="1.5" strokeDasharray="5 4" />
              <text x={B - r - 4} y={Y(g.wert) - 5} fontSize="10" textAnchor="end" fill="currentColor">{`${g.text} ${formatNumber(g.wert, stellen)} ${info.einheit}`}</text>
            </g>
          ))}
          {[p.vorlauf, p.waehrend, p.nachlauf].map((pts, i) => (pts.length > 1 || (i > 0 && pts.length > 0)) && (
            <polyline key={i} fill="none" stroke="var(--accent-text, #52e98c)" strokeWidth="2.2" points={linie(pts)} />
          ))}
        </svg>
      )}
      <div className="pl-legende">
        <span><i style={{ background: 'currentColor', opacity: 0.2 }} />vorher (Vergleich)</span>
        <span><i style={{ background: 'var(--warn)', opacity: 0.5 }} />Gerät aus</span>
        <span><i style={{ background: 'var(--accent)', opacity: 0.4 }} />Nachlauf</span>
      </div>
    </div>
  )
}
